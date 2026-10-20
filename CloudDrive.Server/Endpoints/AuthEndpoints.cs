using CloudDrive.Core.Contracts;
using CloudDrive.Server.Auth;
using CloudDrive.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CloudDrive.Server.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/register", async (RegisterRequest req, AppDbContext db, TokenService tokens) =>
        {
            var name = req.UserName?.Trim() ?? "";
            if (name.Length is < 3 or > 32)
                return Results.BadRequest(new ApiError("Імʼя користувача має містити від 3 до 32 символів."));
            if ((req.Password?.Length ?? 0) < 6)
                return Results.BadRequest(new ApiError("Пароль має містити щонайменше 6 символів."));
            if (await db.Users.AnyAsync(u => u.UserName == name))
                return Results.Conflict(new ApiError("Користувач із таким імʼям уже існує."));

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = name,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
            };
            db.Users.Add(user);
            db.Folders.Add(new Folder { Id = Guid.NewGuid(), OwnerId = user.Id, Name = "root" });

            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                return Results.Conflict(new ApiError("Користувач із таким імʼям уже існує."));
            }

            return Results.Ok(new LoginResponse(tokens.Create(user), user.UserName));
        });

        g.MapPost("/login", async (LoginRequest req, AppDbContext db, TokenService tokens) =>
        {
            var name = req.UserName?.Trim() ?? "";
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == name);
            if (user is null || string.IsNullOrEmpty(req.Password) ||
                !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                return Results.Json(new ApiError("Невірне імʼя користувача або пароль."), statusCode: StatusCodes.Status401Unauthorized);

            return Results.Ok(new LoginResponse(tokens.Create(user), user.UserName));
        });
    }
}
