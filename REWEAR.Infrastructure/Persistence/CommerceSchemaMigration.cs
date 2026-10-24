using MongoDB.Bson;
using MongoDB.Driver;
using REWEAR.Domain.Entities;
namespace REWEAR.Infrastructure.Persistence;

/// <summary>Additive, explicit migration. Never backfills money, stock allocations or user identity.</summary>
public sealed class CommerceSchemaMigration(MongoDbSettings settings)
{
    public const string Version = "20261025_commerce_v1";
    public async Task<CommerceMigrationReport> RunAsync(bool apply)
    {
        var client = new MongoClient(settings.ConnectionString); var db = client.GetDatabase(settings.DatabaseName);
        var hello = await db.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
        if (!hello.Contains("setName") && hello.GetValue("msg", "") != "isdbgrid")
            throw new InvalidOperationException("Commerce requires a MongoDB replica set or sharded cluster; migration stopped.");
        var report = new CommerceMigrationReport { Version = Version, Applied = false,
            ReservedProductsToReview = await db.GetCollection<BsonDocument>("Products").CountDocumentsAsync(new BsonDocument("status", "Reserved")),
            MissingStockQuantity = await db.GetCollection<BsonDocument>("Products").CountDocumentsAsync(new BsonDocument("stockQuantity", new BsonDocument("$exists", false))),
            MissingReservationDeadline = await db.GetCollection<BsonDocument>("Orders").CountDocumentsAsync(new BsonDocument { { "status", "AwaitingPayment" }, { "ReservationExpiresAt", new BsonDocument("$exists", false) } }),
            MissingCostBasisOrders = await db.GetCollection<BsonDocument>("Orders").CountDocumentsAsync(new BsonDocument("items", new BsonDocument("$elemMatch", new BsonDocument("HasCostBasis", new BsonDocument("$ne", true))))) };
        if (!apply) return report;
        // No TTL on reservations/payments: they are durable audit evidence and must be closed by the worker.
        async Task Index(string collection, string name, BsonDocument keys, bool unique = false, BsonDocument? partial = null)
        {
            await db.GetCollection<BsonDocument>(collection).Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys,
                new CreateIndexOptions<BsonDocument> { Name = name, Unique = unique, PartialFilterExpression = partial }));
        }
        await Index("Payments", "ux_payments_gateway_code_v1", new("GatewayOrderCode", 1), true, new("GatewayOrderCode", new BsonDocument("$gt", 0)));
        await Index("Users", "ux_users_email_v1", new("email", 1), true, new("email", new BsonDocument("$type", "string")));
        await Index("Payments", "ix_payments_paid_at_v1", new("paidAt", 1));
        await Index("Payments", "ix_payments_refunded_at_v1", new("refundedAt", 1));
        await Index("Payments", "ix_payments_reconciliation_v1", new() { { "LastGatewayCheckedAt", 1 }, { "createdAt", 1 } });
        await Index("Orders", "ix_orders_reservation_expiry_v1", new() { { "status", 1 }, { "ReservationExpiresAt", 1 } });
        await Index("Orders", "ix_orders_paid_at_v1", new("PaidAt", 1));
        await Index("Orders", "ux_orders_pending_checkout_key_v1", new() { { "userId", 1 }, { "CheckoutKey", 1 } }, true,
            new() { { "CheckoutKey", new BsonDocument("$type", "string") }, { "status", "AwaitingPayment" } });
        await Index("StockReservations", "ix_reservations_product_state_v1", new() { { "ProductId", 1 }, { "State", 1 } });
        await Index("StockReservations", "ix_reservations_order_v1", new("OrderId", 1));
        await Index("PurchasePayouts", "ux_payout_sourcing_v1", new("SourcingId", 1), true);
        await Index("InventoryItems", "ux_inventory_sourcing_v1", new("SourcingId", 1), true);
        await Index("InventoryItems", "ix_inventory_state_v1", new("State", 1));
        await Index("InventoryItems", "ix_inventory_product_v1", new("ProductId", 1));
        await Index("InventoryMovements", "ix_movements_inventory_date_v1", new() { { "InventoryId", 1 }, { "CreatedAt", 1 } });
        await Index("UpcyclingProjects", "ix_project_state_v1", new("State", 1));
        await Index("QualityInspections", "ix_qc_project_date_v1", new() { { "ProjectId", 1 }, { "CreatedAt", 1 } });
        await Index("ReturnRequests", "ux_return_order_v1", new("OrderId", 1), true);
        var migrations = db.GetCollection<BsonDocument>("SchemaMigrations");
        await migrations.UpdateOneAsync(new BsonDocument("_id", Version), new BsonDocument("$setOnInsert", new BsonDocument("AppliedAt", DateTime.UtcNow)), new UpdateOptions { IsUpsert = true });
        report.Applied = true; return report;
    }
}
public sealed class CommerceMigrationReport
{
    public string Version { get; set; } = "";
    public bool Applied { get; set; }
    public long ReservedProductsToReview { get; set; }
    public long MissingStockQuantity { get; set; }
    public long MissingReservationDeadline { get; set; }
    public long MissingCostBasisOrders { get; set; }
}
