namespace TALXIS.CLI.Core.Contracts.Dataverse;

public interface ISolutionLayerMutationService
{
    /// <summary>
    /// Removes the unmanaged active customization layer from a component,
    /// reverting it to the behavior defined by the highest managed layer.
    /// </summary>
    Task RemoveCustomizationAsync(
        string? profileName,
        Guid componentId,
        int componentType,
        CancellationToken ct);
}
