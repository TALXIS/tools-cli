# TALXIS: Migrate Script Library to the New Project Format

This README describes the **standard TALXIS migration flow** for moving a Script Library from an old structure to the new TALXIS project format.

TALXIS **templates** and TALXIS **build targets** handle build behavior automatically. The migration is mainly:
- create a new ScriptLibrary project using the **pp-script-library** template
- move the `TS` source folder into the new project
- remove legacy configuration from old files
- update names so build artifacts do **not** contain `publisherPrefix_` (prefix is added during build)
- reference the ScriptLibrary project from the updated Solution project

---

## Prerequisites

- Power Platform Solution must already be upgraded to **TALXIS.DevKit.SDK** format  
  (the upgrade is performed using **TALXIS.DevKit.Dataverse.Project.Upgrade.Tool**).
- TALXIS .NET templates installed (`dotnet new`), including **pp-script-library**

---

## Goal

Split the old layout into two projects:

- **ScriptLibrary project** — created by TALXIS **pp-script-library** template and contains script sources (folder `TS`)
- **Solution project** — upgraded to **TALXIS.DevKit.SDK** and built by TALXIS build targets

Then connect them using a standard `ProjectReference`.

---

## Step-by-step migration

### 1) Create a new ScriptLibrary project using pp-script-library template

Create the project using `dotnet new`:

```console
dotnet new pp-script-library -n UI.Scripts --LibraryName main
```

**Result:**
- A new ScriptLibrary project exists in the target folder
- The project already follows the TALXIS standard (SDK/targets/structure)

---

### 2) Move script sources (folder `TS`) into the new project

Move the **entire `TS` folder** from the old ScriptLibrary structure into the new ScriptLibrary project.

> The `TS` folder is the only folder you need to migrate. All required build-related structure is already inside `TS`.

**Result:**
- The new ScriptLibrary project contains the script sources under `TS`.

---

### 3) Remove legacy configuration from old files

After moving the `TS` folder, remove legacy/old-format configuration that belonged to the previous build pipeline.

**Result:**
- The new project is not tied to the old configuration and relies on TALXIS build targets.

---

### 4) Reference ScriptLibrary project from the upgraded Solution project

Open the upgraded Solution project (`*.csproj` in **TALXIS.DevKit.SDK** format) and add a `ProjectReference`:

```xml
<ItemGroup>
  <ProjectReference Include="..\MyCompany.ScriptLibrary.MyScript\MyCompany.ScriptLibrary.MyScript.csproj" />
</ItemGroup>
```

**Result:**
- The Solution project sees the ScriptLibrary project as a dependency.

---

### 5) Validation

1. Build the Solution project using the standard repository command dotnrt build.
2. Verify:
   - the Solution project contains a `ProjectReference` to the ScriptLibrary project
   - the ScriptLibrary project builds successfully as part of the Solution build

---

## Final structure (summary)

- **ScriptLibrary project** is created via TALXIS **pp-script-library** template → `TS` folder is migrated into it.
- **Solution project** is upgraded to **TALXIS.DevKit.SDK** and references the ScriptLibrary project via `ProjectReference`.
