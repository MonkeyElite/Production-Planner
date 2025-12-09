using FluentValidation;
using svc.products.Dtos;

namespace svc.products.Validation;

public sealed class ProductionLineCreateValidator : AbstractValidator<ProductionLineCreateDto>
{
    public ProductionLineCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CapacityPerShift).GreaterThan(0);
        RuleFor(x => x.ShiftSchedule).NotEmpty().MaximumLength(200);
    }
}

public sealed class ProductionLineUpdateValidator : AbstractValidator<ProductionLineUpdateDto>
{
    public ProductionLineUpdateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CapacityPerShift).GreaterThan(0);
        RuleFor(x => x.ShiftSchedule).NotEmpty().MaximumLength(200);
    }
}
