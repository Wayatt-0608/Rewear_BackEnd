using MongoDB.Bson;
using MongoDB.Driver;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Infrastructure.Persistence;
namespace REWEAR.Infrastructure.Services;

// Explicit public allowlist. Raw date reads avoid entity defaults inventing legacy event times.
public sealed class ProductJourneyQuery(MongoDbContext context) : IProductJourneyQuery
{
    public async Task<ProductJourneyResponse?> GetAsync(string productId)
    {
        if (!ObjectId.TryParse(productId, out var oid)) throw new ArgumentException("Mã sản phẩm không hợp lệ.");
        var product = await context.Collection<BsonDocument>("Products").Find(new BsonDocument("_id", oid)).FirstOrDefaultAsync();
        if (product == null || !product.GetValue("isActive", false).ToBoolean()) return null;
        var response = new ProductJourneyResponse { ProductId = productId, ProductType = Text(product, "ProductType", "SecondHand"),
            TransformationDescription = Text(product, "description"), AfterImages = Images(product, "imageUrls") };
        bool upcycled = response.ProductType == "Upcycled";
        BsonDocument? project = null;
        if (upcycled && ObjectId.TryParse(Text(product, "UpcyclingProjectId"), out var projectId))
            project = await context.Collection<BsonDocument>("UpcyclingProjects").Find(new BsonDocument("_id", projectId)).FirstOrDefaultAsync();
        var inventoryIds = Array(product, "SourceInventoryIds").Where(i => i.IsString && ObjectId.TryParse(i.AsString, out _)).Select(i => ObjectId.Parse(i.AsString)).ToList();
        var received = new List<DateTime>(); var inspected = new List<DateTime>();
        foreach (var id in inventoryIds)
        {
            var item = await context.Collection<BsonDocument>("InventoryItems").Find(new BsonDocument("_id", id)).FirstOrDefaultAsync();
            if (item == null) continue;
            var movements = await context.Collection<BsonDocument>("InventoryMovements").Find(new BsonDocument { { "InventoryId", id.ToString() }, { "ToState", "AwaitingInspection" } }).ToListAsync();
            var at = movements.Select(m => Date(m, "CreatedAt")).Where(d => d.HasValue).OrderBy(d => d).FirstOrDefault();
            if (at.HasValue) received.Add(at.Value);
            if (!ObjectId.TryParse(Text(item, "SourcingId"), out var sourceId)) continue;
            var source = await context.Collection<BsonDocument>("SourcingRequests").Find(new BsonDocument("_id", sourceId)).FirstOrDefaultAsync();
            var inspectionAt = Date(source, "InspectedAt"); if (inspectionAt.HasValue) inspected.Add(inspectionAt.Value);
        }
        Add(response, "collected", "REWEAR tiếp nhận món đồ", received.Count > 0 ? received.Max() : null);
        Add(response, "inspected", "Kiểm định nguồn hàng", inspected.Count > 0 ? inspected.Max() : null);
        if (upcycled && project != null && Text(project, "ProductId") == productId && Text(project, "State") == "Completed")
        {
            response.BeforeImages = Images(project, "BeforeImages");
            var started = Array(project, "Timeline").Where(e => e.IsBsonDocument && Text(e.AsBsonDocument, "To") == "InProgress")
                .Select(e => Date(e.AsBsonDocument, "At")).Where(d => d.HasValue).OrderBy(d => d).FirstOrDefault();
            Add(response, "upcycled", "Thiết kế và tái chế", started);
            var qc = await context.Collection<BsonDocument>("QualityInspections").Find(new BsonDocument { { "ProjectId", project["_id"].ToString() }, { "Passed", true } }).ToListAsync();
            Add(response, "quality_checked", "Kiểm định thành phẩm đạt", qc.Select(q => Date(q, "CreatedAt")).Where(d => d.HasValue).OrderByDescending(d => d).FirstOrDefault());
        }
        Add(response, "published", "Đăng bán trên REWEAR", Date(product, "createdAt"));
        response.IsComplete = inventoryIds.Count > 0 && received.Count == inventoryIds.Count && inspected.Count == inventoryIds.Count
            && response.Stages.Count == (upcycled ? 5 : 3) && (!upcycled || response.BeforeImages.Count > 0 && response.AfterImages.Count > 0);
        if (response.Stages.Zip(response.Stages.Skip(1), (a, b) => a.OccurredAt > b.OccurredAt).Any(invalid => invalid)) response.IsComplete = false;
        return response;
    }
    private static string Text(BsonDocument? doc, string field, string fallback = "") => doc != null && doc.TryGetValue(field, out var value) && value.IsString ? value.AsString : fallback;
    private static IEnumerable<BsonValue> Array(BsonDocument doc, string field) => doc.TryGetValue(field, out var value) && value.IsBsonArray ? value.AsBsonArray : Enumerable.Empty<BsonValue>();
    private static DateTime? Date(BsonDocument? doc, string field) => doc != null && doc.TryGetValue(field, out var value) && value.IsBsonDateTime ? value.ToUniversalTime() : null;
    private static List<string> Images(BsonDocument doc, string field) => Array(doc, field).Where(v => v.IsString).Select(v => v.AsString)
        .Where(v => Uri.TryCreate(v, UriKind.Absolute, out var url) && url.Scheme == "https").Distinct().Take(5).ToList();
    private static void Add(ProductJourneyResponse response, string code, string label, DateTime? at)
    { if (at.HasValue) response.Stages.Add(new() { Code = code, Label = label, OccurredAt = at.Value }); }
}
