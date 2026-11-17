namespace tap.Models;

public class StudentDeviceViewModel
{
    public string Id { get; set; } = "";
    public string AzureAdDeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ManagementName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string ComplianceState { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public bool? IsEncrypted { get; set; }
    public long? FreeStorageSpaceInBytes { get; set; }
    public long? TotalStorageSpaceInBytes { get; set; }
    public string UserPrincipalName { get; set; } = "";
    public DateTimeOffset? LastSyncDateTime { get; set; }
    public DateTimeOffset? EnrolledDateTime { get; set; }

    public bool IsLastSyncOlderThanSevenDays =>
        !LastSyncDateTime.HasValue ||
        LastSyncDateTime.Value < DateTimeOffset.UtcNow.AddDays(-7);
}
