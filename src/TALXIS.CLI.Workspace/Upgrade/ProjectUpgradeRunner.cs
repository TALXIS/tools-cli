using Microsoft.Extensions.Logging;
using TALXIS.CLI.Workspace.Upgrade.Conversion;
using TALXIS.CLI.Workspace.Upgrade.Models;
using TALXIS.CLI.Workspace.Upgrade.Utilities;

namespace TALXIS.CLI.Workspace.Upgrade;

/// <summary>
/// Coordinates upgrading one or more project files.
/// </summary>
public class ProjectUpgradeRunner
{
    private readonly ILogger<ProjectUpgradeRunner> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _templatesBasePath;
    private readonly bool _createBackup;

    public ProjectUpgradeRunner(
        ILoggerFactory loggerFactory,
        string templatesBasePath,
        bool createBackup = true)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<ProjectUpgradeRunner>();
        _templatesBasePath = templatesBasePath ?? throw new ArgumentNullException(nameof(templatesBasePath));
        _createBackup = createBackup;
    }

    public int Run(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            _logger.LogError("Path to project file or directory must be provided.");
            return 1;
        }

        targetPath = Path.GetFullPath(targetPath);

        var projectFiles = ResolveTargets(targetPath);
        if (projectFiles.Count == 0)
        {
            _logger.LogError("No .csproj or .cdsproj files found at {TargetPath}", targetPath);
            return 1;
        }

        _logger.LogInformation("Found {Count} project file(s) to upgrade.", projectFiles.Count);

        var detector = new ProjectTypeDetector();
        var templateManager = new TemplateManager(_templatesBasePath);

        int processed = 0, succeeded = 0, failed = 0;

        foreach (var projectFile in projectFiles)
        {
            processed++;
            _logger.LogInformation("[{Index}/{Total}] Processing {File}", processed, projectFiles.Count, projectFile);

            var projectType = detector.DetectProjectType(projectFile);
            if (projectType == ProjectType.Unknown)
            {
                _logger.LogError("Unsupported project type for {File}. Supported: Dataverse Solution, Script Library, Plugin, PDPackage.", projectFile);
                failed++;
                continue;
            }

            var isOldTalxisFormat = projectType == ProjectType.DataverseSolution && detector.IsOldTalxisFormat(projectFile);
            if (isOldTalxisFormat)
            {
                _logger.LogInformation("Detected old TALXIS Dataverse Solution format for {File}", projectFile);
            }

            string oldTemplatePath;
            string newTemplatePath;
            try
            {
                oldTemplatePath = templateManager.GetOldFormatTemplatePath(projectType, isOldTalxisFormat);
                newTemplatePath = templateManager.GetNewFormatTemplatePath(projectType);
                templateManager.ValidateTemplates(projectType, isOldTalxisFormat);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Template files missing for {ProjectType}", projectType);
                failed++;
                continue;
            }

            _logger.LogInformation("Using templates. Old: {OldTemplate} | New: {NewTemplate}", Path.GetFileName(oldTemplatePath), Path.GetFileName(newTemplatePath));

            var upgraderLogger = _loggerFactory.CreateLogger<ProjectUpgrader>();
            var upgrader = new ProjectUpgrader(upgraderLogger);
            var result = upgrader.Upgrade(projectFile, oldTemplatePath, newTemplatePath, _createBackup);

            if (result.Success)
            {
                _logger.LogInformation("Upgrade successful. Output: {Output}", result.OutputFilePath);
                if (result.BackupCreated)
                {
                    _logger.LogInformation("Backup saved at {Backup}", result.BackupPath);
                }
                _logger.LogInformation("Transferred: {Packages} package refs, {Projects} project refs, {Assemblies} assembly refs, {Props} custom properties.",
                    result.PackageReferencesFound, result.ProjectReferencesFound, result.AssemblyReferencesFound, result.CustomPropertiesFound);
                succeeded++;
            }
            else
            {
                _logger.LogError("Upgrade failed for {File}: {Message}", projectFile, result.ErrorMessage);
                if (result.Exception != null)
                {
                    _logger.LogDebug(result.Exception, "Details for {File}", projectFile);
                }
                if (result.BackupCreated && !string.IsNullOrWhiteSpace(result.BackupPath))
                {
                    _logger.LogWarning("Original file backed up at {Backup}", result.BackupPath);
                }
                failed++;
            }
        }

        if (projectFiles.Count > 1)
        {
            _logger.LogInformation("Summary: processed {Processed}, successful {Succeeded}, failed {Failed}", processed, succeeded, failed);
        }

        return failed == 0 ? 0 : 1;
    }

    private List<string> ResolveTargets(string targetPath)
    {
        var projectFiles = new List<string>();

        if (File.Exists(targetPath))
        {
            projectFiles.Add(targetPath);
            return projectFiles;
        }

        if (Directory.Exists(targetPath))
        {
            projectFiles.AddRange(Directory.GetFiles(targetPath, "*.csproj", SearchOption.AllDirectories));
            projectFiles.AddRange(Directory.GetFiles(targetPath, "*.cdsproj", SearchOption.AllDirectories));
        }

        return projectFiles;
    }
}
