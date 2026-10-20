using System.Net;
using System.Text;
using CloudDrive.Client;
using CloudDrive.Core.Contracts;
using CloudDrive.Core.Files;

namespace CloudDrive.Tests.Server;

public class ApiIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<HttpStatusCode> StatusOf(Func<Task> action) =>
        (await Assert.ThrowsAsync<ApiException>(action)).StatusCode;

    // ---------- FR-01 ----------
    [Fact]
    public async Task Register_ThenLogin_Works()
    {
        var (_, name) = await factory.CreateLoggedInAsync();
        var second = factory.CreateApiClient();
        var response = await second.LoginAsync(name, "secret123");
        Assert.Equal(name, response.UserName);
        Assert.False(string.IsNullOrEmpty(response.Token));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var (_, name) = await factory.CreateLoggedInAsync();
        var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(() => client.LoginAsync(name, "wrong-password")));
    }

    [Fact]
    public async Task Register_DuplicateUserName_Returns409()
    {
        var (_, name) = await factory.CreateLoggedInAsync();
        var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Conflict, await StatusOf(() => client.RegisterAsync(name, "secret123")));
    }

    [Fact]
    public async Task Files_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/files");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- FR-02, FR-04, FR-05 ----------
    [Fact]
    public async Task Upload_List_Download_RoundTrip()
    {
        var (api, name) = await factory.CreateLoggedInAsync();
        const string text = "int main(void) { return 0; }";

        var uploaded = await api.UploadAsync("main.c", Bytes(text));

        Assert.Equal("main.c", uploaded.Name);
        Assert.Equal(name, uploaded.UploadedBy);
        Assert.Equal(name, uploaded.LastEditedBy);
        Assert.Equal(1, uploaded.Version);
        Assert.Equal(ContentHasher.Compute(Encoding.UTF8.GetBytes(text)), uploaded.ContentHash);

        var list = await api.GetFilesAsync();
        Assert.Contains(list, f => f.Id == uploaded.Id);

        using var ms = new MemoryStream();
        await api.DownloadAsync(uploaded.Id, ms);
        Assert.Equal(text, Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task Upload_DuplicateName_Returns409()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        await api.UploadAsync("a.txt", Bytes("1"));
        Assert.Equal(HttpStatusCode.Conflict, await StatusOf(() => api.UploadAsync("a.txt", Bytes("2"))));
    }

    // ---------- FR-08, FR-09 (індивідуальна операція через API) ----------
    [Fact]
    public async Task List_SortAndFilter_AppliedByServer()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        foreach (var n in new[] { "b.txt", "a.jpg", "c.c" }) await api.UploadAsync(n, Bytes("x"));

        var filtered = await api.GetFilesAsync(sort: SortOrder.NameAsc, filter: FileFilter.TxtJpg);
        Assert.Equal(new[] { "a.jpg", "b.txt" }, filtered.Select(f => f.Name));

        var desc = await api.GetFilesAsync(sort: SortOrder.NameDesc, filter: FileFilter.All);
        Assert.Equal(new[] { "c.c", "b.txt", "a.jpg" }, desc.Select(f => f.Name));
    }

    // ---------- FR-07 ----------
    [Fact]
    public async Task Content_ForCFile_ReturnsText_ForTxt_Returns415()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var c = await api.UploadAsync("hello.c", Bytes("// hi"));
        var txt = await api.UploadAsync("hello.txt", Bytes("hi"));

        Assert.Equal("// hi", Encoding.UTF8.GetString(await api.GetContentAsync(c.Id)));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, await StatusOf(() => api.GetContentAsync(txt.Id)));
    }

    // ---------- FR-06 ----------
    [Fact]
    public async Task Delete_RemovesFile()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var file = await api.UploadAsync("gone.txt", Bytes("bye"));

        await api.DeleteAsync(file.Id);

        Assert.DoesNotContain(await api.GetFilesAsync(), f => f.Id == file.Id);
        Assert.Equal(HttpStatusCode.NotFound, await StatusOf(() => api.DownloadAsync(file.Id, new MemoryStream())));
    }

    // ---------- ізоляція кабінетів ----------
    [Fact]
    public async Task User_CannotAccessAnotherUsersFile()
    {
        var (alice, _) = await factory.CreateLoggedInAsync();
        var (bob, _) = await factory.CreateLoggedInAsync();
        var file = await alice.UploadAsync("secret.txt", Bytes("alice only"));

        Assert.Equal(HttpStatusCode.NotFound, await StatusOf(() => bob.DownloadAsync(file.Id, new MemoryStream())));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOf(() => bob.DeleteAsync(file.Id)));
        Assert.Empty(await bob.GetFilesAsync());
    }

    // ---------- Version / optimistic concurrency ----------
    [Fact]
    public async Task Replace_IncrementsVersion_AndStaleVersionIsRejected()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var v1 = await api.UploadAsync("doc.txt", Bytes("one"));

        var v2 = await api.ReplaceAsync(v1.Id, "doc.txt", Bytes("two"), expectedVersion: 1);
        Assert.Equal(2, v2.Version);
        Assert.NotEqual(v1.ContentHash, v2.ContentHash);
        Assert.True(v2.ModifiedAt >= v1.ModifiedAt);

        Assert.Equal(HttpStatusCode.Conflict,
            await StatusOf(() => api.ReplaceAsync(v1.Id, "doc.txt", Bytes("stale"), expectedVersion: 1)));

        using var ms = new MemoryStream();
        await api.DownloadAsync(v1.Id, ms);
        Assert.Equal("two", Encoding.UTF8.GetString(ms.ToArray()));
    }

    // ---------- Folders + snapshot (для FR-10) ----------
    [Fact]
    public async Task Snapshot_IsRecursive_WithRelativePaths()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var root = await api.GetRootFolderAsync();
        var docs = await api.CreateFolderAsync("docs", root.Id);
        var inner = await api.CreateFolderAsync("inner", docs.Id);

        await api.UploadAsync("top.txt", Bytes("1"));
        await api.UploadAsync("a.c", Bytes("2"), docs.Id);
        await api.UploadAsync("b.txt", Bytes("3"), inner.Id);

        var snapshot = await api.GetSnapshotAsync(root.Id);
        Assert.Equal(new[] { "docs/a.c", "docs/inner/b.txt", "top.txt" }, snapshot.Select(e => e.RelativePath));

        var fromDocs = await api.GetSnapshotAsync(docs.Id);
        Assert.Equal(new[] { "a.c", "inner/b.txt" }, fromDocs.Select(e => e.RelativePath));
    }

    [Fact]
    public async Task DeleteFolder_RecursivelyRemovesFilesAndSubfolders()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var root = await api.GetRootFolderAsync();
        var parent = await api.CreateFolderAsync("delete-parent", root.Id);
        var child = await api.CreateFolderAsync("delete-child", parent.Id);
        var file = await api.UploadAsync("inside.txt", Bytes("data"), child.Id);

        await api.DeleteFolderAsync(parent.Id);

        Assert.DoesNotContain(await api.GetFoldersAsync(), f => f.Id == parent.Id || f.Id == child.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            await StatusOf(() => api.DownloadAsync(file.Id, new MemoryStream())));
    }

    [Fact]
    public async Task DeleteFolder_RejectsRootAndOtherUsersFolders()
    {
        var (alice, _) = await factory.CreateLoggedInAsync();
        var (bob, _) = await factory.CreateLoggedInAsync();
        var root = await alice.GetRootFolderAsync();
        var folder = await alice.CreateFolderAsync("private-folder", root.Id);

        Assert.Equal(HttpStatusCode.BadRequest,
            await StatusOf(() => alice.DeleteFolderAsync(root.Id)));
        Assert.Equal(HttpStatusCode.NotFound,
            await StatusOf(() => bob.DeleteFolderAsync(folder.Id)));
        Assert.Contains(await alice.GetFoldersAsync(), f => f.Id == folder.Id);
    }

    [Fact]
    public async Task CreateFolder_Duplicate_Returns409_AndInvalidName_Returns400()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        await api.CreateFolderAsync("docs");

        Assert.Equal(HttpStatusCode.Conflict, await StatusOf(() => api.CreateFolderAsync("docs")));
        Assert.Equal(HttpStatusCode.BadRequest, await StatusOf(() => api.CreateFolderAsync("bad/name")));
    }

    [Fact]
    public async Task GetChildren_ReturnsDirectSubfoldersOnly()
    {
        var (api, _) = await factory.CreateLoggedInAsync();
        var root = await api.GetRootFolderAsync();
        var a = await api.CreateFolderAsync("a", root.Id);
        await api.CreateFolderAsync("deep", a.Id);

        var children = await api.GetChildrenAsync(root.Id);
        Assert.Equal(new[] { "a" }, children.Select(c => c.Name));
    }
}
