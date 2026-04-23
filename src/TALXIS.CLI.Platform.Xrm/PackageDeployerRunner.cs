using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Tooling.CrmConnectControl.Utility;
using Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageCore.ImportCode;
using Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageCore.Models;
using Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageExtentionBase;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Platform.Xrm;

public sealed class PackageDeployerRunner
{
    static PackageDeployerRunner()
    {
        LegacyAssemblyRuntime.EnsureInitialized();
    }

    public Task<PackageDeployerResult> RunAsync(PackageDeployerRequest request, string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return Task.Run(() => Run(request, () => new CrmServiceClient(connectionString)), cancellationToken);
    }

    /// <summary>
    /// Token-provider overload. Prefer this for every credential kind except
    /// explicit connection-string scenarios: it uses the modern
    /// <c>ServiceClient(Uri, Func&lt;string, Task&lt;string&gt;&gt;, ...)</c>
    /// constructor so InteractiveBrowser, ClientCertificate, and federated
    /// credentials all work without inlining secrets anywhere.
    /// </summary>
    public Task<PackageDeployerResult> RunAsync(
        PackageDeployerRequest request,
        Uri environmentUrl,
        Func<string, Task<string>> tokenProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(environmentUrl);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        if (!environmentUrl.IsAbsoluteUri)
            throw new ArgumentException($"Environment URL '{environmentUrl}' must be absolute.", nameof(environmentUrl));

        // A newly-constructed CrmServiceClient binds its token callback at
        // construction time, so there is no static state to restore around
        // the call — two concurrent deployments remain fully isolated.
        return Task.Run(
            () => Run(request, () => new CrmServiceClient(environmentUrl, tokenProvider, useUniqueInstance: true)),
            cancellationToken);
    }

    private static PackageDeployerResult Run(PackageDeployerRequest request, Func<CrmServiceClient> clientFactory)
    {
        using ManagedPackageDeployerHost host = new(request, clientFactory);
        return host.Run();
    }

    private sealed class ManagedPackageDeployerHost : IDisposable
    {
        private readonly PackageDeployerRequest _request;
        private readonly Func<CrmServiceClient> _clientFactory;
        private readonly ILogger _logger;
        private readonly Dictionary<string, Assembly> _assemblyMap;
        private readonly HashSet<string> _unresolvedAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly ManualResetEventSlim _workComplete = new();
        private readonly List<string> _failureDetails = [];
        private readonly object _failureDetailsLock = new();

        private string? _errorMessage;
        private string? _effectiveLogFilePath;
        private string? _effectiveCmtLogFilePath;
        private string? _packagePathForCoreObjects;
        private string? _searchPathForCoreObjects;
        private string? _extractedDirectory;
        private string? _temporaryArtifactsDirectory;
        private TraceSource? _traceSource;
        private FileStream? _logFile;
        private BaseImportCustomizations? _import;
        private CoreObjects? _coreObjects;

        public ManagedPackageDeployerHost(PackageDeployerRequest request, Func<CrmServiceClient> clientFactory)
        {
            _request = request;
            _clientFactory = clientFactory;
            _logger = TxcLoggerFactory.CreateLogger(nameof(PackageDeployerRunner));

            // Instance map starts as a copy of the static map — the instance
            // resolver adds extracted-directory probing and unresolved tracking.
            _assemblyMap = new Dictionary<string, Assembly>(LegacyAssemblyRuntime.StaticAssemblyMap, StringComparer.OrdinalIgnoreCase);

            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;
            AssemblyLoadContext.Default.Resolving += OnResolveAssemblyLoadContext;
        }

