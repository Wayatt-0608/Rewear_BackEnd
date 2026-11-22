using Microsoft.AspNetCore.Builder;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using REWEAR.API.Controllers;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Application.Services;
using REWEAR.Domain.Entities;

namespace REWEAR.Tests;

/// <summary>Auth regression tests without MongoDB, real email, production config or credentials.</summary>
[TestFixture]
public sealed class AuthEmailRecoveryTests
{
    private RecoveryUsers _users = null!;
    private RecoveryEmail _email = null!;
    private AuthService _auth = null!;

    [SetUp]
    public void Setup()
    {
        _users = new();
        _email = new();
        _auth = new(_users, _email, new JwtSettings
        { SecretKey = new string('t', 64), Issuer = "test", Audience = "test" }, new RecoveryTransactions());
    }

    private static RegisterRequest Registration(string password = "test-only-password") => new()
    {
        FullName = "Test fixture", Email = "signup@example.test",
        Password = password, PasswordConfirm = password, Role = "Admin"
    };

    [Test]
    public async Task FailedEmailCanBeRetriedWithoutDuplicatingOrReplacingTheAccount()
    {
        _email.Succeed = false;
        var first = await _auth.RegisterAsync(Registration());
        Assert.That(first.ErrorCode, Is.EqualTo("EMAIL_DELIVERY_FAILED"));
        Assert.That(first.RequiresVerification, Is.True);
        var saved = _users.Rows.Values.Single();
        var id = saved.Id;
        var hash = saved.PasswordHash;
        var oldOtp = saved.OtpCode;
        Assert.That(saved.IsVerified, Is.False);
        Assert.That(saved.Role, Is.EqualTo(UserRole.Member));

        saved.OtpIssuedAt = DateTime.UtcNow.AddMinutes(-2);
        saved.OtpAttempts = 5;
        _email.Succeed = true;
        var retry = Registration();
        retry.FullName = "Must not overwrite";
        var result = await _auth.RegisterAsync(retry);
        Assert.That(result.Success, Is.True);
        Assert.That(result.UserId, Is.EqualTo(id));
        Assert.That(_users.Rows, Has.Count.EqualTo(1));
        Assert.That(saved.PasswordHash, Is.EqualTo(hash));
        Assert.That(saved.FullName, Is.EqualTo("Test fixture"));
        Assert.That(saved.OtpAttempts, Is.Zero);
        Assert.That(saved.OtpPurpose, Is.EqualTo("Registration"));
        Assert.That(_email.Calls, Is.EqualTo(2));

        var verification = await _auth.VerifyOtpAsync(new() { Email = saved.Email, OtpCode = _email.LastOtp! });
        Assert.That(verification.Success, Is.True);
        Assert.That(verification.Token, Is.Not.Empty);
        Assert.That(_users.Rows[id].IsVerified, Is.True);
        Assert.That(_users.Rows[id].OtpCode, Is.Null);
        Assert.That((await _auth.VerifyOtpAsync(new() { Email = saved.Email, OtpCode = _email.LastOtp! })).Success, Is.False);
    }

