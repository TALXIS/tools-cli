using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline;

internal interface IProjectReferenceMetadataReader
{
    IReadOnlyCollection<string> ReadPluginAssemblyNames(string projectFilePath);
    IReadOnlyCollection<string> ReadScriptLibraryWebResourceNames(string solutionProjectFilePath, ILogger? logger = null);
    IReadOnlyCollection<string> ReadPcfControlNames(string solutionProjectFilePath);
}

internal sealed class ProjectReferenceMetadataReader : IProjectReferenceMetadataReader
{
    public IReadOnlyCollection<string> ReadPluginAssemblyNames(string projectFilePath)
        => ProjectReferenceReader.ReadPluginAssemblyNames(projectFilePath);

    public IReadOnlyCollection<string> ReadScriptLibraryWebResourceNames(string solutionProjectFilePath, ILogger? logger = null)
        => ProjectReferenceReader.ReadScriptLibraryWebResourceNames(solutionProjectFilePath, logger);

    public IReadOnlyCollection<string> ReadPcfControlNames(string solutionProjectFilePath)
        => ProjectReferenceReader.ReadPcfControlNames(solutionProjectFilePath);
}
