using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ElectronicsStoreApi.Services;

namespace ElectronicsStoreApi.Tests;

public class ProductImagesApiIntegrationTests
{
    private const string Password = "TestPassword_12345";

    private static readonly byte[] ValidJpeg =
    {
        0xFF, 0xD8, 0xFF, 0xE0,
        0x00, 0x10, 0x20, 0x30
    };

    // ==================================================
    // POST /api/products/{productId}/image
    // ==================================================

    [Fact]
    public async Task Upload_ValidImage_ReturnsOkAndSavesImage()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int productId = await CreateProductAsync(client);

        string? imageUrl = null;

        try
        {
            using var content = CreateImageContent(
                "photo.jpg",
                ValidJpeg);

            using var response = await client.PostAsync(
                $"/api/products/{productId}/image",
                content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            imageUrl = json.RootElement
                .GetProperty("imageUrl")
                .GetString();

            Assert.False(string.IsNullOrWhiteSpace(imageUrl));
            Assert.StartsWith("/uploads/products/", imageUrl);
            Assert.EndsWith(".jpg", imageUrl);

            // Проверяем, что URL сохранён в БД.
            using var scope = factory.Services.CreateScope();

            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            string? savedImageUrl = await context.Products
                .Where(p => p.Id == productId)
                .Select(p => p.ImageUrl)
                .SingleAsync();

            Assert.Equal(imageUrl, savedImageUrl);

            // Проверяем наличие физического файла.
            string physicalPath = GetPhysicalImagePath(
                factory,
                imageUrl);

            Assert.True(File.Exists(physicalPath));
            Assert.Equal(ValidJpeg, await File.ReadAllBytesAsync(physicalPath));
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

        int productId = await CreateProductAsync(client);

        using var content = new MultipartFormDataContent();

        using var response = await client.PostAsync(
            $"/api/products/{productId}/image",
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

        int productId = await CreateProductAsync(client);

        // Расширение JPEG, но содержимое не является JPEG.
        byte[] invalidContent =
        {
            0x00, 0x01, 0x02, 0x03,
            0x04, 0x05, 0x06, 0x07
        };

        using var content = CreateImageContent(
            "photo.jpg",
            invalidContent);

        using var response = await client.PostAsync(
            $"/api/products/{productId}/image",
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

        int productId = await CreateProductAsync(client);

        using var content = CreateImageContent(
            "photo.gif",
            ValidJpeg);

        using var response = await client.PostAsync(
            $"/api/products/{productId}/image",
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

        int productId = await CreateProductAsync(client);

        byte[] largeContent =
            new byte[ProductImageService.MaxFileSize + 1];

        // Оставляем корректную JPEG-сигнатуру.
        Array.Copy(ValidJpeg, largeContent, ValidJpeg.Length);

        using var content = CreateImageContent(
            "large.jpg",
            largeContent);

        using var response = await client.PostAsync(
            $"/api/products/{productId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
    }

    [Fact]
    public async Task Upload_ProductDoesNotExist_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        using var content = CreateImageContent(
            "photo.jpg",
            ValidJpeg);

        using var response = await client.PostAsync(
            "/api/products/9999/image",
            content);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ==================================================
    // DELETE /api/products/{productId}/image
    // ==================================================

    [Fact]
    public async Task Delete_ExistingImage_ReturnsNoContentAndDeletesFile()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int productId = await CreateProductAsync(client);

        string? imageUrl = null;

        try
        {
            imageUrl = await UploadValidImageAsync(
                client,
                productId);

            string physicalPath = GetPhysicalImagePath(
                factory,
                imageUrl);

            Assert.True(File.Exists(physicalPath));

            using var response = await client.DeleteAsync(
                $"/api/products/{productId}/image");

            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);

            Assert.False(File.Exists(physicalPath));

            // Проверяем, что URL удалён из БД.
            using var scope = factory.Services.CreateScope();

            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            string? savedImageUrl = await context.Products
                .Where(p => p.Id == productId)
                .Select(p => p.ImageUrl)
                .SingleAsync();

            Assert.Null(savedImageUrl);
        }
        finally
        {
            DeleteGeneratedImage(factory, imageUrl);
        }
    }

    [Fact]
    public async Task Delete_ProductDoesNotExist_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        using var response = await client.DeleteAsync(
            "/api/products/9999/image");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ==================================================
    // AUTHORIZATION
    // ==================================================

    [Fact]
    public async Task ImageEndpoints_RegularUser_ReturnsForbidden()
    {
        using var factory = new TestApplicationFactory();

        using var adminClient = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: true);

        int productId = await CreateProductAsync(adminClient);

        using var userClient = await CreateAuthenticatedClientAsync(
            factory,
            isAdmin: false);

        using var content = CreateImageContent(
            "photo.jpg",
            ValidJpeg);

        using var uploadResponse = await userClient.PostAsync(
            $"/api/products/{productId}/image",
            content);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            uploadResponse.StatusCode);

        using var deleteResponse = await userClient.DeleteAsync(
            $"/api/products/{productId}/image");

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
            "/api/products/1/image",
            content);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            uploadResponse.StatusCode);

        using var deleteResponse = await client.DeleteAsync(
            "/api/products/1/image");

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

        string username = $"image_{Guid.NewGuid():N}";

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

    private static async Task<int> CreateProductAsync(HttpClient client)
    {
        var request = new
        {
            name = "Product Image Integration Test",
            price = 2500,
            quantity = 5,
            categoryId = (int?)null,
            description = "Image controller integration test"
        };

        using var response = await client.PostAsJsonAsync(
            "/api/products",
            request);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var product =
            await response.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.NotNull(product);
        Assert.True(product.Id > 0);

        return product.Id;
    }

    private static MultipartFormDataContent CreateImageContent(
        string fileName,
        byte[] imageBytes)
    {
        var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(imageBytes);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("image/jpeg");

        content.Add(fileContent, "file", fileName);

        return content;
    }

    private static async Task<string> UploadValidImageAsync(
        HttpClient client,
        int productId)
    {
        using var content = CreateImageContent(
            "photo.jpg",
            ValidJpeg);

        using var response = await client.PostAsync(
            $"/api/products/{productId}/image",
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
            "ProductImages",
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

        string path = GetPhysicalImagePath(factory, imageUrl);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}