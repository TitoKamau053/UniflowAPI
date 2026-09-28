using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/contributions")]
public class ContributionsController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;

    public ContributionsController(StoredProcExecutor db, ICurrentUser me)
    {
        _db = db;
        _me = me;
    }

    /// <summary>Scheme-wide, one fixed amount for the month. Idempotent per (customer, month) — reruns skip customers already generated.</summary>
    [HttpPost("generate")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Generate([FromBody] GenerateContributionsRequest request)
    {
        await _db.ExecuteAsync("usp_GenerateContributions", new
        {
            SchemeID = _me.SchemeId,
            request.ContributionMonth,
            request.AmountRequired
        });

        return Ok(ApiResponse.Ok("Contributions generated"));
    }

    /// <summary>For contributions settled OUTSIDE the payment waterfall (e.g. cash handed to an admin directly).</summary>
    [HttpPost("{contributionId:int}/mark-paid")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> MarkPaid(int contributionId, [FromBody] MarkContributionPaidRequest request)
    {
        await _db.ExecuteAsync("usp_MarkContributionPaid", new { ContributionID = contributionId, request.AmountPaid });
        return Ok(ApiResponse.Ok("Contribution updated"));
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ListForScheme([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var contributions = await _db.QueryAsync<ContributionRow>("usp_ListContributionsByScheme",
            new { SchemeID = _me.SchemeId, Status = status, Page = page, PageSize = pageSize });

        return Ok(ApiResponse<IEnumerable<ContributionRow>>.Ok(contributions));
    }

    [HttpGet("summary")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Summary()
    {
        var summary = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetContributionsSummary", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<dynamic>.Ok(summary));
    }

    [HttpGet("overdue")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Overdue([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var overdue = await _db.QueryAsync<dynamic>("usp_ListOverdueContributions", new { SchemeID = _me.SchemeId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(overdue));
    }

    [HttpGet("{contributionId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetById(int contributionId)
    {
        var contribution = await _db.QuerySingleOrDefaultAsync<ContributionRow>("usp_GetContributionById",
            new { ContributionID = contributionId, SchemeID = _me.SchemeId });
        if (contribution is null) throw ApiException.NotFound("Contribution not found");
        return Ok(ApiResponse<ContributionRow>.Ok(contribution));
    }

    [HttpGet("customer/{customerId:int}")]
    [Authorize]
    public async Task<IActionResult> ListForCustomer(int customerId)
    {
        if (_me.IsCustomer && _me.UserId != customerId)
            throw ApiException.Forbidden();

        var contributions = await _db.QueryAsync<ContributionRow>("usp_ListContributionsByCustomer", new { CustomerID = customerId });
        return Ok(ApiResponse<IEnumerable<ContributionRow>>.Ok(contributions));
    }

    /// <summary>The logged-in customer's own contribution history — mirrors Nyanjigi's GET /contributions/me.</summary>
    [HttpGet("me")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> ListMine()
    {
        var contributions = await _db.QueryAsync<ContributionRow>("usp_ListContributionsByCustomer", new { CustomerID = _me.UserId });
        return Ok(ApiResponse<IEnumerable<ContributionRow>>.Ok(contributions));
    }
}
