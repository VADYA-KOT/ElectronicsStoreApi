
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class CategoryImagesApiIntegrationTests
{
    private const string Password = "TestPassword_12345";

    private static readonly byte[] ValidJpeg =
    {
        0xFF, 0xD8, 0xFF, 0xE0,
        0x00, 0x10, 0x20, 0x30
    };

    private static readonly byte[] ValidPng =
    {
        0x89, 0x50, 0x4E, 0x47,
        0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x01
    };

    // ==================================================
    // POST /api/categories/{categoryId}/image
    // ==================================================

    [Fact]
    public async Task Upload_ValidImage_ReturnsOkAndSavesImage()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int categoryId = await CreateCategoryAsync(factory);

        string? imageUrl = null;

        try
        {
            using var content = CreateImageContent(
                "category.jpg",
                ValidJpeg);

            using var response = await client.PostAsync(
                $"/api/categories/{categoryId}/image",
                content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            imageUrl = json.RootElement
                .GetProperty("imageUrl")
                .GetString();

            Assert.False(string.IsNullOrWhiteSpace(imageUrl));
            Assert.StartsWith("/uploads/categories/", imageUrl);
            Assert.EndsWith(".jpg", imageUrl);

            // Проверяем URL в БД.
            using var scope = factory.Services.CreateScope();

            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            string? savedImageUrl = await context.Categories
                .Where(c => c.Id == categoryId)
                .Select(c => c.ImageUrl)
                .SingleAsync();

            Assert.Equal(imageUrl, savedImageUrl);

            // Проверяем физический файл.
            string physicalPath = GetPhysicalImagePath(
                factory,
                imageUrl);

            Assert.True(File.Exists(physicalPath));

            Assert.Equal(
                ValidJpeg,
                await File.ReadAllBytesAsync(physicalPath));
        }
        finally
        {
            DeleteGeneratedImage(factory, imageUrl);
        }
    }

    [Fact]
    public async Task Upload_WithoutFile_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int categoryId = await CreateCategoryAsync(factory);

        using var content = new MultipartFormDataContent();

        using var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_InvalidFileSignature_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int categoryId = await CreateCategoryAsync(factory);

        // Расширение JPEG, но содержимое имеет сигнатуру PNG.
        using var content = CreateImageContent(
            "category.jpg",
            ValidPng);

        using var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_UnsupportedExtension_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int categoryId = await CreateCategoryAsync(factory);

        using var content = CreateImageContent(
            "category.gif",
            ValidJpeg);

        using var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_FileTooLarge_ReturnsPayloadTooLarge()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int categoryId = await CreateCategoryAsync(factory);

        byte[] largeContent =
            new byte[CategoryImageService.MaxFileSize + 1];

        Array.Copy(
            ValidJpeg,
            largeContent,
            ValidJpeg.Length);

        using var content = CreateImageContent(
            "large.jpg",
            largeContent);

        using var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_CategoryDoesNotExist_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        using var content = CreateImageContent(
            "category.jpg",
            ValidJpeg);

        using var response = await client.PostAsync(
            "/api/categories/9999/image",
            content);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ==================================================
    // POST: ЗАМЕНА ИЗОБРАЖЕНИЯ
    // ==================================================

    [Fact]
    public async Task Upload_ReplaceExistingImage_DeletesOldFile()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int categoryId = await CreateCategoryAsync(factory);

        string? firstImageUrl = null;
        string? secondImageUrl = null;

        try
        {
            firstImageUrl = await UploadImageAsync(
                client,
                categoryId,
                "first.jpg",
                ValidJpeg);

            string firstPath = GetPhysicalImagePath(
                factory,
                firstImageUrl);

            Assert.True(File.Exists(firstPath));

            secondImageUrl = await UploadImageAsync(
                client,
                categoryId,
                "second.png",
                ValidPng);

            string secondPath = GetPhysicalImagePath(
                factory,
                secondImageUrl);

            Assert.NotEqual(firstImageUrl, secondImageUrl);

            Assert.False(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));

            using var scope = factory.Services.CreateScope();

            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            string? savedImageUrl = await context.Categories
                .Where(c => c.Id == categoryId)
                .Select(c => c.ImageUrl)
                .SingleAsync();

            Assert.Equal(secondImageUrl, savedImageUrl);
        }
        finally
        {
            DeleteGeneratedImage(factory, firstImageUrl);
            DeleteGeneratedImage(factory, secondImageUrl);
        }
    }

    // ==================================================
    // DELETE /api/categories/{categoryId}/image
    // ==================================================

    [Fact]
    public async Task Delete_ExistingImage_ReturnsNoContentAndDeletesFile()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int categoryId = await CreateCategoryAsync(factory);

        string? imageUrl = null;

        try
        {
            imageUrl = await UploadImageAsync(
                client,
                categoryId,
                "category.jpg",
                ValidJpeg);

            string physicalPath = GetPhysicalImagePath(
                factory,
                imageUrl);

            Assert.True(File.Exists(physicalPath));

            using var response = await client.DeleteAsync(
                $"/api/categories/{categoryId}/image");

            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);

            Assert.False(File.Exists(physicalPath));

            using var scope = factory.Services.CreateScope();

            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var category = await context.Categories
                .SingleAsync(c => c.Id == categoryId);

            Assert.Null(category.ImageUrl);

            // Удаление изображения не удаляет саму категорию.
            Assert.Equal(categoryId, category.Id);
        }
        finally
        {
            DeleteGeneratedImage(factory, imageUrl);
        }
    }

    [Fact]
    public async Task Delete_CategoryDoesNotExist_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        using var response = await client.DeleteAsync(
            "/api/categories/9999/image");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task Delete_CategoryWithoutImage_ReturnsNoContent()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int categoryId = await CreateCategoryAsync(factory);

        using var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}/image");

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        Assert.True(
            await context.Categories.AnyAsync(c => c.Id == categoryId));
    }

    // ==================================================
    // AUTHORIZATION
    // ==================================================

    [Fact]
    public async Task ImageEndpoints_RegularUser_ReturnsForbidden()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: false);

        using var content = CreateImageContent(
            "category.jpg",
            ValidJpeg);

        using var uploadResponse = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            uploadResponse.StatusCode);

        using var deleteResponse = await client.DeleteAsync(
            $"/api/categories/{categoryId}/image");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ImageEndpoints_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory();

        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();

        using var uploadResponse = await client.PostAsync(
            "/api/categories/1/image",
            content);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            uploadResponse.StatusCode);

        using var deleteResponse = await client.DeleteAsync(
            "/api/categories/1/image");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            deleteResponse.StatusCode);
    }

    // ==================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ==================================================

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        TestApplicationFactory factory,
        bool isAdmin)
    {
        var client = factory.CreateClient();

        string username = $"catimg_{Guid.NewGuid():N}";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username,
                password = Password
            });

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode);

        if (isAdmin)
        {
            using var scope = factory.Services.CreateScope();

            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var user = await context.AppUsers.SingleAsync(
                u => u.NormalizedUsername == username.ToUpperInvariant());

            user.Role = "Admin";

            await context.SaveChangesAsync();
        }

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username,
                password = Password
            });

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var token =
            await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        Assert.NotNull(token);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token.AccessToken);

        return client;
    }

    private static async Task<int> CreateCategoryAsync(
        TestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var category = new Category
        {
            Name = $"ImageIntegration_{Guid.NewGuid():N}"
        };

        context.Categories.Add(category);

        await context.SaveChangesAsync();

        return category.Id;
    }

    private static MultipartFormDataContent CreateImageContent(
        string fileName,
        byte[] imageBytes)
    {
        var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(imageBytes);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");

        content.Add(fileContent, "file", fileName);

        return content;
    }

    private static async Task<string> UploadImageAsync(
        HttpClient client,
        int categoryId,
        string fileName,
        byte[] imageBytes)
    {
        using var content = CreateImageContent(
            fileName,
            imageBytes);

        using var response = await client.PostAsync(
            $"/api/categories/{categoryId}/image",
            content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        string? imageUrl = json.RootElement
            .GetProperty("imageUrl")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(imageUrl));

        return imageUrl!;
    }

    private static string GetPhysicalImagePath(
        TestApplicationFactory factory,
        string imageUrl)
    {
        var environment =
            factory.Services.GetRequiredService<IWebHostEnvironment>();

        string fileName = Path.GetFileName(imageUrl);

        return Path.Combine(
            environment.ContentRootPath,
            "CategoryImages",
            fileName);
    }

    private static void DeleteGeneratedImage(
        TestApplicationFactory factory,
        string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        string path = GetPhysicalImagePath(
            factory,
            imageUrl);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