        public PackageDeployerResult Run()
        {
            if (!File.Exists(_request.PackagePath))
            {
                throw new InvalidOperationException($"Deployable package '{_request.PackagePath}' does not exist.");
            }

            PreparePackageInputs();

            TraceLogger traceLogger = SetupLogging();

            CrmServiceClient? crmServiceClient = null;
            try
            {
                crmServiceClient = _clientFactory();

                if (!crmServiceClient.IsReady)
                {
                    return new PackageDeployerResult(
                        false,
                        crmServiceClient.LastCrmException?.Message ?? crmServiceClient.LastCrmError,
                        _effectiveLogFilePath,
                        _effectiveCmtLogFilePath,
                        _temporaryArtifactsDirectory);
                }

                if (_request.Verbose)
                {
                    _logger.LogInformation("Connected to: {Url}", crmServiceClient.ConnectedOrgUriActual);
                    _logger.LogInformation("Organization version: {Version}", crmServiceClient.ConnectedOrgVersion);
                    _logger.LogInformation("Organization ID: {OrgId}", crmServiceClient.ConnectedOrgId);
                }

                _coreObjects = new CoreObjects(
                    targetSearchPath: _searchPathForCoreObjects,
                    sourcePackageAssemblyPath: _packagePathForCoreObjects,
                    allowPackageCodeExecution: true,
                    forceSyncExecution: false,
                    packageInfo: BuildPackageInfo(_request),
                    logger: traceLogger,
                    allowAsyncRibbonProcessing: false,
                    correlationId: Guid.NewGuid());

                // CoreObjects constructor may reset EnabledInMemoryLogCapture
                // to false via its InMemoryLogCollectionEnabled default. Re-set
                // it so that GetAllLogsAsStringList() has data to collect.
                traceLogger.EnabledInMemoryLogCapture = true;
                try
                {
                    var inMemProp = _coreObjects.GetType().GetProperty("InMemoryLogCollectionEnabled");
                    inMemProp?.SetValue(_coreObjects, true);
                }
                catch { /* best-effort */ }

                if (!string.IsNullOrWhiteSpace(_request.Settings))
                {
                    _coreObjects.ParseRuntimeSettingsDelimitedString(_request.Settings);
                }

                // Use reflection to set CrmSvc — the property type is
                // CrmServiceClient from the strong-named legacy assembly, but at
                // runtime our shim satisfies the reference via the assembly resolver.
                var crmSvcProp = _coreObjects.GetType().GetProperty("CrmSvc")
                    ?? throw new InvalidOperationException("CoreObjects.CrmSvc property not found.");
                crmSvcProp.SetValue(_coreObjects, crmServiceClient);

                PackageImportConfigurationParser parser = new(_coreObjects);
                parser.ConfigReadComplete += Parser_ConfigReadComplete;
                parser.AddNewProgressItem += Parser_AddNewProgressItem;
                parser.UpdateProgressItem += Parser_UpdateProgressItem;

                // Capture first-chance exceptions during PD/CMT execution.
                // CMT often swallows exceptions internally, logging only
                // "Stage Failed" — this handler captures the real error.
                var capturedExceptions = new List<string>();
                EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs>? exHandler = null;
                if (_request.Verbose)
                {
                    exHandler = (_, args) =>
                    {
                        var ex = args.Exception;
                        // Filter noise: skip common benign exceptions
                        if (ex is System.IO.FileNotFoundException or
                            System.IO.FileLoadException or
                            System.TypeLoadException or
                            System.Reflection.ReflectionTypeLoadException)
                        {
                            return;
                        }
                        string msg = $"[FirstChance] {ex.GetType().Name}: {ex.Message}";
                        lock (capturedExceptions)
                        {
                            if (capturedExceptions.Count < 50)
                                capturedExceptions.Add(msg);
                        }
                    };
                    AppDomain.CurrentDomain.FirstChanceException += exHandler;
                }

                try
                {
                    parser.ReadConfig();
                }
                catch (Exception ex)
                {
                    TryPersistCmtLog();
                    return new PackageDeployerResult(
                        false,
                        ex.Message,
                        _effectiveLogFilePath,
                        _effectiveCmtLogFilePath,
                        _temporaryArtifactsDirectory);
                }
                finally
                {
                    if (exHandler is not null)
                        AppDomain.CurrentDomain.FirstChanceException -= exHandler;
                }

                TimeSpan workTimeout = ResolveWorkTimeout();
                bool signaled = workTimeout == Timeout.InfiniteTimeSpan
                    ? _workComplete.Wait(Timeout.InfiniteTimeSpan)
                    : _workComplete.Wait(workTimeout);
                if (!signaled)
                {
                    _errorMessage = $"Package Deployer did not signal completion within {workTimeout.TotalMinutes:0} minutes (TXC_DEPLOY_TIMEOUT_MINUTES). " +
                        "Captured failure details: " + FormatFailureDetails();
                }

                // Dump captured first-chance exceptions if verbose
                if (capturedExceptions.Count > 0)
                {
                    _logger.LogWarning("{Count} first-chance exception(s) during deploy", capturedExceptions.Count);
                    foreach (var msg in capturedExceptions)
                        _logger.LogWarning("  {Exception}", msg);
                }

                TryPersistCmtLog();
                TryDumpImportLogErrors();
                return new PackageDeployerResult(
                    string.IsNullOrWhiteSpace(_errorMessage),
                    _errorMessage,
                    _effectiveLogFilePath,
                    _effectiveCmtLogFilePath,
                    _temporaryArtifactsDirectory);
            }
            finally
            {
                CrmServiceClient.AuthOverrideHook = null;
                crmServiceClient?.Dispose();
            }
        }

