using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElectronicsStoreApi.Controllers;

[ApiController]
[Route("api/categories/{categoryId:int}/image")]
[Authorize(Roles = "Admin")]
public class CategoryImagesController : ControllerBase
{
    private readonly CategoryImageService _imageService;

    public CategoryImagesController(
        CategoryImageService imageService)
    {
        _imageService = imageService;
    }

    // Загрузка или замена изображения категории.
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(CategoryImageService.MaxFileSize + 64 * 1024)]
    public async Task<IActionResult> Upload(
        int categoryId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var result = await _imageService.UploadAsync(
            categoryId,
            file,
            cancellationToken);

        switch (result.Status)
        {
            case CategoryImageServiceStatus.Success:
                return Ok(new
                {
                    message = "Фотография категории сохранена.",
                    imageUrl = result.ImageUrl
                });

            case CategoryImageServiceStatus.CategoryNotFound:
                return NotFound(new
                {
                    message = "Категория не найдена."
                });

            case CategoryImageServiceStatus.InvalidFile:
                return BadRequest(new
                {
                    message =
                        "Недопустимый файл. Используйте JPEG, PNG или WebP."
                });

            case CategoryImageServiceStatus.FileTooLarge:
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

    // Удаление изображения категории.
    [HttpDelete]
    public async Task<IActionResult> Delete(
        int categoryId,
        CancellationToken cancellationToken)
    {
        var status = await _imageService.DeleteAsync(
            categoryId,
            cancellationToken);

        if (status == CategoryImageServiceStatus.CategoryNotFound)
        {
            return NotFound(new
            {
                message = "Категория не найдена."
            });
        }

        return NoContent();
    }
}
