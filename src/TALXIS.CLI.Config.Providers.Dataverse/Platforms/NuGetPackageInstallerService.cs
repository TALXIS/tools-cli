using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TALXIS.CLI.Config.Providers.Dataverse.Platforms;

public sealed class NuGetPackageInstallerService
{
    private const string LatestVersionKeyword = "latest";
    private static readonly HttpClient SharedHttpClient = new();

    private readonly HttpClient _httpClient;

    public NuGetPackageInstallerService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<NuGetPackageInstallResult> InstallAsync(NuGetPackageInstallOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.PackageName))
        {
            throw new ArgumentException("A NuGet package name must be specified.", nameof(options));
        }

        string packageNameLower = options.PackageName.ToLowerInvariant();
        string resolvedVersion = await ResolveVersionAsync(packageNameLower, options.PackageVersion, cancellationToken);

        string workingDirectory = CreateWorkingDirectory(options.PackageName, options.OutputDirectory);
        string downloadedPackagePath = Path.Combine(workingDirectory, $"{options.PackageName}.{resolvedVersion}.nupkg");
        string extractedPackageDirectory = Path.Combine(workingDirectory, "expanded");

        await DownloadPackageAsync(packageNameLower, resolvedVersion, downloadedPackagePath, cancellationToken);

        Directory.CreateDirectory(extractedPackageDirectory);
        ZipFile.ExtractToDirectory(downloadedPackagePath, extractedPackageDirectory, overwriteFiles: true);

        string deployablePackagePath = ResolveDeployablePackagePath(extractedPackageDirectory);
        string stagedDeployablePackagePath = Path.Combine(workingDirectory, Path.GetFileName(deployablePackagePath));

        if (!string.Equals(deployablePackagePath, stagedDeployablePackagePath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(deployablePackagePath, stagedDeployablePackagePath, overwrite: true);
        }

        return new NuGetPackageInstallResult(
            options.PackageName,
            resolvedVersion,
            workingDirectory,
            downloadedPackagePath,
            extractedPackageDirectory,
            stagedDeployablePackagePath,
            string.IsNullOrWhiteSpace(options.OutputDirectory));
    }

    public static string ResolveDeployablePackagePath(string extractedPackageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedPackageDirectory);

        if (!Directory.Exists(extractedPackageDirectory))
        {
            throw new DirectoryNotFoundException($"The extracted package directory '{extractedPackageDirectory}' does not exist.");
        }

        List<string> candidates = Directory
            .EnumerateFiles(extractedPackageDirectory, "*", SearchOption.AllDirectories)
            .Where(IsDeployablePackageCandidate)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No deployable package was found under '{extractedPackageDirectory}'. Expected a file ending with '.pdpkg.zip' or '.pdpkg'.");
        }

        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one deployable package, but found {candidates.Count}: {string.Join(", ", candidates.Select(Path.GetFileName))}.");
        }

        return candidates[0];
    }

    private static bool IsDeployablePackageCandidate(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.EndsWith(".pdpkg.zip", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pdpkg", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateWorkingDirectory(string packageName, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);
            return fullOutputDirectory;
        }

        string sanitizedPackageName = string.Concat(packageName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "txc", "environment-install", sanitizedPackageName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        return temporaryDirectory;
    }

    private async Task<string> ResolveVersionAsync(string packageNameLower, string packageVersion, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(packageVersion)
            && !string.Equals(packageVersion, LatestVersionKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return packageVersion;
        }

        string indexUrl = $"https://api.nuget.org/v3-flatcontainer/{packageNameLower}/index.json";
        NuGetVersionIndexResponse? response = await _httpClient.GetFromJsonAsync<NuGetVersionIndexResponse>(indexUrl, cancellationToken);

        string? resolvedVersion = response?.Versions?.LastOrDefault();
        if (string.IsNullOrWhiteSpace(resolvedVersion))
        {
            throw new InvalidOperationException($"Could not resolve the latest version for NuGet package '{packageNameLower}'.");
        }

        return resolvedVersion;
    }

    private async Task DownloadPackageAsync(string packageNameLower, string version, string destinationPath, CancellationToken cancellationToken)
    {
        string packageUrl = $"https://api.nuget.org/v3-flatcontainer/{packageNameLower}/{version}/{packageNameLower}.{version}.nupkg";

        using HttpResponseMessage response = await _httpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream fileStream = File.Create(destinationPath);
        await responseStream.CopyToAsync(fileStream, cancellationToken);
    }

    private sealed class NuGetVersionIndexResponse
    {
        [JsonPropertyName("versions")]
        public List<string>? Versions { get; set; }
    }
}
