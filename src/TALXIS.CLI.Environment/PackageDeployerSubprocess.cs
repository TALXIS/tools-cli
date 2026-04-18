using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using TALXIS.CLI.XrmTools;

namespace TALXIS.CLI.Environment;

public static class PackageDeployerSubprocess
{
    private const string CommandName = "__txc_internal_package_deployer";
    private const string CleanupCommandName = "__txc_internal_package_deployer_cleanup";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], CleanupCommandName, StringComparison.Ordinal))
        {
            return await RunCleanupHelperAsync(args);
        }

        if (args.Length != 3 || !string.Equals(args[0], CommandName, StringComparison.Ordinal))
        {
            return null;
        }

        string requestPath = args[1];
        string resultPath = args[2];

        PackageDeployerResult result;
        try
        {
            PackageDeployerRequest request = await ReadJsonAsync<PackageDeployerRequest>(requestPath);
            using CancellationTokenSource parentWatcher = RegisterParentExitWatcher(request.ParentProcessId);
            try
            {
                PackageDeployerRunner runner = new();
                result = await runner.RunAsync(request);
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

        await WriteJsonAsync(resultPath, result);
        return result.Succeeded ? 0 : 1;
    }

    public static async Task<PackageDeployerResult> RunAsync(PackageDeployerRequest request, CancellationToken cancellationToken = default)
    {
        string coordinatorDirectory = Path.Combine(
            Path.GetTempPath(),
            "txc",
            "package-deployer-process",
            Guid.NewGuid().ToString("N"));
        string temporaryArtifactsDirectory = Path.Combine(
            Path.GetTempPath(),
            "txc",
            "package-deployer-host",
            Guid.NewGuid().ToString("N"));
        PackageDeployerRequest effectiveRequest = request with
        {
            TemporaryArtifactsDirectory = temporaryArtifactsDirectory,
            ParentProcessId = System.Environment.ProcessId
        };

        Directory.CreateDirectory(coordinatorDirectory);

        string requestPath = Path.Combine(coordinatorDirectory, "request.json");
        string resultPath = Path.Combine(coordinatorDirectory, "result.json");

        try
        {
            await WriteJsonAsync(requestPath, effectiveRequest);

            using Process process = StartSubprocess(requestPath, resultPath);
            using Process cleanupHelper = StartCleanupHelper(coordinatorDirectory, temporaryArtifactsDirectory, process.Id);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                TryKillProcess(cleanupHelper);
                await WaitForExitIgnoringErrorsAsync(process);
                await WaitForExitIgnoringErrorsAsync(cleanupHelper);
                throw;
            }

            if (!File.Exists(resultPath))
            {
                throw new InvalidOperationException("Package Deployer subprocess did not produce a result.");
            }

            return await ReadJsonAsync<PackageDeployerResult>(resultPath);
        }
        finally
        {
            TryDeleteDirectory(temporaryArtifactsDirectory);
            TryDeleteDirectory(coordinatorDirectory);
        }
    }

    internal static void TryDeleteDirectory(string path)
    {
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

    private static Process StartSubprocess(string requestPath, string resultPath)
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

        startInfo.ArgumentList.Add(CommandName);
        startInfo.ArgumentList.Add(requestPath);
        startInfo.ArgumentList.Add(resultPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Package Deployer subprocess.");
    }

    private static Process StartCleanupHelper(string coordinatorDirectory, string temporaryArtifactsDirectory, int childProcessId)
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

        startInfo.ArgumentList.Add(CleanupCommandName);
        startInfo.ArgumentList.Add(coordinatorDirectory);
        startInfo.ArgumentList.Add(temporaryArtifactsDirectory);
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
            await process.WaitForExitAsync();
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

        await WaitForProcessExitAsync(childProcessId);
        TryDeleteDirectory(temporaryArtifactsDirectory);

        await WaitForProcessExitAsync(parentProcessId);
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
            await WaitForProcessExitAsync(parentProcessId);
            if (!cts.IsCancellationRequested)
            {
                System.Environment.FailFast("Parent txc process exited while Package Deployer subprocess was still running.");
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
            await process.WaitForExitAsync();
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
        T? value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
        return value ?? throw new InvalidOperationException($"Could not deserialize '{path}'.");
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
    }
}
