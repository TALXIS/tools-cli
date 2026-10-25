using System.Xml.Linq;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Packaging;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata.Components;
using TALXIS.Platform.Metadata.Controls;
using TALXIS.Platform.Metadata.Serialization.Xml;
using TALXIS.Platform.Metadata.Serialization.Xml.Controls;
using TALXIS.Platform.Metadata.Validation;

namespace TALXIS.CLI.Features.Workspace.Controls;

/// <summary>
/// Binds a PCF custom control to an existing subgrid of a form. The control's
/// <c>ControlManifest.xml</c> supplies the parameter schema — resolved from a NuGet
/// package name (downloaded automatically, like <c>env pkg import</c>), from a local
/// file (bare manifest, solution zip, pdpkg.zip, or nupkg), or from a control project
/// folder / <c>.csproj</c>; dataset binding is copied from the host subgrid; the
/// modified form is re-validated with the platform metadata schema validator.
/// </summary>
[CliIdempotent]
[CliCommand(
    Description = "Bind a custom control to a subgrid on a form, driven by the control's manifest",
    Name = "bind")]
public class ControlBindCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ControlBindCliCommand));

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Solution project root containing the Entities folder", Required = true)]
    public string OutputPath { get; set; } = null!;

    [CliOption(Name = "--entity", Description = "Logical name of the entity that owns the form (e.g. almlab_warehouselocation). Optional when the form can be resolved unambiguously without it.", Required = false)]
    public string? EntityLogicalName { get; set; }

    [CliOption(Name = "--form-type", Description = "Form type folder name", Required = false)]
    public string FormType { get; set; } = "main";

    [CliOption(Name = "--form-id", Description = "Form GUID (without braces). Optional when exactly one form of the given type is in scope.", Required = false)]
    public string? FormId { get; set; }

    [CliOption(Name = "--target-control", Description = "FormXml id of the subgrid to bind to (e.g. subgrid)", Required = true)]
    public string TargetControlId { get; set; } = null!;

    [CliOption(Name = "--source", Description = "NuGet package name of the control (downloaded automatically), local path to its ControlManifest.xml / solution .zip / .pdpkg.zip / .nupkg, or the control project's folder / .csproj", Required = true)]
    public string Source { get; set; } = null!;

    [CliOption(Name = "--version", Description = "NuGet package version (only when '--source' is a NuGet name).", Required = false)]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--control-name", Description = "Publisher-prefixed control name for FormXml (e.g. talxis_TALXIS.PCF.Grid). Required only when it cannot be resolved from the manifest source.", Required = false)]
    public string? ControlName { get; set; }

    [CliOption(Description = "Control parameters in key=value format (validated against the manifest). Can be specified multiple times.")]
    public List<string> Param { get; set; } = new();

    [CliOption(Description = "Replace an existing custom control binding on the same subgrid.")]
    public bool Force { get; set; }

    private readonly NuGetPackageInstallerService _packageInstaller = new();

    protected override async Task<int> ExecuteAsync()
    {
        string manifestSource;
        string? tempWorkingDirectory = null;
        if (File.Exists(Source) || Directory.Exists(Source))
        {
            manifestSource = Path.GetFullPath(Source);
        }
        else if (Source.IndexOfAny(['\\', '/']) >= 0 || HasManifestFileExtension(Source))
        {
            // Looks like a file path (NuGet ids contain dots but never these extensions).
            Logger.LogError("Manifest source not found: {Source}", Source);
            return ExitValidationError;
        }
        else
        {
            var install = await _packageInstaller.InstallAsync(new NuGetPackageInstallOptions(Source, PackageVersion, null));
            Logger.LogInformation("Resolved {PackageName} version {Version}", install.PackageName, install.ResolvedVersion);
            manifestSource = install.DownloadedPackagePath;
            if (install.UsesTemporaryWorkingDirectory)
                tempWorkingDirectory = install.WorkingDirectory;
        }

        try
        {
            return BindFromManifest(manifestSource);
        }
        finally
        {
            if (tempWorkingDirectory != null)
                Directory.Delete(tempWorkingDirectory, recursive: true);
        }
    }

    private int BindFromManifest(string manifestSource)
    {
        var control = CustomControlReader.Read(manifestSource);
        var manifest = control.Manifest;

        var controlName = ControlName ?? control.Name;
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

        ControlBindingResult result;
        try
        {
            result = BindToFormFile(formFile, new ControlBindingRequest
            {
                TargetControlId = TargetControlId,
                Manifest = manifest,
                ControlName = controlName,
                Parameters = parameters,
                Force = Force,
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already bound"))
        {
            Logger.LogError("{Message} Use --force to replace it.", ex.Message);
            return ExitValidationError;
        }

        var postErrors = CountSchemaErrors(formFile);
        if (postErrors > preErrors)
            Logger.LogWarning("Schema validation reports {New} new issue(s) on {File} after the change — run 'txc workspace validate' for details.", postErrors - preErrors, formFile);

        var action = result.ReplacedExisting ? "replaced on" : "bound to";
        OutputFormatter.WriteResult("succeeded", $"{controlName} {action} '{TargetControlId}' in {formFile}");
        return ExitSuccess;
    }

    // File-level adapter over the in-memory operation: load the form body into the
    // metadata model, bind, and write the modified body back into the same document.
    private static ControlBindingResult BindToFormFile(string formFile, ControlBindingRequest request)
    {
        var doc = XDocument.Load(formFile);
        var formElement = doc.Descendants("form").FirstOrDefault()
            ?? throw new InvalidOperationException($"No <form> element in '{formFile}'.");

        var form = new FormMetadata
        {
            FormId = doc.Root?.Element("systemform")?.Element("formid")?.Value ?? NormalizeFormFileName(formFile),
            Body = MergeableNodeXmlConverter.FromXElement(formElement),
        };

        var result = FormControlBinding.Bind(form, request);

        formElement.ReplaceWith(MergeableNodeXmlConverter.ToXElement(form.Body!));
        doc.Save(formFile);
        return result;
    }

    private string ResolveFormFile()
    {
        var candidates = CollectCandidateFormFiles();

        if (!string.IsNullOrEmpty(FormId))
        {
            var wanted = FormId!.Trim('{', '}');
            var match = candidates.FirstOrDefault(f =>
                NormalizeFormFileName(f).Equals(wanted, StringComparison.OrdinalIgnoreCase));
            return match ?? throw new InvalidOperationException($"Form {FormId} not found in the searched form folders.");
        }

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException($"No '{FormType}' forms found under {Path.Combine(OutputPath, "Entities")}"),
            _ => throw new InvalidOperationException($"Multiple forms found - pass --entity and/or --form-id. Candidates: {string.Join(", ", candidates.Select(RelativeToOutput))}"),
        };
    }

    // With --entity the scope is that entity's form folder; without it every entity in the project is searched.
    private List<string> CollectCandidateFormFiles()
    {
        if (!string.IsNullOrEmpty(EntityLogicalName))
        {
            var formDir = Path.Combine(OutputPath, "Entities", EntityLogicalName, "FormXml", FormType);
            if (!Directory.Exists(formDir))
                throw new InvalidOperationException($"Form folder not found: {formDir}");
            return Directory.GetFiles(formDir, "*.xml").ToList();
        }

        var entitiesRoot = Path.Combine(OutputPath, "Entities");
        if (!Directory.Exists(entitiesRoot))
            throw new InvalidOperationException($"Entities folder not found: {entitiesRoot}");

        return Directory.GetDirectories(entitiesRoot)
            .Select(entityDir => Path.Combine(entityDir, "FormXml", FormType))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.xml"))
            .ToList();
    }

    private string RelativeToOutput(string path) =>
        Path.GetRelativePath(OutputPath, path);

    private static bool HasManifestFileExtension(string value) =>
        value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

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