    [Test]
    public async Task PendingSignupWithDifferentPasswordCannotOverwriteOrSendMail()
    {
        await _auth.RegisterAsync(Registration());
        var saved = _users.Rows.Values.Single();
        var hash = saved.PasswordHash;
        saved.OtpIssuedAt = DateTime.UtcNow.AddMinutes(-2);
        Assert.That((await _auth.RegisterAsync(Registration("different-test-password"))).Success, Is.False);
        Assert.That(saved.PasswordHash, Is.EqualTo(hash));
        Assert.That(_email.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task CooldownAppliesAcrossSignupResendAndForgotPassword()
    {
        await _auth.RegisterAsync(Registration());
        var results = new[]
        {
            await _auth.RegisterAsync(Registration()),
            await _auth.ResendOtpAsync(" SIGNUP@example.test "),
            await _auth.ForgotPasswordAsync("signup@example.test")
        };
        Assert.That(results.All(r => r.ErrorCode == "OTP_RESEND_COOLDOWN"), Is.True);
        Assert.That(results.All(r => r.RetryAfterSeconds is > 0 and <= 60), Is.True);
        Assert.That(_email.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task ExistingUnverifiedLegacyAccountCanRequestFreshOtp()
    {
        var user = new User { Email = "legacy@example.test", FullName = "Fixture", PasswordHash = "unchanged",
            Role = UserRole.Staff, OtpAttempts = 5, OtpCode = "old-fixture-code" };
        _users.Rows[user.Id] = user;
        var result = await _auth.ResendOtpAsync(user.Email);
        Assert.That(result.Success, Is.True);
        Assert.That(user.PasswordHash, Is.EqualTo("unchanged"));
        Assert.That(user.Role, Is.EqualTo(UserRole.Staff));
        Assert.That(user.OtpAttempts, Is.Zero);
        Assert.That(user.OtpPurpose, Is.EqualTo("Registration"));
        Assert.That(user.OtpExpiresAt, Is.GreaterThan(DateTime.UtcNow.AddMinutes(4)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ResendDoesNotSendForUnknownOrAlreadyVerifiedAccounts(bool exists)
    {
        if (exists) await _auth.RegisterAsync(Registration());
        if (exists) _users.Rows.Values.Single().IsVerified = true;
        int previousCalls = _email.Calls;
        var result = await _auth.ResendOtpAsync("signup@example.test");
        Assert.That(result.Success, Is.True);
        Assert.That(result.UserId, Is.Null);
        Assert.That(_email.Calls, Is.EqualTo(previousCalls));
    }

    [Test]
    public async Task ConcurrentResendsIssueOnlyOneMessage()
    {
        await _auth.RegisterAsync(Registration());
        _users.Rows.Values.Single().OtpIssuedAt = DateTime.UtcNow.AddMinutes(-2);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _auth.ResendOtpAsync("signup@example.test")));
        Assert.That(results.Count(r => r.Success), Is.EqualTo(1));
        Assert.That(_email.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task RegistrationOtpCannotResetPassword()
    {
        await _auth.RegisterAsync(Registration());
        var hash = _users.Rows.Values.Single().PasswordHash;
        var result = await _auth.ResetPasswordAsync(new()
        { Email = "signup@example.test", OtpCode = _email.LastOtp!, NewPassword = "changed-test-password", NewPasswordConfirm = "changed-test-password" });
        Assert.That(result.Success, Is.False);
        Assert.That(_users.Rows.Values.Single().PasswordHash, Is.EqualTo(hash));
    }

    [Test]
    public async Task ResetOtpCannotActivateRegistrationButCanResetPassword()
    {
        await _auth.RegisterAsync(Registration());
        _users.Rows.Values.Single().OtpIssuedAt = DateTime.UtcNow.AddMinutes(-2);
        Assert.That((await _auth.ForgotPasswordAsync("signup@example.test")).Success, Is.True);
        Assert.That((await _auth.VerifyOtpAsync(new() { Email = "signup@example.test", OtpCode = _email.LastOtp! })).Success, Is.False);
        var result = await _auth.ResetPasswordAsync(new()
        { Email = "signup@example.test", OtpCode = _email.LastOtp!, NewPassword = "changed-test-password", NewPasswordConfirm = "changed-test-password" });
        Assert.That(result.Success, Is.True);
        Assert.That(BCrypt.Net.BCrypt.Verify("changed-test-password", _users.Rows.Values.Single().PasswordHash), Is.True);
        Assert.That(_users.Rows.Values.Single().IsVerified, Is.False);
    }

    [Test]
    public async Task ControllerDistinguishesMailFailureCooldownAndValidation()
    {
        _email.Succeed = false;
        var controller = new AuthController(_auth) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var failed = (ObjectResult)await controller.Register(Registration());
        Assert.That(failed.StatusCode, Is.EqualTo(503));
        var throttled = (ObjectResult)await controller.ResendOtp(new() { Email = "signup@example.test" });
        Assert.That(throttled.StatusCode, Is.EqualTo(429));
        Assert.That(controller.Response.Headers.RetryAfter.ToString(), Is.Not.Empty);
        var invalid = (ObjectResult)await controller.ResendOtp(new() { Email = "invalid" });
        Assert.That(invalid.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task NewlyVerifiedAccountStillRejectsDuplicateRegistration()
    {
        await _auth.RegisterAsync(Registration());
        _users.Rows.Values.Single().IsVerified = true;
        Assert.That((await _auth.RegisterAsync(Registration())).Success, Is.False);
        Assert.That(_email.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task HttpSignupRecoveryResendAndVerificationUseActualControllerRoutes()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions
        { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath(), Args = Array.Empty<string>() });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IAuthService>(_auth);
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("auth", limiter =>
        { limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1); }));
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapControllers();
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses;
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Single()) };
        _email.Succeed = false;
        var failed = await client.PostAsJsonAsync("/api/Auth/register", Registration());
        Assert.That((int)failed.StatusCode, Is.EqualTo(503));
        var failedBody = await failed.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.That(failedBody!.RequiresVerification, Is.True);
        var throttled = await client.PostAsJsonAsync("/api/Auth/resend-otp", new { email = "signup@example.test" });
        Assert.That((int)throttled.StatusCode, Is.EqualTo(429));
        Assert.That(throttled.Headers.RetryAfter, Is.Not.Null);
        _users.Rows.Values.Single().OtpIssuedAt = DateTime.UtcNow.AddMinutes(-2);
        _email.Succeed = true;
        var retry = await client.PostAsJsonAsync("/api/Auth/register", Registration());
        Assert.That((int)retry.StatusCode, Is.EqualTo(200));
        var verified = await client.PostAsJsonAsync("/api/Auth/verify-otp", new { email = "signup@example.test", otpCode = _email.LastOtp });
        Assert.That((int)verified.StatusCode, Is.EqualTo(200));
        var loggedIn = await client.PostAsJsonAsync("/api/Auth/login", new { email = "signup@example.test", password = "test-only-password" });
        Assert.That((int)loggedIn.StatusCode, Is.EqualTo(200));
        await app.StopAsync();
    }

    private sealed class RecoveryEmail : IEmailService
    {
        public bool Succeed = true;
        public int Calls;
        public string? LastOtp;
        public Task<bool> SendOtpEmailAsync(string email, string otp, string name)
        { Calls++; LastOtp = otp; return Task.FromResult(Succeed); }
    }

    private sealed class RecoveryTransactions : ICommerceUnitOfWork
    { public Task<T> ExecuteAsync<T>(Func<Task<T>> operation) => operation(); }

    private sealed class RecoveryUsers : IUserRepository
    {
        public readonly Dictionary<string, User> Rows = new();
        private readonly object _gate = new();
        private static User Copy(User user) => JsonSerializer.Deserialize<User>(JsonSerializer.Serialize(user))!;
        public Task<User?> GetByIdAsync(string id)
        { lock (_gate) return Task.FromResult(Rows.TryGetValue(id, out var user) ? Copy(user) : null); }
        public Task<User?> GetByEmailAsync(string email)
        { lock (_gate) { var user = Rows.Values.FirstOrDefault(u => u.Email == email); return Task.FromResult(user == null ? null : Copy(user)); } }
        public Task<List<User>> GetAllAsync()
        { lock (_gate) return Task.FromResult(Rows.Values.Select(Copy).ToList()); }
        public Task CreateAsync(User user)
        { lock (_gate) Rows.Add(user.Id, Copy(user)); return Task.CompletedTask; }
        public Task UpdateAsync(User user)
        { lock (_gate) Rows[user.Id] = Copy(user); return Task.CompletedTask; }
        public Task DeleteAsync(string id)
        { lock (_gate) Rows.Remove(id); return Task.CompletedTask; }
        public async Task<bool> TryIssueOtpAsync(User expected, string otp, DateTime expiry, string purpose, DateTime issued)
        {
            await Task.Yield(); // Let requests compete with snapshots read before another issuer writes.
            lock (_gate)
            {
                if (!Rows.TryGetValue(expected.Id, out var user) || user.IsVerified != expected.IsVerified
                    || user.OtpCode != expected.OtpCode || user.OtpIssuedAt > issued.AddSeconds(-60))
                    return false;
                user.OtpCode = otp; user.OtpExpiresAt = expiry; user.OtpPurpose = purpose;
                user.OtpIssuedAt = issued; user.OtpAttempts = 0;
                return true;
            }
        }
    }
}
