using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RMS.Core.Abstractions;
using RMS.Core.Services;
using RMS.Infrastructure.Configuration;
using RMS.Infrastructure.Delivery;
using RMS.Tests.Fakes;

namespace RMS.Tests;

public class DeliveryAndConfigurationTests
{
    // ---------------------------------------------------------------- env: references

    [Fact]
    public void Env_values_are_replaced_and_plain_values_are_kept()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RmsDb"] = "env:RMS_DB",
            ["ConnectionStrings:ErpDb"] = "Data Source=erp;User Id=x;Password=plain",
            ["Email:Smtp:Password"] = "  ENV: RMS_SMTP_PASSWORD ",
        });

        var variables = new Dictionary<string, string> { ["RMS_DB"] = "Server=db;Database=RMS", ["RMS_SMTP_PASSWORD"] = "s3cret" };
        configuration.ResolveEnvironmentReferences(name => variables.GetValueOrDefault(name));

        Assert.Equal("Server=db;Database=RMS", configuration.GetConnectionString("RmsDb"));
        Assert.Equal("Data Source=erp;User Id=x;Password=plain", configuration.GetConnectionString("ErpDb"));
        Assert.Equal("s3cret", configuration["Email:Smtp:Password"]);
    }

    [Fact]
    public void A_missing_variable_stops_startup_without_revealing_values()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RmsDb"] = "env:RMS_DB",
            ["Email:Graph:ClientSecret"] = "env:",
            ["Other"] = "secret-plain-value",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => configuration.ResolveEnvironmentReferences(_ => null));

        Assert.Contains("ConnectionStrings:RmsDb -> RMS_DB", ex.Message);
        Assert.Contains("Email:Graph:ClientSecret (no variable name", ex.Message);
        Assert.DoesNotContain("secret-plain-value", ex.Message);
    }

    // ---------------------------------------------------------------- shared folder

    [Fact]
    public async Task Files_are_saved_without_overwriting_and_only_under_allowed_roots()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-share-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new FileShareWriter(
                Options.Create(new FileShareOptions()),
                new SchedulingOptions { AllowedShareRoots = [root] },
                NullLogger<FileShareWriter>.Instance);

            var folder = Path.Combine(root, "finance", "2026");
            var first = await writer.SaveAsync(folder, "sales.csv", new MemoryStream("one"u8.ToArray()));
            var second = await writer.SaveAsync(folder, "sales.csv", new MemoryStream("two"u8.ToArray()));

            Assert.Equal(Path.Combine(folder, "sales.csv"), first);
            Assert.Equal(Path.Combine(folder, "sales (2).csv"), second);
            Assert.Equal("one", File.ReadAllText(first));
            Assert.Equal("two", File.ReadAllText(second));
            Assert.Equal(2, Directory.GetFiles(folder).Length);   // no .partial files left behind

            var outside = Path.Combine(Path.GetTempPath(), "rms-not-allowed");
            var ex = await Assert.ThrowsAsync<FileDeliveryException>(() => writer.SaveAsync(outside, "x.csv", new MemoryStream()));
            Assert.Contains("can only be saved under", ex.Message);

            await Assert.ThrowsAsync<FileDeliveryException>(() => writer.SaveAsync(folder, "../x.csv", new MemoryStream()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------- Microsoft Graph

    [Fact]
    public async Task Graph_signs_in_once_and_posts_sendMail_with_the_attachment()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, """{"token_type":"Bearer","expires_in":3599,"access_token":"tok-123"}""")
            : new HttpResponseMessage(HttpStatusCode.Accepted));
        var sender = Graph(handler, new GraphOptions { TenantId = "contoso-tenant", ClientId = "app-id", ClientSecret = "secret" });

        var message = new EmailMessage
        {
            To = ["finance@company.com"],
            Cc = ["cfo@company.com"],
            Subject = "Sales - 2026-11-01",
            HtmlBody = "<p>Attached</p>",
            TextBody = "Attached",
            Attachments = [new EmailAttachment("sales.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [1, 2, 3])],
        };
        await sender.SendAsync(message);
        await sender.SendAsync(message);

        Assert.Equal(3, handler.Requests.Count);   // one token request, reused for both messages
        var (tokenRequest, tokenBody) = handler.Requests[0];
        Assert.Equal("https://login.example.test/contoso-tenant/oauth2/v2.0/token", tokenRequest.RequestUri!.ToString());
        Assert.Contains("grant_type=client_credentials", tokenBody);
        Assert.Contains("scope=https%3A%2F%2Fgraph.microsoft.com%2F.default", tokenBody);

        var (send, sendBody) = handler.Requests[1];
        Assert.Equal("https://graph.example.test/v1.0/users/reports%40company.com/sendMail", send.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer tok-123", send.Headers.Authorization!.ToString());

        using var json = JsonDocument.Parse(sendBody);
        var mail = json.RootElement.GetProperty("message");
        Assert.Equal("Sales - 2026-11-01", mail.GetProperty("subject").GetString());
        Assert.Equal("HTML", mail.GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal("finance@company.com", mail.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
        Assert.Equal("cfo@company.com", mail.GetProperty("ccRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
        var attachment = mail.GetProperty("attachments")[0];
        Assert.Equal("#microsoft.graph.fileAttachment", attachment.GetProperty("@odata.type").GetString());
        Assert.Equal("AQID", attachment.GetProperty("contentBytes").GetString());
        Assert.False(json.RootElement.GetProperty("saveToSentItems").GetBoolean());
    }

    [Fact]
    public async Task Graph_errors_are_reported_in_plain_words()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, """{"access_token":"tok","expires_in":3600}""")
            : Json(HttpStatusCode.Forbidden, """{"error":{"code":"ErrorAccessDenied","message":"Access is denied. Check credentials and try again."}}"""));
        var sender = Graph(handler, new GraphOptions { TenantId = "t", ClientId = "c", ClientSecret = "s" });

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(new EmailMessage
        {
            To = ["a@company.com"], Subject = "s", HtmlBody = "h", TextBody = "t",
        }));

        Assert.Equal("Microsoft Graph refused the message (403): Access is denied. Check credentials and try again.", ex.Message);
    }

    [Fact]
    public async Task Graph_sign_in_errors_name_the_settings_to_check()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"AADSTS7000215: Invalid client secret provided.\r\nTrace ID: x"}"""));
        var sender = Graph(handler, new GraphOptions { TenantId = "t", ClientId = "c", ClientSecret = "wrong" });

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(new EmailMessage
        {
            To = ["a@company.com"], Subject = "s", HtmlBody = "h", TextBody = "t",
        }));

        Assert.Contains("AADSTS7000215: Invalid client secret provided.", ex.Message);
        Assert.Contains("Check Email:Graph:TenantId, ClientId and ClientSecret", ex.Message);
    }

    [Fact]
    public void Graph_attachments_are_capped_below_its_request_limit()
    {
        var sender = Graph(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)), new GraphOptions(), maxMb: 25);
        Assert.Equal(3 * 1024 * 1024, sender.MaxAttachmentBytes);
    }

    private static GraphEmailSender Graph(RecordingHandler handler, GraphOptions graph, double maxMb = 10)
    {
        graph.AuthorityHost = "https://login.example.test";
        graph.GraphBaseUrl = "https://graph.example.test/v1.0/";
        var options = Options.Create(new EmailOptions { Provider = "Graph", From = "reports@company.com", MaxAttachmentMb = maxMb, Graph = graph });
        return new GraphEmailSender(new SingleClientFactory(handler), options,
            new FixedClock(new DateTimeOffset(2026, 10, 13, 7, 0, 0, TimeSpan.Zero)), NullLogger<GraphEmailSender>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
