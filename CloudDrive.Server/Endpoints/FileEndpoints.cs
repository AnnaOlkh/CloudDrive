using System.Security.Claims;
using CloudDrive.Core.Contracts;
using CloudDrive.Core.Files;
using CloudDrive.Server.Auth;
using CloudDrive.Server.Common;
using CloudDrive.Server.Data;
using CloudDrive.Server.Storage;
using Microsoft.EntityFrameworkCore;

namespace CloudDrive.Server.Endpoints;

public static class FileEndpoints
{
    public static void MapFiles(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/files").RequireAuthorization().WithTags("Files");

        // FR-02, FR-08, FR-09
        g.MapGet("/", async (Guid? folderId, SortOrder? sort, FileFilter? filter,
            ClaimsPrincipal u, AppDbContext db, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            var fid = await FolderResolver.ResolveAsync(db, uid, folderId, ct);
            if (fid is null) return Results.NotFound(new ApiError("Папку не знайдено."));

            var files = await db.Files.Where(f => f.FolderId == fid && f.OwnerId == uid)
                .Select(Projections.ToDto).ToListAsync(ct);
            return Results.Ok(FileListQuery.Apply(files, filter ?? FileFilter.All, sort ?? SortOrder.NameAsc));
        });

        // FR-04: завантаження на сервер
        g.MapPost("/", async (Guid? folderId, IFormFile file,
            ClaimsPrincipal u, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            var fid = await FolderResolver.ResolveAsync(db, uid, folderId, ct);
            if (fid is null) return Results.NotFound(new ApiError("Папку не знайдено."));

            var name = Path.GetFileName(file.FileName);
            if (!FileNameValidator.IsValid(name)) return Results.BadRequest(new ApiError("Недопустиме імʼя файлу."));

            if (await db.Files.AnyAsync(f => f.FolderId == fid && f.Name == name, ct) ||
                await db.Folders.AnyAsync(f => f.OwnerId == uid && f.ParentId == fid && f.Name == name, ct))
                return Results.Conflict(new ApiError("Файл або папка з такою назвою вже існує."));

            await using var stream = file.OpenReadStream();
            var saved = await storage.SaveAsync(uid, stream, ct);

            var now = DateTime.UtcNow;
            var entry = new FileEntry
            {
                Id = Guid.NewGuid(), OwnerId = uid, FolderId = fid.Value, Name = name,
                Size = saved.Size, StoragePath = saved.RelativePath, ContentHash = saved.Hash,
                CreatedAt = now, ModifiedAt = now,
                UploadedById = uid, LastEditedById = uid, Version = 1
            };
            db.Files.Add(entry);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                storage.Delete(saved.RelativePath);
                return Results.Conflict(new ApiError("Не вдалося створити файл у БД."));
            }
            catch
            {
                storage.Delete(saved.RelativePath);
                throw;
            }

            var dto = await db.Files.Where(f => f.Id == entry.Id).Select(Projections.ToDto).SingleAsync(ct);
            return Results.Created($"/api/files/{entry.Id}", dto);
        }).DisableAntiforgery();

        // FR-10: перезапис (оновлює ModifiedAt, LastEditedBy, Version). expectedVersion захищає від одночасних змін.
        g.MapPut("/{id:guid}", async (Guid id, int expectedVersion, IFormFile file,
            ClaimsPrincipal u, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            var entry = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == uid, ct);
            if (entry is null) return Results.NotFound(new ApiError("Файл не знайдено."));

            if (expectedVersion != entry.Version)
                return Results.Conflict(new ApiError(
                    $"Файл змінено іншим клієнтом (поточна версія {entry.Version}, очікувалась {expectedVersion})."));

            await using var stream = file.OpenReadStream();
            var saved = await storage.SaveAsync(uid, stream, ct);

            var oldPath = entry.StoragePath;
            entry.StoragePath = saved.RelativePath;
            entry.Size = saved.Size;
            entry.ContentHash = saved.Hash;
            entry.ModifiedAt = DateTime.UtcNow;
            entry.LastEditedById = uid;
            entry.Version++;

            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                storage.Delete(saved.RelativePath);
                return Results.Conflict(new ApiError("Файл одночасно змінено іншим клієнтом."));
            }
            catch
            {
                storage.Delete(saved.RelativePath);
                throw;
            }

            storage.Delete(oldPath);
            var dto = await db.Files.Where(f => f.Id == id).Select(Projections.ToDto).SingleAsync(ct);
            return Results.Ok(dto);
        }).DisableAntiforgery();

        // FR-05: вивантаження
        g.MapGet("/{id:guid}/download", async (Guid id, ClaimsPrincipal u, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            var entry = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == uid, ct);
            if (entry is null || !storage.Exists(entry.StoragePath))
                return Results.NotFound(new ApiError("Файл не знайдено."));

            return Results.File(storage.OpenRead(entry.StoragePath), "application/octet-stream", entry.Name);
        });

        // FR-07: вміст для превʼю (.c як текст, .jpg як зображення)
        g.MapGet("/{id:guid}/content", async (Guid id, ClaimsPrincipal u, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            var entry = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == uid, ct);
            if (entry is null || !storage.Exists(entry.StoragePath))
                return Results.NotFound(new ApiError("Файл не знайдено."));

            var contentType = FileTypeHelper.GetPreviewContentType(entry.Name);
            if (contentType is null)
                return Results.Json(new ApiError("Превʼю для цього типу файлів не підтримується."),
                    statusCode: StatusCodes.Status415UnsupportedMediaType);

            return Results.File(storage.OpenRead(entry.StoragePath), contentType);
        });

        // FR-06: видалення
        g.MapDelete("/{id:guid}", async (Guid id, int? expectedVersion, ClaimsPrincipal u, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            var uid = u.GetUserId();
            var entry = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == uid, ct);
            if (entry is null) return Results.NotFound(new ApiError("Файл не знайдено."));

            if (expectedVersion.HasValue && expectedVersion.Value != entry.Version)
                return Results.Conflict(new ApiError("Файл було змінено після початку синхронізації."));
            db.Files.Remove(entry);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new ApiError("Файл одночасно змінено іншим клієнтом."));
            }
            storage.Delete(entry.StoragePath);
            return Results.NoContent();
        });
    }
}
