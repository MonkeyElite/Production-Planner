using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using svc.products.Authorization;
using svc.products.Data;
using svc.products.Dtos;
using svc.products.Extensions;
using svc.products.Models;
using System.Text.RegularExpressions;

namespace svc.products.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly ProductsDb _db;
        private readonly IValidator<ProductCreateDto> _createValidator;
        private readonly IValidator<ProductUpdateDto> _updateValidator;
        private readonly IAuthorizationService _authorizationService;

        public ProductsController(
            ProductsDb db,
            IValidator<ProductCreateDto> createValidator,
            IValidator<ProductUpdateDto> updateValidator,
            IAuthorizationService authorizationService)
        {
            _db = db;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _authorizationService = authorizationService;
        }

        // GET: api/products
        [HttpGet]
        [Authorize(Policy = "products:read")]
        public async Task<ActionResult<IEnumerable<ProductView>>> GetProducts(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            if (!User.TryGetOwnerId(out var ownerId))
                return Forbid();

            var products = await _db.Products
                .AsNoTracking()
                .Where(p => p.OwnerId == ownerId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new ProductView(
                    p.Id,
                    p.Name,
                    p.Description,
                    p.Price,
                    p.IsActive))
                .ToListAsync();

            return Ok(products);
        }

        // GET: api/products/{id}
        [HttpGet("{id:guid}")]
        [Authorize(Policy = "products:read")]
        public async Task<ActionResult<ProductView>> GetProduct(Guid id)
        {
            if (!User.TryGetOwnerId(out var ownerId))
                return Forbid();

            var product = await _db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId);
            if (product is null)
                return NotFound();

            var authz = await _authorizationService.AuthorizeAsync(User, product.OwnerId, SameOwnerRequirement.Instance);
            if (!authz.Succeeded)
                return Forbid();

            // Generate a Base64 ETag from uint Version (xmin)
            var bytes = BitConverter.GetBytes(product.Version);
            Response.Headers.ETag = Convert.ToBase64String(bytes);

            return Ok(new ProductView(
                product.Id,
                product.Name,
                product.Description,
                product.Price,
                product.IsActive));
        }

        // POST: api/products
        [HttpPost]
        [Authorize(Policy = "products:write")]
        //[Authorize(Policy = "mfa")]
        public async Task<ActionResult> CreateProduct([FromBody] ProductCreateDto dto)
        {
            var validation = await _createValidator.ValidateAsync(dto);
            if (!validation.IsValid)
                return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

            if (!User.TryGetOwnerId(out var ownerId))
                return Forbid();

            var entity = new Product
            {
                OwnerId = ownerId,
                Name = dto.Name,
                Description = dto.Description,
                Price = dto.Price,
                IsActive = true
            };

            _db.Products.Add(entity);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetProduct), new { id = entity.Id }, new { entity.Id });
        }

        // PUT: api/products/{id}
        [HttpPut("{id:guid}")]
        [Authorize(Policy = "products:write")]
        //[Authorize(Policy = "mfa")]
        public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] ProductUpdateDto dto)
        {
            var validation = await _updateValidator.ValidateAsync(dto);
            if (!validation.IsValid)
                return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

            if (!User.TryGetOwnerId(out var ownerId))
                return Forbid();

            var entity = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId);
            if (entity is null)
                return NotFound();

            var authz = await _authorizationService.AuthorizeAsync(User, entity.OwnerId, SameOwnerRequirement.Instance);
            if (!authz.Succeeded)
                return Forbid();

            // ETag concurrency check (If-Match)
            var ifMatchHeader = Request.Headers[HeaderNames.IfMatch].ToString();
            if (!string.IsNullOrWhiteSpace(ifMatchHeader))
            {
                var base64 = Regex.Replace(ifMatchHeader, "^W?/?\"?|\"?$", ""); // strip W/ and quotes
                try
                {
                    var bytes = Convert.FromBase64String(base64);
                    if (bytes.Length != sizeof(uint))
                        return StatusCode(StatusCodes.Status412PreconditionFailed);

                    var expected = BitConverter.ToUInt32(bytes, 0);
                    if (entity.Version != expected)
                        return StatusCode(StatusCodes.Status412PreconditionFailed,
                            new { message = "ETag mismatch. Concurrency conflict." });
                }
                catch
                {
                    return StatusCode(StatusCodes.Status412PreconditionFailed,
                        new { message = "Invalid ETag format." });
                }
            }

            entity.Name = dto.Name;
            entity.Description = dto.Description;
            entity.Price = dto.Price;
            entity.IsActive = dto.IsActive;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/products/{id}
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = "products:write")]
        //[Authorize(Policy = "mfa")]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            if (!User.TryGetOwnerId(out var ownerId))
                return Forbid();

            var entity = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId);
            if (entity is null)
                return NotFound();

            var authz = await _authorizationService.AuthorizeAsync(User, entity.OwnerId, SameOwnerRequirement.Instance);
            if (!authz.Succeeded)
                return Forbid();

            _db.Products.Remove(entity);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
