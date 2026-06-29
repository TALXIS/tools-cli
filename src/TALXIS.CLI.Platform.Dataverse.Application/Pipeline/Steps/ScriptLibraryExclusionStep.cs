using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

internal sealed class ScriptLibraryExclusionStep : ISolutionPullStep
{
    private readonly ILogger _logger;
    private readonly IProjectReferenceMetadataReader _projectReferenceReader;

    public ScriptLibraryExclusionStep(IProjectReferenceMetadataReader projectReferenceReader, ILogger logger)
    {
        _projectReferenceReader = projectReferenceReader;
        _logger = logger;
    }

    public void Execute(SolutionPullContext context)
    {
        var webResourceNames = context.ReferencedScriptLibraryWebResources
            ?? ReadScriptLibraryWebResourceNames(context.ProjectFilePath);
        if (webResourceNames.Count == 0)
            return;

        var webResourcesDir = Path.Combine(
            context.StagingDirectory,
            SolutionPullPipelineConstants.WebResourcesDirectoryName);
        if (!Directory.Exists(webResourcesDir))
            return;

        foreach (var dataXml in Directory.GetFiles(webResourcesDir, "*" + SolutionPullPipelineConstants.DataXmlSuffix))
        {
            var resourceName = Path.GetFileName(dataXml)[..^SolutionPullPipelineConstants.DataXmlSuffix.Length];
            if (!webResourceNames.Contains(resourceName))
                continue;

            var contentPath = Path.Combine(webResourcesDir, resourceName);
            if (!File.Exists(contentPath))
                continue;

            File.Delete(contentPath);
            context.ExcludedWebResources.Add(resourceName);
        }
    }

    private IReadOnlyCollection<string> ReadScriptLibraryWebResourceNames(string? projectFilePath)
        => string.IsNullOrWhiteSpace(projectFilePath)
            ? []
            : _projectReferenceReader.ReadScriptLibraryWebResourceNames(projectFilePath, _logger);
}
