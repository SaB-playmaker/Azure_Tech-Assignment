namespace Azure_Tech_Assignment.Models;

public sealed class VmInfo
{
    public DateTime Timestamp { get; set; }

    public string SubscriptionId { get; set; } = string.Empty;

    public string ResourceGroup { get; set; } = string.Empty;

    public string ComputerName { get; set; } = string.Empty;

    public string PowerState { get; set; } = "Unknown";

    public bool HasAutoshutdownTag { get; set; }

    public string VmId { get; set; } = string.Empty;

    public DateTime? StartTime { get; set; }
}
