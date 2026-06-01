using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Azure_Tech_Assignment.Services;

public sealed class VmRunningStateStore
{
    private readonly string _filePath;
    private readonly ILogger<VmRunningStateStore> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _runningSinceByVmId = new();

    public VmRunningStateStore(
        string filePath,
        ILogger<VmRunningStateStore> logger)
    {
        _filePath = filePath;
        _logger = logger;

        Load();
    }

    public DateTime GetOrAddStartTime(string vmId, DateTime timestampUtc)
    {
        return _runningSinceByVmId.GetOrAdd(vmId, timestampUtc);
    }

    public void Remove(string vmId)
    {
        _runningSinceByVmId.TryRemove(vmId, out _);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                _runningSinceByVmId,
                new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save VM running state to {FilePath}", _filePath);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);

            var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);

            if (data is null)
            {
                return;
            }

            foreach (var item in data)
            {
                _runningSinceByVmId[item.Key] = DateTime.SpecifyKind(item.Value, DateTimeKind.Utc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load VM running state from {FilePath}", _filePath);
        }
    }
}