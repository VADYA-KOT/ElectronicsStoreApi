
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ElectronicsStoreApi.Tests;

public class ProductImagesControllerIntegrationTests
    : IClassFixture<TestApplicationFactory>
{
    private const string ImageUrlPrefix = "/uploads/products/";

    private readonly TestApplicationFactory _factory;

    public ProductImagesControllerIntegrationTests(
        TestApplicationFactory factory)
    {
        _factory = factory;
    }

    // Admin может загрузить изображение товара.
    [Fact]
    public async Task Upload_AdminUser_ReturnsOkAndSavesImage()
    {
        int productId = await CreateProductAsync();

        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "product.png",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/products/{productId}/image",
            content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        string? imageUrl = json.RootElement
            .GetProperty("imageUrl")
            .GetString();

        Assert.NotNull(imageUrl);
        Assert.StartsWith(ImageUrlPrefix, imageUrl);

        string fileName = Path.GetFileName(imageUrl);

        string filePath = Path.Combine(
            GetImagesDirectory(),
            fileName);

        try
        {
            Assert.True(File.Exists(filePath));

            using var scope = _factory.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

            var product = await db.Products
                .AsNoTracking()
                .FirstAsync(p => p.Id == productId);

            Assert.Equal(imageUrl, product.ImageUrl);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    // Неавторизованный пользователь не может загружать изображения.
    [Fact]
    public async Task Upload_AnonymousUser_ReturnsUnauthorized()
    {
        int productId = await CreateProductAsync();

        using var client = _factory.CreateClient();

        using var content = CreateImageContent(
            "product.png",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/products/{productId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // Обычный пользователь не может загружать изображения.
    [Fact]
    public async Task Upload_RegularUser_ReturnsForbidden()
    {
        int productId = await CreateProductAsync();

        using var client = CreateAuthenticatedClient("User");

        using var content = CreateImageContent(
            "product.png",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/products/{productId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    // Обычный пользователь не может удалять изображения.
    [Fact]
    public async Task Delete_RegularUser_ReturnsForbidden()
    {
        int productId = await CreateProductAsync();

        using var client = CreateAuthenticatedClient("User");

        var response = await client.DeleteAsync(
            $"/api/products/{productId}/image");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    // Admin может удалить существующее изображение.
    [Fact]
    public async Task Delete_AdminUser_ReturnsNoContentAndDeletesImage()
    {
        const string fileName = "integration-test-image.png";

        string imageUrl = $"{ImageUrlPrefix}{fileName}";

        int productId = await CreateProductAsync(imageUrl);

        string imagesDirectory = GetImagesDirectory();

        Directory.CreateDirectory(imagesDirectory);

        string filePath = Path.Combine(
            imagesDirectory,
            fileName);

        await File.WriteAllBytesAsync(
            filePath,
            GetValidPngSignature());

        try
        {
            using var client = CreateAuthenticatedClient("Admin");

            var response = await client.DeleteAsync(
                $"/api/products/{productId}/image");

            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);

            Assert.False(File.Exists(filePath));

            using var scope = _factory.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

            var product = await db.Products
                .AsNoTracking()
                .FirstAsync(p => p.Id == productId);

            Assert.Null(product.ImageUrl);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    // Попытка загрузить изображение для отсутствующего товара.
    [Fact]
    public async Task Upload_NonExistingProduct_ReturnsNotFound()
    {
        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "product.png",
            GetValidPngSignature());

        var response = await client.PostAsync(
            "/api/products/2000000000/image",
            content);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // API должен отклонять неподдерживаемые форматы.
    [Fact]
    public async Task Upload_UnsupportedFormat_ReturnsBadRequest()
    {
        int productId = await CreateProductAsync();

        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "product.gif",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/products/{productId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    // API должен отклонять изображения размером более 5 МБ.
    [Fact]
    public async Task Upload_FileTooLarge_Returns413()
    {
        int productId = await CreateProductAsync();

        byte[] largeFile = new byte[
            checked((int)ProductImageService.MaxFileSize + 1)];

        // Добавляем корректную сигнатуру JPEG.
        largeFile[0] = 0xFF;
        largeFile[1] = 0xD8;
        largeFile[2] = 0xFF;

        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "large-image.jpg",
            largeFile);

        var response = await client.PostAsync(
            $"/api/products/{productId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
    }

    // Создание тестового товара в InMemory базе.
    private async Task<int> CreateProductAsync(
        string? imageUrl = null)
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<StoreDbContext>();

        var product = new Product
        {
            Name = $"ImageTest_{Guid.NewGuid():N}",
            Price = 100m,
            Quantity = 5,
            ImageUrl = imageUrl
        };

        db.Products.Add(product);

        await db.SaveChangesAsync();

        return product.Id;
    }

    // Создание HTTP-клиента с JWT-токеном нужной роли.
    private HttpClient CreateAuthenticatedClient(string role)
    {
        var client = _factory.CreateClient();

        string token = CreateJwtToken(role);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // Создание JWT с использованием ключа,
    // который настроен непосредственно в приложении.
    private string CreateJwtToken(string role)
    {
        var configuration = _factory.Services
            .GetRequiredService<IConfiguration>();

        var options = _factory.Services
            .GetRequiredService<
                IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        var signingKey = options.TokenValidationParameters
            .IssuerSigningKey as SymmetricSecurityKey
            ?? throw new InvalidOperationException(
                "JWT signing key не настроен в тестовом приложении.");

        string issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT Issuer не настроен.");

        string audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT Audience не настроен.");

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                Guid.NewGuid().ToString()),

            new Claim(
                ClaimTypes.Name,
                "image-integration-test"),

            new Claim(
                ClaimTypes.Role,
                role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                signingKey,
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    // Формирование multipart/form-data для загрузки файла.
    private static MultipartFormDataContent CreateImageContent(
        string fileName,
        byte[] fileBytes)
    {
        var content = new MultipartFormDataContent();

        var imageContent = new ByteArrayContent(fileBytes);

        imageContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");

        content.Add(
            imageContent,
            "file",
            fileName);

        return content;
    }

    // Минимальная сигнатура PNG.
    private static byte[] GetValidPngSignature()
    {
        return
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ];
    }

    private string GetImagesDirectory()
    {
        var environment = _factory.Services
            .GetRequiredService<IWebHostEnvironment>();

        return Path.Combine(
            environment.ContentRootPath,
            "ProductImages");
    }
}
