using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;

namespace TALXIS.CLI.XrmTools;

/// <summary>
/// Shared runtime infrastructure for hosting legacy .NET Framework assemblies
/// (Package Deployer, CMT) on modern .NET. Handles Cecil IL patching of
/// WindowsBase/Dispatcher references and System.IO.Packaging rebinding,
/// plus assembly resolution for redirecting legacy type references to
/// modern equivalents.
///
/// Both <see cref="PackageDeployerRunner"/> and <see cref="CmtImportRunner"/>
/// use this class for their shared initialization.
/// </summary>
public static class LegacyAssemblyRuntime
{
    private static readonly object s_initLock = new();
    private static bool s_initialized;

    /// <summary>
    /// Assembly map populated during initialization — maps simple assembly
    /// names to pre-loaded assemblies for the static resolver.
    /// </summary>
    internal static readonly Dictionary<string, Assembly> StaticAssemblyMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ensures the legacy assembly runtime is initialized exactly once.
    /// Safe to call from multiple threads / runners — idempotent.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (s_initialized)
            return;

        lock (s_initLock)
        {
            if (s_initialized)
                return;

            // Patch net462 assemblies on disk BEFORE any of their types are
            // loaded. The runtime's default probing will then find the
            // patched versions when the JIT first needs them.
            PatchAssembliesOnDisk();

            // Register resolvers for assemblies the default probing can't find
            // or that need redirection (e.g. our CrmServiceClient shim).
            AppDomain.CurrentDomain.AssemblyResolve += StaticOnResolveAssembly;
            AssemblyLoadContext.Default.Resolving += StaticOnResolveAssemblyLoadContext;

            StaticAssemblyMap["Newtonsoft.Json"] = typeof(Newtonsoft.Json.JsonConverter).Assembly;
            StaticAssemblyMap["Microsoft.Xrm.Tooling.Connector"] = typeof(Microsoft.Xrm.Tooling.Connector.CrmServiceClient).Assembly;
            StaticAssemblyMap["System.ServiceModel"] = typeof(System.ServiceModel.FaultException).Assembly;
            StaticAssemblyMap["System.IO.Packaging"] = typeof(System.IO.Packaging.Package).Assembly;

            s_initialized = true;
        }
    }

    /// <summary>
    /// Patches all net462 assemblies on disk, replacing
    /// System.Windows.Threading.Dispatcher references with System.Object
    /// and removing the WindowsBase assembly reference, plus rebinding
    /// System.IO.Packaging types to the modern assembly.
    /// Patching is idempotent — assemblies already patched are left untouched.
    /// </summary>
    private static void PatchAssembliesOnDisk()
    {
        string baseDir = AppContext.BaseDirectory;

        foreach (string path in Directory.EnumerateFiles(baseDir, "*.dll"))
        {
            try
            {
                PatchDispatcherReferencesOnDisk(path);
            }
            catch
            {
                // Skip DLLs that Cecil can't read (native, malformed, etc.)
            }
        }
    }

    /// <summary>
    /// Rewrites a single assembly on disk:
    /// <list type="bullet">
    ///   <item>Replaces <c>System.Windows.Threading</c> type references with <c>System.Object</c>
    ///         and removes the <c>WindowsBase</c> assembly reference.</item>
    ///   <item>Rebinds <c>System.IO.Packaging</c> types to the modern assembly.</item>
    ///   <item>Replaces <c>Microsoft.VisualBasic.Logging.FileLogTraceListener</c> references
    ///         with <c>Microsoft.Xrm.Tooling.Connector.DynamicsFileLogTraceListener</c>
    ///         (our shim) and removes the <c>Microsoft.VisualBasic</c> assembly reference.</item>
    /// </list>
    /// No-ops if the assembly has none of these references.
    /// </summary>
    internal static void PatchDispatcherReferencesOnDisk(string assemblyPath)
    {
        var readerParams = new ReaderParameters { ReadingMode = ReadingMode.Immediate };

        bool needsPatch;
        byte[] patchedBytes;

        using (var cecilAssembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParams))
        {
            var module = cecilAssembly.MainModule;

            var windowsBaseRef = module.AssemblyReferences
                .FirstOrDefault(r => r.Name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase));
            var visualBasicRef = module.AssemblyReferences
                .FirstOrDefault(r => r.Name.Equals("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase));
            var packagingTypeRefs = module.GetTypeReferences()
                .Where(r => r.Namespace == "System.IO.Packaging")
                .ToList();
            var vbLoggingTypeRefs = module.GetTypeReferences()
                .Where(r => r.Namespace == "Microsoft.VisualBasic.Logging")
                .ToList();

            if (windowsBaseRef == null && packagingTypeRefs.Count == 0 &&
                visualBasicRef == null && vbLoggingTypeRefs.Count == 0)
                return;

            needsPatch = false;
            var objectTypeRef = module.TypeSystem.Object;

            // Patch System.Windows.Threading → System.Object
            foreach (var typeRef in module.GetTypeReferences())
            {
                if (typeRef.Namespace == "System.Windows.Threading")
                {
                    typeRef.Namespace = objectTypeRef.Namespace;
                    typeRef.Name = objectTypeRef.Name;
                    typeRef.Scope = objectTypeRef.Scope;
                    needsPatch = true;
                }
            }

            if (windowsBaseRef != null)
            {
                module.AssemblyReferences.Remove(windowsBaseRef);
                needsPatch = true;
            }

            // Patch Microsoft.VisualBasic.Logging.FileLogTraceListener →
            // Microsoft.Xrm.Tooling.Connector.DynamicsFileLogTraceListener.
            // Our shim provides this type with the FullLogFileName property
            // that CMT's GetLogFilePath() expects.
            if (vbLoggingTypeRefs.Count > 0)
            {
                // Find or create a reference to our shim assembly
                // (Microsoft.Xrm.Tooling.Connector).
                var connectorRef = module.AssemblyReferences
                    .FirstOrDefault(r => r.Name.Equals("Microsoft.Xrm.Tooling.Connector", StringComparison.OrdinalIgnoreCase));

                foreach (var typeRef in vbLoggingTypeRefs)
                {
                    if (typeRef.Name == "FileLogTraceListener")
                    {
                        // Redirect to our DynamicsFileLogTraceListener shim
                        typeRef.Namespace = "Microsoft.Xrm.Tooling.Connector";
                        typeRef.Name = "DynamicsFileLogTraceListener";
                        if (connectorRef != null)
                        {
                            typeRef.Scope = connectorRef;
                        }
                        needsPatch = true;
                    }
                    else
                    {
                        // Any other VB.Logging types → System.Object
                        typeRef.Namespace = objectTypeRef.Namespace;
                        typeRef.Name = objectTypeRef.Name;
                        typeRef.Scope = objectTypeRef.Scope;
                        needsPatch = true;
                    }
                }

                if (visualBasicRef != null)
                {
                    module.AssemblyReferences.Remove(visualBasicRef);
                    needsPatch = true;
                }
            }
            else if (visualBasicRef != null)
            {
                // Assembly has a VB reference but no VB.Logging type refs — remove it.
                module.AssemblyReferences.Remove(visualBasicRef);
                needsPatch = true;
            }

            // Rebind System.IO.Packaging types to the modern assembly.
            if (packagingTypeRefs.Count > 0)
            {
                var packagingAssemblyName = typeof(System.IO.Packaging.Package).Assembly.GetName();
                var packagingRef = module.AssemblyReferences
                    .FirstOrDefault(r => r.Name.Equals(packagingAssemblyName.Name, StringComparison.OrdinalIgnoreCase));

                if (packagingRef == null)
                {
                    packagingRef = new AssemblyNameReference(packagingAssemblyName.Name!, packagingAssemblyName.Version!);
                    if (!string.IsNullOrWhiteSpace(packagingAssemblyName.CultureName))
                    {
                        packagingRef.Culture = packagingAssemblyName.CultureName;
                    }

                    byte[] publicKeyToken = packagingAssemblyName.GetPublicKeyToken() ?? Array.Empty<byte>();
                    if (publicKeyToken.Length > 0)
                    {
                        packagingRef.PublicKeyToken = publicKeyToken;
                    }

                    module.AssemblyReferences.Add(packagingRef);
                }

                foreach (var typeRef in packagingTypeRefs)
                {
                    if (!string.Equals(typeRef.Scope?.Name, packagingRef.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        typeRef.Scope = packagingRef;
                        needsPatch = true;
                    }
                }
            }

            if (!needsPatch)
                return;

            using var ms = new MemoryStream();
            cecilAssembly.Write(ms);
            patchedBytes = ms.ToArray();
        }

        // Overwrite the original file with the patched version.
        File.WriteAllBytes(assemblyPath, patchedBytes);
    }

    private static Assembly? StaticResolve(string key)
    {
        StaticAssemblyMap.TryGetValue(key, out Assembly? assembly);
        return assembly;
    }

    private static Assembly? StaticOnResolveAssembly(object? sender, ResolveEventArgs args)
    {
        string key = (args.Name ?? "<unknown>").Split(',')[0];
        return StaticResolve(key);
    }

    private static Assembly? StaticOnResolveAssemblyLoadContext(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        string key = assemblyName.Name ?? "<unknown>";
        return StaticResolve(key);
    }
}
