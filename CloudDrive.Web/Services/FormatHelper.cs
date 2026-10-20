using System.Globalization;

namespace CloudDrive.Web.Services;

public static class FormatHelper
{
    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string Time(DateTime utc) =>
        utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static (string Icon, string Css) FileKind(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".c" or ".h" or ".cpp" or ".cs" or ".py" or ".js" or ".json" or ".xml" => ("code", "kind-code"),
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => ("image", "kind-image"),
            ".txt" or ".md" or ".log" => ("file-text", "kind-doc"),
            ".pdf" => ("file-text", "kind-pdf"),
            ".doc" or ".docx" => ("file-text", "kind-word"),
            ".xls" or ".xlsx" => ("file-text", "kind-sheet"),
            ".ppt" or ".pptx" => ("file-text", "kind-slides"),
            _ => ("file", "kind-generic")
        };
}
