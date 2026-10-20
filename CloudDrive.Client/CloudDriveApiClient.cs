using System.Net.Http.Headers;
using System.Net.Http.Json;
using CloudDrive.Core.Contracts;

namespace CloudDrive.Client;

public class CloudDriveApiClient(HttpClient http)
{
    public async Task<LoginResponse> RegisterAsync(string userName, string password, CancellationToken ct = default) =>
        await AuthAsync("api/auth/register", new RegisterRequest(userName, password), ct);

    public async Task<LoginResponse> LoginAsync(string userName, string password, CancellationToken ct = default) =>
        await AuthAsync("api/auth/login", new LoginRequest(userName, password), ct);

    public void SetToken(string? token) =>
        http.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);

    public void Logout() => SetToken(null);

    private async Task<LoginResponse> AuthAsync<T>(string url, T body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(url, body, ct);
        await EnsureOkAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(ct)
                     ?? throw new ApiException(response.StatusCode, "Порожня відповідь сервера.");
        SetToken(result.Token);
        return result;
    }

    // ---------- Folders ----------
    public Task<List<FolderDto>> GetFoldersAsync(CancellationToken ct = default) =>
        GetJsonAsync<List<FolderDto>>("api/folders", ct);

    public async Task<FolderDto> GetRootFolderAsync(CancellationToken ct = default) =>
        (await GetFoldersAsync(ct)).First(f => f.ParentId is null);

    public Task<List<FolderDto>> GetChildrenAsync(Guid folderId, CancellationToken ct = default) =>
        GetJsonAsync<List<FolderDto>>($"api/folders/{folderId}/children", ct);

    public async Task<FolderDto> CreateFolderAsync(string name, Guid? parentId = null, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/folders", new CreateFolderRequest(name, parentId), ct);
        await EnsureOkAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<FolderDto>(ct))!;
    }

    public async Task DeleteFolderAsync(Guid folderId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/folders/{folderId}", ct);
        await EnsureOkAsync(response, ct);
    }

    public Task<List<RemoteFileEntry>> GetSnapshotAsync(Guid folderId, CancellationToken ct = default) =>
        GetJsonAsync<List<RemoteFileEntry>>($"api/folders/{folderId}/snapshot", ct);

    public Task<List<FileDto>> GetFilesAsync(Guid? folderId = null, SortOrder sort = SortOrder.NameAsc,
        FileFilter filter = FileFilter.All, CancellationToken ct = default)
    {
        var url = $"api/files?sort={sort}&filter={filter}";
        if (folderId is not null) url += $"&folderId={folderId}";
        return GetJsonAsync<List<FileDto>>(url, ct);
    }

    public async Task<FileDto> UploadAsync(string fileName, Stream content, Guid? folderId = null, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent { { new StreamContent(content), "file", fileName } };
        var url = folderId is null ? "api/files" : $"api/files?folderId={folderId}";
        using var response = await http.PostAsync(url, form, ct);
        await EnsureOkAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<FileDto>(ct))!;
    }

    public async Task<FileDto> ReplaceAsync(Guid fileId, string fileName, Stream content,
        int expectedVersion, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent { { new StreamContent(content), "file", fileName } };
        var url = $"api/files/{fileId}?expectedVersion={expectedVersion}";
        using var response = await http.PutAsync(url, form, ct);
        await EnsureOkAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<FileDto>(ct))!;
    }

    public async Task DownloadAsync(Guid fileId, Stream destination, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/files/{fileId}/download", HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureOkAsync(response, ct);
        await response.Content.CopyToAsync(destination, ct);
    }

    public async Task<byte[]> GetContentAsync(Guid fileId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/files/{fileId}/content", ct);
        await EnsureOkAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/files/{fileId}", ct);
        await EnsureOkAsync(response, ct);
    }

    public async Task DeleteVersionAsync(Guid fileId, int expectedVersion, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/files/{fileId}?expectedVersion={expectedVersion}", ct);
        await EnsureOkAsync(response, ct);
    }

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        await EnsureOkAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? message = null;
        try { message = (await response.Content.ReadFromJsonAsync<ApiError>(ct))?.Error; }
        catch { }

        throw new ApiException(response.StatusCode, message ?? $"{(int)response.StatusCode} {response.ReasonPhrase}");
    }
}
