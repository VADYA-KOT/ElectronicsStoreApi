using System.Security.Claims;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Controllers.
builder.Services.AddControllers();

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
builder.Services.AddScoped<CategoryImageService>();

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

// Swagger.
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Electronics Store API",
        Version = "v1",
        Description = "API интернет-магазина электроники."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description =
            "Введите JWT access token. Префикс Bearer добавляется автоматически.",

        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
});

var app = builder.Build();

app.UseHttpsRedirection();

// Angular application.
app.UseDefaultFiles();
app.UseStaticFiles();

// Product images.
var productImagesPath = Path.Combine(
    app.Environment.ContentRootPath,
    "ProductImages");

Directory.CreateDirectory(productImagesPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(productImagesPath),
    RequestPath = "/uploads/products"
});

// Category images.
var categoryImagesPath = Path.Combine(
    app.Environment.ContentRootPath,
    "CategoryImages");

Directory.CreateDirectory(categoryImagesPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(categoryImagesPath),
    RequestPath = "/uploads/categories"
});

// OpenAPI and Swagger UI.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "Electronics Store API v1");

        options.RoutePrefix = "swagger";
    });
}

// Authentication and authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Angular client-side routes.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
