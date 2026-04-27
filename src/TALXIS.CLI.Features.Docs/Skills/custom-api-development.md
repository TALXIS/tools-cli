# Custom API Development

## Key Concept

Custom APIs are custom messages (actions) in Dataverse that expose reusable business logic as callable endpoints. They define request parameters, response properties, and are backed by plugin logic for implementation.

## When to Use Custom APIs

| Approach | Best For |
|---|---|
| **Custom API** | Reusable business operations callable from any client, integrations, or Power Automate |
| **Plugin** | Reactive logic triggered by data events (Create, Update, Delete) |
| **Workflow** | Simple automated processes triggered by record changes |
| **Power Automate** | Low-code automation with connectors and approval flows |

**Choose Custom API when:**
- You need a callable endpoint (not just a data event trigger)
- Multiple clients or flows need to invoke the same operation
- You want typed request/response contracts
- You need synchronous execution with a return value

## Custom API XML Structure

### Main Definition (`customapi.xml`)
```xml
<customapi uniquename="{prefix}_{apiname}">
  <bindingtype>0</bindingtype>      <!-- 0=Global, 1=Entity, 2=EntityCollection -->
  <isfunction>0</isfunction>        <!-- 0=Action (POST), 1=Function (GET) -->
  <isprivate>0</isprivate>          <!-- 0=Public, 1=Private -->
  <allowedcustomprocessingsteptype>0</allowedcustomprocessingsteptype> <!-- 0=None, 1=Async, 2=Sync -->
  <plugintypeid>{plugin-type-guid}</plugintypeid>
</customapi>
```

### Request Parameters (`customapirequestparameter.xml`)
```xml
<customapirequestparameter>
  <uniquename>{prefix}_{apiname}.{ParamName}</uniquename>
  <name>{ParamName}</name>
  <type>10</type>                   <!-- See type codes below -->
  <isoptional>0</isoptional>        <!-- 0=Required, 1=Optional -->
  <logicalentityname></logicalentityname> <!-- Only for EntityReference types -->
</customapirequestparameter>
```

### Response Properties (`customapiresponseproperty.xml`)
```xml
<customapiresponseproperty>
  <uniquename>{prefix}_{apiname}.{PropertyName}</uniquename>
  <name>{PropertyName}</name>
  <type>10</type>                   <!-- See type codes below -->
  <logicalentityname></logicalentityname>
</customapiresponseproperty>
```

## Parameter & Property Type Codes

| Type Code | Data Type | Description |
|---|---|---|
| 0 | Boolean | True/false value |
| 1 | DateTime | Date and time value |
| 2 | Decimal | Decimal number |
| 3 | Entity | Full entity record |
| 4 | EntityCollection | Collection of entity records |
| 5 | EntityReference | Reference to a record |
| 6 | Float | Floating-point number |
| 7 | Integer | Whole number |
| 8 | Money | Currency value |
| 9 | Picklist | Option set value |
| 10 | String | Text value |
| 11 | StringArray | Array of strings |
| 12 | Guid | Unique identifier |

## Binding Types

| Value | Type | Description |
|---|---|---|
| 0 | Global | Not bound to any entity — callable independently |
| 1 | Entity | Bound to a specific entity — receives a record as input |
| 2 | EntityCollection | Bound to a collection — operates on multiple records |

## Naming Convention

- API name: `{customizationprefix}_{apilogicalname}` (e.g., `talxis_CalculateDiscount`)
- Request parameter: `{prefix}_{apiname}.{ParamName}` (e.g., `talxis_CalculateDiscount.OrderId`)
- Response property: `{prefix}_{apiname}.{PropertyName}` (e.g., `talxis_CalculateDiscount.DiscountAmount`)

## Plugin Backing

Custom APIs require a plugin to implement the business logic:

```csharp
public class CalculateDiscountPlugin : PluginBase
{
    public CalculateDiscountPlugin(string unsecure, string secure)
        : base(typeof(CalculateDiscountPlugin)) { }

    protected override void ExecutePlugin(LocalPluginContext context)
    {
        // Read request parameters
        var orderId = (EntityReference)context.PluginExecutionContext.InputParameters["OrderId"];

        // Business logic
        var discount = CalculateDiscount(context.OrganizationService, orderId);

        // Set response properties
        context.PluginExecutionContext.OutputParameters["DiscountAmount"] = new Money(discount);
    }
}
```

## Registration Workflow

1. **Create the plugin** that implements the API logic (see [plugin-development](plugin-development.md))
2. **Scaffold the Custom API definition** — creates `customapi.xml` with binding, function/action type, and plugin reference
3. **Add request parameters** — define each input parameter with type and required/optional flag
4. **Add response properties** — define each output property with type
5. **Register in solution** — ensure all components are added to the solution

```
Tool: workspace_component_create
Parameters: { componentType: "CustomAPI", SolutionRootPath: "Declarations", ... }
```

## Testing Custom APIs

### Via Organization Service (C#)
```csharp
var request = new OrganizationRequest("talxis_CalculateDiscount");
request["OrderId"] = new EntityReference("salesorder", orderId);
var response = service.Execute(request);
var discount = (Money)response["DiscountAmount"];
```

### Via Web API (HTTP)
```http
POST /api/data/v9.2/talxis_CalculateDiscount
Content-Type: application/json

{
  "OrderId": { "@odata.type": "#Microsoft.Dynamics.CRM.salesorder", "salesorderid": "{guid}" }
}
```

## What NOT to Do

- ❌ Don't create a Custom API without a backing plugin — it won't do anything
- ❌ Don't use Generic type codes — always specify exact types for parameters
- ❌ Don't forget the customization prefix in naming — it's required
- ❌ Don't use Custom APIs for simple field calculations — use plugins on Update instead

See also: [plugin-development](plugin-development.md), [component-creation](component-creation.md)
