using System.Text.Json;

namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>A connector as it appears in the environment's connector catalog.</summary>
public sealed record ConnectorSummary(
    string Name,
    string? DisplayName,
    string? Description,
    string? Tier);

/// <summary>Kinds of connector operation, as a flow definition has to treat them.</summary>
public static class ConnectorOperationKind
{
    public const string Action = "action";
    public const string Trigger = "trigger";

    /// <summary>A trigger that registers a callback instead of being polled.</summary>
    public const string WebhookTrigger = "webhook-trigger";
}

/// <summary>
/// One entry of a connector's operation index. Deliberately carries no
/// parameter specs — that is what
/// <see cref="IConnectorMetadataService.GetOperationAsync"/> is for, and
/// keeping the index thin is what makes browsing a large connector cheap.
/// </summary>
public sealed record ConnectorOperationSummary(
    string OperationId,
    string Kind,
    string? Summary,
    string Method,
    string Path,
    bool IsDeprecated);

/// <summary>A connector plus its operation index.</summary>
public sealed record ConnectorDetail(
    string Name,
    string? DisplayName,
    string ApiId,
    int OperationCount,
    int? MatchedOperations,
    IReadOnlyList<ConnectorOperationSummary> Operations);

/// <summary>A hit from the cross-connector operation search.</summary>
public sealed record OperationSearchHit(
    string Name,
    string? DisplayName,
    string? Description,
    string? ConnectorName,
    string? ConnectorDisplayName);

/// <summary>
/// A pointer to the connector operation that resolves a parameter at design
/// time (the designer's dropdowns, tree pickers and dynamic sub-schemas).
/// Arguments that reference another parameter of the same operation are
/// rendered as <c>$ref:name</c>.
/// </summary>
public sealed record DynamicValuesRef(
    string OperationId,
    IReadOnlyDictionary<string, string?>? Parameters);

/// <summary>
/// The spec for a single operation input. <see cref="Name"/> is the exact key
/// a flow definition uses in <c>inputs.parameters</c>: nested body fields are
/// slash-joined (<c>emailMessage/To</c>), as the designer writes them.
/// </summary>
public sealed record OperationParameter(
    string Name,
    string? Type,
    bool Required,
    string? Description,
    IReadOnlyList<string>? AllowedValues,
    string? DefaultValue,
    string? Visibility,
    DynamicValuesRef? DynamicValues,
    DynamicValuesRef? DynamicTree,
    DynamicValuesRef? DynamicSchema);

/// <summary>
/// Full specification of one connector operation: what a flow action needs in
/// <c>type</c>, <c>inputs.host</c> and <c>inputs.parameters</c>.
/// </summary>
public sealed record OperationDetail(
    string Connector,
    string ApiId,
    string OperationId,
    string? Summary,
    string? Description,
    string ActionType,
    bool IsDeprecated,
    IReadOnlyList<OperationParameter> Parameters,
    JsonElement? ResponseSchema);

/// <summary>
/// Read-only Power Automate connector metadata from the environment bound to
/// the profile.
/// </summary>
/// <remarks>
/// Flows in a TALXIS workspace are solution components authored locally. This
/// service never creates anything in the environment; it exists so that the
/// operation IDs and parameter names written into a local definition come from
/// the connectors that actually exist rather than from recollection.
/// </remarks>
public interface IConnectorMetadataService
{
    /// <summary>Lists connectors available in the environment, optionally filtered by name.</summary>
    Task<IReadOnlyList<ConnectorSummary>> ListConnectorsAsync(
        string? profileName, string? search, CancellationToken ct);

    /// <summary>Gets a connector's operation index, optionally filtered by keyword.</summary>
    Task<ConnectorDetail> GetConnectorAsync(
        string? profileName, string connector, string? search, CancellationToken ct);

    /// <summary>
    /// Searches operations across connectors. <paramref name="kind"/> is
    /// <c>all</c>, <c>actions</c> or <c>triggers</c>.
    /// </summary>
    Task<IReadOnlyList<OperationSearchHit>> SearchOperationsAsync(
        string? profileName, string search, string? connector, string kind, int top, CancellationToken ct);

    /// <summary>Gets the parameter specification and action type for one operation.</summary>
    Task<OperationDetail> GetOperationAsync(
        string? profileName, string connector, string operation, CancellationToken ct);
}
