using ClassHub.Core.Infrastructure.Files;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ClassHub.IntegrationTests.Teachers;

/// <summary>The local file store never reads or writes outside its root (no Docker needed).</summary>
public sealed class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "classhub-storage-tests", Guid.NewGuid().ToString("N"));

    private LocalDiskFileStorage Storage() =>
        new(Options.Create(new FileStorageOptions { RootPath = "uploads" }), new Environment(_contentRoot));

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"ws\file.webp")]
    [InlineData("ws//file.webp")]
    [InlineData(".hidden/file")]
    public void Keys_that_would_leave_the_root_are_refused(string key) =>
        Should.Throw<ArgumentException>(() => Storage().Resolve(key));

    [Fact]
    public async Task Files_round_trip_and_are_never_overwritten()
    {
        var storage = Storage();
        var ct = TestContext.Current.CancellationToken;
        await storage.WriteAsync("ws/file.webp", "first"u8.ToArray(), ct);

        await Should.ThrowAsync<IOException>(() => storage.WriteAsync("ws/file.webp", "second"u8.ToArray(), ct));
        await using (var stream = (await storage.OpenReadAsync("ws/file.webp", ct)).ShouldNotBeNull())
        {
            using var reader = new StreamReader(stream);
            (await reader.ReadToEndAsync(ct)).ShouldBe("first");
        }

        await storage.DeleteAsync("ws/file.webp", ct);
        (await storage.OpenReadAsync("ws/file.webp", ct)).ShouldBeNull();
        File.Exists(Path.Combine(_contentRoot, "uploads", "ws", "file.webp")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_cancelled_write_leaves_no_partial_file()
    {
        var storage = Storage();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => storage.WriteAsync("ws/partial.webp", new byte[1024 * 1024], cancelled.Token));

        File.Exists(Path.Combine(_contentRoot, "uploads", "ws", "partial.webp")).ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private sealed class Environment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "ClassHub.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
