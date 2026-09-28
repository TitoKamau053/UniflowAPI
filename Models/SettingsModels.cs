namespace UniflowApi.Models;

public record UpsertSettingRequest(string Category, string SettingKey, string SettingValue);
public record BulkUpsertSettingsRequest(SettingItem[] Settings);
public record SettingItem(string Category, string Key, string Value);

public class SettingRow
{
    public string Category { get; set; } = string.Empty;
    public string SettingKey { get; set; } = string.Empty;
    public string? SettingValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}
