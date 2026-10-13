using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using RMS.Core.Abstractions;

namespace RMS.Infrastructure.Delivery;

/// <summary>Bound from the "Email" configuration section of the scheduler service.</summary>
public sealed class EmailOptions
{
    public const string SmtpProvider = "Smtp";
    public const string GraphProvider = "Graph";
    public const string NoProvider = "None";

    /// <summary>
    /// "Smtp" for an SMTP server (Exchange, or Office 365 with SMTP AUTH allowed for the mailbox), "Graph" for
    /// Microsoft 365 through the Graph API (use it when SMTP sign-in is switched off), or "None".
    /// </summary>
    public string Provider { get; set; } = NoProvider;

    /// <summary>Sender address, for example reports@company.com. With Graph it must be a real mailbox.</summary>
    public string From { get; set; } = "";

    public string FromName { get; set; } = "Reports Management";

    /// <summary>Largest file attached to an email. Bigger files are only saved to the share, and the email gives the path.</summary>
    public double MaxAttachmentMb { get; set; } = 10;

    public SmtpOptions Smtp { get; set; } = new();
    public GraphOptions Graph { get; set; } = new();
}

public sealed class SmtpOptions
{
    /// <summary>For example smtp.office365.com, or the company's Exchange server.</summary>
    public string Host { get; set; } = "";

    /// <summary>587 for STARTTLS (Office 365), 465 for SSL on connect, 25 for an internal relay.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Auto, None, StartTls, StartTlsWhenAvailable or SslOnConnect.</summary>
    public string Security { get; set; } = "Auto";

    /// <summary>Leave empty for an anonymous internal relay.</summary>
    public string? Username { get; set; }

    /// <summary>Use "env:VARIABLE_NAME" to keep it out of the file.</summary>
    public string? Password { get; set; }

    /// <summary>Only for a server with a self-signed certificate on a trusted network.</summary>
    public bool IgnoreCertificateErrors { get; set; }

    public int TimeoutSeconds { get; set; } = 100;
}

public sealed class GraphOptions
{
    /// <summary>Directory (tenant) Id of the Microsoft 365 organisation.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>Application (client) Id of the app registration that has the Mail.Send application permission.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Client secret of that app registration. Use "env:VARIABLE_NAME" to keep it out of the file.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Mailbox the mail is sent from. Empty means <see cref="EmailOptions.From"/>.</summary>
    public string? SenderMailbox { get; set; }

    public bool SaveToSentItems { get; set; }

    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";
    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
}

internal static class EmailLimits
{
    // Graph's sendMail takes the whole message as one request of at most 4 MB, and attachments are base64 encoded.
    public const long GraphMaxAttachmentBytes = 3 * 1024 * 1024;

    public static long FromMegabytes(double megabytes) => (long)(Math.Max(0.1, megabytes) * 1024 * 1024);
}

/// <summary>Used when Email:Provider is "None": a schedule that asks for email fails with a clear message.</summary>
internal sealed class DisabledEmailSender : IEmailSender
{
    public string Name => "email";
    public long MaxAttachmentBytes => long.MaxValue;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default) =>
        throw new EmailDeliveryException(
            "Email is not set up for the scheduler service. Set Email:Provider to Smtp or Graph in its appsettings.json.");
}

/// <summary>Sends through an SMTP server with MailKit.</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public string Name => "SMTP";
    public long MaxAttachmentBytes => EmailLimits.FromMegabytes(_options.MaxAttachmentMb);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var smtp = _options.Smtp;
        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(_options.From))
        {
            throw new EmailDeliveryException("Email:Smtp:Host and Email:From must be set in the scheduler's appsettings.json.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.From));
        foreach (var address in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(address));
        }
        foreach (var address in message.Cc)
        {
            mime.Cc.Add(MailboxAddress.Parse(address));
        }
        mime.Subject = message.Subject;

        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var attachment in message.Attachments)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }
        mime.Body = body.ToMessageBody();

        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = Math.Max(10, smtp.TimeoutSeconds) * 1000 };
        if (smtp.IgnoreCertificateErrors)
        {
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        }

        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, ParseSecurity(smtp.Security), ct);

            if (!string.IsNullOrWhiteSpace(smtp.Username))
            {
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? "", ct);
            }

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (AuthenticationException ex)
        {
            logger.LogWarning(ex, "SMTP sign-in failed for {Username} on {Host}", smtp.Username, smtp.Host);
            // 5.7.139: SMTP AUTH switched off for the mailbox or tenant. 5.7.30: Basic authentication no longer accepted.
            var hint = ex.Message.Contains("5.7.139", StringComparison.Ordinal)
                       || ex.Message.Contains("5.7.30", StringComparison.Ordinal)
                       || ex.Message.Contains("SmtpClientAuthentication", StringComparison.OrdinalIgnoreCase)
                       || ex.Message.Contains("basic authentication", StringComparison.OrdinalIgnoreCase)
                ? " Microsoft 365 no longer accepts a username and password over SMTP for this mailbox (SMTP AUTH or Basic "
                  + "authentication is switched off). Set Email:Provider to Graph, or ask for SMTP AUTH to be allowed for this mailbox."
                : " Check Email:Smtp:Username and Password.";
            throw new EmailDeliveryException("The SMTP server refused the sign-in." + hint, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not EmailDeliveryException)
        {
            logger.LogWarning(ex, "Sending email through {Host}:{Port} failed", smtp.Host, smtp.Port);
            throw new EmailDeliveryException($"Sending through {smtp.Host}:{smtp.Port} failed: {ex.Message}", ex);
        }
    }

    private static SecureSocketOptions ParseSecurity(string? value) =>
        Enum.TryParse<SecureSocketOptions>(value, ignoreCase: true, out var option) ? option : SecureSocketOptions.Auto;
}

