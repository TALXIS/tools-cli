using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using TALXIS.CLI.Platform.Dataverse.Application.DependencyInjection;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Platform.Xrm;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Out-of-process host for runners that depend on the legacy-patched
/// Xrm.Tooling assemblies (<see cref="PackageDeployerRunner"/> and
/// <see cref="CmtImportRunner"/>). Spawning each run in a fresh process
/// keeps the <c>AssemblyResolve</c> probing + Cecil-patched assembly
/// redirects isolated from the main <c>txc</c> process.
/// </summary>
/// <remarks>
/// Auth flows through the shared MSAL cache file referenced by
/// <c>TXC_CONFIG_DIR</c>; the parent primes the cache (see
/// <see cref="DataverseCommandBridge.PrimeTokenAsync"/>) before spawning,
/// and the child performs its own silent token acquisitions against the
/// same cache via <see cref="DataverseCommandBridge.BuildTokenProviderAsync"/>.
/// </remarks>
public static class LegacyAssemblyHostSubprocess
{
    private const string PackageDeployerCommand = "__txc_internal_package_deployer";
    private const string CmtImportCommand = "__txc_internal_cmt_import";
    private const string CmtExportCommand = "__txc_internal_cmt_export";
    private const string CleanupCommand = "__txc_internal_package_deployer_cleanup";
    private const string ConfigDirectoryEnvVar = "TXC_CONFIG_DIR";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Child-process dispatcher. Called from <c>Program.Main</c>.</summary>
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        return args[0] switch
        {
            CleanupCommand => await RunCleanupHelperAsync(args).ConfigureAwait(false),
            PackageDeployerCommand => await RunPackageDeployerChildAsync(args).ConfigureAwait(false),
            CmtImportCommand => await RunCmtImportChildAsync(args).ConfigureAwait(false),
            CmtExportCommand => await RunCmtExportChildAsync(args).ConfigureAwait(false),
            _ => null,
        };
    }

    /// <summary>Parent-side entry point for a Package Deployer run.</summary>
    public static async Task<PackageDeployerResult> RunPackageDeployerAsync(
        PackageDeployerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string temporaryArtifactsDirectory = BuildTempDir("package-deployer-host");
        string? configDirectory = request.ConfigDirectory
            ?? System.Environment.GetEnvironmentVariable(ConfigDirectoryEnvVar);

        PackageDeployerRequest effectiveRequest = request with
        {
            TemporaryArtifactsDirectory = temporaryArtifactsDirectory,
            ParentProcessId = System.Environment.ProcessId,
            ConfigDirectory = configDirectory
        };

        return await RunJobAsync<PackageDeployerRequest, PackageDeployerResult>(
            PackageDeployerCommand,
            effectiveRequest,
            temporaryArtifactsDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parent-side entry point for a CMT data-import run.</summary>
    public static async Task<CmtImportResult> RunCmtImportAsync(
        CmtImportRequest request, string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profileId);

        string? configDirectory = System.Environment.GetEnvironmentVariable(ConfigDirectoryEnvVar);

        CmtImportJob envelope = new(
            request,
            profileId,
            configDirectory,
            System.Environment.ProcessId);

        // CMT does not produce the same extracted-package host directory
        // Package Deployer does, so there is nothing to clean up after the
        // run beyond the coordinator directory itself.
        return await RunJobAsync<CmtImportJob, CmtImportResult>(
            CmtImportCommand,
            envelope,
            temporaryArtifactsDirectory: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parent-side entry point for a CMT data-export run.</summary>
    public static async Task<CmtExportResult> RunCmtExportAsync(
        CmtExportRequest request, string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profileId);

        string? configDirectory = System.Environment.GetEnvironmentVariable(ConfigDirectoryEnvVar);

        CmtExportJob envelope = new(
            request,
            profileId,
            configDirectory,
            System.Environment.ProcessId);

        return await RunJobAsync<CmtExportJob, CmtExportResult>(
            CmtExportCommand,
            envelope,
            temporaryArtifactsDirectory: null,
            cancellationToken).ConfigureAwait(false);
    }

    internal static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        const int maxAttempts = 5;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                TryDeleteEmptyParentDirectories(path);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(250 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(250 * attempt));
            }
        }
    }

    private static async Task<TResult> RunJobAsync<TRequest, TResult>(
        string commandName,
        TRequest request,
        string? temporaryArtifactsDirectory,
        CancellationToken cancellationToken)
    {
        string coordinatorDirectory = BuildTempDir("package-deployer-process");
        Directory.CreateDirectory(coordinatorDirectory);

        string requestPath = Path.Combine(coordinatorDirectory, "request.json");
        string resultPath = Path.Combine(coordinatorDirectory, "result.json");

        try
        {
            await WriteJsonAsync(requestPath, request).ConfigureAwait(false);
            // Even though requests no longer carry secrets, keep coordinator
            // files readable only by the current user. On Windows per-user
            // %TEMP% ACLs suffice; on Unix we chmod 600.
            TrySetOwnerReadWriteOnly(requestPath);

            using Process process = StartSubprocess(commandName, requestPath, resultPath);
            using Process cleanupHelper = StartCleanupHelper(
                coordinatorDirectory,
                temporaryArtifactsDirectory,
                process.Id);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                TryKillProcess(cleanupHelper);
                await WaitForExitIgnoringErrorsAsync(process).ConfigureAwait(false);
                await WaitForExitIgnoringErrorsAsync(cleanupHelper).ConfigureAwait(false);
                throw;
            }

            if (!File.Exists(resultPath))
            {
                throw new InvalidOperationException($"Legacy-assembly host subprocess ('{commandName}') did not produce a result.");
            }

            return await ReadJsonAsync<TResult>(resultPath).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(temporaryArtifactsDirectory);
            TryDeleteDirectory(coordinatorDirectory);
        }
    }

    private static async Task<int?> RunPackageDeployerChildAsync(string[] args)
    {
        if (args.Length != 3)
        {
            return null;
        }

        string requestPath = args[1];
        string resultPath = args[2];

        PackageDeployerResult result;
        try
        {
            PackageDeployerRequest request = await ReadJsonAsync<PackageDeployerRequest>(requestPath).ConfigureAwait(false);

            ApplyConfigDirectory(request.ConfigDirectory);
            TxcServicesBootstrap.EnsureInitialized();

            using CancellationTokenSource parentWatcher = RegisterParentExitWatcher(request.ParentProcessId);
            try
            {
                var (envUrl, tokenProvider) = await DataverseCommandBridge
                    .BuildTokenProviderAsync(request.ProfileId, parentWatcher.Token)
                    .ConfigureAwait(false);

                PackageDeployerRunner runner = new();
                result = await runner.RunAsync(request, envUrl, tokenProvider, parentWatcher.Token).ConfigureAwait(false);
            }
            finally
            {
                parentWatcher.Cancel();
            }
        }
        catch (Exception ex)
        {
            result = new PackageDeployerResult(false, ex.Message, null, null, null);
        }

        await WriteJsonAsync(resultPath, result).ConfigureAwait(false);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int?> RunCmtImportChildAsync(string[] args)
    {
        if (args.Length != 3)
        {
            return null;
        }

        string requestPath = args[1];
        string resultPath = args[2];

        CmtImportResult result;
        try
        {
            CmtImportJob envelope = await ReadJsonAsync<CmtImportJob>(requestPath).ConfigureAwait(false);

            ApplyConfigDirectory(envelope.ConfigDirectory);
            TxcServicesBootstrap.EnsureInitialized();

            using CancellationTokenSource parentWatcher = RegisterParentExitWatcher(envelope.ParentProcessId);
            try
            {
                var (envUrl, tokenProvider) = await DataverseCommandBridge
                    .BuildTokenProviderAsync(envelope.ProfileId, parentWatcher.Token)
                    .ConfigureAwait(false);

                CmtImportRunner runner = new();
                result = await runner.RunAsync(envelope.Request, envUrl, tokenProvider, parentWatcher.Token).ConfigureAwait(false);
            }
            finally
            {
                parentWatcher.Cancel();
            }
        }
        catch (Exception ex)
        {
            result = new CmtImportResult(false, ex.Message);
        }

        await WriteJsonAsync(resultPath, result).ConfigureAwait(false);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int?> RunCmtExportChildAsync(string[] args)
    {
        if (args.Length != 3)
        {
            return null;
        }

        string requestPath = args[1];
        string resultPath = args[2];

        CmtExportResult result;
        try
        {
            CmtExportJob envelope = await ReadJsonAsync<CmtExportJob>(requestPath).ConfigureAwait(false);

            ApplyConfigDirectory(envelope.ConfigDirectory);
            TxcServicesBootstrap.EnsureInitialized();

            // Initialize the legacy assembly runtime BEFORE the JIT
            // encounters CmtExportRunner. ExportProcessor ships in a
            // NuGet tools/ folder and is not probed automatically —
            // the runtime must register it via TryPreloadAssembly.
            // The actual runner call is in a separate method so the JIT
            // does not try to resolve CmtExportRunner types until after
            // LegacyAssemblyRuntime has registered the assembly.
            LegacyAssemblyRuntime.EnsureInitialized();

            result = await RunCmtExportCoreAsync(envelope).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new CmtExportResult(false, ex.Message);
        }

        await WriteJsonAsync(resultPath, result).ConfigureAwait(false);
        return result.Succeeded ? 0 : 1;
    }

    /// <summary>
    /// Separated from <see cref="RunCmtExportChildAsync"/> so that the JIT
    /// defers resolution of <see cref="CmtExportRunner"/> (which depends on
    /// the ExportProcessor assembly) until after
    /// <see cref="LegacyAssemblyRuntime"/> has registered it.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<CmtExportResult> RunCmtExportCoreAsync(CmtExportJob envelope)
    {
        using CancellationTokenSource parentWatcher = RegisterParentExitWatcher(envelope.ParentProcessId);
        try
        {
            var (envUrl, tokenProvider) = await DataverseCommandBridge
                .BuildTokenProviderAsync(envelope.ProfileId, parentWatcher.Token)
                .ConfigureAwait(false);

            CmtExportRunner runner = new();
            return await runner.RunAsync(envelope.Request, envUrl, tokenProvider, parentWatcher.Token).ConfigureAwait(false);
        }
        finally
        {
            parentWatcher.Cancel();
        }
    }

    private static void ApplyConfigDirectory(string? configDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            System.Environment.SetEnvironmentVariable(ConfigDirectoryEnvVar, configDirectory);
        }
    }

    private static string BuildTempDir(string subPath) =>
        Path.Combine(Path.GetTempPath(), "txc", subPath, Guid.NewGuid().ToString("N"));

    private static void TryDeleteEmptyParentDirectories(string deletedPath)
    {
        string stopPath = Path.Combine(Path.GetTempPath(), "txc");
        DirectoryInfo? current = Directory.GetParent(deletedPath);

        while (current is not null &&
            current.Exists &&
            !string.Equals(current.FullName, stopPath, StringComparison.OrdinalIgnoreCase))
        {
            if (current.EnumerateFileSystemInfos().Any())
            {
                return;
            }

            try
            {
                Directory.Delete(current.FullName, recursive: false);
            }
            catch
            {
                return;
            }

            current = current.Parent;
        }
    }

    private static Process StartSubprocess(string commandName, string requestPath, string resultPath)
    {
        string processPath = System.Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve the current txc process path.");

        string? entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = false,
            WorkingDirectory = System.Environment.CurrentDirectory
        };

        if (IsDotnetHost(processPath))
        {
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("Could not resolve the txc entry assembly path.");
            }

            startInfo.FileName = processPath;
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }
        else
        {
            startInfo.FileName = processPath;
        }

        startInfo.ArgumentList.Add(commandName);
        startInfo.ArgumentList.Add(requestPath);
        startInfo.ArgumentList.Add(resultPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start the '{commandName}' subprocess.");
    }

    private static Process StartCleanupHelper(string coordinatorDirectory, string? temporaryArtifactsDirectory, int childProcessId)
    {
        string processPath = System.Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve the current txc process path.");

        string? entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = false,
            WorkingDirectory = System.Environment.CurrentDirectory,
            CreateNoWindow = true
        };

        if (IsDotnetHost(processPath))
        {
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("Could not resolve the txc entry assembly path.");
            }

            startInfo.FileName = processPath;
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }
        else
        {
            startInfo.FileName = processPath;
        }

        startInfo.ArgumentList.Add(CleanupCommand);
        startInfo.ArgumentList.Add(coordinatorDirectory);
        // Empty string sentinel for "no temp-artifacts directory" — argv lists
        // can't carry nulls, and the cleanup helper skips empty paths.
        startInfo.ArgumentList.Add(temporaryArtifactsDirectory ?? string.Empty);
        startInfo.ArgumentList.Add(childProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Package Deployer cleanup helper.");
    }

    private static bool IsDotnetHost(string processPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(processPath);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
            TryKillProcess(process);
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitForExitIgnoringErrorsAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<int?> RunCleanupHelperAsync(string[] args)
    {
        if (args.Length != 5)
        {
            return 1;
        }

        string coordinatorDirectory = args[1];
        string temporaryArtifactsDirectory = args[2];
        int childProcessId = int.Parse(args[3], CultureInfo.InvariantCulture);
        int parentProcessId = int.Parse(args[4], CultureInfo.InvariantCulture);

        await WaitForProcessExitAsync(childProcessId).ConfigureAwait(false);
        TryDeleteDirectory(temporaryArtifactsDirectory);

        await WaitForProcessExitAsync(parentProcessId).ConfigureAwait(false);
        TryDeleteDirectory(coordinatorDirectory);
        TryDeleteDirectory(temporaryArtifactsDirectory);
        return 0;
    }

    private static CancellationTokenSource RegisterParentExitWatcher(int parentProcessId)
    {
        CancellationTokenSource cts = new();
        if (parentProcessId <= 0)
        {
            return cts;
        }

        _ = Task.Run(async () =>
        {
            await WaitForProcessExitAsync(parentProcessId).ConfigureAwait(false);
            if (!cts.IsCancellationRequested)
            {
                System.Environment.FailFast("Parent txc process exited while a legacy-assembly host subprocess was still running.");
            }
        });

        return cts;
    }

    private static async Task WaitForProcessExitAsync(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        T? value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException($"Could not deserialize '{path}'.");
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions).ConfigureAwait(false);
    }

    private static void TrySetOwnerReadWriteOnly(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Per-user %TEMP% ACLs already restrict access. Explicit ACL
            // tightening via System.Security.AccessControl would add a Windows
            // SDK dependency for negligible benefit.
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
            // Running on a platform where Unix modes are meaningless.
        }
        catch (IOException)
        {
            // Filesystem does not honor mode bits (e.g. some mounted shares) —
            // best-effort only; the request file contains no secrets.
        }
    }
}
