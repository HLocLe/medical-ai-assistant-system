using System.Text;
using System.Text.Json;
using MedMateAI.Application.Common;
using MedMateAI.Application.Helpers;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Infrastructure.AI.Helpers;

internal static class AiHttpClientHelper
{
    public static void SetJsonContent(HttpRequestMessage request, object payload)
    {
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
    }

    public static async Task<string> SendJsonAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        ILogger logger,
        string serviceName,
        CancellationToken cancellationToken)
    {
        using var httpResponse = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var statusCode = (int)httpResponse.StatusCode;
            var truncatedBody = Truncate(responseBody, 500);
            logger.LogWarning(
                "{ServiceName} request failed with status code {StatusCode}. Response: {ResponseBody}",
                serviceName,
                statusCode,
                truncatedBody);

            var message =
                $"{serviceName} request failed with status code {statusCode}. Response: {truncatedBody}";

            if (BackgroundJobRetry.IsTransientHttpStatusCode(statusCode))
            {
                throw new TransientRemoteCallException(message, statusCode);
            }

            throw new InvalidOperationException(message);
        }

        return responseBody;
    }

    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
