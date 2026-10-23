using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Packaging;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata.Validation;

namespace TALXIS.CLI.Features.Workspace.Controls;

/// <summary>
/// Overlays a PCF custom control on an existing subgrid of a form. The control's
/// <c>ControlManifest.xml</c> supplies the parameter schema — resolved from a NuGet
/// package name (downloaded automatically, like <c>env pkg import</c>) or from a local
/// file (bare manifest, solution zip, pdpkg.zip, or nupkg); dataset binding is copied
/// from the host subgrid; the modified form is re-validated with the platform metadata
/// schema validator.
/// </summary>
[CliIdempotent]
[CliCommand(
    Description = "Attach a custom control to a subgrid on a form, driven by the control's manifest",
    Name = "attach")]
public class ControlAttachCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ControlAttachCliCommand));

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Solution project root containing the Entities folder", Required = true)]
    public string OutputPath { get; set; } = null!;

    [CliOption(Name = "--entity", Description = "Logical name of the entity that owns the form (e.g. almlab_warehouselocation)", Required = true)]
    public string EntityLogicalName { get; set; } = null!;

    [CliOption(Name = "--form-type", Description = "Form type folder name", Required = false)]
    public string FormType { get; set; } = "main";

    [CliOption(Name = "--form-id", Description = "Form GUID (without braces). Optional when the entity has exactly one form of the given type.", Required = false)]
    public string? FormId { get; set; }

    [CliOption(Name = "--target-control", Description = "FormXml id of the subgrid to overlay (e.g. subgrid)", Required = true)]
    public string TargetControlId { get; set; } = null!;

    [CliOption(Name = "--package", Description = "NuGet package name of the control (downloaded automatically), or local path to its ControlManifest.xml / solution .zip / .pdpkg.zip / .nupkg", Required = true)]
    public string Package { get; set; } = null!;

    [CliOption(Name = "--version", Description = "NuGet package version (only when '--package' is a NuGet name).", Required = false)]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--control-name", Description = "Publisher-prefixed control name for FormXml (e.g. talxis_TALXIS.PCF.Grid). Required only when it cannot be resolved from the manifest source.", Required = false)]
    public string? ControlName { get; set; }

    [CliOption(Description = "Control parameters in key=value format (validated against the manifest). Can be specified multiple times.")]
    public List<string> Param { get; set; } = new();

    [CliOption(Description = "Replace an existing custom control attachment on the same subgrid.")]
    public bool Force { get; set; }

    private readonly NuGetPackageInstallerService _packageInstaller = new();

    protected override async Task<int> ExecuteAsync()
    {
        string manifestSource;
        string? tempWorkingDirectory = null;
        if (File.Exists(Package))
        {
            manifestSource = Path.GetFullPath(Package);
        }
        else if (Package.IndexOfAny(['\\', '/']) >= 0 || HasManifestFileExtension(Package))
        {
            // Looks like a file path (NuGet ids contain dots but never these extensions).
            Logger.LogError("Manifest source not found: {Package}", Package);
            return ExitValidationError;
        }
        else
        {
            var install = await _packageInstaller.InstallAsync(new NuGetPackageInstallOptions(Package, PackageVersion, null));
            Logger.LogInformation("Resolved {PackageName} version {Version}", install.PackageName, install.ResolvedVersion);
            manifestSource = install.DownloadedPackagePath;
            if (install.UsesTemporaryWorkingDirectory)
                tempWorkingDirectory = install.WorkingDirectory;
        }

        try
        {
            return AttachFromManifest(manifestSource);
        }
        finally
        {
            if (tempWorkingDirectory != null)
                Directory.Delete(tempWorkingDirectory, recursive: true);
        }
    }

    private int AttachFromManifest(string manifestSource)
    {
        var manifest = ControlManifestReader.Read(manifestSource);

        var controlName = ControlName ?? manifest.PrefixedName;
        if (string.IsNullOrEmpty(controlName))
        {
            Logger.LogError("The publisher-prefixed control name could not be resolved from '{Manifest}'. Pass it explicitly with --control-name (e.g. talxis_{Qualified}).", manifestSource, manifest.QualifiedName);
            return ExitValidationError;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Param)
        {
            var idx = p.IndexOf('=');
            if (idx <= 0 || idx == p.Length - 1)
                throw new ArgumentException($"Invalid parameter format: '{p}'. Use key=value.");
            parameters[p.Substring(0, idx)] = p.Substring(idx + 1);
        }

        var formFile = ResolveFormFile();
        var preErrors = CountSchemaErrors(formFile);

        var result = FormControlAttachmentService.Attach(new ControlAttachmentRequest
        {
            FormFilePath = formFile,
            TargetControlId = TargetControlId,
            Manifest = manifest,
            ControlName = controlName,
            Parameters = parameters,
            Force = Force,
        });

        var postErrors = CountSchemaErrors(formFile);
        if (postErrors > preErrors)
            Logger.LogWarning("Schema validation reports {New} new issue(s) on {File} after the change — run 'txc workspace validate' for details.", postErrors - preErrors, formFile);

        var action = result.ReplacedExisting ? "replaced on" : "attached to";
        OutputFormatter.WriteResult("succeeded", $"{controlName} {action} '{TargetControlId}' in {formFile}");
        return ExitSuccess;
    }

    private string ResolveFormFile()
    {
        var formDir = Path.Combine(OutputPath, "Entities", EntityLogicalName, "FormXml", FormType);
        if (!Directory.Exists(formDir))
            throw new InvalidOperationException($"Form folder not found: {formDir}");

        if (!string.IsNullOrEmpty(FormId))
        {
            var wanted = FormId.Trim('{', '}');
            var match = Directory.GetFiles(formDir, "*.xml").FirstOrDefault(f =>
                NormalizeFormFileName(f).Equals(wanted, StringComparison.OrdinalIgnoreCase));
            return match ?? throw new InvalidOperationException($"Form {FormId} not found in {formDir}");
        }

        var forms = Directory.GetFiles(formDir, "*.xml");
        return forms.Length switch
        {
            1 => forms[0],
            0 => throw new InvalidOperationException($"No forms found in {formDir}"),
            _ => throw new InvalidOperationException($"Multiple forms found in {formDir} — pass --form-id. Candidates: {string.Join(", ", forms.Select(Path.GetFileName))}"),
        };
    }

    private static bool HasManifestFileExtension(string value) =>
        value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

    // Managed-layer sources use a "{guid}_managed.xml" file name — match on the guid alone.
    private static string NormalizeFormFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith("_managed", StringComparison.OrdinalIgnoreCase))
            name = name[..^"_managed".Length];
        return name.Trim('{', '}');
    }

    private static int CountSchemaErrors(string formFile)
    {
        try
        {
            var results = new SchemaValidator().ValidateFile(formFile);
            return results.Count(r => r.Severity == ValidationSeverity.Error);
        }
        catch
        {
            return 0;
        }
    }
}
