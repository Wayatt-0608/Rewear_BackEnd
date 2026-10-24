using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
namespace REWEAR.Tests;
public partial class CommerceTests
{
    [Test]
    public async Task ActualApiEntrypointResolvesCommerceServicesWithIsolatedConfiguration()
    {
        // Use an empty content root and explicit test-only arguments. Never load repo appsettings/secrets.
        var contentRoot = Path.Combine(Path.GetTempPath(), "rewear-runtime-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(contentRoot);
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        const string testKey = "isolated-runtime-key-not-a-real-secret-0000000000000000000000000000";
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = contentRoot, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "REWEAR.API.dll"));
        var settings = new Dictionary<string, string> {
            ["urls"] = $"http://127.0.0.1:{port}", ["environment"] = "Development",
            ["MongoDbSettings:ConnectionString"] = LocalServer, ["MongoDbSettings:DatabaseName"] = _database,
            ["JwtSettings:SecretKey"] = testKey, ["JwtSettings:Issuer"] = "runtime-test", ["JwtSettings:Audience"] = "runtime-test",
            ["Cloudinary:CloudName"] = "test-only", ["Cloudinary:ApiKey"] = "test-only", ["Cloudinary:ApiSecret"] = "test-only",
            ["PayOs:ClientId"] = "test-only", ["PayOs:ApiKey"] = "test-only", ["PayOs:ChecksumKey"] = "test-only",
            ["PayOs:BaseUrl"] = "https://api-merchant.payos.vn", ["PayOs:AppBaseUrl"] = "https://example.test",
            ["Logging:LogLevel:Default"] = "Warning" };
        foreach (var pair in settings) { start.ArgumentList.Add("--" + pair.Key); start.ArgumentList.Add(pair.Value); }
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            bool ready = false;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                if (process.HasExited) break;
                try { ready = (await client.GetAsync("/health")).StatusCode == HttpStatusCode.OK; if (ready) break; }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            if (!ready) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); Assert.Fail("API startup failed: " + await stdout + await stderr); }
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(testKey));
            var token = new JwtSecurityToken("runtime-test", "runtime-test", new[] { new Claim(ClaimTypes.NameIdentifier, "111111111111111111111111"), new Claim(ClaimTypes.Role, "Admin") },
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(key, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            foreach (var path in new[] { "/api/operations/inventory", "/api/operations/metrics", "/api/orders", "/api/Sourcing" })
            { var response = await client.GetAsync(path); Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path + " " + await response.Content.ReadAsStringAsync()); }
            using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/operations/inventory");
            preflight.Headers.Add("Origin", "http://localhost:5173"); preflight.Headers.Add("Access-Control-Request-Method", "GET");
            var cors = await client.SendAsync(preflight);
            Assert.That(cors.Headers.GetValues("Access-Control-Allow-Origin").Single(), Is.EqualTo("http://localhost:5173"));
            for (int attempt = 0; attempt < 5; attempt++) await client.PostAsync("/api/Auth/login", new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.That((await client.PostAsync("/api/Auth/login", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        }
        finally
        {
            if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await stdout; await stderr;
            Directory.Delete(contentRoot, false);
        }
    }
}
