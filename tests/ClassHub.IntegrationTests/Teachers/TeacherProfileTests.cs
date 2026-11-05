using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using ClassHub.UnitTests.Files;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Teachers;

/// <summary>R1-P1: teacher profile, profile photo, catalog and the derived onboarding checklist.</summary>
[Collection(ApiCollection.Name)]
public sealed class TeacherProfileTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_catalog_lists_seeded_subjects_levels_and_languages_in_both_languages()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        var catalog = await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog");

        catalog.Categories.ShouldContain(c => c.Code == "languages" && c.NameMk == "Јазици" && c.NameEn == "Languages");
        catalog.Subjects.ShouldContain(s => s.Code == "math" && s.NameMk == "Математика");
        catalog.Subjects.ShouldAllBe(s => catalog.Categories.Any(c => c.Id == s.CategoryId));
        catalog.Levels.Select(l => l.Code).ShouldContain("secondary");
        catalog.Languages.Select(l => l.Code).ShouldContain("mk");
        catalog.Countries.ShouldContain("MK");
        catalog.Countries.Count.ShouldBe(249);
    }

    [Fact]
    public async Task The_catalog_requires_a_session()
    {
        using var anonymous = await ApiClient.CreateAsync(fixture);
        using var response = await anonymous.GetAsync("/api/catalog");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_new_teacher_has_an_empty_profile_and_two_open_onboarding_steps()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        var profile = await teacher.Client.GetJsonAsync<TeacherProfileResponse>(ProfilePath(teacher.WorkspaceId));
        profile.FirstName.ShouldBeNull();
        profile.HasPhoto.ShouldBeFalse();
        profile.SubjectIds.ShouldBeEmpty();

        var onboarding = await teacher.Client.GetJsonAsync<OnboardingResponse>($"/api/w/{teacher.WorkspaceId}/onboarding");
        onboarding.Steps.Select(s => s.Key).ShouldBe([OnboardingSteps.Profile, OnboardingSteps.Subjects, OnboardingSteps.FirstOffer, OnboardingSteps.PublicPage, OnboardingSteps.Publish]);
        onboarding.Steps.Where(s => s.Available).Select(s => s.Key).ShouldBe([OnboardingSteps.Profile, OnboardingSteps.Subjects]);
        onboarding.Completed.ShouldBe(0);
    }

    [Fact]
    public async Task The_teacher_completes_the_profile_and_the_checklist_follows_the_data()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var catalog = await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog");
        var math = catalog.Subjects.Single(s => s.Code == "math").Id;
        var physics = catalog.Subjects.Single(s => s.Code == "physics").Id;
        var secondary = catalog.Levels.Single(l => l.Code == "secondary").Id;

        var saved = await PutProfileAsync(teacher, FullProfile([math, physics], [secondary]));
        saved.FirstName.ShouldBe("Ана");
        saved.TeachingMode.ShouldBe("Hybrid");
        saved.SubjectIds.Order().ShouldBe(new[] { math, physics }.Order());
        saved.Languages.ShouldBe(["en", "mk"]);

        // Without a photo the profile step is still open; subjects are done.
        var onboarding = await Onboarding(teacher);
        Step(onboarding, OnboardingSteps.Profile).Completed.ShouldBeFalse();
        Step(onboarding, OnboardingSteps.Subjects).Completed.ShouldBeTrue();

        using (var upload = await UploadPhotoAsync(teacher, TestImages.Jpeg(800, 600)))
        {
            upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        onboarding = await Onboarding(teacher);
        Step(onboarding, OnboardingSteps.Profile).Completed.ShouldBeTrue();
        onboarding.Completed.ShouldBe(2);

        // Removing all subjects re-opens that step: the checklist is derived, never ticked.
        await PutProfileAsync(teacher, FullProfile([], [secondary]));
        Step(await Onboarding(teacher), OnboardingSteps.Subjects).Completed.ShouldBeFalse();
    }

    [Fact]
    public async Task Time_zone_and_currency_are_saved_with_the_profile_in_one_audited_transaction()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        var saved = await PutProfileAsync(teacher, new { firstName = "Ана", timeZone = "Europe/Berlin", currency = "eur" });

        saved.FirstName.ShouldBe("Ана");
        var workspace = await teacher.Client.GetJsonAsync<ClassHub.Core.Modules.Workspaces.Contracts.WorkspaceResponse>($"/api/w/{teacher.WorkspaceId}");
        (workspace.TimeZone, workspace.Currency).ShouldBe(("Europe/Berlin", "EUR"));
        await using var db = fixture.CreateDbContext();
        (await db.AuditLog.CountAsync(a => a.Action == "workspace.settings.updated" && a.WorkspaceId == teacher.WorkspaceId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_stale_form_cannot_overwrite_a_newer_save()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var path = ProfilePath(teacher.WorkspaceId);
        var first = await PutProfileAsync(teacher, new { firstName = "Ана" });
        var loadedVersion = first.Version;

        // Tab A saves on top of the loaded version ...
        using (var tabA = await teacher.Client.PutAsync(path, new { firstName = "Ана Марија", version = loadedVersion }))
        {
            tabA.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // ... then tab B, still on the old version, is refused and changes nothing.
        using (var tabB = await teacher.Client.PutAsync(path, new { firstName = "Стара", version = loadedVersion }))
        {
            tabB.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await tabB.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
        }

        using (var noVersion = await teacher.Client.PutAsync(path, new { firstName = "Без верзија" }))
        {
            noVersion.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        (await teacher.Client.GetJsonAsync<TeacherProfileResponse>(path)).FirstName.ShouldBe("Ана Марија");
    }

    [Fact]
    public async Task A_photo_upload_does_not_make_an_open_form_stale()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var path = ProfilePath(teacher.WorkspaceId);
        var loaded = await teacher.Client.GetJsonAsync<TeacherProfileResponse>(path); // no profile yet: version null

        (await UploadPhotoAsync(teacher, TestImages.Png(300, 300))).Dispose();

        using var save = await teacher.Client.PutAsync(path, new { firstName = "Ана", version = loaded.Version });
        save.StatusCode.ShouldBe(HttpStatusCode.OK, await save.Content.ReadAsStringAsync(Ct));
        var saved = await save.Content.ReadFromJsonAsync<TeacherProfileResponse>(Ct);
        saved!.HasPhoto.ShouldBeTrue();
        saved.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData("timeZone", "Mars/Olympus")]
    [InlineData("currency", "ABC")]
    public async Task Invalid_settings_reject_the_whole_profile_change(string field, string value)
    {
        using var teacher = await fixture.CreateTeacherAsync();

        using var response = await teacher.Client.PutAsync(ProfilePath(teacher.WorkspaceId), new Dictionary<string, object> { ["firstName"] = "Ана", [field] = value });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
        (await teacher.Client.GetJsonAsync<TeacherProfileResponse>(ProfilePath(teacher.WorkspaceId))).FirstName.ShouldBeNull();
    }

    [Theory]
    [InlineData("teachingMode", "Telepathy")]
    [InlineData("teachingMode", "1")]
    [InlineData("countryCode", "XX")]
    [InlineData("countryCode", "mk")]
    public async Task Invalid_profile_values_are_rejected(string field, string value)
    {
        using var teacher = await fixture.CreateTeacherAsync();

        using var response = await teacher.Client.PutAsync(ProfilePath(teacher.WorkspaceId), new Dictionary<string, object> { [field] = value });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_catalog_references_languages_and_oversized_text_are_rejected()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var path = ProfilePath(teacher.WorkspaceId);

        foreach (var (body, field) in new (object Body, string Field)[]
                 {
                     (new { subjectIds = new[] { Guid.NewGuid() } }, "subjectIds"),
                     (new { levelIds = new[] { Guid.NewGuid() } }, "levelIds"),
                     (new { languages = new[] { "xx" } }, "languages"),
                     (new { bio = new string('a', 1001) }, "bio"),
                     (new { firstName = new string('a', 101) }, "firstName"),
                 })
        {
            using var response = await teacher.Client.PutAsync(path, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, field);
            (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }
    }

    [Fact]
    public async Task The_photo_is_stored_re_encoded_without_metadata_and_served_privately()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var upload = TestImages.JpegWithMetadata(400, 300, orientation: 6, secret: "GPS-SECRET-LOCATION");

        using (var response = await UploadPhotoAsync(teacher, upload))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var profile = await response.Content.ReadFromJsonAsync<TeacherProfileResponse>(Ct);
            profile!.HasPhoto.ShouldBeTrue();
            profile.PhotoVersion.ShouldNotBeNullOrEmpty();
        }

        using var photo = await teacher.Client.GetAsync($"{ProfilePath(teacher.WorkspaceId)}/photo");
        photo.StatusCode.ShouldBe(HttpStatusCode.OK);
        photo.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        // Never publicly cacheable: "private" normally, "no-store" when the response also renews the session cookie.
        var cache = photo.Headers.CacheControl.ShouldNotBeNull();
        cache.Public.ShouldBeFalse();
        (cache.Private || cache.NoStore).ShouldBeTrue();
        var bytes = await photo.Content.ReadAsByteArrayAsync(Ct);
        ImageProcessor.DetectFormat(bytes).ShouldBe("webp");
        Encoding.ASCII.GetString(bytes).ShouldNotContain("GPS-SECRET-LOCATION");
        Encoding.ASCII.GetString(bytes).ShouldNotContain("Exif");

        await using var db = fixture.CreateDbContext();
        var stored = await db.Set<StoredFile>().IgnoreQueryFilters().SingleAsync(f => f.WorkspaceId == teacher.WorkspaceId, Ct);
        (stored.Width, stored.Height).ShouldBe((300, 400)); // orientation applied
        stored.ContentType.ShouldBe("image/webp");
        File.Exists(Path.Combine(fixture.Factory.FileRoot, stored.StorageKey)).ShouldBeTrue();
        stored.StorageKey.ShouldStartWith(teacher.WorkspaceId.ToString("N"));
    }

    [Fact]
    public async Task Replacing_and_removing_the_photo_cleans_up_the_old_file()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        (await UploadPhotoAsync(teacher, TestImages.Png(300, 300))).Dispose();
        var first = await StoredFileAsync(teacher.WorkspaceId);

        (await UploadPhotoAsync(teacher, TestImages.Png(500, 500))).Dispose();
        var second = await StoredFileAsync(teacher.WorkspaceId);
        second.Id.ShouldNotBe(first.Id);
        File.Exists(Path.Combine(fixture.Factory.FileRoot, first.StorageKey)).ShouldBeFalse();

        using (var removed = await teacher.Client.DeleteAsync($"{ProfilePath(teacher.WorkspaceId)}/photo"))
        {
            removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using var db = fixture.CreateDbContext();
        (await db.Set<StoredFile>().IgnoreQueryFilters().CountAsync(f => f.WorkspaceId == teacher.WorkspaceId, Ct)).ShouldBe(0);
        File.Exists(Path.Combine(fixture.Factory.FileRoot, second.StorageKey)).ShouldBeFalse();
        using var photo = await teacher.Client.GetAsync($"{ProfilePath(teacher.WorkspaceId)}/photo");
        photo.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Disguised_tiny_and_oversized_photos_are_rejected_and_nothing_is_stored()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var oversized = new byte[(5 * 1024 * 1024) + 1];
        TestImages.Png(200, 200).CopyTo(oversized, 0);

        foreach (var (bytes, name) in new[]
                 {
                     ("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray(), "photo.png"),
                     ("GIF89a......"u8.ToArray(), "photo.gif"),
                     (TestImages.Png(64, 64), "small.png"),
                     (oversized, "big.png"),
                 })
        {
            using var response = await UploadPhotoAsync(teacher, bytes, name, "image/png");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
            (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.ImageInvalid, name);
        }

        await using var db = fixture.CreateDbContext();
        (await db.Set<StoredFile>().IgnoreQueryFilters().CountAsync(f => f.WorkspaceId == teacher.WorkspaceId, Ct)).ShouldBe(0);
        Directory.Exists(Path.Combine(fixture.Factory.FileRoot, teacher.WorkspaceId.ToString("N"))).ShouldBeFalse();
    }

    [Fact]
    public async Task A_photo_upload_needs_multipart_and_the_antiforgery_header()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var path = $"{ProfilePath(teacher.WorkspaceId)}/photo";

        using (var json = await teacher.Client.PutAsync(path, new { file = "x" }))
        {
            json.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
            (await json.ErrorCodeAsync()).ShouldBe(ErrorCodes.UnsupportedMediaType);
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative)) { Content = PhotoContent(TestImages.Png(300, 300), "a.png", "image/png") };
        using var noToken = await teacher.Client.Http.SendAsync(request, Ct);
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noToken.ErrorCodeAsync()).ShouldBe(ErrorCodes.AntiforgeryInvalid);
    }

    [Fact]
    public async Task Another_teacher_cannot_read_or_change_the_profile_or_photo()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        await PutProfileAsync(owner, new { firstName = "Owner" });
        (await UploadPhotoAsync(owner, TestImages.Png(300, 300))).Dispose();
        var path = ProfilePath(owner.WorkspaceId);

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, path);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, path, new { firstName = "Hijacked" });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, $"{path}/photo");
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Delete, $"{path}/photo");
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, $"/api/w/{owner.WorkspaceId}/onboarding");
        using (var upload = await intruder.Client.SendContentAsync(HttpMethod.Put, $"{path}/photo", PhotoContent(TestImages.Png(300, 300), "x.png", "image/png")))
        {
            upload.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await owner.Client.GetJsonAsync<TeacherProfileResponse>(path)).FirstName.ShouldBe("Owner");
        (await owner.Client.GetJsonAsync<TeacherProfileResponse>(path)).HasPhoto.ShouldBeTrue();
        // The intruder's own profile is untouched by all of this.
        (await intruder.Client.GetJsonAsync<TeacherProfileResponse>(ProfilePath(intruder.WorkspaceId))).FirstName.ShouldBeNull();
    }

    private static string ProfilePath(Guid workspaceId) => $"/api/w/{workspaceId}/profile";

    private static object FullProfile(Guid[] subjects, Guid[] levels) => new
    {
        firstName = "Ана",
        lastName = "Петровска",
        bio = "Наставничка по математика и физика со 10 години искуство.",
        teachingMode = "Hybrid",
        countryCode = "MK",
        subjectIds = subjects,
        levelIds = levels,
        languages = new[] { "mk", "en", "mk" },
    };

    /// <summary>Saves like the SPA does: with the version of the profile as currently stored.</summary>
    private static async Task<TeacherProfileResponse> PutProfileAsync(TeacherSession teacher, object body)
    {
        var current = await teacher.Client.GetJsonAsync<TeacherProfileResponse>(ProfilePath(teacher.WorkspaceId));
        var withVersion = System.Text.Json.JsonSerializer.SerializeToNode(body, System.Text.Json.JsonSerializerOptions.Web)!.AsObject();
        withVersion["version"] = current.Version;
        using var response = await teacher.Client.PutAsync(ProfilePath(teacher.WorkspaceId), withVersion);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TeacherProfileResponse>(Ct))!;
    }

    private static Task<OnboardingResponse> Onboarding(TeacherSession teacher) =>
        teacher.Client.GetJsonAsync<OnboardingResponse>($"/api/w/{teacher.WorkspaceId}/onboarding");

    private static OnboardingStep Step(OnboardingResponse onboarding, string key) => onboarding.Steps.Single(s => s.Key == key);

    private static Task<HttpResponseMessage> UploadPhotoAsync(TeacherSession teacher, byte[] bytes, string name = "photo.jpg", string contentType = "image/jpeg") =>
        teacher.Client.SendContentAsync(HttpMethod.Put, $"{ProfilePath(teacher.WorkspaceId)}/photo", PhotoContent(bytes, name, contentType));

    private static MultipartFormDataContent PhotoContent(byte[] bytes, string name, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    private async Task<StoredFile> StoredFileAsync(Guid workspaceId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<StoredFile>().IgnoreQueryFilters().SingleAsync(f => f.WorkspaceId == workspaceId, Ct);
    }
}
