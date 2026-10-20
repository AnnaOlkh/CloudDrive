using System.Text;
using CloudDrive.Client;
using CloudDrive.Core.Files;
using CloudDrive.Core.Sync;

namespace CloudDrive.Tests.Server;

public sealed class SyncServiceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Run_UploadsNestedFiles_DownloadsRemoteChanges_AndPersistsConfirmedManifest()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var remoteRoot = await api.GetRootFolderAsync();
        var directory = Path.Combine(Path.GetTempPath(), "clouddrive-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        var manifestDirectory = Path.Combine(Path.GetTempPath(), "clouddrive-manifest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(directory, "nested", "example.txt");
            await File.WriteAllTextAsync(source, "first");
            var store = new SyncManifestStore(manifestDirectory);
            var sync = new SyncService(api, store);

            var first = await sync.RunAsync(directory, remoteRoot.Id);
            Assert.Empty(first.Failures);
            Assert.Single(first.Completed);
            Assert.Empty(first.Plan.Conflicts);

            var uploaded = Assert.Single(await api.GetSnapshotAsync(remoteRoot.Id));
            Assert.Equal("nested/example.txt", uploaded.RelativePath);
            Assert.Equal(ContentHasher.Compute(Encoding.UTF8.GetBytes("first")), uploaded.ContentHash);

            using var replacement = new MemoryStream(Encoding.UTF8.GetBytes("second"));
            await api.ReplaceAsync(uploaded.Id, "example.txt", replacement, uploaded.Version);
            var second = await sync.RunAsync(directory, remoteRoot.Id);
            Assert.Empty(second.Failures);
            Assert.Equal("second", await File.ReadAllTextAsync(source));
            Assert.Equal("nested/example.txt", Assert.Single(second.Completed).Path);
            var manifest = await store.LoadAsync(directory, remoteRoot.Id);
            Assert.Equal(ContentHasher.Compute(Encoding.UTF8.GetBytes("second")), Assert.Single(manifest.Files).Hash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            if (Directory.Exists(manifestDirectory)) Directory.Delete(manifestDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WhenBothSidesChanged_DoesNotOverwriteEitherVersion()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var root = await api.GetRootFolderAsync();
        var directory = Path.Combine(Path.GetTempPath(), "clouddrive-sync-" + Guid.NewGuid().ToString("N"));
        var manifestDirectory = Path.Combine(Path.GetTempPath(), "clouddrive-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "both.txt");
            await File.WriteAllTextAsync(path, "base");
            var sync = new SyncService(api, new SyncManifestStore(manifestDirectory));
            await sync.RunAsync(directory, root.Id);
            var original = Assert.Single(await api.GetSnapshotAsync(root.Id));
            await File.WriteAllTextAsync(path, "local");
            using var modified = new MemoryStream(Encoding.UTF8.GetBytes("remote"));
            await api.ReplaceAsync(original.Id, "both.txt", modified, original.Version);

            var next = await sync.RunAsync(directory, root.Id);
            Assert.Equal(new[] { "both.txt" }, next.Plan.Conflicts);
            Assert.Empty(next.Completed);
            Assert.Equal("local", await File.ReadAllTextAsync(path));
            var entry = Assert.Single(await api.GetSnapshotAsync(root.Id));
            Assert.Equal(ContentHasher.Compute(Encoding.UTF8.GetBytes("remote")), entry.ContentHash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            if (Directory.Exists(manifestDirectory)) Directory.Delete(manifestDirectory, recursive: true);
        }
    }
    [Fact]
    public async Task Run_RemoteDeletion_RequiresApproval_ThenDeletesLocal()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var root = await api.GetRootFolderAsync();
        var local = Path.Combine(Path.GetTempPath(), "clouddrive-delete-" + Guid.NewGuid().ToString("N"));
        var manifests = Path.Combine(Path.GetTempPath(), "clouddrive-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(local);
        try
        {
            var path = Path.Combine(local, "photo.jpg");
            await File.WriteAllTextAsync(path, "example");
            var sync = new SyncService(api, new SyncManifestStore(manifests));
            await sync.RunAsync(local, root.Id);
            var entry = Assert.Single(await api.GetSnapshotAsync(root.Id));
            await api.DeleteAsync(entry.Id);
            var skipped = await sync.RunAsync(local, root.Id);
            Assert.Equal(new[] { "photo.jpg" }, skipped.Plan.ToDeleteLocal);
            Assert.True(File.Exists(path));
            var completed = await sync.RunAsync(local, root.Id, plan =>
                Task.FromResult<IReadOnlyCollection<string>>(plan.ToDeleteLocal));
            Assert.False(File.Exists(path));
            Assert.Contains(completed.Completed, x => x.Path == "photo.jpg" && x.ConfirmedHash is null);
        }
        finally
        {
            Directory.Delete(local, true);
            if (Directory.Exists(manifests)) Directory.Delete(manifests, true);
        }
    }

    [Fact]
    public async Task Run_LocalDeletion_RequiresApproval_ThenDeletesRemote()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var root = await api.GetRootFolderAsync();
        var local = Path.Combine(Path.GetTempPath(), "clouddrive-delete-" + Guid.NewGuid().ToString("N"));
        var manifests = Path.Combine(Path.GetTempPath(), "clouddrive-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(local);
        try
        {
            var path = Path.Combine(local, "gone.txt");
            await File.WriteAllTextAsync(path, "example");
            var sync = new SyncService(api, new SyncManifestStore(manifests));
            await sync.RunAsync(local, root.Id);
            File.Delete(path);
            var skipped = await sync.RunAsync(local, root.Id);
            Assert.Equal(new[] { "gone.txt" }, skipped.Plan.ToDeleteRemote);
            Assert.Single(await api.GetSnapshotAsync(root.Id));
            var completed = await sync.RunAsync(local, root.Id, plan =>
                Task.FromResult<IReadOnlyCollection<string>>(plan.ToDeleteRemote));
            Assert.Empty(await api.GetSnapshotAsync(root.Id));
            Assert.Contains(completed.Completed, x => x.Path == "gone.txt" && x.ConfirmedHash is null);
        }
        finally
        {
            Directory.Delete(local, true);
            if (Directory.Exists(manifests)) Directory.Delete(manifests, true);
        }
    }

}
