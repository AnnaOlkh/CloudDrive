using CloudDrive.Client;
using CloudDrive.Web;
using CloudDrive.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBase)) apiBase = builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromMinutes(10)
});
builder.Services.AddScoped<CloudDriveApiClient>();
builder.Services.AddScoped<BrowserInterop>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<DialogService>();
builder.Services.AddScoped<UserPrefs>();
builder.Services.AddScoped<WebSyncService>();

var host = builder.Build();
await host.Services.GetRequiredService<SessionService>().InitializeAsync();
await host.RunAsync();
