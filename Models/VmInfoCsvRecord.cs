namespace Azure_Tech_Assignment.Models;

public sealed class VmInfoCsvRecord
{
    public string Timestamp { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string ResourceGroup { get; set; } = string.Empty;

    public string ComputerName { get; set; } = string.Empty;

    public string PowerState { get; set; } = string.Empty;
}
