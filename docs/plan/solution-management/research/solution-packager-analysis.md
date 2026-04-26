# SolutionPackager Integration Analysis for txc

## 1. SolutionPackagerLib Public API

### Namespace: `Microsoft.Crm.Tools.SolutionPackager`

#### Core Class: `SolutionPackager`
```csharp
public sealed class SolutionPackager
{
    // Constructor — takes all config via PackagerArguments
    public SolutionPackager(PackagerArguments arguments);

    // Main execution — runs pack or extract based on arguments.Action
    public void Run(IEnumerable<string> ignoreComponentList = null);

    // Static helpers — inspect solution metadata without full pack/unpack
    public static SolutionMetadata ExtractMetadata(string path, bool isPathAFolder = false);
    public static SolutionMetadata ExtractMetadata(Stream solutionZipFileAsBytes);
    public static SolutionInformation InspectLocalFolder(string pathToExtractedSolution);
    public static SolutionInformation InspectZip(string pathToSolutionZip);

    // Repack helper (used for testing round-trip fidelity)
    public static void Repack(PackagerArguments arguments);

    // Exposed context (read-only after construction)
    public Context Context { get; }

    // Static component folder map (ComponentType → folder name)
    public static IReadOnlyDictionary<ComponentType, string> ComponentFolders { get; }
}
```

