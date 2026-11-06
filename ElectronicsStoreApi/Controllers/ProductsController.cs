using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElectronicsStoreApi;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ProductService _productService;

    public ProductsController(ProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<ActionResult<ProductListResponse>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int? maxQuantity,
        [FromQuery] string? sortBy = "id",
        [FromQuery] bool descending = false,
        [FromQuery] bool inStockOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        if (page < 1)
        {
            return BadRequest(new
            {
                message = "Номер страницы должен быть не меньше 1."
            });
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                message = "Размер страницы должен быть от 1 до 100."
            });
        }

        if ((minPrice.HasValue && minPrice.Value < 0) ||
            (maxPrice.HasValue && maxPrice.Value < 0) ||
            (minPrice.HasValue &&
             maxPrice.HasValue &&
             minPrice.Value > maxPrice.Value))
        {
            return BadRequest(new
            {
                message = "Некорректный диапазон цен."
            });
        }

        if (maxQuantity.HasValue && maxQuantity.Value < 0)
        {
            return BadRequest(new
            {
                message = "Количество не может быть отрицательным."
            });
        }

        sortBy = string.IsNullOrWhiteSpace(sortBy)
            ? "id"
            : sortBy.Trim().ToLowerInvariant();

        if (sortBy != "id" &&
            sortBy != "name" &&
            sortBy != "price" &&
            sortBy != "quantity")
        {
            return BadRequest(new
            {
                message =
                    "Сортировка доступна по id, name, price или quantity."
            });
        }

        var products = await _productService.GetAllAsync(
            search,
            categoryId,
            minPrice,
            maxPrice,
            maxQuantity,
            sortBy,
            descending,
            inStockOnly,
            page,
            pageSize);

        return Ok(products);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductResponse>> GetById(int id)
    {
        var product = await _productService.GetByIdAsync(id);

        if (product is null)
        {
            return NotFound();
        }

        return Ok(product);
    }

    [HttpGet("by-category/{categoryId:int}")]
    public async Task<ActionResult<List<ProductResponse>>> GetByCategory(
        int categoryId)
    {
        var products =
            await _productService.GetByCategoryAsync(categoryId);

        return Ok(products);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<ProductResponse>> CreateProduct(
        [FromBody] CreateProductRequest request)
    {
        var result = await _productService.CreateAsync(request);

        if (result.Status == ProductServiceStatus.CategoryNotFound)
        {
            return BadRequest(new
            {
                message =
                    $"Категория с ID {request.CategoryId} не существует."
            });
        }

        if (result.Status == ProductServiceStatus.InvalidPrice)
        {
            return BadRequest(new
            {
                message =
                    "Старая цена должна быть выше текущей."
            });
        }

        if (result.Status == ProductServiceStatus.InvalidSpecifications)
        {
            return BadRequest(new
            {
                message = "Переданы некорректные характеристики. Проверьте их принадлежность категории товара и отсутствие повторов."
            });
        }

        var product = result.Product!;

        return CreatedAtAction(
            nameof(GetById),
            new { id = product.Id },
            product);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateProduct(
        int id,
        [FromBody] UpdateProductRequest request)
    {
        var status = await _productService.UpdateAsync(id, request);

        if (status == ProductServiceStatus.ProductNotFound)
        {
            return NotFound();
        }

        if (status == ProductServiceStatus.CategoryNotFound)
        {
            return BadRequest(new
            {
                message =
                    $"Категория с ID {request.CategoryId} не существует."
            });
        }

        if (status == ProductServiceStatus.InvalidPrice)
        {
            return BadRequest(new
            {
                message =
                    "Старая цена должна быть выше текущей."
            });
        }

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var status = await _productService.DeleteAsync(id);

        if (status == ProductServiceStatus.ProductNotFound)
        {
            return NotFound();
        }

        return NoContent();
    }
}