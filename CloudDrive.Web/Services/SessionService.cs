using System.Text.Json;
using Microsoft.JSInterop;
using CloudDrive.Client;
using CloudDrive.Core.Contracts;

namespace CloudDrive.Web.Services;

public sealed class SessionService(CloudDriveApiClient api, BrowserInterop browser)
{
    private const string StorageKey = "clouddrive.session";

    private sealed record StoredSession(string Token, string UserName);

    public CloudDriveApiClient Api => api;
    public string? UserName { get; private set; }
    public bool IsAuthenticated => UserName is not null;

    public string? Notice { get; set; }

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            var json = await browser.StorageGetAsync(StorageKey);
            if (string.IsNullOrEmpty(json)) return;

            var stored = JsonSerializer.Deserialize<StoredSession>(json);
            if (stored is null || string.IsNullOrEmpty(stored.Token)) return;

            api.SetToken(stored.Token);
            UserName = stored.UserName;
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            await browser.StorageRemoveAsync(StorageKey);
        }
    }

    public Task LoginAsync(string userName, string password) => AuthenticateAsync(api.LoginAsync(userName, password));
    public Task RegisterAsync(string userName, string password) => AuthenticateAsync(api.RegisterAsync(userName, password));

    private async Task AuthenticateAsync(Task<LoginResponse> request)
    {
        var response = await request;
        UserName = response.UserName;
        Notice = null;
        await browser.StorageSetAsync(StorageKey, JsonSerializer.Serialize(new StoredSession(response.Token, response.UserName)));
        Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        api.Logout();
        UserName = null;
        await browser.StorageRemoveAsync(StorageKey);
        Changed?.Invoke();
    }

    public async Task ExpireAsync()
    {
        await LogoutAsync();
        Notice = "Your session has expired. Please sign in again.";
        Changed?.Invoke();
    }
}
