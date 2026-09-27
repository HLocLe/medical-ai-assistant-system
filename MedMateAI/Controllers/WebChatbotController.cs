using MedMateAI.Application.Common;
using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.WebChatbot.Requests;
using MedMateAI.Application.DTOs.WebChatbot.Responses;
using MedMateAI.Application.IService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Controllers;

[ApiController]
[Route("api/web-chatbot")]
public sealed class WebChatbotController : ControllerBase
{
    private const int MaxMessageLength = 2000;

    private readonly IWebChatbotService _webChatbotService;
    private readonly ILogger<WebChatbotController> _logger;

    public WebChatbotController(
        IWebChatbotService webChatbotService,
        ILogger<WebChatbotController> logger)
    {
        _webChatbotService = webChatbotService;
        _logger = logger;
    }

    [HttpPost("message")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<WebChatbotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<WebChatbotResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendMessage(
        [FromBody] WebChatbotRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ApiResponse<WebChatbotResponse>
            {
                Success = false,
                Message = "Send message failed.",
                Errors = new List<string> { "Message is required." },
            });
        }

        if (request.Message.Trim().Length > MaxMessageLength)
        {
            return BadRequest(new ApiResponse<WebChatbotResponse>
            {
                Success = false,
                Message = "Send message failed.",
                Errors = new List<string> { $"Message must be {MaxMessageLength} characters or fewer." },
            });
        }

        try
        {
            var data = await _webChatbotService.SendMessageAsync(request, cancellationToken);
            return Ok(new ApiResponse<WebChatbotResponse>
            {
                Success = true,
                Message = "OK",
                Data = data,
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiResponse<WebChatbotResponse>
            {
                Success = false,
                Message = "Send message failed.",
                Errors = new List<string> { ex.Message },
            });
        }
        catch (TransientRemoteCallException ex)
        {
            _logger.LogWarning(ex, "Web chatbot AI provider is temporarily unavailable.");

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiResponse<WebChatbotResponse>
            {
                Success = false,
                Message = "Web chatbot is temporarily unavailable.",
                Errors = new List<string>
                {
                    "The AI chatbot service is busy. Please try again later.",
                },
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Web chatbot service is unavailable.");

            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<WebChatbotResponse>
            {
                Success = false,
                Message = "Web chatbot is unavailable.",
                Errors = new List<string>
                {
                    "The AI chatbot service is currently unavailable. Please try again later.",
                },
            });
        }
    }
}
