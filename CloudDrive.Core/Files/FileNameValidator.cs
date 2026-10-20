namespace CloudDrive.Core.Files;

public static class FileNameValidator
{
    private static readonly char[] Forbidden = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "CONIN$", "CONOUT$"
    };

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return false;
        if (name is "." or "..") return false;
        if (name != name.Trim() || name.EndsWith('.')) return false;
        if (Reserved.Contains(name.Split('.')[0])) return false;
        return name.IndexOfAny(Forbidden) < 0 && !name.Any(char.IsControl);
    }
}
