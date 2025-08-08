using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge;
using System.Security.Cryptography;
using System.Diagnostics;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Manages the TALXIS template package ensuring a single installation across processes.
    /// </summary>
    public class TemplatePackageService : IDisposable
    {
        private readonly TemplatePackageManager _templatePackageManager;
        private readonly IEngineEnvironmentSettings _environmentSettings;
        private readonly string _templatePackageName = "TALXIS.DevKit.Templates.Dataverse";
        private readonly SemaphoreSlim _installationSemaphore = new(1, 1);
        private volatile bool _isTemplateInstalled;
        private IManagedTemplatePackage? _installedTemplatePackage;

        // Tunables
        private const int MutexPollDelayMs = 300;          // Small delay between attempts
        private static readonly TimeSpan MutexMaxWait = TimeSpan.FromSeconds(30); // Fail fast threshold

        public string TemplatePackageName => _templatePackageName;

        public TemplatePackageService(TemplatePackageManager templatePackageManager, IEngineEnvironmentSettings environmentSettings)
        {
            _templatePackageManager = templatePackageManager ?? throw new ArgumentNullException(nameof(templatePackageManager));
            _environmentSettings = environmentSettings ?? throw new ArgumentNullException(nameof(environmentSettings));
        }

        /// <summary>
        /// Ensures the template package is installed (idempotent, thread + process safe).
        /// </summary>
        public async Task EnsureTemplatePackageInstalledAsync(string? version = null)
        {
            // Fast in-memory short‑circuit
            if (_isTemplateInstalled && _installedTemplatePackage != null) return;

            await _installationSemaphore.WaitAsync();
            try
            {
                if (_isTemplateInstalled && _installedTemplatePackage != null) return;
                await EnsureInstalledCrossProcessAsync(version);
            }
            finally
            {
                _installationSemaphore.Release();
            }
        }

        // ---------------------------- Internal helpers ----------------------------

        private async Task EnsureInstalledCrossProcessAsync(string? version)
        {
            // Pre-check without lock (cheap) – if another process already completed install.
            if (await TryLoadExistingInstalledPackageAsync()) return;

            var mutexName = CreateCrossProcessMutexName(_templatePackageName);
            using var mutex = new Mutex(false, mutexName, out _);
            var acquired = await AcquireMutexWithPollingAsync(mutex, version);
            try
            {
                if (!acquired)
                {
                    throw new TimeoutException($"Timeout ({MutexMaxWait.TotalSeconds:F0}s) waiting to install '{_templatePackageName}'. Another process may be stalled.");
                }

                // Final check inside critical section (double-checked cross-process)
                if (await TryLoadExistingInstalledPackageAsync()) return;

                await InstallTemplatePackageAsync(version);
            }
            finally
            {
                if (acquired)
                {
                    try { mutex.ReleaseMutex(); } catch { /* ignore */ }
                }
            }
        }

        /// <summary>
        /// Polls for mutex ownership while periodically re-checking whether installation completed elsewhere.
        /// </summary>
        private async Task<bool> AcquireMutexWithPollingAsync(Mutex mutex, string? version)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < MutexMaxWait)
            {
                try
                {
                    if (mutex.WaitOne(TimeSpan.Zero)) return true; // Acquired immediately
                }
                catch (AbandonedMutexException)
                {
                    return true; // Treat abandoned as success (we now own it)
                }

                // Re-check installation status – if installed we do not need the lock anymore.
                if (await TryLoadExistingInstalledPackageAsync()) return false; // False = we did not own the mutex but work is done

                await Task.Delay(MutexPollDelayMs);
            }
            return false; // Timed out
        }

        /// <summary>
        /// Attempts to locate an already installed package; updates internal state if found.
        /// </summary>
        private async Task<bool> TryLoadExistingInstalledPackageAsync()
        {
            var existingPackages = await _templatePackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
            var existing = existingPackages.FirstOrDefault(p => string.Equals(p.Identifier, _templatePackageName, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return false;
            _installedTemplatePackage = existing;
            _isTemplateInstalled = true;
            return true;
        }

        private async Task InstallTemplatePackageAsync(string? version)
        {
            var request = new InstallRequest(_templatePackageName, version, details: new Dictionary<string, string>(), force: false);
            var provider = _templatePackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            var results = await provider.InstallAsync(new[] { request }, CancellationToken.None);
            var result = results.FirstOrDefault();

            if (result == null || !result.Success)
            {
                var details = result?.ErrorMessage ?? "Unknown installation error";
                throw new InvalidOperationException($"Failed to install template package '{_templatePackageName}'.\nDetails:\n{details}\n" +
                                                    "💡 Corrective actions:\n" +
                                                    "   • Check internet connectivity\n" +
                                                    "   • Verify package name/version\n" +
                                                    "   • Ensure global install permissions\n" +
                                                    "   • Validate private feeds (if used)");
            }

            _installedTemplatePackage = result.TemplatePackage as IManagedTemplatePackage
                ?? throw new InvalidOperationException($"Template package '{_templatePackageName}' installed but not retrievable as managed package");
            _isTemplateInstalled = true; // Publish state last
        }

        private static string CreateCrossProcessMutexName(string packageName)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(packageName));
            var token = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            // Omit Windows-specific Global\ prefix for cross-platform consistency.
            return $"TALXIS_CLI_TemplatePackage_{token}";
        }

        public async Task<List<ITemplateInfo>> ListTemplatesAsync(string? version = null)
        {
            await EnsureTemplatePackageInstalledAsync(version);
            var pkg = _installedTemplatePackage ?? throw new InvalidOperationException("Template package reference missing after install.");
            var templates = await _templatePackageManager.GetTemplatesAsync(pkg, CancellationToken.None);
            return templates.ToList();
        }

        public void Dispose()
        {
            _installationSemaphore.Dispose();
        }
    }
}
