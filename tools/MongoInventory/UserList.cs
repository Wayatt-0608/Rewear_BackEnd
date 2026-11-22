// Tool tạm: liệt kê user theo email + createdAt để debug.
using MongoDB.Bson;
using MongoDB.Driver;

internal static class UserList
{
    public static async Task RunAsync(IMongoDatabase db)
    {
        var users = db.GetCollection<BsonDocument>("Users");
        var all = await users.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        foreach (var u in all.OrderBy(u => u["createdAt"].ToUniversalTime()))
        {
            string created = u["createdAt"].ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"{created}  role={u["role"],-3} {u["email"].AsString,-38} {u["fullName"].AsString}");
        }
    }
}
