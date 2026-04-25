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
/// from <c>Microsoft.Xrm.Tooling.Dmt.ExportProcessor</c> via reflection.
/// Mirrors the approach of <see cref="CmtImportRunner"/>.
///
/// The ExportProcessor assembly is not available at compile time (it is
/// resolved at runtime through <see cref="LegacyAssemblyRuntime"/> and
/// the instance assembly resolver), so all interaction with
/// <c>ExportCrmDataHandler</c> is performed reflectively.
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

            // 2. Load ExportCrmDataHandler via reflection.
            // The ExportProcessor assembly is resolved at runtime through the
            // legacy assembly resolver infrastructure rather than being available
            // as a compile-time reference.
            object handler = CreateExportHandler(crmServiceClient);

            // 3. Wire progress event handlers via reflection.
            TryWireProgressEvents(handler);

            // 4. Wire CMT console trace logging.
            TryWireCmtConsoleLogging(handler, request.Verbose);

            // 5. Enable file column export if requested.
            if (request.ExportFiles)
            {
                TrySetExportFilesEnabled(handler);
            }

            // 6. Export data.
            _logger.LogInformation("Starting data export...");

            MethodInfo? exportMethod = handler.GetType()
                .GetMethod("ExportData", BindingFlags.Public | BindingFlags.Instance);

            if (exportMethod is null)
            {
                return new CmtExportResult(false, "ExportCrmDataHandler.ExportData method not found.");
            }

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
    /// Loads and instantiates <c>ExportCrmDataHandler</c> from the
    /// <c>Microsoft.Xrm.Tooling.Dmt.ExportProcessor</c> assembly via
    /// reflection, then sets the <c>CrmConnection</c> property.
    /// </summary>
    private object CreateExportHandler(CrmServiceClient crmServiceClient)
    {
        // Try to load the ExportProcessor assembly by name — the legacy
        // runtime assembly resolver will locate it.
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

        if (exportAssembly is null)
        {
            throw new InvalidOperationException(
                "Could not load assembly 'Microsoft.Xrm.Tooling.Dmt.ExportProcessor'. " +
                "Ensure the CMT ExportProcessor DLL is available in the application base directory.");
        }

        Type handlerType = exportAssembly.GetType("Microsoft.Xrm.Tooling.Dmt.ExportProcessor.DataInteraction.ExportCrmDataHandler")
            ?? throw new InvalidOperationException("ExportCrmDataHandler type not found in ExportProcessor assembly.");

        // Create the handler — try constructor that accepts CrmServiceClient first.
        object? handler = null;
        try
        {
            handler = Activator.CreateInstance(handlerType, crmServiceClient);
        }
        catch (MissingMethodException)
        {
            // Fall back to default constructor and set CrmConnection via reflection.
            handler = Activator.CreateInstance(handlerType);
        }

        if (handler is null)
        {
            throw new InvalidOperationException("Failed to create ExportCrmDataHandler instance.");
        }

        // Set CrmConnection property via reflection (same pattern as CmtImportRunner).
        // The property type is the legacy strong-named CrmServiceClient, so we
        // set it reflectively to avoid type mismatch issues.
        var crmConnProp = handlerType.GetProperty("CrmConnection");
        if (crmConnProp is not null)
        {
            crmConnProp.SetValue(handler, crmServiceClient);
        }

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

    /// <summary>
    /// Attempts to enable the ExportFiles feature on the handler by setting the
    /// internal <c>_isExportFilesEnabled</c> field via reflection. The handler
    /// normally reads this from AppSettings but we don't control that in the
    /// subprocess environment.
    /// </summary>
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

            MethodInfo? addListener = logger.GetType()
                .GetMethod("AddTraceListener", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(TraceListener) }, null);

            if (addListener is not null)
            {
                addListener.Invoke(logger, new object[] { new ConsoleTraceListener() });
                if (verbose)
                    _logger.LogDebug("CMT console trace listener wired");
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
