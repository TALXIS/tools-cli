using System.IO.Compression;
using System.Xml.Linq;
using TALXIS.CLI.Core.Contracts.Packaging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Reads package ImportConfig.xml and resolves solution unique names in import order.
/// Supports NuGet package names, local deployable package files, and extracted package folders.
/// </summary>
public sealed class PackageImportConfigReader
{
    private readonly NuGetPackageInstallerService _nugetInstaller;

    public PackageImportConfigReader(NuGetPackageInstallerService? nugetInstaller = null)
    {
        _nugetInstaller = nugetInstaller ?? new NuGetPackageInstallerService();
    }

    public async Task<IReadOnlyList<string>> ReadSolutionUniqueNamesInImportOrderAsync(
        string packageSource,
        string packageVersion,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageSource))
        {
            throw new ArgumentException("A package source must be provided.", nameof(packageSource));
        }

        if (Directory.Exists(packageSource))
        {
            return ReadFromDirectory(Path.GetFullPath(packageSource));
        }

        if (File.Exists(packageSource))
        {
            return ReadFromDeployablePackageFile(Path.GetFullPath(packageSource));
        }

        var options = new NuGetPackageInstallOptions(
            PackageName: packageSource,
            PackageVersion: string.IsNullOrWhiteSpace(packageVersion) ? "latest" : packageVersion,
            OutputDirectory: outputDirectory);

        NuGetPackageInstallResult installResult = await _nugetInstaller
            .InstallAsync(options, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return ReadFromDeployablePackageFile(installResult.DeployablePackagePath);
        }
        finally
        {
            if (installResult.UsesTemporaryWorkingDirectory)
            {
                LegacyAssemblyHostSubprocess.TryDeleteDirectory(installResult.WorkingDirectory);
            }
        }
    }

    private IReadOnlyList<string> ReadFromDirectory(string directory)
    {
        var importConfig = Directory
            .EnumerateFiles(directory, "ImportConfig.xml", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();

        if (importConfig is null)
        {
            throw new InvalidOperationException($"ImportConfig.xml was not found under '{directory}'.");
        }

        var solutionPackages = ParseImportConfigSolutionPackageFilenames(importConfig);
        if (solutionPackages.Count == 0)
        {
            throw new InvalidOperationException($"No <configsolutionfile> entries found in '{importConfig}'.");
        }

        var resolved = new List<string>(solutionPackages.Count);
        foreach (var packageFileName in solutionPackages)
        {
            var candidate = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetFileName(path), packageFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.Length)
                .FirstOrDefault();

            if (candidate is null)
            {
                throw new InvalidOperationException($"Solution package '{packageFileName}' from ImportConfig.xml was not found under '{directory}'.");
            }

            resolved.Add(ReadSolutionUniqueNameFromSolutionZip(candidate));
        }

        return resolved;
    }

    private IReadOnlyList<string> ReadFromDeployablePackageFile(string deployablePackagePath)
    {
        using var archive = ZipFile.OpenRead(deployablePackagePath);

        var importConfigEntry = archive.Entries
            .FirstOrDefault(e => string.Equals(Path.GetFileName(e.FullName), "ImportConfig.xml", StringComparison.OrdinalIgnoreCase));

        if (importConfigEntry is null)
        {
            throw new InvalidOperationException($"ImportConfig.xml was not found in deployable package '{deployablePackagePath}'.");
        }

        var solutionPackages = ParseImportConfigSolutionPackageFilenames(importConfigEntry);
        if (solutionPackages.Count == 0)
        {
            throw new InvalidOperationException($"No <configsolutionfile> entries found in ImportConfig.xml inside '{deployablePackagePath}'.");
        }

        var resolved = new List<string>(solutionPackages.Count);
        foreach (var packageFileName in solutionPackages)
        {
            var solutionZipEntry = archive.Entries
                .FirstOrDefault(e => string.Equals(Path.GetFileName(e.FullName), packageFileName, StringComparison.OrdinalIgnoreCase));

            if (solutionZipEntry is null)
            {
                throw new InvalidOperationException($"Solution package '{packageFileName}' from ImportConfig.xml was not found inside '{deployablePackagePath}'.");
            }

            resolved.Add(ReadSolutionUniqueNameFromSolutionZip(solutionZipEntry));
        }

        return resolved;
    }

    private static IReadOnlyList<string> ParseImportConfigSolutionPackageFilenames(string importConfigPath)
    {
        var doc = XDocument.Load(importConfigPath);
        return ParseImportConfigSolutionPackageFilenames(doc);
    }

    private static IReadOnlyList<string> ParseImportConfigSolutionPackageFilenames(ZipArchiveEntry importConfigEntry)
    {
        using var stream = importConfigEntry.Open();
        var doc = XDocument.Load(stream);
        return ParseImportConfigSolutionPackageFilenames(doc);
    }

    private static IReadOnlyList<string> ParseImportConfigSolutionPackageFilenames(XDocument doc)
    {
        return doc
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "configsolutionfile", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attribute("solutionpackagefilename")?.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
    }

    private static string ReadSolutionUniqueNameFromSolutionZip(string solutionZipPath)
    {
        using var archive = ZipFile.OpenRead(solutionZipPath);
        return ReadSolutionUniqueNameFromSolutionZipArchive(archive, solutionZipPath);
    }

    private static string ReadSolutionUniqueNameFromSolutionZip(ZipArchiveEntry solutionZipEntry)
    {
        using var payload = solutionZipEntry.Open();
        using var ms = new MemoryStream();
        payload.CopyTo(ms);
        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        return ReadSolutionUniqueNameFromSolutionZipArchive(archive, solutionZipEntry.FullName);
    }

    private static string ReadSolutionUniqueNameFromSolutionZipArchive(ZipArchive archive, string sourceLabel)
    {
        var solutionXml = archive.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFileName(e.FullName), "solution.xml", StringComparison.OrdinalIgnoreCase));

        if (solutionXml is null)
        {
            throw new InvalidOperationException($"solution.xml was not found in solution package '{sourceLabel}'.");
        }

        using var stream = solutionXml.Open();
        var doc = XDocument.Load(stream);
        var manifest = doc.Root?.Element("SolutionManifest")
            ?? throw new InvalidOperationException($"SolutionManifest is missing from solution.xml in '{sourceLabel}'.");

        var uniqueName = manifest.Element("UniqueName")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(uniqueName))
        {
            throw new InvalidOperationException($"UniqueName is missing from solution.xml in '{sourceLabel}'.");
        }

        return uniqueName;
    }
}
