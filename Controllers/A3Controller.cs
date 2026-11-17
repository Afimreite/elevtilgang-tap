using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tap.Models;
using tap.Services;

namespace tap.Controllers;

[Authorize(Policy = "ITDriftOrTeacherOrSuperuser")]
public class A3Controller : Controller
{
    private readonly GraphService _graph;
    private readonly IConfiguration _config;
    private readonly AuditService _audit;
    private readonly ILogger<A3Controller> _logger;

public A3Controller(
    GraphService graph,
    IConfiguration config,
    AuditService audit,
    ILogger<A3Controller> logger)
{
    _graph = graph;
    _config = config;
    _audit = audit;
    _logger = logger;
}

    // =====================================================
    // HENT OID FOR INNLOGGET BRUKER
    // =====================================================
    private string? GetLoggedInUserId()
    {
        return User.Claims
            .FirstOrDefault(c =>
                c.Type ==
                "http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?.Value;
    }

    // =====================================================
    // HENT GRUNNKONFIG
    // =====================================================
    private (
        string ITDriftGroupId,
        string SuperbrukereGroupId,
        string TeacherGroupId,
        string A3StudentsGroupId)
        GetRequiredGroups()
    {
        var itDriftGroupId =
            _config["Groups:ITDrift"];

        var teacherGroupId =
            _config["Groups:Teachers"];

        var superbrukereGroupId =
            _config["Groups:Superbrukere"];

        var a3StudentsGroupId =
            _config["Groups:A3Students"];

        if (string.IsNullOrWhiteSpace(itDriftGroupId) ||
            string.IsNullOrWhiteSpace(superbrukereGroupId) ||
            string.IsNullOrWhiteSpace(teacherGroupId) ||
            string.IsNullOrWhiteSpace(a3StudentsGroupId))
        {
            throw new InvalidOperationException(
                "Mangler gruppekonfigurasjon i appsettings.json.");
        }

        return (
            itDriftGroupId,
            superbrukereGroupId,
            teacherGroupId,
            a3StudentsGroupId);
    }

    // =====================================================
    // HENT SKOLECONFIG
    // =====================================================
    private List<IConfigurationSection> GetSchoolSections()
    {
        return _config
            .GetSection("Groups:Schools")
            .GetChildren()
            .Where(x =>
                !string.IsNullOrWhiteSpace(
                    x["GroupId"]))
            .ToList();
    }

    // =====================================================
    // BESTEM HVILKE SKOLER INNLOGGET BRUKER KAN SE
    // =====================================================
    private async Task<
        (
            bool IsITDrift,
            List<KeyValuePair<string, string>> AllowedSchools
        )>
        GetAllowedSchoolsAsync(string loggedInUserId)
    {
        var groups = GetRequiredGroups();

        var schoolSections =
            GetSchoolSections();

        var schools = schoolSections
            .ToDictionary(
                x => x.Key,
                x => x["GroupId"]!,
                StringComparer.OrdinalIgnoreCase);

        var groupsToCheck = new List<string>
        {
            groups.ITDriftGroupId,
            groups.TeacherGroupId,
            groups.SuperbrukereGroupId
        };

        groupsToCheck.AddRange(
            schools.Values);

        List<string> memberships;
        try
        {
            memberships = await _graph.CheckUserGroupsAsync(
                loggedInUserId,
                groupsToCheck);
        }
        catch
        {
            // Access cannot be established safely when Graph is unavailable.
            return (false, new List<KeyValuePair<string, string>>());
        }

        var membershipSet =
            memberships.ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        var isITDrift =
            membershipSet.Contains(
                groups.ITDriftGroupId);

        if (isITDrift)
        {
            return (
                true,
                schools.ToList());
        }

        var isTeacher =
            membershipSet.Contains(
                groups.TeacherGroupId);

        var isSuperbruker =
            membershipSet.Contains(
                groups.SuperbrukereGroupId);

        if (!isTeacher && !isSuperbruker)
        {
            return (
                false,
                new List<
                    KeyValuePair<string, string>>());
        }

        // Teachers and superusers both use the configured school groups.
        // Superuser access must not depend on membership in Teachers.
        var allowedSchools =
            schools
                .Where(x =>
                    membershipSet.Contains(
                        x.Value))
                .ToList();

        return (false, allowedSchools);
    }

    // =====================================================
    // SJEKK OM EN MÅLBRUKER ER EN ELEV BRUKEREN HAR TILGANG TIL
    // =====================================================
    private async Task<bool> CanManageStudentAsync(
        string loggedInUserId,
        string targetUserId)
    {
        var groups =
            GetRequiredGroups();

        var access =
            await GetAllowedSchoolsAsync(
                loggedInUserId);

        if (access.AllowedSchools.Count == 0)
            return false;

        // Må alltid være A3-elev
        List<string> a3Membership;
        try
        {
            a3Membership = await _graph.CheckUserGroupsAsync(
                targetUserId,
                new[] { groups.A3StudentsGroupId });
        }
        catch
        {
            return false;
        }

        if (!a3Membership.Contains(
                groups.A3StudentsGroupId,
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // ITDrift kan administrere alle A3-elever
        if (access.IsITDrift)
            return true;

        // Lærer må i tillegg dele skole med eleven
        var schoolGroupIds =
            access.AllowedSchools
                .Select(x => x.Value)
                .ToList();

        List<string> targetSchoolMemberships;
        try
        {
            targetSchoolMemberships = await _graph.CheckUserGroupsAsync(
                targetUserId,
                schoolGroupIds);
        }
        catch
        {
            return false;
        }

        return targetSchoolMemberships.Count > 0;
    }

    private async Task<bool> CanWipeDevicesAsync(string loggedInUserId)
    {
        var groups = GetRequiredGroups();

        var memberships = await _graph.CheckUserGroupsAsync(
            loggedInUserId,
            new[]
            {
                groups.ITDriftGroupId,
                groups.SuperbrukereGroupId
            });

        return memberships.Contains(
                   groups.ITDriftGroupId,
                   StringComparer.OrdinalIgnoreCase) ||
               memberships.Contains(
                   groups.SuperbrukereGroupId,
                   StringComparer.OrdinalIgnoreCase);
    }

    // =====================================================
    // LISTE ELEVER
    // =====================================================
    public async Task<IActionResult> Index()
    {
        var loggedInUserId =
            GetLoggedInUserId();

        if (string.IsNullOrWhiteSpace(
            loggedInUserId))
        {
            return Forbid();
        }

        var groups =
            GetRequiredGroups();

        var access =
            await GetAllowedSchoolsAsync(
                loggedInUserId);

        if (access.AllowedSchools.Count == 0)
            return Forbid();

        var schoolSections =
            GetSchoolSections();

        // ==========================================
        // HENT ALLE A3-ELEVER
        // ==========================================
        var allA3Students =
            await _graph.GetGroupMembersAsync(
                groups.A3StudentsGroupId);

        var a3StudentIds =
            allA3Students
                .Select(x => x.Id)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        // ==========================================
        // USERID -> SKOLE
        // ==========================================
        var studentSchools =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        // ==========================================
        // USERID -> KLASSE
        // ==========================================
        var studentClasses =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        // ==========================================
        // BEHANDLE TILLATTE SKOLER
        // ==========================================
        foreach (var school
            in access.AllowedSchools)
        {
            var schoolName =
                school.Key;

            var schoolGroupId =
                school.Value;

            var schoolMembers =
                await _graph.GetGroupMembersAsync(
                    schoolGroupId);

            foreach (var member
                in schoolMembers)
            {
                if (a3StudentIds.Contains(
                    member.Id))
                {
                    studentSchools[
                        member.Id] =
                        schoolName;
                }
            }

            var schoolConfig =
                schoolSections.First(x =>
                    x.Key.Equals(
                        schoolName,
                        StringComparison.OrdinalIgnoreCase));

            var classSections =
                schoolConfig
                    .GetSection("Classes")
                    .GetChildren();

            foreach (var classSection
                in classSections)
            {
                var className =
                    classSection.Key;

                var classGroupId =
                    classSection.Value;

                if (string.IsNullOrWhiteSpace(
                        classGroupId) ||
                    classGroupId.StartsWith(
                        "GUID-",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var classMembers =
                    await _graph
                        .GetGroupMembersAsync(
                            classGroupId);

                foreach (var member
                    in classMembers)
                {
                    if (a3StudentIds.Contains(
                        member.Id))
                    {
                        studentClasses[
                            member.Id] =
                            className;
                    }
                }
            }
        }

        // ==========================================
        // BYGG LISTE
        // ==========================================
        var visibleStudents =
            allA3Students
                .Where(student =>
                    studentSchools.ContainsKey(
                        student.Id))
                .Select(student =>
                    new StudentViewModel
                    {
                        Id = student.Id,
                        Name = student.Name,
                        UPN = student.UPN,

                        SchoolName =
                            studentSchools.TryGetValue(
                                student.Id,
                                out var schoolName)
                                    ? schoolName
                                    : "",

                        ClassName =
                            studentClasses.TryGetValue(
                                student.Id,
                                out var className)
                                    ? className
                                    : ""
                    })
                .OrderBy(x =>
                    x.SchoolName)
                .ThenBy(x =>
                    string.IsNullOrEmpty(
                        x.ClassName)
                        ? 1
                        : 0)
                .ThenBy(x =>
                    x.ClassName)
                .ThenBy(x =>
                    x.Name)
                .ToList();

        var deviceSerialNumbers =
            await _graph.GetManagedDeviceSerialNumbersAsync();

        var serialsByUpn =
            deviceSerialNumbers
                .GroupBy(x => x.UserPrincipalName,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(x => x.SerialNumber)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

        foreach (var student in visibleStudents)
        {
            if (serialsByUpn.TryGetValue(
                student.UPN,
                out var serialNumbers))
            {
                student.SerialNumbers = serialNumbers;
            }
        }

        ViewBag.AccessType =
            access.IsITDrift
                ? "ITDrift"
                : "Teacher";

        ViewBag.SchoolName =
            access.IsITDrift
                ? "Alle skoler"
                : string.Join(
                    ", ",
                    access.AllowedSchools
                        .Select(x => x.Key));

        return View(
            visibleStudents);
    }

    [HttpGet]
    public async Task<IActionResult> Student(string id)
    {
        var loggedInUserId = GetLoggedInUserId();

        if (string.IsNullOrWhiteSpace(loggedInUserId) ||
            string.IsNullOrWhiteSpace(id))
            return Forbid();

        if (!await CanManageStudentAsync(loggedInUserId, id))
            return Forbid();

        var student = (await _graph.GetGroupMembersAsync(
                GetRequiredGroups().A3StudentsGroupId))
            .FirstOrDefault(x => x.Id.Equals(
                id, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(student.Id))
            return NotFound();

        var devices = (await _graph.GetManagedDevicesAsync())
            .Where(device =>
                string.Equals(
                    device.UserPrincipalName,
                    student.UPN,
                    StringComparison.OrdinalIgnoreCase))
            .Select(device => new StudentDeviceViewModel
            {
                Id = device.Id ?? "",
                AzureAdDeviceId = device.AzureADDeviceId ?? "",
                Name = device.DeviceName ?? "",
                ManagementName = device.ManagedDeviceName ?? "",
                Manufacturer = device.Manufacturer ?? "",
                Model = device.Model ?? "",
                OperatingSystem = device.OperatingSystem ?? "",
                SerialNumber = device.SerialNumber ?? "",
                ComplianceState = device.ComplianceState?.ToString() ?? "",
                OsVersion = device.OsVersion ?? "",
                IsEncrypted = device.IsEncrypted,
                FreeStorageSpaceInBytes = device.FreeStorageSpaceInBytes,
                TotalStorageSpaceInBytes = device.TotalStorageSpaceInBytes,
                UserPrincipalName = device.UserPrincipalName ?? "",
                LastSyncDateTime = device.LastSyncDateTime,
                EnrolledDateTime = device.EnrolledDateTime
            })
            .OrderBy(x => x.Name)
            .ToList();

        ViewBag.StudentName = student.Name;
        ViewBag.StudentUpn = student.UPN;
        ViewBag.CanWipe = await CanWipeDevicesAsync(loggedInUserId);
        ViewBag.CanViewBitlocker = await CanViewBitlockerAsync(loggedInUserId);
        ViewBag.ActionHistory = await _audit.GetStudentHistoryAsync(student.Id);

        return View(devices);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncDevice(string studentId, string deviceId)
    {
        var operatorId = GetLoggedInUserId();
        if (string.IsNullOrWhiteSpace(operatorId) ||
            string.IsNullOrWhiteSpace(studentId) ||
            string.IsNullOrWhiteSpace(deviceId))
            return Unauthorized();

        if (!await CanWipeDevicesAsync(operatorId) ||
            !await CanManageStudentAsync(operatorId, studentId))
            return Forbid();

        var validated = await GetValidatedDeviceAsync(studentId, deviceId);
        if (validated.Student == null || validated.Device == null ||
            string.IsNullOrWhiteSpace(validated.School))
            return Forbid();

        try
        {
            await _graph.SyncManagedDeviceAsync(deviceId);
            await _audit.WriteAsync("SUCCESS", User.Identity?.Name ?? "", operatorId,
                validated.Student.Value.Name, validated.Student.Value.UPN, validated.Student.Value.Id,
                validated.School, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                "Synkroniseringsforespørsel sendt til Intune", "SyncDevice",
                deviceSerial: validated.Device.SerialNumber, deviceId: validated.Device.Id,
                entraDeviceId: validated.Device.AzureADDeviceId);

            return Json(new { success = true, message = "Synkroniseringsforespørselen er sendt til Intune. Enheten er ikke nødvendigvis synkronisert ennå." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Intune sync failed. OperatorOid={OperatorOid}, DeviceId={DeviceId}", operatorId, deviceId);
            await _audit.WriteAsync("ERROR", User.Identity?.Name ?? "", operatorId,
                validated.Student.Value.Name, validated.Student.Value.UPN, validated.Student.Value.Id,
                validated.School, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                "Intune avviste eller kunne ikke behandle synkroniseringsforespørselen", "SyncDevice",
                deviceSerial: validated.Device.SerialNumber, deviceId: validated.Device.Id,
                entraDeviceId: validated.Device.AzureADDeviceId);
            return StatusCode(502, new { success = false, message = "Synkroniseringsforespørselen kunne ikke sendes." });
        }
    }

    private async Task<bool> CanViewBitlockerAsync(string loggedInUserId)
    {
        var groups = GetRequiredGroups();
        try
        {
            var memberships = await _graph.CheckUserGroupsAsync(
                loggedInUserId,
                new[] { groups.ITDriftGroupId, groups.SuperbrukereGroupId });

            return memberships.Contains(groups.ITDriftGroupId, StringComparer.OrdinalIgnoreCase) ||
                   memberships.Contains(groups.SuperbrukereGroupId, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<(
        (string Id, string Name, string UPN)? Student,
        Microsoft.Graph.Models.ManagedDevice? Device,
        string? School)> GetValidatedDeviceAsync(string studentId, string managedDeviceId)
    {
        var student = (await _graph.GetGroupMembersAsync(
                GetRequiredGroups().A3StudentsGroupId))
            .FirstOrDefault(x => x.Id.Equals(studentId, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(student.Id))
            return (null, null, null);

        var device = (await _graph.GetManagedDevicesAsync())
            .FirstOrDefault(x =>
                string.Equals(x.Id, managedDeviceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.UserPrincipalName, student.UPN, StringComparison.OrdinalIgnoreCase));

        if (device == null)
            return (student, null, null);

        return (student, device, await GetStudentSchoolAsync(student.Id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetBitlockerKeys(string studentId, string deviceId)
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        var operatorId = GetLoggedInUserId();
        if (string.IsNullOrWhiteSpace(operatorId) ||
            string.IsNullOrWhiteSpace(studentId) ||
            string.IsNullOrWhiteSpace(deviceId) ||
            !await CanViewBitlockerAsync(operatorId) ||
            !await CanManageStudentAsync(operatorId, studentId))
            return Forbid();

        var validated = await GetValidatedDeviceAsync(studentId, deviceId);
        if (validated.Student == null || validated.Device == null ||
            string.IsNullOrWhiteSpace(validated.School) ||
            string.IsNullOrWhiteSpace(validated.Device.AzureADDeviceId))
            return NotFound(new { success = false, message = "BitLocker-nøkler er ikke tilgjengelige for enheten." });

        try
        {
            var keys = await _graph.GetBitlockerRecoveryKeysAsync(validated.Device.AzureADDeviceId);
            await _audit.WriteAsync("SUCCESS", User.Identity?.Name ?? "", operatorId,
                validated.Student.Value.Name, validated.Student.Value.UPN, validated.Student.Value.Id,
                validated.School, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                $"Hentet BitLocker-metadata for enhet {deviceId}", "ViewBitLockerKeys",
                deviceSerial: validated.Device.SerialNumber, deviceId: validated.Device.Id,
                entraDeviceId: validated.Device.AzureADDeviceId);

            return Json(new
            {
                success = true,
                keys = keys.Select(key => new
                {
                    id = key.Id,
                    deviceId = key.DeviceId,
                    createdDateTime = key.CreatedDateTime,
                    volumeType = key.VolumeType?.ToString()
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BitLocker metadata lookup failed. OperatorOid={OperatorOid}, DeviceId={DeviceId}", operatorId, deviceId);
            await _audit.WriteAsync("ERROR", User.Identity?.Name ?? "", operatorId,
                validated.Student.Value.Name, validated.Student.Value.UPN, validated.Student.Value.Id,
                validated.School, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                "Microsoft Graph feilet ved henting av BitLocker-metadata", "ViewBitLockerKeys",
                deviceSerial: validated.Device.SerialNumber, deviceId: validated.Device.Id,
                entraDeviceId: validated.Device.AzureADDeviceId);
            return StatusCode(500, new { success = false, message = "BitLocker-nøkler kunne ikke hentes." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetBitlockerKey(string studentId, string deviceId, string keyId)
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        var operatorId = GetLoggedInUserId();
        if (string.IsNullOrWhiteSpace(operatorId) || string.IsNullOrWhiteSpace(keyId) ||
            !await CanViewBitlockerAsync(operatorId) ||
            !await CanManageStudentAsync(operatorId, studentId))
            return Forbid();

        var validated = await GetValidatedDeviceAsync(studentId, deviceId);
        if (validated.Student == null || validated.Device == null ||
            string.IsNullOrWhiteSpace(validated.School) ||
            string.IsNullOrWhiteSpace(validated.Device.AzureADDeviceId))
            return NotFound(new { success = false, message = "Enheten ble ikke funnet." });

        try
        {
            var keys = await _graph.GetBitlockerRecoveryKeysAsync(validated.Device.AzureADDeviceId);
            var selectedKey = keys.FirstOrDefault(key =>
                string.Equals(key.Id, keyId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(key.DeviceId, validated.Device.AzureADDeviceId, StringComparison.OrdinalIgnoreCase));

            if (selectedKey == null)
                return Forbid();

            var value = await _graph.GetBitlockerRecoveryKeyValueAsync(selectedKey.Id!);
            if (string.IsNullOrWhiteSpace(value))
                return NotFound(new { success = false, message = "BitLocker-nøkkelen finnes ikke." });

            await _audit.WriteAsync("SUCCESS", User.Identity?.Name ?? "", operatorId,
                validated.Student.Value.Name, validated.Student.Value.UPN, validated.Student.Value.Id,
                validated.School, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                $"Hentet BitLocker-nøkkel {selectedKey.Id} for enhet {deviceId}", "ViewBitLockerKey",
                deviceSerial: validated.Device.SerialNumber, deviceId: validated.Device.Id,
                entraDeviceId: validated.Device.AzureADDeviceId);

            return Json(new { success = true, key = value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BitLocker key lookup failed. OperatorOid={OperatorOid}, DeviceId={DeviceId}", operatorId, deviceId);
            await _audit.WriteAsync("ERROR", User.Identity?.Name ?? "", operatorId,
                validated.Student.Value.Name, validated.Student.Value.UPN, validated.Student.Value.Id,
                validated.School, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                "Microsoft Graph feilet ved henting av BitLocker-nøkkel", "ViewBitLockerKey",
                deviceSerial: validated.Device.SerialNumber, deviceId: validated.Device.Id,
                entraDeviceId: validated.Device.AzureADDeviceId);
            return StatusCode(500, new { success = false, message = "BitLocker-nøkkelen kunne ikke hentes." });
        }
    }

    private async Task<string?> GetStudentSchoolAsync(string studentId)
    {
        var schoolSections = GetSchoolSections();
        var schoolGroupIds = schoolSections
            .Select(section => (Name: section.Key, GroupId: section["GroupId"]!))
            .ToList();

        if (schoolGroupIds.Count == 0)
            return null;

        try
        {
            var memberships = await _graph.CheckUserGroupsAsync(
                studentId,
                schoolGroupIds.Select(x => x.GroupId));

            return schoolGroupIds
                .FirstOrDefault(x => memberships.Contains(
                    x.GroupId, StringComparer.OrdinalIgnoreCase))
                .Name;
        }
        catch
        {
            return null;
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WipeDevice(
        string deviceId,
        string studentId,
        bool keepUserData,
        bool confirmed)
    {
        var loggedInUserId = GetLoggedInUserId();

        if (string.IsNullOrWhiteSpace(loggedInUserId))
            return Unauthorized();

        if (!confirmed || string.IsNullOrWhiteSpace(deviceId) ||
            string.IsNullOrWhiteSpace(studentId))
            return BadRequest("Wipe krever aktiv bekreftelse.");

        if (!await CanWipeDevicesAsync(loggedInUserId))
            return Forbid();

        if (!await CanManageStudentAsync(loggedInUserId, studentId))
            return Forbid();

        var validated = await GetValidatedDeviceAsync(studentId, deviceId);
        if (validated.Student == null || validated.Device == null ||
            string.IsNullOrWhiteSpace(validated.School))
            return Forbid();

        var targetStudent = validated.Student.Value;
        var targetDevice = validated.Device;
        var targetSchool = validated.School;

        try
        {
            await _graph.WipeManagedDeviceAsync(deviceId, keepUserData);

            await _audit.WriteAsync(
                result: "SUCCESS",
                operatorUpn: User.Identity?.Name ?? "",
                operatorOid: loggedInUserId,
                targetName: targetStudent.Name,
                targetUpn: targetStudent.UPN,
                targetOid: targetStudent.Id,
                school: targetSchool,
                targetClass: null,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                message: $"Wipe sendt for enhet {deviceId}. Behold brukerdata={keepUserData}",
                action: "WipeDevice",
                deviceSerial: targetDevice.SerialNumber,
                deviceId: targetDevice.Id,
                entraDeviceId: targetDevice.AzureADDeviceId);

            TempData["Message"] = "Wipe-kommandoen er sendt til enheten.";
        }
        catch (Exception)
        {
            await _audit.WriteAsync(
                result: "ERROR",
                operatorUpn: User.Identity?.Name ?? "",
                operatorOid: loggedInUserId,
                targetName: targetStudent.Name,
                targetUpn: targetStudent.UPN,
                targetOid: targetStudent.Id,
                school: targetSchool,
                targetClass: null,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                message: "Wipe-forespørselen kunne ikke sendes til Intune.",
                action: "WipeDevice",
                deviceSerial: targetDevice.SerialNumber,
                deviceId: targetDevice.Id,
                entraDeviceId: targetDevice.AzureADDeviceId);

            TempData["Error"] = "Wipe kunne ikke sendes til enheten.";
        }

        return RedirectToAction(nameof(Student), new { id = studentId });
    }

     // =====================================================
    // GENERER TAP
    // =====================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateTap(
    string userId,
    string? studentName,
    string? studentUpn,
    string? school,
    string? studentClass)
    {
        if (string.IsNullOrWhiteSpace(
            userId))
        {
            return BadRequest(new
            {
                success = false,
                message = "Mangler bruker-ID."
            });
        }

        var loggedInUserId =
            GetLoggedInUserId();

        if (string.IsNullOrWhiteSpace(
            loggedInUserId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Brukeren er ikke innlogget."
            });
        }

        // ==========================================
        // KRITISK SIKKERHETSSJEKK
        // ==========================================
        var allowed =
            await CanManageStudentAsync(
                loggedInUserId,
                userId);

if (!allowed)
{
    // ==========================================
    // AUDIT - AVVIST TAP-FORSØK
    // ==========================================
    await _audit.WriteAsync(
        result: "DENIED",
        operatorUpn: User.Identity?.Name ?? "",
        operatorOid: loggedInUserId,
        targetName: studentName ?? "",
        targetUpn: studentUpn ?? "",
        targetOid: userId,
        school: school,
        targetClass: studentClass,
        ipAddress:
            HttpContext.Connection.RemoteIpAddress?.ToString(),
        message: "Ingen tilgang til målbruker"
    );

    return StatusCode(
        StatusCodes.Status403Forbidden,
        new
        {
            success = false,
            message =
                "Du har ikke tilgang til å generere TAP for denne eleven."
        });
}

        string? tap;

        // Bare Graph-kallet avgjør om TAP-genereringen lyktes.
        try
        {
            tap = await _graph.GenerateTapAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Graph feilet under TAP-generering. OperatorOid={OperatorOid}, TargetOid={TargetOid}",
                loggedInUserId,
                userId);

            try
            {
                await _audit.WriteAsync(
                    result: "ERROR",
                    operatorUpn: User.Identity?.Name ?? "",
                    operatorOid: loggedInUserId,
                    targetName: studentName ?? "",
                    targetUpn: studentUpn ?? "",
                    targetOid: userId,
                    school: school,
                    targetClass: studentClass,
                    ipAddress:
                        HttpContext.Connection.RemoteIpAddress?.ToString(),
                    message: "Microsoft Graph feilet under TAP-generering"
                );
            }
            catch (Exception auditException)
            {
                _logger.LogError(
                    auditException,
                    "Kunne ikke skrive ERROR til TAP-auditloggen. OperatorOid={OperatorOid}, TargetOid={TargetOid}",
                    loggedInUserId,
                    userId);
            }

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message =
                        "Det oppstod en feil under generering av TAP."
                });
        }

        if (string.IsNullOrWhiteSpace(tap))
        {
            _logger.LogError(
                "Graph returnerte ingen TAP. OperatorOid={OperatorOid}, TargetOid={TargetOid}",
                loggedInUserId,
                userId);

            try
            {
                await _audit.WriteAsync(
                    result: "ERROR",
                    operatorUpn: User.Identity?.Name ?? "",
                    operatorOid: loggedInUserId,
                    targetName: studentName ?? "",
                    targetUpn: studentUpn ?? "",
                    targetOid: userId,
                    school: school,
                    targetClass: studentClass,
                    ipAddress:
                        HttpContext.Connection.RemoteIpAddress?.ToString(),
                    message: "Microsoft Graph returnerte ingen TAP"
                );
            }
            catch (Exception auditException)
            {
                _logger.LogError(
                    auditException,
                    "Kunne ikke skrive ERROR til TAP-auditloggen. OperatorOid={OperatorOid}, TargetOid={TargetOid}",
                    loggedInUserId,
                    userId);
            }

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message =
                        "Kunne ikke generere TAP."
                });
        }

        // TAP-en er opprettet. En etterfølgende auditfeil må ikke
        // føre til at klienten får beskjed om at genereringen feilet.
        try
        {
            await _audit.WriteAsync(
                result: "SUCCESS",
                operatorUpn: User.Identity?.Name ?? "",
                operatorOid: loggedInUserId,
                targetName: studentName ?? "",
                targetUpn: studentUpn ?? "",
                targetOid: userId,
                school: school,
                targetClass: studentClass,
                ipAddress:
                    HttpContext.Connection.RemoteIpAddress?.ToString()
            );
        }
        catch (Exception auditException)
        {
            _logger.LogCritical(
                auditException,
                "TAP ble opprettet, men SUCCESS kunne ikke skrives til auditloggen. OperatorOid={OperatorOid}, TargetOid={TargetOid}",
                loggedInUserId,
                userId);
        }

return Json(new
{
    success = true,
    tap,
    lifetimeMinutes = _graph.TapLifetimeMinutes,
    isUsableOnce = _graph.TapIsUsableOnce
});

    }
}
