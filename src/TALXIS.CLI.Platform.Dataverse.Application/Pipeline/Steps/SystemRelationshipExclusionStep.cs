using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

internal sealed class SystemRelationshipExclusionStep : ISolutionPullStep
{
    private static readonly Regex StandardSystemRelationshipPattern = new(
        "^(business_unit_.+|lk_.+_(createdby|modifiedby)|owner_.+|team_.+|user_.+)$",
        RegexOptions.IgnoreCase);

    public void Execute(SolutionPullContext context)
    {
        var relationshipsXml = Path.Combine(
            context.StagingDirectory,
            SolutionPullPipelineConstants.OtherDirectoryName,
            "Relationships.xml");
        if (!File.Exists(relationshipsXml))
            return;

        var fileContents = File.ReadAllText(relationshipsXml);
        if (string.IsNullOrWhiteSpace(fileContents))
            return;

        XDocument relationshipsDocument;
        try
        {
            relationshipsDocument = XDocument.Parse(fileContents);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        var removed = relationshipsDocument
            .Descendants()
            .Where(element => element.Name.LocalName == "EntityRelationship")
            .Select(element => new
            {
                Element = element,
                Name = element.Attribute("Name")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && StandardSystemRelationshipPattern.IsMatch(item.Name))
            .ToList();

        if (removed.Count == 0)
            return;

        foreach (var relationship in removed)
        {
            relationship.Element.Remove();
            context.ExcludedRelationships.Add(relationship.Name!);
        }

        relationshipsDocument.Save(relationshipsXml);
    }
}
