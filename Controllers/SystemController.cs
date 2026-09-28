using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;

namespace UniflowApi.Controllers;

public class ErrorCodeRow
{
    public int ErrorCode { get; set; }
    public string ErrorName { get; set; } = string.Empty;
    public int HttpStatus { get; set; }
    public string DefaultMessage { get; set; } = string.Empty;
}

/// <summary>Public reference for API consumers — the exact same catalog StoredProcExecutor derives HTTP statuses from.</summary>
[ApiController]
[Route("api/v1/system")]
public class SystemController : ControllerBase
{
    private readonly StoredProcExecutor _db;

    public SystemController(StoredProcExecutor db) => _db = db;

    [HttpGet("error-codes")]
    public async Task<IActionResult> ListErrorCodes()
    {
        var codes = await _db.QueryAsync<ErrorCodeRow>("usp_ListErrorCodes");
        return Ok(ApiResponse<IEnumerable<ErrorCodeRow>>.Ok(codes));
    }
}
