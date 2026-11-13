
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
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

namespace ElectronicsStoreApi.Tests;

public class CategoryImagesControllerIntegrationTests
    : IClassFixture<TestApplicationFactory>
{
    private const string ImageUrlPrefix = "/uploads/categories/";

    private readonly TestApplicationFactory _factory;

    public CategoryImagesControllerIntegrationTests(
        TestApplicationFactory factory)
    {
        _factory = factory;
    }

    // ================================================
    // UPLOAD IMAGE
    // ================================================

    [Fact]
    public async Task Upload_AdminUser_ReturnsOkAndSavesImage()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "category.png",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        string? imageUrl = json.RootElement
            .GetProperty("imageUrl")
            .GetString();

        Assert.NotNull(imageUrl);
        Assert.StartsWith(ImageUrlPrefix, imageUrl);

        string fileName = imageUrl![ImageUrlPrefix.Length..];

        string filePath = Path.Combine(
            GetImagesDirectory(),
            fileName);

        try
        {
            Assert.True(File.Exists(filePath));

            using var scope = _factory.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

            var category = await db.Categories
                .AsNoTracking()
                .SingleAsync(c => c.Id == categoryId);

            Assert.Equal(imageUrl, category.ImageUrl);
        }
        finally
        {
            DeleteTestFile(filePath);
        }
    }

    [Fact]
    public async Task Upload_AdminUser_ReplacesOldImage()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("Admin");

        string? firstImageUrl = null;
        string? secondImageUrl = null;

        string? firstFilePath = null;
        string? secondFilePath = null;

        try
        {
            using (var firstContent = CreateImageContent(
                "first.jpg",
                GetValidJpegSignature()))
            {
                var firstResponse = await client.PostAsync(
                    $"/api/categories/{categoryId}/image",
                    firstContent);

                Assert.Equal(
                    HttpStatusCode.OK,
                    firstResponse.StatusCode);

                using var firstJson = JsonDocument.Parse(
                    await firstResponse.Content.ReadAsStringAsync());

                firstImageUrl = firstJson.RootElement
                    .GetProperty("imageUrl")
                    .GetString();

                Assert.NotNull(firstImageUrl);

                firstFilePath = GetImagePath(firstImageUrl);
            }

            Assert.True(File.Exists(firstFilePath));

            using (var secondContent = CreateImageContent(
                "second.png",
                GetValidPngSignature()))
            {
                var secondResponse = await client.PostAsync(
                    $"/api/categories/{categoryId}/image",
                    secondContent);

                Assert.Equal(
                    HttpStatusCode.OK,
                    secondResponse.StatusCode);

                using var secondJson = JsonDocument.Parse(
                    await secondResponse.Content.ReadAsStringAsync());

                secondImageUrl = secondJson.RootElement
                    .GetProperty("imageUrl")
                    .GetString();

                Assert.NotNull(secondImageUrl);

                secondFilePath = GetImagePath(secondImageUrl);
            }

            Assert.NotEqual(firstImageUrl, secondImageUrl);

            // Старый файл удаляется после сохранения нового URL.
            Assert.False(File.Exists(firstFilePath));

            Assert.True(File.Exists(secondFilePath));

            using var scope = _factory.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

            var category = await db.Categories
                .AsNoTracking()
                .SingleAsync(c => c.Id == categoryId);

            Assert.Equal(secondImageUrl, category.ImageUrl);
        }
        finally
        {
            if (firstFilePath is not null)
            {
                DeleteTestFile(firstFilePath);
            }

            if (secondFilePath is not null)
            {
                DeleteTestFile(secondFilePath);
            }
        }
    }

    [Fact]
    public async Task Upload_AnonymousUser_ReturnsUnauthorized()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = _factory.CreateClient();

        using var content = CreateImageContent(
            "category.png",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_RegularUser_ReturnsForbidden()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("User");

        using var content = CreateImageContent(
            "category.png",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_NonExistingCategory_ReturnsNotFound()
    {
        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "category.png",
            GetValidPngSignature());

        var response = await client.PostAsync(
            "/api/categories/999999/image",
            content);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_UnsupportedFormat_ReturnsBadRequest()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "category.gif",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_InvalidSignature_ReturnsBadRequest()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "category.jpg",
            GetValidPngSignature());

        var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "empty.jpg",
            []);

        var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_MissingFile_ReturnsBadRequest()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("Admin");
        using var content = new MultipartFormDataContent();

        var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_FileTooLarge_Returns413()
    {
        int categoryId = await CreateCategoryAsync();

        byte[] largeFile = new byte[
            checked((int)CategoryImageService.MaxFileSize + 1)];

        // Сигнатура JPEG. Сам сервис сначала проверяет размер.
        largeFile[0] = 0xFF;
        largeFile[1] = 0xD8;
        largeFile[2] = 0xFF;

        using var client = CreateAuthenticatedClient("Admin");

        using var content = CreateImageContent(
            "large-category.jpg",
            largeFile);

        var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
    }

    // ================================================
    // DELETE IMAGE
    // ================================================

    [Fact]
    public async Task Delete_AdminUser_ReturnsNoContentAndDeletesImage()
    {
        const string fileName =
            "category-image-integration-test.png";

        string imageUrl = $"{ImageUrlPrefix}{fileName}";

        int categoryId = await CreateCategoryAsync(imageUrl);

        string filePath = Path.Combine(
            GetImagesDirectory(),
            fileName);

        Directory.CreateDirectory(GetImagesDirectory());

        await File.WriteAllBytesAsync(
            filePath,
            GetValidPngSignature());

        try
        {
            using var client = CreateAuthenticatedClient("Admin");

            var response = await client.DeleteAsync(
                $"/api/categories/{categoryId}/image");

            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);

            Assert.False(File.Exists(filePath));

            using var scope = _factory.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

            var category = await db.Categories
                .AsNoTracking()
                .SingleAsync(c => c.Id == categoryId);

            Assert.Null(category.ImageUrl);
        }
        finally
        {
            DeleteTestFile(filePath);
        }
    }

    [Fact]
    public async Task Delete_AdminUserWithoutExistingImage_ReturnsNoContent()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("Admin");

        var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}/image");

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<StoreDbContext>();

        var category = await db.Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == categoryId);

        Assert.Null(category.ImageUrl);
    }

    [Fact]
    public async Task Delete_NonExistingCategory_ReturnsNotFound()
    {
        using var client = CreateAuthenticatedClient("Admin");

        var response = await client.DeleteAsync(
            "/api/categories/999999/image");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExternalImageUrl_ClearsUrlWithoutDeletingExternalFile()
    {
        const string externalUrl =
            "https://example.com/category-image.jpg";

        int categoryId = await CreateCategoryAsync(externalUrl);

        using var client = CreateAuthenticatedClient("Admin");

        var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}/image");

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<StoreDbContext>();

        var category = await db.Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == categoryId);

        Assert.Null(category.ImageUrl);
    }

    [Fact]
    public async Task Delete_AnonymousUser_ReturnsUnauthorized()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}/image");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Delete_RegularUser_ReturnsForbidden()
    {
        int categoryId = await CreateCategoryAsync();

        using var client = CreateAuthenticatedClient("User");

        var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}/image");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    // ================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ================================================

    private async Task<int> CreateCategoryAsync(
        string? imageUrl = null)
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<StoreDbContext>();

        var category = new Category
        {
            Name = $"CategoryImageTest_{Guid.NewGuid():N}",
            ImageUrl = imageUrl
        };

        db.Categories.Add(category);

        await db.SaveChangesAsync();

        return category.Id;
    }

    private HttpClient CreateAuthenticatedClient(string role)
    {
        var client = _factory.CreateClient();

        string token = CreateJwtToken(role);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private string CreateJwtToken(string role)
    {
        var configuration = _factory.Services
            .GetRequiredService<IConfiguration>();

        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
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
                "category-image-integration-test"),

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

    private static byte[] GetValidPngSignature()
    {
        return
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ];
    }

    private static byte[] GetValidJpegSignature()
    {
        return
        [
            0xFF, 0xD8, 0xFF, 0xE0,
            0x00, 0x10, 0x4A, 0x46,
            0x49, 0x46, 0x00, 0x01
        ];
    }

    private string GetImagesDirectory()
    {
        var environment = _factory.Services
            .GetRequiredService<IWebHostEnvironment>();

        return Path.Combine(
            environment.ContentRootPath,
            "CategoryImages");
    }

    private string GetImagePath(string imageUrl)
    {
        Assert.StartsWith(ImageUrlPrefix, imageUrl);

        string fileName = imageUrl[ImageUrlPrefix.Length..];

        Assert.Equal(fileName, Path.GetFileName(fileName));

        return Path.Combine(
            GetImagesDirectory(),
            fileName);
    }

    private static void DeleteTestFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
