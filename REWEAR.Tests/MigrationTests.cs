using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using REWEAR.Infrastructure.Persistence;
namespace REWEAR.Tests;
public partial class CommerceTests
{
    [Test]
    public async Task DeliveryMigrationPreservesLegacyShippingHistoryAndIsIdempotent()
    {
        var db = _client.GetDatabase(_database);
        var oldShipping = new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "orderId", ObjectId.GenerateNewId().ToString() }, { "status", "InTransit" }, { "legacyField", "preserve" } };
        var oldHistory = new BsonDocument { { "orderId", oldShipping["orderId"] }, { "toStatus", "Shipping" } };
        await db.GetCollection<BsonDocument>("Shippings").InsertOneAsync(oldShipping);
        await db.GetCollection<BsonDocument>("OrderStatusHistories").InsertOneAsync(oldHistory);
        var migration = new DeliverySchemaMigration(new() { ConnectionString = LocalServer, DatabaseName = _database });
        var check = await migration.RunAsync(false); Assert.That(check.Applied, Is.False); Assert.That(check.LegacyShipmentsWithoutAssignmentDate, Is.EqualTo(1));
        Assert.That(check.HistoriesWithoutDate, Is.EqualTo(1));
        await migration.RunAsync(true); await migration.RunAsync(true);
        Assert.That(await db.GetCollection<BsonDocument>("Shippings").Find(new BsonDocument("_id", oldShipping["_id"])).SingleAsync(), Is.EqualTo(oldShipping));
        Assert.That(await db.GetCollection<BsonDocument>("OrderStatusHistories").Find(new BsonDocument("_id", oldHistory["_id"])).SingleAsync(), Is.EqualTo(oldHistory));
        Assert.That(await db.GetCollection<BsonDocument>("SchemaMigrations").CountDocumentsAsync(new BsonDocument("_id", DeliverySchemaMigration.Version)), Is.EqualTo(1));
    }

    [Test]
    public async Task VersionedMigrationIsAdditiveIdempotentAndPreservesLegacyDocuments()
    {
        var db = _client.GetDatabase(_database);
        var rawProduct = new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "title", "Legacy fixture" }, { "status", "Reserved" } };
        var rawUser = new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "legacyFixtureField", "preserve-this-fixture" } };
        await db.GetCollection<BsonDocument>("Products").InsertOneAsync(rawProduct);
        Assert.That((await Products.GetByIdAsync(rawProduct["_id"].ToString()!))!.StockQuantity, Is.Zero, "Missing stock must never invent a sellable unit.");
        await db.GetCollection<BsonDocument>("Users").InsertOneAsync(rawUser);
        var migration = new CommerceSchemaMigration(new() { ConnectionString = LocalServer, DatabaseName = _database });
        var check = await migration.RunAsync(false); Assert.That(check.Applied, Is.False); Assert.That(check.MissingStockQuantity, Is.EqualTo(1));
        await migration.RunAsync(true); await migration.RunAsync(true);
        Assert.That(await db.GetCollection<BsonDocument>("SchemaMigrations").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty), Is.EqualTo(1));
        Assert.That(await db.GetCollection<BsonDocument>("Products").Find(new BsonDocument("_id", rawProduct["_id"])).SingleAsync(), Is.EqualTo(rawProduct));
        Assert.That(await db.GetCollection<BsonDocument>("Users").Find(new BsonDocument("_id", rawUser["_id"])).SingleAsync(), Is.EqualTo(rawUser));
        Assert.That(await db.GetCollection<BsonDocument>("Payments").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty), Is.Zero);
    }
}