        private void PreparePackageInputs()
        {
            if (LooksLikeZipArchive(_request.PackagePath))
            {
                _extractedDirectory = Path.Combine(GetTemporaryArtifactsDirectory(), "package");
                Directory.CreateDirectory(_extractedDirectory);
                ZipFile.ExtractToDirectory(_request.PackagePath, _extractedDirectory, overwriteFiles: true);
                _searchPathForCoreObjects = _extractedDirectory;
                _packagePathForCoreObjects = null;
                return;
            }

            _packagePathForCoreObjects = _request.PackagePath;
            _searchPathForCoreObjects = Path.GetDirectoryName(_request.PackagePath);
        }

        private TraceLogger SetupLogging()
        {
            SourceLevels level = _request.Verbose ? SourceLevels.Verbose : SourceLevels.Information;

            _traceSource = new TraceSource("txc-package-deployer")
            {
                Switch = { Level = level }
            };

            Microsoft.Xrm.Tooling.CrmConnectControl.Utility.TraceControlSettings.TraceLevel = level;
            Microsoft.Xrm.Tooling.Connector.TraceControlSettings.TraceLevel = level == SourceLevels.Information
                ? SourceLevels.Warning
                : level;

            if (_request.LogConsole)
            {
                ConsoleTraceListener consoleListener = new();
                _traceSource.Listeners.Add(consoleListener);
                Microsoft.Xrm.Tooling.CrmConnectControl.Utility.TraceControlSettings.AddTraceListener(consoleListener);
                Microsoft.Xrm.Tooling.Connector.TraceControlSettings.AddTraceListener(consoleListener);
            }

            string logFilePath = ResolveEffectiveLogFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
            _logFile = File.Open(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _effectiveLogFilePath = logFilePath;
            TextWriterTraceListener fileListener = new(_logFile);
            _traceSource.Listeners.Add(fileListener);
            Microsoft.Xrm.Tooling.CrmConnectControl.Utility.TraceControlSettings.AddTraceListener(fileListener);
            Microsoft.Xrm.Tooling.Connector.TraceControlSettings.AddTraceListener(fileListener);

            return new TraceLogger(_traceSource)
            {
                EnabledInMemoryLogCapture = true,
                LogRetentionDuration = TimeSpan.FromMinutes(30)
            };
        }

        private string ResolveEffectiveLogFilePath()
        {
            if (!string.IsNullOrWhiteSpace(_request.LogFile))
            {
                return Path.GetFullPath(_request.LogFile);
            }

            string logsDirectory = Path.Combine(GetTemporaryArtifactsDirectory(), "logs");
            string packageName = Path.GetFileNameWithoutExtension(_request.PackagePath);
            return Path.Combine(logsDirectory, $"{packageName}-{Guid.NewGuid():N}.log");
        }

        private string ResolveEffectiveCmtLogFilePath()
        {
            if (!string.IsNullOrWhiteSpace(_effectiveCmtLogFilePath))
            {
                return _effectiveCmtLogFilePath;
            }

            string logsDirectory = !string.IsNullOrWhiteSpace(_effectiveLogFilePath)
                ? Path.GetDirectoryName(_effectiveLogFilePath!)!
                : Path.Combine(GetTemporaryArtifactsDirectory(), "logs");

            string packageName = Path.GetFileNameWithoutExtension(_request.PackagePath);
            _effectiveCmtLogFilePath = Path.Combine(logsDirectory, $"{packageName}-cmt-{Guid.NewGuid():N}.log");
            return _effectiveCmtLogFilePath;
        }

        private string GetTemporaryArtifactsDirectory()
        {
            if (string.IsNullOrWhiteSpace(_temporaryArtifactsDirectory))
            {
                _temporaryArtifactsDirectory = !string.IsNullOrWhiteSpace(_request.TemporaryArtifactsDirectory)
                    ? Path.GetFullPath(_request.TemporaryArtifactsDirectory)
                    : Path.Combine(
                        Path.GetTempPath(),
                        "txc",
                        "package-deployer-host",
                        Guid.NewGuid().ToString("N"));
            }

            Directory.CreateDirectory(_temporaryArtifactsDirectory);
            return _temporaryArtifactsDirectory;
        }

        private void TryPersistCmtLog()
        {
            if (_import is null)
            {
                return;
            }

            string cmtLogFilePath = ResolveEffectiveCmtLogFilePath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cmtLogFilePath)!);

