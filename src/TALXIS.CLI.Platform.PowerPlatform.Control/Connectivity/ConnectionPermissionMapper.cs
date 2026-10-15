using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// Translates between <see cref="ConnectionPermissionRole"/> and the connectivity
/// API's permission payloads. The platform accepts exactly <c>CanView</c>,
/// <c>CanViewWithShare</c> and <c>CanEdit</c> as roles to grant; the creator is
/// <c>Owner</c>.
/// </summary>
internal static class ConnectionPermissionMapper
{
    /// <summary>
    /// Builds a <c>put</c> request. <c>put</c> is an upsert by principal, so it
    /// both grants and changes a role. A user may be addressed by object id or
    /// by e-mail (user principal name).
    /// </summary>
    public static JsonObject BuildPut(
        string? principalId, string? email, string principalType, string? tenantId, ConnectionPermissionRole role)
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
                    ["roleName"] = ConnectionRoleNames.ToPlatform(role),
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
                ["id"] = $"{ConnectorIds.PathPrefix}{apiId}/connections/{connectionId}/permissions/{permissionId}",
            }),
        };

    /// <summary>
    /// Reads a permission list. Rows without an id or with a role the CLI does
    /// not know are left out.
    /// </summary>
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

            var name = JsonElementReader.TryGetString(row, "name");
            if (string.IsNullOrWhiteSpace(name)
                || !ConnectionRoleNames.TryFromPlatform(JsonElementReader.TryGetString(props, "roleName"), out var role))
            {
                continue;
            }

            list.Add(new ConnectionPermission(
                name,
                JsonElementReader.TryGetString(principal, "id"),
                JsonElementReader.TryGetString(principal, "type"),
                JsonElementReader.TryGetString(principal, "displayName"),
                JsonElementReader.TryGetString(principal, "email"),
                role));
        }

        return list;
    }
}
