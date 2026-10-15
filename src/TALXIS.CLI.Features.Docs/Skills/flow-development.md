# Power Automate Flow Development

## Key Concept

Cloud flows in a TALXIS workspace are **solution components authored locally**, not objects created through a flow API. You scaffold a flow, expand its definition JSON by hand, and ship it through the normal solution pipeline.

Scaffolding produces a base JSON definition plus the component XML. The XML is schema-checked by `workspace_validate`. **The JSON is not**, and expanding it is free-text authoring — which makes it the single easiest place in the workspace to invent an operation ID, a parameter name, or an action type that does not exist. Connector metadata is too large and too volatile to recall correctly.

So: never write a connector action from memory, and never skip validation.

## Authoring Workflow

1. **Scaffold** → `workspace_component_create` with `componentType: "Workflow"` (the alias `Flow` resolves to the same template).
2. **Find the operation**:
   - Know the connector → `environment_connector_get` with a `query` to filter its operation index.
   - Don't know it → `environment_connector_operation_search` across connectors.
   - Browsing what exists → `environment_connector_list`.
3. **Read the exact spec** → `environment_connector_operation_get` for every operation you are about to use. Copy the parameter names, types, allowed values and the returned `actionType` verbatim.
4. **Bind connections** → `environment_connection_list`. A solution-aware flow binds by connection reference logical name, not by a raw connection.
5. **Validate** → `environment_flow_validate` against the component. Fix every error before continuing.
6. **Deploy** → the normal solution pipeline (`environment_solution_import`). There is no flow-create API step.

## Definition Rules

Declare both definition parameters whenever the flow calls a connector:

```json
"parameters": {
  "$authentication": { "defaultValue": {}, "type": "SecureObject" },
  "$connections": { "defaultValue": {}, "type": "Object" }
}
```

A connector action looks like this:

```json
{
  "type": "OpenApiConnection",
  "inputs": {
    "parameters": { "recipient/to": "someone@example.com" },
    "host": {
      "apiId": "/providers/Microsoft.PowerApps/apis/shared_teams",
      "operationId": "PostMessageToConversation",
      "connectionName": "shared_teams"
    }
  },
  "runAfter": {}
}
```

- **Use the action type the metadata gives you.** Standard operations are `OpenApiConnection`. Long-running webhook operations, such as an approval that waits for a response, are `OpenApiConnectionWebhook`. Declaring a webhook operation as `OpenApiConnection` is accepted on save and then the flow never resumes.
- **Do not put `authentication` in action inputs.** The platform injects it.
- **Use `"source": "Embedded"`** in connection references, never `Invoker`.
- **`kind: "Http"` triggers need Premium.** Use `kind: "Button"` otherwise.

## Dynamically Resolved Parameters

Some parameters are not statically described. `environment_connector_operation_get` reports where their values come from:

- **`values from: <operation>`** — the allowed values are fetched at design time, so there is no static enum to check against.
- **`sub-schema from: <operation>`** — the parameter is an object whose real fields are resolved per the values of *other* parameters. A `$ref:<name>` argument shows which ones.

Teams `PostMessageToConversation` is the common example. It takes `poster`, `location` and `body`, where `body`'s fields are resolved by `GetUnifiedActionSchema` from the chosen `poster` and `location`. The flattened `body/...` names a finished action uses therefore cannot be read from the operation spec alone — they depend on those choices and require an existing connection to resolve.

When you hit this, do not invent the sub-field names. Copy them from a working flow that already uses the same poster and location combination, or build that one action in the designer once and export it.

## What NOT to Do

- ❌ Don't write parameter names from memory — call `environment_connector_operation_get` first
- ❌ Don't guess enum values — the operation spec lists the allowed ones
- ❌ Don't skip `environment_flow_validate` because the JSON "looks right"
- ❌ Don't create flows through an API — they are solution components here
- ❌ Don't use deprecated operations:
  - Teams `PostMessageToChannel`, `PostMessageToChannelV2/V3`, `PostUserNotification` → `PostMessageToConversation`
  - Teams `PostUserAdaptiveCard`, `PostChannelAdaptiveCard` → `PostCardToConversation`
  - Outlook `SendEmail` → `SendEmailV2`; `OnNewEmail`/`OnNewEmailV2` → `OnNewEmailV3`
  - Approvals `approvalSubscribeV2` → `StartAndWaitForAnApproval`
  - Planner `CreateTask`/`CreateTask_V2` → `CreateTask_V3`

## Validation

`environment_flow_validate` cross-checks the local definition against the live environment and reports a JSON pointer per finding:

| Check | Catches |
|---|---|
| Connector and operation exist | Invented connectors and operation IDs |
| Declared type matches metadata | An approval declared as a plain connection |
| Parameter names exist | Misspellings and imagined parameters |
| Required parameters present | Omissions |
| Enum values allowed | Guessed literals |
| Connection references resolve | Bindings a solution import cannot satisfy |

Warnings (deprecated operations, premium triggers) do not fail the command; errors exit with code 2. Add `--connection-check` to also confirm each reference exists in the environment, or `--offline` to run only the structural rules.

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md), [solution-layering](solution-layering.md)
