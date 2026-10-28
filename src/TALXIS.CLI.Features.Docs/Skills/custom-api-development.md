# Custom API Development

## Key Concept

Custom APIs are custom messages (actions) in Dataverse that expose reusable business logic as callable endpoints. They require a backing plugin for implementation.

## When to Use Custom APIs vs Alternatives

- **Custom API** — reusable callable endpoint, typed request/response, synchronous with return value
- **Plugin** — reactive logic triggered by data events (Create, Update, Delete)
- **Power Automate** — low-code automation with connectors and approval flows

Choose Custom API when multiple clients or flows need to invoke the same operation.

## Registration Workflow

1. **Create the backing plugin** first (see [plugin-development](plugin-development.md))
2. **Scaffold Custom API** → `txc workspace component create pp-api-endpoint --output <project-dir> --param CustomizationPrefix=<prefix> --param ApiLogicalName=<Name> --param DisplayName="<Display name>" --param Description="<description>"`
3. **Shape request parameters** → the template also creates one placeholder string request parameter (`StringParameter`); rename it and add more by copying its folder under `customapirequestparameters/` and editing the XML (`name` is `{prefix}_{apiname}.{ParamName}`; the `type` code sets the data type)
4. **Shape response properties** → same for the placeholder `StringProperty` under `customapiresponseproperties/`

`<project-dir>` is the folder of the solution project that owns the API (find it in the repository, for example with `dotnet sln list`). There is no separate template for individual parameters or properties. Run `txc workspace component parameter list pp-api-endpoint` for the full, current parameter list.

## Naming Convention

`{prefix}` is the customization prefix of the target solution (`<CustomizationPrefix>` in the solution project's `Other/Solution.xml`, or `txc environment publisher list` for a live environment).

- API name: `{prefix}_{apilogicalname}` (e.g., `prefix_CalculateDiscount`)
- Request parameter: `{prefix}_{apiname}.{ParamName}`
- Response property: `{prefix}_{apiname}.{PropertyName}`

## What NOT to Do

- ❌ Don't create a Custom API without a backing plugin — it won't do anything
- ❌ Don't leave out the customization prefix in naming (`CustomizationPrefix`)
- ❌ Don't use Custom APIs for simple field calculations — use plugins on Update instead

See also: [plugin-development](plugin-development.md), [component-creation](component-creation.md)
