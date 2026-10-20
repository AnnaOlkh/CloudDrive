namespace CloudDrive.Core.Sync;

public record FileSnapshot(string RelativePath, string Hash);

public record SyncManifest(string LocalPath, Guid RemoteFolderId, IReadOnlyList<FileSnapshot> Files);
