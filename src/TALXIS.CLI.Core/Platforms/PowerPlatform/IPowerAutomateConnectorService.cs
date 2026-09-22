using System.Text.Json;

namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>A connector as it appears in the environment's connector catalog.</summary>
public sealed record ConnectorSummary(
    string Name,
    string? DisplayName,
    string? Description,
    string? Tier);

/// <summary>
/// One entry of a connector's operation index. Deliberately carries no
/// parameter specs — that is what
/// <see cref="IPowerAutomateConnectorService.GetOperationDetailsAsync"/> is for,
/// and keeping the index thin is what makes browsing a large connector cheap.
/// </summary>
public sealed record ConnectorOperationSummary(
    string OperationId,
    string? Summary,
    string Method,
    string Path,
    int ParamCount,
    bool IsDeprecated,
    bool IsTrigger);

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
    string? Usage,
    string? ConnectorName,
    string? ConnectorDisplayName);

/// <summary>
/// A pointer to the connector operation that supplies a parameter's allowed
/// values at design time (the designer's dropdowns and tree pickers).
/// </summary>
public sealed record DynamicValuesRef(
    string? OperationId,
    IReadOnlyDictionary<string, string?>? Parameters);

/// <summary>
/// The authoritative spec for a single operation input: the exact name, type,
/// requiredness and allowed values a flow definition must use.
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

/// <summary>Full specification of one connector operation.</summary>
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
/// A connection available to flows in the environment. For solution-aware
/// flows the binding that matters is
/// <see cref="ConnectionReferenceLogicalName"/>, not the raw connection name.
/// </summary>
public sealed record FlowConnectionSummary(
    string ConnectionName,
    string ConnectorId,
    string? DisplayName,
    string? Status,
    string? ConnectionReferenceLogicalName,
    string Source);

/// <summary>Severity of a single flow-definition validation finding.</summary>
public static class FlowValidationSeverity
{
    public const string Error = "error";
    public const string Warning = "warning";
}

/// <summary>
/// One problem found in a flow definition. <paramref name="Path"/> is a JSON
/// pointer to the offending node so a caller can fix it in place.
/// </summary>
public sealed record FlowValidationFinding(
    string Severity,
    string Path,
    string Code,
    string Message);

/// <summary>Outcome of validating a locally authored flow definition.</summary>
public sealed record FlowValidationReport(
    bool Valid,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<FlowValidationFinding> Findings);

/// <summary>
/// Reads Power Automate connector metadata from the live environment bound to
/// the active profile, and checks locally authored flow definitions against it.
/// </summary>
/// <remarks>
/// Flows in a TALXIS workspace are solution components authored locally; this
/// service never creates or edits a flow in the environment. It exists so the
/// definition being written locally can be type-checked against the connectors
/// and operations that actually exist, rather than against recollection.
/// </remarks>
public interface IPowerAutomateConnectorService
{
    /// <summary>Lists connectors available in the environment.</summary>
    Task<IReadOnlyList<ConnectorSummary>> ListConnectorsAsync(
        string? profileName, string? query, int? top, CancellationToken ct);

    /// <summary>Gets a connector's operation index, optionally filtered by keyword.</summary>
    Task<ConnectorDetail> GetConnectorAsync(
        string? profileName, string connector, string? query, CancellationToken ct);

    /// <summary>Searches operations across connectors.</summary>
    Task<IReadOnlyList<OperationSearchHit>> SearchOperationsAsync(
        string? profileName, string? query, string? connector, string? kind, int? top, CancellationToken ct);

    /// <summary>Gets the full parameter specification and action type for one operation.</summary>
    Task<OperationDetail> GetOperationDetailsAsync(
        string? profileName, string connector, string operation, CancellationToken ct);

    /// <summary>Lists connections and connection references in the environment.</summary>
    Task<IReadOnlyList<FlowConnectionSummary>> ListConnectionsAsync(
        string? profileName, string? connector, CancellationToken ct);

    /// <summary>
    /// Validates a locally authored flow definition, cross-checking every
    /// connector action against live metadata unless <paramref name="offline"/>
    /// is set.
    /// </summary>
    Task<FlowValidationReport> ValidateDefinitionAsync(
        string? profileName,
        JsonElement document,
        bool connectionCheck,
        bool offline,
        CancellationToken ct);
}
