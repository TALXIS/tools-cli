using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// Translates between txc's role vocabulary and the connectivity API's
/// permission payloads. The role strings and request shapes are verified
/// against a live tenant: the platform accepts exactly <c>CanView</c>,
/// <c>CanViewWithShare</c> and <c>CanEdit</c> (the creator is <c>Owner</c>).
/// </summary>
internal static class ConnectionPermissionMapper
{
    public const string OwnerRole = "Owner";

    public static string ToPlatformRole(ConnectionPermissionRole role) => role switch
    {
        ConnectionPermissionRole.CanView => "CanView",
        ConnectionPermissionRole.CanViewWithShare => "CanViewWithShare",
        ConnectionPermissionRole.CanEdit => "CanEdit",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown connection permission role."),
    };

    /// <summary>
    /// Builds a <c>put</c> request. <c>put</c> is an upsert by principal, so it
    /// both grants and changes a role. A user may be addressed by object id or
    /// by e-mail (user principal name).
    /// </summary>
    public static JsonObject BuildPut(string? principalId, string? email, string principalType, string? tenantId, string platformRole)
    {
        var principal = new JsonObject { ["type"] = principalType };
        if (!string.IsNullOrWhiteSpace(principalId))
            principal["id"] = principalId;
        if (!string.IsNullOrWhiteSpace(email))
            principal["email"] = email;
        if (!string.IsNullOrWhiteSpace(tenantId))
            principal["tenantId"] = tenantId;

        return new JsonObject
        {
            ["put"] = new JsonArray(new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["roleName"] = platformRole,
                    ["principal"] = principal,
                    ["NotifyShareTargetOption"] = "DoNotNotify",
                },
            }),
            ["delete"] = new JsonArray(),
        };
    }

    /// <summary>
    /// Builds a <c>delete</c> request. Removal is keyed by the permission's own
    /// id (the full resource id); using the principal's object id is accepted
    /// by the platform but silently removes nothing.
    /// </summary>
    public static JsonObject BuildDelete(string apiId, string connectionId, string permissionId)
        => new()
        {
            ["put"] = new JsonArray(),
            ["delete"] = new JsonArray(new JsonObject
            {
                ["id"] = $"{ConnectivityEndpoints.ApiIdPrefix}{apiId}/connections/{connectionId}/permissions/{permissionId}",
            }),
        };

    public static IReadOnlyList<ConnectionPermission> ParseList(JsonElement root)
    {
        var list = new List<ConnectionPermission>();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var row in value.EnumerateArray())
        {
            var props = row.TryGetProperty("properties", out var p) ? p : default;
            var principal = props.ValueKind == JsonValueKind.Object && props.TryGetProperty("principal", out var pr) ? pr : default;

            var name = Str(row, "name");
            var role = Str(props, "roleName");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(role))
                continue;

            list.Add(new ConnectionPermission(
                name,
                Str(principal, "id"),
                Str(principal, "type"),
                Str(principal, "displayName"),
                Str(principal, "email"),
                role));
        }

        return list;
    }

    private static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
}
