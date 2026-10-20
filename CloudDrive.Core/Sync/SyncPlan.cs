namespace CloudDrive.Core.Sync;

public record SyncPlan(
    IReadOnlyList<string> ToUpload,
    IReadOnlyList<string> ToDownload,
    IReadOnlyList<string> ToDeleteLocal,
    IReadOnlyList<string> ToDeleteRemote,
    IReadOnlyList<string> Conflicts)
{
    public bool IsEmpty => ToUpload.Count == 0 && ToDownload.Count == 0 &&
        ToDeleteLocal.Count == 0 && ToDeleteRemote.Count == 0 && Conflicts.Count == 0;
}
