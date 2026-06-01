# Azure VM Autoscheduler

## Overview

Azure VM Autoscheduler is a .NET 8 console application that continuously monitors Azure Virtual Machines across all accessible subscriptions within a tenant and automatically applies power management rules to reduce infrastructure costs.

The application discovers virtual machines, logs their current state to CSV files, and automatically performs shutdown and deallocation operations for VMs tagged with:

```text
Autoshutdown=1
```

The solution is designed to be reliable, scalable, and efficient when managing hundreds of virtual machines across multiple Azure subscriptions.

---

## Features

### VM Discovery

* Discovers VMs across all subscriptions accessible to the authenticated identity
* Supports multi-subscription Azure environments
* Collects VM state information asynchronously

### Power Management

For VMs tagged with:

```text
Autoshutdown=1
```

the following rules are applied:

#### Rule 1 – Automatic Shutdown

If a VM has been running longer than the configured threshold (default: 8 hours), the application initiates a graceful shutdown.

#### Rule 2 – Automatic Deallocation

If a VM is in the **Stopped** state but remains allocated, the application automatically deallocates it to eliminate compute charges.

### CSV Logging

Every polling cycle appends VM information to a CSV file:

```text
vm_logs_YYYYMMDD.csv
```

Recorded fields:

| Column         | Description                   |
| -------------- | ----------------------------- |
| Timestamp      | UTC timestamp                 |
| SubscriptionId | Azure subscription identifier |
| ResourceGroup  | Resource group name           |
| ComputerName   | VM name                       |
| PowerState     | Current VM power state        |

### Persistent Runtime Tracking

Azure does not provide a reliable VM operating system boot timestamp through the Resource Manager API.

To accurately evaluate VM running duration, the application stores first-observed running timestamps in:

```text
vm_running_state.json
```

This allows runtime tracking to survive application restarts.

---

## Architecture

### Components

#### VmMonitoringService

Responsible for:

* Enumerating subscriptions
* Discovering resource groups
* Collecting VM information
* Determining power state
* Tracking VM runtime

#### VmPowerManagementService

Responsible for:

* Applying shutdown rules
* Applying deallocation rules
* Managing VM lifecycle operations

#### CsvLogger

Responsible for:

* Thread-safe CSV writing
* Append-only logging
* Daily log generation

#### VmRunningStateStore

Responsible for:

* Persisting VM runtime information
* Loading runtime state on startup
* Saving runtime state after each polling cycle

---

## Configuration

### appsettings.json

```json
{
  "Azure": {
    "TenantId": "your-tenant-id"
  },
  "VmAutoScheduler": {
    "PollingIntervalMinutes": 5,
    "MaxRunningHours": 8,
    "MaxParallelAzureCalls": 20,
    "MaxParallelPowerOperations": 10
  }
}
```

### Settings

| Setting                    | Description                        | Default |
| -------------------------- | ---------------------------------- | ------- |
| PollingIntervalMinutes     | Polling interval                   | 5       |
| MaxRunningHours            | Maximum VM runtime before shutdown | 8       |
| MaxParallelAzureCalls      | Concurrent Azure read operations   | 20      |
| MaxParallelPowerOperations | Concurrent power operations        | 10      |

---

## Authentication

The application uses:

```csharp
DefaultAzureCredential
```

Supported authentication mechanisms include:

* Managed Identity
* Azure CLI
* Visual Studio
* Visual Studio Code
* Environment Variables (Service Principal)

### Development

```bash
az login
```

Verify access:

```bash
az account show
```

### Production

Use Managed Identity whenever possible.

---

## Performance Considerations

The solution is optimized for environments containing hundreds of VMs.

### Optimizations

* Fully asynchronous implementation
* Parallel processing using Task.WhenAll
* Configurable throttling using SemaphoreSlim
* Non-blocking Azure operations using:

```csharp
WaitUntil.Started
```

* Exception isolation per VM
* Thread-safe collections

---

## Error Handling

The application is designed to run continuously.

Features include:

* Per-VM exception handling
* Subscription-level error isolation
* Polling cycle recovery
* Graceful shutdown using Ctrl+C
* Structured logging

Failures affecting individual VMs do not interrupt monitoring of other VMs.

---

## Running the Application

### Command Line

```bash
dotnet run
```

### Build

```bash
dotnet build
```

### Publish

```bash
dotnet publish -c Release
```

---

## Example Log Output

```text
Azure VM Autoscheduler starting

Polling cycle started

Found 1 subscription(s)

Logged 3 VM record(s)

Applying power rules to 2 VM(s) with Autoshutdown=1

Shutting down VM test-vm-autoshutdown

Shutdown operation started

Polling cycle completed
```

---

## Cost Optimization

Azure billing behavior:

| State       | Compute Charges |
| ----------- | --------------- |
| Running     | Yes             |
| Stopped     | Yes             |
| Deallocated | No              |

Automatic deallocation ensures that stopped VMs do not continue generating unnecessary compute costs.

---

## Technologies

* .NET 8
* Azure.ResourceManager
* Azure.Identity
* Azure.ResourceManager.Compute
* Azure.ResourceManager.Resources
* CsvHelper
* Microsoft.Extensions.Logging

---

## Assumptions

* All managed virtual machines are Windows-based.
* VM runtime is calculated using the first observed running timestamp.
* Only VMs tagged with `Autoshutdown=1` are managed.
* CSV logging is append-only.

---

## License

This project was created as part of the Azure VM Autoscheduler technical assessment.