                // Step 1: Try GetAllLogsAsStringList — combines PD logger,
                // CrmServiceClient logs, and CMT parser logger.
                try
                {
                    string[] allLogs = _import.GetAllLogsAsStringList();
                    if (allLogs.Length > 0)
                    {
                        File.WriteAllLines(cmtLogFilePath, allLogs);
                    }
                }
                catch (Exception ex)
                {
                    Exception root = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                    _traceSource?.TraceEvent(TraceEventType.Warning, 0,
                        $"GetAllLogsAsStringList failed: {root.GetType().Name}: {root.Message}{System.Environment.NewLine}{root.StackTrace}");

                    // Step 1b: Fallback — read TraceLogger.Logs ConcurrentQueue directly
                    TryWriteTraceLoggerLogsDirect(cmtLogFilePath);
                }

                // Step 2: Try to discover CMT's own log file path
                object? parser = _import.GetType()
                    .GetField("_parser", BindingFlags.Instance | BindingFlags.NonPublic)?
                    .GetValue(_import);

                object? parserLogger = parser?.GetType()
                    .GetProperty("Logger", BindingFlags.Instance | BindingFlags.Public)?
                    .GetValue(parser);

                string? discoveredLogPath = null;
                try
                {
                    discoveredLogPath = parserLogger?.GetType()
                        .GetMethod("GetLogFilePath", BindingFlags.Instance | BindingFlags.Public)?
                        .Invoke(parserLogger, null) as string;
                }
                catch (Exception ex)
                {
                    Exception root = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                    _traceSource?.TraceEvent(TraceEventType.Warning, 0,
                        $"GetLogFilePath failed: {root.GetType().Name}: {root.Message}");
                }

                if (!string.IsNullOrWhiteSpace(discoveredLogPath) && File.Exists(discoveredLogPath))
                {
                    _effectiveCmtLogFilePath = discoveredLogPath;
                    return;
                }

                // Step 3: Try WriteOutLogToFile for richer import detail log
                object? importLog = parser?.GetType()
                    .GetProperty("ImportLog", BindingFlags.Instance | BindingFlags.Public)?
                    .GetValue(parser);

                MethodInfo? writeOutLogMethod = importLog?.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method =>
                        method.Name == "WriteOutLogToFile" &&
                        method.GetParameters().Length == 2);

                if (writeOutLogMethod is not null && parserLogger is not null)
                {
                    try
                    {
                        writeOutLogMethod.Invoke(importLog, [cmtLogFilePath, parserLogger]);
                    }
                    catch (TargetInvocationException ex)
                    {
                        Exception rootCause = ex.InnerException ?? ex;
                        _traceSource?.TraceEvent(TraceEventType.Warning, 0,
                            $"WriteOutLogToFile failed: {rootCause.GetType().Name}: {rootCause.Message}{System.Environment.NewLine}{rootCause.StackTrace}");
                    }
                }

