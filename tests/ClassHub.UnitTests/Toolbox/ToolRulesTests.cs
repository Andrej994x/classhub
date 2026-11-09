using ClassHub.Core.Modules.Toolbox.Domain;

namespace ClassHub.UnitTests.Toolbox;

/// <summary>R3-P1: external tools open only by official HTTPS links on approved sites.</summary>
public sealed class ToolRulesTests
{
    private static readonly string[] Allowed = ["geogebra.org", "phet.colorado.edu"];

    [Theory]
    [InlineData("https://geogebra.org/classic")]
    [InlineData("https://www.geogebra.org/classic?lang=mk")]
    [InlineData("https://phet.colorado.edu/sims/html/forces/latest/forces_en.html")]
    [InlineData("https://WWW.GeoGebra.org/")]
    public void Official_https_links_on_approved_sites_are_allowed(string url) => ToolRules.IsAllowedUrl(url, Allowed).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("geogebra.org/classic")]
    [InlineData("http://www.geogebra.org/classic")] // not HTTPS
    [InlineData("https://www.geogebra.org:8443/")] // another port
    [InlineData("https://user:secret@www.geogebra.org/")] // credentials
    [InlineData("https://evilgeogebra.org/")] // only the domain itself and its subdomains
    [InlineData("https://geogebra.org.example.com/")]
    [InlineData("https://colorado.edu/")] // a parent of an approved host is not approved
    [InlineData("https://203.0.113.5/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    public void Other_links_are_refused(string? url) => ToolRules.IsAllowedUrl(url, Allowed).ShouldBeFalse();

    [Fact]
    public void Overlong_links_are_refused() =>
        ToolRules.IsAllowedUrl("https://www.geogebra.org/" + new string('a', Tool.UrlMaxLength), Allowed).ShouldBeFalse();
}
