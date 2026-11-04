using ClassHub.Api.Platform;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ClassHub.UnitTests.Platform;

public sealed class SensitiveDataRedactionEnricherTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("newPassword")]
    [InlineData("ResetToken")]
    [InlineData("token")]
    [InlineData("ClientSecret")]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("SecurityStamp")]
    [InlineData("RecoveryCode")]
    [InlineData("ConnectionString")]
    public void Secret_properties_are_redacted(string propertyName)
    {
        var events = Capture(logger => logger.Information("Value {" + propertyName + "}", "s3cr3t-value"));

        var rendered = events.Single().RenderMessage();
        rendered.ShouldNotContain("s3cr3t-value");
        rendered.ShouldContain(SensitiveDataRedactionEnricher.Redacted);
    }

    [Fact]
    public void Cancellation_tokens_and_ordinary_properties_are_kept()
    {
        SensitiveDataRedactionEnricher.IsSensitive("CancellationToken").ShouldBeFalse();
        SensitiveDataRedactionEnricher.IsSensitive("WorkspaceId").ShouldBeFalse();

        var events = Capture(logger => logger.Information("Workspace {WorkspaceId}", "ws-42"));

        events.Single().RenderMessage().ShouldContain("ws-42");
    }

    [Fact]
    public void Email_addresses_are_masked()
    {
        var events = Capture(logger => logger.Information("Login for {Email}", "ana.petrovska@example.mk"));

        var rendered = events.Single().RenderMessage();
        rendered.ShouldNotContain("ana.petrovska");
        rendered.ShouldContain("a***@example.mk");
    }

    [Fact]
    public void Nested_objects_and_dictionaries_are_redacted()
    {
        var events = Capture(logger => logger.Information(
            "Request {@Body} {@Headers}",
            new { UserName = "ana", Password = "pw-123", Nested = new { AccessToken = "tok-456", Email = "b@x.com" } },
            new Dictionary<string, string> { ["Authorization"] = "Bearer abc", ["Accept"] = "application/json" }));

        var rendered = events.Single().RenderMessage();
        rendered.ShouldNotContain("pw-123");
        rendered.ShouldNotContain("tok-456");
        rendered.ShouldNotContain("Bearer abc");
        rendered.ShouldNotContain("b@x.com");
        rendered.ShouldContain("ana");
        rendered.ShouldContain("application/json");
    }

    [Theory]
    [InlineData("ana@example.com", "a***@example.com")]
    [InlineData("x@y.z", "x***@y.z")]
    [InlineData("not-an-email", "***")]
    [InlineData("@leading", "***")]
    public void MaskEmail_keeps_only_the_first_character_and_domain(string input, string expected)
    {
        SensitiveDataRedactionEnricher.MaskEmail(input).ShouldBe(expected);
    }

    private static List<LogEvent> Capture(Action<ILogger> log)
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();
        log(logger);
        return sink.Events;
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
