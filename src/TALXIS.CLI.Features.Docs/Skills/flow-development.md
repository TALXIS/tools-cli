# Cloud Flow Development

## Key Concept

Power Automate cloud flows are **solution components authored locally**: a JSON definition under `Workflows/` (`<prefix>_<name>-<GUID>.json`) plus a sibling `.json.data.xml` metadata file. They reach the environment through the normal solution deployment. Never create or edit a flow in the environment to get it working.

The scaffolded definition only has a manual trigger. Everything else is written by hand, which makes it the easiest place in a workspace to invent an operation ID, a parameter name or an action type. Connector metadata is too large and changes too often to recall, so **every connector trigger and action is grounded in the connected environment** with the read-only connector tools below.

## Authoring Workflow

1. **Scaffold** → `workspace_component_create` with `componentType: "Flow"` (template `pp-flow`). Call `workspace_component_parameter_list` for its parameters.
2. **Find the operation**:
   - Connector unknown → `environment_connector_operation_search` with what the step should do (e.g. `"add a row"`), optionally `--kind actions|triggers`.
   - Connector known → `environment_connector_get <connector> --search <text>` lists its operations with their kind (`action`, `trigger`, `webhook-trigger`).
   - Browse → `environment_connector_list --search <text>`.
3. **Read the exact spec** → `environment_connector_operation_get <connector> <operation>` for **every** operation you use. Copy the parameter keys, allowed values and `Action type` verbatim. Dynamic parameters are resolved as described under *Dynamically Resolved Parameters*.
4. **Bind a connection reference** → `environment_connection-reference_list --connector <connector>`. Use an existing reference's logical name; if none exists, ask the user which one to use rather than inventing one.
5. **Build** → `dotnet build`, then follow the [deployment workflow](deployment-workflow.md).

## Definition Shape

The scaffold already declares `$connections` and `$authentication` in `properties.definition.parameters`; keep both.

Each connector used is declared once in `properties.connectionReferences`, keyed by the name actions use as `host.connectionName`:

```json
"connectionReferences": {
  "shared_commondataserviceforapps": {
    "runtimeSource": "embedded",
    "connection": { "connectionReferenceLogicalName": "contoso_sharedcommondataserviceforapps_1a2b3" },
    "api": { "name": "shared_commondataserviceforapps" }
  }
}
```

A connector action, here Dataverse `ListRecords`:

```json
"List_active_accounts": {
  "type": "OpenApiConnection",
  "inputs": {
    "host": {
      "apiId": "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps",
      "connectionName": "shared_commondataserviceforapps",
      "operationId": "ListRecords"
    },
    "parameters": {
      "entityName": "accounts",
      "$select": "name,accountnumber",
      "$filter": "statecode eq 0"
    },
    "authentication": "@parameters('$authentication')"
  },
  "runAfter": {}
}
```

Rules:

- **`type` is the `Action type` from `environment_connector_operation_get`.** Standard operations are `OpenApiConnection`; long-running ones such as an approval that waits for a response are `OpenApiConnectionWebhook`. A webhook operation declared as `OpenApiConnection` saves fine and then never resumes. `webhook-trigger` operations go under `triggers` as `OpenApiConnectionWebhook`.
- **`host.apiId`** is the connector's API id (printed by `environment_connector_get`), **`host.connectionName`** a key of `connectionReferences`.
- **`inputs.parameters` keys are the parameter names exactly as listed**, including the slash-joined nested ones (`emailMessage/To`, `emailMessage/Subject`). Never nest them as JSON objects.
- **`authentication`** is `"@parameters('$authentication')"` on every connector trigger and action, as in exported solutions.
- `runAfter` references existing action names; the first action uses `{}`.
- Expressions use `@{...}` with functions such as `triggerOutputs()`, `outputs('<Action>')?['body/value']`, `concat()`, `utcNow()`.

## Dynamically Resolved Parameters

`environment_connector_operation_get` marks parameters that are not statically described:

- **`values from: <operation>`** — allowed values are fetched at design time (e.g. `entityName` from `GetEntities`), so there is no enum to copy.
- **`sub-schema from: <operation>`** — the parameter is an object whose fields depend on other parameters; `$ref:<name>` shows which.

**Dataverse rows are the common case and can be grounded.** For `shared_commondataserviceforapps` the `item` of `CreateRecord`/`UpdateRecord` is resolved from `entityName`: its keys are `item/<column logical name>`, with `@odata.bind` for lookups (`item/primarycontactid@odata.bind`). `entityName` is the table's entity set name (plural, e.g. `accounts`). Take the column names from the workspace's table definition, or `environment_entity_describe` for a deployed table, never from memory.

**Anything else is resolved through a connection, with the user's consent.** The designer fills these fields by calling the connector's lookup operations through a connection, and `environment_connector_operation_resolve` makes the same calls. Because a connection acts as the person who owns it, this is always two steps:

1. **Plan** → `environment_connector_operation_resolve <connector> <operation> --connection-reference <logical name> --value <name>=<value> ...` without `--use-connection`. Pass every choice the lookups depend on (the `$ref:` names, e.g. `--value "poster=Flow bot" --value "location=Chat with Flow bot"`). Nothing is called through the connection; the output says whose connection it is and which read-only lookups would be made.
2. **Ask** → show the user that plan in plain words: which connection and who owns it, that the calls run as that person, which lookups and what they return, and that nothing is created or changed. Ask whether to go ahead. Do not phrase it as a request for them to do the work.
3. **Run** → only if they agree, repeat the call with `--use-connection`. The output lists the resolved keys (`body/recipient`, `body/messageBody`) and allowed values; copy them as with `operation_get`.

If a lookup is `skipped`, its reason says what is missing: usually another `--value` (resolve in the order the `$ref:`s imply), a bound connection, or a lookup txc does not make (only GETs are sent through a connection). If no connection reference for the connector is bound, ask the user to bind one; never create connections, flows or anything else in the environment to discover field names, and never ask the user to build a sample flow.

## What NOT to Do

- ❌ Don't write operation IDs or parameter names from memory — call `environment_connector_operation_get` first
- ❌ Don't guess enum values — use the listed allowed values
- ❌ Don't use operations flagged `[deprecated]` — search for the current replacement
- ❌ Don't create, edit or test flows in the environment — they are solution components authored here
- ❌ Don't reference a connection reference that does not exist in the environment
- ❌ Don't rename the flow JSON file by hand — `JsonFileName` in the `.data.xml` must keep matching it
- ❌ Don't use premium triggers (e.g. the HTTP request trigger) without checking licensing with the user

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
