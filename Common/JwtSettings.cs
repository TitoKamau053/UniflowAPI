namespace UniflowApi.Common;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 1440;
}

public class EquitySettings
{
    public const string SectionName = "Equity";
    public string[] IpWhitelist { get; set; } = Array.Empty<string>();
}
