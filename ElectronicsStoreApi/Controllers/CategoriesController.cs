using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;

namespace ElectronicsStoreApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly StoreDbContext _context;
    private readonly CategoryImageService _categoryImageService;

    public CategoriesController(
        StoreDbContext context,
        CategoryImageService categoryImageService)
    {
        _context = context;
        _categoryImageService = categoryImageService;
    }

    // Получение всех категорий.
    [HttpGet]
    public async Task<ActionResult<List<CategoryResponse>>> GetAll()
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CategoryResponse
            {
                Id = c.Id,
                Name = c.Name,
                ProductCount = c.Products.Count(),
                ImageUrl = c.ImageUrl
            })
            .ToListAsync();

        return Ok(categories);
    }

    // Получение категории по ID.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CategoryResponse>> GetById(int id)
    {
        var category = await _context.Categories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategoryResponse
            {
                Id = c.Id,
                Name = c.Name,
                ProductCount = c.Products.Count(),
                ImageUrl = c.ImageUrl
            })
            .FirstOrDefaultAsync();

        if (category is null)
        {
            return NotFound(new
            {
                message = "Категория не найдена."
            });
        }

        return Ok(category);
    }

    // Создание категории.
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryResponse>> Create(
        [FromBody] CreateCategoryRequest request)
    {
        string name = request.Name.Trim();

        if (name.Length < 2 || name.Length > 100)
        {
            return BadRequest(new
            {
                message = "Название должно содержать от 2 до 100 символов."
            });
        }

        bool nameExists = await _context.Categories
            .AnyAsync(c => c.Name == name);

        if (nameExists)
        {
            return Conflict(new
            {
                message = "Категория с таким названием уже существует."
            });
        }

        var category = new Category
        {
            Name = name
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        var response = new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            ProductCount = 0,
            ImageUrl = category.ImageUrl
        };

        return CreatedAtAction(
            nameof(GetById),
            new { id = category.Id },
            response);
    }

    // Переименование категории.
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryResponse>> Update(
        int id,
        [FromBody] CreateCategoryRequest request)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
        {
            return NotFound(new
            {
                message = "Категория не найдена."
            });
        }

        string name = request.Name.Trim();

        if (name.Length < 2 || name.Length > 100)
        {
            return BadRequest(new
            {
                message = "Название должно содержать от 2 до 100 символов."
            });
        }

        bool nameExists = await _context.Categories
            .AnyAsync(c => c.Name == name && c.Id != id);

        if (nameExists)
        {
            return Conflict(new
            {
                message = "Категория с таким названием уже существует."
            });
        }

        category.Name = name;
        await _context.SaveChangesAsync();

        int productCount = await _context.Products
            .CountAsync(p => p.CategoryId == id);

        var response = new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            ProductCount = productCount,
            ImageUrl = category.ImageUrl
        };

        return Ok(response);
    }

    // Удаление категории.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
        {
            return NotFound(new
            {
                message = "Категория не найдена."
            });
        }

        bool hasProducts = await _context.Products
            .AnyAsync(p => p.CategoryId == id);

        if (hasProducts)
        {
            return Conflict(new
            {
                message = "Нельзя удалить категорию, пока к ней привязаны товары."
            });
        }

        string? imageUrl = category.ImageUrl;

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        // Файл удаляется только после успешного удаления категории из БД.
        _categoryImageService.DeleteStoredImage(imageUrl);

        return NoContent();
    }
}
