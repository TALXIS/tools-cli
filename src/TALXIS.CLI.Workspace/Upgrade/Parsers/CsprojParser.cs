using System.Xml.Linq;
using TALXIS.CLI.Workspace.Upgrade.Models;

namespace TALXIS.CLI.Workspace.Upgrade.Parsers;

public class CsprojParser 
{
    private static readonly XNamespace MsbuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

    public CsprojProject Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Project file not found: {filePath}");

        var xml = XDocument.Load(filePath);
        var root = xml.Root ?? throw new InvalidOperationException("Invalid project file: no root element");

        var project = new CsprojProject
        {
            FilePath = filePath,
            OriginalXml = xml,
            IsSdkStyle = root.Attribute("Sdk") != null,
            Sdk = root.Attribute("Sdk")?.Value
        };

        ParseProperties(root, project);
        ParsePackageReferences(root, project);
        ParseProjectReferences(root, project);
        ParseAssemblyReferences(root, project);
        ParseCustomElements(root, project);

        return project;
    }

    private void ParseProperties(XElement root, CsprojProject project)
    {
        // Parse all PropertyGroup elements
        var propertyGroups = root.Elements(MsbuildNamespace + "PropertyGroup")
            .Concat(root.Elements("PropertyGroup"));

        foreach (var group in propertyGroups)
        {
            foreach (var property in group.Elements())
            {
                var name = property.Name.LocalName;
                var value = property.Value;

                // Store all properties
                if (!project.Properties.ContainsKey(name))
                {
                    project.Properties[name] = value;
                }
            }
        }
    }

    private void ParsePackageReferences(XElement root, CsprojProject project)
    {
        var packageRefs = root.Descendants(MsbuildNamespace + "PackageReference")
            .Concat(root.Descendants("PackageReference"));

        foreach (var packageRef in packageRefs)
        {
            var include = packageRef.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
                continue;

            var package = new PackageReference
            {
                Name = include,
                Version = packageRef.Attribute("Version")?.Value
                    ?? packageRef.Element(MsbuildNamespace + "Version")?.Value
                    ?? packageRef.Element("Version")?.Value
            };

            // Parse attributes as metadata (except Include and Version)
            foreach (var attribute in packageRef.Attributes())
            {
                if (attribute.Name.LocalName != "Include" && attribute.Name.LocalName != "Version")
                {
                    package.Metadata[attribute.Name.LocalName] = attribute.Value;
                }
            }

            // Parse child elements as metadata
            foreach (var element in packageRef.Elements())
            {
                if (element.Name.LocalName != "Version")
                {
                    package.Metadata[element.Name.LocalName] = element.Value;
                }
            }

            project.PackageReferences.Add(package);
        }
    }

    private void ParseProjectReferences(XElement root, CsprojProject project)
    {
        var projectRefs = root.Descendants(MsbuildNamespace + "ProjectReference")
            .Concat(root.Descendants("ProjectReference"));

        foreach (var projectRef in projectRefs)
        {
            var include = projectRef.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
                continue;

            var reference = new ProjectReference
            {
                Include = include
            };

            // Parse metadata
            foreach (var element in projectRef.Elements())
            {
                reference.Metadata[element.Name.LocalName] = element.Value;
            }

            project.ProjectReferences.Add(reference);
        }
    }

    private void ParseAssemblyReferences(XElement root, CsprojProject project)
    {
        var assemblyRefs = root.Descendants(MsbuildNamespace + "Reference")
            .Concat(root.Descendants("Reference"));

        foreach (var assemblyRef in assemblyRefs)
        {
            var include = assemblyRef.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
                continue;

            var reference = new AssemblyReference
            {
                Include = include,
                HintPath = assemblyRef.Element(MsbuildNamespace + "HintPath")?.Value
                    ?? assemblyRef.Element("HintPath")?.Value
            };

            // Parse metadata
            foreach (var element in assemblyRef.Elements())
            {
                if (element.Name.LocalName != "HintPath")
                {
                    reference.Metadata[element.Name.LocalName] = element.Value;
                }
            }

            project.AssemblyReferences.Add(reference);
        }
    }

    private void ParseCustomElements(XElement root, CsprojProject project)
    {
        // Collect custom Target elements
        var targets = root.Descendants(MsbuildNamespace + "Target")
            .Concat(root.Descendants("Target"));
        project.CustomTargets.AddRange(targets);

        // Collect custom Import elements
        var imports = root.Elements(MsbuildNamespace + "Import")
            .Concat(root.Elements("Import"));
        project.CustomImports.AddRange(imports);

        // Store ItemGroups only if they contain elements OTHER than PackageReference, ProjectReference, or Reference
        // Those are already parsed separately and will be added by the generator
        var itemGroups = root.Elements(MsbuildNamespace + "ItemGroup")
            .Concat(root.Elements("ItemGroup"));

        foreach (var itemGroup in itemGroups)
        {
            var hasStandardReferences = itemGroup.Elements()
                .Any(e => e.Name.LocalName == "PackageReference" ||
                         e.Name.LocalName == "ProjectReference" ||
                         e.Name.LocalName == "Reference");

            var hasOtherElements = itemGroup.Elements()
                .Any(e => e.Name.LocalName != "PackageReference" &&
                         e.Name.LocalName != "ProjectReference" &&
                         e.Name.LocalName != "Reference");


            if (hasOtherElements && !hasStandardReferences)
            {
                project.CustomItemGroups.Add(itemGroup);
            }
        }

        var propertyGroups = root.Elements(MsbuildNamespace + "PropertyGroup")
            .Concat(root.Elements("PropertyGroup"));
        project.CustomPropertyGroups.AddRange(propertyGroups);
    }
}
