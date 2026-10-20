namespace CloudDrive.Core.Sync;

public static class SyncPlanner
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    public static SyncPlan Plan(IEnumerable<FileSnapshot> localFiles,
        IEnumerable<FileSnapshot> remoteFiles, IEnumerable<FileSnapshot> previousSnapshot)
    {
        var local = Map(localFiles);
        var remote = Map(remoteFiles);
        var previous = Map(previousSnapshot);
        var uploads = new List<string>();
        var downloads = new List<string>();
        var deleteLocal = new List<string>();
        var deleteRemote = new List<string>();
        var conflicts = new List<string>();
        foreach (var path in All(local, remote, previous))
        {
            local.TryGetValue(path, out var l);
            remote.TryGetValue(path, out var r);
            previous.TryGetValue(path, out var baseline);
            if (Equal(l, r)) continue;
            var lc = !Equal(l, baseline);
            var rc = !Equal(r, baseline);
            if (lc && rc) { conflicts.Add(path); continue; }
            if (lc)
            {
                if (l is null) deleteRemote.Add(path);
                else uploads.Add(path);
            }
            else if (rc)
            {
                if (r is null) deleteLocal.Add(path);
                else downloads.Add(path);
            }
        }
        return new SyncPlan(uploads, downloads, deleteLocal, deleteRemote, conflicts);
    }

    public static IReadOnlyList<FileSnapshot> NextSnapshot(
        IEnumerable<FileSnapshot> previousSnapshot, IEnumerable<FileSnapshot> localFiles,
        IEnumerable<FileSnapshot> remoteFiles, IEnumerable<SyncOperationResult> completedOperations)
    {
        var result = Map(previousSnapshot);
        var local = Map(localFiles);
        var remote = Map(remoteFiles);
        var confirmed = new HashSet<string>(Paths);
        foreach (var operation in completedOperations)
        {
            var path = Normalize(operation.Path);
            if (!confirmed.Add(path)) throw new ArgumentException($"Duplicate confirmation: {path}", nameof(completedOperations));
            if (operation.ConfirmedHash is null) result.Remove(path);
            else if (string.IsNullOrWhiteSpace(operation.ConfirmedHash))
                throw new ArgumentException($"Missing confirmed hash: {path}", nameof(completedOperations));
            else result[path] = operation.ConfirmedHash;
        }
        foreach (var path in All(local, remote, result).ToList())
        {
            if (confirmed.Contains(path)) continue;
            local.TryGetValue(path, out var l);
            remote.TryGetValue(path, out var r);
            if (l is null && r is null) result.Remove(path);
            else if (l is not null && r is not null && Equal(l, r)) result[path] = l;
        }
        return result.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FileSnapshot(kv.Key, kv.Value)).ToList();
    }

    private static IEnumerable<string> All(params Dictionary<string, string>[] maps) =>
        maps.SelectMany(m => m.Keys).Distinct(Paths).OrderBy(p => p, StringComparer.Ordinal);
    private static Dictionary<string, string> Map(IEnumerable<FileSnapshot> files)
    {
        var map = new Dictionary<string, string>(Paths);
        foreach (var f in files) map[Normalize(f.RelativePath)] = f.Hash;
        return map;
    }
    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
    private static bool Equal(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
