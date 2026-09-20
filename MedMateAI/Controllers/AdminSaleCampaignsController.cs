using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.Sales.Requests;
using MedMateAI.Application.DTOs.Sales.Responses;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedMateAI.Controllers;

[ApiController]
[Route("api/admin/sale-campaigns")]
[Authorize(Roles = "Admin")]
public sealed class AdminSaleCampaignsController : ControllerBase
{
    private readonly ISaleCampaignService _service;
    private readonly ISaleCampaignAnalyticsService _analyticsService;

    public AdminSaleCampaignsController(
        ISaleCampaignService service,
        ISaleCampaignAnalyticsService analyticsService)
    {
        _service = service;
        _analyticsService = analyticsService;
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<SaleCampaignResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] PaginationQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await _service.GetAdminCampaignsAsync(
            query.PageNumber,
            query.PageSize,
            cancellationToken);
        return Ok(Success(data));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var data = await _service.GetByIdAsync(id, cancellationToken);
        return data is null
            ? NotFound(Failure<SaleCampaignResponse>("Sale campaign not found."))
            : Ok(Success(data));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] UpsertSaleCampaignRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await _service.CreateAsync(request, cancellationToken);
            return Ok(Success(data, "Sale campaign created."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Failure<SaleCampaignResponse>(
                "Create sale campaign failed.",
                ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpsertSaleCampaignRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await _service.UpdateAsync(id, request, cancellationToken);
            return data is null
                ? NotFound(Failure<SaleCampaignResponse>("Sale campaign not found."))
                : Ok(Success(data, "Sale campaign updated."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Failure<SaleCampaignResponse>(
                "Update sale campaign failed.",
                ex.Message));
        }
        catch (SaleCampaignConflictException ex)
        {
            return Conflict(Failure<SaleCampaignResponse>(
                "Update sale campaign conflicted with existing redemptions.",
                ex.Message));
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateSaleCampaignStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var data = await _service.UpdateStatusAsync(
            id,
            request.IsActive,
            cancellationToken);
        return data is null
            ? NotFound(Failure<SaleCampaignResponse>("Sale campaign not found."))
            : Ok(Success(data, "Sale campaign status updated."));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _service.DeleteAsync(id, cancellationToken);
            return deleted
                ? Ok(Success(true, "Sale campaign deleted (soft)."))
                : NotFound(Failure<bool>("Sale campaign not found."));
        }
        catch (SaleCampaignConflictException ex)
        {
            return Conflict(Failure<bool>(
                "Sale campaign cannot be deleted.",
                ex.Message));
        }
    }

    [HttpGet("{id:guid}/redemptions")]
    public async Task<IActionResult> GetRedemptions(
        Guid id,
        [FromQuery] PaginationQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await _service.GetRedemptionsAsync(
            id,
            query.PageNumber,
            query.PageSize,
            cancellationToken);
        return data is null
            ? NotFound(Failure<PagedResponse<SaleRedemptionResponse>>(
                "Sale campaign not found."))
            : Ok(Success(data));
    }

    [HttpGet("{id:guid}/revenue-impact")]
    [ProducesResponseType(
        typeof(ApiResponse<SaleRevenueImpactResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<SaleRevenueImpactResponse>),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRevenueImpact(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var data = await _analyticsService.GetRevenueImpactAsync(
            id,
            cancellationToken);
        return data is null
            ? NotFound(Failure<SaleRevenueImpactResponse>(
                "Sale campaign not found."))
            : Ok(Success(data));
    }

    private static ApiResponse<T> Success<T>(T data, string message = "OK")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data
        };
    }

    private static ApiResponse<T> Failure<T>(string message, params string[] errors)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors.ToList()
        };
    }
}
