using System.Text.Json;
using CloudDrive.Web.Models;

namespace CloudDrive.Web.Services;

public sealed class UserPrefs(BrowserInterop browser)
{
    private const string StorageKey = "clouddrive.columns";
    private bool _loaded;

    public ColumnPrefs Columns { get; private set; } = new();

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var json = await browser.StorageGetAsync(StorageKey);
            if (!string.IsNullOrEmpty(json))
                Columns = JsonSerializer.Deserialize<ColumnPrefs>(json) ?? new ColumnPrefs();
        }
        catch (JsonException) { Columns = new ColumnPrefs(); }
    }

    public ValueTask SaveAsync() => browser.StorageSetAsync(StorageKey, JsonSerializer.Serialize(Columns));
}
