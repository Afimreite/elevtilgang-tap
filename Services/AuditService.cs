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
        string? message = null)
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
            $"Action=GenerateTAP | " +
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