using System.Text.Json;
using ClassHub.Core.Modules.Activities.Domain;

namespace ClassHub.UnitTests.Activities;

/// <summary>R3-P2: what an activity document may be, and the copy title.</summary>
public sealed class ActivityRulesTests
{
    [Theory]
    [InlineData("drawing-board", 1, true)]
    [InlineData("geometry-board", 1, true)]
    [InlineData("drawing-board", 2, false)]
    [InlineData("chess-board", 1, false)]
    [InlineData(null, 1, false)]
    [InlineData("drawing-board", null, false)]
    public void Only_known_boards_and_their_format_versions_are_supported(string? tool, int? version, bool supported) =>
        ActivityRules.IsSupported(tool, version).ShouldBe(supported);

    [Fact]
    public void Documents_are_json_objects_within_the_limit_measured_in_utf8_bytes()
    {
        var cyrillic = JsonDocument.Parse("""{"t":"шшш"}""").RootElement; // 11 characters, 14 bytes in UTF-8
        ActivityRules.Document(cyrillic, 14).ShouldBe(("""{"t":"шшш"}""", 14));
        ActivityRules.Document(cyrillic, 13).ShouldBeNull();
        ActivityRules.Document(JsonDocument.Parse("[]").RootElement, 100).ShouldBeNull();
        ActivityRules.Document(JsonDocument.Parse("\"text\"").RootElement, 100).ShouldBeNull();
        ActivityRules.Document(null, 100).ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"elements":[{"type":"rectangle"},{"type":"text","text":"x"},{"type":"freedraw"}],"appState":{"gridModeEnabled":true}}""", true)]
    [InlineData("""{"elements":[],"files":{}}""", true)]
    [InlineData("""{"elements":{}}""", false)]
    [InlineData("""{"appState":{}}""", false)]
    [InlineData("""{"elements":[{"type":"image"}]}""", false)]
    [InlineData("""{"elements":[{"type":"iframe"}]}""", false)]
    [InlineData("""{"elements":[{"type":"embeddable"}]}""", false)]
    [InlineData("""{"elements":[1]}""", false)]
    [InlineData("""{"elements":[],"files":{"a":{}}}""", false)]
    [InlineData("""{"elements":[],"appState":[]}""", false)]
    public void Drawings_contain_only_allowed_elements_and_no_files(string json, bool valid) =>
        (ActivityRules.ContentProblem("drawing-board", 1, JsonDocument.Parse(json).RootElement) is null).ShouldBe(valid);

    [Fact]
    public void The_geometry_board_content_is_checked_with_that_board() =>
        ActivityRules.ContentProblem("geometry-board", 1, JsonDocument.Parse("""{"anything":true}""").RootElement).ShouldBeNull();

    [Fact]
    public void A_copy_title_always_fits()
    {
        ActivityRules.CopyTitle("Агли", " (копија)").ShouldBe("Агли (копија)");
        var longTitle = new string('а', Activity.TitleMaxLength);
        var copy = ActivityRules.CopyTitle(longTitle, " (копија)");
        copy.Length.ShouldBe(Activity.TitleMaxLength);
        copy.ShouldEndWith(" (копија)");
    }
}
