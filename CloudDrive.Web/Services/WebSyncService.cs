using System.Text.Json;
using CloudDrive.Client;
using CloudDrive.Core.Contracts;
using CloudDrive.Core.Files;
using CloudDrive.Core.Sync;
using CloudDrive.Web.Models;

namespace CloudDrive.Web.Services;

public sealed class WebSyncService(CloudDriveApiClient api, BrowserInterop browser)
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    public async Task<SyncExecutionResult> RunAsync(
        PickedFolder folder,
        Guid remoteFolderId,
        Func<SyncPlan, Task<IReadOnlyCollection<string>>>? approveDeletions,
        CancellationToken ct = default)
    {
        var previous = await LoadManifestAsync(folder, remoteFolderId);

        var scanned = await browser.ScanFolderAsync(folder.Id);
        foreach (var file in scanned)
            if (file.Path.Split('/').Any(part => !FileNameValidator.IsValid(part)))
                throw new IOException($"Unsupported local file name: {file.Path}");

        var local = scanned.Select(f => new FileSnapshot(f.Path, f.Hash)).ToList();
        var remote = await api.GetSnapshotAsync(remoteFolderId, ct);
        var plan = SyncPlanner.Plan(local, remote.Select(f => new FileSnapshot(f.RelativePath, f.ContentHash)), previous.Files);
        var remoteMap = remote.ToDictionary(x => Normalize(x.RelativePath), Paths);

        var approved = approveDeletions is null
            ? new HashSet<string>(Paths)
            : new HashSet<string>(await approveDeletions(plan), Paths);

        var completed = new List<SyncOperationResult>();
        var failures = new Dictionary<string, string>(Paths);

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
                var created = existing ?? await api.CreateFolderAsync(part, parent, ct);
                folders[created.Id] = created;
                directories[prefix] = created.Id;
                parent = created.Id;
            }
            return parent;
        }

        foreach (var path in plan.ToUpload)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                SafeRelativePath(path);
                var bytes = await browser.ReadFileAsync(folder.Id, path);
                await using var stream = new MemoryStream(bytes, writable: false);
                var name = LastSegment(path);

                FileDto result;
                if (remoteMap.TryGetValue(Normalize(path), out var current))
                    result = await api.ReplaceAsync(current.Id, name, stream, current.Version, ct);
                else
                    result = await api.UploadAsync(name, stream, await FolderForAsync(path), ct);

                var actual = await browser.FileHashAsync(folder.Id, path);
                if (!Same(actual, result.ContentHash))
                    throw new IOException($"Local file changed while uploading: {path}");

                completed.Add(new SyncOperationResult(path, result.ContentHash));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures[path] = ex.Message; }
        }

        foreach (var path in plan.ToDownload)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                SafeRelativePath(path);
                var entry = remoteMap[Normalize(path)];

                using var buffer = new MemoryStream();
                await api.DownloadAsync(entry.Id, buffer, ct);
                var data = buffer.ToArray();
                if (!Same(ContentHasher.Compute(data), entry.ContentHash))
                    throw new IOException($"Remote file changed while downloading: {path}");

                // Планувальник дозволив перезапис лише щодо знімка, який він бачив: перевіряємо ще раз.
                var old = local.FirstOrDefault(x => Paths.Equals(Normalize(x.RelativePath), Normalize(path)));
                var currentHash = await browser.FileHashAsync(folder.Id, path);
                if (currentHash is not null)
                {
                    if (old is null || !Same(currentHash, old.Hash))
                        throw new IOException($"Local file changed while downloading: {path}");
                }
                else if (old is not null)
                    throw new IOException($"Local file was deleted while downloading: {path}");

                await browser.WriteFileAsync(folder.Id, path, data);
                completed.Add(new SyncOperationResult(path, entry.ContentHash));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures[path] = ex.Message; }
        }

        foreach (var path in plan.ToDeleteLocal.Where(approved.Contains))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                SafeRelativePath(path);
                var baseline = previous.Files.First(f => Paths.Equals(Normalize(f.RelativePath), Normalize(path)));
                var hash = await browser.FileHashAsync(folder.Id, path)
                           ?? throw new IOException($"Local file disappeared while preparing deletion: {path}");
                if (!Same(hash, baseline.Hash))
                    throw new IOException($"Local file changed after sync scan: {path}");

                if ((await api.GetSnapshotAsync(remoteFolderId, ct)).Any(x => Paths.Equals(Normalize(x.RelativePath), Normalize(path))))
                    throw new IOException($"Remote file reappeared while synchronizing: {path}");

                await browser.DeleteFileAsync(folder.Id, path);
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
                SafeRelativePath(path);
                var entry = remoteMap[Normalize(path)];
                var current = (await api.GetSnapshotAsync(remoteFolderId, ct))
                    .FirstOrDefault(x => Paths.Equals(Normalize(x.RelativePath), Normalize(path)));
                if (current is null || current.Id != entry.Id || current.Version != entry.Version ||
                    !Same(current.ContentHash, entry.ContentHash))
                    throw new IOException($"Remote file changed after sync scan: {path}");
                if (await browser.FileExistsAsync(folder.Id, path))
                    throw new IOException($"Local file reappeared while synchronizing: {path}");

                await api.DeleteVersionAsync(entry.Id, entry.Version, ct);
                completed.Add(new SyncOperationResult(path, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures[path] = ex.Message; }
        }

        // Manifest просувається лише за підтвердженими операціями.
        var next = SyncPlanner.NextSnapshot(previous.Files, local,
            remote.Select(f => new FileSnapshot(f.RelativePath, f.ContentHash)), completed);
        await SaveManifestAsync(folder, new SyncManifest(folder.Name, remoteFolderId, next));

        return new SyncExecutionResult(plan, completed, failures);
    }


    private static string ManifestKey(PickedFolder folder, Guid remoteFolderId) =>
        $"clouddrive.sync.{remoteFolderId:N}.{folder.Name}";

    private async Task<SyncManifest> LoadManifestAsync(PickedFolder folder, Guid remoteFolderId)
    {
        var json = await browser.StorageGetAsync(ManifestKey(folder, remoteFolderId));
        if (string.IsNullOrEmpty(json))
            return new SyncManifest(folder.Name, remoteFolderId, Array.Empty<FileSnapshot>());

        SyncManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<SyncManifest>(json); }
        catch (JsonException ex) { throw new InvalidDataException("Stored sync state is damaged. Clear this site's data and try again.", ex); }

        if (manifest is null || manifest.RemoteFolderId != remoteFolderId)
            throw new InvalidDataException("Stored sync state does not match the selected folders.");
        return manifest;
    }

    private async Task SaveManifestAsync(PickedFolder folder, SyncManifest manifest) =>
        await browser.StorageSetAsync(ManifestKey(folder, manifest.RemoteFolderId), JsonSerializer.Serialize(manifest));


    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string LastSegment(string path)
    {
        var normalized = Normalize(path);
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void SafeRelativePath(string path)
    {
        var parts = Normalize(path).Split('/');
        if (parts.Length == 0 || parts.Any(part => !FileNameValidator.IsValid(part)))
            throw new IOException($"Unsafe relative path: {path}");
    }
}
