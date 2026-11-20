namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record ComponentAddOptions(
    string SolutionUniqueName,
    Guid ComponentId,
    int ComponentType,
    bool AddRequiredComponents,
    bool DoNotIncludeSubcomponents);

public sealed record ComponentRemoveOptions(
    string SolutionUniqueName,
    Guid ComponentId,
    int ComponentType);

public interface ISolutionComponentMutationService
{
    Task AddAsync(string? profileName, ComponentAddOptions options, CancellationToken ct);
    Task RemoveAsync(string? profileName, ComponentRemoveOptions options, CancellationToken ct);

    /// <summary>
    /// Permanently deletes a component's underlying object from the environment.
    ///  Only record-backed types in <see cref="SolutionComponentEntityMap"/> are supported.
    /// </summary>
    Task DeleteFromEnvironmentAsync(string? profileName, int componentType, Guid objectId, CancellationToken ct);
}
