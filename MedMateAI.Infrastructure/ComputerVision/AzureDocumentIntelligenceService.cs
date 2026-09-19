using System.Net.Http.Json;
using System.Text.Json;
using MedMateAI.Application.Common;
using MedMateAI.Application.Helpers;
using MedMateAI.Application.IService;
using MedMateAI.Infrastructure.ComputerVision.DTOs;
using MedMateAI.Infrastructure.ComputerVision.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedMateAI.Infrastructure.ComputerVision;

public sealed class AzureDocumentIntelligenceService : IDocumentIntelligenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(2);

    private readonly HttpClient _httpClient;
    private readonly AzureOptions _options;
    private readonly ILogger<AzureDocumentIntelligenceService> _logger;

    public AzureDocumentIntelligenceService(
        HttpClient httpClient,
        IOptions<AzureOptions> options,
        ILogger<AzureDocumentIntelligenceService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> AnalyzeFromUrlAsync(
        string documentUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentUrl))
        {
            throw new ArgumentException("Document URL is required.", nameof(documentUrl));
        }

        var analyzeUrl = BuildAnalyzeUrl();
        var payload = new AzureAnalyzeRequest { UrlSource = documentUrl.Trim() };

        using var request = new HttpRequestMessage(HttpMethod.Post, analyzeUrl);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.Key);
        request.Content = JsonContent.Create(payload);

        return await SubmitAndPollAsync(request, cancellationToken);
    }

    public async Task<string> AnalyzeFromStreamAsync(
        Stream documentStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentStream);

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (!documentStream.CanRead)
        {
            throw new ArgumentException("Document stream is not readable.", nameof(documentStream));
        }

        var analyzeUrl = BuildAnalyzeUrl();

        using var request = new HttpRequestMessage(HttpMethod.Post, analyzeUrl);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.Key);
        request.Content = new StreamContent(documentStream);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType.Trim());

        return await SubmitAndPollAsync(request, cancellationToken);
    }

    private async Task<string> SubmitAndPollAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            _logger.LogWarning(
                "Azure Document Intelligence analyze request failed with status {StatusCode}. Response: {ResponseBody}",
                statusCode,
                Truncate(responseBody, 500));

            ThrowAzureHttpFailure("analyze", statusCode);
        }

        if (!response.Headers.TryGetValues("Operation-Location", out var operationLocations))
        {
            throw new InvalidOperationException(
                "Azure Document Intelligence response is missing Operation-Location header.");
        }

        var operationUrl = operationLocations.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(operationUrl))
        {
            throw new InvalidOperationException(
                "Azure Document Intelligence Operation-Location header is empty.");
        }

        _logger.LogInformation("Azure Document Intelligence analyze accepted. Polling for result...");

        return await PollUntilSucceededAsync(operationUrl, cancellationToken);
    }

    private async Task<string> PollUntilSucceededAsync(
        string operationUrl,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(MaxWait);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, operationUrl);
            pollRequest.Headers.Add("Ocp-Apim-Subscription-Key", _options.Key);

            using var pollResponse = await _httpClient.SendAsync(pollRequest, cancellationToken);
            var pollBody = await pollResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!pollResponse.IsSuccessStatusCode)
            {
                var statusCode = (int)pollResponse.StatusCode;
                _logger.LogWarning(
                    "Azure Document Intelligence poll failed with status {StatusCode}. Response: {ResponseBody}",
                    statusCode,
                    Truncate(pollBody, 500));

                ThrowAzureHttpFailure("poll", statusCode);
            }

            var result = JsonSerializer.Deserialize<AzureAnalyzeOperationResponse>(pollBody, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize Azure analyze operation response.");

            var status = result.Status?.Trim().ToLowerInvariant();

            if (status == "succeeded")
            {
                _logger.LogInformation("Azure Document Intelligence processing succeeded.");
                return result.AnalyzeResult?.Content ?? string.Empty;
            }

            if (status is "failed" or "canceled")
            {
                var errorMessage = result.Error?.Message ?? "Unknown error.";
                _logger.LogWarning(
                    "Azure Document Intelligence processing {Status}. Error: {ErrorMessage}",
                    status,
                    errorMessage);

                throw new InvalidOperationException(
                    $"Azure Document Intelligence processing {status}: {errorMessage}");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for Azure Document Intelligence analysis to complete.");
    }

    private string BuildAnalyzeUrl()
    {
        var endpoint = _options.Endpoint.Trim();
        if (!endpoint.EndsWith('/'))
        {
            endpoint += "/";
        }

        var modelId = string.IsNullOrWhiteSpace(_options.ModelId)
            ? "prebuilt-layout"
            : _options.ModelId.Trim();

        var apiVersion = string.IsNullOrWhiteSpace(_options.ApiVersion)
            ? "2024-11-30"
            : _options.ApiVersion.Trim();

        return $"{endpoint}documentintelligence/documentModels/{modelId}:analyze?api-version={apiVersion}";
    }

    private static void ThrowAzureHttpFailure(string operation, int statusCode)
    {
        var message = $"Azure Document Intelligence {operation} failed with status {statusCode}.";
        if (BackgroundJobRetry.IsTransientHttpStatusCode(statusCode))
        {
            throw new TransientRemoteCallException(message, statusCode);
        }

        throw new InvalidOperationException(message);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
