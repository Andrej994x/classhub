using System.Text;
using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.SharedKernel.Errors;
using SkiaSharp;

namespace ClassHub.UnitTests.Files;

public sealed class ImageProcessorTests
{
    private static readonly ImageRules Rules = ImageRules.ProfilePhoto;

    [Fact]
    public void Formats_are_recognized_by_magic_bytes_only()
    {
        ImageProcessor.DetectFormat(TestImages.Png(10, 10)).ShouldBe("png");
        ImageProcessor.DetectFormat(TestImages.Jpeg(10, 10)).ShouldBe("jpeg");
        ImageProcessor.DetectFormat("RIFF\0\0\0\0WEBPVP8 "u8).ShouldBe("webp");
        ImageProcessor.DetectFormat("<svg xmlns='http://www.w3.org/2000/svg'/>"u8).ShouldBeNull();
        ImageProcessor.DetectFormat("GIF89a"u8).ShouldBeNull();
        ImageProcessor.DetectFormat([]).ShouldBeNull();
    }

    [Fact]
    public void Valid_images_are_re_encoded_as_webp_and_scaled_down()
    {
        var result = ImageProcessor.Process(TestImages.Png(2000, 1000), Rules);

        result.ContentType.ShouldBe("image/webp");
        ImageProcessor.DetectFormat(result.Content).ShouldBe("webp");
        (result.Width, result.Height).ShouldBe((1024, 512));
    }

    [Fact]
    public void Metadata_is_removed_and_the_orientation_applied()
    {
        // Orientation 6 = the camera was rotated: displayed as 90° clockwise.
        var upload = TestImages.JpegWithMetadata(300, 200, orientation: 6, secret: "GPS-SECRET-LOCATION");
        Encoding.ASCII.GetString(upload).ShouldContain("Exif");

        var result = ImageProcessor.Process(upload, Rules);

        var stored = Encoding.ASCII.GetString(result.Content);
        stored.ShouldNotContain("Exif");
        stored.ShouldNotContain("GPS-SECRET-LOCATION");
        stored.ShouldNotContain("EXIF");
        (result.Width, result.Height).ShouldBe((200, 300));

        // The gold top-left quadrant of the sensor image is now in the top-right corner.
        using var decoded = SKBitmap.Decode(result.Content);
        IsGold(decoded.GetPixel(decoded.Width - 10, 10)).ShouldBeTrue();
        IsGold(decoded.GetPixel(10, 10)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(100, 300)]   // too small
    [InlineData(300, 100)]
    [InlineData(6001, 200)]  // too large (header dimensions, no decoding)
    public void Images_outside_the_dimension_limits_are_rejected(int width, int height)
    {
        var error = Should.Throw<BadRequestException>(() => ImageProcessor.Process(TestImages.Png(width, height), Rules));
        error.Code.ShouldBe(ErrorCodes.ImageInvalid);
    }

    [Fact]
    public void Non_images_and_disguised_files_are_rejected()
    {
        foreach (var bytes in new[]
                 {
                     "not an image at all"u8.ToArray(),
                     "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray(),
                     // PNG signature followed by garbage
                     new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5 },
                     Array.Empty<byte>(),
                 })
        {
            Should.Throw<BadRequestException>(() => ImageProcessor.Process(bytes, Rules)).Code.ShouldBe(ErrorCodes.ImageInvalid);
        }
    }

    [Fact]
    public void Files_over_the_size_limit_are_rejected_before_decoding()
    {
        var big = new byte[Rules.MaxBytes + 1];
        TestImages.Png(200, 200).CopyTo(big, 0);
        Should.Throw<BadRequestException>(() => ImageProcessor.Process(big, Rules)).Message.ShouldContain("5 MB");
    }

    private static bool IsGold(SKColor c) => c.Red > 200 && c.Green > 170 && c.Blue < 80;
}
