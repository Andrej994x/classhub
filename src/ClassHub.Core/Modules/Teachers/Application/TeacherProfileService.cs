using System.Security.Cryptography;
using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
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
    IFileStorage files,
    TimeProvider clock,
    ILogger<TeacherProfileService> logger)
{
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

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if ((await catalog.UnknownSubjectsAsync(subjectIds, cancellationToken)).Count > 0)
        {
            errors["subjectIds"] = ["Unknown subject."];
        }

        if ((await catalog.UnknownLevelsAsync(levelIds, cancellationToken)).Count > 0)
        {
            errors["levelIds"] = ["Unknown education level."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.Set<TeacherProfile>().SingleOrDefaultAsync(cancellationToken);
        // The version the client loaded must still be current: otherwise the form is stale (another tab or
        // device saved in between) and replacing the fields would silently lose that save.
        // (0 and null both mean "no field save yet", e.g. a profile created only by a photo upload.)
        if ((request.Version ?? 0) != (existing?.Version ?? 0))
        {
            throw new ConflictException("The profile was changed elsewhere. Reload it and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        var profile = existing ?? await LoadOrCreateAsync(now, cancellationToken);
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

        string? oldKey;
        try
        {
            // Inside the cleanup scope: whatever fails after the blob exists removes it again.
            await files.WriteAsync(file.StorageKey, image.Content, cancellationToken);
            // The new file, the profile pointer and the removal of the old file row commit together.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var profile = await LoadOrCreateAsync(now, cancellationToken);
            var old = profile.PhotoFileId is { } oldId ? await db.Set<StoredFile>().SingleOrDefaultAsync(f => f.Id == oldId, cancellationToken) : null;
            db.Set<StoredFile>().Add(file);
            profile.PhotoFileId = file.Id;
            profile.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            if (old is not null)
            {
                db.Set<StoredFile>().Remove(old);
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            oldKey = old?.StorageKey;
        }
        catch
        {
            await files.DeleteAsync(file.StorageKey, CancellationToken.None);
            throw;
        }

        await DeleteBlobAsync(oldKey);
        return await GetAsync(cancellationToken);
    }

    public async Task RemovePhotoAsync(CancellationToken cancellationToken)
    {
        var profile = await db.Set<TeacherProfile>().SingleOrDefaultAsync(cancellationToken);
        if (profile?.PhotoFileId is not { } photoId)
        {
            return;
        }

        var file = await db.Set<StoredFile>().SingleOrDefaultAsync(f => f.Id == photoId, cancellationToken);
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            profile.PhotoFileId = null;
            profile.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            if (file is not null)
            {
                db.Set<StoredFile>().Remove(file);
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await DeleteBlobAsync(file?.StorageKey);
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
        // Offers (R1-P2) and the public site (R1-P3/P4) do not exist yet: their steps are shown, not yet available.
        OnboardingStep[] steps =
        [
            new(OnboardingSteps.Profile, profileComplete, true),
            new(OnboardingSteps.Subjects, profile.SubjectIds.Count > 0, true),
            new(OnboardingSteps.FirstOffer, false, false),
            new(OnboardingSteps.PublicPage, false, false),
            new(OnboardingSteps.Publish, false, false),
        ];
        return new OnboardingResponse(steps, steps.Count(s => s.Completed), steps.Length);
    }

    private static readonly TeacherProfileResponse Empty = new(null, null, null, null, null, [], [], [], false, null, null, null);

    private async Task<TeacherProfile> LoadOrCreateAsync(DateTime now, CancellationToken cancellationToken)
    {
        var profile = await db.Set<TeacherProfile>().SingleOrDefaultAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = new TeacherProfile { Id = Guid.NewGuid(), WorkspaceId = tenant.RequireWorkspaceId(), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Set<TeacherProfile>().Add(profile);
        return profile;
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
