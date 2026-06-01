namespace Azure_Tech_Assignment.Configuration;

public sealed class VmAutoSchedulerSettings
{
    public int PollingIntervalMinutes { get; set; } = 5;

    public double MaxRunningHours { get; set; } = 8;

    public int MaxParallelAzureCalls { get; set; } = 20;

    public int MaxParallelPowerOperations { get; set; } = 10;
}
