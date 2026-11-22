// Tool tạm: quản lý dữ liệu MongoDB cho dev/test. Xoá file này sau khi dùng.
// Usage: dotnet run --project tools/MongoInventory -- inventory <dbName>
//        dotnet run --project tools/MongoInventory -- verify <dbName>
//        dotnet run --project tools/MongoInventory -- wipe <dbName> <backupDir>
//        dotnet run --project tools/MongoInventory -- seed <dbName> [memberCount]
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

const string ConnectionString = "mongodb+srv://phanquoclam682004_db_user:Wayatt0608@cluster0.isat8sx.mongodb.net/?appName=Cluster0";
const string DefaultDb = "rewear_db";

var mode = args.Length > 0 ? args[0] : "inventory";
var dbName = args.Length > 1 ? args[1] : DefaultDb;
var backupDir = args.Length > 2 ? args[2] : Path.Combine("backup", dbName);

var client = new MongoClient(ConnectionString);
var db = client.GetDatabase(dbName);

if (mode == "verify")
{
    var names = (await db.ListCollectionNamesAsync()).ToList();
    long total = 0;
    foreach (var n in names.OrderBy(x => x))
    {
        var c = await db.GetCollection<BsonDocument>(n).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        total += c;
        Console.WriteLine($"{n,-32} {c,8}");
    }
    Console.WriteLine(new string('-', 42));
    Console.WriteLine($"{"TOTAL",-32} {total,8}");
    return;
}

if (mode == "inventory")
{
    var names = (await db.ListCollectionNamesAsync()).ToList();
    foreach (var n in names.OrderBy(x => x))
        Console.WriteLine($"{n,-32} {await db.GetCollection<BsonDocument>(n).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty),8}");
    return;
}

if (mode == "otp")
{
    var email = args.Length > 2 ? args[2] : "";
    if (string.IsNullOrWhiteSpace(email)) { Console.Error.WriteLine("Thieu email."); return; }
    var users = db.GetCollection<BsonDocument>("Users");
    var found = await users.Find(Builders<BsonDocument>.Filter.Eq("email", email)).FirstOrDefaultAsync();
    if (found == null) { Console.WriteLine("Khong tim thay user: " + email); return; }

    bool hasOtp = found.Contains("otpCode") && !found["otpCode"].IsBsonNull && !string.IsNullOrEmpty(found["otpCode"].AsString);
    bool verified = found.Contains("isVerified") && !found["isVerified"].IsBsonNull && found["isVerified"].ToBoolean();
    Console.WriteLine($"email       = {email}");
    Console.WriteLine($"isVerified  = {verified}");
    Console.WriteLine($"otpCode     = {(hasOtp ? "da luu (6 chu so)" : "khong co")}");
    Console.WriteLine($"otpExpires  = {(found.Contains("otpExpiresAt") && !found["otpExpiresAt"].IsBsonNull ? found["otpExpiresAt"].ToString() : "(null)")}");
    return;
}

if (mode == "smtp")
{
    if (args.Length < 3) { Console.Error.WriteLine("Thieu email nhan."); return; }
    await SmtpCheck.RunAsync(args[2]);
    return;
}

if (mode == "deleteuser")
{
    var email = args.Length > 2 ? args[2] : "";
    if (string.IsNullOrWhiteSpace(email)) { Console.Error.WriteLine("Thiếu email."); return; }
    await UserDelete.RunAsync(db, email);
    return;
}

if (mode == "listusers")
{
    await UserList.RunAsync(db);
    return;
}

if (mode == "checkusers")
{
    await UserCheck.RunAsync(db);
    return;
}

if (mode == "preview")
{
    int previewCount = 100;
    if (args.Length > 2 && int.TryParse(args[2], out var parsed)) previewCount = parsed;
    UserSeed.Preview(previewCount);
    return;
}

if (mode == "seed")
{
    int memberCount = 100;
    if (args.Length > 2 && int.TryParse(args[2], out var parsed)) memberCount = parsed;
    // Cờ "replace" (đối số 4) xóa toàn bộ user cũ trước khi seed.
    bool replaceAll = args.Length > 3 && args[3].Equals("replace", StringComparison.OrdinalIgnoreCase);
    await UserSeed.SeedAsync(db, memberCount, replaceAll);
    return;
}

if (mode == "fullseed")
{
    await FullSeed.RunAsync(db);
    return;
}

if (mode != "wipe") { Console.Error.WriteLine($"Unknown mode: {mode}"); return; }

// ====== WIPE ======
Console.WriteLine($"Target database: {dbName}");

// 1) Backup toàn bộ document ra file JSON (mỗi collection 1 file).
Directory.CreateDirectory(backupDir);
// MongoDB.Bson serialization (Extended JSON) thay cho System.Text.Json,
// vì System.Text.Json không serialize được BSON (BsonObjectId -> BsonBoolean lỗi cast).
var bsonJson = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };

var collections = (await db.ListCollectionNamesAsync()).ToList();
foreach (var name in collections)
{
    var docs = await db.GetCollection<BsonDocument>(name)
        .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
    var file = Path.Combine(backupDir, $"{name}.json");
    using var writer = new StreamWriter(file);
    writer.WriteLine("[");
    for (int i = 0; i < docs.Count; i++)
    {
        writer.WriteLine(docs[i].ToJson(bsonJson));
        writer.WriteLine(i < docs.Count - 1 ? "," : "");
    }
    writer.WriteLine("]");
    Console.WriteLine($"  backup {name,-32} {docs.Count,6} docs -> {file}");
}
Console.WriteLine($"Backup xong: {backupDir}");
Console.WriteLine();

// 2) Xóa toàn bộ document trong từng collection (drop document, GIỮ collection + index).
foreach (var name in collections)
{
    var result = await db.GetCollection<BsonDocument>(name)
        .DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
    Console.WriteLine($"  wiped {name,-32} deleted={result.DeletedCount}");
}

// 3) Xác minh: không còn document nào, index vẫn còn.
Console.WriteLine();
long grand = 0;
foreach (var name in (await db.ListCollectionNamesAsync()).ToList().OrderBy(x => x))
{
    var c = await db.GetCollection<BsonDocument>(name).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
    grand += c;
    var idx = (await db.GetCollection<BsonDocument>(name).Indexes.ListAsync()).ToList();
    Console.WriteLine($"{name,-32} docs={c,6}  indexes={idx.Count}");
}
Console.WriteLine(new string('-', 60));
Console.WriteLine($"TOTAL DOCUMENTS REMAINING: {grand}");
