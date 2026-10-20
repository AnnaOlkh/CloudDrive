using System.IO;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CloudDrive.Desktop;

public sealed class FileSizeConverter : IValueConverter
{
    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long bytes ? Format(bytes) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LocalTimeConverter : IValueConverter
{
    public static string Format(DateTime utc) => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime dt ? Format(dt) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FileIconConverter : IValueConverter
{
    private const string Code = "\uE943", Photo = "\uE91B", Doc = "\uE8A5", Generic = "\uE7C3";

    private static readonly Dictionary<string, (string Glyph, string Color)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".c"] = (Code, "#7C4DFF"), [".h"] = (Code, "#7C4DFF"), [".cpp"] = (Code, "#7C4DFF"), [".cs"] = (Code, "#7C4DFF"),
        [".py"] = (Code, "#7C4DFF"), [".js"] = (Code, "#7C4DFF"), [".json"] = (Code, "#7C4DFF"), [".xml"] = (Code, "#7C4DFF"),
        [".jpg"] = (Photo, "#2E9E6B"), [".jpeg"] = (Photo, "#2E9E6B"), [".png"] = (Photo, "#2E9E6B"),
        [".gif"] = (Photo, "#2E9E6B"), [".bmp"] = (Photo, "#2E9E6B"), [".webp"] = (Photo, "#2E9E6B"),
        [".txt"] = (Doc, "#5B6B86"), [".md"] = (Doc, "#5B6B86"), [".log"] = (Doc, "#5B6B86"),
        [".pdf"] = (Doc, "#D14343"), [".doc"] = (Doc, "#2B6CB0"), [".docx"] = (Doc, "#2B6CB0"),
        [".xls"] = (Doc, "#1E8E5A"), [".xlsx"] = (Doc, "#1E8E5A"), [".ppt"] = (Doc, "#D9822B"), [".pptx"] = (Doc, "#D9822B"),
    };

    private static readonly Dictionary<string, Brush> BrushCache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ext = Path.GetExtension(value as string ?? "");
        var (glyph, color) = Map.TryGetValue(ext, out var entry) ? entry : (Generic, "#7A8699");

        if ((parameter as string) == "brush")
        {
            if (!BrushCache.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                brush.Freeze();
                BrushCache[color] = brush;
            }
            return brush;
        }
        return glyph;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
