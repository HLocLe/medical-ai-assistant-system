using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MedMateAI.Application.Common;
using MedMateAI.Application.DTOs.WebChatbot.Requests;
using MedMateAI.Application.Helpers;
using MedMateAI.Application.DTOs.WebChatbot.Responses;
using MedMateAI.Application.IService;
using MedMateAI.Infrastructure.AI.DTOs.OpenRouter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Infrastructure.AI;

public sealed class OpenRouterChatProvider : IAIChatProvider
{
    private const string ProviderName = "openrouter";
    private const string DefaultBaseUrl = "https://openrouter.ai/api/v1";
    private const string DefaultModel = "openai/gpt-oss-20b:free";
    private const decimal DefaultTemperature = 0.3m;
    private const int DefaultMaxTokens = 800;

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenRouterChatProvider> _logger;

    public OpenRouterChatProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenRouterChatProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AIProviderChatResult> GenerateAsync(
        AIProviderChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentException("Request is required.");
        }

        var apiKey = _configuration["OpenRouter:ApiKey"]?.Trim();
       
        if (string.IsNullOrWhiteSpace(apiKey)
            || string.Equals(apiKey, "YOUR_OPENROUTER_API_KEY", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OpenRouter API key is not configured. Please set OpenRouter:ApiKey locally or OpenRouter__ApiKey in environment variables.");
        }

        var baseUrl = _configuration["OpenRouter:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = DefaultBaseUrl;
        }

        var defaultModel = _configuration["OpenRouter:DefaultModel"];
        if (string.IsNullOrWhiteSpace(defaultModel))
        {
            defaultModel = DefaultModel;
        }

       var model = string.IsNullOrWhiteSpace(request.Model) ? defaultModel : request.Model.Trim();

        var payload = new
        {
            model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = request.SystemPrompt,
                },
                new
                {
                    role = "user",
                    content = request.UserMessage,
                },
            },
            temperature = request.Temperature ?? DefaultTemperature,
            max_tokens = request.MaxTokens ?? DefaultMaxTokens,
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl.TrimEnd('/')}/chat/completions");

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        
        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

      //  _logger.LogDebug(
      //"OpenRouter raw HTTP response. StatusCode: {StatusCode}, Body: {ResponseBody}",
      //(int)httpResponse.StatusCode,
      // responseBody);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var statusCode = (int)httpResponse.StatusCode;
            var truncatedBody = Truncate(responseBody, 500);
            var message = $"OpenRouter request failed with status code {statusCode}. Response: {truncatedBody}";

            _logger.LogWarning(
                "OpenRouter request failed with status code {StatusCode}. Response: {ResponseBody}",
                statusCode,
                truncatedBody);

            if (BackgroundJobRetry.IsTransientHttpStatusCode(statusCode))
            {
                throw new TransientRemoteCallException(message, statusCode);
            }

            throw new InvalidOperationException(message);
        }

        OpenRouterResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<OpenRouterResponse>(responseBody, ResponseJsonOptions);
      
       //_logger.LogInformation(
      //"OpenRouter deserialized. Model: {Model}, Content: {Content}",
      //response?.Model,
      //response?.Choices?.FirstOrDefault()?.Message?.Content);
        
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse OpenRouter response.", ex);
        }

        var firstChoice = response?.Choices?.FirstOrDefault();

        var content = firstChoice?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new TransientRemoteCallException("OpenRouter response does not contain message content.");
        }

        return new AIProviderChatResult
        {
            Content = content.Trim(),
            Model = response!.Model ?? model,
            Provider = ProviderName,
        };
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
