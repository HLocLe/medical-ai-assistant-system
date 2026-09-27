using System.Net;
using System.Text;
using MedMateAI.Application.Common;
using MedMateAI.Application.DTOs.WebChatbot.Requests;
using MedMateAI.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MedMateAI.Tests.Services;

[TestFixture]
public class OpenRouterChatProviderTests
{
    [TestCase(HttpStatusCode.RequestTimeout)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.BadGateway)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public void GenerateAsync_TransientStatusCode_ThrowsTransientRemoteCallException(HttpStatusCode statusCode)
    {
        var provider = CreateProvider(statusCode, "{\"error\":\"busy\"}");

        var ex = Assert.ThrowsAsync<TransientRemoteCallException>(() => provider.GenerateAsync(CreateRequest()));

        Assert.That(ex!.StatusCode, Is.EqualTo((int)statusCode));
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.PaymentRequired)]
    [TestCase(HttpStatusCode.NotFound)]
    public void GenerateAsync_NonTransientStatusCode_ThrowsInvalidOperationException(HttpStatusCode statusCode)
    {
        var provider = CreateProvider(statusCode, "{\"error\":\"bad\"}");

        Assert.ThrowsAsync<InvalidOperationException>(() => provider.GenerateAsync(CreateRequest()));
    }

    [Test]
    public void GenerateAsync_EmptyContent_ThrowsTransientRemoteCallException()
    {
        var provider = CreateProvider(
            HttpStatusCode.OK,
            "{\"choices\":[{\"message\":{\"content\":\"\"}}]}");

        Assert.ThrowsAsync<TransientRemoteCallException>(() => provider.GenerateAsync(CreateRequest()));
    }

    [Test]
    public async Task GenerateAsync_Success_ReturnsTrimmedContent()
    {
        var provider = CreateProvider(
            HttpStatusCode.OK,
            "{\"model\":\"m\",\"choices\":[{\"message\":{\"content\":\"  hello  \"}}]}");

        var result = await provider.GenerateAsync(CreateRequest());

        Assert.That(result.Content, Is.EqualTo("hello"));
    }

    private static OpenRouterChatProvider CreateProvider(HttpStatusCode statusCode, string body)
    {
        var httpClient = new HttpClient(new StubHandler(statusCode, body));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:ApiKey"] = "test-key",
                ["OpenRouter:BaseUrl"] = "https://openrouter.test/api/v1",
            })
            .Build();

        return new OpenRouterChatProvider(httpClient, configuration, NullLogger<OpenRouterChatProvider>.Instance);
    }

    private static AIProviderChatRequest CreateRequest() => new()
    {
        SystemPrompt = "system",
        UserMessage = "user",
        Model = "test-model",
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }
}
