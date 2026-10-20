namespace CloudDrive.Core.Contracts;

public record FileDto(
    Guid Id,
    Guid FolderId,
    string Name,
    long Size,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string UploadedBy,
    string LastEditedBy,
    string ContentHash,
    int Version);

public record RemoteFileEntry(Guid Id, string RelativePath, string ContentHash, int Version);
