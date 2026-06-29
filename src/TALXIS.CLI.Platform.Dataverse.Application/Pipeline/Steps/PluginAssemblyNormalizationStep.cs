using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

internal sealed class PluginAssemblyNormalizationStep : ISolutionPullStep
{
    private readonly ILogger _logger;

    public PluginAssemblyNormalizationStep(ILogger logger)
    {
        _logger = logger;
    }

    public void Execute(SolutionPullContext context)
    {
        var stagingPluginsDir = Path.Combine(
            context.StagingDirectory,
            SolutionPullPipelineConstants.PluginAssembliesDirectoryName);
        if (!Directory.Exists(stagingPluginsDir))
            return;

        var localConventionMap = BuildLocalPluginConventionMap(context.DestinationDirectory);
        foreach (var nestedDir in Directory.GetDirectories(stagingPluginsDir).ToList())
        {
            var dataXmlFile = Directory.GetFiles(nestedDir, "*" + SolutionPullPipelineConstants.DataXmlSuffix).FirstOrDefault();
            if (dataXmlFile is null)
                continue;

            var simpleName = SolutionPullPipelineXml.ReadAssemblySimpleName(dataXmlFile);
            if (simpleName is null)
                continue;

            string targetDir;
            string? desiredFileName;
            var isNew = false;
            if (localConventionMap.TryGetValue(simpleName, out var localInfo))
            {
                targetDir = localInfo.RelativeDir == "."
                    ? stagingPluginsDir
                    : Path.Combine(stagingPluginsDir, localInfo.RelativeDir);
                desiredFileName = localInfo.LocalFileName;
            }
            else
            {
                targetDir = stagingPluginsDir;
                desiredFileName = null;
                isNew = true;
                _logger.LogInformation(
                    "Assembly '{Name}' is new (no local convention found) — using flat PluginAssemblies/ layout as default.",
                    simpleName);
            }

            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(nestedDir))
            {
                var fileName = Path.GetFileName(file);
                var destination = Path.Combine(targetDir, fileName);
                if (!string.Equals(file, destination, StringComparison.Ordinal))
                {
                    if (File.Exists(destination))
                    {
                        _logger.LogWarning(
                            "Staging file '{Destination}' already exists — overwriting. This may indicate two assemblies with the same name in different folders.",
                            destination);
                        File.Delete(destination);
                    }

                    File.Move(file, destination);
                }

                if (!fileName.EndsWith(SolutionPullPipelineConstants.DataXmlSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetFileName = isNew
                    ? $"/{SolutionPullPipelineConstants.PluginAssembliesDirectoryName}/{fileName[..^SolutionPullPipelineConstants.DataXmlSuffix.Length]}"
                    : desiredFileName;

                if (targetFileName is not null)
                    SolutionPullPipelineXml.RewriteFileName(destination, targetFileName);
            }

            if (!Directory.EnumerateFileSystemEntries(nestedDir).Any())
                Directory.Delete(nestedDir);

            context.NormalizedAssemblies.Add(simpleName);
        }
    }

    private static Dictionary<string, (string RelativeDir, string? LocalFileName)> BuildLocalPluginConventionMap(string destinationRoot)
    {
        var map = new Dictionary<string, (string RelativeDir, string? LocalFileName)>(StringComparer.OrdinalIgnoreCase);
        var localPluginsDir = Path.Combine(
            destinationRoot,
            SolutionPullPipelineConstants.PluginAssembliesDirectoryName);
        if (!Directory.Exists(localPluginsDir))
            return map;

        foreach (var dataXml in Directory.GetFiles(
                     localPluginsDir,
                     "*" + SolutionPullPipelineConstants.DataXmlSuffix,
                     SearchOption.AllDirectories))
        {
            var simpleName = SolutionPullPipelineXml.ReadAssemblySimpleName(dataXml);
            if (simpleName is null)
                continue;

            var dataXmlDir = Path.GetDirectoryName(dataXml)!;
            var relativeDir = Path.GetRelativePath(localPluginsDir, dataXmlDir);
            var localFileName = SolutionPullPipelineXml.ReadFileNameElement(dataXml);
            map[simpleName] = (relativeDir, localFileName);
        }

        return map;
    }
}
