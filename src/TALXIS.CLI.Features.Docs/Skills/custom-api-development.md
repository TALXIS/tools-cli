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
2. **Scaffold Custom API** → `workspace_component_create` with `componentType: "CustomAPI"`
3. **Add request parameters** → scaffold each input parameter
4. **Add response properties** → scaffold each output property

Call `workspace_component_parameter_list` for required parameters at each step.

## Naming Convention

- API name: `{prefix}_{apilogicalname}` (e.g., `talxis_CalculateDiscount`)
- Request parameter: `{prefix}_{apiname}.{ParamName}`
- Response property: `{prefix}_{apiname}.{PropertyName}`

## What NOT to Do

- ❌ Don't create a Custom API without a backing plugin — it won't do anything
- ❌ Don't forget the customization prefix in naming — it's required
- ❌ Don't use Custom APIs for simple field calculations — use plugins on Update instead

See also: [plugin-development](plugin-development.md), [component-creation](component-creation.md)
