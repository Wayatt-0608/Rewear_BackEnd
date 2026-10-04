using MongoDB.Driver;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Persistence;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(MongoDbSettings settings)
    {
        var client = new MongoClient(settings.ConnectionString);
        _database = client.GetDatabase(settings.DatabaseName);
    }

    public IMongoCollection<Product> Products =>
        _database.GetCollection<Product>("Products");

    public IMongoCollection<User> Users =>
        _database.GetCollection<User>("Users");

    public IMongoCollection<Brand> Brands =>
        _database.GetCollection<Brand>("Brands");

    public IMongoCollection<Category> Categories =>
        _database.GetCollection<Category>("Categories");

    public IMongoCollection<Address> Addresses =>
        _database.GetCollection<Address>("Addresses");
}
