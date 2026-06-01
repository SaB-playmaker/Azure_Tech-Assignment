using Azure_Tech_Assignment.Models;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Azure_Tech_Assignment.Services;

public sealed class CsvLogger
{
    private readonly string _csvFilePath;
    private readonly ILogger<CsvLogger> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public CsvLogger(string csvFilePath, ILogger<CsvLogger> logger)
    {
        _csvFilePath = csvFilePath;
        _logger = logger;
    }

    public async Task LogVmInfoAsync(
        IEnumerable<VmInfo> vmInfos,
        CancellationToken cancellationToken)
    {
        var records = vmInfos.ToList();

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(_csvFilePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileExists = File.Exists(_csvFilePath);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = !fileExists
            };

            await using var stream = new FileStream(
                _csvFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);

            await using var writer = new StreamWriter(stream);
            await using var csv = new CsvWriter(writer, config);

            if (!fileExists)
            {
                csv.WriteHeader<VmInfoCsvRecord>();
                await csv.NextRecordAsync();
            }

            foreach (var vmInfo in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                csv.WriteRecord(new VmInfoCsvRecord
                {
                    Timestamp = vmInfo.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    SubscriptionId = vmInfo.SubscriptionId,
                    ResourceGroup = vmInfo.ResourceGroup,
                    ComputerName = vmInfo.ComputerName,
                    PowerState = vmInfo.PowerState
                });

                await csv.NextRecordAsync();
            }

            await writer.FlushAsync(cancellationToken);

            _logger.LogInformation(
                "Logged {Count} VM record(s) to CSV file {FilePath}",
                records.Count,
                _csvFilePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error writing VM information to CSV file {FilePath}",
                _csvFilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}