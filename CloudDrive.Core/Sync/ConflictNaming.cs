namespace CloudDrive.Core.Sync;

public static class ConflictNaming
{
    public static string Make(string relativePath, string label, DateTime date)
    {
        var path = relativePath.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        var dir = slash >= 0 ? path[..(slash + 1)] : "";
        var name = path[(slash + 1)..];
        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var ext = dot > 0 ? name[dot..] : "";
        return $"{dir}{stem} (conflict {label} {date:yyyy-MM-dd}){ext}";
    }
}
