using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Resources;
using Azure_Tech_Assignment.Configuration;
using Azure_Tech_Assignment.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Azure_Tech_Assignment.Services;

public sealed class VmMonitoringService
{
    private readonly ArmClient _armClient;
    private readonly VmRunningStateStore _stateStore;
    private readonly VmAutoSchedulerSettings _settings;
    private readonly ILogger<VmMonitoringService> _logger;

    public VmMonitoringService(
        ArmClient armClient,
        VmRunningStateStore stateStore,
        VmAutoSchedulerSettings settings,
        ILogger<VmMonitoringService> logger)
    {
        _armClient = armClient;
        _stateStore = stateStore;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<VmInfo>> CollectVmInfoAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var result = new ConcurrentBag<VmInfo>();

        try
        {
            var subscriptions = await GetSubscriptionsAsync(cancellationToken);

            _logger.LogInformation("Found {Count} subscription(s)", subscriptions.Count);

            using var throttler = new SemaphoreSlim(_settings.MaxParallelAzureCalls);

            var tasks = subscriptions.Select(subscription =>
                CollectVmsFromSubscriptionAsync(subscription, timestamp, result, throttler, cancellationToken));

            await Task.WhenAll(tasks);

            await _stateStore.SaveAsync(cancellationToken);

            return result
                .OrderBy(vm => vm.SubscriptionId)
                .ThenBy(vm => vm.ResourceGroup)
                .ThenBy(vm => vm.ComputerName)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect VM information");
            return result.ToList();
        }
    }

    private async Task<List<SubscriptionResource>> GetSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var subscriptions = new List<SubscriptionResource>();

        await foreach (var subscription in _armClient.GetSubscriptions().GetAllAsync(cancellationToken))
        {
            subscriptions.Add(subscription);
        }

        return subscriptions;
    }

    private async Task CollectVmsFromSubscriptionAsync(
        SubscriptionResource subscription,
        DateTime timestamp,
        ConcurrentBag<VmInfo> result,
        SemaphoreSlim throttler,
        CancellationToken cancellationToken)
    {
        try
        {
            var resourceGroups = new List<ResourceGroupResource>();

            await foreach (var resourceGroup in subscription.GetResourceGroups().GetAllAsync())
            {
                resourceGroups.Add(resourceGroup);
            }

            var tasks = resourceGroups.Select(resourceGroup =>
                CollectVmsFromResourceGroupAsync(
                    subscription,
                    resourceGroup,
                    timestamp,
                    result,
                    throttler,
                    cancellationToken));

            await Task.WhenAll(tasks);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Azure error while processing subscription {SubscriptionId}. Status: {Status}. Code: {Code}",
                subscription.Data.SubscriptionId,
                ex.Status,
                ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error while processing subscription {SubscriptionId}",
                subscription.Data.SubscriptionId);
        }
    }

    private async Task CollectVmsFromResourceGroupAsync(
        SubscriptionResource subscription,
        ResourceGroupResource resourceGroup,
        DateTime timestamp,
        ConcurrentBag<VmInfo> result,
        SemaphoreSlim throttler,
        CancellationToken cancellationToken)
    {
        try
        {
            var tasks = new List<Task>();

            await foreach (var vm in resourceGroup.GetVirtualMachines().GetAllAsync())
            {
                tasks.Add(ProcessVmWithThrottleAsync(
                    subscription,
                    resourceGroup,
                    vm,
                    timestamp,
                    result,
                    throttler,
                    cancellationToken));
            }

            await Task.WhenAll(tasks);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Azure error while reading VMs from resource group {ResourceGroup}. Status: {Status}. Code: {Code}",
                resourceGroup.Data.Name,
                ex.Status,
                ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error while reading VMs from resource group {ResourceGroup}",
                resourceGroup.Data.Name);
        }
    }

    private async Task ProcessVmWithThrottleAsync(
        SubscriptionResource subscription,
        ResourceGroupResource resourceGroup,
        VirtualMachineResource vm,
        DateTime timestamp,
        ConcurrentBag<VmInfo> result,
        SemaphoreSlim throttler,
        CancellationToken cancellationToken)
    {
        await throttler.WaitAsync(cancellationToken);

        try
        {
            await ProcessVmAsync(subscription, resourceGroup, vm, timestamp, result, cancellationToken);
        }
        finally
        {
            throttler.Release();
        }
    }

    private async Task ProcessVmAsync(
        SubscriptionResource subscription,
        ResourceGroupResource resourceGroup,
        VirtualMachineResource vm,
        DateTime timestamp,
        ConcurrentBag<VmInfo> result,
        CancellationToken cancellationToken)
    {
        try
        {
            var instanceView = await vm.InstanceViewAsync(cancellationToken);
            var powerState = GetPowerState(instanceView.Value);
            var normalizedPowerState = NormalizePowerState(powerState);

            var hasAutoshutdownTag =
                vm.Data.Tags.TryGetValue("Autoshutdown", out var tagValue) &&
                string.Equals(tagValue, "1", StringComparison.OrdinalIgnoreCase);

            DateTime? startTime = null;

            if (IsPowerState(powerState, "running"))
            {
                startTime = _stateStore.GetOrAddStartTime(vm.Id.ToString(), timestamp);
            }
            else
            {
                _stateStore.Remove(vm.Id.ToString());
            }

            result.Add(new VmInfo
            {
                Timestamp = timestamp,
                SubscriptionId = subscription.Data.SubscriptionId,
                ResourceGroup = resourceGroup.Data.Name,
                ComputerName = vm.Data.Name,
                PowerState = normalizedPowerState,
                HasAutoshutdownTag = hasAutoshutdownTag,
                VmId = vm.Id.ToString(),
                StartTime = startTime
            });
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Azure error while processing VM {VmName}. Status: {Status}. Code: {Code}",
                vm.Data.Name,
                ex.Status,
                ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error while processing VM {VmName}",
                vm.Data.Name);
        }
    }

    private static string GetPowerState(VirtualMachineInstanceView instanceView)
    {
        return instanceView.Statuses?
            .FirstOrDefault(status => status.Code?.StartsWith("PowerState/", StringComparison.OrdinalIgnoreCase) == true)?
            .Code?
            .Replace("PowerState/", "", StringComparison.OrdinalIgnoreCase)
            ?? "unknown";
    }

    private static string NormalizePowerState(string powerState)
    {
        return powerState.ToLowerInvariant() switch
        {
            "running" => "Running",
            "stopped" => "Stopped",
            "deallocated" => "Deallocated",
            "starting" => "Starting",
            "stopping" => "Stopping",
            "deallocating" => "Deallocating",
            _ => "Unknown"
        };
    }

    private static bool IsPowerState(string? actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}