namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

internal sealed class PcfControlExclusionStep : ISolutionPullStep
{
    private readonly IProjectReferenceMetadataReader _projectReferenceReader;

    public PcfControlExclusionStep(IProjectReferenceMetadataReader projectReferenceReader)
    {
        _projectReferenceReader = projectReferenceReader;
    }

    public void Execute(SolutionPullContext context)
    {
        var pcfControlNames = context.ReferencedPcfControlNames
            ?? ReadPcfControlNames(context.ProjectFilePath);
        if (pcfControlNames.Count == 0)
            return;

        var controlsDir = Path.Combine(
            context.StagingDirectory,
            SolutionPullPipelineConstants.ControlsDirectoryName);
        if (!Directory.Exists(controlsDir))
            return;

        var publisherPrefix = SolutionPullPipelineXml.ReadPublisherPrefix(context.StagingDirectory);
        if (string.IsNullOrWhiteSpace(publisherPrefix))
            return;

        foreach (var controlDir in Directory.GetDirectories(controlsDir))
        {
            var directoryName = Path.GetFileName(controlDir);
            if (!MatchesPcfControl(directoryName, pcfControlNames, publisherPrefix))
                continue;

            Directory.Delete(controlDir, recursive: true);
            context.ExcludedPcfControls.Add(directoryName);
        }
    }

    private IReadOnlyCollection<string> ReadPcfControlNames(string? projectFilePath)
        => string.IsNullOrWhiteSpace(projectFilePath)
            ? []
            : _projectReferenceReader.ReadPcfControlNames(projectFilePath);

    private static bool MatchesPcfControl(
        string folderName,
        IReadOnlyCollection<string> pcfControlNames,
        string publisherPrefix)
    {
        foreach (var qualifiedName in pcfControlNames)
        {
            var namePart = qualifiedName.Replace('.', '_');
            var expected = $"{publisherPrefix}_{namePart}";
            if (string.Equals(folderName, expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