/// <summary>
/// Sends through Microsoft Graph (POST /users/{mailbox}/sendMail) with an app registration's client credentials.
/// Needs the Mail.Send application permission with admin consent; an Exchange application access policy can limit
/// the app to the one sender mailbox.
/// </summary>
internal sealed class GraphEmailSender(
    IHttpClientFactory httpClients,
    IOptions<EmailOptions> options,
    TimeProvider clock,
    ILogger<GraphEmailSender> logger) : IEmailSender
{
    public const string HttpClientName = "graph-mail";

    private readonly EmailOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;

    public string Name => "Microsoft Graph";

    public long MaxAttachmentBytes =>
        Math.Min(EmailLimits.GraphMaxAttachmentBytes, EmailLimits.FromMegabytes(_options.MaxAttachmentMb));

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var graph = _options.Graph;
        var mailbox = string.IsNullOrWhiteSpace(graph.SenderMailbox) ? _options.From : graph.SenderMailbox;
        if (string.IsNullOrWhiteSpace(graph.TenantId) || string.IsNullOrWhiteSpace(graph.ClientId)
            || string.IsNullOrWhiteSpace(graph.ClientSecret) || string.IsNullOrWhiteSpace(mailbox))
        {
            throw new EmailDeliveryException(
                "Email:Graph:TenantId, ClientId, ClientSecret and a sender mailbox (Email:From) must be set in the scheduler's appsettings.json.");
        }

        var payload = new GraphSendMail
        {
            SaveToSentItems = graph.SaveToSentItems,
            Message = new GraphMessage
            {
                Subject = message.Subject,
                Body = new GraphBody { ContentType = "HTML", Content = message.HtmlBody },
                ToRecipients = [.. message.To.Select(GraphRecipient.For)],
                CcRecipients = [.. message.Cc.Select(GraphRecipient.For)],
                Attachments = [.. message.Attachments.Select(a => new GraphAttachment
                {
                    Name = a.FileName,
                    ContentType = a.ContentType,
                    ContentBytes = Convert.ToBase64String(a.Content),
                })],
            },
        };

        var client = httpClients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{graph.GraphBaseUrl.TrimEnd('/')}/users/{Uri.EscapeDataString(mailbox)}/sendMail")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(client, graph, ct));

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new EmailDeliveryException("Microsoft Graph could not be reached: " + ex.Message, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorAsync(response, ct);
                logger.LogWarning("Graph sendMail for {Mailbox} returned {Status}: {Detail}", mailbox, (int)response.StatusCode, detail);
                throw new EmailDeliveryException($"Microsoft Graph refused the message ({(int)response.StatusCode}): {detail}");
            }
        }
    }

    private async Task<string> GetTokenAsync(HttpClient client, GraphOptions graph, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && clock.GetUtcNow() < _tokenExpires)
            {
                return _token;
            }

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = graph.ClientId,
                ["client_secret"] = graph.ClientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials",
            });

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(
                    $"{graph.AuthorityHost.TrimEnd('/')}/{Uri.EscapeDataString(graph.TenantId)}/oauth2/v2.0/token", form, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new EmailDeliveryException("The Microsoft sign-in service could not be reached: " + ex.Message, ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await ReadErrorAsync(response, ct);
                    throw new EmailDeliveryException(
                        $"Microsoft 365 refused the app sign-in ({(int)response.StatusCode}): {detail}. Check Email:Graph:TenantId, ClientId and ClientSecret.");
                }

                var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                    ?? throw new EmailDeliveryException("Microsoft 365 returned an empty sign-in response.");
                _token = token.AccessToken;
                _tokenExpires = clock.GetUtcNow().AddSeconds(Math.Max(60, token.ExpiresIn - 300));
                return _token;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? text;
                }
                if (root.TryGetProperty("error_description", out var description))
                {
                    return (description.GetString() ?? text).Split('\n')[0].Trim();
                }
            }
        }
        catch (JsonException)
        {
        }
        return text.Length > 300 ? text[..300] : text;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class GraphSendMail
    {
        [JsonPropertyName("message")] public required GraphMessage Message { get; init; }
        [JsonPropertyName("saveToSentItems")] public bool SaveToSentItems { get; init; }
    }

    private sealed class GraphMessage
    {
        [JsonPropertyName("subject")] public required string Subject { get; init; }
        [JsonPropertyName("body")] public required GraphBody Body { get; init; }
        [JsonPropertyName("toRecipients")] public List<GraphRecipient> ToRecipients { get; init; } = [];
        [JsonPropertyName("ccRecipients")] public List<GraphRecipient> CcRecipients { get; init; } = [];
        [JsonPropertyName("attachments")] public List<GraphAttachment> Attachments { get; init; } = [];
    }

    private sealed class GraphBody
    {
        [JsonPropertyName("contentType")] public required string ContentType { get; init; }
        [JsonPropertyName("content")] public required string Content { get; init; }
    }

    private sealed class GraphRecipient
    {
        [JsonPropertyName("emailAddress")] public required GraphAddress EmailAddress { get; init; }

        public static GraphRecipient For(string address) => new() { EmailAddress = new GraphAddress { Address = address } };
    }

    private sealed class GraphAddress
    {
        [JsonPropertyName("address")] public required string Address { get; init; }
    }

    private sealed class GraphAttachment
    {
        [JsonPropertyName("@odata.type")] public string ODataType { get; init; } = "#microsoft.graph.fileAttachment";
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("contentType")] public required string ContentType { get; init; }
        [JsonPropertyName("contentBytes")] public required string ContentBytes { get; init; }
    }
}
