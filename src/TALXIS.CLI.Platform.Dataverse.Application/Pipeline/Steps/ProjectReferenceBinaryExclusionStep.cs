namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

internal sealed class ProjectReferenceBinaryExclusionStep : ISolutionPullStep
{
    private readonly IProjectReferenceMetadataReader _projectReferenceReader;

    public ProjectReferenceBinaryExclusionStep(IProjectReferenceMetadataReader projectReferenceReader)
    {
        _projectReferenceReader = projectReferenceReader;
    }

    public void Execute(SolutionPullContext context)
    {
        var referencedAssemblyNames = context.ReferencedPluginAssemblyNames
            ?? ReadPluginAssemblyNames(context.ProjectFilePath);
        if (referencedAssemblyNames.Count == 0)
            return;

        var pluginsDir = Path.Combine(
            context.StagingDirectory,
            SolutionPullPipelineConstants.PluginAssembliesDirectoryName);
        if (!Directory.Exists(pluginsDir))
            return;

        foreach (var dataXml in Directory.GetFiles(
                     pluginsDir,
                     "*" + SolutionPullPipelineConstants.DataXmlSuffix,
                     SearchOption.AllDirectories))
        {
            var assemblySimpleName = SolutionPullPipelineXml.ReadAssemblySimpleName(dataXml);
            if (assemblySimpleName is null || !MatchesReference(assemblySimpleName, referencedAssemblyNames))
                continue;

            var binaryName = Path.GetFileName(dataXml)[..^SolutionPullPipelineConstants.DataXmlSuffix.Length];
            var binaryPath = Path.Combine(Path.GetDirectoryName(dataXml)!, binaryName);
            if (!File.Exists(binaryPath))
                continue;

            File.Delete(binaryPath);
            context.ExcludedBinaries.Add(binaryName);
        }
    }

    private IReadOnlyCollection<string> ReadPluginAssemblyNames(string? projectFilePath)
        => string.IsNullOrWhiteSpace(projectFilePath)
            ? []
            : _projectReferenceReader.ReadPluginAssemblyNames(projectFilePath);

    private static bool MatchesReference(string assemblySimpleName, IReadOnlyCollection<string> referencedNames)
    {
        foreach (var name in referencedNames)
        {
            if (assemblySimpleName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;

            if (assemblySimpleName.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
