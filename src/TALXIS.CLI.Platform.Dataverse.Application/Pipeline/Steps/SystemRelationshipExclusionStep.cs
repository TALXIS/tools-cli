using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

internal sealed class SystemRelationshipExclusionStep : ISolutionPullStep
{
    private const string RelationshipComponentType = "10";

    private static readonly Regex StandardSystemRelationshipPattern = new(
        "^(business_unit_.+|lk_.+_(createdby|modifiedby)|owner_.+|team_.+|user_.+)$",
        RegexOptions.IgnoreCase);

    public void Execute(SolutionPullContext context)
    {
        var otherDirectory = Path.Combine(context.StagingDirectory, SolutionPullPipelineConstants.OtherDirectoryName);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        RemoveSystemRelationships(Path.Combine(otherDirectory, "Relationships.xml"), context, excluded);

        // SolutionPackager keeps the full definitions in one file per entity next to the index.
        var relationshipsDirectory = Path.Combine(otherDirectory, "Relationships");
        if (Directory.Exists(relationshipsDirectory))
        {
            foreach (var file in Directory.GetFiles(relationshipsDirectory, "*.xml"))
            {
                var document = RemoveSystemRelationships(file, context, excluded);
                if (document is not null && !document.Descendants().Any(IsEntityRelationship))
                    File.Delete(file);
            }

            if (!Directory.EnumerateFileSystemEntries(relationshipsDirectory).Any())
                Directory.Delete(relationshipsDirectory);
        }

        RemoveMissingDependencies(Path.Combine(otherDirectory, "Solution.xml"), excluded);
    }

    // Returns the document only when something was removed from it.
    private static XDocument? RemoveSystemRelationships(string path, SolutionPullContext context, HashSet<string> excluded)
    {
        var document = TryLoad(path);
        if (document is null)
            return null;

        var removed = document
            .Descendants()
            .Where(IsEntityRelationship)
            .Select(element => new
            {
                Element = element,
                Name = element.Attribute("Name")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && StandardSystemRelationshipPattern.IsMatch(item.Name))
            .ToList();

        if (removed.Count == 0)
            return null;

        foreach (var relationship in removed)
        {
            relationship.Element.Remove();
            if (excluded.Add(relationship.Name!))
                context.ExcludedRelationships.Add(relationship.Name!);
        }

        document.Save(path);
        return document;
    }

    private static void RemoveMissingDependencies(string solutionXml, HashSet<string> excluded)
    {
        if (excluded.Count == 0)
            return;

        var document = TryLoad(solutionXml);
        if (document is null)
            return;

        var removed = document
            .Descendants()
            .Where(element => element.Name.LocalName == "MissingDependency")
            .Where(dependency => dependency.Elements().Any(side =>
                side.Attribute("type")?.Value == RelationshipComponentType
                && side.Attribute("schemaName")?.Value is { } schemaName
                && excluded.Contains(schemaName)))
            .ToList();

        if (removed.Count == 0)
            return;

        foreach (var dependency in removed)
            dependency.Remove();

        document.Save(solutionXml);
    }

    private static bool IsEntityRelationship(XElement element)
        => element.Name.LocalName == "EntityRelationship";

    private static XDocument? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        var fileContents = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(fileContents))
            return null;

        try
        {
            return XDocument.Parse(fileContents);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
