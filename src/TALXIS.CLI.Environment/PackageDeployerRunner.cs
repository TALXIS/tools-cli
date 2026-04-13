using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Tooling.CrmConnectControl.Utility;
using Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageCore.ImportCode;
using Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageCore.Models;
using Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageExtentionBase;

namespace TALXIS.CLI.Environment;

public sealed class PackageDeployerRunner
{
    public Task<PackageDeployerResult> RunAsync(PackageDeployerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Run(request), cancellationToken);
    }

    private static PackageDeployerResult Run(PackageDeployerRequest request)
    {
        using ManagedPackageDeployerHost host = new(request);
        return host.Run();
    }

    private sealed class ManagedPackageDeployerHost : IDisposable
    {
        private readonly PackageDeployerRequest _request;
        private readonly Dictionary<string, Assembly> _assemblyMap;
        private readonly HashSet<string> _unresolvedAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly ManualResetEventSlim _workComplete = new();

        private string? _errorMessage;
        private string? _packagePathForCoreObjects;
        private string? _searchPathForCoreObjects;
        private string? _extractedDirectory;
        private TraceSource? _traceSource;
        private FileStream? _logFile;
        private BaseImportCustomizations? _import;
        private CoreObjects? _coreObjects;

        public ManagedPackageDeployerHost(PackageDeployerRequest request)
        {
            _request = request;

            // Register our shim assembly for the Connector assembly name that
            // CrmPackageCore was compiled against.
            _assemblyMap = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase)
            {
                ["Newtonsoft.Json"] = typeof(Newtonsoft.Json.JsonConverter).Assembly,
                ["Microsoft.Xrm.Tooling.Connector"] = typeof(CrmServiceClient).Assembly,
                ["Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageExtentionBase"] = typeof(IImportExtensions).Assembly
            };

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
            DataverseInteractiveAuthHook? authHook = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(_request.ConnectionString))
                {
                    crmServiceClient = new CrmServiceClient(_request.ConnectionString);
                }
                else
                {
                    if (!Uri.TryCreate(_request.EnvironmentUrl, UriKind.Absolute, out Uri? environmentUri))
                    {
                        throw new InvalidOperationException("A valid Dataverse environment URL is required for interactive authentication.");
                    }

                    authHook = new DataverseInteractiveAuthHook(environmentUri, _request.DeviceCode, _request.Verbose);
                    CrmServiceClient.AuthOverrideHook = authHook;

                    // Use the token provider constructor — our shim delegates
                    // to AuthOverrideHook internally when the URI constructor is used.
                    crmServiceClient = new CrmServiceClient(environmentUri, useUniqueInstance: true);
                }

                if (!crmServiceClient.IsReady)
                {
                    return new PackageDeployerResult(false, crmServiceClient.LastCrmException?.Message ?? crmServiceClient.LastCrmError);
                }

                _coreObjects = new CoreObjects(
                    targetSearchPath: _searchPathForCoreObjects,
                    sourcePackageAssemblyPath: _packagePathForCoreObjects,
                    allowPackageCodeExecution: true,
                    forceSyncExecution: false,
                    packageInfo: null,
                    logger: traceLogger,
                    allowAsyncRibbonProcessing: false,
                    correlationId: Guid.NewGuid());

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

                try
                {
                    parser.ReadConfig();
                }
                catch (Exception ex)
                {
                    return new PackageDeployerResult(false, ex.Message);
                }

                _workComplete.Wait();
                return new PackageDeployerResult(string.IsNullOrWhiteSpace(_errorMessage), _errorMessage);
            }
            finally
            {
                CrmServiceClient.AuthOverrideHook = null;
                crmServiceClient?.Dispose();
                authHook?.Dispose();
            }
        }

        private void PreparePackageInputs()
        {
            if (LooksLikeZipArchive(_request.PackagePath))
            {
                _extractedDirectory = Path.Combine(Path.GetTempPath(), "txc", "package-deployer", Guid.NewGuid().ToString("N"));
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

            if (!string.IsNullOrWhiteSpace(_request.LogFile))
            {
                string logFilePath = Path.GetFullPath(_request.LogFile);
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                _logFile = File.Open(logFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                TextWriterTraceListener fileListener = new(_logFile);
                _traceSource.Listeners.Add(fileListener);
                Microsoft.Xrm.Tooling.CrmConnectControl.Utility.TraceControlSettings.AddTraceListener(fileListener);
                Microsoft.Xrm.Tooling.Connector.TraceControlSettings.AddTraceListener(fileListener);
            }

            return new TraceLogger(_traceSource);
        }

        private Assembly? OnResolveAssembly(object? sender, ResolveEventArgs args)
        {
            string requestedAssembly = args.Name ?? "<unknown>";
            string key = requestedAssembly.Split(',')[0];

            if (_assemblyMap.TryGetValue(key, out Assembly? assembly))
            {
                return assembly;
            }

            if (_unresolvedAssemblies.Add(requestedAssembly) && _request.Verbose)
            {
                Console.WriteLine($"[txc] unresolved assembly requested: {requestedAssembly}");
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

            return null;
        }

        private void Parser_AddNewProgressItem(object? sender, ProdgressDataItemEventArgs e)
        {
            Parser_UpdateProgressItem(sender, e);
        }

        private void Parser_UpdateProgressItem(object? sender, ProdgressDataItemEventArgs e)
        {
            WriteProgressStatus(e);
        }

        private void Parser_ConfigReadComplete(object? sender, ImportProgressStatus e)
        {
            if (!string.IsNullOrWhiteSpace(e.StatusMessage))
            {
                Console.WriteLine(e.StatusMessage);
                Console.WriteLine();
            }

            if (sender is PackageImportConfigurationParser parser)
            {
                parser.ConfigReadComplete -= Parser_ConfigReadComplete;
                parser.AddNewProgressItem -= Parser_AddNewProgressItem;
                parser.UpdateProgressItem -= Parser_UpdateProgressItem;
            }

            if (!e.isCompleted)
            {
                _errorMessage = e.StatusMessage ?? "Package Deployer failed while parsing import configuration.";
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
                WriteProgressStatus(e);
            }
        }

        private void Import_ImportStatusUpdate(object? sender, ImportProgressStatus e)
        {
            if (_request.Verbose && !string.IsNullOrWhiteSpace(e.StatusMessage))
            {
                Console.WriteLine($"IMP_STATUS > {e.StatusMessage}");
            }
        }

        private void Import_ImportComplete(object? sender, ImportProgressStatus e)
        {
            if (!e.isCompleted)
            {
                _errorMessage = e.StatusMessage;
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

        private static void WriteProgressStatus(ProdgressDataItemEventArgs e)
        {
            string message = e.progressItem?.ItemText ?? string.Empty;
            switch (e.progressItem?.ItemStatus)
            {
                case ProgressPanelItemStatus.Complete:
                    Console.WriteLine($"{message} - {e.progressItem.ItemStatus}");
                    break;
                case ProgressPanelItemStatus.Failed:
                    Console.Error.WriteLine(message);
                    break;
                case ProgressPanelItemStatus.Warning:
                    Console.Error.WriteLine($"Warning: {message}");
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        Console.WriteLine(message);
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

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= OnResolveAssembly;
            AssemblyLoadContext.Default.Resolving -= OnResolveAssemblyLoadContext;
            _workComplete.Dispose();
            _logFile?.Dispose();
            _traceSource?.Close();

            if (!string.IsNullOrWhiteSpace(_extractedDirectory) && Directory.Exists(_extractedDirectory))
            {
                Directory.Delete(_extractedDirectory, recursive: true);
            }
        }
    }
}
