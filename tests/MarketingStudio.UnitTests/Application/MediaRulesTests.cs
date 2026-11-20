using FluentAssertions;
using MarketingStudio.Application.Brands;

namespace MarketingStudio.UnitTests.Application;

public class MediaRulesTests
{
    [Theory]
    [Trait("Story", "BRD-3")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }, "image/png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)] // "<svg" — scripts possible, not accepted
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, null)] // GIF
    [InlineData(new byte[0], null)]
    public void Image_type_comes_from_the_file_bytes(byte[] header, string? expected) =>
        ImageSniffer.Detect(header).Should().Be(expected);

    [Theory]
    [Trait("Story", "BRD-3")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("logo.png", "logo.png")]
    [InlineData("a\"b\u0001c.png", "abc.png")]
    [InlineData("", "image")]
    [InlineData(null, "image")]
    public void File_names_are_reduced_to_a_safe_display_name(string? input, string expected) =>
        ImageSniffer.SafeFileName(input).Should().Be(expected);

    [Fact]
    public void Very_long_file_names_keep_their_extension() =>
        ImageSniffer.SafeFileName(new string('x', 500) + ".png").Should().HaveLength(200).And.EndWith(".png");
}
