using Microsoft.Extensions.Options;
using UniflowApi.Common;

namespace UniflowApi.Middleware;

/// <summary>
/// Layer 1 of Equity's two-layer check (IP whitelist, then JWT). Applied only
/// to the /api/v1/equity/* group via endpoint filter — see Program.cs.
/// </summary>
public class EquityIpWhitelistMiddleware : IMiddleware
{
    private readonly EquitySettings _settings;
    private readonly ILogger<EquityIpWhitelistMiddleware> _logger;

    public EquityIpWhitelistMiddleware(IOptions<EquitySettings> settings, ILogger<EquityIpWhitelistMiddleware> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.MapToIPv4().ToString();

        if (remoteIp is null || !_settings.IpWhitelist.Contains(remoteIp))
        {
            _logger.LogWarning("Blocked unauthorized Equity access from IP: {Ip}", remoteIp);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Access Forbidden: Unauthorized Source" });
            return;
        }

        await next(context);
    }
}
