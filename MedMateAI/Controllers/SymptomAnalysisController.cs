using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.SymptomAnalysis.Requests;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Session;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.ClinicalQuestions;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Quota;
using MedMateAI.Application.IService;
using MedMateAI.Domain.Enums;
using MedMateAI.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedMateAI.Controllers;

[ApiController]
[Route("api/symptom-analysis")]
public sealed class SymptomAnalysisController : ControllerBase
{
    private const int MaxMessageLength = 2000;
    private const string NotFoundMessage = "Không tìm thấy phiên phân tích triệu chứng";
    private const string UnauthenticatedError = "Người dùng chưa đăng nhập";

    private readonly ISymptomAnalysisService _symptomAnalysisService;
    private readonly IUserService _userService;

    public SymptomAnalysisController(
        ISymptomAnalysisService symptomAnalysisService,
        IUserService userService)
    {
        _symptomAnalysisService = symptomAnalysisService;
        _userService = userService;
    }

    [HttpPost("suggest-clinical-questions")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<SuggestClinicalQuestionsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SuggestClinicalQuestionsResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SuggestClinicalQuestions(
        [FromBody] SuggestClinicalQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await _symptomAnalysisService.SuggestClinicalQuestionAsync(request, cancellationToken);
            return Ok(ApiResponseFactory.Success(data, "OK"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseFactory.FailFromErrors<SuggestClinicalQuestionsResponse>(
                new[] { ex.Message },
                "Gợi ý câu hỏi lâm sàng thất bại"));
        }
    }

    [HttpPost("submit-clinical-question-answers")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ClinicalQuestionAnswersResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ClinicalQuestionAnswersResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitClinicalQuestionAnswers(
        [FromBody] SubmitClinicalQuestionAnswersRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await _symptomAnalysisService.SubmitClinicalQuestionAnswersAsync(request, cancellationToken);
            return Ok(ApiResponseFactory.Success(data, "OK"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseFactory.FailFromErrors<ClinicalQuestionAnswersResponse>(
                new[] { ex.Message },
                "Gửi câu trả lời lâm sàng thất bại"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseFactory.FailFromErrors<ClinicalQuestionAnswersResponse>(
                new[] { ex.Message },
                "Gửi câu trả lời lâm sàng thất bại"));
        }
    }

    [HttpGet("quota")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<SymptomAnalysisQuotaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SymptomAnalysisQuotaResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetQuota(CancellationToken cancellationToken = default)
    {
        var data = await _symptomAnalysisService.GetQuotaAsync(cancellationToken);
        if (data is null)
        {
            return Unauthorized(ApiResponseFactory.FailFromErrors<SymptomAnalysisQuotaResponse>(
                new[] { UnauthenticatedError },
                "Chưa đăng nhập"));
        }

        return Ok(ApiResponseFactory.Success(data, "OK"));
    }

    [HttpGet("my-sessions")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SymptomAnalysisSessionSummaryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SymptomAnalysisSessionSummaryResponse>>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySessions(
        [FromQuery] PaginationQuery query,
        [FromQuery] SymptomAnalysisSessionType? sessionType,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized(ApiResponseFactory.FailFromErrors<PagedResponse<SymptomAnalysisSessionSummaryResponse>>(
                new[] { UnauthenticatedError },
                "Chưa đăng nhập"));
        }

        var data = await _symptomAnalysisService.GetSessionsByUserIdAsync(
            currentUser.Id,
            sessionType,
            query.PageNumber,
            query.PageSize,
            cancellationToken);

        return Ok(ApiResponseFactory.Success(data, "OK"));
    }

    [HttpGet("sessions")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SymptomAnalysisSessionSummaryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SymptomAnalysisSessionSummaryResponse>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SymptomAnalysisSessionSummaryResponse>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SymptomAnalysisSessionSummaryResponse>>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllSessions(
        [FromQuery] PaginationQuery query,
        [FromQuery] SymptomAnalysisSessionType? sessionType,
        [FromQuery] SymptomAnalysisSessionStatus? status,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (userId.HasValue && userId.Value == Guid.Empty)
        {
            return BadRequest(ApiResponseFactory.Fail<PagedResponse<SymptomAnalysisSessionSummaryResponse>>("Id người dùng không hợp lệ"));
        }

        var data = await _symptomAnalysisService.GetAllSessionsAsync(
            sessionType,
            status,
            userId,
            query.PageNumber,
            query.PageSize,
            cancellationToken);

        return Ok(ApiResponseFactory.Success(data, "OK"));
    }

    [HttpGet("{sessionId}")]
    [ProducesResponseType(typeof(ApiResponse<SymptomAnalysisResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SymptomAnalysisResponse>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return BadRequest(ApiResponseFactory.Fail<SymptomAnalysisResponse>("Id phiên phân tích triệu chứng không hợp lệ"));
        }

        var data = await _symptomAnalysisService.GetSessionByIdAsync(sessionId, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseFactory.Fail<SymptomAnalysisResponse>(NotFoundMessage));
        }

        return Ok(ApiResponseFactory.Success(data, "OK"));
    }
}
