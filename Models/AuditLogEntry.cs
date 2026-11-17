using System.Globalization;

namespace tap.Models;

// Only fields intended for the ITDrift table are retained; Message is never exposed.
public sealed record AuditLogEntry(
    DateTimeOffset Timestamp, string Action, string Result, string Operator,
    string StudentName, string StudentUpn, string School, string DeviceSerial, string IpAddress)
{
    public string ActionLabel => Action.ToUpperInvariant() switch
    {
        "GENERATETAP" => "TAP generert",
        "VIEWBITLOCKERKEY" => "BitLocker-nøkkel hentet",
        "VIEWBITLOCKERKEYS" => "BitLocker-metadata hentet",
        "SYNCDEVICE" => "Synkronisering forespurt",
        "WIPEDEVICE" => "Wipe forespurt",
        "" => "Handling ikke registrert (eldre logg)",
        _ => "Annen administrativ handling"
    };

    public string ResultLabel => Result.ToUpperInvariant() switch
    {
        "SUCCESS" when Action.Equals("SyncDevice", StringComparison.OrdinalIgnoreCase) ||
                       Action.Equals("WipeDevice", StringComparison.OrdinalIgnoreCase) => "Forespørsel sendt",
        "SUCCESS" => "Vellykket",
        "DENIED" => "Avvist",
        "ERROR" => "Mislykket",
        _ => "Ukjent resultat"
    };

    public string ResultStyle => Result.ToUpperInvariant() switch
    {
        "SUCCESS" => "bg-success",
        "DENIED" => "bg-warning text-dark",
        "ERROR" => "bg-danger",
        _ => "bg-secondary"
    };

    public static AuditLogEntry? Parse(string line)
    {
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;
        if (!DateTimeOffset.TryParseExact(parts[0],
                new[] { "yyyy-MM-dd HH:mm:ss", "O" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp))
            return null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Action", "Operator", "TargetName", "TargetUpn", "School", "DeviceSerial", "IP" };
        foreach (var part in parts.Skip(2))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            var name = part[..separator].Trim();
            if (allowed.Contains(name)) fields.TryAdd(name, part[(separator + 1)..].Trim());
        }
        string Get(string name) =>
            fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) && value != "-"
                ? value : "—";
        var action = Get("Action");
        if (action == "—") action = "";
        return new(timestamp, action, parts[1], Get("Operator"), Get("TargetName"),
            Get("TargetUpn"), Get("School"),
            action.Equals("GenerateTAP", StringComparison.OrdinalIgnoreCase) ? "—" : Get("DeviceSerial"), Get("IP"));
    }
}
