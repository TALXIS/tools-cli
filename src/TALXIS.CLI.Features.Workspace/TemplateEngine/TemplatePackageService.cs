using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge;
using NuGet.Versioning;
using System.Security.Cryptography;
using System.Diagnostics;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine
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
        /// <remarks>
        /// A package's <see cref="IManagedTemplatePackage.Identifier"/> is the package id, not a
        /// unique key per registration — it is possible (e.g. after an update that installs a new
        /// version without uninstalling the previous one) for <c>packages.json</c> to contain more
        /// than one registration with the same identifier. Picking the first one blindly can select
        /// a stale/broken registration that has zero templates indexed, which then poisons every
        /// subsequent template lookup for the lifetime of the process. To guard against that, all
        /// matching candidates are ranked (highest version first, most recently changed as a
        /// tie-breaker) and probed in order until one actually yields templates.
        /// </remarks>
        private async Task<bool> TryLoadExistingInstalledPackageAsync()
        {
            var existingPackages = await _templatePackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
            var candidates = RankCandidates(existingPackages, _templatePackageName);
            var selected = await SelectFirstPackageWithTemplatesAsync(candidates, HasTemplatesAsync);
            if (selected == null) return false;

            _installedTemplatePackage = selected;
            _isTemplateInstalled = true;
            return true;
        }

        private async Task<bool> HasTemplatesAsync(IManagedTemplatePackage package)
        {
            var templates = await _templatePackageManager.GetTemplatesAsync(package, CancellationToken.None);
            return templates.Any();
        }

        /// <summary>
        /// Walks <paramref name="rankedCandidates"/> in order and returns the first one for which
        /// <paramref name="hasTemplatesAsync"/> reports at least one template, skipping any stale/duplicate
        /// registration that indexes zero templates. Returns <see langword="null"/> if none qualify.
        /// </summary>
        internal static async Task<IManagedTemplatePackage?> SelectFirstPackageWithTemplatesAsync(
            IEnumerable<IManagedTemplatePackage> rankedCandidates,
            Func<IManagedTemplatePackage, Task<bool>> hasTemplatesAsync)
        {
            foreach (var candidate in rankedCandidates)
            {
                if (await hasTemplatesAsync(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Orders packages matching <paramref name="identifier"/> so the most likely-correct
        /// registration is probed first: highest parsed version wins, falling back to the most
        /// recently changed package when versions are equal or unparseable.
        /// </summary>
        internal static IEnumerable<IManagedTemplatePackage> RankCandidates(IEnumerable<IManagedTemplatePackage> packages, string identifier)
        {
            return packages
                .Where(p => string.Equals(p.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParsePackageVersion, NullableVersionComparer.Instance)
                .ThenByDescending(p => p.LastChangeTime);
        }

        /// <summary>
        /// Parses <see cref="IManagedTemplatePackage.Version"/> as a <see cref="NuGetVersion"/>, returning
        /// <see langword="null"/> when it is missing or not a valid version string.
        /// </summary>
        /// <remarks>
        /// <see cref="IManagedTemplatePackage.Version"/> is a NuGet package version, which follows SemVer
        /// 2.0 and may carry a prerelease and/or build-metadata suffix (e.g. <c>2.0.0-beta.1</c>).
        /// <see cref="System.Version"/> only understands dotted numeric components and rejects such
        /// strings outright, which used to make every prerelease-versioned registration sort as if it had
        /// no version at all. <see cref="NuGetVersion"/> parses and compares using correct NuGet/SemVer
        /// precedence rules instead.
        /// </remarks>
        internal static NuGetVersion? ParsePackageVersion(IManagedTemplatePackage package)
        {
            return NuGetVersion.TryParse(package.Version, out var version) ? version : null;
        }

        /// <summary>
        /// Compares nullable <see cref="NuGetVersion"/> values, treating a missing/unparseable version as
        /// lower than any parsed version.
        /// </summary>
        private sealed class NullableVersionComparer : IComparer<NuGetVersion?>
        {
            public static readonly NullableVersionComparer Instance = new();

            public int Compare(NuGetVersion? x, NuGetVersion? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                return x.CompareTo(y);
            }
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
