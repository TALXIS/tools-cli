# TALXIS: Migrating Power Platform (Dataverse) plugins to the new project format

This README describes the **standard TALXIS process** for migrating plugins from the old project format to the new one.

TALXIS **templates** and TALXIS **build targets** automatically take care of building/packaging. Migration basically comes down to:
- creating a new plugin project using a TALXIS template
- moving the source code
- referencing the plugin project from the solution project via `ProjectReference`
- bringing the `PluginAssemblies` folder structure to the standard

---

## Goal

Split the old structure (where plugin code could live in the same project/folder tree as the solution) into two projects:

- **Plugin project** (plugins) — created using a **TALXIS `dotnet new` template**
- **Solution project** (Dataverse solution) — built using **TALXIS build targets**

After that, the projects are linked with a standard `ProjectReference`.

---

## Requirements

- TALXIS.DevKit.SDK is installed
- TALXIS templates (`dotnet new`) are installed
- Plugin source code is available in the old structure
- Power Platform Solution must already be upgraded to the TALXIS.DevKit.SDK format (the upgrade is performed using TALXIS.DevKit.Dataverse.Project.Upgrade.Tool)

---

## Step-by-step migration

### 1) Create a new Plugin project using a TALXIS template

Create a new plugin project with `dotnet new`:

```console
dotnet new pp-plugin `
--output "src/Plugins.Project" `
--PublisherName "tomas" `
--SigningKeyFilePath "<Path to snk key file>" `
--Company "NETWORG" `
--allow-scripts yes
```

**Result:**
- `Plugins.Project.csproj` is created

---

### 2) Move the source code into the new Plugin project

Move the source code from the old project into the new plugin project.

**Move:**
- plugin classes (`*.cs`)
- helper/shared classes that were used within the same plugin assembly
- required resources used by plugins (only if they are actually needed): `*.resx`, `*.json`, `*.xml`, etc.

**Result:**
- the new plugin project contains the full set of sources required to build the plugin assembly.

---

### 3) Reference the Plugin project from the Solution project via `ProjectReference`

Open the solution project `Solution.csproj` (the one built by TALXIS build targets) and add a `ProjectReference`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Plugins.Project\Plugins.Project.csproj" />
</ItemGroup>
```

**Result:**
- the solution project sees the plugin project as a dependency.

---

### 4) Bring `PluginAssemblies` to the standard structure

TALXIS expects the plugin assembly to be stored in a separate subfolder under `PluginAssemblies`.

Required structure:

```text
PluginAssemblies\<AssemblyName>-<AssemblyGuid.ToUpper()>\
```

Where:
- `AssemblyName` = the plugin name
- `AssemblyGuid` = the GUID id (in uppercase) of the plugin assembly 
Example:

```text
PluginAssemblies\Plugins.Project-571BCBFE-AAF4-4BE5-A8AE-424E878CBDEC\
```

#### What you need to do

1. Under `PluginAssemblies`, create a subfolder `AssemblyName-AssemblyGuid`.
2. If your plugin assembly files currently live directly under `PluginAssemblies\`, move them into the created subfolder.

---

### 5) Plugin Assembly behavior (TALXIS)

TALXIS build targets support both scenarios:

1. **Generate Plugin Assembly automatically** (if the assembly does not exist yet).
2. **Update an existing Plugin Assembly** (if the assembly already exists).

This means the folder:

```text
PluginAssemblies\<AssemblyName>-<AssemblyGuid>\
```

can be used in two ways:

- **Empty folder**: TALXIS build targets will generate the required files/metadata during the build.
- **Folder with files**: you can pre-copy the assembly from a previous version (old repo/branch/release) into this folder — TALXIS build targets will take it as a base and update it to the current state.

---

### 6) Verification

1. Build the solution project using the standard `dotnet build` command.
2. Verify that:
   - the solution project has a `ProjectReference` to the plugin project
   - `PluginAssemblies` contains a subfolder `AssemblyName-AssemblyGuid`
   - plugin assembly files are located inside that subfolder
   - TALXIS build targets generate or update the assembly as expected

---

## Summary

- The **plugin project** is created using a TALXIS template → then plugin sources are moved into it.
- The **solution project** references the plugin project via `ProjectReference`.
- **PluginAssemblies** matches the standard: `PluginAssemblies\<AssemblyName>-<AssemblyGuid>\`.
- The assembly can be generated automatically or copied from a previous version and then updated by TALXIS build targets.
