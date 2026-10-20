using System.Linq.Expressions;
using CloudDrive.Core.Contracts;
using CloudDrive.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CloudDrive.Server.Common;

public static class Projections
{
    public static readonly Expression<Func<FileEntry, FileDto>> ToDto = f => new FileDto(
        f.Id, f.FolderId, f.Name, f.Size, f.CreatedAt, f.ModifiedAt,
        f.UploadedBy.UserName, f.LastEditedBy.UserName, f.ContentHash, f.Version);
}

public static class FolderResolver
{
    public static async Task<Guid?> ResolveAsync(AppDbContext db, Guid ownerId, Guid? folderId, CancellationToken ct = default)
    {
        if (folderId is null)
            return await db.Folders.Where(f => f.OwnerId == ownerId && f.ParentId == null)
                .Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);

        var exists = await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == ownerId, ct);
        return exists ? folderId : null;
    }
}
