using CloudDrive.Core.Contracts;

namespace CloudDrive.Web.Models;

public sealed class FolderNode
{
    public FolderNode(Guid id, Guid? parentId, string name, string fullPath)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
        FullPath = fullPath;
    }

    public Guid Id { get; }
    public Guid? ParentId { get; }
    public string Name { get; }

    public string FullPath { get; }

    public List<FolderNode> Children { get; } = new();
    public bool IsExpanded { get; set; }
    public bool IsRoot => ParentId is null;
}

public enum FileColumn { Name, Size, Created, Modified, UploadedBy, EditedBy }

public sealed record NodeContext(FolderNode Node, double X, double Y);
public sealed record FileContext(FileDto File, double X, double Y);
public sealed record MenuState(int X, int Y);

public enum PreviewStatus { None, Loading, Text, Image, Unsupported, Failed }

public sealed record PreviewState(PreviewStatus Status, string? Text = null, string? ImageUrl = null, string? Message = null)
{
    public static readonly PreviewState None = new(PreviewStatus.None);
    public static readonly PreviewState Loading = new(PreviewStatus.Loading);
    public static readonly PreviewState Unsupported = new(PreviewStatus.Unsupported);
    public static PreviewState FromText(string text) => new(PreviewStatus.Text, Text: text);
    public static PreviewState FromImage(string url) => new(PreviewStatus.Image, ImageUrl: url);
    public static PreviewState Failed(string message) => new(PreviewStatus.Failed, Message: message);
}

public sealed record PickedFolder(int Id, string Name);

public sealed record LocalFileInfo(string Path, string Hash, long Size);

public sealed class ColumnPrefs
{
    public bool Size { get; set; } = true;
    public bool Created { get; set; } = true;
    public bool Modified { get; set; } = true;
    public bool UploadedBy { get; set; } = true;
    public bool EditedBy { get; set; } = true;
}
