using TALXIS.CLI.Workspace.Upgrade.Models;

namespace TALXIS.CLI.Workspace.Upgrade.Utilities;

public class TemplateManager
{
    private readonly string _templatesBasePath;

    public TemplateManager(string templateBasePath)
    {
        _templatesBasePath = templateBasePath;
    }

    public string GetOldFormatTemplatePath(ProjectType projectType, bool isOldTalxisFormat = false)
    {
        return projectType switch
        {
            ProjectType.DataverseSolution => isOldTalxisFormat
                ? Path.Combine(_templatesBasePath, "DataverseSolution", "template_talxis.csproj")
                : Path.Combine(_templatesBasePath, "DataverseSolution", "template.cdsproj"),
            ProjectType.ScriptLibrary => Path.Combine(_templatesBasePath, "ScriptLibrary", "template.csproj"),
            ProjectType.Plugin => Path.Combine(_templatesBasePath, "Plugin", "template.csproj"),
            ProjectType.PDPackage => Path.Combine(_templatesBasePath, "PDPackage", "template.csproj"),
            _ => throw new NotSupportedException($"Project type {projectType} is not supported")
        };
    }

    public string GetNewFormatTemplatePath(ProjectType projectType)
    {
        return projectType switch
        {
            ProjectType.DataverseSolution => Path.Combine(_templatesBasePath, "DataverseSolution", "template.csproj"),
            ProjectType.ScriptLibrary => Path.Combine(_templatesBasePath, "ScriptLibrary", "template_new.csproj"),
            ProjectType.Plugin => Path.Combine(_templatesBasePath, "Plugin", "template_new.csproj"),
            ProjectType.PDPackage => Path.Combine(_templatesBasePath, "PDPackage", "template_new.csproj"),
            _ => throw new NotSupportedException($"Project type {projectType} is not supported")
        };
    }

    public bool TemplatesExist(ProjectType projectType, bool isOldTalxisFormat = false)
    {
        try
        {
            var oldPath = GetOldFormatTemplatePath(projectType, isOldTalxisFormat);
            var newPath = GetNewFormatTemplatePath(projectType);

            return File.Exists(oldPath) && File.Exists(newPath);
        }
        catch
        {
            return false;
        }
    }

    public void ValidateTemplates(ProjectType projectType, bool isOldTalxisFormat = false)
    {
        if (!TemplatesExist(projectType, isOldTalxisFormat))
        {
            throw new FileNotFoundException(
                $"Template files for {projectType} not found in {_templatesBasePath}. " +
                $"Expected files:\n" +
                $"  - {GetOldFormatTemplatePath(projectType, isOldTalxisFormat)}\n" +
                $"  - {GetNewFormatTemplatePath(projectType)}"
            );
        }
    }
}
