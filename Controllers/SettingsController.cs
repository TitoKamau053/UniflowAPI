using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/settings")]
[Authorize(Policy = "AdminOnly")]
public class SettingsController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;

    public SettingsController(StoredProcExecutor db, ICurrentUser me)
    {
        _db = db;
        _me = me;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await _db.QueryAsync<SettingRow>("usp_GetAllSettings", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<IEnumerable<SettingRow>>.Ok(settings));
    }

    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize()
    {
        await _db.ExecuteAsync("usp_InitializeDefaultSettings", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse.Ok("Default settings initialized"));
    }

    [HttpPut("bulk")]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkUpsertSettingsRequest request)
    {
        var json = JsonSerializer.Serialize(request.Settings.Select(s => new { category = s.Category, key = s.Key, value = s.Value }));
        await _db.ExecuteAsync("usp_BulkUpsertSettings", new { SchemeID = _me.SchemeId, SettingsJson = json });
        return Ok(ApiResponse.Ok("Settings updated"));
    }

    [HttpPost("reset")]
    public async Task<IActionResult> ResetCategory([FromQuery] string category)
    {
        await _db.ExecuteAsync("usp_ResetSettingsCategory", new { SchemeID = _me.SchemeId, Category = category });
        return Ok(ApiResponse.Ok($"'{category}' settings reset"));
    }

    [HttpGet("key/{key}")]
    public async Task<IActionResult> GetByKey(string key, [FromQuery] string category)
    {
        var setting = await _db.QuerySingleOrDefaultAsync<SettingRow>("usp_GetSettingByKey", new { SchemeID = _me.SchemeId, Category = category, SettingKey = key });
        if (setting is null) throw ApiException.NotFound("Setting not found");
        return Ok(ApiResponse<SettingRow>.Ok(setting));
    }

    [HttpPut("key/{key}")]
    public async Task<IActionResult> SetByKey(string key, [FromQuery] string category, [FromBody] string value)
    {
        await _db.ExecuteAsync("usp_UpsertSetting", new { SchemeID = _me.SchemeId, Category = category, SettingKey = key, SettingValue = value });
        return Ok(ApiResponse.Ok("Setting updated"));
    }
    
    [HttpGet("billing/config")]
    public Task<IActionResult> GetBillingConfig() => GetCategory("billing");

    [HttpPut("billing/config")]
    public Task<IActionResult> PutBillingConfig([FromBody] SettingItem[] items) => PutCategory("billing", items);

    [HttpGet("payments/config")]
    public Task<IActionResult> GetPaymentsConfig() => GetCategory("payments");

    [HttpPut("payments/config")]
    public Task<IActionResult> PutPaymentsConfig([FromBody] SettingItem[] items) => PutCategory("payments", items);

    [HttpGet("contributions/config")]
    public Task<IActionResult> GetContributionsConfig() => GetCategory("contributions");

    [HttpPut("contributions/config")]
    public Task<IActionResult> PutContributionsConfig([FromBody] SettingItem[] items) => PutCategory("contributions", items);

    [HttpGet("notifications/config")]
    public Task<IActionResult> GetNotificationsConfig() => GetCategory("notifications");

    [HttpPut("notifications/config")]
    public Task<IActionResult> PutNotificationsConfig([FromBody] SettingItem[] items) => PutCategory("notifications", items);

    [HttpGet("company/config")]
    public Task<IActionResult> GetCompanyConfig() => GetCategory("company");

    [HttpPut("company/config")]
    public Task<IActionResult> PutCompanyConfig([FromBody] SettingItem[] items) => PutCategory("company", items);

    [HttpGet("{category}")]
    public Task<IActionResult> GetCategory(string category) => GetCategoryInternal(category);

    private async Task<IActionResult> GetCategoryInternal(string category)
    {
        var settings = await _db.QueryAsync<SettingRow>("usp_GetSettingsByCategory", new { SchemeID = _me.SchemeId, Category = category });
        return Ok(ApiResponse<IEnumerable<SettingRow>>.Ok(settings));
    }

    private async Task<IActionResult> PutCategory(string category, SettingItem[] items)
    {
        var json = JsonSerializer.Serialize(items.Select(s => new { category, key = s.Key, value = s.Value }));
        await _db.ExecuteAsync("usp_BulkUpsertSettings", new { SchemeID = _me.SchemeId, SettingsJson = json });
        return Ok(ApiResponse.Ok($"'{category}' settings updated"));
    }
}
