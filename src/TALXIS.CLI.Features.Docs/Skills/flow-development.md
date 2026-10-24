# Cloud Flow Development

## Key Concept

Power Automate cloud flows are solution components stored locally as source: a JSON definition (Logic Apps workflow schema) under `Workflows/` plus a sibling `.data.xml` metadata file (`Category=5`, `Type=1`). Scaffolding creates the flow locally - it reaches the environment through the normal solution deployment, never by clicking it together in the designer first.

## Flow Scaffolding Chain

1. **Scaffold the flow** → `workspace_component_create` with `componentType: "pp-flow"` and a `Trigger` choice:
   - `manual` - instant flow started by a button (default)
   - `recurrence` - scheduled flow; set `RecurrenceFrequency` (Minute/Hour/Day/Week/Month) and `RecurrenceInterval`
   - `dataverse` - automated flow on a Dataverse row event; set `EntityLogicalName`, `TriggerEvent`, `Scope`, optional `FilteringAttributes`, and `ConnectionReferenceLogicalName`
2. **Author actions** by editing the generated `Workflows/<prefix>_<name>-<guid>.json` (rules below)
3. **Build locally** to validate and pack: `dotnet build`
4. Follow the [deployment workflow](deployment-workflow.md)

Call `workspace_component_parameter_list` for required parameters at each step.

## Dataverse Trigger Parameters

- **`TriggerEvent`** → `subscriptionRequest/message`: create=1, delete=2, update=3, create-or-update=4, create-or-delete=5, update-or-delete=6, create-or-update-or-delete=7
- **`Scope`** → `subscriptionRequest/scope`: user=1, business-unit=2, parent-child-business-unit=3, organization=4 (default)
- **`FilteringAttributes`** - comma-separated attribute logical names; the flow only fires when one of them changes (meaningful for events that include update)
- **`EntityLogicalName`** - entity logical name **with** publisher prefix (e.g. `udpp_warehouseitem`)
- **`ConnectionReferenceLogicalName`** - logical name of an **existing** Dataverse connection reference; connection references are declared under `<connectionreferences>` in `Other/Customizations.xml`

## Authoring Actions in the Definition JSON

- The definition must declare both `$connections` and `$authentication` parameters (the template already does)
- Standard connector actions use `"type": "OpenApiConnection"` (never `ApiConnection`); webhook-style operations (e.g. Approvals) use `"type": "OpenApiConnectionWebhook"`
- Every connector action's `host.connectionName` must match a key in `properties.connectionReferences`; each entry needs `"runtimeSource": "embedded"` and a real `connectionReferenceLogicalName`
- `runAfter` must reference existing action names; the first action uses `"runAfter": {}`
- Every `OpenApiConnection`/`OpenApiConnectionWebhook` trigger and action passes `"authentication": "@parameters('$authentication')"` inside `inputs` (real exported flows carry it for all connectors, not just Dataverse)
- Never guess connector `operationId`, `apiId`, or parameter names - discover them live: `environment_connector_list` (available connectors), `environment_connector_operation_list` (operations with kind action/trigger/webhook-trigger), `environment_connector_operation_get` (exact parameter names, types, enums; body leaves are already slash-joined like `emailMessage/To`)
- `webhook-trigger` operations go into `triggers` as `OpenApiConnectionWebhook`; parameters marked dynamic require values resolved at authoring time
- `operation get` names the operation behind each dynamic parameter (`dynamicSource` in JSON): `values from` lists the allowed values, `sub-schema from` builds the body fields; `$ref:<name>` arguments mean that parameter must be chosen first (e.g. Teams `PostMessageToConversation`: pick `poster`, then `location`, and only then are the `body/...` fields known). The concrete fields behind `sub-schema from` cannot be read from the spec, so never invent them; take them from a real exported flow or ask the user
- Dataverse `CreateRecord`/`UpdateRecord` bodies are dynamic: keys are `item/{attribute logical name}` from the entity schema, with the `@odata.bind` suffix for lookups (e.g. `item/ownerid@odata.bind`)
- Expressions use `@{...}` interpolation with functions like `triggerOutputs()`, `outputs('<Action>')?['body/field']`, `concat()`, `utcNow()`

## What NOT to Do

- ❌ Don't create flows directly in the environment - scaffold locally so the flow lives in source control
- ❌ Don't scaffold a `dataverse`-trigger flow before the triggering entity exists in the workspace
- ❌ Don't reference a connection reference that doesn't exist - declare it in `Other/Customizations.xml` first
- ❌ Don't invent connector operation parameters - mirror a real flow or a designer export
- ❌ Don't use premium triggers (e.g. HTTP request) without checking licensing; if a DLP policy blocks a trigger connector, fall back to a `recurrence` trigger that polls instead
- ❌ Don't rename the flow JSON file by hand - `JsonFileName` inside the `.data.xml` must keep matching it exactly

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
