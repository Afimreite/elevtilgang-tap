using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        var lines = new List<string>();

        if (System.IO.File.Exists(logFilePath))
        {
            lines = System.IO.File
                .ReadAllLines(logFilePath)
                .Reverse()
                .Take(500)
                .ToList();
        }

        return View(lines);
    }
}