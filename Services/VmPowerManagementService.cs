using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure_Tech_Assignment.Configuration;
using Azure_Tech_Assignment.Models;
using Microsoft.Extensions.Logging;

namespace Azure_Tech_Assignment.Services;

public sealed class VmPowerManagementService
{
    private readonly ArmClient _armClient;
    private readonly VmAutoSchedulerSettings _settings;
    private readonly ILogger<VmPowerManagementService> _logger;

    public VmPowerManagementService(
        ArmClient armClient,
        VmAutoSchedulerSettings settings,
        ILogger<VmPowerManagementService> logger)
    {
        _armClient = armClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task ApplyPowerManagementRulesAsync(
        IReadOnlyCollection<VmInfo> vmInfos,
        CancellationToken cancellationToken)
    {
        var targetVms = vmInfos
            .Where(vm => vm.HasAutoshutdownTag)
            .ToList();

        _logger.LogInformation(
            "Applying power rules to {Count} VM(s) with Autoshutdown=1",
            targetVms.Count);

        using var throttler = new SemaphoreSlim(_settings.MaxParallelPowerOperations);

        var tasks = targetVms.Select(vm =>
            ApplyRulesWithThrottleAsync(vm, throttler, cancellationToken));

        await Task.WhenAll(tasks);
    }

    private async Task ApplyRulesWithThrottleAsync(
        VmInfo vmInfo,
        SemaphoreSlim throttler,
        CancellationToken cancellationToken)
    {
        await throttler.WaitAsync(cancellationToken);

        try
        {
            await ApplyRulesToVmAsync(vmInfo, cancellationToken);
        }
        finally
        {
            throttler.Release();
        }
    }

    private async Task ApplyRulesToVmAsync(VmInfo vmInfo, CancellationToken cancellationToken)
    {
        try
        {
            var vm = _armClient.GetVirtualMachineResource(new ResourceIdentifier(vmInfo.VmId));

            if (IsPowerState(vmInfo.PowerState, "Running"))
            {
                await ShutdownIfRunningTooLongAsync(vm, vmInfo, cancellationToken);
                return;
            }

            if (IsPowerState(vmInfo.PowerState, "Stopped"))
            {
                await DeallocateStoppedVmAsync(vm, vmInfo, cancellationToken);
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Azure error while applying rules to VM {VmName}. Status: {Status}. Code: {Code}",
                vmInfo.ComputerName,
                ex.Status,
                ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error while applying rules to VM {VmName}",
                vmInfo.ComputerName);
        }
    }

    private async Task ShutdownIfRunningTooLongAsync(
        VirtualMachineResource vm,
        VmInfo vmInfo,
        CancellationToken cancellationToken)
    {
        if (!vmInfo.StartTime.HasValue)
        {
            _logger.LogWarning(
                "VM {VmName} is running, but start time is unknown. Shutdown rule skipped.",
                vmInfo.ComputerName);

            return;
        }

        var runningTime = vmInfo.Timestamp - vmInfo.StartTime.Value;
        var maxRunningTime = TimeSpan.FromHours(_settings.MaxRunningHours);

        if (runningTime <= maxRunningTime)
        {
            return;
        }

        _logger.LogInformation(
            "Shutting down VM {VmName}. Running time: {RunningHours:F2}h. Max allowed: {MaxHours:F2}h.",
            vmInfo.ComputerName,
            runningTime.TotalHours,
            maxRunningTime.TotalHours);

        await vm.PowerOffAsync(WaitUntil.Started, skipShutdown: false, cancellationToken);

        _logger.LogInformation(
            "Shutdown operation started for VM {VmName}",
            vmInfo.ComputerName);
    }

    private async Task DeallocateStoppedVmAsync(
        VirtualMachineResource vm,
        VmInfo vmInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Deallocating stopped VM {VmName}",
            vmInfo.ComputerName);

        await vm.DeallocateAsync(WaitUntil.Started, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Deallocation operation started for VM {VmName}",
            vmInfo.ComputerName);
    }

    private static bool IsPowerState(string? actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}