using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tap.Models;

namespace tap.Controllers;

[Authorize(Policy = "ITDriftOnly")]
public class AuditController : Controller
{
    private readonly IConfiguration _config;

    public AuditController(IConfiguration config)
    {
        _config = config;
    }

    public IActionResult Index()
    {
        var logFilePath =
            _config["Audit:LogFilePath"]
            ?? @"C:\Logger\tap-audit.log";

        var entries = new List<AuditLogEntry>();

        if (System.IO.File.Exists(logFilePath))
        {
            try
            {
                entries = System.IO.File.ReadLines(logFilePath)
                    .Select(AuditLogEntry.Parse)
                    .OfType<AuditLogEntry>()
                    .OrderByDescending(entry => entry.Timestamp)
                    .Take(500)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewBag.ReadError = true;
            }
        }

        return View(entries);
    }
}
