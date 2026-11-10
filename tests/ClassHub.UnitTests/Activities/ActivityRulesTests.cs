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

    [Theory]
    [InlineData("""{"format":"classhub.geometry","version":1,"view":{"boundingBox":[-8,6,8,-6],"axis":true,"grid":false},"objects":[{"id":"A","type":"point","x":0,"y":0},{"id":"B","type":"point","x":3,"y":0},{"id":"C","type":"point","x":0,"y":4},{"id":"t1","type":"triangle","points":["A","B","C"]},{"id":"a1","type":"angle","points":["B","A","C"]},{"id":"c1","type":"circle","center":"A","through":"B"},{"id":"s1","type":"segment","points":["A","B"]},{"id":"l1","type":"line","points":["B","C"]}]}""", true)]
    [InlineData("""{"format":"classhub.geometry","objects":[]}""", true)]
    [InlineData("""{"objects":[]}""", false)]
    [InlineData("""{"format":"classhub.geometry"}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[{"id":"A","type":"point","x":"1","y":0}]}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[{"id":"A","type":"point","x":1e7,"y":0}]}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[{"id":"A","type":"point","x":1,"y":0},{"id":"A","type":"point","x":2,"y":0}]}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[{"id":"A","type":"point","x":1,"y":0},{"id":"s1","type":"segment","points":["A","A"]}]}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[{"id":"s1","type":"segment","points":["A","B"]},{"id":"A","type":"point","x":1,"y":0},{"id":"B","type":"point","x":2,"y":0}]}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[{"id":"A","type":"point","x":1,"y":0},{"id":"B","type":"point","x":2,"y":0},{"id":"c1","type":"circle","center":"A","through":"A"}]}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[{"id":"A","type":"point","x":1,"y":0},{"id":"f1","type":"function","term":"x^2"}]}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[{"id":"<script>","type":"point","x":1,"y":0}]}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[],"view":{"boundingBox":[1,2,3]}}""", false)]
    [InlineData("""{"format":"classhub.geometry","objects":[],"view":{"axis":"yes"}}""", false)]
    public void Constructions_hold_known_objects_on_named_points_and_a_valid_view(string json, bool valid) =>
        (ActivityRules.ContentProblem("geometry-board", 1, JsonDocument.Parse(json).RootElement) is null).ShouldBe(valid);

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
