using System.Text;
using ClassHub.Core.Modules.Materials.Domain;

namespace ClassHub.UnitTests.Files;

/// <summary>R2-P8: the material upload allow-list (extension and signature) and the safe file name.</summary>
public sealed class MaterialFilesTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8];
    private static readonly byte[] OfficeZip = [.. "PK\u0003\u0004"u8, 0x14, 0x00, .. "[Content_Types].xml"u8];
    private static readonly byte[] PlainZip = [.. "PK\u0003\u0004"u8, 0x14, 0x00, .. "payload.bin"u8];

    [Theory]
    [InlineData("a.pdf", "application/pdf", false)]
    [InlineData("A.PDF", "application/pdf", false)]
    [InlineData("a.png", "image/png", true)]
    [InlineData("a.jpg", "image/jpeg", true)]
    [InlineData("a.jpeg", "image/jpeg", true)]
    [InlineData("a.webp", "image/webp", true)]
    [InlineData("a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    [InlineData("a.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false)]
    [InlineData("a.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", false)]
    [InlineData("a.txt", "text/plain; charset=utf-8", false)]
    public void Allowed_kinds_are_recognized_by_extension_and_signature(string name, string contentType, bool inline)
    {
        var bytes = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".pdf" => Pdf,
            ".png" => Png,
            ".jpg" or ".jpeg" => Jpeg,
            ".webp" => Webp,
            ".txt" => Encoding.UTF8.GetBytes("Здраво, свету!"),
            _ => OfficeZip,
        };

        var kind = MaterialFiles.Detect(name, bytes).ShouldNotBeNull();

        kind.ContentType.ShouldBe(contentType);
        kind.Inline.ShouldBe(inline);
    }

    [Fact]
    public void Mismatching_or_unknown_files_are_not_allowed()
    {
        MaterialFiles.Detect("a.pdf", Png).ShouldBeNull();
        MaterialFiles.Detect("a.png", Pdf).ShouldBeNull();
        MaterialFiles.Detect("a.jpg", Png).ShouldBeNull();
        MaterialFiles.Detect("a.webp", [.. "RIFF"u8, 0, 0, 0, 0, .. "WAVE"u8]).ShouldBeNull();
        MaterialFiles.Detect("a.webp", "RIFF"u8).ShouldBeNull(); // too short for the WEBP tag
        MaterialFiles.Detect("a.docx", PlainZip).ShouldBeNull(); // a ZIP, but not Office Open XML
        MaterialFiles.Detect("a.txt", [0x41, 0x00, 0x42]).ShouldBeNull(); // NUL: binary
        MaterialFiles.Detect("a.txt", [0xC3, 0x28]).ShouldBeNull(); // invalid UTF-8
        MaterialFiles.Detect("a.exe", [0x4D, 0x5A]).ShouldBeNull();
        MaterialFiles.Detect("a.html", "<html>"u8).ShouldBeNull();
        MaterialFiles.Detect("a.svg", "<svg>"u8).ShouldBeNull();
        MaterialFiles.Detect("pdf", Pdf).ShouldBeNull();
        MaterialFiles.Detect("a.pdf", []).ShouldBeNull();
    }

    [Theory]
    [InlineData("Равенки.pdf", ".pdf", "Равенки.pdf")]
    [InlineData("C:\\Users\\ana\\Desktop\\test.pdf", ".pdf", "test.pdf")]
    [InlineData("../../etc/passwd.pdf", ".pdf", "passwd.pdf")]
    [InlineData("a<b>c:d\"e|f?g*h.pdf", ".pdf", "a_b_c_d_e_f_g_h.pdf")]
    [InlineData("tab\tand\u0007bell.txt", ".txt", "tab_and_bell.txt")]
    [InlineData("  ...hidden... .pdf", ".pdf", "hidden.pdf")]
    [InlineData("photo.JPEG", ".jpg", "photo.jpg")] // the kind's own extension, never the uploaded one
    [InlineData("noext", ".pdf", "noext.pdf")]
    [InlineData("", ".pdf", "file.pdf")]
    [InlineData(null, ".pdf", "file.pdf")]
    [InlineData("....pdf", ".pdf", "file.pdf")]
    public void Names_are_made_safe(string? uploaded, string extension, string expected) =>
        MaterialFiles.SafeName(uploaded, extension).ShouldBe(expected);

    [Fact]
    public void Long_names_are_cut_to_the_limit_keeping_the_extension()
    {
        var name = MaterialFiles.SafeName(new string('а', 300) + ".docx", ".docx");

        name.Length.ShouldBe(Material.FileNameMaxLength);
        name.ShouldEndWith(".docx");
    }
}
