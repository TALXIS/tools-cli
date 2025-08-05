using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge;

namespace TALXIS.CLI.Workspace.TemplateEngine.Services
{
    /// <summary>
    /// Service responsible for managing template packages (installation, listing, etc.)
    /// </summary>
    public class TemplatePackageService : ITemplatePackageService
    {
        private readonly TemplatePackageManager _templatePackageManager;
        private readonly IEngineEnvironmentSettings _environmentSettings;
        private readonly string _templatePackageName = "TALXIS.DevKit.Templates.Dataverse";
        private bool _isTemplateInstalled = false;
        private IManagedTemplatePackage? _installedTemplatePackage;

        public string TemplatePackageName => _templatePackageName;

        public TemplatePackageService(TemplatePackageManager templatePackageManager, IEngineEnvironmentSettings environmentSettings)
        {
            _templatePackageManager = templatePackageManager ?? throw new ArgumentNullException(nameof(templatePackageManager));
            _environmentSettings = environmentSettings ?? throw new ArgumentNullException(nameof(environmentSettings));
        }

        public async Task EnsureTemplatePackageInstalledAsync(string? version = null)
        {
            if (_isTemplateInstalled && _installedTemplatePackage != null)
            {
                return; // Already installed and we have a reference to it
            }

            try
            {
                // Following the official dotnet CLI pattern: create install request with details
                var installRequest = new InstallRequest(_templatePackageName, version, details: new Dictionary<string, string>(), force: false);
                
                // Get the managed provider for global scope (matches official CLI approach)
                var provider = _templatePackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
                var installResults = await provider.InstallAsync(new[] { installRequest }, CancellationToken.None);
                
                // Check if installation was successful
                var installResult = installResults.FirstOrDefault();
                if (installResult == null || !installResult.Success)
                {
                    var packageId = _templatePackageName;
                    var detailedErrors = installResult?.ErrorMessage ?? "Unknown installation error";
                    
                    var userErrorMessage = $"Failed to install template package '{packageId}'.\n" + 
                                         $"Details:\n{detailedErrors}\n\n" + 
                                         $"💡 Corrective actions:\n" + 
                                         $"   • Check your internet connection\n" + 
                                         $"   • Verify the package name and version are correct\n" + 
                                         $"   • Ensure you have sufficient permissions for global package installation\n" + 
                                         $"   • If using a private package source, ensure it's properly configured";
                    
                    throw new InvalidOperationException(userErrorMessage);
                }
                
                // Following the official dotnet CLI pattern: store reference to the installed package
                // This is crucial for later template discovery
                _installedTemplatePackage = installResult.TemplatePackage as IManagedTemplatePackage;
                if (_installedTemplatePackage == null)
                {
                    throw new InvalidOperationException($"Template package '{_templatePackageName}' was installed but could not be retrieved as a managed package");
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
            
            if (_installedTemplatePackage == null)
            {
                throw new InvalidOperationException("Template package was installed but reference is not available");
            }
            
            // Use the official dotnet CLI pattern - get templates from the specific installed package
            var templates = await _templatePackageManager.GetTemplatesAsync(_installedTemplatePackage, CancellationToken.None);
            return templates.ToList();
        }
    }
}
