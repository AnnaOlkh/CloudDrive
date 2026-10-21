using System.Text;
using CloudDrive.Server.Auth;
using CloudDrive.Server.Data;
using CloudDrive.Server.Endpoints;
using CloudDrive.Server.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Builder;

const long MaxUploadBytes = 100L * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);


var configuredDataRoot = builder.Configuration["Data:Root"];

var dataRoot = string.IsNullOrWhiteSpace(configuredDataRoot)
    ? Path.Combine(
        builder.Environment.ContentRootPath,
        "..",
        "CloudDriveData")
    : Path.IsPathRooted(configuredDataRoot)
        ? configuredDataRoot
        : Path.Combine(
            builder.Environment.ContentRootPath,
            configuredDataRoot);

dataRoot = Path.GetFullPath(dataRoot);

Directory.CreateDirectory(dataRoot);


var dbPath = Path.Combine(dataRoot, "clouddrive.db");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IFileStorage, DiskFileStorage>();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwt) =>
    {
        var j = jwt.Value;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = j.Issuer,
            ValidateAudience = true, ValidAudience = j.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(j.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
    if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    else p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUploadBytes);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxUploadBytes);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "CloudDrive API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
        Description = "Вставте JWT з /api/auth/login"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.UseSwagger();
app.UseSwaggerUI();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuth();
app.MapFolders();
app.MapFiles();

app.Map("/api/{**path}", () => Results.NotFound());

app.MapFallbackToFile("index.html");

app.Run();


public partial class Program { }
