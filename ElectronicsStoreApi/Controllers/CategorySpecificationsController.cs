using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElectronicsStoreApi.Controllers;

[ApiController]
[Route("api/categories/{categoryId:int}/specifications")]
public class CategorySpecificationsController : ControllerBase
{
    private readonly StoreDbContext _context;

    public CategorySpecificationsController(StoreDbContext context)
    {
        _context = context;
    }

    // ==================================================
    // ПОЛУЧЕНИЕ ХАРАКТЕРИСТИК КАТЕГОРИИ
    // ==================================================

    [HttpGet]
    public async Task<ActionResult<List<CategorySpecificationResponse>>> GetAll(
        int categoryId)
    {
        bool categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == categoryId);

        if (!categoryExists)
        {
            return NotFound(new
            {
                message = "Категория не найдена."
            });
        }

        var specifications = await _context.CategorySpecifications
            .AsNoTracking()
            .Where(s => s.CategoryId == categoryId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .Select(s => new CategorySpecificationResponse
            {
                Id = s.Id,
                CategoryId = s.CategoryId,
                Code = s.Code,
                Name = s.Name,
                Unit = s.Unit,
                SortOrder = s.SortOrder
            })
            .ToListAsync();

        return Ok(specifications);
    }

    // ==================================================
    // СОЗДАНИЕ ХАРАКТЕРИСТИКИ
    // ==================================================

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategorySpecificationResponse>> Create(
        int categoryId,
        [FromBody] SaveCategorySpecificationRequest request)
    {
        bool categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == categoryId);

        if (!categoryExists)
        {
            return NotFound(new
            {
                message = "Категория не найдена."
            });
        }

        string code = request.Code.Trim().ToLowerInvariant();

        string name = request.Name.Trim();

        string? unit = string.IsNullOrWhiteSpace(request.Unit)
            ? null
            : request.Unit.Trim();

        bool codeExists = await _context.CategorySpecifications
            .AnyAsync(s =>
                s.CategoryId == categoryId &&
                s.Code == code);

        if (codeExists)
        {
            return Conflict(new
            {
                message = "Характеристика с таким кодом уже существует в этой категории."
            });
        }

        var specification = new CategorySpecification
        {
            CategoryId = categoryId,
            Code = code,
            Name = name,
            Unit = unit,
            SortOrder = request.SortOrder
        };

        _context.CategorySpecifications.Add(specification);

        await _context.SaveChangesAsync();

        var response = MapToResponse(specification);

        return CreatedAtAction(
            nameof(GetAll),
            new { categoryId },
            response);
    }

    // ==================================================
    // ИЗМЕНЕНИЕ ХАРАКТЕРИСТИКИ
    // ==================================================

    [HttpPut("{specificationId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategorySpecificationResponse>> Update(
        int categoryId,
        int specificationId,
        [FromBody] SaveCategorySpecificationRequest request)
    {
        var specification = await _context.CategorySpecifications
            .FirstOrDefaultAsync(s =>
                s.Id == specificationId &&
                s.CategoryId == categoryId);

        if (specification == null)
        {
            return NotFound(new
            {
                message = "Характеристика не найдена."
            });
        }

        string code = request.Code.Trim().ToLowerInvariant();

        string name = request.Name.Trim();

        string? unit = string.IsNullOrWhiteSpace(request.Unit)
            ? null
            : request.Unit.Trim();

        bool codeExists = await _context.CategorySpecifications
            .AnyAsync(s =>
                s.CategoryId == categoryId &&
                s.Code == code &&
                s.Id != specificationId);

        if (codeExists)
        {
            return Conflict(new
            {
                message = "Характеристика с таким кодом уже существует в этой категории."
            });
        }

        specification.Code = code;
        specification.Name = name;
        specification.Unit = unit;
        specification.SortOrder = request.SortOrder;

        await _context.SaveChangesAsync();

        return Ok(MapToResponse(specification));
    }

    // ==================================================
    // УДАЛЕНИЕ ХАРАКТЕРИСТИКИ
    // ==================================================

    [HttpDelete("{specificationId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(
        int categoryId,
        int specificationId)
    {
        var specification = await _context.CategorySpecifications
            .FirstOrDefaultAsync(s =>
                s.Id == specificationId &&
                s.CategoryId == categoryId);

        if (specification == null)
        {
            return NotFound(new
            {
                message = "Характеристика не найдена."
            });
        }

        bool isUsed = await _context.ProductSpecifications
            .AnyAsync(s =>
                s.CategorySpecificationId == specificationId);

        if (isUsed)
        {
            return Conflict(new
            {
                message = "Нельзя удалить характеристику, пока она используется товарами."
            });
        }

        _context.CategorySpecifications.Remove(specification);

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ==================================================
    // ПРЕОБРАЗОВАНИЕ В DTO
    // ==================================================

    private static CategorySpecificationResponse MapToResponse(
        CategorySpecification specification)
    {
        return new CategorySpecificationResponse
        {
            Id = specification.Id,
            CategoryId = specification.CategoryId,
            Code = specification.Code,
            Name = specification.Name,
            Unit = specification.Unit,
            SortOrder = specification.SortOrder
        };
    }
}