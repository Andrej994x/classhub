using System.Text;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Materials.Domain;

/// <summary>
/// A teaching material (R2-P8): a stored file with a title, an optional description and subject. Private by default:
/// only the teacher and the students it is assigned to (directly or through a group) can open it.
/// </summary>
public sealed class Material : ITenantOwned, IArchivable
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 1000;
    public const int FileNameMaxLength = 120;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public Guid? SubjectId { get; set; }

    /// <summary>The stored file (<c>StoredFiles</c>, purpose <see cref="MaterialFiles.Purpose"/>).</summary>
    public Guid FileId { get; set; }

    /// <summary>The uploaded name, made safe (no path, no control or reserved characters).</summary>
    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>A material given to one student or one group (exactly one of the two).</summary>
public sealed class MaterialAssignment : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid MaterialId { get; set; }

    public Guid? StudentId { get; set; }

    public Guid? GroupId { get; set; }

    public DateTime AssignedAtUtc { get; set; }
}

/// <summary>A file kind on the allow-list: its content type, and whether browsers may show it inline.</summary>
public sealed record MaterialFileKind(string Extension, string ContentType, bool Inline);

/// <summary>
/// The upload rules for materials (R2-P8, D-20 until scanning is decided with hosting): an allow-list of file kinds,
/// recognized by extension <b>and</b> by the file's own signature (the client's content type is never trusted), a size
/// limit, and a safe file name.
/// </summary>
public static class MaterialFiles
{
    public const string Purpose = "material";
    public const int MaxBytes = 20 * 1024 * 1024;

    private static readonly MaterialFileKind Pdf = new(".pdf", "application/pdf", Inline: false);
    private static readonly MaterialFileKind Png = new(".png", "image/png", Inline: true);
    private static readonly MaterialFileKind Jpeg = new(".jpg", "image/jpeg", Inline: true);
    private static readonly MaterialFileKind Webp = new(".webp", "image/webp", Inline: true);
    private static readonly MaterialFileKind Docx = new(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Inline: false);
    private static readonly MaterialFileKind Xlsx = new(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Inline: false);
    private static readonly MaterialFileKind Pptx = new(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", Inline: false);
    private static readonly MaterialFileKind Text = new(".txt", "text/plain; charset=utf-8", Inline: false);

    /// <summary>The allowed extensions, for messages and the upload form.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".pdf", ".png", ".jpg", ".jpeg", ".webp", ".docx", ".xlsx", ".pptx", ".txt"];

    /// <summary>The kind of an upload, or null when the name and the bytes do not match an allowed kind.</summary>
    public static MaterialFileKind? Detect(string fileName, ReadOnlySpan<byte> bytes)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" when bytes.StartsWith("%PDF-"u8) => Pdf,
            ".png" when bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) => Png,
            ".jpg" or ".jpeg" when bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) => Jpeg,
            ".webp" when bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) => Webp,
            ".docx" when IsOfficeZip(bytes) => Docx,
            ".xlsx" when IsOfficeZip(bytes) => Xlsx,
            ".pptx" when IsOfficeZip(bytes) => Pptx,
            ".txt" when IsPlainText(bytes) => Text,
            _ => null,
        };
    }

    /// <summary>
    /// A safe display and download name: only the last path part, without control and reserved characters, trimmed of
    /// dots and spaces, at most <see cref="Material.FileNameMaxLength"/> characters, keeping the extension.
    /// </summary>
    public static string SafeName(string? uploaded, string extension)
    {
        var name = (uploaded ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            builder.Append(char.IsControl(c) || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal) ? '_' : c);
        }

        var stem = Path.GetFileNameWithoutExtension(builder.ToString()).Trim().Trim('.').Trim();
        if (stem.Length == 0)
        {
            stem = "file";
        }

        var maxStem = Material.FileNameMaxLength - extension.Length;
        return (stem.Length > maxStem ? stem[..maxStem] : stem) + extension;
    }

    /// <summary>Office Open XML is a ZIP whose first entry is <c>[Content_Types].xml</c> (or that names it early).</summary>
    private static bool IsOfficeZip(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith("PK\u0003\u0004"u8) && bytes[..Math.Min(bytes.Length, 4096)].IndexOf("[Content_Types].xml"u8) >= 0;

    /// <summary>Valid UTF-8 without NUL characters.</summary>
    private static bool IsPlainText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
