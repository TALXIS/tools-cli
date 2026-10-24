using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Contracts.PowerPlatform;

/// <summary>
/// A role assignment scoped to a single environment group (as opposed to the
/// whole tenant — see <see cref="PowerPlatformTenantRoleAssignment"/>).
/// Same wire shape as tenant/environment role assignments
/// (<c>principalObjectId</c>/<c>principalType</c>/<c>roleDefinitionId</c>/
/// <c>scope</c>), scoped to
/// <c>/environmentGroups/{environmentGroupId}</c> instead of
/// <c>/tenants/{tenantId}</c>.
/// </summary>
public sealed record PowerPlatformEnvironmentGroupRoleAssignment(
    string RoleAssignmentId,
    Guid EnvironmentGroupId,
    PowerPlatformPrincipalType PrincipalType,
    Guid PrincipalObjectId,
    Guid RoleDefinitionId,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ExpiresOn);

/// <summary>
/// Client abstraction over environment-group role-assignment endpoints under
/// <c>api.powerplatform.com/authorization/environmentGroups/{id}/roleAssignments</c>
/// (Preview).
/// </summary>
public interface IPowerPlatformEnvironmentGroupRoleClient
{
    Task<IReadOnlyList<PowerPlatformEnvironmentGroupRoleAssignment>> ListAsync(
        Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct);

    Task<PowerPlatformEnvironmentGroupRoleAssignment> AddAsync(
        Connection connection, Credential credential, Guid environmentGroupId,
        PowerPlatformPrincipalType principalType, Guid principalObjectId, Guid roleDefinitionId, CancellationToken ct);

    Task RemoveAsync(
        Connection connection, Credential credential, Guid environmentGroupId, string roleAssignmentId, CancellationToken ct);
}

/// <summary>
/// The resource type a policy is assigned to or a per-resource override
/// targets. Confirmed from the official REST API reference
/// (<c>PolicyAssignmentOverride.resourceType</c> /
/// <c>RuleAssignment.resourceType</c>) — <c>Tenant</c> is accepted on
/// override requests but never observed on a <c>RuleAssignment</c> response.
/// </summary>
public enum PowerPlatformPolicyAssignmentResourceType
{
    NotSpecified = 0,
    EnvironmentGroup = 1,
    Environment = 2,
    Tenant = 3,
}

/// <summary>
/// How a per-resource assignment override behaves: whether the target
/// resource is explicitly included or excluded from the policy it would
/// otherwise inherit (e.g. excluding one environment from a
/// group-wide assignment). Confirmed enum values from the official REST
/// API reference (<c>PolicyAssignmentOverride.behaviorType</c>).
/// </summary>
public enum PowerPlatformPolicyBehaviorType
{
    NotSpecified = 0,
    Include = 1,
    Exclude = 2,
}

/// <summary>
/// A per-resource override supplied when assigning a policy to an
/// environment group or environment (e.g. "assign to this group, but
/// exclude this one member environment").
/// </summary>
public sealed record PowerPlatformPolicyAssignmentOverride(
    PowerPlatformPolicyBehaviorType BehaviorType,
    Guid ResourceId,
    PowerPlatformPolicyAssignmentResourceType ResourceType);

/// <summary>
/// One rule set within a policy. <c>Id</c> is a type discriminator (the only
/// confirmed value as of this writing is <c>"ConnectorManagement"</c> for the
/// Advanced Connector Policy rule type — see
/// <see cref="PowerPlatformAdvancedConnectorPolicyInputs"/>). <c>InputsJson</c>
/// is kept as raw JSON rather than a closed DTO because the <c>inputs</c>
/// shape varies per rule type and only one rule type's shape is confirmed
/// so far; use <see cref="PowerPlatformAdvancedConnectorPolicyInputs"/> to
/// build/parse it for <c>ConnectorManagement</c> rule sets.
/// </summary>
public sealed record PowerPlatformPolicyRuleSet(
    string Id,
    string Version,
    string InputsJson)
{
    /// <summary>The only confirmed rule set type: Advanced Connector Policy.</summary>
    public const string ConnectorManagementRuleSetId = "ConnectorManagement";
}

/// <summary>
/// One connector's allow-list entry within an Advanced Connector Policy
/// (<see cref="PowerPlatformPolicyRuleSet.ConnectorManagementRuleSetId"/>)
/// rule set's <c>inputs.AllowedConnectorList</c>. Connectors NOT present in
/// the list are blocked by default (default-deny). Field names/casing and
/// enum values confirmed from the official Microsoft "Advanced Connector
/// Policy programmability" tutorial.
/// </summary>
public sealed record PowerPlatformAllowedConnectorRule(
    [property: System.Text.Json.Serialization.JsonPropertyName("AllowedConnector")] string AllowedConnector,
    [property: System.Text.Json.Serialization.JsonPropertyName("AllowedActionsMode")] string AllowedActionsMode,
    [property: System.Text.Json.Serialization.JsonPropertyName("AllowedActions")] IReadOnlyList<string>? AllowedActions,
    [property: System.Text.Json.Serialization.JsonPropertyName("AllowedConnectionTypesMode")] string AllowedConnectionTypesMode)
{
    /// <summary>All actions and connection types on this connector are allowed.</summary>
    public const string AllAllowedMode = "AllAllowed";

    /// <summary>Only the actions listed in <see cref="AllowedActions"/> are allowed.</summary>
    public const string SomeAllowedMode = "SomeAllowed";
}

