
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace ElectronicsStoreApi.Tests.Services;

public class ProductImageServiceTests
{
    private const string ImageUrlPrefix = "/uploads/products/";

    // Проверяем загрузку всех поддерживаемых форматов.
    [Theory]
    [InlineData("photo.jpg", "jpeg")]
    [InlineData("photo.jpeg", "jpeg")]
    [InlineData("photo.png", "png")]
    [InlineData("photo.webp", "webp")]
    public async Task UploadAsync_ValidImage_SavesFileAndImageUrl(
        string fileName,
        string format)
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateProduct());

        byte[] content = GetImageSignature(format);
        IFormFile file = CreateFile(fileName, content);

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(ProductImageServiceStatus.Success, result.Status);
        Assert.NotNull(result.ImageUrl);
        Assert.StartsWith(ImageUrlPrefix, result.ImageUrl);

        var savedProduct = await fixture.Context.Products
            .AsNoTracking()
            .FirstAsync(p => p.Id == 1);

        Assert.Equal(result.ImageUrl, savedProduct.ImageUrl);

        string savedFileName =
            result.ImageUrl![ImageUrlPrefix.Length..];

        string savedFilePath = Path.Combine(
            fixture.ImagesDirectory,
            savedFileName);

        Assert.True(File.Exists(savedFilePath));
        Assert.Equal(content, await File.ReadAllBytesAsync(savedFilePath));
    }

    // Расширение правильное, но содержимое не соответствует формату.
    [Fact]
    public async Task UploadAsync_InvalidSignature_ReturnsInvalidFile()
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateProduct());

        byte[] pngSignature = GetImageSignature("png");
        IFormFile file = CreateFile("photo.jpg", pngSignature);

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            ProductImageServiceStatus.InvalidFile,
            result.Status);

        var product = await fixture.Context.Products
            .AsNoTracking()
            .FirstAsync(p => p.Id == 1);

        Assert.Null(product.ImageUrl);
        Assert.Empty(Directory.GetFiles(fixture.ImagesDirectory));
    }

    // Проверяем запрещённое расширение.
    [Fact]
    public async Task UploadAsync_UnsupportedExtension_ReturnsInvalidFile()
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateProduct());

        IFormFile file = CreateFile(
            "photo.gif",
            GetImageSignature("png"));

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            ProductImageServiceStatus.InvalidFile,
            result.Status);

        Assert.Empty(Directory.GetFiles(fixture.ImagesDirectory));
    }

    // Проверяем пустой файл.
    [Fact]
    public async Task UploadAsync_EmptyFile_ReturnsInvalidFile()
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateProduct());

        IFormFile file = CreateFile("photo.jpg", []);

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            ProductImageServiceStatus.InvalidFile,
            result.Status);
    }

    // Проверяем ограничение размера 5 МБ.
    [Fact]
    public async Task UploadAsync_FileTooLarge_ReturnsFileTooLarge()
    {
        await using var fixture =
            await TestFixture.CreateAsync();

        int fileSize = checked((int)ProductImageService.MaxFileSize + 1);

        IFormFile file = CreateFile(
            "photo.jpg",
            new byte[fileSize]);

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            ProductImageServiceStatus.FileTooLarge,
            result.Status);

        Assert.Empty(Directory.GetFiles(fixture.ImagesDirectory));
    }

    // Проверяем попытку загрузить фотографию
    // для несуществующего товара.
    [Fact]
    public async Task UploadAsync_ProductNotFound_ReturnsProductNotFound()
    {
        await using var fixture =
            await TestFixture.CreateAsync();

        IFormFile file = CreateFile(
            "photo.jpg",
            GetImageSignature("jpeg"));

        var result = await fixture.Service.UploadAsync(999, file);

        Assert.Equal(
            ProductImageServiceStatus.ProductNotFound,
            result.Status);

        Assert.Empty(Directory.GetFiles(fixture.ImagesDirectory));
    }

    // Проверяем замену старой фотографии.
    // Старая фотография должна удаляться только после сохранения новой.
    [Fact]
    public async Task UploadAsync_ReplaceImage_DeletesOldFileAndSavesNewFile()
    {
        const string oldFileName = "old-image.jpg";
        string oldImageUrl = $"{ImageUrlPrefix}{oldFileName}";

        await using var fixture =
            await TestFixture.CreateAsync(CreateProduct(oldImageUrl));

        string oldFilePath = Path.Combine(
            fixture.ImagesDirectory,
            oldFileName);

        await File.WriteAllBytesAsync(
            oldFilePath,
            GetImageSignature("jpeg"));

        IFormFile file = CreateFile(
            "new-image.png",
            GetImageSignature("png"));

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(ProductImageServiceStatus.Success, result.Status);
        Assert.NotNull(result.ImageUrl);
        Assert.NotEqual(oldImageUrl, result.ImageUrl);

        Assert.False(File.Exists(oldFilePath));

        string newFileName =
            result.ImageUrl![ImageUrlPrefix.Length..];

        string newFilePath = Path.Combine(
            fixture.ImagesDirectory,
            newFileName);

        Assert.True(File.Exists(newFilePath));

        var product = await fixture.Context.Products
            .AsNoTracking()
            .FirstAsync(p => p.Id == 1);

        Assert.Equal(result.ImageUrl, product.ImageUrl);
    }

    // Проверяем удаление фотографии.
    [Fact]
    public async Task DeleteAsync_ExistingImage_DeletesFileAndClearsImageUrl()
    {
        const string fileName = "product-image.jpg";
        string imageUrl = $"{ImageUrlPrefix}{fileName}";

        await using var fixture =
            await TestFixture.CreateAsync(CreateProduct(imageUrl));

        string filePath = Path.Combine(
            fixture.ImagesDirectory,
            fileName);

        await File.WriteAllBytesAsync(
            filePath,
            GetImageSignature("jpeg"));

        var result = await fixture.Service.DeleteAsync(1);

        Assert.Equal(
            ProductImageServiceStatus.Success,
            result);

        Assert.False(File.Exists(filePath));

        var product = await fixture.Context.Products
            .AsNoTracking()
            .FirstAsync(p => p.Id == 1);

        Assert.Null(product.ImageUrl);
    }

    // Проверяем удаление для несуществующего товара.
    [Fact]
    public async Task DeleteAsync_ProductNotFound_ReturnsProductNotFound()
    {
        await using var fixture =
            await TestFixture.CreateAsync();

        var result = await fixture.Service.DeleteAsync(999);

        Assert.Equal(
            ProductImageServiceStatus.ProductNotFound,
            result);
    }

    // Проверяем, что сервис не удаляет файлы
    // по внешним или посторонним URL.
    [Fact]
    public async Task DeleteAsync_ExternalImageUrl_DoesNotDeleteExternalFile()
    {
        const string externalUrl = "https://example.com/image.jpg";

        await using var fixture =
            await TestFixture.CreateAsync(CreateProduct(externalUrl));

        var result = await fixture.Service.DeleteAsync(1);

        Assert.Equal(
            ProductImageServiceStatus.Success,
            result);

        var product = await fixture.Context.Products
            .AsNoTracking()
            .FirstAsync(p => p.Id == 1);

        Assert.Null(product.ImageUrl);
    }

    private static Product CreateProduct(string? imageUrl = null)
    {
        return new Product
        {
            Id = 1,
            Name = "Test product",
            Price = 100m,
            Quantity = 5,
            ImageUrl = imageUrl
        };
    }

    private static IFormFile CreateFile(
        string fileName,
        byte[] content)
    {
        var stream = new MemoryStream(content);

        return new FormFile(
            stream,
            0,
            stream.Length,
            "file",
            fileName)
        {
            Headers = new HeaderDictionary()
        };
    }

    private static byte[] GetImageSignature(string format)
    {
        return format switch
        {
            "jpeg" =>
            [
                0xFF, 0xD8, 0xFF, 0xE0,
                0x00, 0x10, 0x4A, 0x46,
                0x49, 0x46, 0x00, 0x01
            ],

            "png" =>
            [
                0x89, 0x50, 0x4E, 0x47,
                0x0D, 0x0A, 0x1A, 0x0A
            ],

            "webp" =>
            [
                0x52, 0x49, 0x46, 0x46,
                0x00, 0x00, 0x00, 0x00,
                0x57, 0x45, 0x42, 0x50
            ],

            _ => throw new ArgumentException(
                "Неизвестный формат изображения.",
                nameof(format))
        };
    }

    // Отдельная тестовая среда для каждого теста.
    private sealed class TestFixture : IAsyncDisposable
    {
        public StoreDbContext Context { get; }

        public ProductImageService Service { get; }

        public string ContentRootPath { get; }

        public string ImagesDirectory { get; }

        private TestFixture(
            StoreDbContext context,
            ProductImageService service,
            string contentRootPath)
        {
            Context = context;
            Service = service;
            ContentRootPath = contentRootPath;

            ImagesDirectory = Path.Combine(
                contentRootPath,
                "ProductImages");
        }

        public static async Task<TestFixture> CreateAsync(
            Product? product = null)
        {
            string contentRootPath = Path.Combine(
                Path.GetTempPath(),
                "ElectronicsStoreApiTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(contentRootPath);

            var options = new DbContextOptionsBuilder<StoreDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var context = new StoreDbContext(options);

            if (product is not null)
            {
                context.Products.Add(product);
                await context.SaveChangesAsync();
            }

            var environment = new TestWebHostEnvironment(
                contentRootPath);

            var service = new ProductImageService(
                context,
                environment,
                NullLogger<ProductImageService>.Instance);

            return new TestFixture(
                context,
                service,
                contentRootPath);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();

            if (Directory.Exists(ContentRootPath))
            {
                Directory.Delete(ContentRootPath, recursive: true);
            }
        }
    }

    // Минимальная реализация IWebHostEnvironment для тестов.
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } =
            "ElectronicsStoreApi.Tests";

        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Testing";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();

        public TestWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }
    }
}
