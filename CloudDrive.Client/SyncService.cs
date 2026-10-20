using System.Text.Json;
using CloudDrive.Core.Contracts;
using CloudDrive.Core.Files;
using CloudDrive.Core.Sync;

namespace CloudDrive.Client;

public record SyncExecutionResult(
    SyncPlan Plan,
    IReadOnlyList<SyncOperationResult> Completed,
    IReadOnlyDictionary<string, string> Failures);

public sealed class SyncService(CloudDriveApiClient api, SyncManifestStore manifests)
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    public async Task<SyncExecutionResult> RunAsync(
        string localDirectory, Guid remoteFolderId,
        CancellationToken ct = default) => await RunAsync(
            localDirectory, remoteFolderId, null, ct);

    public async Task<SyncExecutionResult> RunAsync(string localDirectory, 
        Guid remoteFolderId,
        Func<SyncPlan, Task<IReadOnlyCollection<string>>>? approveDeletions,
        CancellationToken ct = default)
    {
        var root = Path.GetFullPath(localDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        var previous = await manifests.LoadAsync(root, remoteFolderId, ct);
        var local = await LocalFileScanner.ScanAsync(root, ct);
        var remote = await api.GetSnapshotAsync(remoteFolderId, ct);
        var plan = SyncPlanner.Plan(local, remote.Select(f => new FileSnapshot(f.RelativePath, f.ContentHash)), previous.Files);
        var remoteMap = remote.ToDictionary(x => Normalize(x.RelativePath), Paths);
        var approved = approveDeletions is null
            ? new HashSet<string>(Paths)
            : new HashSet<string>(await approveDeletions(plan), Paths);
        var completed = new List<SyncOperationResult>();
        var failures = new Dictionary<string, string>(Paths);

        // Folder lookup is lazy; create missing folders only for upload paths.
        var folders = (await api.GetFoldersAsync(ct)).ToDictionary(f => f.Id);
        var directories = new Dictionary<string, Guid>(Paths) { [""] = remoteFolderId };

        async Task<Guid> FolderForAsync(string relative)
        {
            var parts = Normalize(relative).Split('/');
            var parent = remoteFolderId;
            var prefix = "";
            foreach (var part in parts.SkipLast(1))
            {
                prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                if (directories.TryGetValue(prefix, out var cached)) { parent = cached; continue; }
                var existing = folders.Values.FirstOrDefault(f => f.ParentId == parent &&
                    string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase));
                var folder = existing ?? await api.CreateFolderAsync(part, parent, ct);
                folders[folder.Id] = folder;
                directories[prefix] = folder.Id;
                parent = folder.Id;
            }
            return parent;
        }

        foreach (var path in plan.ToUpload)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var full = SafeLocalPath(root, path);
                await using var stream = File.OpenRead(full);
                FileDto result;
                if (remoteMap.TryGetValue(Normalize(path), out var current))
                    result = await api.ReplaceAsync(current.Id, Path.GetFileName(full), stream, current.Version, ct);
                else
                    result = await api.UploadAsync(Path.GetFileName(full), stream, await FolderForAsync(path), ct);
                await using var checkStream = File.OpenRead(full);
                var actual = await ContentHasher.ComputeAsync(checkStream, ct);
                if (!string.Equals(actual, result.ContentHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Local file changed while uploading: {path}");
                completed.Add(new SyncOperationResult(path, result.ContentHash));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures[path] = ex.Message; }
        }

        foreach (var path in plan.ToDownload)
        {
            ct.ThrowIfCancellationRequested();
            string? temp = null;
            try
            {
                var entry = remoteMap[Normalize(path)];
                var target = SafeLocalPath(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                temp = target + ".sync-" + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    81920, useAsync: true))
                    await api.DownloadAsync(entry.Id, output, ct);
                await using (var input = File.OpenRead(temp))
                {
                    var hash = await ContentHasher.ComputeAsync(input, ct);
                    if (!string.Equals(hash, entry.ContentHash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"Remote file changed while downloading: {path}");
                }
                var old = local.FirstOrDefault(x => string.Equals(Normalize(x.RelativePath), Normalize(path), StringComparison.OrdinalIgnoreCase));
                if (File.Exists(target))
                {
                    await using var source = File.OpenRead(target);
                    var currentHash = await ContentHasher.ComputeAsync(source, ct);
                    if (old is null || !string.Equals(currentHash, old.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"Local file changed while downloading: {path}");
                }
                else if (old is not null)
                    throw new IOException($"Local file was deleted while downloading: {path}");
                File.Move(temp, target, overwrite: true);
                temp = null;
                completed.Add(new SyncOperationResult(path, entry.ContentHash));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures[path] = ex.Message; }
            finally { if (temp is not null && File.Exists(temp)) File.Delete(temp); }
        }

        foreach (var path in plan.ToDeleteLocal.Where(approved.Contains))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var target = SafeLocalPath(root, path);
                var baseline = previous.Files.First(f => Paths.Equals(Normalize(f.RelativePath), Normalize(path)));
                if (!File.Exists(target))
                    throw new IOException($"Local file disappeared while preparing deletion: {path}");
                await using (var input = File.OpenRead(target))
                {
                    var hash = await ContentHasher.ComputeAsync(input, ct);
                    if (!string.Equals(hash, baseline.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"Local file changed after sync scan: {path}");
                }
                // Refuse deletion if the remote file has returned in the meantime.
                if ((await api.GetSnapshotAsync(remoteFolderId, ct))
                    .Any(x => Paths.Equals(Normalize(x.RelativePath), Normalize(path))))
                    throw new IOException($"Remote file reappeared while synchronizing: {path}");
                File.Delete(target);
                completed.Add(new SyncOperationResult(path, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures[path] = ex.Message; }
        }
        foreach (var path in plan.ToDeleteRemote.Where(approved.Contains))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var entry = remoteMap[Normalize(path)];
                var current = (await api.GetSnapshotAsync(remoteFolderId, ct))
                    .FirstOrDefault(x => Paths.Equals(Normalize(x.RelativePath), Normalize(path)));
                if (current is null || current.Id != entry.Id || current.Version != entry.Version ||
                    !string.Equals(current.ContentHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Remote file changed after sync scan: {path}");
                if (File.Exists(SafeLocalPath(root, path)))
                    throw new IOException($"Local file reappeared while synchronizing: {path}");
                await api.DeleteVersionAsync(entry.Id, entry.Version, ct);
                completed.Add(new SyncOperationResult(path, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures[path] = ex.Message; }
        }

        var next = SyncPlanner.NextSnapshot(previous.Files, local,
            remote.Select(f => new FileSnapshot(f.RelativePath, f.ContentHash)), completed);
        await manifests.SaveAsync(new SyncManifest(root, remoteFolderId, next), ct);
        return new SyncExecutionResult(plan, completed, failures);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string SafeLocalPath(string root, string path)
    {
        var parts = Normalize(path).Split('/');
        if (parts.Length == 0 || parts.Any(part => !FileNameValidator.IsValid(part)))
            throw new IOException($"Unsafe remote relative path: {path}");
        var full = Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var relative = Path.GetRelativePath(root, full);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new IOException($"Path escapes the local folder: {path}");
        return full;
    }
}

public static class LocalFileScanner
{
    public static async Task<IReadOnlyList<FileSnapshot>> ScanAsync(string root, CancellationToken ct = default)
    {
        var files = new List<FileSnapshot>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Split('/').Any(part => !FileNameValidator.IsValid(part)))
                throw new IOException($"Unsupported local file name: {relative}");
            await using var stream = File.OpenRead(file);
            files.Add(new FileSnapshot(relative, await ContentHasher.ComputeAsync(stream, ct)));
        }
        return files;
    }
}

public sealed class SyncManifestStore(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudDrive", "sync");

    private string FilePath(string root, Guid remoteFolderId)
    {
        var identity = System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant() + "|" + remoteFolderId);
        return Path.Combine(_directory, 
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(identity)) + ".json");
    }

    public async Task<SyncManifest> LoadAsync(string root, Guid folderId, CancellationToken ct = default)
    {
        var file = FilePath(root, folderId);
        if (!File.Exists(file)) return new SyncManifest(root, folderId, Array.Empty<FileSnapshot>());
        await using var stream = File.OpenRead(file);
        var manifest = await JsonSerializer.DeserializeAsync<SyncManifest>(stream, cancellationToken: ct);
        if (manifest is null || manifest.RemoteFolderId != folderId ||
            !string.Equals(Path.GetFullPath(manifest.LocalPath), 
            Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Manifest does not match the selected sync pair.");
        return manifest;
    }

    public async Task SaveAsync(SyncManifest manifest, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        var target = FilePath(manifest.LocalPath, manifest.RemoteFolderId);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: ct);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
