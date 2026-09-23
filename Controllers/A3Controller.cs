using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tap.Models;
using tap.Services;

namespace tap.Controllers;

[Authorize(Policy = "ITDriftOrTeacher")]
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
        string TeacherGroupId,
        string A3StudentsGroupId)
        GetRequiredGroups()
    {
        var itDriftGroupId =
            _config["Groups:ITDrift"];

        var teacherGroupId =
            _config["Groups:Teachers"];

        var a3StudentsGroupId =
            _config["Groups:A3Students"];

        if (string.IsNullOrWhiteSpace(itDriftGroupId) ||
            string.IsNullOrWhiteSpace(teacherGroupId) ||
            string.IsNullOrWhiteSpace(a3StudentsGroupId))
        {
            throw new InvalidOperationException(
                "Mangler gruppekonfigurasjon i appsettings.json.");
        }

        return (
            itDriftGroupId,
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
            groups.TeacherGroupId
        };

        groupsToCheck.AddRange(
            schools.Values);

        var memberships =
            await _graph.CheckUserGroupsAsync(
                loggedInUserId,
                groupsToCheck);

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

        if (!isTeacher)
        {
            return (
                false,
                new List<
                    KeyValuePair<string, string>>());
        }

        var allowedSchools =
            schools
                .Where(x =>
                    membershipSet.Contains(
                        x.Value))
                .ToList();

        return (
            false,
            allowedSchools);
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
        var a3Membership =
            await _graph.CheckUserGroupsAsync(
                targetUserId,
                new[]
                {
                    groups.A3StudentsGroupId
                });

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

        var targetSchoolMemberships =
            await _graph.CheckUserGroupsAsync(
                targetUserId,
                schoolGroupIds);

        return targetSchoolMemberships.Count > 0;
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
