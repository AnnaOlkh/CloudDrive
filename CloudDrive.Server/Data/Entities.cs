namespace CloudDrive.Server.Data;

public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public class Folder
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public Folder? Parent { get; set; }
}

public class FileEntry
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid FolderId { get; set; }
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string StoragePath { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    public Guid UploadedById { get; set; }
    public Guid LastEditedById { get; set; }
    public User UploadedBy { get; set; } = null!;
    public User LastEditedBy { get; set; } = null!;

    public string ContentHash { get; set; } = "";
    public int Version { get; set; }

    public Folder Folder { get; set; } = null!;
}
