# Plugin & Custom Code Development

## Key Concept

Plugins are server-side .NET classes that execute custom business logic in response to Dataverse events (messages). Plugin development follows a strict chain: create project → write plugin class → register assembly → register steps.

All plugin scaffolding uses `txc` workspace tools — this keeps everything in source control and locally reviewable before deployment.

## Plugin Project Structure

Plugins follow the `Plugins.{Domain}/` naming convention within the `src/` directory:

```
src/
├─ Plugins.Sales/
│  ├─ Plugins.Sales.csproj       # ProjectType=Plugin, SignAssembly=true
│  ├─ PluginBase.cs               # Shared base class with LocalPluginContext
│  ├─ CreateOrderPlugin.cs        # {Action}{Entity}Plugin.cs naming
│  └─ ValidateContactPlugin.cs
├─ Plugins.Service/
│  └─ ...
```

**Project file essentials (.csproj):**
- `ProjectType=Plugin` — tells the build SDK this is a plugin assembly
- `SignAssembly=true` — required for Dataverse registration
- Target framework: `net462` (Dataverse sandbox requirement)

## Plugin Class Naming Convention

Follow the `{Action}{Entity}Plugin.cs` pattern:
- `ValidateContactPlugin.cs` — validates Contact records
- `CalculateInvoiceTotalPlugin.cs` — calculates Invoice totals
- `AutoNumberAccountPlugin.cs` — auto-numbers Account records
- `SetDefaultsOnCreateLeadPlugin.cs` — sets defaults when Lead is created

## Plugin Base Class Pattern

The `PluginBase.cs` provides a `LocalPluginContext` with:
- **IServiceProvider** — Dataverse service container
- **IPluginExecutionContext** — message name, entity, stage, depth
- **IOrganizationService** — CRUD operations on Dataverse
- **ITracingService** — diagnostic logging (visible in Plugin Trace Log)

```csharp
// Typical plugin structure inheriting PluginBase
public class ValidateContactPlugin : PluginBase
{
    public ValidateContactPlugin(string unsecureConfig, string secureConfig)
        : base(typeof(ValidateContactPlugin))
    {
        // Register step handler
        RegisteredEvents.Add(new Tuple<int, string, string, Action<LocalPluginContext>>(
            (int)StageEnum.PreOperation, "Create", "contact", ExecutePlugin));
    }

    private void ExecutePlugin(LocalPluginContext context)
    {
        var target = context.PluginExecutionContext.InputParameters["Target"] as Entity;
        // Validation logic here
    }
}
```

## Registration Chain

Plugin registration is a 3-step chain. **Order matters** — each step depends on the previous one.

### Step 1: Create Plugin Project
```
Tool: workspace_component_create
Parameters: { componentType: "PluginProject", SolutionRootPath: "Declarations", ... }
```
Generates the `.csproj` with correct SDK, signing, and target framework.

### Step 2: Register Assembly
```
Tool: workspace_component_create
Parameters: { componentType: "PluginAssembly", SolutionRootPath: "Declarations", ... }
```
Adds assembly registration XML to the solution with `IsolationMode`, `SourceType`, and `Description`.

### Step 3: Register Steps
```
Tool: workspace_component_create
Parameters: { componentType: "PluginStep", SolutionRootPath: "Declarations", ... }
```
Registers individual plugin steps specifying the message, stage, entity, and filtering attributes.

## Execution Stages

| Stage | Value | When it Runs | Use Case |
|---|---|---|---|
| Pre-validation | 10 | Before main transaction | Input validation, reject bad data early |
| Pre-operation | 20 | Inside transaction, before DB write | Modify data before save, calculated fields |
| Post-operation | 40 | Inside transaction, after DB write | Trigger side effects, create related records |

## SDK Message GUIDs

Common SDK messages used in step registration:

| Message | GUID |
|---|---|
| Create | `9ebdbb1b-ea3e-db11-86a7-000a3a5473e8` |
| Update | `caca167b-a0db-4b71-a5a8-2d7880c3e4cf` |
| Delete | `2beb0117-cf66-dc11-b865-0019b9b35a02` |
| Retrieve | `7beb0117-cf66-dc11-b865-0019b9b35a02` |
| RetrieveMultiple | `7feb0117-cf66-dc11-b865-0019b9b35a02` |

## Common Plugin Patterns

### Validation Plugin (Pre-validation)
Reject invalid data before it enters the pipeline:
```csharp
if (string.IsNullOrEmpty(target.GetAttributeValue<string>("emailaddress1")))
    throw new InvalidPluginExecutionException("Email address is required.");
```

### Auto-Numbering Plugin (Pre-operation)
Generate sequential numbers before save:
```csharp
var nextNumber = GetNextSequence(context.OrganizationService, "account");
target["accountnumber"] = $"ACC-{nextNumber:D6}";
```

### Field Calculation Plugin (Pre-operation)
Calculate derived fields before save:
```csharp
var quantity = target.GetAttributeValue<int>("quantity");
var unitPrice = target.GetAttributeValue<Money>("priceperunit");
target["extendedamount"] = new Money(quantity * unitPrice.Value);
```

### Side-Effect Plugin (Post-operation)
Create related records or trigger workflows after save:
```csharp
var followUp = new Entity("task");
followUp["subject"] = $"Follow up on {target.GetAttributeValue<string>("name")}";
followUp["regardingobjectid"] = target.ToEntityReference();
context.OrganizationService.Create(followUp);
```

## Testing Plugins

Use FakeXrmEasy for unit testing plugins without a live Dataverse environment:

```csharp
public class ValidateContactPluginTests : FakeXrmEasyTestBase
{
    [Fact]
    public void Should_Reject_Contact_Without_Email()
    {
        var target = new Entity("contact") { Id = Guid.NewGuid() };
        // No emailaddress1 set

        var context = _context.GetDefaultPluginContext();
        context.InputParameters["Target"] = target;
        context.MessageName = "Create";

        Assert.Throws<InvalidPluginExecutionException>(
            () => _context.ExecutePluginWith<ValidateContactPlugin>(context));
    }
}
```

## What NOT to Do

- ❌ Don't register assembly before creating the plugin project — the build won't find it
- ❌ Don't skip `SignAssembly=true` — Dataverse rejects unsigned assemblies
- ❌ Don't use Post-operation for validation — data is already saved
- ❌ Don't use Pre-validation if you need related record data — it's outside the transaction
- ❌ Don't hardcode SDK message GUIDs from memory — use the reference table above

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
