using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

[BsonIgnoreExtraElements]
public abstract class LifecycleRecord
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PurchasePayout : LifecycleRecord
{
    public string SourcingId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Method { get; set; } = "ManualTransfer";
    public string State { get; set; } = "Confirmed";
    public string Reference { get; set; } = "";
    public string ConfirmedBy { get; set; } = "";
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}

public sealed class InventoryItem : LifecycleRecord
{
    public string Custody { get; set; } = "Unknown";
    public string SourcingId { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "AwaitingInspection";
    public decimal AcquisitionCost { get; set; }
    public string Location { get; set; } = "Kho REWEAR";
    public List<string> Images { get; set; } = new();
    public string? ProjectId { get; set; }
    public string? ProductId { get; set; }
}

public sealed class InventoryMovement : LifecycleRecord
{
    public string? FromCustody { get; set; }
    public string? ToCustody { get; set; }
    public string InventoryId { get; set; } = "";
    public string? FromState { get; set; }
    public string ToState { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? RelatedId { get; set; }
}

public sealed class UpcyclingProject : LifecycleRecord
{
    public string Title { get; set; } = "";
    public string Concept { get; set; } = "";
    public string ResponsibleStaffId { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime? PlannedCompletion { get; set; }
    public List<string> InventoryIds { get; set; } = new();
    public List<string> BeforeImages { get; set; } = new();
    public List<string> AfterImages { get; set; } = new();
    public string State { get; set; } = "Pending";
    public decimal AcquisitionCost { get; set; }
    public List<ProductionCost> Costs { get; set; } = new();
    public List<LifecycleEvent> Timeline { get; set; } = new();
    public string? ProductId { get; set; }
}

public sealed class ProductionCost
{
    public string IdempotencyKey { get; set; } = "";
    public string Kind { get; set; } = "Material";
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
    public string ActorId { get; set; } = "";
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public sealed class LifecycleEvent
{
    public string? From { get; set; }
    public string To { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public sealed class QualityInspection : LifecycleRecord
{
    public string ProjectId { get; set; } = "";
    public string InspectorId { get; set; } = "";
    public bool Passed { get; set; }
    public string Note { get; set; } = "";
    public List<string> Images { get; set; } = new();
}

public sealed class SourcingOffer
{
    public int Version { get; set; }
    public decimal Amount { get; set; }
    public string? ActorId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
}
