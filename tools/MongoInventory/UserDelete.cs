// Tool tạm: xóa user theo email cụ thể.
// Usage: dotnet run --project tools/MongoInventory -- deleteuser <dbName> <email>
using MongoDB.Bson;
using MongoDB.Driver;

internal static class UserDelete
{
    public static async Task RunAsync(IMongoDatabase db, string email)
    {
        var users = db.GetCollection<BsonDocument>("Users");
        var result = await users.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("email", email.ToLower().Trim()));
        Console.WriteLine(result.DeletedCount > 0
            ? $"Đã xóa user '{email}'."
            : $"Không tìm thấy user '{email}'.");
    }
}
