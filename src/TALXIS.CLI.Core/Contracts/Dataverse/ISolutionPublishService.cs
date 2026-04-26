namespace TALXIS.CLI.Core.Contracts.Dataverse;

public interface ISolutionPublishService
{
    /// <summary>
    /// Publishes all customizations, or a selective set of entities.
    /// </summary>
    /// <param name="entityLogicalNames">
    /// If non-empty, only these entities are published. If null/empty, publishes all.
    /// </param>
    Task PublishAsync(
        string? profileName,
        IReadOnlyList<string>? entityLogicalNames,
        CancellationToken ct);
}
