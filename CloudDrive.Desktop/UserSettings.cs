using System.IO;
using System.Text.Json;

namespace CloudDrive.Desktop;

public sealed class UserSettings
{
    public string ServerUrl { get; set; } = "http://localhost:5000/";
    public string UserName { get; set; } = "";
    public bool ShowSize { get; set; } = true;
    public bool ShowCreated { get; set; } = true;
    public bool ShowModified { get; set; } = true;
    public bool ShowUploadedBy { get; set; } = true;
    public bool ShowEditedBy { get; set; } = true;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudDrive", "desktop-settings.json");

    private static UserSettings? _current;
    public static UserSettings Current => _current ??= Load();

    private static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
        }
        catch { }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
