using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Web;
using tap.Services;

var builder = WebApplication.CreateBuilder(args);

// ======================
// AUTH (Entra ID)
// ======================
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(
        builder.Configuration.GetSection("AzureAd"));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.AccessDeniedPath = "/Account/AccessDenied";
});
// ======================
// GRAPH SERVICE
// ======================
builder.Services.AddSingleton<GraphService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var cache = sp.GetRequiredService<IMemoryCache>();

    return new GraphService(
        config["AzureAd:TenantId"]
            ?? throw new InvalidOperationException("Missing AzureAd:TenantId"),

        config["AzureAd:ClientId"]
            ?? throw new InvalidOperationException("Missing AzureAd:ClientId"),

        config["AzureAd:ClientSecret"]
            ?? throw new InvalidOperationException("Missing AzureAd:ClientSecret"),

        cache,
        config
    );
});

// ======================
// AUTHORIZATION
// ======================
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ITDriftOnly", policy =>
    {
        policy.RequireAssertion(async context =>
        {
            var httpContext = context.Resource as HttpContext;

            if (httpContext == null)
                return false;

            var userId =
                context.User.FindFirst(
                    "http://schemas.microsoft.com/identity/claims/objectidentifier")
                ?.Value;

            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var graph =
                httpContext.RequestServices
                    .GetRequiredService<GraphService>();

            var config =
                httpContext.RequestServices
                    .GetRequiredService<IConfiguration>();

            var itDriftGroupId =
                config["Groups:ITDrift"];

            if (string.IsNullOrWhiteSpace(itDriftGroupId))
                return false;

            var memberships =
                await graph.CheckUserGroupsAsync(
                    userId,
                    new[] { itDriftGroupId });

            return memberships.Contains(
                itDriftGroupId,
                StringComparer.OrdinalIgnoreCase);
        });
    });

    options.AddPolicy("ITDriftOrTeacher", policy =>
    {
        policy.RequireAssertion(async context =>
        {
            var httpContext = context.Resource as HttpContext;

            if (httpContext == null)
                return false;

            var userId =
                context.User.FindFirst(
                    "http://schemas.microsoft.com/identity/claims/objectidentifier")
                ?.Value;

            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var graph =
                httpContext.RequestServices
                    .GetRequiredService<GraphService>();

            var config =
                httpContext.RequestServices
                    .GetRequiredService<IConfiguration>();

            var groupIds = new[]
            {
                config["Groups:ITDrift"],
                config["Groups:Teachers"]
            }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();

            if (groupIds.Length == 0)
                return false;

            var memberships =
                await graph.CheckUserGroupsAsync(userId, groupIds);

            return memberships.Any(membership =>
                groupIds.Contains(membership, StringComparer.OrdinalIgnoreCase));
        });
    });
});
// ======================
// AUDITING
// ======================
builder.Services.AddSingleton<AuditService>();


// ======================
// MVC
// ======================
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ======================
// ERROR HANDLING
// ======================
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// ======================
// PIPELINE
// ======================
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ======================
// ROUTING
// ======================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
