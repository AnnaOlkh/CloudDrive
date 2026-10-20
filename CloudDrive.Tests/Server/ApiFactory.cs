using CloudDrive.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace CloudDrive.Tests.Server;

/// <summary>Піднімає справжній сервер у памʼяті з тимчасовою SQLite-БД і тимчасовим сховищем.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "clouddrive-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_dir, "test.db")}");
        builder.UseSetting("Storage:Root", Path.Combine(_dir, "storage"));
    }

    public CloudDriveApiClient CreateApiClient() => new(CreateClient());

    /// <summary>Новий клієнт із зареєстрованим унікальним користувачем.</summary>
    public async Task<(CloudDriveApiClient Client, string UserName)> CreateLoggedInAsync()
    {
        var client = CreateApiClient();
        var name = "user_" + Guid.NewGuid().ToString("N")[..8];
        await client.RegisterAsync(name, "secret123");
        return (client, name);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* тимчасові файли */ }
    }
}
