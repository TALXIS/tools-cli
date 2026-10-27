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
/// <remarks>
/// <see cref="ItemsPath"/>, <see cref="ValuePath"/> and <see cref="TitlePath"/>
/// say where the lookup's answer sits in its response: the item collection
/// and each item's value and title for a value list, or the schema for a
/// sub-schema.
/// </remarks>
public sealed record DynamicValuesRef(
    string OperationId,
    IReadOnlyDictionary<string, string?>? Parameters,
    string? ItemsPath = null,
    string? ValuePath = null,
    string? TitlePath = null);

/// <summary>
/// The spec for a single operation input. <see cref="Name"/> is the exact key
/// a flow definition uses in <c>inputs.parameters</c>: nested body fields are
/// slash-joined (<c>emailMessage/To</c>), as the designer writes them.
/// <see cref="ValueLabels"/> maps an allowed value to what the designer shows
/// for it (<c>3</c> → <c>Modified</c>) when a lookup supplied one.
/// </summary>
public sealed record OperationParameter(
    string Name,
    string? Type,
    bool Required,
    string? Description,
    IReadOnlyList<string>? AllowedValues,
    string? DefaultValue,
    DynamicValuesRef? DynamicValues,
    DynamicValuesRef? DynamicTree,
    DynamicValuesRef? DynamicSchema,
    IReadOnlyDictionary<string, string>? ValueLabels = null);

/// <summary>
/// Full specification of one connector operation: what a flow action needs in
/// <c>type</c>, <c>inputs.host</c> and <c>inputs.parameters</c>, and what it
/// returns. <see cref="Outputs"/> are named by the key expressions read them
/// with (<c>body/value</c>); fields of array items carry a <c>[]</c> segment
/// (<c>body/value[]/emailaddress1</c>), read inside a loop over the array.
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
    bool IsTrigger = false,
    IReadOnlyList<OperationParameter>? Outputs = null);

/// <summary>A connection, as shown to a user before it is used on their behalf.</summary>
public sealed record ConnectionDetail(
    string ConnectionId,
    string? OwnerName,
    string? OwnerPrincipalName,
    string? Status);

/// <summary>States of a <see cref="DynamicLookup"/>.</summary>
public static class DynamicLookupStatus
{
    /// <summary>Would be called once the user allows the connection to be used.</summary>
    public const string Planned = "planned";
    public const string Resolved = "resolved";
    /// <summary>Not callable, e.g. a missing input or a lookup that is not a GET.</summary>
    public const string Skipped = "skipped";
}

/// <summary>
/// One design-time lookup that resolves a dynamic parameter, as the flow
/// designer would call it through the connection.
/// </summary>
public sealed record DynamicLookup(
    string Parameter,
    string Kind,
    string OperationId,
    string Status,
    string? Reason,
    IReadOnlyList<string>? MissingValues = null);

/// <summary>
/// The outcome of resolving an operation's dynamic parameters. When
/// <see cref="ConnectionUsed"/> is false this is only the plan: nothing has
/// been called through the connection, and <see cref="Lookups"/> lists what
/// would be.
/// </summary>
public sealed record OperationResolution(
    OperationDetail Operation,
    ConnectionDetail Connection,
    bool ConnectionUsed,
    IReadOnlyList<DynamicLookup> Lookups);

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

    /// <summary>
    /// Resolves one dynamic parameter or output (a value list or a sub-schema)
    /// through an existing connection, the way the flow designer does.
    /// </summary>
    /// <param name="values">Values chosen for the operation's other parameters, which lookups take as inputs.</param>
    /// <param name="parameter">The parameter to resolve; pass this or <paramref name="output"/>.</param>
    /// <param name="useConnection">
    /// False returns the plan without calling the connection. Only pass true
    /// once the connection's owner has agreed to it being used.
    /// </param>
    /// <param name="output">The output to resolve (its fields); pass this or <paramref name="parameter"/>.</param>
    Task<OperationResolution> ResolveOperationAsync(
        string? profileName,
        string connector,
        string operation,
        string connectionReference,
        string connectionId,
        IReadOnlyDictionary<string, string> values,
        string? parameter,
        string? output,
        bool useConnection,
        CancellationToken ct);

    /// <summary>A connection's display name, owner and status.</summary>
    Task<ConnectionDetail> GetConnectionAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct);
}