                _effectiveCmtLogFilePath = File.Exists(cmtLogFilePath) ? cmtLogFilePath : null;
            }
            catch (Exception ex)
            {
                _effectiveCmtLogFilePath = File.Exists(cmtLogFilePath) ? cmtLogFilePath : null;
                Exception root = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                _traceSource?.TraceEvent(TraceEventType.Warning, 0,
                    $"Failed to persist CMT log: {root.GetType().Name}: {root.Message}{System.Environment.NewLine}{root.StackTrace}");
            }
        }

        /// <summary>
        /// Directly reads the TraceLogger.Logs ConcurrentQueue via reflection
        /// and writes entries to a file, bypassing GetAllLogsAsStringList.
        /// </summary>
        private void TryWriteTraceLoggerLogsDirect(string filePath)
        {
            try
            {
                // Access CoreObjects.Logger.Logs (ConcurrentQueue<Tuple<DateTime, string>>)
                var logger = _coreObjects?.GetType()
                    .GetProperty("Logger", BindingFlags.Instance | BindingFlags.Public)?
                    .GetValue(_coreObjects);

                var logsProperty = logger?.GetType()
                    .GetProperty("Logs", BindingFlags.Instance | BindingFlags.Public);

                if (logsProperty?.GetValue(logger) is System.Collections.IEnumerable logs)
                {
                    var lines = new List<string>();
                    foreach (var entry in logs)
                    {
                        lines.Add(entry?.ToString() ?? string.Empty);
                    }

                    if (lines.Count > 0)
                    {
                        File.WriteAllLines(filePath, lines);
                    }
                }
            }
            catch (Exception ex)
            {
                _traceSource?.TraceEvent(TraceEventType.Warning, 0,
                    $"Direct TraceLogger.Logs fallback failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Dumps ImportResultsLog error details to stderr via reflection.
        /// CMT's ImportResultsLog tracks per-entity/per-record results
        /// including exceptions that are otherwise swallowed.
        /// </summary>
        private void TryDumpImportLogErrors()
        {
            try
            {
                object? parser = _import?.GetType()
                    .GetField("_parser", BindingFlags.Instance | BindingFlags.NonPublic)?
                    .GetValue(_import);

                object? importLog = parser?.GetType()
                    .GetProperty("ImportLog", BindingFlags.Instance | BindingFlags.Public)?
                    .GetValue(parser);

                if (importLog is null) return;

                // Try to enumerate any collection-like properties on ImportResultsLog
                // that might contain error entries (ErrorLog, FailureLog, Results, etc.)
                foreach (var prop in importLog.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (prop.GetValue(importLog) is System.Collections.IEnumerable collection and not string)
                    {
                        foreach (var item in collection)
                        {
                            string? itemStr = item?.ToString();
                            if (!string.IsNullOrWhiteSpace(itemStr) &&
                                (itemStr.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                                 itemStr.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                 itemStr.Contains("exception", StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogError("[CMT ImportLog] {Property}: {Content}", prop.Name, itemStr);
                            }
                        }
                    }
                }

                // Also dump the Logger's LastError/LastException if any
                object? parserLogger = parser?.GetType()
                    .GetProperty("Logger", BindingFlags.Instance | BindingFlags.Public)?
                    .GetValue(parser);

                if (parserLogger is not null)
                {
                    var lastErrorProp = parserLogger.GetType().GetProperty("LastError", BindingFlags.Instance | BindingFlags.Public);
                    var lastExProp = parserLogger.GetType().GetProperty("LastException", BindingFlags.Instance | BindingFlags.Public);

                    string? lastError = lastErrorProp?.GetValue(parserLogger)?.ToString();
                    object? lastEx = lastExProp?.GetValue(parserLogger);

                    if (!string.IsNullOrWhiteSpace(lastError))
                        _logger.LogError("[CMT Logger.LastError] {Error}", lastError);
                    if (lastEx is not null)
                        _logger.LogError("[CMT Logger.LastException] {Exception}", lastEx);
                }
            }
            catch (Exception ex)
            {
                _traceSource?.TraceEvent(TraceEventType.Warning, 0,
                    $"TryDumpImportLogErrors failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Wires a ConsoleTraceListener into CMT's internal TraceSource so
        /// data import progress is emitted to the console in real time.
        /// Mirrors how PAC CLI's ListenToDataMigrationLogging works:
        /// it accesses _parser.Logger and calls AddTraceListener.
        /// </summary>
        private void TryWireCmtConsoleLogging()
        {
            try
            {
                // Access BaseImportCustomizations._parser
                object? parser = _import?.GetType()
                    .GetField("_parser", BindingFlags.Instance | BindingFlags.NonPublic)?
                    .GetValue(_import);

                // Access parser.Logger (TraceLogger)
                object? parserLogger = parser?.GetType()
                    .GetProperty("Logger", BindingFlags.Instance | BindingFlags.Public)?
                    .GetValue(parser);

                // Call AddTraceListener with a ConsoleTraceListener
                MethodInfo? addListenerMethod = parserLogger?.GetType()
                    .GetMethod("AddTraceListener", BindingFlags.Instance | BindingFlags.Public);

                if (addListenerMethod is not null)
                {
                    var consoleListener = new ConsoleTraceListener();
                    addListenerMethod.Invoke(parserLogger, [consoleListener]);

                    if (_request.Verbose)
                    {
                        _logger.LogDebug("CMT console logging wired successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                _traceSource?.TraceEvent(TraceEventType.Warning, 0,
                    $"Could not wire CMT console logging: {ex.Message}");
            }
        }

        private Assembly? OnResolveAssembly(object? sender, ResolveEventArgs args)
        {
            string requestedAssembly = args.Name ?? "<unknown>";
            string key = requestedAssembly.Split(',')[0];

            if (_assemblyMap.TryGetValue(key, out Assembly? assembly))
            {
                return assembly;
            }

            // Probe the extracted package directory for package-specific assemblies
            // (e.g. custom ImportExtensions DLLs shipped inside the pdpkg).
            assembly = TryLoadFromExtractedDirectory(key);
            if (assembly is not null)
            {
                _assemblyMap[key] = assembly;
                return assembly;
            }

            if (_unresolvedAssemblies.Add(requestedAssembly) && _request.Verbose)
            {
                _logger.LogDebug("Unresolved assembly requested: {Assembly}", requestedAssembly);
            }

            return null;
        }

        /// <summary>
        /// AssemblyLoadContext resolver — catches requests that the
        /// AppDomain resolver does not handle on modern .NET.
        /// </summary>
        private Assembly? OnResolveAssemblyLoadContext(AssemblyLoadContext context, System.Reflection.AssemblyName assemblyName)
        {
            string key = assemblyName.Name ?? "<unknown>";

            if (_assemblyMap.TryGetValue(key, out Assembly? assembly))
            {
                return assembly;
            }

            assembly = TryLoadFromExtractedDirectory(key);
            if (assembly is not null)
            {
                _assemblyMap[key] = assembly;
                return assembly;
            }

            return null;
        }

        private Assembly? TryLoadFromExtractedDirectory(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(_extractedDirectory))
            {
                return null;
            }

            string candidate = Path.Combine(_extractedDirectory, assemblyName + ".dll");
            if (File.Exists(candidate))
            {
                try
                {
                    // Patch WindowsBase references in pdpkg-shipped DLLs
                    // before loading, just like we do for the built-in ones.
                    LegacyAssemblyRuntime.PatchDispatcherReferencesOnDisk(candidate);
                    return Assembly.LoadFrom(candidate);
                }
                catch
                {
                    // Silently ignore load failures — the caller will report the
                    // assembly as unresolved if no other handler can satisfy it.
                }
            }

            return null;
        }

        private void Parser_AddNewProgressItem(object? sender, ProdgressDataItemEventArgs e)
        {
            Parser_UpdateProgressItem(sender, e);
        }

        private void Parser_UpdateProgressItem(object? sender, ProdgressDataItemEventArgs e)
        {
            CaptureFailure(e);
            WriteProgressStatus(e);
        }

        private void Parser_ConfigReadComplete(object? sender, ImportProgressStatus e)
        {
            if (!string.IsNullOrWhiteSpace(e.StatusMessage))
            {
                RecordFailureDetail(e.StatusMessage);
                _logger.LogInformation("{StatusMessage}", e.StatusMessage);
            }

            if (sender is PackageImportConfigurationParser parser)
            {
                parser.ConfigReadComplete -= Parser_ConfigReadComplete;
                parser.AddNewProgressItem -= Parser_AddNewProgressItem;
                parser.UpdateProgressItem -= Parser_UpdateProgressItem;
            }

            if (!e.isCompleted)
            {
                _errorMessage = ChooseFailureMessage(
                    e.StatusMessage,
                    "Package Deployer configuration read failed.");
                _workComplete.Set();
                return;
            }

            _import = new BaseImportCustomizations(_coreObjects!);
            _import.ImportComplete += Import_ImportComplete;
            _import.AddNewProgressItem += Import_AddNewProgressItem;
            _import.ImportStatusUpdate += Import_ImportStatusUpdate;
            _import.UpdateProgressItem += Import_UpdateProgressItem;
            _import.UseAsyncModeByDefaultForSolutionDeleteAndPromote = true;
            _import.UseAsyncModeByDefaultForSolutionImport = true;

            // Wire a ConsoleTraceListener to CMT's internal TraceSource
            // so data import progress is visible on the console in real time,
            // similar to how PAC CLI's ListenToDataMigrationLogging works.
            TryWireCmtConsoleLogging();
            _import.BeginSolutionImport();
        }

        private void Import_AddNewProgressItem(object? sender, ProdgressDataItemEventArgs e)
        {
            Import_UpdateProgressItem(sender, e);
        }

        private void Import_UpdateProgressItem(object? sender, ProdgressDataItemEventArgs e)
        {
            if (e.progressItem is not null)
            {
                CaptureFailure(e);
                WriteProgressStatus(e);
            }
        }

        private void Import_ImportStatusUpdate(object? sender, ImportProgressStatus e)
        {
            if (LooksLikeFailureStatus(e.StatusMessage))
            {
                RecordFailureDetail(e.StatusMessage);
            }

            if (_request.Verbose && !string.IsNullOrWhiteSpace(e.StatusMessage))
            {
                _logger.LogInformation("IMP_STATUS > {StatusMessage}", e.StatusMessage);
            }
        }

        private void Import_ImportComplete(object? sender, ImportProgressStatus e)
        {
            if (!e.isCompleted)
            {
                _errorMessage = ChooseFailureMessage(
                    e.StatusMessage,
                    "Package Deployer reported failure.");
            }

            if (_import is not null)
            {
                _import.ImportComplete -= Import_ImportComplete;
                _import.AddNewProgressItem -= Import_AddNewProgressItem;
                _import.ImportStatusUpdate -= Import_ImportStatusUpdate;
                _import.UpdateProgressItem -= Import_UpdateProgressItem;
            }

            _workComplete.Set();
        }

        private void CaptureFailure(ProdgressDataItemEventArgs e)
        {
            if (e.progressItem?.ItemStatus == ProgressPanelItemStatus.Failed &&
                !string.IsNullOrWhiteSpace(e.progressItem.ItemText))
            {
                RecordFailureDetail(e.progressItem.ItemText);
            }
        }

        private void WriteProgressStatus(ProdgressDataItemEventArgs e)
        {
            string message = e.progressItem?.ItemText ?? string.Empty;
            switch (e.progressItem?.ItemStatus)
            {
                case ProgressPanelItemStatus.Complete:
                    _logger.LogInformation("{Message} - {Status}", message, e.progressItem.ItemStatus);
                    break;
                case ProgressPanelItemStatus.Failed:
                    RecordFailureDetail(message);
                    _logger.LogError("{Message}", message);
                    break;
                case ProgressPanelItemStatus.Warning:
                    _logger.LogWarning("{Message}", message);
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        _logger.LogInformation("{Message}", message);
                    }
                    break;
            }
        }

        private static bool LooksLikeZipArchive(string path)
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length < 4)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[4];
            stream.ReadExactly(header);
            return header[0] == (byte)'P' && header[1] == (byte)'K';
        }

        /// <summary>
        /// Builds a <see cref="PackageInfo"/> from the NuGet metadata on the request so that
        /// Package Deployer writes the NuGet package name into <c>packagehistory.uniquename</c>,
        /// enabling reliable lookup via <c>txc environment deployment show --package-name</c>.
        /// Returns <see langword="null"/> when the request has no NuGet identity (local-file deploys).
        /// </summary>
        private static PackageInfo? BuildPackageInfo(PackageDeployerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.NuGetPackageName))
            {
                return null;
            }

            return new PackageInfo(
                packageUniqueName: request.NuGetPackageName,
                packageVersion: string.IsNullOrWhiteSpace(request.NuGetPackageVersion) ? "0.0.0.0" : request.NuGetPackageVersion,
                applicationId: Guid.Empty,
                applicationName: request.NuGetPackageName,
                publisherId: Guid.Empty,
                publisherName: string.Empty,
                tpsPackageId: Guid.Empty,
                packageInstanceId: Guid.NewGuid(),
                packageInstanceOperationId: Guid.NewGuid());
        }

        private void RecordFailureDetail(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return;
            }

            string normalized = detail.Trim();
            lock (_failureDetailsLock)
            {
                if (_failureDetails.Count == 0 || !string.Equals(_failureDetails[^1], normalized, StringComparison.Ordinal))
                {
                    _failureDetails.Add(normalized);
                }
            }
        }

        /// <summary>
        /// Resolves the maximum time the runner will block on <see cref="_workComplete"/>. Package Deployer
        /// is expected to raise <c>Import_ImportComplete</c> (or <c>Parser_ConfigReadComplete</c> on config
        /// failure) to end the wait. Real-world Package Deployer runs can span many hours for large data
        /// packages, so the default is an infinite wait — preserving previous behavior. CI or callers that
        /// need a bounded wait can opt in by setting <c>TXC_DEPLOY_TIMEOUT_MINUTES</c>.
        /// </summary>
        private static TimeSpan ResolveWorkTimeout()
        {
            string? raw = System.Environment.GetEnvironmentVariable("TXC_DEPLOY_TIMEOUT_MINUTES");
            if (!string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)
                && minutes > 0)
            {
                return TimeSpan.FromMinutes(minutes);
            }
            return Timeout.InfiniteTimeSpan;
        }

        private string FormatFailureDetails()
        {
            lock (_failureDetailsLock)
            {
                return _failureDetails.Count == 0
                    ? "(none recorded)"
                    : string.Join(" | ", _failureDetails);
            }
        }

        private string ChooseFailureMessage(string? statusMessage, string fallback)
        {
            return FirstNonEmpty(
                GetLatestTraceLoggerFailureDetail(),
                GetLatestFailureDetail(),
                statusMessage,
                _errorMessage,
                fallback)!;
        }

        private string? GetLatestFailureDetail()
        {
            lock (_failureDetailsLock)
            {
                return _failureDetails.Count > 0 ? _failureDetails[^1] : null;
            }
        }

        private static bool LooksLikeFailureStatus(string? statusMessage)
        {
            return !string.IsNullOrWhiteSpace(statusMessage) &&
                (statusMessage.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                 statusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                 statusMessage.Contains("exception", StringComparison.OrdinalIgnoreCase));
        }

        private string? GetLatestTraceLoggerFailureDetail()
        {
            try
            {
                object? logger = _coreObjects?.GetType()
                    .GetProperty("Logger", BindingFlags.Instance | BindingFlags.Public)?
                    .GetValue(_coreObjects);

                object? logsValue = logger?.GetType()
                    .GetProperty("Logs", BindingFlags.Instance | BindingFlags.Public)?
                    .GetValue(logger);

                if (logsValue is not System.Collections.IEnumerable logs)
                {
                    return null;
                }

                string? latestError = null;
                string? latestFailure = null;

                foreach (object? entry in logs)
                {
                    string? line = entry?.ToString();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    string message = ExtractTraceLoggerMessage(line);
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }

                    if (line.Contains(" Error:", StringComparison.OrdinalIgnoreCase))
                    {
                        latestError = message;
                    }
                    else if (LooksLikeFailureStatus(message))
                    {
                        latestFailure = message;
                    }
                }

                return FirstNonEmpty(latestError, latestFailure);
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractTraceLoggerMessage(string line)
        {
            const string marker = "Message:";
            int index = line.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            string message = index >= 0
                ? line[(index + marker.Length)..].Trim()
                : line.Trim();

            // TraceLogger.Logs entries come from Tuple<DateTime, string>.ToString(),
            // which wraps the original log line in parentheses. If we extracted the
            // trailing message from such a tuple string, strip only the tuple's final
            // closing parenthesis while preserving legitimate message content.
            if (index >= 0 &&
                line.Length > 0 &&
                line[0] == '(' &&
                line[^1] == ')' &&
                message.Length > 0 &&
                message[^1] == ')')
            {
                message = message[..^1].TrimEnd();
            }

            return message;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= OnResolveAssembly;
            AssemblyLoadContext.Default.Resolving -= OnResolveAssemblyLoadContext;
            _workComplete.Dispose();
            _logFile?.Dispose();
            _traceSource?.Close();
        }
    }
}
