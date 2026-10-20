using CloudDrive.Core.Contracts;

namespace CloudDrive.Core.Files;

public static class FileListQuery
{
    private static readonly HashSet<string> TxtJpgExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".jpg" };

    public static IReadOnlyList<FileDto> Apply(IEnumerable<FileDto> files, FileFilter filter, SortOrder order)
    {
        ArgumentNullException.ThrowIfNull(files);

        IEnumerable<FileDto> query = filter switch
        {
            FileFilter.TxtJpg => files.Where(f => TxtJpgExtensions.Contains(Path.GetExtension(f.Name))),
            FileFilter.TxtOnly => files.Where(f => Path.GetExtension(f.Name).Equals(".txt", StringComparison.OrdinalIgnoreCase)),
            FileFilter.JpgOnly => files.Where(f => Path.GetExtension(f.Name).Equals(".jpg", StringComparison.OrdinalIgnoreCase)),
            _ => files
        };

        var ordered = order == SortOrder.NameDesc
            ? query.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                   .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            : query.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(f => f.Name, StringComparer.Ordinal);

        return ordered.ToList();
    }
}
