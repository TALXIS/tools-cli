using Microsoft.Extensions.Logging;
using TALXIS.CLI.Workspace.Upgrade.Generation;
using TALXIS.CLI.Workspace.Upgrade.Models;
using TALXIS.CLI.Workspace.Upgrade.Parsers;
using TALXIS.CLI.Workspace.Upgrade.Utilities;

namespace TALXIS.CLI.Workspace.Upgrade.Conversion;

public class ProjectUpgrader
{
    private readonly CsprojParser _parser;
    private readonly SdkProjectGenerator _generator;
    private readonly ILogger<ProjectUpgrader> _logger;
    
    public ProjectUpgrader(ILogger<ProjectUpgrader> logger)
    {
        _parser = new CsprojParser();
        _generator = new SdkProjectGenerator();
        _logger = logger;
    }

    public UpgradeResult Upgrade(
        string targetFilePath,
        string oldTemplateFilePath,
        string newTemplateFilePath,
        bool createBackup = true)
    {
        var result = new UpgradeResult();

        try
        {
            _logger.LogInformation("Parsing project and templates...");
            var targetProject = _parser.Parse(targetFilePath);
            var oldTemplate = _parser.Parse(oldTemplateFilePath);
            var newTemplate = _parser.Parse(newTemplateFilePath);

            _logger.LogInformation("Filling parameter placeholders from existing project...");
            var newProject = FillParameterPlaceholders(newTemplate, targetProject, targetFilePath);

            _logger.LogInformation("Extracting additional dependencies not in baseline template...");
            ExtractAdditionalDependencies(oldTemplate, targetProject, newProject);

            result.CustomPropertiesFound = CountFilledParameters(newTemplate, targetProject);
            result.PackageReferencesFound = newProject.PackageReferences.Count - newTemplate.PackageReferences.Count;
            result.ProjectReferencesFound = newProject.ProjectReferences.Count - newTemplate.ProjectReferences.Count;
            result.AssemblyReferencesFound = newProject.AssemblyReferences.Count - newTemplate.AssemblyReferences.Count;

            if (createBackup)
            {
                var backupPath = targetFilePath + ".backup";
                if (File.Exists(targetFilePath))
                {
                    File.Copy(targetFilePath, backupPath, overwrite: true);
                    result.BackupCreated = true;
                    result.BackupPath = backupPath;
                    _logger.LogInformation("Backup created at {BackupPath}", backupPath);
                }
            }

            if (File.Exists(targetFilePath))
            {
                _logger.LogInformation("Removing original file {TargetFile}", targetFilePath);
                File.Delete(targetFilePath);
            }

            var newFilePath = Path.Combine(
                Path.GetDirectoryName(targetFilePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(targetFilePath) + ".csproj");

            _logger.LogInformation("Generating new SDK-style project file at {NewFilePath}", newFilePath);
            var xml = _generator.Generate(newProject);
            _generator.SaveToFile(xml, newFilePath);

            result.Success = true;
            result.OutputFilePath = newFilePath;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            _logger.LogError(ex, "Upgrade failed for {TargetFile}", targetFilePath);
        }

        return result;
    }

    private CsprojProject FillParameterPlaceholders(
        CsprojProject newTemplate,
        CsprojProject targetProject,
        string targetFilePath)
    {
        var newProject = new CsprojProject
        {
            FilePath = targetFilePath.Replace(".cdsproj", ".csproj"),
            IsSdkStyle = true,
            Sdk = newTemplate.Sdk ?? "Microsoft.NET.Sdk"
        };

        // Copy all properties 
        foreach (var prop in newTemplate.Properties)
        {
            // If template property has empty value, it's a parameter placeholder - fill from target
            if (string.IsNullOrWhiteSpace(prop.Value))
            {
                if (targetProject.Properties.TryGetValue(prop.Key, out var targetValue))
                {
                    newProject.Properties[prop.Key] = targetValue;
                }
                else
                {
                    newProject.Properties[prop.Key] = prop.Value;
                }
            }
            else
            {
                newProject.Properties[prop.Key] = prop.Value;
            }
        }

        // Copy all other elements
        newProject.PackageReferences.AddRange(newTemplate.PackageReferences);
        newProject.ProjectReferences.AddRange(newTemplate.ProjectReferences);
        newProject.AssemblyReferences.AddRange(newTemplate.AssemblyReferences);
        newProject.CustomTargets.AddRange(newTemplate.CustomTargets);
        newProject.CustomImports.AddRange(newTemplate.CustomImports);
        newProject.CustomItemGroups.AddRange(newTemplate.CustomItemGroups);
        newProject.CustomPropertyGroups.AddRange(newTemplate.CustomPropertyGroups);

        return newProject;
    }

    private int CountFilledParameters(CsprojProject newTemplate, CsprojProject targetProject)
    {
        int count = 0;
        foreach (var prop in newTemplate.Properties)
        {
            if (string.IsNullOrWhiteSpace(prop.Value) && targetProject.Properties.ContainsKey(prop.Key))
            {
                count++;
            }
        }
        return count;
    }

    private void ExtractAdditionalDependencies(
        CsprojProject oldTemplate,
        CsprojProject targetProject,
        CsprojProject newProject)
    {
        // Find PackageReferences that are in target but NOT in old template baseline
        var baselinePackages = new HashSet<string>(
            oldTemplate.PackageReferences.Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var package in targetProject.PackageReferences)
        {
            if (!baselinePackages.Contains(package.Name))
            {
                // This is a custom package added by user
                newProject.PackageReferences.Add(package);
            }
        }

        // Find ProjectReferences that are in target but NOT in old template baseline
        var baselineProjects = new HashSet<string>(
            oldTemplate.ProjectReferences.Select(p => p.Include),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var projectRef in targetProject.ProjectReferences)
        {
            if (!baselineProjects.Contains(projectRef.Include))
            {
                // This is a custom project reference added by user
                newProject.ProjectReferences.Add(projectRef);
            }
        }

        // Find AssemblyReferences that are in target but NOT in old template baseline
        var baselineAssemblies = new HashSet<string>(
            oldTemplate.AssemblyReferences.Select(a => a.Include),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var assembly in targetProject.AssemblyReferences)
        {
            if (!baselineAssemblies.Contains(assembly.Include))
            {
                // This is a custom assembly reference added by user
                newProject.AssemblyReferences.Add(assembly);
            }
        }
    }
}
