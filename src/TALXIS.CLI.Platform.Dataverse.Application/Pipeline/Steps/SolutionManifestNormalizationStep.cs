using System.Xml.Linq;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

internal sealed class SolutionManifestNormalizationStep : ISolutionPullStep
{
    public void Execute(SolutionPullContext context)
    {
        var stagingSolutionXml = Path.Combine(
            context.StagingDirectory,
            SolutionPullPipelineConstants.OtherDirectoryName,
            "Solution.xml");
        if (!File.Exists(stagingSolutionXml))
            return;

        XDocument stagingDocument;
        try
        {
            stagingDocument = XDocument.Load(stagingSolutionXml);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        var stagingManifest = SolutionPullPipelineXml.FindSolutionManifest(stagingDocument);
        if (stagingManifest is null)
            return;

        var namespaceName = stagingManifest.Name.Namespace;
        var stagingVersion = stagingManifest.Element(namespaceName + "Version");
        var localVersion = SolutionPullPipelineXml.ReadSolutionManifestElementValue(
            context.DestinationDirectory,
            "Version");
        if (stagingVersion is not null && !string.IsNullOrWhiteSpace(localVersion))
            stagingVersion.Value = localVersion;

        var managedElement = stagingManifest.Element(namespaceName + "Managed");
        if (managedElement is null)
        {
            managedElement = new XElement(namespaceName + "Managed");
            stagingManifest.Add(managedElement);
        }

        managedElement.Value = "2";
        stagingDocument.Save(stagingSolutionXml);
    }
}
