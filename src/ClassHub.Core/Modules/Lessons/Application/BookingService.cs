using System.Net;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.Modules.Lessons.Persistence;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Application;

/// <summary>
/// The teacher's side of self-service booking (R2-P6): settings, and deciding on requests. Confirming re-checks for
/// conflicts under the teacher's schedule lock, makes the request a student and creates the lesson in one transaction
/// (D-23); the visitor is told by email.
/// </summary>
internal sealed class BookingService(
    ClassHubDbContext db,
    LessonQueries queries,
    ITenantContext tenant,
    IWorkspaceSettings workspace,
    IWorkspaceNames names,
    ILeadConversion leads,
    ILeadStudents leadStudents,
    LessonParticipants participants,
    IPublicSiteCache siteCache,
    IEmailOutbox outbox,
    TimeProvider clock)
{
    public async Task<BookingSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.Set<BookingSettings>().AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new BookingSettings();
        var hours = await db.Set<WorkingHours>().AsNoTracking().OrderBy(h => h.DayOfWeek).ThenBy(h => h.StartTime).ToListAsync(cancellationToken);
        var off = await db.Set<TimeOff>().AsNoTracking().OrderBy(o => o.StartDate).ToListAsync(cancellationToken);
        return new BookingSettingsResponse(settings.Enabled, settings.SlotStepMinutes, settings.MinNoticeHours, settings.HorizonDays,
            [.. hours.Select(h => new WorkingHoursItem(h.DayOfWeek, h.StartTime, h.EndTime))],
            [.. off.Select(o => new TimeOffItem(o.StartDate, o.EndDate, o.Note))],
            await workspace.GetTimeZoneAsync(cancellationToken), settings.Version);
    }

    /// <summary>Replaces the settings, working hours and days off (version-checked; the first save has version 0).</summary>
    public async Task<BookingSettingsResponse> SaveSettingsAsync(SaveBookingSettingsRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var settings = await db.Set<BookingSettings>().SingleOrDefaultAsync(cancellationToken);
        if ((settings?.Version ?? 0) != request.Version)
        {
            throw new ConflictException("The booking settings were changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        if (settings is null)
        {
            settings = new BookingSettings { WorkspaceId = tenant.RequireWorkspaceId() };
            db.Set<BookingSettings>().Add(settings);
        }

        settings.Enabled = request.Enabled;
        settings.SlotStepMinutes = request.SlotStepMinutes!.Value;
        settings.MinNoticeHours = request.MinNoticeHours!.Value;
        settings.HorizonDays = request.HorizonDays!.Value;
        settings.UpdatedAtUtc = now;
        settings.Version++;
        db.Set<WorkingHours>().RemoveRange(await db.Set<WorkingHours>().ToListAsync(cancellationToken));
        db.Set<WorkingHours>().AddRange(request.WorkingHours!.Select(h => new WorkingHours { Id = Guid.CreateVersion7(), DayOfWeek = h.DayOfWeek, StartTime = h.Start, EndTime = h.End }));
        db.Set<TimeOff>().RemoveRange(await db.Set<TimeOff>().ToListAsync(cancellationToken));
        db.Set<TimeOff>().AddRange(request.TimeOff!.Select(o => new TimeOff { Id = Guid.CreateVersion7(), StartDate = o.StartDate, EndDate = o.EndDate, Note = Clean(o.Note) }));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("The booking settings were changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        // The public page links to booking only while it is on.
        await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
        return await GetSettingsAsync(cancellationToken);
    }

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public async Task<BookingRequestListResponse> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 10_000 || pageSize is < 1 or > MaxPageSize)
        {
            throw new BadRequestException($"The page must be 1 to 10000 and the page size 1 to {MaxPageSize}.");
        }

        var requests = db.Set<BookingRequest>().AsNoTracking();
        if (status is not null)
        {
            var wanted = Enum.TryParse<BookingStatus>(status, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new BadRequestException("Unknown status.");
            requests = requests.Where(r => r.Status == wanted);
        }

        var total = await requests.CountAsync(cancellationToken);
        var list = await requests.OrderBy(r => r.Status).ThenBy(r => r.StartUtc).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var zone = await ZoneAsync(cancellationToken);
        var result = new List<BookingRequestResponse>(list.Count);
        foreach (var request in list)
        {
            result.Add(await ToResponseAsync(request, zone, cancellationToken));
        }

        return new BookingRequestListResponse(result, total, page, pageSize);
    }

    public async Task<BookingRequestResponse> GetAsync(Guid bookingId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await LoadAsync(bookingId, tracking: false, cancellationToken), await ZoneAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Confirms a pending request: the request becomes a student (D-12, idempotent), and under the schedule lock the
    /// slot is checked again and the lesson created. A conflict leaves the request pending (409 LESSON_OVERLAP).
    /// </summary>
    public async Task<BookingRequestResponse> ConfirmAsync(Guid bookingId, BookingDecisionRequest request, CancellationToken cancellationToken)
    {
        ThrowIfNot(await LoadAsync(bookingId, tracking: false, cancellationToken), BookingStatus.Pending, request.Version);
        var teacher = tenant.UserId!.Value;
        var now = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockScheduleAsync(teacher, cancellationToken);
        var booking = await LoadAsync(bookingId, tracking: true, cancellationToken);
        ThrowIfNot(booking, BookingStatus.Pending, request.Version);
        if (await queries.OverlapsAsync(teacher, booking.StartUtc, booking.EndUtc, null, cancellationToken))
        {
            throw new ConflictException("Another lesson is already scheduled at this time.", ErrorCodes.LessonOverlap);
        }

        // In this transaction: a failed confirmation (conflict, plan limit, a parallel decision) converts nothing.
        var studentId = await leadStudents.EnsureStudentAsync(booking.LeadId, cancellationToken);
        // The request may match an archived student: the same rule as any lesson (restore first) rolls everything back.
        await participants.RequireAsync(LessonKind.Individual, studentId, null, cancellationToken);
        var lesson = new Lesson
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = booking.WorkspaceId,
            TeacherUserId = teacher,
            Kind = LessonKind.Individual,
            SubjectId = booking.SubjectId,
            Title = booking.OfferTitle.Length > Lesson.TitleMaxLength ? booking.OfferTitle[..Lesson.TitleMaxLength] : booking.OfferTitle,
            StartUtc = booking.StartUtc,
            EndUtc = booking.EndUtc,
            TimeZone = await workspace.GetTimeZoneAsync(cancellationToken),
            Mode = booking.Mode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Set<Lesson>().Add(lesson);
        db.Set<LessonParticipant>().Add(new LessonParticipant { WorkspaceId = booking.WorkspaceId, LessonId = lesson.Id, StudentId = studentId });
        booking.Status = BookingStatus.Confirmed;
        booking.LessonId = lesson.Id;
        Decide(booking, null, now);
        await NotifyAsync(booking, BookingEmails.Kind.Confirmed, cancellationToken);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(booking.Id, cancellationToken);
    }

    public async Task<BookingRequestResponse> DeclineAsync(Guid bookingId, BookingDecisionRequest request, CancellationToken cancellationToken)
    {
        var booking = await LoadAsync(bookingId, tracking: true, cancellationToken);
        ThrowIfNot(booking, BookingStatus.Pending, request.Version);
        booking.Status = BookingStatus.Declined;
        Decide(booking, Clean(request.Reason), clock.GetUtcNow().UtcDateTime);
        await NotifyAsync(booking, BookingEmails.Kind.Declined, cancellationToken);
        await SaveAsync(cancellationToken);
        return await GetAsync(booking.Id, cancellationToken);
    }

    /// <summary>Moves a confirmed booking's lesson (still scheduled) to a free time, re-checked under the schedule lock.</summary>
    public async Task<BookingRequestResponse> RescheduleAsync(Guid bookingId, RescheduleBookingRequest request, CancellationToken cancellationToken)
    {
        var teacher = tenant.UserId!.Value;
        var zone = await ZoneAsync(cancellationToken);
        var startUtc = LessonTime.ToUtc(request.StartLocal!.Value, zone, "startLocal");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockScheduleAsync(teacher, cancellationToken);
        var booking = await LoadAsync(bookingId, tracking: true, cancellationToken);
        ThrowIfNot(booking, BookingStatus.Confirmed, request.Version);
        var lesson = await ScheduledLessonAsync(booking, cancellationToken);
        var endUtc = startUtc + (booking.EndUtc - booking.StartUtc);
        if (await queries.OverlapsAsync(teacher, startUtc, endUtc, lesson.Id, cancellationToken))
        {
            throw new ConflictException("Another lesson is already scheduled at this time.", ErrorCodes.LessonOverlap);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        lesson.StartUtc = booking.StartUtc = startUtc;
        lesson.EndUtc = booking.EndUtc = endUtc;
        lesson.UpdatedAtUtc = now;
        lesson.Version++;
        Decide(booking, booking.Reason, now);
        await NotifyAsync(booking, BookingEmails.Kind.Rescheduled, cancellationToken);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(booking.Id, cancellationToken);
    }

    /// <summary>Cancels a confirmed booking: its lesson is cancelled with the reason (it never counts as held).</summary>
    public async Task<BookingRequestResponse> CancelAsync(Guid bookingId, CancelBookingRequest request, CancellationToken cancellationToken)
    {
        var booking = await LoadAsync(bookingId, tracking: true, cancellationToken);
        ThrowIfNot(booking, BookingStatus.Confirmed, request.Version);
        var lesson = await ScheduledLessonAsync(booking, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var reason = request.Reason!.Trim();
        lesson.Status = LessonStatus.Cancelled;
        lesson.CancellationReason = reason;
        lesson.CancelledAtUtc = now;
        lesson.UpdatedAtUtc = now;
        lesson.Version++;
        booking.Status = BookingStatus.Cancelled;
        Decide(booking, reason, now);
        await NotifyAsync(booking, BookingEmails.Kind.Cancelled, cancellationToken);
        await SaveAsync(cancellationToken);
        return await GetAsync(booking.Id, cancellationToken);
    }

    /// <summary>
    /// After a booked lesson was changed in the lesson editor (in the caller's unit of work): its confirmed booking takes
    /// the new time or the cancellation, and the visitor is told, as if it had been done on the booking.
    /// </summary>
    public async Task OnLessonChangedAsync(Lesson lesson, CancellationToken cancellationToken)
    {
        var booking = await db.Set<BookingRequest>().SingleOrDefaultAsync(b => b.LessonId == lesson.Id && b.Status == BookingStatus.Confirmed, cancellationToken);
        if (booking is null)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (lesson.Status == LessonStatus.Cancelled)
        {
            booking.Status = BookingStatus.Cancelled;
            Decide(booking, lesson.CancellationReason, now);
            await NotifyAsync(booking, BookingEmails.Kind.Cancelled, cancellationToken);
        }
        else if (lesson.StartUtc != booking.StartUtc || lesson.EndUtc != booking.EndUtc)
        {
            booking.StartUtc = lesson.StartUtc;
            booking.EndUtc = lesson.EndUtc;
            Decide(booking, booking.Reason, now);
            await NotifyAsync(booking, BookingEmails.Kind.Rescheduled, cancellationToken);
        }
    }

    private async Task<Lesson> ScheduledLessonAsync(BookingRequest booking, CancellationToken cancellationToken)
    {
        var lesson = await db.Set<Lesson>().SingleAsync(l => l.Id == booking.LessonId, cancellationToken);
        return lesson.Status == LessonStatus.Scheduled
            ? lesson
            : throw new BusinessRuleException("The lesson was already held or cancelled.", ErrorCodes.InvalidStatusTransition);
    }

    private async Task NotifyAsync(BookingRequest booking, BookingEmails.Kind kind, CancellationToken cancellationToken)
    {
        var lead = await leads.FindAsync(booking.LeadId, cancellationToken);
        if (lead is null)
        {
            return;
        }

        var workspaceId = tenant.RequireWorkspaceId();
        var teacherName = (await names.FindAsync([workspaceId], cancellationToken))[workspaceId].Name;
        var zone = await ZoneAsync(cancellationToken);
        outbox.Enqueue(BookingEmails.Create(kind, lead.Email, await workspace.GetLocaleAsync(cancellationToken), teacherName, booking.OfferTitle,
            LessonTime.ToLocal(booking.StartUtc, zone), zone.Id, booking.Reason));
    }

    private static void ThrowIfNot(BookingRequest booking, BookingStatus status, int? version)
    {
        if (booking.Version != version)
        {
            throw new ConflictException("The request was changed elsewhere. Reload and try again.", ErrorCodes.ConcurrencyConflict);
        }

        if (booking.Status != status)
        {
            throw new BusinessRuleException($"Only a {status.ToString().ToLowerInvariant()} request can do this.", ErrorCodes.InvalidStatusTransition);
        }
    }

    private static void Decide(BookingRequest booking, string? reason, DateTime now)
    {
        booking.Reason = reason;
        booking.DecidedAtUtc ??= now;
        booking.UpdatedAtUtc = now;
        booking.Version++;
    }

    private async Task<BookingRequest> LoadAsync(Guid bookingId, bool tracking, CancellationToken cancellationToken)
    {
        var requests = tracking ? db.Set<BookingRequest>() : db.Set<BookingRequest>().AsNoTracking();
        return await requests.SingleOrDefaultAsync(r => r.Id == bookingId, cancellationToken) ?? throw new NotFoundException();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The request was changed elsewhere. Reload and try again.", ErrorCodes.ConcurrencyConflict);
        }
    }

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken cancellationToken) =>
        TimeZoneInfo.FindSystemTimeZoneById(await workspace.GetTimeZoneAsync(cancellationToken));

    private async Task<BookingRequestResponse> ToResponseAsync(BookingRequest r, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var lead = await leads.FindAsync(r.LeadId, cancellationToken);
        return new BookingRequestResponse(r.Id, r.LeadId, lead?.Name ?? string.Empty, lead?.Email ?? string.Empty, lead?.Phone, r.OfferId, r.OfferTitle,
            r.StartUtc, r.EndUtc, LessonTime.ToLocal(r.StartUtc, zone), LessonTime.ToLocal(r.EndUtc, zone), zone.Id, r.Status.ToString(), r.LessonId, r.Reason,
            r.CreatedAtUtc, r.Version);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Emails to the visitor about their booking (mk/en, the site's language). Times always name their zone.</summary>
internal static class BookingEmails
{
    public enum Kind
    {
        Confirmed,
        Declined,
        Rescheduled,
        Cancelled,
    }

    public static EmailMessage Create(Kind kind, string to, string locale, string teacher, string offer, DateTime startLocal, string zone, string? reason)
    {
        var en = locale == "en";
        var when = $"{startLocal:yyyy-MM-dd HH:mm} ({zone})";
        var (subject, intro) = (kind, en) switch
        {
            (Kind.Confirmed, true) => ($"{teacher} confirmed your lesson", $"Your lesson \"{offer}\" is confirmed for {when}."),
            (Kind.Confirmed, false) => ($"{teacher} го потврди вашиот час", $"Вашиот час „{offer}“ е потврден за {when}."),
            (Kind.Declined, true) => ($"{teacher} cannot take your requested time", $"Unfortunately the time {when} for \"{offer}\" is not possible. You can choose another one on the teacher's page."),
            (Kind.Declined, false) => ($"{teacher} не може во бараното време", $"За жал, терминот {when} за „{offer}“ не е можен. Може да изберете друг на страницата на наставникот."),
            (Kind.Rescheduled, true) => ($"{teacher} moved your lesson", $"Your lesson \"{offer}\" is now on {when}."),
            (Kind.Rescheduled, false) => ($"{teacher} го помести вашиот час", $"Вашиот час „{offer}“ сега е на {when}."),
            (Kind.Cancelled, true) => ($"{teacher} cancelled your lesson", $"Your lesson \"{offer}\" on {when} is cancelled."),
            (_, _) => ($"{teacher} го откажа вашиот час", $"Вашиот час „{offer}“ на {when} е откажан."),
        };
        var note = reason is null ? null : (en ? $"Note from the teacher: {reason}" : $"Порака од наставникот: {reason}");
        var html = $"<p>{WebUtility.HtmlEncode(intro)}</p>" + (note is null ? string.Empty : $"<p>{WebUtility.HtmlEncode(note)}</p>");
        var text = intro + "\n" + (note is null ? string.Empty : note + "\n");
        return new EmailMessage(to, subject, html, text, $"booking.{kind.ToString().ToLowerInvariant()}");
    }
}
