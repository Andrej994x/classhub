using System.Security.Cryptography;
using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.Modules.Teachers.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Globalization;
using ClassHub.Core.SharedKernel.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClassHub.Core.Modules.Teachers.Application;

/// <summary>Profile of the current (verified) workspace. All queries are tenant-filtered.</summary>
internal sealed partial class TeacherProfileService(
    ClassHubDbContext db,
    ITenantContext tenant,
    ICatalogLookup catalog,
    IWorkspaceSettings workspaceSettings,
    IOfferLookup offers,
    ISiteStatus site,
    IPublicSiteCache siteCache,
    IFileStorage files,
    IStorageQuota storage,
    TimeProvider clock,
    ILogger<TeacherProfileService> logger) : ITeacherSummary, ITeacherPhoto, ITeacherPublicProfile, IOnboardingProgress
{
    public async Task<PublicTeacherProfile> GetPublicProfileAsync(CancellationToken cancellationToken)
    {
        var profile = await GetAsync(cancellationToken);
        return new PublicTeacherProfile(profile.FirstName, profile.LastName, profile.TeachingMode, profile.SubjectIds, profile.LevelIds,
            profile.Languages, profile.PhotoVersion);
    }

    async Task<(Stream Content, string ContentType)?> ITeacherPhoto.OpenAsync(CancellationToken cancellationToken) =>
        await OpenPhotoAsync(cancellationToken) is { } photo ? (photo.Content, photo.ContentType) : null;

    public async Task<TeacherSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var profile = await GetAsync(cancellationToken);
        return new TeacherSummary(profile.FirstName, profile.LastName, profile.PhotoVersion);
    }

    public const string PhotoPurpose = "profile-photo";

    public async Task<TeacherProfileResponse> GetAsync(CancellationToken cancellationToken)
    {
        var profile = await db.Set<TeacherProfile>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return profile is null ? Empty : await ToResponseAsync(profile, cancellationToken);
    }

    public async Task<TeacherProfileResponse> UpdateAsync(UpdateTeacherProfileRequest request, CancellationToken cancellationToken)
    {
        var subjectIds = (request.SubjectIds ?? []).Distinct().ToList();
        var levelIds = (request.LevelIds ?? []).Distinct().ToList();
        var languages = (request.Languages ?? []).Distinct(StringComparer.Ordinal).ToList();

        // Entries the ClassHub team deactivated stay valid where the profile already has them (R1-P11): only the
        // newly added ones must be active.
        var savedSubjects = await db.Set<TeacherProfileSubject>().AsNoTracking().Select(x => x.SubjectId).ToListAsync(cancellationToken);
        var savedLevels = await db.Set<TeacherProfileLevel>().AsNoTracking().Select(x => x.LevelId).ToListAsync(cancellationToken);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if ((await catalog.UnknownSubjectsAsync([.. subjectIds.Except(savedSubjects)], cancellationToken)).Count > 0)
        {
            errors["subjectIds"] = ["Unknown subject."];
        }

        if ((await catalog.UnknownLevelsAsync([.. levelIds.Except(savedLevels)], cancellationToken)).Count > 0)
        {
            errors["levelIds"] = ["Unknown education level."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await EnsureProfileAsync(now, cancellationToken);
        // The version the client loaded must still be current: otherwise the form is stale (another tab or
        // device saved in between) and replacing the fields would silently lose that save.
        // (0 and null both mean "no field save yet", e.g. a profile created only by a photo upload.)
        if ((request.Version ?? 0) != existing.Version)
        {
            throw new ConflictException("The profile was changed elsewhere. Reload it and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        var profile = existing;
        profile.Version++;
        profile.FirstName = Clean(request.FirstName);
        profile.LastName = Clean(request.LastName);
        profile.Bio = Clean(request.Bio);
        profile.TeachingMode = request.TeachingMode is null ? null : Enum.Parse<TeachingMode>(request.TeachingMode);
        profile.CountryCode = Clean(request.CountryCode);
        profile.UpdatedAtUtc = now;

        // Replace the lists: remove what is no longer chosen, add what is new.
        var currentSubjects = await db.Set<TeacherProfileSubject>().Where(x => x.ProfileId == profile.Id).ToListAsync(cancellationToken);
        db.RemoveRange(currentSubjects.Where(x => !subjectIds.Contains(x.SubjectId)));
        db.AddRange(subjectIds.Except(currentSubjects.Select(x => x.SubjectId)).Select(id => new TeacherProfileSubject { ProfileId = profile.Id, SubjectId = id }));

        var currentLevels = await db.Set<TeacherProfileLevel>().Where(x => x.ProfileId == profile.Id).ToListAsync(cancellationToken);
        db.RemoveRange(currentLevels.Where(x => !levelIds.Contains(x.LevelId)));
        db.AddRange(levelIds.Except(currentLevels.Select(x => x.LevelId)).Select(id => new TeacherProfileLevel { ProfileId = profile.Id, LevelId = id }));

        var currentLanguages = await db.Set<TeacherProfileLanguage>().Where(x => x.ProfileId == profile.Id).ToListAsync(cancellationToken);
        db.RemoveRange(currentLanguages.Where(x => !languages.Contains(x.LanguageCode)));
        db.AddRange(languages.Except(currentLanguages.Select(x => x.LanguageCode), StringComparer.Ordinal).Select(code => new TeacherProfileLanguage { ProfileId = profile.Id, LanguageCode = code }));

        // Time zone and currency: same unit of work, so profile and settings commit together or not at all.
        await workspaceSettings.ApplyAsync(request.TimeZone, request.Currency?.ToUpperInvariant(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        // The public site shows name, subjects, levels, languages and teaching mode from the profile.
        await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
        return await ToResponseAsync(profile, cancellationToken);
    }

    /// <summary>
    /// Validates, re-encodes (SkiaSharp, metadata stripped) and stores a new profile photo. The new blob is
    /// written before the database change and removed again if the change fails; the old blob is removed after
    /// the commit (best effort: an orphaned blob is harmless, a missing one is not).
    /// </summary>
    public async Task<TeacherProfileResponse> SetPhotoAsync(byte[] upload, CancellationToken cancellationToken)
    {
        var image = ImageProcessor.Process(upload, ImageRules.ProfilePhoto);
        var workspaceId = tenant.RequireWorkspaceId();
        var now = clock.GetUtcNow().UtcDateTime;
        var file = new StoredFile
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Purpose = PhotoPurpose,
            ContentType = image.ContentType,
            SizeBytes = image.Content.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(image.Content)),
            Width = image.Width,
            Height = image.Height,
            CreatedAtUtc = now,
            StorageKey = string.Empty,
        };
        file.StorageKey = $"{workspaceId:N}/{file.Id:N}.webp";

        // Before the transaction: a first profile may be created concurrently by a form save.
        await EnsureProfileAsync(now, cancellationToken);
        string? oldKey;
        try
        {
            // Inside the cleanup scope: whatever fails after the blob exists removes it again.
            await files.WriteAsync(file.StorageKey, image.Content, cancellationToken);
            // The new file row, the profile pointer and the removal of the old file row commit together.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // The plan storage counts every stored file; the new photo replaces the current one, read under the lock
            // that parallel photo uploads and material uploads share.
            await storage.EnsureRoomAsync(file.SizeBytes, async ct => await db.Set<TeacherProfile>().AsNoTracking()
                .Where(p => p.PhotoFileId != null)
                .Select(p => db.Set<StoredFile>().Where(f => f.Id == p.PhotoFileId).Select(f => (long?)f.SizeBytes).FirstOrDefault())
                .FirstOrDefaultAsync(ct) ?? 0, cancellationToken);
            db.Set<StoredFile>().Add(file);
            await db.SaveChangesAsync(cancellationToken);
            oldKey = await SwapPhotoAsync(file.Id, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(file.StorageKey, CancellationToken.None);
            throw;
        }

        await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
        await DeleteBlobAsync(oldKey);
        return await GetAsync(cancellationToken);
    }

    public async Task RemovePhotoAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<TeacherProfile>().AsNoTracking().Select(p => p.PhotoFileId).SingleOrDefaultAsync(cancellationToken) is null)
        {
            return;
        }

        string? oldKey;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            oldKey = await SwapPhotoAsync(null, clock.GetUtcNow().UtcDateTime, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
        await DeleteBlobAsync(oldKey);
    }

    /// <summary>
    /// Points the profile at <paramref name="newFileId"/> with an atomic compare-and-swap on the current photo id,
    /// then deletes the replaced file row and returns its storage key. Only the photo columns are written, so a
    /// parallel form save (guarded by <see cref="TeacherProfile.Version"/>) and a photo change never conflict; two
    /// parallel photo changes are serialized by the swap (the loser re-reads and retries).
    /// </summary>
    private async Task<string?> SwapPhotoAsync(Guid? newFileId, DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await db.Set<TeacherProfile>().AsNoTracking().Select(p => new { p.Id, p.PhotoFileId }).SingleAsync(cancellationToken);
            var swapped = await db.Set<TeacherProfile>()
                .Where(p => p.Id == current.Id && p.PhotoFileId == current.PhotoFileId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.PhotoFileId, newFileId).SetProperty(p => p.UpdatedAtUtc, now), cancellationToken);
            if (swapped != 1)
            {
                continue;
            }

            if (current.PhotoFileId is not { } oldId)
            {
                return null;
            }

            var oldKey = await db.Set<StoredFile>().Where(f => f.Id == oldId).Select(f => f.StorageKey).SingleOrDefaultAsync(cancellationToken);
            await db.Set<StoredFile>().Where(f => f.Id == oldId).ExecuteDeleteAsync(cancellationToken);
            return oldKey;
        }

        throw new ConflictException("The photo was changed at the same time. Try again.", ErrorCodes.ConcurrencyConflict);
    }

    /// <summary>The current photo bytes, or null.</summary>
    public async Task<(Stream Content, string ContentType, string ETag)?> OpenPhotoAsync(CancellationToken cancellationToken)
    {
        var photo = await (from p in db.Set<TeacherProfile>()
                           join f in db.Set<StoredFile>() on p.PhotoFileId equals f.Id
                           select f).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var stream = await files.OpenReadAsync(photo.StorageKey, cancellationToken);
        return stream is null ? null : (stream, photo.ContentType, $"\"{photo.Sha256}\"");
    }

    public async Task<OnboardingResponse> GetOnboardingAsync(CancellationToken cancellationToken)
    {
        var profile = await GetAsync(cancellationToken);
        var profileComplete = !string.IsNullOrWhiteSpace(profile.FirstName) && !string.IsNullOrWhiteSpace(profile.LastName)
            && !string.IsNullOrWhiteSpace(profile.Bio) && profile.TeachingMode is not null && profile.CountryCode is not null
            && profile.Languages.Count > 0 && profile.HasPhoto;
        var hasOffer = await offers.CountAsync(cancellationToken) > 0;
        var (siteExists, sitePublished) = await site.GetStatusAsync(cancellationToken);
        OnboardingStep[] steps =
        [
            new(OnboardingSteps.Profile, profileComplete, true),
            new(OnboardingSteps.Subjects, profile.SubjectIds.Count > 0, true),
            new(OnboardingSteps.FirstOffer, hasOffer, true),
            new(OnboardingSteps.PublicPage, siteExists, true),
            new(OnboardingSteps.Publish, sitePublished, true),
        ];
        return new OnboardingResponse(steps, steps.Count(s => s.Completed), steps.Length);
    }

    private static readonly TeacherProfileResponse Empty = new(null, null, null, null, null, [], [], [], false, null, null, null);

    /// <summary>
    /// The workspace's profile, created (and saved) if it does not exist yet. Two first requests in parallel (a photo
    /// upload and a form save) can both try to create it: the unique workspace index lets one win, and the other
    /// simply loads the winner's row instead of failing.
    /// </summary>
    private async Task<TeacherProfile> EnsureProfileAsync(DateTime now, CancellationToken cancellationToken)
    {
        var profile = await db.Set<TeacherProfile>().SingleOrDefaultAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = new TeacherProfile { Id = Guid.NewGuid(), WorkspaceId = tenant.RequireWorkspaceId(), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Set<TeacherProfile>().Add(profile);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return profile;
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            db.Entry(profile).State = EntityState.Detached;
            return await db.Set<TeacherProfile>().SingleAsync(cancellationToken);
        }
    }

    private async Task<TeacherProfileResponse> ToResponseAsync(TeacherProfile profile, CancellationToken cancellationToken)
    {
        var subjects = await db.Set<TeacherProfileSubject>().AsNoTracking().Where(s => s.ProfileId == profile.Id).Select(s => s.SubjectId).ToListAsync(cancellationToken);
        var levels = await db.Set<TeacherProfileLevel>().AsNoTracking().Where(l => l.ProfileId == profile.Id).Select(l => l.LevelId).ToListAsync(cancellationToken);
        var languages = await db.Set<TeacherProfileLanguage>().AsNoTracking().Where(l => l.ProfileId == profile.Id).Select(l => l.LanguageCode).ToListAsync(cancellationToken);
        var photoVersion = profile.PhotoFileId is { } photoId
            ? await db.Set<StoredFile>().AsNoTracking().Where(f => f.Id == photoId).Select(f => f.Sha256.Substring(0, 12)).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new TeacherProfileResponse(
            profile.FirstName, profile.LastName, profile.Bio, profile.TeachingMode?.ToString(), profile.CountryCode,
            [.. subjects.Order()], [.. levels.Order()], [.. languages.Order(StringComparer.Ordinal)],
            photoVersion is not null, photoVersion, profile.UpdatedAtUtc, profile.Version);
    }

    private async Task DeleteBlobAsync(string? key)
    {
        if (key is null)
        {
            return;
        }

        try
        {
            await files.DeleteAsync(key, CancellationToken.None);
        }
        catch (IOException ex)
        {
            LogOrphanedBlob(logger, ex);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "A replaced file could not be deleted from storage; it is orphaned.")]
    private static partial void LogOrphanedBlob(ILogger logger, Exception exception);
}

internal sealed class UpdateTeacherProfileRequestValidator : AbstractValidator<UpdateTeacherProfileRequest>
{
    public UpdateTeacherProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(TeacherProfile.NameMaxLength);
        RuleFor(x => x.LastName).MaximumLength(TeacherProfile.NameMaxLength);
        RuleFor(x => x.Bio).MaximumLength(TeacherProfile.BioMaxLength);
        RuleFor(x => x.TeachingMode)
            .Must(m => Enum.TryParse<TeachingMode>(m, ignoreCase: false, out var mode) && Enum.IsDefined(mode) && !int.TryParse(m, out _))
            .WithMessage("Teaching mode must be Online, InPerson or Hybrid.")
            .When(x => x.TeachingMode is not null);
        RuleFor(x => x.CountryCode).Must(Countries.IsIsoCode).WithMessage("Unknown country.").When(x => !string.IsNullOrWhiteSpace(x.CountryCode));
        RuleFor(x => x.TimeZone).Must(TimeZones.IsIanaId).WithMessage("Unknown time zone.").When(x => x.TimeZone is not null);
        RuleFor(x => x.Currency).Must(Currencies.IsActiveIsoCode).WithMessage("Currency must be an active ISO 4217 code.").When(x => x.Currency is not null);
        RuleFor(x => x.SubjectIds).Must(ids => ids!.Count <= TeacherProfile.MaxSubjects).WithMessage($"At most {TeacherProfile.MaxSubjects} subjects.").When(x => x.SubjectIds is not null);
        RuleFor(x => x.LevelIds).Must(ids => ids!.Count <= TeacherProfile.MaxLevels).WithMessage($"At most {TeacherProfile.MaxLevels} education levels.").When(x => x.LevelIds is not null);
        RuleFor(x => x.Languages)
            .Must(l => l!.Count <= TeacherProfile.MaxLanguages).WithMessage($"At most {TeacherProfile.MaxLanguages} languages.")
            .Must(l => l!.All(TeachingLanguages.IsKnown)).WithMessage("Unknown teaching language.")
            .When(x => x.Languages is not null);
    }
}