#### Configuration Class: `PackagerArguments`
| Field | Type | Description |
|-------|------|-------------|
| `Action` | `CommandAction` (Extract/Pack) | **Required** — what to do |
| `PathToZipFile` | `string` | **Required** — path to the .zip solution file |
| `Folder` | `string` | Root folder for unpacked solution (default `.`) |
| `PackageType` | `SolutionPackageType` | Unmanaged / Managed / Both / None |
| `LogFile` | `string` | Optional log file path |
| `ErrorLevel` | `TraceLevel` | Verbosity (default Info) |
| `SingleComponent` | `string` | Filter to single component type (WebResource/Plugin/Workflow/None) |
| `AllowDeletes` | `AllowDelete` | Yes / No / Prompt |
| `AllowWrites` | `AllowWrite` | Yes / No |
| `Clobber` | `bool` | Overwrite read-only files |
| `MappingFile` | `string` | Path to mapping XML |
| `LocaleTemplate` | `string` | Locale for .resx export |
| `Localize` | `bool` | Extract/merge string resources to .resx |
| `UseLcid` | `bool` | Use LCID (1033) instead of ISO (en-US) |
| `UseUnmanagedFileForManaged` | `bool` | Fall back to unmanaged XML when managed missing |
| `RemapPluginTypeNames` | `bool` | Remap plugin FQ type names |
| `SolutionZipStream` | `Stream` | Alternative to PathToZipFile — provide stream directly |
| `SolutionName` | `string` | Solution name under solutions/*/solution.yml |

#### Key Enums
```csharp
public enum CommandAction { Extract, Pack }
public enum SolutionPackageType { Unmanaged, Managed, Both, None__ }
public enum AllowDelete { Prompt, Yes, No }
public enum AllowWrite { Yes, No }
```

#### Key Interfaces
- `IPackageReader` — `Initialize(Context)`, `Load()` — implemented by `ZipReader` (reads .zip) and `DiskReader` (reads folder)
- `IPackageWriter` — `Initialize(Context)`, `WriteComponents(...)`, `LocalizeComponents()` — implemented by `ZipWriter` and `DiskWriter`
- `IPackagePlugin` — lifecycle hooks: `BeforeRead`, `AfterRead`, `BeforeWrite`, `AfterWrite`

#### Other Notable Types
- `SolutionInformation` — holds UniqueName, Version, Publisher, IsManaged, RootComponents, etc.
- `SolutionMetadata` (in `Microsoft.Crm.Tools.ComponentMetadata`) — richer metadata model used by `ExtractMetadata`
- `Context` — internal state container, uses MEF (`[ImportMany]`) to discover component processors
- `ComponentType` — enum with ~100 Dataverse component types (Entity, Form, Workflow, WebResource, etc.)

---

## 2. How PAC CLI Invokes SolutionPackagerLib

### Call Chain
```
pac solution pack/unpack
  → SolutionPackVerb.Execute(command) / SolutionUnpackVerb.Execute(command)
    → SolutionPackagerVerbBase.RunSolutionPackager(SopaAction, command)
      → Builds SolutionPackagerSettings from CLI arguments
      → Calls ISolutionPackagerProvider.PackSolution() or .UnpackSolution()
        → SolutionPackagerProvider.RunSolutionPackager(CommandAction, settings, ignoreList)
          → GenerateSoPaArguments() — maps settings to PackagerArguments
          → new SolutionPackager(arguments).Run(ignoreComponentList)
```

### Key Observations
1. **Direct library call** — PAC CLI does **not** spawn `SolutionPackager.exe` as a process. It references `SolutionPackagerLib.dll` directly and calls `new SolutionPackager(args).Run()`.
2. **SolutionPackagerProvider** is the abstraction layer (`ISolutionPackagerProvider`) — registered as a scoped service via DI.
3. **Settings mapping** is straightforward — `SolutionPackagerSettings` (PAC CLI model) → `PackagerArguments` (SolutionPackagerLib model) via `GenerateSoPaArguments()`.
4. **Error handling** wraps exceptions into `VerbExecutionException` via `RunWithExceptionHandlers<T>`.
5. **AllowDeletes is set to `No`** (not `Prompt`) during unpack to avoid interactive console prompts.
6. **Canvas apps** have separate pack/unpack processing via `ICanvasPacker` (not part of SolutionPackagerLib itself).

### Minimal Usage Example (derived from PAC CLI)
```csharp
var arguments = new PackagerArguments
{
    Action = CommandAction.Extract,       // or CommandAction.Pack
    PathToZipFile = "/path/to/solution.zip",
    Folder = "/path/to/unpacked/folder",
    PackageType = SolutionPackageType.Unmanaged,
    AllowDeletes = AllowDelete.Yes,       // avoid interactive prompt
    AllowWrites = AllowWrite.Yes,
    ErrorLevel = TraceLevel.Info,
    SingleComponent = "None",
};

var packager = new SolutionPackager(arguments);
packager.Run();
```

---

## 3. Integration Options for txc

### Option A: Direct DLL Reference (Recommended)
**Approach:** Reference `SolutionPackagerLib.dll` directly from the PAC CLI tools distribution.

| Pros | Cons |
|------|------|
| Same approach PAC CLI uses internally | Must bundle or locate the DLL at runtime |
| Full API access (pack, unpack, inspect, metadata) | Dependency on internal Microsoft assemblies |
| No process overhead | API is undocumented — could break between versions |
| Can intercept errors programmatically | |

**Dependencies required** (from the csproj):
- `Newtonsoft.Json.dll`
- `System.ComponentModel.Composition.dll`
- `System.Configuration.ConfigurationManager.dll`
- `Microsoft.Deployment.Compression.dll` + `Microsoft.Deployment.Compression.Cab.dll`
- `System.IO.Packaging.dll`
- `YamlDotNet.dll`
- Standard .NET 10 framework libs

**Runtime note:** SolutionPackagerLib uses MEF (`System.ComponentModel.Composition`) to discover `IComponentProcessor` implementations. All processors are defined in the same assembly, so it should work if the DLL is loaded properly. It also reads a configuration section (`ComponentConfigurationManager`) — this needs the `SolutionPackagerLib.dll.config` or equivalent configuration to be present.

### Option B: NuGet Package Reference
**Approach:** Reference a NuGet package containing SolutionPackagerLib.

**Status: NOT AVAILABLE.** The old package `Microsoft.CrmSdk.CoreXrmTooling.SolutionPackager` returned 404 on NuGet — it has been retired. `SolutionPackagerLib` is not published as a standalone NuGet package. It is only distributed as part of the PAC CLI tooling.

### Option C: Process Spawn (`pac solution pack/unpack`)
**Approach:** Shell out to `pac` CLI as a subprocess.

| Pros | Cons |
|------|------|
| No DLL dependency management | Requires PAC CLI installed on machine |
| Always uses latest version | Process overhead per invocation |
| Clean boundary | Parsing stdout/stderr for errors |
| | Cannot intercept partial results or metadata |

### Recommendation
**Option A (Direct DLL Reference)** is the best fit because:
1. It's exactly how PAC CLI itself works.
2. `txc` can provide richer UX — inspect metadata, show progress, handle errors properly.
3. The dependency chain is manageable (6 additional DLLs, all already present in PAC CLI distribution).
4. A fallback to Option C can be added for scenarios where DLLs aren't available.

**Implementation approach for txc:**
1. Create an `ISolutionPackagerService` interface in txc (similar to PAC's `ISolutionPackagerProvider`).
2. Locate SolutionPackagerLib.dll either from a bundled copy or from the PAC CLI installation path.
3. Use `AssemblyLoadContext` to load SolutionPackagerLib and its dependencies at runtime to avoid version conflicts.
4. Wrap `new SolutionPackager(args).Run()` with txc's own error handling and progress reporting.

---

## 4. Unpacked Folder Structure and File Format

When `CommandAction.Extract` runs, SolutionPackager writes components to a well-defined folder structure:

```
<RootFolder>/
├── Solution.xml                          # Solution manifest (name, version, publisher)
├── Customizations.xml                    # (May be empty/minimal if components are split)
├── [Content_Types].xml                   # Content types
│
├── Entities/                             # Entity definitions
│   └── <EntitySchemaName>/
│       ├── Entity.xml                    # Entity metadata
│       ├── SavedQueries/                 # Views
│       │   └── <ViewId>.xml
│       ├── FormXml/                      # Forms
│       │   └── main/
│       │       └── <FormId>.xml
│       ├── RibbonDiff.xml               # Entity-specific ribbon customizations
│       └── Charts/                       # Charts/visualizations
│
├── Workflows/                            # Workflows / Power Automate Cloud Flows
│   └── <WorkflowId>.json               # or .xml for classic workflows
│
├── PluginAssemblies/                     # Plugin assemblies
│   └── <AssemblyName>-<GUID>/
│       └── <AssemblyName>.dll
│
├── WebResources/                         # Web resources
│   └── <prefix_name>.js/.html/.css/etc.
│
├── Roles/                                # Security roles
│   └── <RoleName>.xml
│
├── OptionSets/                           # Global option sets
│   └── <OptionSetName>.xml
│
├── SiteMap/                              # Sitemap
│   └── <SiteMapId>.xml
│
├── EnvironmentVariableDefinitions/       # Environment variables
│   └── <SchemaName>.json
│
├── ConnectionReferences/                 # Connection references
│   └── <LogicalName>.json
│
├── CanvasApps/                           # Canvas apps (msapp binary)
│   └── <AppName>_<GUID>.msapp
│   └── src/                             # Unpacked canvas source (if processed)
│
├── Resources/                            # Localization resources
│   └── resources.<locale>.resx
│
├── Other/                                # Other solution components
│   ├── Customizations.xml               # Remaining customizations
│   └── Relationships/                   # N:N relationships
│       └── <RelationshipName>.xml
│
└── <ManagedSuffix>/                      # If PackageType=Both, managed variants
    └── ...                               # Same structure with "_managed" suffix
```

**File formats:**
- `.xml` — Entity metadata, forms, views, roles, ribbons, customizations
- `.json` — Cloud flows, environment variables, connection references, canvas app metadata
- `.resx` — Localized string resources
- `.dll` — Plugin assemblies (binary)
- `.msapp` — Canvas apps (binary ZIP format)
- `.sql` — Stored procedures (Power Pages)

**Key file:** `Solution.xml` is always present and contains the solution's unique name, version, publisher reference, and root component list.

---

## 5. Licensing Concerns

### SolutionPackagerLib.dll
- **Proprietary Microsoft code.** SolutionPackagerLib is not open-source. It ships as part of the PAC CLI tool (`microsoft.powerapps.cli.tool` NuGet package).
- **Redistribution:** The PAC CLI is distributed under the [Microsoft Software License Terms](https://go.microsoft.com/fwlink/?linkid=2214684). Redistributing `SolutionPackagerLib.dll` in txc would require compliance with those terms.
- **No standalone NuGet package:** Microsoft has not published SolutionPackagerLib as a reusable NuGet package, which suggests they don't intend for third-party tools to directly reference it.

### Recommendations
1. **Runtime dependency approach (safest):** Don't bundle SolutionPackagerLib.dll. Instead, locate it from the user's existing PAC CLI installation at runtime. This avoids redistribution concerns entirely.
2. **PAC CLI detection:** Find PAC CLI via:
   - `dotnet tool list -g` (global tool)
   - Known install paths (`~/.dotnet/tools/.store/microsoft.powerapps.cli.tool/`)
   - `which pac` / `where pac`
3. **Fallback:** If SolutionPackagerLib.dll cannot be found, fall back to spawning `pac solution pack/unpack` as a subprocess (Option C).
4. **User guidance:** Document that txc requires PAC CLI to be installed for solution pack/unpack features.

---

## 6. Summary — Proposed txc Integration Architecture

```
txc solution pack/unpack
  → ISolutionPackagerService (txc abstraction)
    → Try: Load SolutionPackagerLib.dll from PAC CLI installation
      → new SolutionPackager(PackagerArguments).Run()
    → Fallback: spawn `pac solution pack/unpack` as process
```

### What txc Doesn't Have Yet
- No existing SolutionPackager references in `src/` (confirmed via grep).
- Need to add: service interface, DLL discovery, argument mapping, and CLI commands.

### Next Steps
1. Design `ISolutionPackagerService` interface for txc
2. Implement PAC CLI DLL discovery (locate installation, load via `AssemblyLoadContext`)
3. Create `txc solution pack` and `txc solution unpack` commands
4. Add `txc solution inspect` command using `SolutionPackager.InspectLocalFolder()` / `InspectZip()`
5. Handle Canvas App pack/unpack separately (PAC uses `ICanvasPacker`, which is in a different module)