/// <summary>
/// Strongly-typed helper for building/parsing the <c>inputs</c> JSON of a
/// <see cref="PowerPlatformPolicyRuleSet.ConnectorManagementRuleSetId"/> rule
/// set — the only rule set type whose <c>inputs</c> shape is confirmed as of
/// this writing (from the official ACP programmability tutorial). Other
/// rule set types must be authored via raw <see cref="PowerPlatformPolicyRuleSet.InputsJson"/>
/// until their shapes are confirmed.
/// </summary>
public sealed record PowerPlatformAdvancedConnectorPolicyInputs(
    [property: System.Text.Json.Serialization.JsonPropertyName("AllowedConnectorList")] IReadOnlyList<PowerPlatformAllowedConnectorRule> AllowedConnectorList)
{
    public string ToInputsJson() => System.Text.Json.JsonSerializer.Serialize(this);

    public static PowerPlatformAdvancedConnectorPolicyInputs FromInputsJson(string json)
        => System.Text.Json.JsonSerializer.Deserialize<PowerPlatformAdvancedConnectorPolicyInputs>(json)
            ?? throw new ArgumentException("Could not parse Advanced Connector Policy inputs JSON.", nameof(json));
}

/// <summary>
/// A single rule-based policy — the modern governance/policy framework that
/// is replacing classic DLP policies. Confirmed shape from
/// <c>api.powerplatform.com/governance/ruleBasedPolicies</c> (api-version
/// 2024-10-01).
/// </summary>
public sealed record PowerPlatformPolicy(
    Guid Id,
    string Name,
    string? TenantId,
    DateTimeOffset? LastModified,
    int RuleSetCount,
    IReadOnlyList<PowerPlatformPolicyRuleSet> RuleSets);

/// <summary>Fields accepted when creating a rule-based policy.</summary>
public sealed record PowerPlatformPolicyCreateOptions(
    string Name,
    IReadOnlyList<PowerPlatformPolicyRuleSet> RuleSets);

/// <summary>
/// Fields accepted by the PATCH "add or update rule sets" operation. Unlike
/// <see cref="PowerPlatformPolicyCreateOptions"/>, <see cref="RuleSets"/> is
/// additive/merging server-side (existing rule sets not present in this list
/// are left untouched) — there is no "full replace" (PUT) operation exposed
/// by this client because the PATCH semantics cover every supported CLI
/// workflow without risking accidental deletion of unrelated rule sets.
/// </summary>
public sealed record PowerPlatformPolicyPatchOptions(
    string? Name,
    IReadOnlyList<PowerPlatformPolicyRuleSet>? RuleSets);

/// <summary>
/// Records that a policy has been assigned to an environment group or a
/// single environment.
/// </summary>
public sealed record PowerPlatformPolicyAssignment(
    Guid PolicyId,
    Guid ResourceId,
    PowerPlatformPolicyAssignmentResourceType ResourceType,
    int RuleSetCount,
    string? TenantId);

/// <summary>
/// Client abstraction over the rule-based-policy CRUD and assignment
/// endpoints under <c>api.powerplatform.com/governance/ruleBasedPolicies</c>
/// (api-version 2024-10-01, confirmed from the official REST API reference).
/// </summary>
/// <remarks>
/// Two operations Microsoft's own <c>pac</c>-equivalent tooling would
/// normally offer are deliberately NOT part of this interface because the
/// confirmed API surface (as of this writing) does not expose them:
/// deleting a policy, and removing/unassigning a policy assignment. Only
/// <see cref="RemoveRuleSetAsync"/> (removing one rule set from a policy) is
/// supported. If Microsoft adds these operations, extend this interface
/// then — do not fake them with unsupported workarounds.
/// </remarks>
public interface IPowerPlatformPolicyRuleClient
{
    Task<IReadOnlyList<PowerPlatformPolicy>> ListAsync(
        Connection connection, Credential credential, CancellationToken ct);

    Task<PowerPlatformPolicy?> GetAsync(
        Connection connection, Credential credential, Guid policyId, CancellationToken ct);

    Task<PowerPlatformPolicy> CreateAsync(
        Connection connection, Credential credential, PowerPlatformPolicyCreateOptions options, CancellationToken ct);

    /// <summary>Adds or updates one or more rule sets on an existing policy (and/or renames it).</summary>
    Task<PowerPlatformPolicy> UpdateAsync(
        Connection connection, Credential credential, Guid policyId, PowerPlatformPolicyPatchOptions options, CancellationToken ct);

    /// <summary>Removes a single rule set (identified by its rule set id, e.g. "ConnectorManagement") from a policy.</summary>
    Task<PowerPlatformPolicy> RemoveRuleSetAsync(
        Connection connection, Credential credential, Guid policyId, string ruleSetId, CancellationToken ct);

    Task<PowerPlatformPolicyAssignment> AssignToEnvironmentGroupAsync(
        Connection connection, Credential credential, Guid policyId, Guid environmentGroupId,
        IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? overrides, CancellationToken ct);

    Task<PowerPlatformPolicyAssignment> AssignToEnvironmentAsync(
        Connection connection, Credential credential, Guid policyId, Guid environmentId,
        IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? overrides, CancellationToken ct);

    /// <summary>
    /// Lists policy assignments, optionally filtered to exactly one
    /// dimension (policy, environment group, or environment). Pass all
    /// nulls to list every assignment in the tenant. Specifying more than
    /// one filter is a caller error (throws <see cref="ArgumentException"/>)
    /// since the underlying API exposes one endpoint per filter dimension.
    /// </summary>
    Task<IReadOnlyList<PowerPlatformPolicyAssignment>> ListAssignmentsAsync(
        Connection connection, Credential credential,
        Guid? policyId, Guid? environmentGroupId, Guid? environmentId, CancellationToken ct);
}
