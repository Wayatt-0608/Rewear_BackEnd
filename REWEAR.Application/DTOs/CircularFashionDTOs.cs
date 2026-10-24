namespace REWEAR.Application.DTOs;

public sealed class ConfirmPayoutRequest
{
    public decimal Amount { get; set; }
    public string Method { get; set; } = "ManualTransfer";
    public string Reference { get; set; } = "";
    public string? Note { get; set; }
}
public sealed class CreateUpcyclingRequest
{
    public string Title { get; set; } = "";
    public string Concept { get; set; } = "";
    public string ResponsibleStaffId { get; set; } = "";
    public DateTime? PlannedCompletion { get; set; }
    public List<string> InventoryIds { get; set; } = new();
}
public sealed class UpdateProjectRequest
{
    public string State { get; set; } = "";
    public string Note { get; set; } = "";
    public List<string>? AfterImages { get; set; }
}
public sealed class RecordCostRequest
{
    public string IdempotencyKey { get; set; } = "";
    public string Kind { get; set; } = "Material";
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
}
public sealed class QualityDecisionRequest
{
    public bool Passed { get; set; }
    public string Note { get; set; } = "";
    public List<string> Images { get; set; } = new();
}
public sealed class PublishProjectRequest
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string BrandId { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public decimal SellingPrice { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
}

public sealed class PublishResaleRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? BrandId { get; set; }
    public string? CategoryId { get; set; }
    public decimal SellingPrice { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public REWEAR.Domain.Enums.ProductCondition? Condition { get; set; }
    public List<string>? Images { get; set; }
}

