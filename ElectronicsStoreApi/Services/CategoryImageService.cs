using ElectronicsStoreApi.Data;
using Microsoft.EntityFrameworkCore;

namespace ElectronicsStoreApi.Services;

public enum CategoryImageServiceStatus
{
    Success,
    CategoryNotFound,
    InvalidFile,
    FileTooLarge
}

public sealed record CategoryImageServiceResult(
    CategoryImageServiceStatus Status,
    string? ImageUrl = null);

public class CategoryImageService
{
    public const long MaxFileSize = 5 * 1024 * 1024;

    private const string ImageUrlPrefix = "/uploads/categories/";

    private readonly StoreDbContext _context;
    private readonly string _imagesDirectory;
    private readonly ILogger<CategoryImageService> _logger;

    public CategoryImageService(
        StoreDbContext context,
        IWebHostEnvironment environment,
        ILogger<CategoryImageService> logger)
    {
        _context = context;
        _logger = logger;

        _imagesDirectory = Path.Combine(
            environment.ContentRootPath,
            "CategoryImages");

        Directory.CreateDirectory(_imagesDirectory);
    }

    // Загрузка или замена изображения категории.
    public async Task<CategoryImageServiceResult> UploadAsync(
        int categoryId,
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return new CategoryImageServiceResult(
                CategoryImageServiceStatus.InvalidFile);
        }

        if (file.Length > MaxFileSize)
        {
            return new CategoryImageServiceResult(
                CategoryImageServiceStatus.FileTooLarge);
        }

        var category = await _context.Categories
            .FirstOrDefaultAsync(
                c => c.Id == categoryId,
                cancellationToken);

        if (category is null)
        {
            return new CategoryImageServiceResult(
                CategoryImageServiceStatus.CategoryNotFound);
        }

        string extension = Path.GetExtension(file.FileName)
            .ToLowerInvariant();

        if (extension is not ".jpg" and not ".jpeg"
            and not ".png" and not ".webp")
        {
            return new CategoryImageServiceResult(
                CategoryImageServiceStatus.InvalidFile);
        }

        // Проверяем сигнатуру файла, а не только его расширение.
        await using (var validationStream = file.OpenReadStream())
        {
            var header = new byte[12];

            int bytesRead = await validationStream.ReadAsync(
                header.AsMemory(),
                cancellationToken);

            if (!HasValidSignature(extension, header, bytesRead))
            {
                return new CategoryImageServiceResult(
                    CategoryImageServiceStatus.InvalidFile);
            }
        }

        string fileName = $"{Guid.NewGuid():N}{extension}";
        string fullPath = Path.Combine(_imagesDirectory, fileName);
        string imageUrl = $"{ImageUrlPrefix}{fileName}";

        // Сначала записываем новый файл. Старый файл пока не трогаем.
        try
        {
            await using var input = file.OpenReadStream();

            await using var output = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);

            await input.CopyToAsync(output, cancellationToken);
        }
        catch
        {
            DeletePhysicalFile(fullPath);
            throw;
        }

        string? oldImageUrl = category.ImageUrl;
        category.ImageUrl = imageUrl;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Если БД не сохранила новый URL, удаляем новый файл.
            DeletePhysicalFile(fullPath);
            throw;
        }

        // Старый файл удаляется только после успешной записи в БД.
        DeleteStoredImage(oldImageUrl);

        return new CategoryImageServiceResult(
            CategoryImageServiceStatus.Success,
            imageUrl);
    }

    // Удаление изображения категории без удаления самой категории.
    public async Task<CategoryImageServiceStatus> DeleteAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(
                c => c.Id == categoryId,
                cancellationToken);

        if (category is null)
        {
            return CategoryImageServiceStatus.CategoryNotFound;
        }

        string? oldImageUrl = category.ImageUrl;
        category.ImageUrl = null;

        await _context.SaveChangesAsync(cancellationToken);

        DeleteStoredImage(oldImageUrl);

        return CategoryImageServiceStatus.Success;
    }

    // Вызывается после успешного удаления самой категории из БД.
    public void DeleteStoredImage(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) ||
            !imageUrl.StartsWith(
                ImageUrlPrefix,
                StringComparison.Ordinal))
        {
            return;
        }

        string fileName = imageUrl[ImageUrlPrefix.Length..];

        // Не позволяем выйти за пределы папки CategoryImages.
        if (string.IsNullOrWhiteSpace(fileName) ||
            Path.GetFileName(fileName) != fileName)
        {
            return;
        }

        string fullPath = Path.Combine(_imagesDirectory, fileName);
        DeletePhysicalFile(fullPath);
    }

    private static bool HasValidSignature(
        string extension,
        byte[] header,
        int bytesRead)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" =>
                bytesRead >= 3 &&
                header[0] == 0xFF &&
                header[1] == 0xD8 &&
                header[2] == 0xFF,

            ".png" =>
                bytesRead >= 8 &&
                header.AsSpan(0, 8).SequenceEqual(
                    new byte[]
                    {
                        0x89, 0x50, 0x4E, 0x47,
                        0x0D, 0x0A, 0x1A, 0x0A
                    }),

            ".webp" =>
                bytesRead >= 12 &&
                header.AsSpan(0, 4).SequenceEqual(
                    new byte[] { 0x52, 0x49, 0x46, 0x46 }) &&
                header.AsSpan(8, 4).SequenceEqual(
                    new byte[] { 0x57, 0x45, 0x42, 0x50 }),

            _ => false
        };
    }

    private void DeletePhysicalFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Не удалось удалить файл изображения категории: {Path}",
                path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                ex,
                "Нет доступа для удаления изображения категории: {Path}",
                path);
        }
    }
}
