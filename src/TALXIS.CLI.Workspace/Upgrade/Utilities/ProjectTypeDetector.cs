using System.Xml.Linq;
using TALXIS.CLI.Workspace.Upgrade.Models;

namespace TALXIS.CLI.Workspace.Upgrade.Utilities;

public class ProjectTypeDetector
{
    private static readonly XNamespace MsbuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

    public ProjectType DetectProjectType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if(extension == ".cdsproj") return ProjectType.DataverseSolution;

        if (extension == ".csproj")
        {
            return DetectFromContent(filePath);
        }

        return ProjectType.Unknown;
    }

    private ProjectType DetectFromContent(string filePath)
    {
        try
        {
            var xml = XDocument.Load(filePath);
            var root = xml.Root;

            if (root == null) return ProjectType.Unknown;

            // Check for PDPackage first (most specific)
            if (IsPDPackage(root))
            {
                return ProjectType.PDPackage;
            }

            // Check for Plugin
            if (IsPlugin(root))
            {
                return ProjectType.Plugin;
            }

            // Check for Script Library
            if (IsScriptLibrary(root))
            {
                return ProjectType.ScriptLibrary;
            }

            // Check for Dataverse Solution
            if (IsDataverseSolution(root))
            {
                return ProjectType.DataverseSolution;
            }

            return ProjectType.Unknown;
        }
        catch
        {
            // If we can't parse, fall back to Unknown
            return ProjectType.Unknown;
        }
    }

    private bool IsDataverseSolution(XElement root)
    {
        // Get all PropertyGroup elements
        var propertyGroups = root.Elements(MsbuildNamespace + "PropertyGroup")
            .Concat(root.Elements("PropertyGroup"));

        // Check for ProjectType = "Solution"
        var projectType = propertyGroups
            .Elements(MsbuildNamespace + "ProjectType")
            .Concat(propertyGroups.Elements("ProjectType"))
            .FirstOrDefault()?.Value;

        if (projectType == "Solution")
            return true;

        // Check for SolutionRootPath property
        var hasSolutionRootPath = propertyGroups
            .Elements(MsbuildNamespace + "SolutionRootPath")
            .Concat(propertyGroups.Elements("SolutionRootPath"))
            .Any();

        if (hasSolutionRootPath)
            return true;

        // Check for Publisher metadata
        var hasPublisherName = propertyGroups
            .Elements(MsbuildNamespace + "PublisherName")
            .Concat(propertyGroups.Elements("PublisherName"))
            .Any();

        var hasPublisherPrefix = propertyGroups
            .Elements(MsbuildNamespace + "PublisherPrefix")
            .Concat(propertyGroups.Elements("PublisherPrefix"))
            .Any();

        if (hasPublisherName || hasPublisherPrefix)
            return true;

        // Check for PowerApps imports
        var imports = root.Elements(MsbuildNamespace + "Import")
            .Concat(root.Elements("Import"));

        var hasPowerAppsImport = imports.Any(import =>
        {
            var project = import.Attribute("Project")?.Value ?? "";
            return project.Contains("PowerApps", StringComparison.OrdinalIgnoreCase);
        });

        if (hasPowerAppsImport)
            return true;

        // Check for old TALXIS format package reference
        var itemGroups = root.Elements(MsbuildNamespace + "ItemGroup")
            .Concat(root.Elements("ItemGroup"));

        var hasTalxisSdkReference = itemGroups
            .Elements(MsbuildNamespace + "PackageReference")
            .Concat(itemGroups.Elements("PackageReference"))
            .Any(pkg =>
            {
                var include = pkg.Attribute("Include")?.Value ?? "";
                return include.Contains("TALXIS.SDK.BuildTargets.CDS.Solution", StringComparison.OrdinalIgnoreCase);
            });

        if (hasTalxisSdkReference)
            return true;

        return false;
    }

    private bool IsPDPackage(XElement root)
    {
        var itemGroups = root.Elements(MsbuildNamespace + "ItemGroup")
            .Concat(root.Elements("ItemGroup"));

        var hasPDPackageReference = itemGroups
            .Elements(MsbuildNamespace + "PackageReference")
            .Concat(itemGroups.Elements("PackageReference"))
            .Any(pkg =>
            {
                var include = pkg.Attribute("Include")?.Value ?? "";
                return include.Contains("TALXIS.PowerApps.MSBuild.PDPackage", StringComparison.OrdinalIgnoreCase) ||
                        include.Contains("TALXIS.SDK.BuildTargets.CDS.Package", StringComparison.OrdinalIgnoreCase) ||
                       include.Contains("Microsoft.PowerApps.MSBuild.PDPackage", StringComparison.OrdinalIgnoreCase);
            });

        return hasPDPackageReference;
    }

    private bool IsPlugin(XElement root)
    {
        var itemGroups = root.Elements(MsbuildNamespace + "ItemGroup")
            .Concat(root.Elements("ItemGroup"));

        var hasPluginReference = itemGroups
            .Elements(MsbuildNamespace + "PackageReference")
            .Concat(itemGroups.Elements("PackageReference"))
            .Any(pkg =>
            {
                var include = pkg.Attribute("Include")?.Value ?? "";
                return include.Contains("Microsoft.PowerApps.MSBuild.Plugin", StringComparison.OrdinalIgnoreCase);
            });

        return hasPluginReference;
    }

    private bool IsScriptLibrary(XElement root)
    {
        var propertyGroups = root.Elements(MsbuildNamespace + "PropertyGroup")
            .Concat(root.Elements("PropertyGroup"));

        // Check for ProjectType = "ScriptLibrary" (new format)
        var projectType = propertyGroups
            .Elements(MsbuildNamespace + "ProjectType")
            .Concat(propertyGroups.Elements("ProjectType"))
            .FirstOrDefault()?.Value;

        if (projectType == "ScriptLibrary")
            return true;

        // Check for TypeScript properties
        var hasTypeScriptDir = propertyGroups
            .Elements(MsbuildNamespace + "TypeScriptDir")
            .Concat(propertyGroups.Elements("TypeScriptDir"))
            .Any();

        var hasTypeScriptBuildDir = propertyGroups
            .Elements(MsbuildNamespace + "TypeScriptBuildDir")
            .Concat(propertyGroups.Elements("TypeScriptBuildDir"))
            .Any();

        if (hasTypeScriptDir || hasTypeScriptBuildDir)
            return true;

        // Check for BuildTypeScript target
        var targets = root.Elements(MsbuildNamespace + "Target")
            .Concat(root.Elements("Target"));

        var hasBuildTypeScriptTarget = targets.Any(target =>
        {
            var name = target.Attribute("Name")?.Value ?? "";
            return name.Equals("BuildTypeScript", StringComparison.OrdinalIgnoreCase);
        });

        if (hasBuildTypeScriptTarget)
            return true;

        // Check for npm commands in Exec elements
        var execs = targets
            .Elements(MsbuildNamespace + "Exec")
            .Concat(targets.Elements("Exec"));

        var hasNpmCommand = execs.Any(exec =>
        {
            var command = exec.Attribute("Command")?.Value ?? "";
            return command.Contains("npm", StringComparison.OrdinalIgnoreCase);
        });

        return hasNpmCommand;
    }

    public bool IsOldTalxisFormat(string filePath)
    {
        try
        {
            var xml = XDocument.Load(filePath);
            var root = xml.Root;

            if (root == null) return false;

            var itemGroups = root.Elements(MsbuildNamespace + "ItemGroup")
                .Concat(root.Elements("ItemGroup"));

            var hasTalxisSdkReference = itemGroups
                .Elements(MsbuildNamespace + "PackageReference")
                .Concat(itemGroups.Elements("PackageReference"))
                .Any(pkg =>
                {
                    var include = pkg.Attribute("Include")?.Value ?? "";
                    return include.Contains("TALXIS.SDK.BuildTargets.CDS.Solution", StringComparison.OrdinalIgnoreCase);
                });

            return hasTalxisSdkReference;
        }
        catch
        {
            return false;
        }
    }
}
