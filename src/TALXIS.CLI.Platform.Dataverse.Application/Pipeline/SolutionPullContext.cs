namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline;

internal sealed class SolutionPullContext
{
    public required string StagingDirectory { get; init; }
    public required string DestinationDirectory { get; init; }
    public required IReadOnlyList<string> ReferencedProjectDirectories { get; init; }
    public string? ProjectFilePath { get; init; }
    public IReadOnlyCollection<string>? ReferencedPluginAssemblyNames { get; init; }
    public IReadOnlyCollection<string>? ReferencedScriptLibraryWebResources { get; init; }
    public IReadOnlyCollection<string>? ReferencedPcfControlNames { get; init; }
    public List<string> NormalizedAssemblies { get; } = [];
    public List<string> ExcludedRelationships { get; } = [];
    public List<string> ExcludedBinaries { get; } = [];
    public List<string> ExcludedWebResources { get; } = [];
    public List<string> ExcludedPcfControls { get; } = [];
}
