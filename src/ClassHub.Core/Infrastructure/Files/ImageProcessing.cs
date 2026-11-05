using ClassHub.Core.SharedKernel.Errors;
using SkiaSharp;

namespace ClassHub.Core.Infrastructure.Files;

/// <summary>Result of re-encoding an uploaded image.</summary>
public sealed record ProcessedImage(byte[] Content, string ContentType, int Width, int Height);

/// <summary>Limits for an image purpose (for example the profile photo).</summary>
public sealed record ImageRules(long MaxBytes, int MinSide, int MaxInputSide, long MaxInputPixels, int OutputMaxSide, int Quality)
{
    /// <summary>Profile photo: up to 5 MB, at least 128 px, decoded at most 6000 × 6000 (36 MP), stored ≤ 1024 px.</summary>
    public static ImageRules ProfilePhoto { get; } = new(5 * 1024 * 1024, 128, 6000, 36_000_000, 1024, 85);
}

/// <summary>
/// Validates and re-encodes uploaded images with SkiaSharp. The stored file is always a new WebP encoded from
/// the decoded pixels, so metadata (EXIF, GPS, comments) and any embedded payload are dropped. The EXIF
/// orientation is applied before the metadata is discarded.
/// </summary>
public static class ImageProcessor
{
    public const string OutputContentType = "image/webp";

    /// <summary>Recognized by magic bytes, never by file name or the client's content type.</summary>
    public static string? DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "jpeg";
        }

        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "png";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "webp";
        }

        return null;
    }

    public static ProcessedImage Process(byte[] upload, ImageRules rules)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(rules);

        if (upload.Length == 0)
        {
            throw Invalid("The file is empty.");
        }

        if (upload.Length > rules.MaxBytes)
        {
            throw Invalid($"The image must be at most {rules.MaxBytes / (1024 * 1024)} MB.");
        }

        if (DetectFormat(upload) is null)
        {
            throw Invalid("Only JPEG, PNG and WebP images are accepted.");
        }

        using var data = SKData.CreateCopy(upload);
        using var codec = SKCodec.Create(data) ?? throw Invalid("The image cannot be read.");

        // Dimensions come from the header: refuse decompression bombs before allocating pixels.
        var info = codec.Info;
        if (info.Width > rules.MaxInputSide || info.Height > rules.MaxInputSide || (long)info.Width * info.Height > rules.MaxInputPixels)
        {
            throw Invalid($"The image is too large (at most {rules.MaxInputSide} × {rules.MaxInputSide} pixels).");
        }

        if (info.Width < rules.MinSide || info.Height < rules.MinSide)
        {
            throw Invalid($"The image must be at least {rules.MinSide} × {rules.MinSide} pixels.");
        }

        using var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw Invalid("The image cannot be read.");
        }

        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);
        using var resized = Resize(oriented, rules.OutputMaxSide);
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, rules.Quality) ?? throw Invalid("The image cannot be processed.");
        return new ProcessedImage(encoded.ToArray(), OutputContentType, resized.Width, resized.Height);
    }

    private static SKBitmap Resize(SKBitmap source, int maxSide)
    {
        var scale = Math.Min(1d, (double)maxSide / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        if (width == source.Width && height == source.Height)
        {
            return source.Copy();
        }

        return source.Resize(new SKImageInfo(width, height, source.ColorType, source.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw Invalid("The image cannot be processed.");
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return source.Copy();
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var target = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(target);
        var w = source.Width;
        var h = source.Height;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); canvas.Translate(-w, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180); canvas.Translate(-w, -h); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); canvas.Translate(0, -h); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.RotateDegrees(90); canvas.Translate(0, -h); break;
            case SKEncodedOrigin.RightBottom: canvas.RotateDegrees(270); canvas.Scale(1, -1); canvas.Translate(-w, -h); break;
            case SKEncodedOrigin.LeftBottom: canvas.RotateDegrees(270); canvas.Translate(-w, 0); break;
        }

        using var sourceImage = SKImage.FromBitmap(source);
        canvas.DrawImage(sourceImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return target;
    }

    private static BadRequestException Invalid(string message) => new(message, ErrorCodes.ImageInvalid);
}
