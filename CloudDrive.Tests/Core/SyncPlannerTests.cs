using CloudDrive.Core.Sync;

namespace CloudDrive.Tests.Core;

public class SyncPlannerTests
{
    private static FileSnapshot S(string path, string hash) => new(path, hash);
    private static FileSnapshot[] None => Array.Empty<FileSnapshot>();

    [Fact]
    public void NewLocalFile_IsUploaded()
    {
        var plan = SyncPlanner.Plan(new[] { S("a.txt", "h1") }, None, None);
        Assert.Equal(new[] { "a.txt" }, plan.ToUpload);
        Assert.Empty(plan.ToDownload);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void NewRemoteFile_IsDownloaded()
    {
        var plan = SyncPlanner.Plan(None, new[] { S("a.txt", "h1") }, None);
        Assert.Equal(new[] { "a.txt" }, plan.ToDownload);
        Assert.Empty(plan.ToUpload);
    }

    [Fact]
    public void IdenticalFiles_NothingToDo()
    {
        var files = new[] { S("a.txt", "h1") };
        Assert.True(SyncPlanner.Plan(files, files, files).IsEmpty);
    }

    [Fact]
    public void FirstSync_SameFileOnBothSides_NothingToDo()
    {
        var files = new[] { S("a.txt", "h1") };
        Assert.True(SyncPlanner.Plan(files, files, None).IsEmpty);
    }

    [Fact]
    public void FirstSync_DifferentContentOnBothSides_IsConflict()
    {
        var plan = SyncPlanner.Plan(new[] { S("a.txt", "L") }, new[] { S("a.txt", "R") }, None);
        Assert.Equal(new[] { "a.txt" }, plan.Conflicts);
    }

    [Fact]
    public void ChangedLocallyOnly_IsUploaded()
    {
        var prev = new[] { S("a.txt", "h1") };
        var plan = SyncPlanner.Plan(new[] { S("a.txt", "h2") }, prev, prev);
        Assert.Equal(new[] { "a.txt" }, plan.ToUpload);
    }

    [Fact]
    public void ChangedRemotelyOnly_IsDownloaded()
    {
        var prev = new[] { S("a.txt", "h1") };
        var plan = SyncPlanner.Plan(prev, new[] { S("a.txt", "h2") }, prev);
        Assert.Equal(new[] { "a.txt" }, plan.ToDownload);
    }

    [Fact]
    public void ChangedOnBothSides_IsConflict_NotOverwritten()
    {
        var prev = new[] { S("a.txt", "h1") };
        var plan = SyncPlanner.Plan(new[] { S("a.txt", "L") }, new[] { S("a.txt", "R") }, prev);
        Assert.Equal(new[] { "a.txt" }, plan.Conflicts);
        Assert.Empty(plan.ToUpload);
        Assert.Empty(plan.ToDownload);
    }

    [Fact]
    public void DeletedLocally_RemoteUnchanged_IsNotRedownloaded()
    {
        var prev = new[] { S("a.txt", "h1") };
        var plan = SyncPlanner.Plan(None, prev, prev);
        Assert.Equal(new[] { "a.txt" }, plan.ToDeleteRemote);
    }

    [Fact]
    public void DeletedRemotely_LocalUnchanged_IsNotReuploaded()
    {
        var prev = new[] { S("a.txt", "h1") };
        var plan = SyncPlanner.Plan(prev, None, prev);
        Assert.Equal(new[] { "a.txt" }, plan.ToDeleteLocal);
    }

    [Fact]
    public void DeletedLocally_ChangedRemotely_IsConflict()
    {
        var prev = new[] { S("a.txt", "h1") };
        var plan = SyncPlanner.Plan(None, new[] { S("a.txt", "h2") }, prev);
        Assert.Equal(new[] { "a.txt" }, plan.Conflicts);
    }

    [Fact]
    public void DeletedRemotely_ChangedLocally_IsConflict()
    {
        var prev = new[] { S("a.txt", "h1") };
        var plan = SyncPlanner.Plan(new[] { S("a.txt", "h2") }, None, prev);
        Assert.Equal(new[] { "a.txt" }, plan.Conflicts);
    }

    [Fact]
    public void ConfirmedDeletion_RemovesBaseline()
    {
        var prev = new[] { S("a.txt", "h1") };
        var next = SyncPlanner.NextSnapshot(prev, prev, None,
            new[] { new SyncOperationResult("a.txt", null) });
        Assert.Empty(next);
    }

    [Fact]
    public void UnconfirmedDeletion_PreservesBaseline()
    {
        var prev = new[] { S("a.txt", "h1") };
        var next = SyncPlanner.NextSnapshot(prev, prev, None, Array.Empty<SyncOperationResult>());
        Assert.Equal(prev, next);
    }

    [Fact]
    public void Paths_AreNormalized_BackslashAndCase()
    {
        var plan = SyncPlanner.Plan(new[] { S("Docs\\A.txt", "h1") }, new[] { S("docs/a.txt", "h1") }, None);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void NextSnapshot_RecordsSuccessfulTransfers()
    {
        var local = new[] { S("up.txt", "h1"), S("same.txt", "s") };
        var remote = new[] { S("down.txt", "h2"), S("same.txt", "s") };
        var plan = SyncPlanner.Plan(local, remote, None);

        var next = SyncPlanner.NextSnapshot(None, local, remote, new[] { new SyncOperationResult("up.txt", "h1"), new SyncOperationResult("down.txt", "h2") }).ToDictionary(x => x.RelativePath, x => x.Hash);

        Assert.Equal("h1", next["up.txt"]);
        Assert.Equal("h2", next["down.txt"]);
        Assert.Equal("s", next["same.txt"]);
    }

    [Fact]
    public void NextSnapshot_KeepsOldValue_ForFailedAndConflicts()
    {
        var prev = new[] { S("fail.txt", "old"), S("conf.txt", "old") };
        var local = new[] { S("fail.txt", "new"), S("conf.txt", "L") };
        var remote = new[] { S("fail.txt", "old"), S("conf.txt", "R") };
        var plan = SyncPlanner.Plan(local, remote, prev);

        var next = SyncPlanner.NextSnapshot(prev, local, remote, Array.Empty<SyncOperationResult>())
            .ToDictionary(x => x.RelativePath, x => x.Hash);

        Assert.Equal("old", next["fail.txt"]);
        Assert.Equal("old", next["conf.txt"]);
    }

    [Fact]
    public void NextSnapshot_RequiresExplicitConfirmation()
    {
        var local = new[] { S("pending.txt", "new") };
        Assert.Empty(SyncPlanner.NextSnapshot(None, local, None, Array.Empty<SyncOperationResult>()));
        var confirmed = SyncPlanner.NextSnapshot(None, local, None,
            new[] { new SyncOperationResult("pending.txt", "new") });
        Assert.Equal("new", Assert.Single(confirmed).Hash);
    }

    [Fact]
    public void NextSnapshot_RemovesFilesDeletedOnBothSides()
    {
        var prev = new[] { S("gone.txt", "h1") };
        var plan = SyncPlanner.Plan(None, None, prev);
        Assert.Empty(SyncPlanner.NextSnapshot(prev, None, None, Array.Empty<SyncOperationResult>()));
    }

    [Fact]
    public void NextSnapshot_KeepsSkippedDeletion_SoItStaysSkipped()
    {
        var prev = new[] { S("a.txt", "h1") };
        var remote = new[] { S("a.txt", "h1") };
        var plan = SyncPlanner.Plan(None, remote, prev);

        var next = SyncPlanner.NextSnapshot(prev, None, remote, Array.Empty<SyncOperationResult>());
        Assert.Equal(new[] { "a.txt" }, SyncPlanner.Plan(None, remote, next).ToDeleteRemote); // наступна синхронізація теж нічого не робить
    }
}

public class ConflictNamingTests
{
    [Theory]
    [InlineData("a.txt", "a (conflict remote 2026-10-10).txt")]
    [InlineData("docs/report.final.c", "docs/report.final (conflict remote 2026-10-10).c")]
    [InlineData("docs\\x.jpg", "docs/x (conflict remote 2026-10-10).jpg")]
    [InlineData("README", "README (conflict remote 2026-10-10)")]
    public void Make_InsertsMarkerBeforeExtension(string path, string expected) =>
        Assert.Equal(expected, ConflictNaming.Make(path, "remote", new DateTime(2026, 10, 10)));
}
