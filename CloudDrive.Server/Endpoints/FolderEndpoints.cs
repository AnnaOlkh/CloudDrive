using System.Security.Claims;
using CloudDrive.Core.Contracts;
using CloudDrive.Core.Files;
using CloudDrive.Server.Auth;
using CloudDrive.Server.Common;
using CloudDrive.Server.Data;
using CloudDrive.Server.Storage;
using Microsoft.EntityFrameworkCore;

namespace CloudDrive.Server.Endpoints;

public static class FolderEndpoints
{
    public static void MapFolders(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/folders").RequireAuthorization().WithTags("Folders");

        g.MapGet("/", async (ClaimsPrincipal u, AppDbContext db, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            var list = await db.Folders.Where(f => f.OwnerId == uid)
                .OrderBy(f => f.Name)
                .Select(f => new FolderDto(f.Id, f.Name, f.ParentId))
                .ToListAsync(ct);
            return Results.Ok(list);
        });

        g.MapPost("/", async (CreateFolderRequest req, ClaimsPrincipal u, AppDbContext db, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            if (!FileNameValidator.IsValid(req.Name))
                return Results.BadRequest(new ApiError("Недопустиме імʼя папки."));

            var parentId = await FolderResolver.ResolveAsync(db, uid, req.ParentId, ct);
            if (parentId is null) return Results.NotFound(new ApiError("Батьківську папку не знайдено."));

            if (await db.Folders.AnyAsync(f => f.OwnerId == uid && f.ParentId == parentId && f.Name == req.Name, ct) ||
                await db.Files.AnyAsync(f => f.FolderId == parentId && f.Name == req.Name, ct))
                return Results.Conflict(new ApiError("Елемент із таким імʼям уже існує в цій папці."));

            var folder = new Folder { Id = Guid.NewGuid(), OwnerId = uid, ParentId = parentId, Name = req.Name };
            db.Folders.Add(folder);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                return Results.Conflict(new ApiError("Елемент із таким імʼям уже існує в цій папці."));
            }

            return Results.Created($"/api/folders/{folder.Id}", new FolderDto(folder.Id, folder.Name, folder.ParentId));
        });

        g.MapGet("/{id:guid}/children", async (Guid id, ClaimsPrincipal u, AppDbContext db, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            if (await FolderResolver.ResolveAsync(db, uid, id, ct) is null)
                return Results.NotFound(new ApiError("Папку не знайдено."));

            var list = await db.Folders.Where(f => f.OwnerId == uid && f.ParentId == id)
                .OrderBy(f => f.Name)
                .Select(f => new FolderDto(f.Id, f.Name, f.ParentId))
                .ToListAsync(ct);
            return Results.Ok(list);
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, AppDbContext db,
            IFileStorage storage, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            var target = await db.Folders.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == uid, ct);
            if (target is null) return Results.NotFound(new ApiError("Папку не знайдено."));
            if (target.ParentId is null)
                return Results.BadRequest(new ApiError("Кореневу папку My Drive видаляти не можна."));

            var allFolders = await db.Folders.Where(f => f.OwnerId == uid).ToListAsync(ct);
            var byParent = allFolders.Where(f => f.ParentId.HasValue)
                .ToLookup(f => f.ParentId!.Value);
            var depths = new Dictionary<Guid, int> { [id] = 0 };
            var queue = new Queue<Guid>();
            queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                foreach (var child in byParent[parent])
                {
                    if (!depths.TryAdd(child.Id, depths[parent] + 1)) continue;
                    queue.Enqueue(child.Id);
                }
            }

            var ids = depths.Keys.ToList();
            var files = await db.Files.Where(f => f.OwnerId == uid && ids.Contains(f.FolderId))
                .ToListAsync(ct);
            var foldersToRemove = allFolders.Where(f => depths.ContainsKey(f.Id))
                .OrderByDescending(f => depths[f.Id]).ToList();

            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                db.Files.RemoveRange(files);
                await db.SaveChangesAsync(ct);
                foreach (var folder in foldersToRemove)
                {
                    db.Folders.Remove(folder);
                    await db.SaveChangesAsync(ct);
                }
                await transaction.CommitAsync(ct);
            }
            foreach (var file in files)
                storage.Delete(file.StoragePath);
            return Results.NoContent();
        });

        g.MapGet("/{id:guid}/snapshot", async (Guid id, ClaimsPrincipal u, AppDbContext db, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            if (await FolderResolver.ResolveAsync(db, uid, id, ct) is null)
                return Results.NotFound(new ApiError("Папку не знайдено."));

            var folders = await db.Folders.Where(f => f.OwnerId == uid)
                .Select(f => new { f.Id, f.ParentId, f.Name }).ToListAsync(ct);
            var byParent = folders.Where(f => f.ParentId != null).ToLookup(f => f.ParentId!.Value);

            var prefixes = new Dictionary<Guid, string> { [id] = "" };
            var queue = new Queue<Guid>();
            queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in byParent[current])
                {
                    prefixes[child.Id] = prefixes[current] + child.Name + "/";
                    queue.Enqueue(child.Id);
                }
            }

            var ids = prefixes.Keys.ToList();
            var files = await db.Files.Where(f => ids.Contains(f.FolderId))
                .Select(f => new { f.Id, f.FolderId, f.Name, f.ContentHash, f.Version })
                .ToListAsync(ct);

            var result = files
                .Select(f => new RemoteFileEntry(f.Id, prefixes[f.FolderId] + f.Name, f.ContentHash, f.Version))
                .OrderBy(e => e.RelativePath, StringComparer.Ordinal)
                .ToList();
            return Results.Ok(result);
        });
    }
}
