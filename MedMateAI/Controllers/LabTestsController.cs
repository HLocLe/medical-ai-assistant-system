using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.LabTests.Requests;
using MedMateAI.Application.DTOs.LabTests.Responses;
using MedMateAI.Application.IService;
using MedMateAI.Domain.Enums;
using MedMateAI.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedMateAI.Controllers;

[ApiController]
[Route("api/lab-tests")]
public sealed class LabTestsController : ControllerBase
{
    private const string InvalidSessionIdMessage = "Id phiên xét nghiệm không hợp lệ";
    private const string NotFoundMessage = "Không tìm thấy phiên xét nghiệm";
    private const string UnauthenticatedError = "Người dùng chưa đăng nhập";

    private readonly ILabTestService _labTestService;
    private readonly IUserService _userService;

    public LabTestsController(ILabTestService labTestService, IUserService userService)
    {
        _labTestService = labTestService;
        _userService = userService;
    }

    [HttpPost("analyze")]
    [ProducesResponseType(typeof(ApiResponse<LabTestUploadResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<LabTestUploadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<LabTestUploadResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Analyze(
        [FromBody] LabTestAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized(ApiResponseFactory.FailFromErrors<LabTestUploadResponse>(
                new[] { UnauthenticatedError },
                "Chưa đăng nhập"));
        }

        var (ok, errors, data) = await _labTestService.AnalyzeFromDocumentUrlAsync(
            currentUser.Id,
            request,
            cancellationToken);

        if (!ok || data is null)
        {
            return BadRequest(ApiResponseFactory.FailFromErrors<LabTestUploadResponse>(
                errors,
                "Yêu cầu phân tích xét nghiệm thất bại"));
        }

        return Accepted(ApiResponseFactory.Success(data, "Đã xếp hàng OCR xét nghiệm"));
    }

    [HttpGet("my-sessions")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<LabTestSessionSummaryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<LabTestSessionSummaryResponse>>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySessions(
        [FromQuery] PaginationQuery query,
        [FromQuery] LabTestSessionStatus? status,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized(ApiResponseFactory.FailFromErrors<PagedResponse<LabTestSessionSummaryResponse>>(
                new[] { UnauthenticatedError },
                "Chưa đăng nhập"));
        }

        var data = await _labTestService.GetSessionsByUserIdAsync(
            currentUser.Id,
            status,
            query.PageNumber,
            query.PageSize,
            cancellationToken);

        return Ok(ApiResponseFactory.Success(data, "OK"));
    }

    [HttpGet("sessions")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<LabTestSessionSummaryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<LabTestSessionSummaryResponse>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<LabTestSessionSummaryResponse>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<LabTestSessionSummaryResponse>>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllSessions(
        [FromQuery] PaginationQuery query,
        [FromQuery] LabTestSessionStatus? status,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (userId.HasValue && userId.Value == Guid.Empty)
        {
            return BadRequest(ApiResponseFactory.Fail<PagedResponse<LabTestSessionSummaryResponse>>("Id người dùng không hợp lệ"));
        }

        var data = await _labTestService.GetAllSessionsAsync(
            status,
            userId,
            query.PageNumber,
            query.PageSize,
            cancellationToken);

        return Ok(ApiResponseFactory.Success(data, "OK"));
    }

    [HttpGet("{sessionId:guid}/ocr-extracts")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LabTestOcrExtractResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LabTestOcrExtractResponse>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LabTestOcrExtractResponse>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LabTestOcrExtractResponse>>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOcrExtracts(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized(ApiResponseFactory.FailFromErrors<IReadOnlyList<LabTestOcrExtractResponse>>(
                new[] { UnauthenticatedError },
                "Chưa đăng nhập"));
        }

        if (sessionId == Guid.Empty)
        {
            return BadRequest(ApiResponseFactory.Fail<IReadOnlyList<LabTestOcrExtractResponse>>(InvalidSessionIdMessage));
        }

        var data = await _labTestService.GetOcrExtractsBySessionIdAsync(
            currentUser.Id,
            sessionId,
            cancellationToken);

        if (data is null)
        {
            return NotFound(ApiResponseFactory.Fail<IReadOnlyList<LabTestOcrExtractResponse>>(NotFoundMessage));
        }

        return Ok(ApiResponseFactory.Success(data, "OK"));
    }

    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LabTestUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LabTestUploadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<LabTestUploadResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<LabTestUploadResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSession(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized(ApiResponseFactory.FailFromErrors<LabTestUploadResponse>(
                new[] { UnauthenticatedError },
                "Chưa đăng nhập"));
        }

        if (sessionId == Guid.Empty)
        {
            return BadRequest(ApiResponseFactory.Fail<LabTestUploadResponse>(InvalidSessionIdMessage));
        }

        var data = await _labTestService.GetSessionAsync(currentUser.Id, sessionId, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseFactory.Fail<LabTestUploadResponse>(NotFoundMessage));
        }

        var message = data.Status switch
        {
            LabTestSessionStatus.Processing => "Đang xử lý OCR xét nghiệm",
            LabTestSessionStatus.Completed => "OCR xét nghiệm hoàn tất",
            LabTestSessionStatus.Failed => "OCR xét nghiệm thất bại",
            _ => "OK",
        };

        return Ok(ApiResponseFactory.Success(data, message));
    }

    [HttpPost("{sessionId:guid}/summary")]
    [ProducesResponseType(typeof(ApiResponse<LabTestSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LabTestSummaryResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<LabTestSummaryResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<LabTestSummaryResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<LabTestSummaryResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SummarizeSession(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized(ApiResponseFactory.FailFromErrors<LabTestSummaryResponse>(
                new[] { UnauthenticatedError },
                "Chưa đăng nhập"));
        }

        if (sessionId == Guid.Empty)
        {
            return BadRequest(ApiResponseFactory.Fail<LabTestSummaryResponse>(InvalidSessionIdMessage));
        }

        var (ok, errors, data) = await _labTestService.SummarizeSessionAsync(
            currentUser.Id,
            sessionId,
            cancellationToken);

        if (!ok || data is null)
        {
            var errorList = errors?.ToList() ?? new List<string>();
            if (errorList.Contains(NotFoundMessage))
            {
                return NotFound(ApiResponseFactory.FailFromErrors<LabTestSummaryResponse>(errorList, NotFoundMessage));
            }

            return BadRequest(ApiResponseFactory.FailFromErrors<LabTestSummaryResponse>(
                errorList,
                "Tóm tắt kết quả xét nghiệm thất bại"));
        }

        if (data.Status == LabTestSummaryStatus.Completed)
        {
            return Ok(ApiResponseFactory.Success(data, "Tóm tắt kết quả xét nghiệm thành công"));
        }

        return Accepted(ApiResponseFactory.Success(data, "Đã xếp hàng tóm tắt kết quả xét nghiệm"));
    }
}
