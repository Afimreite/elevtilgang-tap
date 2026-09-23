using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Extensions.Caching.Memory;

namespace tap.Services;

public class GraphService
{
private readonly GraphServiceClient _graphClient;
private readonly IMemoryCache _cache;
private readonly int _groupMembersCacheMinutes;
private readonly int _tapLifetimeMinutes;
private readonly bool _tapIsUsableOnce;

public int TapLifetimeMinutes => _tapLifetimeMinutes;
public bool TapIsUsableOnce => _tapIsUsableOnce;

public GraphService(
    string tenantId,
    string clientId,
    string clientSecret,
    IMemoryCache cache,
    IConfiguration configuration)
{
    _cache = cache;

    _groupMembersCacheMinutes =
        configuration.GetValue<int?>(
            "Cache:GroupMembersMinutes")
        ?? 60;

    _tapLifetimeMinutes =
        configuration.GetValue<int?>(
            "TAP:OneTime:LifetimeMinutes")
        ?? throw new InvalidOperationException(
            "Mangler TAP:OneTime:LifetimeMinutes i appsettings.json.");

    if (_tapLifetimeMinutes <= 0)
    {
        throw new InvalidOperationException(
            "TAP:OneTime:LifetimeMinutes må være større enn 0.");
    }

    // Default true preserves the existing one-time TAP behaviour.
    _tapIsUsableOnce = configuration.GetValue<bool?>(
        "TAP:OneTime:IsUsableOnce") ?? true;

    var credential = new ClientSecretCredential(
        tenantId,
        clientId,
        clientSecret);

    _graphClient = new GraphServiceClient(credential);
}

public async Task<List<string>> CheckUserGroupsAsync(
    string userId,
    IEnumerable<string> groupIds)
{
    var ids = groupIds
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (ids.Count == 0)
        return new List<string>();

    var requestBody =
        new Microsoft.Graph.Users.Item.CheckMemberGroups.CheckMemberGroupsPostRequestBody
        {
            GroupIds = ids
        };

var result = await _graphClient
    .Users[userId]
    .CheckMemberGroups
    .PostAsCheckMemberGroupsPostResponseAsync(requestBody);

    return result?.Value?.ToList() ?? new List<string>();
}



    // =========================
    // HENT GRUPPE-IDER
    // =========================
    public async Task<List<string>> GetUserGroupIdsAsync(string userId)
    {
        var requestBody = new Microsoft.Graph.Users.Item.GetMemberGroups.GetMemberGroupsPostRequestBody
        {
            SecurityEnabledOnly = false
        };

var result = await _graphClient
    .Users[userId]
    .GetMemberGroups
    .PostAsGetMemberGroupsPostResponseAsync(requestBody);

        return result?.Value?.ToList() ?? new List<string>();
    }

    // =========================
    // SJEKK GRUPPE
    // =========================
    public bool IsInGroup(List<string> userGroupIds, string groupId)
    {
        if (userGroupIds == null || string.IsNullOrWhiteSpace(groupId))
            return false;

        return userGroupIds.Contains(groupId);
    }

    // =========================
// HENT ALLE GRUPPEMEDLEMMER
// =========================
public async Task<List<(string Id, string Name, string UPN)>>
    GetGroupMembersAsync(string groupId)
{
    var cacheKey =
        $"group-members:{groupId}";

    if (_cache.TryGetValue(
        cacheKey,
        out List<(string Id, string Name, string UPN)>? cachedMembers) &&
        cachedMembers != null)
    {
        return cachedMembers;
    }

    var resultList =
        new List<(string Id, string Name, string UPN)>();

    var response = await _graphClient
        .Groups[groupId]
        .Members
        .GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Top = 999;
        });

    while (response != null)
    {
        if (response.Value != null)
        {
            foreach (var obj in response.Value)
            {
                if (obj is Microsoft.Graph.Models.User user)
                {
                    resultList.Add((
                        user.Id ?? "",
                        user.DisplayName ?? "",
                        user.UserPrincipalName ?? ""
                    ));
                }
            }
        }

        if (string.IsNullOrEmpty(
            response.OdataNextLink))
        {
            break;
        }

        response = await _graphClient
            .Groups[groupId]
            .Members
            .WithUrl(response.OdataNextLink)
            .GetAsync();
    }

    var sortedResult = resultList
        .OrderBy(x => x.Name)
        .ToList();

_cache.Set(
    cacheKey,
    sortedResult,
    new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow =
            TimeSpan.FromMinutes(
                _groupMembersCacheMinutes)
    });

    return sortedResult;
}

    // =========================
    // HAR AKTIV TAP?
    // =========================
    public async Task<bool> HasActiveTapAsync(string userId)
    {
        var result = await _graphClient
            .Users[userId]
            .Authentication
            .TemporaryAccessPassMethods
            .GetAsync();

        return result?.Value != null && result.Value.Count > 0;
    }

    // =========================
    // GENERER TAP
    // =========================
public async Task<string?> GenerateTapAsync(string userId)
{
    var requestBody = new Microsoft.Graph.Models.TemporaryAccessPassAuthenticationMethod
    {
        LifetimeInMinutes = _tapLifetimeMinutes,
        IsUsableOnce = _tapIsUsableOnce
    };

    var result = await _graphClient
        .Users[userId]
        .Authentication
        .TemporaryAccessPassMethods
        .PostAsync(requestBody);

    return result?.TemporaryAccessPass;
}


    // =========================
    // AUTHCHECK
    // =========================





    // =========================
    // GRUPPENAVN (valgfritt)
    // =========================
    public async Task<List<string>> GetGroupNamesAsync(List<string> groupIds)
    {
        var names = new List<string>();

        if (groupIds == null || groupIds.Count == 0)
            return names;

        foreach (var groupId in groupIds)
        {
            try
            {
                var group = await _graphClient.Groups[groupId].GetAsync();

                if (!string.IsNullOrEmpty(group?.DisplayName))
                    names.Add(group.DisplayName);
            }
            catch
            {
                // ignore
            }
        }

        return names;
    }
}
