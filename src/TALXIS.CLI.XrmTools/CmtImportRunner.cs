using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Tooling.Dmt.DataMigCommon.Utility;
using Microsoft.Xrm.Tooling.Dmt.ImportProcessor.DataInteraction;

namespace TALXIS.CLI.XrmTools;

/// <summary>
/// Standalone CMT data import runner — uses <c>ImportCrmDataHandler</c>
/// directly (no Package Deployer / CoreObjects wrapper). Mirrors the
/// approach taken by <c>pac data import</c>.
///
/// Shared infrastructure (Cecil patching, assembly resolvers) is provided
/// by <see cref="LegacyAssemblyRuntime"/>.
/// </summary>
public sealed class CmtImportRunner
{
    static CmtImportRunner()
    {
        LegacyAssemblyRuntime.EnsureInitialized();
    }

    /// <summary>
    /// Instance-level assembly map that extends the static map with
    /// assemblies found inside the extracted data package directory.
    /// </summary>
    private readonly Dictionary<string, Assembly> _assemblyMap;
    private readonly HashSet<string> _unresolvedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks the number of failed stages reported by CMT progress events.
    /// Checked after <c>ImportDataToCrm</c> returns to detect silent failures.
    /// </summary>
    private int _failedStageCount;

    public CmtImportRunner()
    {
        _assemblyMap = new Dictionary<string, Assembly>(
            LegacyAssemblyRuntime.StaticAssemblyMap,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs a standalone CMT data import. The flow follows the same
    /// pattern as <c>pac data import</c>:
    /// <list type="number">
    ///   <item>Extract the data.zip into a working directory</item>
    ///   <item>Connect to Dataverse (connection string or interactive auth)</item>
    ///   <item>Create <see cref="ImportCrmDataHandler"/></item>
    ///   <item>Wire progress events</item>
    ///   <item>Validate schema</item>
    ///   <item>Import data</item>
    /// </list>
    /// </summary>
    public async Task<CmtImportResult> RunAsync(CmtImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.DataZipPath))
        {
            return new CmtImportResult(false, $"Data package not found: '{request.DataZipPath}'");
        }

        // Register instance-level assembly resolver for any DLLs shipped
        // alongside the data package.
        AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;
        AssemblyLoadContext.Default.Resolving += OnResolveAssemblyLoadContext;

        string? workingFolder = null;
        CrmServiceClient? crmServiceClient = null;
        DataverseInteractiveAuthHook? authHook = null;

        try
        {
            // 1. Extract the data.zip into a temporary working directory.
            workingFolder = Path.Combine(
                Path.GetTempPath(),
                "txc",
                "cmt-import",
                $"{Path.GetFileNameWithoutExtension(request.DataZipPath)}-{Guid.NewGuid():N}");

            Directory.CreateDirectory(workingFolder);
            Console.WriteLine($"Extracting data package to '{workingFolder}'...");
            ZipFile.ExtractToDirectory(request.DataZipPath, workingFolder, overwriteFiles: true);

            // Register extracted directory for assembly probing.
            RegisterExtractedDirectoryForProbing(workingFolder);

            // 2. Connect to Dataverse.
            if (!string.IsNullOrWhiteSpace(request.ConnectionString))
            {
                crmServiceClient = new CrmServiceClient(request.ConnectionString);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.EnvironmentUrl) ||
                    !Uri.TryCreate(request.EnvironmentUrl, UriKind.Absolute, out Uri? environmentUri))
                {
                    return new CmtImportResult(false, "A valid Dataverse environment URL or connection string is required.");
                }

                authHook = new DataverseInteractiveAuthHook(environmentUri, request.DeviceCode, request.Verbose);
                CrmServiceClient.AuthOverrideHook = authHook;
                crmServiceClient = new CrmServiceClient(environmentUri, useUniqueInstance: true);
            }

