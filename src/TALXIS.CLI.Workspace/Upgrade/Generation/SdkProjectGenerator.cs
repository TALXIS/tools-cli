using System.Xml.Linq;
using TALXIS.CLI.Workspace.Upgrade.Models;

namespace TALXIS.CLI.Workspace.Upgrade.Generation;

public class SdkProjectGenerator
{
    public XDocument Generate(CsprojProject project)
    {
        var root = new XElement("Project",
            new XAttribute("Sdk", project.Sdk ?? "Microsoft.NET.Sdk")
        );

        AddPropertyGroup(root, project);

        if (project.PackageReferences.Any())
        {
            AddPackageReferences(root, project);
        }

        if (project.ProjectReferences.Any())
        {
            AddProjectReferences(root, project);
        }

        if (project.AssemblyReferences.Any())
        {
            AddAssemblyReferences(root, project);
        }

        foreach (var itemGroup in project.CustomItemGroups)
        {
            root.Add(itemGroup);
        }

        foreach (var import in project.CustomImports)
        {
            root.Add(import);
        }

        foreach (var target in project.CustomTargets)
        {
            root.Add(target);
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            root
        );
    }


    public void SaveToFile(XDocument document, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        document.Save(filePath, SaveOptions.None);
    }

    private void AddPropertyGroup(XElement root, CsprojProject project)
    {
        if (!project.Properties.Any())
            return;

        var propertyGroup = new XElement("PropertyGroup");

        var orderedProperties = new[]
        {
            "TargetFramework", "TargetFrameworks",
            "OutputType", "AssemblyName", "RootNamespace",
            "LangVersion", "Nullable", "ImplicitUsings"
        };

        foreach (var key in orderedProperties)
        {
            if (project.Properties.TryGetValue(key, out var value))
            {
                propertyGroup.Add(new XElement(key, value));
            }
        }

        foreach (var prop in project.Properties)
        {
            if (!orderedProperties.Contains(prop.Key))
            {
                propertyGroup.Add(new XElement(prop.Key, prop.Value));
            }
        }

        root.Add(propertyGroup);
    }

    private void AddPackageReferences(XElement root, CsprojProject project)
    {
        var itemGroup = new XElement("ItemGroup");

        var attributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PrivateAssets", "ExcludeAssets", "IncludeAssets"
        };

        foreach (var package in project.PackageReferences.OrderBy(p => p.Name))
        {
            var element = new XElement("PackageReference",
                new XAttribute("Include", package.Name)
            );

            if (!string.IsNullOrEmpty(package.Version))
            {
                element.Add(new XAttribute("Version", package.Version));
            }
            
            // Add metadata
            foreach (var metadata in package.Metadata)
            {
                if (attributeNames.Contains(metadata.Key))
                {
                    element.Add(new XAttribute(metadata.Key, metadata.Value));
                }
                else
                {
                    element.Add(new XElement(metadata.Key, metadata.Value));
                }
            }

            itemGroup.Add(element);
        }

        root.Add(itemGroup);
    }

    private void AddProjectReferences(XElement root, CsprojProject project)
    {
        var itemGroup = new XElement("ItemGroup");

        foreach (var projectRef in project.ProjectReferences.OrderBy(p => p.Include))
        {
            var element = new XElement("ProjectReference",
                new XAttribute("Include", projectRef.Include)
            );

            // Add metadata
            foreach (var metadata in projectRef.Metadata)
            {
                element.Add(new XElement(metadata.Key, metadata.Value));
            }

            itemGroup.Add(element);
        }

        root.Add(itemGroup);
    }

    private void AddAssemblyReferences(XElement root, CsprojProject project)
    {
        var itemGroup = new XElement("ItemGroup");

        foreach (var assemblyRef in project.AssemblyReferences.OrderBy(a => a.Include))
        {
            var element = new XElement("Reference",
                new XAttribute("Include", assemblyRef.Include)
            );

            if (!string.IsNullOrEmpty(assemblyRef.HintPath))
            {
                element.Add(new XElement("HintPath", assemblyRef.HintPath));
            }

            // Add metadata
            foreach (var metadata in assemblyRef.Metadata)
            {
                element.Add(new XElement(metadata.Key, metadata.Value));
            }

            itemGroup.Add(element);
        }

        root.Add(itemGroup);
    }
}
