namespace CloudDrive.Core.Files;

public enum PreviewKind { None, Text, Image }

public static class FileTypeHelper
{
    public static PreviewKind GetPreviewKind(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".c" => PreviewKind.Text,
            ".jpg" => PreviewKind.Image,
            _ => PreviewKind.None
        };

    public static string? GetPreviewContentType(string fileName) =>
        GetPreviewKind(fileName) switch
        {
            PreviewKind.Text => "text/plain; charset=utf-8",
            PreviewKind.Image => "image/jpeg",
            _ => null
        };
}
