// See https://aka.ms/new-console-template for more information
using Azure.Identity;
using Azure.ResourceManager;
using Azure_Tech_Assignment.Configuration;
using Azure_Tech_Assignment.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure_Tech_Assignment;

internal static class Program
{
    private static async Task Main()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = configuration
            .GetSection("VmAutoScheduler")
            .Get<VmAutoSchedulerSettings>() ?? new VmAutoSchedulerSettings();

        var azureSettings = configuration
            .GetSection("Azure")
            .Get<AzureSettings>() ?? new AzureSettings();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("AzureVmAutoscheduler");

        try
        {
            logger.LogInformation("Azure VM Autoscheduler starting");
            logger.LogInformation("Polling interval: {Minutes} minutes", settings.PollingIntervalMinutes);
            logger.LogInformation("Max running time: {Hours} hours", settings.MaxRunningHours);

            var credentialOptions = new DefaultAzureCredentialOptions();

            if (!string.IsNullOrWhiteSpace(azureSettings.TenantId))
            {
                credentialOptions.TenantId = azureSettings.TenantId;
            }

            var credential = new DefaultAzureCredential(credentialOptions);

            // Important: no SubscriptionId here, because the task requires all subscriptions in the tenant.
            var armClient = new ArmClient(credential);

            var csvFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                $"vm_logs_{DateTime.UtcNow:yyyyMMdd}.csv");

            var stateFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "vm_running_state.json");

            var stateStore = new VmRunningStateStore(
                stateFilePath,
                loggerFactory.CreateLogger<VmRunningStateStore>());

            var vmMonitoringService = new VmMonitoringService(
                armClient,
                stateStore,
                settings,
                loggerFactory.CreateLogger<VmMonitoringService>());

            var vmPowerManagementService = new VmPowerManagementService(
                armClient,
                settings,
                loggerFactory.CreateLogger<VmPowerManagementService>());

            var csvLogger = new CsvLogger(
                csvFilePath,
                loggerFactory.CreateLogger<CsvLogger>());

            using var cancellationTokenSource = new CancellationTokenSource();

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                logger.LogInformation("Shutdown signal received");
                cancellationTokenSource.Cancel();
            };

            await RunPollingLoopAsync(
                vmMonitoringService,
                vmPowerManagementService,
                csvLogger,
                settings,
                logger,
                cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Fatal error in application");
        }
    }

    private static async Task RunPollingLoopAsync(
        VmMonitoringService vmMonitoringService,
        VmPowerManagementService vmPowerManagementService,
        CsvLogger csvLogger,
        VmAutoSchedulerSettings settings,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var pollingInterval = TimeSpan.FromMinutes(settings.PollingIntervalMinutes);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var cycleStartedAt = DateTime.UtcNow;

                logger.LogInformation("Polling cycle started at {TimestampUtc:u}", cycleStartedAt);

                var vmInfos = await vmMonitoringService.CollectVmInfoAsync(cancellationToken);

                await csvLogger.LogVmInfoAsync(vmInfos, cancellationToken);

                await vmPowerManagementService.ApplyPowerManagementRulesAsync(vmInfos, cancellationToken);

                var elapsed = DateTime.UtcNow - cycleStartedAt;

                logger.LogInformation(
                    "Polling cycle completed. VMs: {Count}. Duration: {Seconds:F2} seconds",
                    vmInfos.Count,
                    elapsed.TotalSeconds);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Polling loop cancelled");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Polling cycle failed. Application will continue.");
            }

            try
            {
                await Task.Delay(pollingInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Application stopped gracefully");
    }
}
