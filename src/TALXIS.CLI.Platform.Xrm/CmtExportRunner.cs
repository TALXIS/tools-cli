using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Tooling.Dmt.DataMigCommon.Utility;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// Standalone CMT data export runner — uses <c>ExportCrmDataHandler</c>
/// directly. Mirrors the approach of <see cref="CmtImportRunner"/>.
///
/// Shared infrastructure (Cecil patching, assembly resolvers) is provided
/// by <see cref="LegacyAssemblyRuntime"/>.
/// </summary>
public sealed class CmtExportRunner
{
    static CmtExportRunner()
    {
        LegacyAssemblyRuntime.EnsureInitialized();
    }

    private readonly Dictionary<string, Assembly> _assemblyMap;
    private readonly HashSet<string> _unresolvedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(CmtExportRunner));

    /// <summary>
    /// Tracks the number of failed stages reported by CMT progress events.
    /// Checked after <c>ExportData</c> returns to detect silent failures.
    /// </summary>
    private int _failedStageCount;

    public CmtExportRunner()
    {
        _assemblyMap = new Dictionary<string, Assembly>(
            LegacyAssemblyRuntime.StaticAssemblyMap,
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<CmtExportResult> RunAsync(CmtExportRequest request, string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return RunInternalAsync(request, () => new CrmServiceClient(connectionString), cancellationToken);
    }

    /// <summary>
    /// Token-provider overload matching
    /// <see cref="CmtImportRunner.RunAsync(CmtImportRequest, Uri, Func{string, Task{string}}, CancellationToken)"/>.
    /// </summary>
    public Task<CmtExportResult> RunAsync(
        CmtExportRequest request,
        Uri environmentUrl,
        Func<string, Task<string>> tokenProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(environmentUrl);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        if (!environmentUrl.IsAbsoluteUri)
            throw new ArgumentException($"Environment URL '{environmentUrl}' must be absolute.", nameof(environmentUrl));

        return RunInternalAsync(
            request,
            () => new CrmServiceClient(environmentUrl, tokenProvider, useUniqueInstance: true),
            cancellationToken);
    }

    private async Task<CmtExportResult> RunInternalAsync(
        CmtExportRequest request,
        Func<CrmServiceClient> clientFactory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.SchemaPath))
        {
            return new CmtExportResult(false, $"Schema file not found: '{request.SchemaPath}'");
        }

        AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;
        AssemblyLoadContext.Default.Resolving += OnResolveAssemblyLoadContext;

        CrmServiceClient? crmServiceClient = null;

        try
        {
            // 1. Connect to Dataverse.
            crmServiceClient = clientFactory();

            if (!crmServiceClient.IsReady)
            {
                string error = crmServiceClient.LastCrmException?.Message ?? crmServiceClient.LastCrmError ?? "Unknown connection error.";
                return new CmtExportResult(false, error);
            }

            if (request.Verbose)
            {
                _logger.LogInformation("Connected to: {Url}", crmServiceClient.ConnectedOrgUriActual);
                _logger.LogInformation("Organization version: {Version}", crmServiceClient.ConnectedOrgVersion);
            }

            // 2. Create ExportCrmDataHandler via reflection.
            // The ExportProcessor assembly ships as a content file (not a
            // compile-time reference) to avoid eager assembly resolution in
            // the parent process. LegacyAssemblyRuntime pre-loads it at
            // initialization, and we instantiate the handler reflectively.
            object handler = CreateExportHandler(crmServiceClient);

            // 3. Wire progress event handlers via reflection.
            TryWireProgressEvents(handler);

            // 4. Wire CMT console trace logging.
            TryWireCmtConsoleLogging(handler, request.Verbose);

            // 5. Enable file column export if requested.
            // The handler reads this from AppSettings, but we set the internal
            // field directly since we don't control AppSettings in the subprocess.
            if (request.ExportFiles)
            {
                TrySetExportFilesEnabled(handler);
            }

            // 6. Export data (synchronous — same as PAC CLI pattern).
            _logger.LogInformation("Starting data export...");

            MethodInfo exportMethod = handler.GetType()
                .GetMethod("ExportData", new[] { typeof(string), typeof(string) })
                ?? throw new InvalidOperationException("ExportCrmDataHandler.ExportData(string, string) method not found.");

            bool success = await Task.Run(() =>
            {
                object? result = exportMethod.Invoke(handler, new object[] { request.SchemaPath, request.OutputPath });
                return result is true;
            });

            if (!success || _failedStageCount > 0)
            {
                return new CmtExportResult(false,
                    _failedStageCount > 0
                        ? $"Data export completed with {_failedStageCount} failed stage(s). See console output above for details."
                        : "Data export failed. Check console output for details.");
            }

            _logger.LogInformation("Data export completed successfully.");
            return new CmtExportResult(true, null);
        }
        catch (Exception ex)
        {
            string message = ex.InnerException?.Message ?? ex.Message;
            return new CmtExportResult(false, $"Data export failed: {message}");
        }
        finally
        {
            crmServiceClient?.Dispose();

            AppDomain.CurrentDomain.AssemblyResolve -= OnResolveAssembly;
            AssemblyLoadContext.Default.Resolving -= OnResolveAssemblyLoadContext;

            if (_unresolvedAssemblies.Count > 0 && request.Verbose)
            {
                _logger.LogDebug("Unresolved assemblies during CMT export: {Assemblies}", string.Join(", ", _unresolvedAssemblies));
            }
        }
    }

    /// <summary>
    /// Attempts to enable the ExportFiles feature on the handler by setting the
    /// internal <c>_isExportFilesEnabled</c> field via reflection. The handler
    /// normally reads this from AppSettings but we don't control that in the
    /// subprocess environment.
    /// </summary>
    /// <summary>
    /// Loads and instantiates <c>ExportCrmDataHandler</c> from the
    /// <c>Microsoft.Xrm.Tooling.Dmt.ExportProcessor</c> assembly via
    /// reflection. The assembly is pre-loaded by <see cref="LegacyAssemblyRuntime"/>.
    /// </summary>
    private object CreateExportHandler(CrmServiceClient crmServiceClient)
    {
        // Load the ExportProcessor assembly — it may already be in the
        // static map, or it may be resolved lazily by the resolver chain.
        Assembly? exportAssembly = null;
        try
        {
            exportAssembly = Assembly.Load("Microsoft.Xrm.Tooling.Dmt.ExportProcessor");
        }
        catch (FileNotFoundException)
        {
        }

        if (exportAssembly is null)
        {
            // Fallback: probe the base directory explicitly.
            string candidate = Path.Combine(AppContext.BaseDirectory, "Microsoft.Xrm.Tooling.Dmt.ExportProcessor.dll");
            if (File.Exists(candidate))
            {
                exportAssembly = Assembly.LoadFrom(candidate);
            }
        }

        Type handlerType = exportAssembly
            ?.GetType("Microsoft.Xrm.Tooling.Dmt.ExportProcessor.DataInteraction.ExportCrmDataHandler")
            ?? throw new InvalidOperationException(
                "Could not find ExportCrmDataHandler. Ensure the CMT ExportProcessor DLL is available.");

        // Constructor takes CrmServiceClient — use Activator to bypass
        // the strong-name version mismatch with the legacy v4.0.0.0 type.
        object handler = Activator.CreateInstance(handlerType, crmServiceClient)
            ?? throw new InvalidOperationException("Failed to create ExportCrmDataHandler instance.");

        return handler;
    }

    /// <summary>
    /// Wires the <c>AddNewProgressItem</c> and <c>UpdateProgressItem</c>
    /// events on the handler via reflection.
    /// </summary>
    private void TryWireProgressEvents(object handler)
    {
        try
        {
            var addNewEvent = handler.GetType().GetEvent("AddNewProgressItem");
            var updateEvent = handler.GetType().GetEvent("UpdateProgressItem");

            EventHandler<ProgressItemEventArgs> onAdd = OnAddNewProgressItem;
            EventHandler<ProgressItemEventArgs> onUpdate = OnUpdateProgressItem;

            addNewEvent?.AddEventHandler(handler, onAdd);
            updateEvent?.AddEventHandler(handler, onUpdate);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not wire progress events: {Error}", ex.Message);
        }
    }

    private void TrySetExportFilesEnabled(object handler)
    {
        try
        {
            var field = handler.GetType().GetField("_isExportFilesEnabled",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(handler, true);
                _logger.LogInformation("File column export enabled.");
            }
            else
            {
                _logger.LogWarning("Could not enable file column export — internal field not found.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not enable file column export: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Wires a <see cref="ConsoleTraceListener"/> to CMT's internal
    /// TraceSource so that export progress and errors are visible in
    /// the console output in real time, and sets the trace level based
    /// on the <paramref name="verbose"/> flag.
    /// </summary>
    private void TryWireCmtConsoleLogging(object handler, bool verbose)
    {
        try
        {
            object? logger = handler.GetType()
                .GetProperty("Logger", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(handler);

            if (logger is null)
            {
                if (verbose)
                    _logger.LogDebug("CMT Logger property not found — console logging skipped");
                return;
            }

            // Set trace level — Verbose when --verbose, Information otherwise.
            // Without this, CMT's TraceLogger may filter out lower-level messages.
            MethodInfo? setLevel = logger.GetType()
                .GetMethod("SetTraceLevel", BindingFlags.Public | BindingFlags.Instance);
            if (setLevel is not null)
            {
                SourceLevels level = verbose ? SourceLevels.Verbose : SourceLevels.Information;
                setLevel.Invoke(logger, new object[] { level });
            }

            MethodInfo? addListener = logger.GetType()
                .GetMethod("AddTraceListener", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(TraceListener) }, null);

            if (addListener is not null)
            {
                addListener.Invoke(logger, new object[] { new ConsoleTraceListener() });
                if (verbose)
                    _logger.LogDebug("CMT console trace listener wired (level: Verbose)");
            }
            else if (verbose)
            {
                _logger.LogDebug("AddTraceListener method not found on CMT Logger");
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
                _logger.LogInformation("{Message} - Complete", message);
                break;
            case ProgressItemStatus.Failed:
                Interlocked.Increment(ref _failedStageCount);
                _logger.LogError("{Message} - Stage Failed", message);
                break;
            case ProgressItemStatus.Warning:
                _logger.LogWarning("{Message}", message);
                break;
            default:
                _logger.LogInformation("{Message}", message);
                break;
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
