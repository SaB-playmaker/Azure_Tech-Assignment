# Azure VM Autoscheduler

A production-ready console application that automatically manages Azure Virtual Machines based on configurable power management rules.

## Overview

This application monitors all Virtual Machines across Azure subscriptions and applies power management policies to VMs tagged with `Autoshutdown=1`. It runs continuously, polling VMs at configurable intervals and logging all activities to CSV files.

## Features

- **Multi-Subscription Support**: Monitors VMs across all accessible Azure subscriptions
- **Automatic Power Management**:
  - Shuts down VMs running longer than configured threshold (default: 8 hours)
  - Deallocates stopped VMs to minimize Azure costs
- **CSV Logging**: Appends VM state information to daily CSV files with timestamps
- **High Performance**: Utilizes async/await and parallel processing for efficient API usage
- **Resilient**: Graceful error handling prevents application crashes
- **Production Ready**: Clean, maintainable code following industry best practices

## Prerequisites

- .NET 8.0 SDK or later
- Azure CLI installed and configured
- Azure subscription with appropriate permissions:
  - `Reader` role (to list VMs)
  - `Virtual Machine Contributor` role (to manage VM power state)

## Configuration

### appsettings.json

```json
{
  "Azure": {
    "SubscriptionId": "your-subscription-id-here"
  },
  "VmAutoScheduler": {
    "PollingIntervalMinutes": 5,
    "MaxRunningTimeHours": 8
  }
}
```

**Configuration Options:**

| Setting | Description | Default |
|---------|-------------|---------|
| `SubscriptionId` | Azure subscription ID to monitor | Required |
| `PollingIntervalMinutes` | Interval between VM checks | 5 minutes |
| `MaxRunningTimeHours` | Maximum VM runtime before shutdown | 8 hours |

## Authentication

The application uses `DefaultAzureCredential` which attempts authentication in this order:

1. Environment variables (for service principal)
2. Managed Identity (when running in Azure)
3. Visual Studio credentials
4. Azure CLI credentials
5. Azure PowerShell credentials

### For Development

```bash
az login
az account set --subscription "your-subscription-id"
```

### For Production

Set environment variables for service principal:

```bash
export AZURE_CLIENT_ID="your-client-id"
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_SECRET="your-client-secret"
```

## Running the Application

### From Command Line

```bash
cd Azure_Tech_Assignment
dotnet run
```

### From Visual Studio

1. Open the solution in Visual Studio
2. Set `Azure_Tech_Assignment` as startup project
3. Press F5 to run

## VM Tagging

Only VMs with the `Autoshutdown` tag set to `1` will be managed by this application.

### Add Tag via Azure CLI

```bash
az vm update --resource-group MyResourceGroup --name MyVM --set tags.Autoshutdown=1
```

### Add Tag via Azure Portal

1. Navigate to Virtual Machine
2. Select "Tags" in the left menu
3. Add tag: `Name: Autoshutdown`, `Value: 1`
4. Click "Apply"

## Power Management Rules

The application applies these rules to VMs with `Autoshutdown=1` tag:

### Rule 1: Auto-Shutdown
- **Condition**: VM has been running longer than `MaxRunningTimeHours`
- **Action**: Powers off the VM
- **API Call**: `VirtualMachine.PowerOffAsync()`

### Rule 2: Auto-Deallocate
- **Condition**: VM is stopped but still allocated
- **Action**: Deallocates the VM to stop compute charges
- **API Call**: `VirtualMachine.DeallocateAsync()`

## CSV Output Format

The application creates daily CSV files: `vm_logs_YYYYMMDD.csv`

### Columns

| Column | Description |
|--------|-------------|
| `Timestamp` | UTC timestamp of the observation |
| `SubscriptionId` | Azure subscription ID |
| `ResourceGroup` | Resource group name |
| `ComputerName` | VM name |
| `PowerState` | Current power state (Running, Stopped, Deallocated, etc.) |

### Example

```csv
Timestamp,SubscriptionId,ResourceGroup,ComputerName,PowerState
2025-01-15 10:00:00,a713b5a9-...,Production-RG,WebServer01,Running
2025-01-15 10:00:00,a713b5a9-...,Production-RG,WebServer02,Stopped
```

## Architecture

### Project Structure

```
Azure_Tech_Assignment/
??? Configuration/
?   ??? AzureSettings.cs
?   ??? VmAutoSchedulerSettings.cs
??? Models/
?   ??? VmInfo.cs
??? Services/
?   ??? VmMonitoringService.cs
?   ??? VmPowerManagementService.cs
?   ??? CsvLogger.cs
??? Program.cs
??? appsettings.json
```

### Key Components

- **VmMonitoringService**: Discovers and collects VM information across subscriptions
- **VmPowerManagementService**: Applies power management rules to tagged VMs
- **CsvLogger**: Thread-safe CSV file logging with append-only writes

## Performance Optimization

### Efficient API Usage

- **Parallel Processing**: Resource groups and VMs are processed concurrently using `Task.WhenAll`
- **Batching**: Subscription-level queries minimize individual API calls
- **Non-Blocking**: Uses `WaitUntil.Started` to avoid waiting for long-running operations
- **Error Isolation**: Exceptions in individual VMs don't affect other VMs

### Scalability

The application efficiently handles hundreds of VMs through:
- Asynchronous I/O operations
- Concurrent processing with `ConcurrentBag<T>`
- Minimal API calls per polling cycle

## Error Handling

- All operations include try-catch blocks
- Errors are logged but don't crash the application
- Continues polling even after individual failures
- Graceful shutdown on Ctrl+C

## Logging

Uses Microsoft.Extensions.Logging with console output:

- `Information`: Normal operations, VM state changes
- `Warning`: Non-critical issues, subscription enumeration failures
- `Error`: Operation failures, API errors
- `Critical`: Application-level failures

## Cost Optimization

### VM States and Billing

| State | Compute Charges | Storage Charges |
|-------|----------------|-----------------|
| Running | Yes | Yes |
| Stopped | Yes | Yes |
| Deallocated | No | Yes |

**The deallocation rule saves money by stopping compute charges for stopped VMs.**

## Dependencies

```xml
<PackageReference Include="Azure.Identity" Version="1.13.1" />
<PackageReference Include="Azure.ResourceManager" Version="1.13.0" />
<PackageReference Include="Azure.ResourceManager.Compute" Version="1.6.0" />
<PackageReference Include="Azure.ResourceManager.Resources" Version="1.9.0" />
<PackageReference Include="CsvHelper" Version="33.0.1" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.1" />
```

## Stopping the Application

Press `Ctrl+C` to gracefully stop the application. The current polling cycle will complete before shutdown.

## Troubleshooting

### Authentication Issues

```bash
# Verify Azure CLI login
az account show

# Re-login if needed
az login
```

### No VMs Found

- Verify subscription ID in `appsettings.json`
- Check Azure permissions (Reader role required)
- Ensure VMs exist in the subscription

### VMs Not Being Managed

- Verify `Autoshutdown=1` tag is set on VMs
- Check logs for errors during power management operations
- Ensure VM Contributor role is assigned

## License

This project is provided as-is for evaluation purposes.

## Support

For issues or questions regarding this application, please refer to the implementation documentation or contact the development team.
