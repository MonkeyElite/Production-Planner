using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using svc.products.Authorization;
using svc.products.Data;
using svc.products.Dtos;
using svc.products.Extensions;
using svc.products.Models;

namespace svc.products.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductionLinesController : ControllerBase
{
    private readonly ProductsDb _db;
    private readonly IValidator<ProductionLineCreateDto> _createValidator;
    private readonly IValidator<ProductionLineUpdateDto> _updateValidator;
    private readonly IAuthorizationService _authorizationService;

    public ProductionLinesController(
        ProductsDb db,
        IValidator<ProductionLineCreateDto> createValidator,
        IValidator<ProductionLineUpdateDto> updateValidator,
        IAuthorizationService authorizationService)
    {
        _db = db;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    [Authorize(Policy = "products:read")]
    public async Task<ActionResult<IEnumerable<ProductionLineView>>> GetProductionLines()
    {
        if (!User.TryGetOwnerId(out var ownerId))
            return Forbid();

        var lines = await _db.ProductionLines
            .AsNoTracking()
            .Where(l => l.OwnerId == ownerId)
            .Include(l => l.ProductionLineProducts)
            .OrderBy(l => l.Name)
            .Select(l => new ProductionLineView(
                l.Id,
                l.Name,
                l.Description,
                l.CapacityPerShift,
                l.ShiftSchedule,
                l.IsActive,
                l.ProductionLineProducts.Select(lp => lp.ProductId).ToList()))
            .ToListAsync();

        return Ok(lines);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "products:read")]
    public async Task<ActionResult<ProductionLineView>> GetProductionLine(Guid id)
    {
        if (!User.TryGetOwnerId(out var ownerId))
            return Forbid();

        var entity = await _db.ProductionLines
            .AsNoTracking()
            .Include(l => l.ProductionLineProducts)
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == ownerId);

        if (entity is null)
            return NotFound();

        var authz = await _authorizationService.AuthorizeAsync(User, entity.OwnerId, SameOwnerRequirement.Instance);
        if (!authz.Succeeded)
            return Forbid();

        var view = new ProductionLineView(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.CapacityPerShift,
            entity.ShiftSchedule,
            entity.IsActive,
            entity.ProductionLineProducts.Select(lp => lp.ProductId).ToList());

        return Ok(view);
    }

    [HttpPost]
    [Authorize(Policy = "products:write")]
    //[Authorize(Policy = "mfa")]
    public async Task<ActionResult> CreateProductionLine([FromBody] ProductionLineCreateDto dto)
    {
        var validation = await _createValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        if (!User.TryGetOwnerId(out var ownerId))
            return Forbid();

        var requestedProducts = NormalizeProductIds(dto.ProductIds);
        var ownedProducts = await GetOwnedProductsAsync(ownerId, requestedProducts);
        if (ownedProducts.Count != requestedProducts.Count)
            return BadRequest(new { message = "One or more products are not owned by the caller." });

        var entity = new ProductionLine
        {
            OwnerId = ownerId,
            Name = dto.Name,
            Description = dto.Description,
            CapacityPerShift = dto.CapacityPerShift,
            ShiftSchedule = dto.ShiftSchedule,
            IsActive = true,
            ProductionLineProducts = ownedProducts
                .Select(id => new ProductionLineProduct { ProductId = id })
                .ToList()
        };

        _db.ProductionLines.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProductionLine), new { id = entity.Id }, new { entity.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "products:write")]
    //[Authorize(Policy = "mfa")]
    public async Task<IActionResult> UpdateProductionLine(Guid id, [FromBody] ProductionLineUpdateDto dto)
    {
        var validation = await _updateValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        if (!User.TryGetOwnerId(out var ownerId))
            return Forbid();

        var entity = await _db.ProductionLines
            .Include(l => l.ProductionLineProducts)
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == ownerId);

        if (entity is null)
            return NotFound();

        var authz = await _authorizationService.AuthorizeAsync(User, entity.OwnerId, SameOwnerRequirement.Instance);
        if (!authz.Succeeded)
            return Forbid();

        var requestedProducts = NormalizeProductIds(dto.ProductIds);
        var ownedProducts = await GetOwnedProductsAsync(ownerId, requestedProducts);
        if (ownedProducts.Count != requestedProducts.Count)
            return BadRequest(new { message = "One or more products are not owned by the caller." });

        entity.Name = dto.Name;
        entity.Description = dto.Description;
        entity.CapacityPerShift = dto.CapacityPerShift;
        entity.ShiftSchedule = dto.ShiftSchedule;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        UpdateProductionLineProducts(entity, ownedProducts);

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "products:write")]
    //[Authorize(Policy = "mfa")]
    public async Task<IActionResult> DeleteProductionLine(Guid id)
    {
        if (!User.TryGetOwnerId(out var ownerId))
            return Forbid();

        var entity = await _db.ProductionLines
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == ownerId);

        if (entity is null)
            return NotFound();

        var authz = await _authorizationService.AuthorizeAsync(User, entity.OwnerId, SameOwnerRequirement.Instance);
        if (!authz.Succeeded)
            return Forbid();

        _db.ProductionLines.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static List<Guid> NormalizeProductIds(IReadOnlyCollection<Guid>? productIds)
    {
        return productIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
    }

    private async Task<List<Guid>> GetOwnedProductsAsync(Guid ownerId, IReadOnlyCollection<Guid> requested)
    {
        if (requested.Count == 0)
            return new List<Guid>();

        return await _db.Products
            .Where(p => p.OwnerId == ownerId && requested.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();
    }

    private static void UpdateProductionLineProducts(ProductionLine entity, IReadOnlyCollection<Guid> desired)
    {
        var current = entity.ProductionLineProducts.Select(lp => lp.ProductId).ToHashSet();
        var desiredSet = desired.ToHashSet();

        var toRemove = entity.ProductionLineProducts.Where(lp => !desiredSet.Contains(lp.ProductId)).ToList();
        foreach (var rel in toRemove)
        {
            entity.ProductionLineProducts.Remove(rel);
        }

        var toAdd = desiredSet.Where(id => !current.Contains(id)).ToList();
        foreach (var id in toAdd)
        {
            entity.ProductionLineProducts.Add(new ProductionLineProduct
            {
                ProductionLineId = entity.Id,
                ProductId = id
            });
        }
    }
}
