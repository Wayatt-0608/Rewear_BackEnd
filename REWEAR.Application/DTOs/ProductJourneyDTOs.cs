namespace REWEAR.Application.DTOs;
public sealed class ProductJourneyResponse
{
    public string ProductId { get; set; } = "";
    public string ProductType { get; set; } = "SecondHand";
    public string TransformationDescription { get; set; } = "";
    public List<string> BeforeImages { get; set; } = new();
    public List<string> AfterImages { get; set; } = new();
    public List<ProductJourneyStage> Stages { get; set; } = new();
    public bool IsComplete { get; set; }
}
public sealed class ProductJourneyStage
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}
