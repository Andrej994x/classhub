using System.IO.Compression;
using System.Text;

namespace ClassHub.Core.Infrastructure.Files;

/// <summary>A file kind on the allow-list: its content type, and whether browsers may show it inline.</summary>
public sealed record DocumentFileKind(string Extension, string ContentType, bool Inline);

/// <summary>
/// The upload rules for documents (materials R2-P8, homework submissions R2-P9; D-20 until scanning is decided with
/// hosting): an allow-list of file kinds, recognized by extension <b>and</b> by the file's own signature (the client's
/// content type is never trusted), a size limit, and a safe file name.
/// </summary>
public static class DocumentFiles
{
    public const int MaxBytes = 20 * 1024 * 1024;

    /// <summary>Longest stored file name (display and download).</summary>
    public const int FileNameMaxLength = 120;

    private static readonly DocumentFileKind Pdf = new(".pdf", "application/pdf", Inline: false);
    private static readonly DocumentFileKind Png = new(".png", "image/png", Inline: true);
    private static readonly DocumentFileKind Jpeg = new(".jpg", "image/jpeg", Inline: true);
    private static readonly DocumentFileKind Webp = new(".webp", "image/webp", Inline: true);
    private static readonly DocumentFileKind Docx = new(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Inline: false);
    private static readonly DocumentFileKind Xlsx = new(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Inline: false);
    private static readonly DocumentFileKind Pptx = new(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", Inline: false);
    private static readonly DocumentFileKind Text = new(".txt", "text/plain; charset=utf-8", Inline: false);

    /// <summary>The allowed extensions, for messages and the upload form.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".pdf", ".png", ".jpg", ".jpeg", ".webp", ".docx", ".xlsx", ".pptx", ".txt"];

    /// <summary>The kind of an upload, or null when the name and the bytes do not match an allowed kind.</summary>
    public static DocumentFileKind? Detect(string fileName, ReadOnlySpan<byte> bytes)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" when bytes.StartsWith("%PDF-"u8) => Pdf,
            ".png" when bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) => Png,
            ".jpg" or ".jpeg" when bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) => Jpeg,
            ".webp" when bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) => Webp,
            ".docx" when IsOfficePackage(bytes, "word/document.xml") => Docx,
            ".xlsx" when IsOfficePackage(bytes, "xl/workbook.xml") => Xlsx,
            ".pptx" when IsOfficePackage(bytes, "ppt/presentation.xml") => Pptx,
            ".txt" when IsPlainText(bytes) => Text,
            _ => null,
        };
    }

    /// <summary>
    /// A safe display and download name: only the last path part, without control and reserved characters, trimmed of
    /// dots and spaces, at most <see cref="FileNameMaxLength"/> characters, keeping the extension.
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

        var maxStem = FileNameMaxLength - extension.Length;
        return (stem.Length > maxStem ? stem[..maxStem] : stem) + extension;
    }

    /// <summary>
    /// Office Open XML: a readable ZIP whose central directory lists <c>[Content_Types].xml</c> and the kind's main part
    /// (wherever they are in the archive; OPC does not fix the entry order). Only the directory is read, nothing is
    /// extracted.
    /// </summary>
    private static bool IsOfficePackage(ReadOnlySpan<byte> bytes, string mainPart)
    {
        if (!bytes.StartsWith("PK\u0003\u0004"u8))
        {
            return false;
        }

        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes.ToArray(), writable: false), ZipArchiveMode.Read);
            var names = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return names.Contains("[Content_Types].xml") && names.Contains(mainPart);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

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
