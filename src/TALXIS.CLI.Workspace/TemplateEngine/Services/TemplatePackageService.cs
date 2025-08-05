using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge.Settings;

namespace TALXIS.CLI.Workspace.TemplateEngine.Services
{
    /// <summary>
    /// Service responsible for managing template packages (installation, listing, etc.)
    /// </summary>
    public class TemplatePackageService : ITemplatePackageService
    {
        private readonly TemplatePackageManager _templatePackageManager;
        private readonly string _templatePackageName = "TALXIS.DevKit.Templates.Dataverse";
        private bool _isTemplateInstalled = false;

        public string TemplatePackageName => _templatePackageName;

        public TemplatePackageService(TemplatePackageManager templatePackageManager)
        {
            _templatePackageManager = templatePackageManager ?? throw new ArgumentNullException(nameof(templatePackageManager));
        }

        public async Task EnsureTemplatePackageInstalledAsync(string? version = null)
        {
            if (_isTemplateInstalled) return;
            
            try
            {
                // Get the managed template package provider
                var managedProvider = _templatePackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
                var packageId = _templatePackageName + (version != null ? $"::{version}" : "");
                var installRequests = new[] { new InstallRequest(packageId) };
                
                var results = await managedProvider.InstallAsync(installRequests, CancellationToken.None);
                if (results.Any(r => !r.Success))
                {
                    var failedResults = results.Where(r => !r.Success);
                    var detailedErrors = string.Join("\n", failedResults.Select(r => 
                        $"   • Error: {r.ErrorMessage}"));
                    
                    var userErrorMessage = $"Failed to install template package '{packageId}'.\n" +
                                         $"Details:\n{detailedErrors}\n\n" +
                                         $"💡 Corrective actions:\n" +
                                         $"   • Check your internet connection\n" +
                                         $"   • Verify the package name and version are correct\n" +
                                         $"   • Ensure you have sufficient permissions for global package installation\n" +
                                         $"   • If using a private package source, ensure it's properly configured";
                    
                    throw new InvalidOperationException(userErrorMessage);
                }
                
                _isTemplateInstalled = true;
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                // Wrap unexpected exceptions with user-friendly message
                var userErrorMessage = $"Unexpected error while installing template package '{_templatePackageName}'{(version != null ? $" version {version}" : "")}.\n" +
                                     $"Technical details: {ex.Message}\n\n" +
                                     $"💡 Corrective actions:\n" +
                                     $"   • Check your internet connection\n" +
                                     $"   • Ensure you have permission to install global packages\n" +
                                     $"   • Check if the package source is accessible";
                
                throw new InvalidOperationException(userErrorMessage, ex);
            }
        }

        public async Task<List<ITemplateInfo>> ListTemplatesAsync(string? version = null)
        {
            await EnsureTemplatePackageInstalledAsync(version);
            var templates = await _templatePackageManager.GetTemplatesAsync(CancellationToken.None);
            return templates.Where(t => t.MountPointUri.Contains(_templatePackageName, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
