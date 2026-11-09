using ClassHub.Core.SharedKernel.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Core.Infrastructure.Files;

/// <summary>An uploaded document read from a multipart form: the other form fields, the uploaded name and the bytes.</summary>
public sealed record DocumentUpload(IFormCollection Form, string? FileName, byte[]? Content);

/// <summary>An opened private file: stream, type, safe name and whether browsers may show it inline.</summary>
public sealed record PrivateFile(Stream Content, string ContentType, string FileName, bool Inline);

/// <summary>
/// The HTTP side of private documents (materials, homework submissions): reading a multipart upload within the document
/// limit, and answering a download that is never sniffed, sandboxed, not cached, and an attachment unless it is an image.
/// </summary>
public static class DocumentUploads
{
    /// <summary>Upper bound for the whole upload request (the file itself is limited to <see cref="DocumentFiles.MaxBytes"/>).</summary>
    public const long RequestLimitBytes = DocumentFiles.MaxBytes + (256 * 1024);

    /// <summary>
    /// Reads the form (read manually, not by IFormFile binding: the antiforgery header filter protects it like every other
    /// unsafe request). The file part is optional when <paramref name="fileRequired"/> is false.
    /// </summary>
    public static async Task<DocumentUpload> ReadAsync(HttpContext http, bool fileRequired, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = RequestLimitBytes;
        }

        if (!http.Request.HasFormContentType)
        {
            throw new BadRequestException("Send the file as multipart/form-data.", ErrorCodes.UnsupportedMediaType);
        }

        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = RequestLimitBytes }, cancellationToken);
        }
        catch (InvalidDataException)
        {
            throw new BadRequestException("The upload is not valid multipart/form-data.", ErrorCodes.BadRequest);
        }

        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return fileRequired
                ? throw new BadRequestException("The form has no file part named 'file'.", ErrorCodes.FileNotAllowed)
                : new DocumentUpload(form, null, null);
        }

        if (file.Length > DocumentFiles.MaxBytes)
        {
            throw new BadRequestException("The file must be at most 20 MB.", ErrorCodes.PayloadTooLarge);
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        return new DocumentUpload(form, file.FileName, buffer.ToArray());
    }

    /// <summary>A private file answer: never sniffed, sandboxed, not cached, and downloaded unless it is an image.</summary>
    public static FileStreamHttpResult FileResult(HttpContext http, PrivateFile file)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(file);
        http.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        http.Response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'; img-src 'self'";
        http.Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        if (file.Inline)
        {
            http.Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue("inline") { FileNameStar = file.FileName }.ToString();
            return TypedResults.Stream(file.Content, file.ContentType);
        }

        return TypedResults.Stream(file.Content, file.ContentType, fileDownloadName: file.FileName);
    }

    /// <summary>Only images may be shown in the browser; everything else is downloaded.</summary>
    public static bool IsInline(string contentType) => contentType.StartsWith("image/", StringComparison.Ordinal);
}
