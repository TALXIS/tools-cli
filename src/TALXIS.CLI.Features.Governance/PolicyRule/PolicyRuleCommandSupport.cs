using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Shared profile-resolution and lookup helpers for
/// <c>txc governance policy-rule</c> commands. Policies are a tenant-wide
/// resource, resolved the same way <c>EnvironmentGroupCommandSupport</c>
/// resolves environment groups.
/// </summary>
internal static class PolicyRuleCommandSupport
{
    internal static Task<ResolvedProfileContext> ResolveContextAsync(string? profile, CancellationToken ct)
        => TxcServices.Get<IConfigurationResolver>().ResolveAsync(profile, ct);

    /// <summary>
    /// Resolves a policy by GUID id, or by exact/unique case-insensitive
    /// name match when a non-GUID value is passed.
    /// </summary>
    internal static async Task<PowerPlatformPolicy> ResolveAsync(
        Connection connection, Credential credential, string policy, CancellationToken ct)
    {
        var client = TxcServices.Get<IPowerPlatformPolicyRuleClient>();

        if (Guid.TryParse(policy, out var id))
        {
            var byId = await client.GetAsync(connection, credential, id, ct).ConfigureAwait(false);
            if (byId is null)
                throw new ArgumentException($"No policy was found with id '{policy}'.");

            return byId;
        }

        var all = await client.ListAsync(connection, credential, ct).ConfigureAwait(false);
        var matches = all.Where(p => string.Equals(p.Name, policy, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
            throw new ArgumentException($"No policy was found with name '{policy}'.");

        if (matches.Count > 1)
        {
            throw new ArgumentException(
                $"Multiple policies match name '{policy}': " +
                string.Join(", ", matches.Select(m => m.Id)) +
                ". Specify the policy id instead.");
        }

        return matches[0];
    }

    /// <summary>
    /// Builds the one <see cref="PowerPlatformPolicyRuleSet"/> a create/update
    /// invocation supplies, from either raw JSON or the friendlier
    /// <c>--allow-connector</c> shorthand — never both. Returns
    /// <c>null</c> when the caller supplied no rule-set options at all
    /// (e.g. a name-only rename during <c>update</c>).
    /// </summary>
    internal static PowerPlatformPolicyRuleSet? BuildRuleSet(
        string ruleSetId, string ruleSetVersion, string? inputsJson, IReadOnlyList<string>? allowConnectors)
    {
        bool hasInputsJson = !string.IsNullOrWhiteSpace(inputsJson);
        bool hasAllowConnectors = allowConnectors is { Count: > 0 };

        if (hasInputsJson && hasAllowConnectors)
        {
            throw new ArgumentException(
                "Specify either --rule-set-inputs-json or --allow-connector, not both.");
        }

        if (!hasInputsJson && !hasAllowConnectors)
            return null;

        string resolvedInputsJson;
        if (hasInputsJson)
        {
            resolvedInputsJson = inputsJson!;
        }
        else
        {
            var rules = allowConnectors!.Select(ParseAllowConnector).ToList();
            resolvedInputsJson = new PowerPlatformAdvancedConnectorPolicyInputs(rules).ToInputsJson();
        }

        return new PowerPlatformPolicyRuleSet(ruleSetId, ruleSetVersion, resolvedInputsJson);
    }

    /// <summary>
    /// Parses one <c>--allow-connector</c> value: either <c>connectorId</c>
    /// (every action and connection type on that connector is allowed) or
    /// <c>connectorId=action1,action2</c> (only the listed actions are
    /// allowed).
    /// </summary>
    private static PowerPlatformAllowedConnectorRule ParseAllowConnector(string value)
    {
        var parts = value.Split('=', 2);
        var connectorId = parts[0].Trim();

        if (string.IsNullOrWhiteSpace(connectorId))
            throw new ArgumentException($"Invalid --allow-connector value '{value}': connector id is required.");

        if (parts.Length == 1)
        {
            return new PowerPlatformAllowedConnectorRule(
                connectorId, PowerPlatformAllowedConnectorRule.AllAllowedMode, null,
                PowerPlatformAllowedConnectorRule.AllAllowedMode);
        }

        var actions = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (actions.Count == 0)
        {
            throw new ArgumentException(
                $"Invalid --allow-connector value '{value}': at least one action is required after '='.");
        }

        return new PowerPlatformAllowedConnectorRule(
            connectorId, PowerPlatformAllowedConnectorRule.SomeAllowedMode, actions,
            PowerPlatformAllowedConnectorRule.AllAllowedMode);
    }
}
