using System.Security.Claims;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Controllers.
builder.Services.AddControllers();

// CORS.
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy
            .WithOrigins(
                "http://192.168.1.12:5500",
                "http://localhost:5500",
                "http://127.0.0.1:5500"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Database.
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Строка подключения DefaultConnection не найдена.");

builder.Services.AddDbContext<StoreDbContext>(options =>
    options.UseNpgsql(connectionString));

// Services.
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ProductImageService>();

// Password hashing.
builder.Services.AddScoped<
    IPasswordHasher<AppUser>,
    PasswordHasher<AppUser>>();

// JWT settings.
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "Настройка Jwt:Issuer не найдена.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "Настройка Jwt:Audience не найдена.");

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Настройка Jwt:Key не найдена.");

// Authentication.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateLifetime = true,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Convert.FromBase64String(jwtKey)),

                RoleClaimType = ClaimTypes.Role,

                ClockSkew = TimeSpan.FromSeconds(30)
            };
    });

// Authorization.
builder.Services.AddAuthorization();

// OpenAPI.
builder.Services.AddOpenApi();

var app = builder.Build();

// HTTPS.
app.UseHttpsRedirection();

// CORS.
app.UseCors("FrontendPolicy");

// Frontend.
var frontendPath = Path.GetFullPath(
    Path.Combine(
        app.Environment.ContentRootPath,
        "..",
        "Frontend"));

if (!Directory.Exists(frontendPath))
{
    throw new DirectoryNotFoundException(
        $"Папка Frontend не найдена: {frontendPath}");
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(frontendPath)
});

// Product images.
var productImagesPath = Path.Combine(
    app.Environment.ContentRootPath,
    "ProductImages");

// Создаём папку, если её ещё нет.
Directory.CreateDirectory(productImagesPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(productImagesPath),
    RequestPath = "/uploads/products"
});

// OpenAPI.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Authentication и Authorization.
app.UseAuthentication();
app.UseAuthorization();

// Controllers.
app.MapControllers();

// Главная страница.
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();

public partial class Program { }