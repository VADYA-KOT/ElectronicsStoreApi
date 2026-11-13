
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class ProductImageServiceTests
{
    // ==================================================
    // UPLOAD: УСПЕШНАЯ ЗАГРУЗКА
    // ==================================================

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.png")]
    [InlineData("photo.webp")]
    [InlineData("photo.PNG")]
    public async Task UploadAsync_ValidImage_SavesFileAndUpdatesProduct(
        string fileName)
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        byte[] content = CreateValidImageBytes(extension);

        var file = new TestImageFile(fileName, content);

        var result = await fixture.Service.UploadAsync(
            product.Id,
            file);

        Assert.Equal(ProductImageServiceStatus.Success, result.Status);

        string imageUrl = Assert.IsType<string>(result.ImageUrl);

        Assert.StartsWith("/uploads/products/", imageUrl);
        Assert.EndsWith(extension, imageUrl);

        Assert.Equal(imageUrl, product.ImageUrl);

        string physicalPath = Path.Combine(
            fixture.ImagesDirectory,
            Path.GetFileName(imageUrl));

        Assert.True(File.Exists(physicalPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(physicalPath));
    }

    // ==================================================
    // UPLOAD: НЕКОРРЕКТНЫЕ ФАЙЛЫ
    // ==================================================

    [Fact]
    public async Task UploadAsync_NullFile_ReturnsInvalidFile()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        var result = await fixture.Service.UploadAsync(
            product.Id,
            null);

        Assert.Equal(
            ProductImageServiceStatus.InvalidFile,
            result.Status);

        Assert.Null(product.ImageUrl);
    }

    [Fact]
    public async Task UploadAsync_EmptyFile_ReturnsInvalidFile()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        var file = new TestImageFile(
            "empty.jpg",
            Array.Empty<byte>());

        var result = await fixture.Service.UploadAsync(
            product.Id,
            file);

        Assert.Equal(
            ProductImageServiceStatus.InvalidFile,
            result.Status);

        Assert.Null(product.ImageUrl);
    }

    [Fact]
    public async Task UploadAsync_FileTooLarge_ReturnsFileTooLarge()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        var file = new TestImageFile(
            "large.jpg",
            CreateValidImageBytes(".jpg"),
            ProductImageService.MaxFileSize + 1);

        var result = await fixture.Service.UploadAsync(
            product.Id,
            file);

        Assert.Equal(
            ProductImageServiceStatus.FileTooLarge,
            result.Status);

        Assert.Null(product.ImageUrl);
        Assert.Empty(Directory.GetFiles(fixture.ImagesDirectory));
    }

    [Fact]
    public async Task UploadAsync_ProductDoesNotExist_ReturnsProductNotFound()
    {
        using var fixture = new ProductImageTestFixture();

        var file = new TestImageFile(
            "photo.jpg",
            CreateValidImageBytes(".jpg"));

        var result = await fixture.Service.UploadAsync(
            9999,
            file);

        Assert.Equal(
            ProductImageServiceStatus.ProductNotFound,
            result.Status);
    }

    [Fact]
    public async Task UploadAsync_UnsupportedExtension_ReturnsInvalidFile()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        var file = new TestImageFile(
            "photo.gif",
            CreateValidImageBytes(".jpg"));

        var result = await fixture.Service.UploadAsync(
            product.Id,
            file);

        Assert.Equal(
            ProductImageServiceStatus.InvalidFile,
            result.Status);

        Assert.Null(product.ImageUrl);
    }

    [Fact]
    public async Task UploadAsync_InvalidSignature_ReturnsInvalidFile()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        // Расширение PNG, но содержимое имеет сигнатуру JPEG.
        var file = new TestImageFile(
            "photo.png",
            CreateValidImageBytes(".jpg"));

        var result = await fixture.Service.UploadAsync(
            product.Id,
            file);

        Assert.Equal(
            ProductImageServiceStatus.InvalidFile,
            result.Status);

        Assert.Null(product.ImageUrl);
        Assert.Empty(Directory.GetFiles(fixture.ImagesDirectory));
    }

    [Fact]
    public async Task UploadAsync_SignatureTooShort_ReturnsInvalidFile()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        // Для PNG требуется минимум 8 байт сигнатуры.
        var file = new TestImageFile(
            "photo.png",
            new byte[] { 0x89, 0x50, 0x4E });

        var result = await fixture.Service.UploadAsync(
            product.Id,
            file);

        Assert.Equal(
            ProductImageServiceStatus.InvalidFile,
            result.Status);
    }

    // ==================================================
    // UPLOAD: ЗАМЕНА ИЗОБРАЖЕНИЯ
    // ==================================================

    [Fact]
    public async Task UploadAsync_ReplaceExistingImage_DeletesOldFile()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        var firstFile = new TestImageFile(
            "first.jpg",
            CreateValidImageBytes(".jpg"));

        var firstResult = await fixture.Service.UploadAsync(
            product.Id,
            firstFile);

        Assert.Equal(
            ProductImageServiceStatus.Success,
            firstResult.Status);

        string firstUrl = Assert.IsType<string>(firstResult.ImageUrl);

        string firstPath = Path.Combine(
            fixture.ImagesDirectory,
            Path.GetFileName(firstUrl));

        Assert.True(File.Exists(firstPath));

        var secondFile = new TestImageFile(
            "second.png",
            CreateValidImageBytes(".png"));

        var secondResult = await fixture.Service.UploadAsync(
            product.Id,
            secondFile);

        Assert.Equal(
            ProductImageServiceStatus.Success,
            secondResult.Status);

        string secondUrl = Assert.IsType<string>(secondResult.ImageUrl);

        string secondPath = Path.Combine(
            fixture.ImagesDirectory,
            Path.GetFileName(secondUrl));

        Assert.NotEqual(firstUrl, secondUrl);

        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));

        Assert.Equal(secondUrl, product.ImageUrl);
    }

    // ==================================================
    // DELETE: УДАЛЕНИЕ ИЗОБРАЖЕНИЯ
    // ==================================================

    [Fact]
    public async Task DeleteAsync_ExistingImage_DeletesFileAndClearsImageUrl()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        var file = new TestImageFile(
            "photo.jpg",
            CreateValidImageBytes(".jpg"));

        var uploadResult = await fixture.Service.UploadAsync(
            product.Id,
            file);

        Assert.Equal(
            ProductImageServiceStatus.Success,
            uploadResult.Status);

        string imageUrl = Assert.IsType<string>(uploadResult.ImageUrl);

        string physicalPath = Path.Combine(
            fixture.ImagesDirectory,
            Path.GetFileName(imageUrl));

        Assert.True(File.Exists(physicalPath));

        var deleteResult = await fixture.Service.DeleteAsync(product.Id);

        Assert.Equal(
            ProductImageServiceStatus.Success,
            deleteResult);

        Assert.Null(product.ImageUrl);
        Assert.False(File.Exists(physicalPath));
    }

    [Fact]
    public async Task DeleteAsync_ProductDoesNotExist_ReturnsProductNotFound()
    {
        using var fixture = new ProductImageTestFixture();

        var result = await fixture.Service.DeleteAsync(9999);

        Assert.Equal(
            ProductImageServiceStatus.ProductNotFound,
            result);
    }

    [Fact]
    public async Task DeleteAsync_ProductWithoutImage_ReturnsSuccess()
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        var result = await fixture.Service.DeleteAsync(product.Id);

        Assert.Equal(
            ProductImageServiceStatus.Success,
            result);

        Assert.Null(product.ImageUrl);
    }

    // ==================================================
    // DELETE: ЗАЩИТА ОТ НЕКОРРЕКТНОГО IMAGE URL
    // ==================================================

    [Theory]
    [InlineData("/uploads/products/../outside.txt")]
    [InlineData("/uploads/categories/outside.txt")]
    public async Task DeleteAsync_InvalidImageUrl_DoesNotDeleteOutsideFile(
        string imageUrl)
    {
        using var fixture = new ProductImageTestFixture();

        var product = await fixture.CreateProductAsync();

        // Создаём файл за пределами директории ProductImages.
        string outsidePath = Path.Combine(
            fixture.ContentRootPath,
            "outside.txt");

        await File.WriteAllTextAsync(outsidePath, "Important data");

        product.ImageUrl = imageUrl;

        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.DeleteAsync(product.Id);

        Assert.Equal(
            ProductImageServiceStatus.Success,
            result);

        Assert.Null(product.ImageUrl);

        // Некорректный URL не должен привести к удалению файла.
        Assert.True(File.Exists(outsidePath));

        Assert.Equal(
            "Important data",
            await File.ReadAllTextAsync(outsidePath));
    }

    // ==================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ==================================================

    private static byte[] CreateValidImageBytes(string extension)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => new byte[]
            {
                0xFF, 0xD8, 0xFF, 0xE0,
                0x00, 0x10
            },

            ".png" => new byte[]
            {
                0x89, 0x50, 0x4E, 0x47,
                0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x01
            },

            ".webp" => new byte[]
            {
                0x52, 0x49, 0x46, 0x46,
                0x00, 0x00, 0x00, 0x00,
                0x57, 0x45, 0x42, 0x50
            },

            _ => throw new ArgumentException(
                $"Неизвестное расширение: {extension}")
        };
    }

    // ==================================================
    // ТЕСТОВАЯ ФОРМА ФАЙЛА
    // ==================================================

    private sealed class TestImageFile : IFormFile
    {
        private readonly byte[] _content;

        public TestImageFile(
            string fileName,
            byte[] content,
            long? declaredLength = null)
        {
            FileName = fileName;
            _content = content;

            Length = declaredLength ?? content.Length;
        }

        public string ContentDisposition => string.Empty;

        public string ContentType => "application/octet-stream";

        public IHeaderDictionary Headers { get; set; } =
            new HeaderDictionary();

        public long Length { get; }

        public string Name => "file";

        public string FileName { get; }

        public Stream OpenReadStream()
        {
            // Каждый вызов возвращает отдельный поток.
            return new MemoryStream(_content, writable: false);
        }

        public void CopyTo(Stream target)
        {
            using var input = OpenReadStream();
            input.CopyTo(target);
        }

        public async Task CopyToAsync(
            Stream target,
            CancellationToken cancellationToken = default)
        {
            await using var input = OpenReadStream();

            await input.CopyToAsync(
                target,
                cancellationToken);
        }
    }

    // ==================================================
    // ТЕСТОВАЯ СРЕДА
    // ==================================================

    private sealed class ProductImageTestFixture : IDisposable
    {
        public string ContentRootPath { get; }

        public string ImagesDirectory { get; }

        public StoreDbContext Context { get; }

        public ProductImageService Service { get; }

        public ProductImageTestFixture()
        {
            ContentRootPath = Path.Combine(
                Path.GetTempPath(),
                $"ProductImageTests_{Guid.NewGuid():N}");

            Directory.CreateDirectory(ContentRootPath);

            ImagesDirectory = Path.Combine(
                ContentRootPath,
                "ProductImages");

            var options =
                new DbContextOptionsBuilder<StoreDbContext>()
                    .UseInMemoryDatabase(
                        $"ProductImageTests_{Guid.NewGuid():N}")
                    .Options;

            Context = new StoreDbContext(options);

            var environment = new FakeWebHostEnvironment(
                ContentRootPath);

            Service = new ProductImageService(
                Context,
                environment,
                NullLogger<ProductImageService>.Instance);
        }

        public async Task<Product> CreateProductAsync()
        {
            var product = new Product
            {
                Name = "Image Test Product",
                Price = 100m,
                Quantity = 5
            };

            Context.Products.Add(product);

            await Context.SaveChangesAsync();

            return product;
        }

        public void Dispose()
        {
            Context.Dispose();

            if (Directory.Exists(ContentRootPath))
            {
                Directory.Delete(ContentRootPath, recursive: true);
            }
        }
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
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

        public FakeWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }
    }
}
