namespace tap.Services;

public class AuditService
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AuditService(IConfiguration configuration)
    {
        _logFilePath =
            configuration["Audit:LogFilePath"]
            ?? @"C:\Logger\tap-audit.log";
    }

    public async Task WriteAsync(
        string result,
        string operatorUpn,
        string operatorOid,
        string targetName,
        string targetUpn,
        string targetOid,
        string? school,
        string? targetClass,
        string? ipAddress,
        string? message = null,
        string action = "GenerateTAP",
        string? deviceSerial = null,
        string? deviceId = null,
        string? entraDeviceId = null)
    {
        var timestamp =
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

        var line =
            $"{timestamp} | " +
            $"{Clean(result)} | " +
            $"Operator={Clean(operatorUpn)} | " +
            $"OperatorOid={Clean(operatorOid)} | " +
            $"TargetName={Clean(targetName)} | " +
            $"TargetUpn={Clean(targetUpn)} | " +
            $"TargetOid={Clean(targetOid)} | " +
            $"School={Clean(school)} | " +
            $"Class={Clean(targetClass)} | " +
            $"Action={Clean(action)} | " +
            $"DeviceSerial={Clean(deviceSerial)} | " +
            $"DeviceId={Clean(deviceId)} | " +
            $"EntraDeviceId={Clean(entraDeviceId)} | " +
            $"IP={Clean(ipAddress)}";

        if (!string.IsNullOrWhiteSpace(message))
        {
            line +=
                $" | Message={Clean(message)}";
        }

        await _lock.WaitAsync();

        try
        {
            var directory =
                Path.GetDirectoryName(_logFilePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.AppendAllTextAsync(
                _logFilePath,
                line + Environment.NewLine);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<AuditHistoryItem>> GetStudentHistoryAsync(string targetOid, int maxItems = 10)
    {
        var items = new List<AuditHistoryItem>();
        if (string.IsNullOrWhiteSpace(targetOid) || !File.Exists(_logFilePath))
            return items;

        string[] lines;
        try
        {
            // Read a bounded tail to avoid loading an unbounded audit file per request.
            lines = File.ReadLines(_logFilePath).TakeLast(5000).ToArray();
        }
        catch
        {
            return items;
        }

        var allowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "GenerateTAP", "WipeDevice", "ViewBitLockerKey", "ViewBitLockerKeys", "SyncDevice" };

        foreach (var line in lines.Reverse())
        {
            var fields = ParseLine(line);
            if (!fields.TryGetValue("TargetOid", out var lineTargetOid) ||
                !string.Equals(lineTargetOid, targetOid, StringComparison.OrdinalIgnoreCase) ||
                !fields.TryGetValue("Action", out var action) ||
                !allowedActions.Contains(action))
                continue;

            fields.TryGetValue("Timestamp", out var timestampText);
            DateTimeOffset.TryParse(timestampText, out var timestamp);
            fields.TryGetValue("Result", out var result);
            fields.TryGetValue("Operator", out var operatorUpn);
            fields.TryGetValue("DeviceSerial", out var deviceSerial);
            items.Add(new AuditHistoryItem(timestamp, action, deviceSerial, operatorUpn, result));
            if (items.Count >= maxItems) break;
        }

        return items;
    }

    private static Dictionary<string, string> ParseLine(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = line.Split(" | ", StringSplitOptions.None);
        if (parts.Length >= 2)
        {
            result["Timestamp"] = parts[0].Trim();
            result["Result"] = parts[1].Trim();
        }
        foreach (var part in parts.Skip(2))
        {
            var separator = part.IndexOf('=');
            if (separator > 0)
                result[part[..separator].Trim()] = part[(separator + 1)..].Trim();
        }
        return result;
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        return value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("|", "/")
            .Trim();
    }
}

public sealed record AuditHistoryItem(
    DateTimeOffset Timestamp,
    string Action,
    string? DeviceSerial,
    string? OperatorUpn,
    string? Result);
