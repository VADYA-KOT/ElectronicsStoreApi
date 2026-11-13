
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace ElectronicsStoreApi.Tests.Services;

public class CategoryImageServiceTests
{
    private const string ImageUrlPrefix = "/uploads/categories/";

    // ================================================
    // УСПЕШНАЯ ЗАГРУЗКА ИЗОБРАЖЕНИЙ
    // ================================================

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
            await TestFixture.CreateAsync(CreateCategory());

        byte[] content = GetImageSignature(format);
        IFormFile file = CreateFile(fileName, content);

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(CategoryImageServiceStatus.Success, result.Status);
        Assert.NotNull(result.ImageUrl);
        Assert.StartsWith(ImageUrlPrefix, result.ImageUrl);

        var savedCategory = await fixture.Context.Categories
            .AsNoTracking()
            .FirstAsync(c => c.Id == 1);

        Assert.Equal(result.ImageUrl, savedCategory.ImageUrl);

        string savedFileName =
            result.ImageUrl![ImageUrlPrefix.Length..];

        string savedFilePath = Path.Combine(
            fixture.ImagesDirectory,
            savedFileName);

        Assert.True(File.Exists(savedFilePath));

        Assert.Equal(
            content,
            await File.ReadAllBytesAsync(savedFilePath));
    }

    // ================================================
    // ПРОВЕРКА ФОРМАТА И СИГНАТУРЫ
    // ================================================

    [Fact]
    public async Task UploadAsync_InvalidSignature_ReturnsInvalidFile()
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateCategory());

        IFormFile file = CreateFile(
            "photo.jpg",
            GetImageSignature("png"));

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            CategoryImageServiceStatus.InvalidFile,
            result.Status);

        var category = await fixture.Context.Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == 1);

        Assert.Null(category.ImageUrl);

        Assert.Empty(
            Directory.GetFiles(fixture.ImagesDirectory));
    }

    [Fact]
    public async Task UploadAsync_UnsupportedExtension_ReturnsInvalidFile()
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateCategory());

        IFormFile file = CreateFile(
            "photo.gif",
            GetImageSignature("png"));

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            CategoryImageServiceStatus.InvalidFile,
            result.Status);

        Assert.Empty(
            Directory.GetFiles(fixture.ImagesDirectory));
    }

    [Fact]
    public async Task UploadAsync_EmptyFile_ReturnsInvalidFile()
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateCategory());

        IFormFile file = CreateFile("photo.jpg", []);

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            CategoryImageServiceStatus.InvalidFile,
            result.Status);

        Assert.Empty(
            Directory.GetFiles(fixture.ImagesDirectory));
    }

    [Fact]
    public async Task UploadAsync_FileTooLarge_ReturnsFileTooLarge()
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateCategory());

        int fileSize = checked(
            (int)CategoryImageService.MaxFileSize + 1);

        IFormFile file = CreateFile(
            "photo.jpg",
            new byte[fileSize]);

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            CategoryImageServiceStatus.FileTooLarge,
            result.Status);

        Assert.Empty(
            Directory.GetFiles(fixture.ImagesDirectory));
    }

    [Fact]
    public async Task UploadAsync_CategoryNotFound_ReturnsCategoryNotFound()
    {
        await using var fixture =
            await TestFixture.CreateAsync();

        IFormFile file = CreateFile(
            "photo.jpg",
            GetImageSignature("jpeg"));

        var result = await fixture.Service.UploadAsync(999, file);

        Assert.Equal(
            CategoryImageServiceStatus.CategoryNotFound,
            result.Status);

        Assert.Empty(
            Directory.GetFiles(fixture.ImagesDirectory));
    }

    // ================================================
    // ЗАМЕНА СТАРОГО ИЗОБРАЖЕНИЯ
    // ================================================

    [Fact]
    public async Task UploadAsync_ReplaceImage_DeletesOldFileAndSavesNewFile()
    {
        const string oldFileName = "old-image.jpg";

        string oldImageUrl =
            $"{ImageUrlPrefix}{oldFileName}";

        await using var fixture =
            await TestFixture.CreateAsync(
                CreateCategory(oldImageUrl));

        string oldFilePath = Path.Combine(
            fixture.ImagesDirectory,
            oldFileName);

        await File.WriteAllBytesAsync(
            oldFilePath,
            GetImageSignature("jpeg"));

        byte[] newContent = GetImageSignature("png");

        IFormFile file = CreateFile(
            "new-image.png",
            newContent);

        var result = await fixture.Service.UploadAsync(1, file);

        Assert.Equal(
            CategoryImageServiceStatus.Success,
            result.Status);

        Assert.NotNull(result.ImageUrl);
        Assert.NotEqual(oldImageUrl, result.ImageUrl);

        Assert.False(File.Exists(oldFilePath));

        string newFileName =
            result.ImageUrl![ImageUrlPrefix.Length..];

        string newFilePath = Path.Combine(
            fixture.ImagesDirectory,
            newFileName);

        Assert.True(File.Exists(newFilePath));

        Assert.Equal(
            newContent,
            await File.ReadAllBytesAsync(newFilePath));

        var category = await fixture.Context.Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == 1);

        Assert.Equal(result.ImageUrl, category.ImageUrl);
    }

    // ================================================
    // ОШИБКА СОХРАНЕНИЯ БД ПРИ ЗАГРУЗКЕ
    // ================================================

    [Fact]
    public async Task UploadAsync_DatabaseSaveFails_DeletesNewFileAndPreservesOldImage()
    {
        const string oldFileName = "old-image.jpg";

        string oldImageUrl =
            $"{ImageUrlPrefix}{oldFileName}";

        var saveInterceptor = new FailingSaveChangesInterceptor();

        await using var fixture =
            await TestFixture.CreateAsync(
                CreateCategory(oldImageUrl),
                saveInterceptor);

        string oldFilePath = Path.Combine(
            fixture.ImagesDirectory,
            oldFileName);

        await File.WriteAllBytesAsync(
            oldFilePath,
            GetImageSignature("jpeg"));

        // Включаем искусственную ошибку только после подготовки БД.
        saveInterceptor.ShouldFail = true;

        IFormFile file = CreateFile(
            "new-image.png",
            GetImageSignature("png"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.UploadAsync(1, file));

        // Старый файл должен остаться.
        Assert.True(File.Exists(oldFilePath));

        // Новый файл должен быть удалён.
        Assert.Single(
            Directory.GetFiles(fixture.ImagesDirectory));

        var category = await fixture.Context.Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == 1);

        Assert.Equal(oldImageUrl, category.ImageUrl);
    }

    // ================================================
    // УДАЛЕНИЕ ИЗОБРАЖЕНИЯ
    // ================================================

    [Fact]
    public async Task DeleteAsync_ExistingImage_DeletesFileAndClearsImageUrl()
    {
        const string fileName = "category-image.jpg";

        string imageUrl = $"{ImageUrlPrefix}{fileName}";

        await using var fixture =
            await TestFixture.CreateAsync(
                CreateCategory(imageUrl));

        string filePath = Path.Combine(
            fixture.ImagesDirectory,
            fileName);

        await File.WriteAllBytesAsync(
            filePath,
            GetImageSignature("jpeg"));

        var result = await fixture.Service.DeleteAsync(1);

        Assert.Equal(
            CategoryImageServiceStatus.Success,
            result);

        Assert.False(File.Exists(filePath));

        var category = await fixture.Context.Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == 1);

        Assert.Null(category.ImageUrl);
    }

    [Fact]
    public async Task DeleteAsync_CategoryNotFound_ReturnsCategoryNotFound()
    {
        await using var fixture =
            await TestFixture.CreateAsync();

        var result = await fixture.Service.DeleteAsync(999);

        Assert.Equal(
            CategoryImageServiceStatus.CategoryNotFound,
            result);
    }

    [Fact]
    public async Task DeleteAsync_WithoutImage_ReturnsSuccess()
    {
        await using var fixture =
            await TestFixture.CreateAsync(CreateCategory());

        var result = await fixture.Service.DeleteAsync(1);

        Assert.Equal(
            CategoryImageServiceStatus.Success,
            result);

        var category = await fixture.Context.Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == 1);

        Assert.Null(category.ImageUrl);

        Assert.Empty(
            Directory.GetFiles(fixture.ImagesDirectory));
    }

    [Fact]
    public async Task DeleteAsync_DatabaseSaveFails_PreservesImageAndImageUrl()
    {
        const string fileName = "category-image.jpg";

        string imageUrl = $"{ImageUrlPrefix}{fileName}";

        var saveInterceptor = new FailingSaveChangesInterceptor();

        await using var fixture =
            await TestFixture.CreateAsync(
                CreateCategory(imageUrl),
                saveInterceptor);

        string filePath = Path.Combine(
            fixture.ImagesDirectory,
            fileName);

        await File.WriteAllBytesAsync(
            filePath,
            GetImageSignature("jpeg"));

        saveInterceptor.ShouldFail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.DeleteAsync(1));

        // Файл не должен удалиться до успешной записи в БД.
        Assert.True(File.Exists(filePath));

        var category = await fixture.Context.Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == 1);

        Assert.Equal(imageUrl, category.ImageUrl);
    }

    // ================================================
    // БЕЗОПАСНОЕ УДАЛЕНИЕ ФАЙЛОВ
    // ================================================

    [Fact]
    public async Task DeleteStoredImage_ValidImageUrl_DeletesFile()
    {
        const string fileName = "stored-image.png";

        await using var fixture =
            await TestFixture.CreateAsync();

        string filePath = Path.Combine(
            fixture.ImagesDirectory,
            fileName);

        await File.WriteAllBytesAsync(
            filePath,
            GetImageSignature("png"));

        fixture.Service.DeleteStoredImage(
            $"{ImageUrlPrefix}{fileName}");

        Assert.False(File.Exists(filePath));
    }

    [Theory]
    [InlineData("https://example.com/image.jpg")]
    [InlineData("/uploads/products/image.jpg")]
    [InlineData("/other/image.jpg")]
    [InlineData("")]
    [InlineData(null)]
    public async Task DeleteStoredImage_ExternalOrInvalidUrl_DoesNotDeleteFiles(
        string? imageUrl)
    {
        const string fileName = "keep-image.png";

        await using var fixture =
            await TestFixture.CreateAsync();

        string filePath = Path.Combine(
            fixture.ImagesDirectory,
            fileName);

        await File.WriteAllBytesAsync(
            filePath,
            GetImageSignature("png"));

        fixture.Service.DeleteStoredImage(imageUrl);

        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteStoredImage_PathTraversal_DoesNotDeleteOutsideFile()
    {
        await using var fixture =
            await TestFixture.CreateAsync();

        string outsideFilePath = Path.Combine(
            fixture.ContentRootPath,
            "outside-image.png");

        await File.WriteAllBytesAsync(
            outsideFilePath,
            GetImageSignature("png"));

        fixture.Service.DeleteStoredImage(
            $"{ImageUrlPrefix}../outside-image.png");

        Assert.True(File.Exists(outsideFilePath));
    }

    // ================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ================================================

    private static Category CreateCategory(string? imageUrl = null)
    {
        return new Category
        {
            Id = 1,
            Name = "Test category",
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

    // ================================================
    // ТЕСТОВАЯ СРЕДА
    // ================================================

    private sealed class TestFixture : IAsyncDisposable
    {
        public StoreDbContext Context { get; }

        public CategoryImageService Service { get; }

        public string ContentRootPath { get; }

        public string ImagesDirectory { get; }

        public FailingSaveChangesInterceptor? SaveInterceptor { get; }

        private TestFixture(
            StoreDbContext context,
            CategoryImageService service,
            string contentRootPath,
            FailingSaveChangesInterceptor? saveInterceptor)
        {
            Context = context;
            Service = service;
            ContentRootPath = contentRootPath;
            SaveInterceptor = saveInterceptor;

            ImagesDirectory = Path.Combine(
                contentRootPath,
                "CategoryImages");
        }

        public static async Task<TestFixture> CreateAsync(
            Category? category = null,
            FailingSaveChangesInterceptor? saveInterceptor = null)
        {
            string contentRootPath = Path.Combine(
                Path.GetTempPath(),
                "ElectronicsStoreApiTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(contentRootPath);

            var optionsBuilder =
                new DbContextOptionsBuilder<StoreDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString());

            if (saveInterceptor is not null)
            {
                optionsBuilder.AddInterceptors(saveInterceptor);
            }

            var context = new StoreDbContext(optionsBuilder.Options);

            if (category is not null)
            {
                context.Categories.Add(category);
                await context.SaveChangesAsync();
            }

            var environment =
                new TestWebHostEnvironment(contentRootPath);

            var service = new CategoryImageService(
                context,
                environment,
                NullLogger<CategoryImageService>.Instance);

            return new TestFixture(
                context,
                service,
                contentRootPath,
                saveInterceptor);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();

            if (Directory.Exists(ContentRootPath))
            {
                Directory.Delete(
                    ContentRootPath,
                    recursive: true);
            }
        }
    }

    private sealed class FailingSaveChangesInterceptor
        : SaveChangesInterceptor
    {
        public bool ShouldFail { get; set; }

        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new InvalidOperationException(
                    "Simulated database save failure.");
            }

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }

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
