using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedMateAI.Application.Common;
using MedMateAI.Application.Helpers;
using MedMateAI.Application.IService;
using MedMateAI.Infrastructure.Auth.Providers;
using MedMateAI.Infrastructure.Email.Brevo.Models;
using MedMateAI.Infrastructure.Email.Brevo.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedMateAI.Infrastructure.Email.Brevo;

public sealed class BrevoEmailSender : IEmailSender, IEmailOtpSender
{
    private const string OtpEmailSubject = "Mã OTP xác thực tài khoản";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly BrevoOptions _options;
    private readonly ILogger<BrevoEmailSender> _logger;

    public BrevoEmailSender(
        HttpClient httpClient,
        IOptions<BrevoOptions> options,
        ILogger<BrevoEmailSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(bool Success, string? OtpCode)> SendOtpEmailAsync(
        string toEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return (false, null);
        }

        var otpCode = OtpCodeGenerator.CreateNumeric(6);
        var htmlContent = BuildOtpEmailHtml(otpCode);

        try
        {
            await SendAsync(toEmail, OtpEmailSubject, htmlContent, cancellationToken);
            return (true, otpCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Brevo SMTP API request failed.");
            return (false, null);
        }
    }

    public async Task SendAsync(
        string toEmail,
        string subject,
        string htmlContent,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        if (string.IsNullOrWhiteSpace(toEmail))
        {
            throw new ArgumentException("Recipient email is required.", nameof(toEmail));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Email subject is required.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(htmlContent))
        {
            throw new ArgumentException("Email html content is required.", nameof(htmlContent));
        }

        var payload = new BrevoSendEmailRequest
        {
            Sender = new BrevoSender
            {
                Name = _options.SenderName.Trim(),
                Email = _options.SenderEmail.Trim(),
            },
            To = [new BrevoRecipient { Email = toEmail.Trim() }],
            Subject = subject.Trim(),
            HtmlContent = htmlContent,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiUrl.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey.Trim());
        request.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await SendBrevoRequestAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = (int)response.StatusCode;
        _logger.LogWarning(
            "Brevo SMTP API failed with status code {StatusCode}. Response: {ResponseBody}",
            statusCode,
            Truncate(responseBody, 500));

        var message = $"Brevo SMTP API failed with status code {statusCode}.";
        if (BackgroundJobRetry.IsTransientHttpStatusCode(statusCode))
        {
            throw new TransientRemoteCallException(message, statusCode);
        }

        throw new InvalidOperationException(message);
    }

    private async Task<HttpResponseMessage> SendBrevoRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or IOException)
        {
            _logger.LogError(ex, "Brevo SMTP API request failed.");
            throw new TransientRemoteCallException("Brevo SMTP API request failed.", innerException: ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Brevo SMTP API request failed.");
            throw;
        }
    }

    private static string BuildOtpEmailHtml(string otpCode)
    {
        return
            $"""
            <div style='font-family: Arial, sans-serif; padding: 20px; border: 1px solid #eee;'>
                <h2>Xác thực tài khoản</h2>
                <p>Mã OTP của bạn là: <strong style='font-size: 24px; color: #2d89ef;'>{otpCode}</strong></p>
                <p>Mã này sẽ hết hạn sau 1 phút. Vui lòng không chia sẻ mã này với bất kỳ ai.</p>
            </div>
            """;
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.Equals(_options.ApiKey, "YOUR_BREVO_API_KEY", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Brevo:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.SenderEmail))
        {
            throw new InvalidOperationException("Brevo:SenderEmail is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.SenderName))
        {
            throw new InvalidOperationException("Brevo:SenderName is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
        {
            throw new InvalidOperationException("Brevo:ApiUrl is required.");
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength];
    }
}
