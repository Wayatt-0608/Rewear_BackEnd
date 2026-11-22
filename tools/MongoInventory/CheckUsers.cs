// Tool tạm: verify user seed — đọc lại document, check role/email/hash format,
// và xác nhận hash BCrypt verify được với mật khẩu "12345678".
using MongoDB.Bson;
using MongoDB.Driver;

internal static class UserCheck
{
    private const string Password = "12345678";

    public static async Task RunAsync(IMongoDatabase db)
    {
        var users = db.GetCollection<BsonDocument>("Users");

        var all = await users.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Console.WriteLine($"Tổng user trong 'Users': {all.Count}");

        // 1) Phân bố role
        Console.WriteLine("\nPhân bố role:");
        foreach (var g in all.GroupBy(u => u["role"].ToString()).OrderBy(g => g.Key))
            Console.WriteLine($"  role={g.Key,-8} {g.Count(),4}");

        // 2) Kiểm tra hash verify được cho từng user
        int ok = 0, bad = 0, missing = 0;
        foreach (var u in all)
        {
            if (!u.Contains("passwordHash") || u["passwordHash"].IsBsonNull) { missing++; continue; }
            string hash = u["passwordHash"].AsString;
            if (BCrypt.Net.BCrypt.Verify(Password, hash)) ok++;
            else
            {
                bad++;
                Console.WriteLine($"  HASH KHONG KHOP: {u["email"].AsString} role={u["role"]}");
            }
        }
        Console.WriteLine($"\nXác minh mật khẩu '{Password}': hợp={ok}  không hợp={bad}  thiếu hash={missing}");

        // 3) Kiểm tra 3 account chính
        Console.WriteLine("\nKiểm tra 3 account chính:");
        foreach (var email in new[] { "admin@gmail.com", "staff@gmail.com", "ship@gmail.com" })
        {
            var u = await users.Find(Builders<BsonDocument>.Filter.Eq("email", email)).FirstOrDefaultAsync();
            if (u == null) { Console.WriteLine($"  {email,-20} KHÔNG TỒN TẠI"); continue; }
            bool hashOk = BCrypt.Net.BCrypt.Verify(Password, u["passwordHash"].AsString);
            Console.WriteLine($"  {email,-20} name={u["fullName"].AsString,-24} role={u["role"]} verified={u["isVerified"]} hashOK={hashOk}");
        }

        // 4) Email trùng?
        var dupEmails = all.GroupBy(u => u["email"].AsString).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Console.WriteLine($"\nEmail trùng lặp: {(dupEmails.Count == 0 ? "không có" : string.Join(", ", dupEmails))}");

        // 5) Danh sách member đầu / cuối
        var members = all.Where(u => u["role"].ToInt32() == 0).ToList();
        Console.WriteLine("\nMember đầu (5):");
        foreach (var u in members.Take(5)) Console.WriteLine($"  {u["fullName"].AsString,-24} {u["email"].AsString}");
        Console.WriteLine("Member cuối (5):");
        foreach (var u in members.TakeLast(5)) Console.WriteLine($"  {u["fullName"].AsString,-24} {u["email"].AsString}");

        // 6) Role hợp lệ
        int invalidRole = all.Count(u => { int r = u["role"].ToInt32(); return r < 0 || r > 3; });
        Console.WriteLine($"\nRole ngoài 0/1/2/3: {invalidRole}");
    }
}
