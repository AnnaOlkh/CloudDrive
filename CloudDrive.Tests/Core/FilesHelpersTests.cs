using CloudDrive.Core.Files;

namespace CloudDrive.Tests.Core;

public class FileTypeHelperTests
{
    [Theory]
    [InlineData("main.c", PreviewKind.Text)]
    [InlineData("MAIN.C", PreviewKind.Text)]
    [InlineData("photo.jpg", PreviewKind.Image)]
    [InlineData("photo.JPG", PreviewKind.Image)]
    [InlineData("notes.txt", PreviewKind.None)]
    [InlineData("main.cpp", PreviewKind.None)]
    [InlineData("noextension", PreviewKind.None)]
    public void GetPreviewKind_ByExtension(string name, PreviewKind expected) =>
        Assert.Equal(expected, FileTypeHelper.GetPreviewKind(name));

    [Fact]
    public void GetPreviewContentType_ReturnsNullWhenUnsupported() =>
        Assert.Null(FileTypeHelper.GetPreviewContentType("a.txt"));
}

public class FileNameValidatorTests
{
    [Theory]
    [InlineData("report.txt", true)]
    [InlineData("мій файл.c", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a/b.txt", false)]
    [InlineData("a\\b.txt", false)]
    [InlineData("what?.txt", false)]
    [InlineData("trailing.", false)]
    [InlineData("CON", false)]
    [InlineData("nul.txt", false)]
    [InlineData("LPT1.jpg", false)]
    [InlineData("report.CON", true)]
    public void IsValid(string name, bool expected) => Assert.Equal(expected, FileNameValidator.IsValid(name));
}

public class ContentHasherTests
{
    [Fact]
    public void Compute_Abc_MatchesKnownSha256() =>
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ContentHasher.Compute("abc"u8.ToArray()));

    [Fact]
    public async Task ComputeAsync_MatchesCompute()
    {
        var data = "hello world"u8.ToArray();
        await using var ms = new MemoryStream(data);
        Assert.Equal(ContentHasher.Compute(data), await ContentHasher.ComputeAsync(ms));
    }
}
