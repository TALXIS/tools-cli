namespace TALXIS.CLI.Core.Contracts.Dataverse;

public enum PluginKind { Plugin = 0, WorkflowActivity = 1 }

public enum PluginStage
{
    PreValidation = 10,
    PreOperation = 20,
    PostOperation = 40,
    PostOperationDeprecated = 50,
}

public enum PluginExecutionMode { Synchronous = 0, Asynchronous = 1 }

public enum PluginIsolationMode { None = 1, Sandbox = 2, External = 3 }

public enum PluginAssemblySourceType { Database = 0, Disk = 1, Gac = 2, Package = 4 }

public sealed record PluginAssemblyRecord(
    Guid Id,
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken,
    PluginIsolationMode IsolationMode,
    PluginAssemblySourceType SourceType,
    string? Description,
    DateTime? ModifiedOn);

public sealed record PluginTypeRecord(
    Guid Id,
    string TypeName,
    string? FriendlyName,
    PluginKind Kind,
    string? WorkflowActivityGroupName,
    string? Description,
    Guid AssemblyId,
    string AssemblyName,
    string? AssemblyVersion);

public sealed record PluginStepRecord(
    Guid Id,
    string Name,
    string? Description,
    string Message,
    string? PrimaryEntity,
    PluginStage Stage,
    PluginExecutionMode Mode,
    int Rank,
    bool Enabled,
    string? FilteringAttributes,
    string? Configuration,
    Guid PluginTypeId,
    string PluginTypeName,
    Guid AssemblyId,
    string AssemblyName,
    string? AssemblyVersion);

public sealed record PluginStepImageRecord(
    Guid Id,
    Guid StepId,
    string ImageType,
    string? EntityAlias,
    string? Attributes);

public interface IPluginInventoryService
{
    Task<IReadOnlyList<PluginAssemblyRecord>> ListAssembliesAsync(
        string? profileName,
        string? nameContains,
        CancellationToken ct);

    Task<IReadOnlyList<PluginTypeRecord>> ListTypesAsync(
        string? profileName,
        string? assemblyContains,
        PluginKind? kind,
        CancellationToken ct);

    Task<IReadOnlyList<PluginStepRecord>> ListStepsAsync(
        string? profileName,
        string? assemblyContains,
        CancellationToken ct);

    Task<IReadOnlyList<PluginStepImageRecord>> ListStepImagesAsync(
        string? profileName,
        string? assemblyContains,
        CancellationToken ct);

    Task SetStepStateAsync(
        string? profileName,
        Guid stepId,
        bool enabled,
        CancellationToken ct);

    Task<int> SetStepsStateAsync(
        string? profileName,
        IReadOnlyCollection<Guid> stepIds,
        bool enabled,
        CancellationToken ct);
}
