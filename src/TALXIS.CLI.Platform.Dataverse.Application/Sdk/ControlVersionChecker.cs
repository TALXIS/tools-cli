using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Detects PCF controls that Dataverse silently skips because their manifest version is not higher than the installed one.
/// </summary>
public sealed class ControlVersionChecker
{
    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public ControlVersionChecker(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    public static IReadOnlyList<PackagedControl> ReadPackagedControls(string solutionZipPath)
    {
        using var archive = ZipFile.OpenRead(solutionZipPath);
        return ReadPackagedControls(archive);
    }

    public static IReadOnlyList<PackagedControl> ReadPackagedControls(ZipArchive archive)
    {
        var controls = new List<PackagedControl>();
        foreach (var entry in archive.Entries)
        {
            if (!entry.Name.Equals("ControlManifest.xml", StringComparison.OrdinalIgnoreCase)) continue;

            // Solution layout is Controls/<prefixed-name>/ControlManifest.xml; the folder is the customcontrol name.
            var segments = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) continue;

            using var stream = entry.Open();
            var version = XDocument.Load(stream).Descendants("control").FirstOrDefault()?.Attribute("version")?.Value?.Trim();
            if (!string.IsNullOrEmpty(version)) controls.Add(new PackagedControl(segments[^2], version));
        }
        return controls;
    }

    /// <summary>
    /// Controls whose packaged version is not higher than the installed one, so the import will keep the old bundle.
    /// </summary>
    public static IReadOnlyList<ControlVersionWarning> FindNotNewer(
        IEnumerable<PackagedControl> packaged,
        IReadOnlyDictionary<string, string> installedVersions)
    {
        var warnings = new List<ControlVersionWarning>();
        foreach (var control in packaged)
        {
            if (!installedVersions.TryGetValue(control.Name, out var installed)) continue;
            if (!Version.TryParse(control.Version, out var packagedVersion) || !Version.TryParse(installed, out var installedVersion)) continue;
            if (packagedVersion > installedVersion) continue;

            warnings.Add(new ControlVersionWarning(
                control.Name,
                control.Version,
                installed,
                $"PCF control '{control.Name}' has manifest version {control.Version}, which is not higher than the installed {installed}. Dataverse keeps the installed bundle; bump the version in ControlManifest.Input.xml."));
        }
        return warnings;
    }

    /// <summary>
    /// Controls that still report a different version than the packaged one after a successful import.
    /// </summary>
    public static IReadOnlyList<ControlVersionWarning> FindNotApplied(
        IEnumerable<PackagedControl> packaged,
        IReadOnlyDictionary<string, string> installedVersionsAfterImport)
    {
        var warnings = new List<ControlVersionWarning>();
        foreach (var control in packaged)
        {
            if (!installedVersionsAfterImport.TryGetValue(control.Name, out var installed)) continue;
            if (string.Equals(installed, control.Version, StringComparison.OrdinalIgnoreCase)) continue;

            warnings.Add(new ControlVersionWarning(
                control.Name,
                control.Version,
                installed,
                $"PCF control '{control.Name}' still has version {installed} after import (packaged {control.Version}). The control was not updated."));
        }
        return warnings;
    }

    /// <summary>
    /// Installed versions keyed by control name, or null when the lookup fails (the check must never break an import).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>?> GetInstalledVersionsAsync(
        IReadOnlyCollection<PackagedControl> controls,
        CancellationToken cancellationToken = default)
    {
        if (controls.Count == 0) return new Dictionary<string, string>();

        try
        {
            var query = new QueryExpression(DataverseSchema.CustomControl.EntityName)
            {
                ColumnSet = new ColumnSet("name", "version"),
            };
            query.Criteria.AddCondition("name", ConditionOperator.In, controls.Select(c => (object)c.Name).ToArray());

            var result = await _service.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in result.Entities)
            {
                var name = entity.GetAttributeValue<string>("name");
                var version = entity.GetAttributeValue<string>("version");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version)) versions[name] = version;
            }
            return versions;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning("Could not read installed PCF control versions, skipping the version check: {Message}", ex.Message);
            return null;
        }
    }
}