            if (!crmServiceClient.IsReady)
            {
                string error = crmServiceClient.LastCrmException?.Message ?? crmServiceClient.LastCrmError ?? "Unknown connection error.";
                return new CmtImportResult(false, error);
            }

            if (request.Verbose)
            {
                Console.WriteLine($"[txc] Connected to: {crmServiceClient.ConnectedOrgUriActual}");
                Console.WriteLine($"[txc] Organization version: {crmServiceClient.ConnectedOrgVersion}");
            }

            // 3. Create ImportCrmDataHandler and set connection via reflection.
            // The CrmConnection property type is the legacy CrmServiceClient
            // (strong-named v4.0.0.0). At runtime our shim satisfies the
            // reference via the assembly resolver, but the compiler can't
            // see the match, so we set it reflectively.
            var handler = new ImportCrmDataHandler();
            var crmConnProp = handler.GetType().GetProperty("CrmConnection")
                ?? throw new InvalidOperationException("ImportCrmDataHandler.CrmConnection property not found.");
            crmConnProp.SetValue(handler, crmServiceClient);

            // 4. Wire progress event handlers.
            handler.AddNewProgressItem += OnAddNewProgressItem;
            handler.UpdateProgressItem += OnUpdateProgressItem;

            // 5. Create clone connections for parallel import.
            if (request.ConnectionCount > 1)
            {
                var clones = new List<object>();
                for (int i = 1; i < request.ConnectionCount; i++)
                {
                    try
                    {
                        // Create additional CrmServiceClient instances using the
                        // same auth approach — either connection string or auth hook.
                        CrmServiceClient clone;
                        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
                        {
                            clone = new CrmServiceClient(request.ConnectionString);
                        }
                        else
                        {
                            clone = new CrmServiceClient(
                                new Uri(request.EnvironmentUrl!),
                                useUniqueInstance: true);
                        }

                        if (clone.IsReady)
                        {
                            clones.Add(clone);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (request.Verbose)
                        {
                            Console.WriteLine($"[txc] Warning: Failed to create clone connection {i + 1}: {ex.Message}");
                        }
                    }
                }

                if (clones.Count > 0)
                {
                    // Set ImportConnections via reflection (same type mismatch as CrmConnection).
                    var importConnProp = handler.GetType().GetProperty("ImportConnections");
                    importConnProp?.SetValue(handler, clones);
                    Console.WriteLine($"[txc] Using {clones.Count + 1} parallel connections for import.");
                }
            }

            // 6. Wire CMT console trace logging.
            TryWireCmtConsoleLogging(handler, request.Verbose);

            // 7. Validate schema.
            Console.WriteLine("Running Schema Validation...");
            bool schemaValid = handler.ValidateSchemaFile(workingFolder);

            if (!schemaValid)
            {
                return new CmtImportResult(false, "Schema validation failed. Check the data package schema against the target environment.");
            }

            Console.WriteLine("Schema Validation Complete.");

            // 8. Import data (synchronous — blocks via internal WaitOne).
            Console.WriteLine("Starting data import...");
            await Task.Run(() => handler.ImportDataToCrm(workingFolder, deleteBeforeAdd: false));

            // CMT often swallows exceptions internally and only reports
            // failures through progress events. Check whether any stages
            // were reported as failed.
            if (_failedStageCount > 0)
            {
                return new CmtImportResult(false,
                    $"Data import completed with {_failedStageCount} failed stage(s). See console output above for details.");
            }

            Console.WriteLine("Data import completed successfully.");
            return new CmtImportResult(true, null);
        }
        catch (Exception ex)
        {
            string message = ex.InnerException?.Message ?? ex.Message;
            return new CmtImportResult(false, $"Data import failed: {message}");
        }
        finally
        {
            authHook?.Dispose();
            crmServiceClient?.Dispose();

            AppDomain.CurrentDomain.AssemblyResolve -= OnResolveAssembly;
            AssemblyLoadContext.Default.Resolving -= OnResolveAssemblyLoadContext;

            // Clean up extracted working directory.
            if (workingFolder is not null)
            {
                try { Directory.Delete(workingFolder, recursive: true); } catch { }
            }

            if (_unresolvedAssemblies.Count > 0 && request.Verbose)
            {
                Console.WriteLine($"[txc] Unresolved assemblies during CMT import: {string.Join(", ", _unresolvedAssemblies)}");
            }
        }
    }

    /// <summary>
    /// Wires a <see cref="ConsoleTraceListener"/> to CMT's internal
    /// TraceSource so that import progress and errors are visible in
    /// the console output in real time.
    /// </summary>
    private static void TryWireCmtConsoleLogging(ImportCrmDataHandler handler, bool verbose)
    {
        try
        {
            // handler.Logger is a DataMigCommon.Utility.TraceLogger.
            // It exposes a TraceSource via the base class hierarchy.
            object? logger = handler.GetType()
                .GetProperty("Logger", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(handler);

            if (logger is null)
            {
                if (verbose)
                    Console.WriteLine("[txc] CMT Logger property not found — console logging skipped.");
                return;
            }

            // Try AddTraceListener(TraceListener) — available on TraceLogger.
            MethodInfo? addListener = logger.GetType()
                .GetMethod("AddTraceListener", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(TraceListener) }, null);

            if (addListener is not null)
            {
                addListener.Invoke(logger, new object[] { new ConsoleTraceListener() });
                if (verbose)
                    Console.WriteLine("[txc] CMT console trace listener wired.");
            }
            else if (verbose)
            {
                Console.WriteLine("[txc] AddTraceListener method not found on CMT Logger.");
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to wire CMT console logging: {ex.Message}");
        }
    }

    private void OnAddNewProgressItem(object? sender, ProgressItemEventArgs e)
    {
        OnUpdateProgressItem(sender, e);
    }

    private void OnUpdateProgressItem(object? sender, ProgressItemEventArgs e)
    {
        if (e.progressItem is null)
            return;

        string message = e.progressItem.ItemText ?? string.Empty;
        switch (e.progressItem.ItemStatus)
        {
            case ProgressItemStatus.Complete:
                Console.WriteLine($"{message} - Complete");
                break;
            case ProgressItemStatus.Failed:
                Interlocked.Increment(ref _failedStageCount);
                Console.Error.WriteLine($"{message} - Stage Failed");
                break;
            case ProgressItemStatus.Warning:
                Console.Error.WriteLine($"Warning: {message}");
                break;
            default:
                Console.WriteLine(message);
                break;
        }
    }

    /// <summary>
    /// Registers the extracted data package directory for instance-level
    /// assembly probing, so that any DLLs inside the package can be resolved.
    /// </summary>
    private void RegisterExtractedDirectoryForProbing(string directory)
    {
        foreach (string dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                LegacyAssemblyRuntime.PatchDispatcherReferencesOnDisk(dll);
            }
            catch { }
        }
    }

    private Assembly? OnResolveAssembly(object? sender, ResolveEventArgs args)
    {
        string key = (args.Name ?? "<unknown>").Split(',')[0];
        return InstanceResolve(key);
    }

    private Assembly? OnResolveAssemblyLoadContext(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        string key = assemblyName.Name ?? "<unknown>";
        return InstanceResolve(key);
    }

    private Assembly? InstanceResolve(string key)
    {
        if (_assemblyMap.TryGetValue(key, out Assembly? cached))
            return cached;

        // Probe extracted directory (if any DLLs match the name).
        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, key + ".dll");

        if (File.Exists(candidate))
        {
            try
            {
                Assembly loaded = Assembly.LoadFrom(candidate);
                _assemblyMap[key] = loaded;
                return loaded;
            }
            catch { }
        }

        _unresolvedAssemblies.Add(key);
        return null;
    }
}
