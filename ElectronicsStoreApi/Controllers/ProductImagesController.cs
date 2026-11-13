using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElectronicsStoreApi.Controllers;

[ApiController]
[Route("api/products/{productId:int}/image")]
[Authorize(Roles = "Admin")]
public class ProductImagesController : ControllerBase
{
    private readonly ProductImageService _imageService;

    public ProductImagesController(
        ProductImageService imageService)
    {
        _imageService = imageService;
    }

    // Загрузка или замена изображения товара.
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(
        ProductImageService.MaxFileSize + 64 * 1024)]
    public async Task<IActionResult> Upload(
        int productId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var result = await _imageService.UploadAsync(
            productId,
            file,
            cancellationToken);

        switch (result.Status)
        {
            case ProductImageServiceStatus.Success:
                return Ok(new
                {
                    message = "Фотография товара сохранена.",
                    imageUrl = result.ImageUrl
                });

            case ProductImageServiceStatus.ProductNotFound:
                return NotFound(new
                {
                    message = "Товар не найден."
                });

            case ProductImageServiceStatus.InvalidFile:
                return BadRequest(new
                {
                    message =
                        "Недопустимый файл. Используйте JPEG, PNG или WebP."
                });

            case ProductImageServiceStatus.FileTooLarge:
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    new
                    {
                        message =
                            "Размер изображения не должен превышать 5 МБ."
                    });

            default:
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message = "Не удалось загрузить изображение."
                    });
        }
    }

    // Удаление изображения товара.
    [HttpDelete]
    public async Task<IActionResult> Delete(
        int productId,
        CancellationToken cancellationToken)
    {
        var status = await _imageService.DeleteAsync(
            productId,
            cancellationToken);

        if (status == ProductImageServiceStatus.ProductNotFound)
        {
            return NotFound(new
            {
                message = "Товар не найден."
            });
        }

        return NoContent();
    }
}