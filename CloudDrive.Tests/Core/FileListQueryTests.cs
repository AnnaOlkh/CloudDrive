using CloudDrive.Core.Contracts;
using CloudDrive.Core.Files;

namespace CloudDrive.Tests.Core;

/// <summary>Тести індивідуальної операції варіанту: сортування за назвою + фільтр .txt/.jpg.</summary>
public class FileListQueryTests
{
    private static FileDto F(string name) =>
        new(Guid.NewGuid(), Guid.Empty, name, 1, DateTime.UtcNow, DateTime.UtcNow, "u", "u", "h", 1);

    private static readonly FileDto[] Sample =
        { F("b.txt"), F("A.jpg"), F("c.c"), F("d.png"), F("E.TXT"), F("f.jpeg") };

    [Fact]
    public void Sort_NameAsc_IsCaseInsensitiveAlphabetical()
    {
        var result = FileListQuery.Apply(Sample, FileFilter.All, SortOrder.NameAsc);
        Assert.Equal(new[] { "A.jpg", "b.txt", "c.c", "d.png", "E.TXT", "f.jpeg" }, result.Select(f => f.Name));
    }

    [Fact]
    public void Sort_NameDesc_IsReverseOfAsc()
    {
        var asc = FileListQuery.Apply(Sample, FileFilter.All, SortOrder.NameAsc).Select(f => f.Name).ToList();
        var desc = FileListQuery.Apply(Sample, FileFilter.All, SortOrder.NameDesc).Select(f => f.Name).ToList();
        asc.Reverse();
        Assert.Equal(asc, desc);
    }

    [Fact]
    public void Filter_TxtJpg_KeepsOnlyTxtAndJpg_CaseInsensitive()
    {
        var result = FileListQuery.Apply(Sample, FileFilter.TxtJpg, SortOrder.NameAsc);
        Assert.Equal(new[] { "A.jpg", "b.txt", "E.TXT" }, result.Select(f => f.Name));
    }

    [Fact]
    public void Filter_TxtOnly_ReturnsTextFiles()
    {
        var result = FileListQuery.Apply(Sample, FileFilter.TxtOnly, SortOrder.NameAsc);
        Assert.Equal(new[] { "b.txt", "E.TXT" }, result.Select(f => f.Name));
    }

    [Fact]
    public void Filter_JpgOnly_ReturnsJpegExtensionJpg()
    {
        var result = FileListQuery.Apply(Sample, FileFilter.JpgOnly, SortOrder.NameAsc);
        Assert.Equal(new[] { "A.jpg" }, result.Select(f => f.Name));
    }

    [Fact]
    public void Filter_All_KeepsEverything()
    {
        Assert.Equal(Sample.Length, FileListQuery.Apply(Sample, FileFilter.All, SortOrder.NameAsc).Count);
    }

    [Fact]
    public void Apply_EmptyList_ReturnsEmpty()
    {
        Assert.Empty(FileListQuery.Apply(Array.Empty<FileDto>(), FileFilter.TxtJpg, SortOrder.NameDesc));
    }

    [Fact]
    public void Apply_DoesNotMutateInput()
    {
        var input = new List<FileDto> { F("z.txt"), F("a.txt") };
        FileListQuery.Apply(input, FileFilter.All, SortOrder.NameAsc);
        Assert.Equal("z.txt", input[0].Name);
    }
}
