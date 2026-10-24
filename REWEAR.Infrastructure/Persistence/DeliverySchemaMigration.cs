using MongoDB.Bson;
using MongoDB.Driver;
using REWEAR.Domain.Entities;
namespace REWEAR.Infrastructure.Persistence;

/// <summary>Explicit additive indexes. No timestamp, money or stock backfill.</summary>
public sealed class DeliverySchemaMigration(MongoDbSettings settings)
{
    public const string Version = "20261025_delivery_v2";
    public async Task<DeliveryMigrationReport> RunAsync(bool apply)
    {
        var db = new MongoClient(settings.ConnectionString).GetDatabase(settings.DatabaseName);
        var hello = await db.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
        if (!hello.Contains("setName") && hello.GetValue("msg", "") != "isdbgrid") throw new InvalidOperationException("Delivery requires MongoDB transactions.");
        var report = new DeliveryMigrationReport { Version = Version,
            LegacyShipmentsWithoutAssignmentDate = await db.GetCollection<BsonDocument>("Shippings").CountDocumentsAsync(new BsonDocument("AssignedAt", BsonNull.Value)),
            HistoriesWithoutDate = await db.GetCollection<BsonDocument>("OrderStatusHistories").CountDocumentsAsync(new BsonDocument("createdAt", BsonNull.Value)) };
        if (!apply) return report;
        async Task Index(string collection, string name, BsonDocument keys) => await db.GetCollection<BsonDocument>(collection).Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys, new CreateIndexOptions { Name = name }));
        await Index("Shippings", "ix_delivery_shipper_status_assigned_v2", new() { { "shipperId", 1 }, { "status", 1 }, { "AssignedAt", -1 } });
        await Index("OrderStatusHistories", "ix_delivery_order_event_date_v2", new() { { "orderId", 1 }, { "EventType", 1 }, { "createdAt", 1 } });
        await Index("InventoryItems", "ix_inventory_custody_v2", new("Custody", 1));
        await Index("Payments", "ix_cash_event_recorded_v2", new("CashEvents.RecordedAt", 1));
        await db.GetCollection<BsonDocument>("SchemaMigrations").UpdateOneAsync(new BsonDocument("_id", Version), new BsonDocument("$setOnInsert", new BsonDocument("AppliedAt", DateTime.UtcNow)), new UpdateOptions { IsUpsert = true });
        report.Applied = true; return report;
    }
}
public sealed class DeliveryMigrationReport
{
    public string Version { get; set; } = "";
    public bool Applied { get; set; }
    public long LegacyShipmentsWithoutAssignmentDate { get; set; }
    public long HistoriesWithoutDate { get; set; }
}
