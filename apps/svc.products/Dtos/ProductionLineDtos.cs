namespace svc.products.Dtos;

public sealed record ProductionLineCreateDto(
    string Name,
    string? Description,
    int CapacityPerShift,
    string ShiftSchedule,
    IReadOnlyCollection<Guid>? ProductIds);

public sealed record ProductionLineUpdateDto(
    string Name,
    string? Description,
    int CapacityPerShift,
    string ShiftSchedule,
    bool IsActive,
    IReadOnlyCollection<Guid>? ProductIds);

public sealed record ProductionLineView(
    Guid Id,
    string Name,
    string? Description,
    int CapacityPerShift,
    string ShiftSchedule,
    bool IsActive,
    IReadOnlyCollection<Guid> ProductIds);
