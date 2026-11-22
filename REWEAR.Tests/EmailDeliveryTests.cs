using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using REWEAR.Infrastructure.Services;

namespace REWEAR.Tests;

[TestFixture]
public sealed class EmailDeliveryTests
{
    private const string FakeKey = "test-only-key-not-a-real-credential";
    private const string FakeOtp = "123456";
    private const string Recipient = "receiver@example.test";

    private static IConfiguration Configuration(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["EmailSettings:Provider"] = "Auto",
            ["EmailSettings:BrevoApiKey"] = FakeKey,
            ["EmailSettings:FromEmail"] = "sender@example.test"
        };
        if (overrides != null) foreach (var pair in overrides) values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static (EmailService Service, DeliveryHandler Handler, SafeLogs Logs) Sender(
        HttpStatusCode status = HttpStatusCode.Created, string body = "{\"messageId\":\"test-id\"}",
        Dictionary<string, string?>? settings = null)
    {
        var handler = new DeliveryHandler(status, body);
        var logs = new SafeLogs();
        return (new EmailService(Configuration(settings), logs, new HttpClient(handler)), handler, logs);
    }

    [Test]
    public async Task AutoUsesHttpsApiAndPreservesOtpWhileEscapingUserHtml()
    {
        var f = Sender();
        Assert.That(await f.Service.SendOtpEmailAsync(Recipient, FakeOtp, "<script>fixture</script>"), Is.True);
        Assert.That(f.Handler.Uri, Is.EqualTo("https://api.brevo.com/v3/smtp/email"));
        Assert.That(f.Handler.ApiKey, Is.EqualTo(FakeKey));
        using var payload = JsonDocument.Parse(f.Handler.Body!);
        Assert.That(payload.RootElement.GetProperty("to")[0].GetProperty("email").GetString(), Is.EqualTo(Recipient));
        var html = payload.RootElement.GetProperty("htmlContent").GetString()!;
        Assert.That(html, Does.Contain(FakeOtp));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
        Assert.That(html, Does.Not.Contain("<script>"));
        AssertSafeLogs(f.Logs);
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.Redirect)]
    public async Task ProviderErrorsAreFailuresAndNeverLogProviderBody(HttpStatusCode status)
    {
        var f = Sender(status, $"{FakeKey} {FakeOtp} {Recipient}");
        Assert.That(await f.Service.SendOtpEmailAsync(Recipient, FakeOtp, "Fixture"), Is.False);
        Assert.That(f.Handler.Calls, Is.EqualTo(1));
        Assert.That(f.Logs.Messages.Any(m => m.Contains($"HTTP_{(int)status}")), Is.True);
        AssertSafeLogs(f.Logs);
    }

    [TestCase("{}")]
    [TestCase("{\"messageId\":\"\"}")]
    [TestCase("{\"messageId\":123}")]
    [TestCase("[]")]
    [TestCase("not-json")]
    public async Task SuccessStatusWithoutValidAcceptanceDoesNotReportSuccess(string body)
    {
        var f = Sender(body: body);
        Assert.That(await f.Service.SendOtpEmailAsync(Recipient, FakeOtp, "Fixture"), Is.False);
        AssertSafeLogs(f.Logs);
    }

    [Test]
    public async Task RenderAutoWithoutApiKeyFailsFastWithoutTryingSmtp()
    {
        var f = Sender(settings: new()
        {
            ["RENDER"] = "true", ["EmailSettings:BrevoApiKey"] = null,
            ["EmailSettings:SmtpHost"] = "smtp.gmail.com", ["EmailSettings:SmtpPort"] = "587"
        });
        Assert.That(await f.Service.SendOtpEmailAsync(Recipient, FakeOtp, "Fixture"), Is.False);
        Assert.That(f.Handler.Calls, Is.Zero);
        Assert.That(f.Logs.Messages.Any(m => m.Contains("MISSING_API_KEY")), Is.True);
        AssertSafeLogs(f.Logs);
    }

    [Test]
    public async Task ExplicitSmtpLocalDoesNotRequireBrevoKey()
    {
        // Missing SMTP host fails configuration immediately, proving no HTTPS fallback or network call.
        var f = Sender(settings: new()
        { ["EmailSettings:Provider"] = "Smtp", ["EmailSettings:BrevoApiKey"] = null });
        Assert.That(await f.Service.SendOtpEmailAsync(Recipient, FakeOtp, "Fixture"), Is.False);
        Assert.That(f.Handler.Calls, Is.Zero);
        Assert.That(f.Logs.Messages.Any(m => m.Contains("MISSING_SMTP_CONFIGURATION")), Is.True);
    }

    [TestCase("invalid")]
    [TestCase(null)]
    public async Task MissingOrInvalidSenderFailsWithoutNetwork(string? sender)
    {
        var f = Sender(settings: new() { ["EmailSettings:FromEmail"] = sender });
        Assert.That(await f.Service.SendOtpEmailAsync(Recipient, FakeOtp, "Fixture"), Is.False);
        Assert.That(f.Handler.Calls, Is.Zero);
    }

    [Test]
    public async Task UnknownProviderFailsWithoutDisclosingConfiguredText()
    {
        var f = Sender(settings: new() { ["EmailSettings:Provider"] = Recipient });
        Assert.That(await f.Service.SendOtpEmailAsync(Recipient, FakeOtp, "Fixture"), Is.False);
        Assert.That(f.Handler.Calls, Is.Zero);
        AssertSafeLogs(f.Logs);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NetworkFailureAndTimeoutAreSafeDeliveryFailures(bool timeout)
    {
        var f = Sender();
        f.Handler.Failure = timeout ? new TaskCanceledException(Recipient) : new HttpRequestException(FakeKey);
        Assert.That(await f.Service.SendOtpEmailAsync(Recipient, FakeOtp, "Fixture"), Is.False);
        AssertSafeLogs(f.Logs);
    }

    private static void AssertSafeLogs(SafeLogs logs)
    {
        var output = string.Join("\n", logs.Messages);
        Assert.That(output, Does.Not.Contain(FakeKey));
        Assert.That(output, Does.Not.Contain(FakeOtp));
        Assert.That(output, Does.Not.Contain(Recipient));
    }

    private sealed class DeliveryHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls;
        public string? Uri;
        public string? ApiKey;
        public string? Body;
        public Exception? Failure;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure != null) throw Failure;
            Uri = request.RequestUri!.ToString();
            ApiKey = request.Headers.GetValues("api-key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed class SafeLogs : ILogger<EmailService>
    {
        public readonly List<string> Messages = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
