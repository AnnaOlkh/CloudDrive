namespace CloudDrive.Core.Contracts;

public record FolderDto(Guid Id, string Name, Guid? ParentId);
public record CreateFolderRequest(string Name, Guid? ParentId);
