using ElectronicsStoreApi.Data;
using Microsoft.EntityFrameworkCore;

namespace ElectronicsStoreApi.Services;

public enum ProductImageServiceStatus
{
    Success,
    ProductNotFound,
    InvalidFile,
    FileTooLarge
}

public sealed record ProductImageServiceResult(
    ProductImageServiceStatus Status,
    string? ImageUrl = null);

public class ProductImageService
{
    public const long MaxFileSize = 5 * 1024 * 1024;

    private const string ImageUrlPrefix = "/uploads/products/";

    private readonly StoreDbContext _context;
    private readonly string _imagesDirectory;
    private readonly ILogger<ProductImageService> _logger;

    public ProductImageService(
        StoreDbContext context,
        IWebHostEnvironment environment,
        ILogger<ProductImageService> logger)
    {
        _context = context;
        _logger = logger;

        _imagesDirectory = Path.Combine(
            environment.ContentRootPath,
            "ProductImages");

        Directory.CreateDirectory(_imagesDirectory);
    }

    // Загрузка или замена фотографии товара.
    public async Task<ProductImageServiceResult> UploadAsync(
        int productId,
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return new ProductImageServiceResult(
                ProductImageServiceStatus.InvalidFile);
        }

        if (file.Length > MaxFileSize)
        {
            return new ProductImageServiceResult(
                ProductImageServiceStatus.FileTooLarge);
        }

        var product = await _context.Products
            .FirstOrDefaultAsync(
                p => p.Id == productId,
                cancellationToken);

        if (product is null)
        {
            return new ProductImageServiceResult(
                ProductImageServiceStatus.ProductNotFound);
        }

        string extension = Path.GetExtension(file.FileName)
            .ToLowerInvariant();

        if (extension is not ".jpg" and not ".jpeg"
            and not ".png" and not ".webp")
        {
            return new ProductImageServiceResult(
                ProductImageServiceStatus.InvalidFile);
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
                return new ProductImageServiceResult(
                    ProductImageServiceStatus.InvalidFile);
            }
        }

        // Генерируем случайное имя файла.
        string fileName = $"{Guid.NewGuid():N}{extension}";

        string fullPath = Path.Combine(
            _imagesDirectory,
            fileName);

        // Формируем публичный URL.
        string imageUrl = $"{ImageUrlPrefix}{fileName}";

        // Сохраняем новую фотографию.
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

            await input.CopyToAsync(
                output,
                cancellationToken);
        }
        catch
        {
            DeletePhysicalFile(fullPath);
            throw;
        }

        // Запоминаем старую фотографию.
        string? oldImageUrl = product.ImageUrl;

        product.ImageUrl = imageUrl;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Если запись в БД не удалась,
            // удаляем новую фотографию.
            DeletePhysicalFile(fullPath);
            throw;
        }

        // Старую фотографию удаляем только после
        // успешного сохранения нового пути в БД.
        DeleteStoredImage(oldImageUrl);

        return new ProductImageServiceResult(
            ProductImageServiceStatus.Success,
            imageUrl);
    }

    // Удаление фотографии товара.
    public async Task<ProductImageServiceStatus> DeleteAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(
                p => p.Id == productId,
                cancellationToken);

        if (product is null)
        {
            return ProductImageServiceStatus.ProductNotFound;
        }

        string? oldImageUrl = product.ImageUrl;

        product.ImageUrl = null;

        await _context.SaveChangesAsync(cancellationToken);

        DeleteStoredImage(oldImageUrl);

        return ProductImageServiceStatus.Success;
    }

    // Проверка сигнатуры изображения.
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

    // Удаление локальной фотографии по физическому пути.
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
                "Не удалось удалить файл изображения: {Path}",
                path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                ex,
                "Нет доступа для удаления изображения: {Path}",
                path);
        }
    }

    // Удаление фотографии, путь к которой хранится в БД.
    private void DeleteStoredImage(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) ||
            !imageUrl.StartsWith(
                ImageUrlPrefix,
                StringComparison.Ordinal))
        {
            return;
        }

        string fileName = imageUrl[ImageUrlPrefix.Length..];

        // Не позволяем выйти за пределы ProductImages.
        if (string.IsNullOrWhiteSpace(fileName) ||
            Path.GetFileName(fileName) != fileName)
        {
            return;
        }

        string fullPath = Path.Combine(
            _imagesDirectory,
            fileName);

        DeletePhysicalFile(fullPath);
    }
}