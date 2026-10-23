# Cloud Flow Development

Cloud flows are **solution components authored locally**: a definition JSON under `Workflows/` plus its `.json.data.xml`, shipped by the normal solution deployment. Never create, edit or test a flow in the environment. Connector operation IDs, parameter keys and values come from the tools below, never from memory.

## Three questions per connector step

1. **Which operation?** → `environment_connector_operation_search "<what the step does>"` (`--kind triggers` for a trigger). Take a hit that is not deprecated.
2. **Which parameters do I need to input?** → `environment_connector_operation_get <connector> <operation>`. One answer lists every parameter key with type, `required` and allowed values, plus the action type and a JSON skeleton. A parameter whose values are dynamic has `valuesFrom` (`list` or `fields`), `dependsOn`, and `getValues`: the exact command for question 3.
3. **What values can it take?** → run that `getValues` command, filling `--value` for each `dependsOn` parameter. One answer, by `status`:
   - `values` — the allowed values with labels; use the value.
   - `fields` — the sub-fields to use as `inputs.parameters` keys (`item/lastname`, `body/recipient`), with types and which are required.
   - `needs-values` — choose the listed parameters first; the message has the command for each.
   - `needs-consent` — the values are read through the user's connection (the message says whose). Ask once per connection, e.g. *"May I use your Dataverse connection mtj_dataverse to read table metadata? It only reads; nothing is changed."* If they agree, re-run with `--use-connection`, and keep passing it for that connection for the rest of the task.

Ask question 3 only for parameters you are going to set. Then write the step into the definition (see shape below) and `dotnet build`.

## When it stops you

| Says | Do |
|---|---|
| No bound connection reference for the connector | Ask the user to create one (in a solution: New → More → Connection reference) and re-run. Don't create connections yourself; never ask for a sample flow. |
| Several bound connection references | Ask which one; pass `--connection-reference <logical name>`. |
| `unavailable` (e.g. a folder picker) | Ask the user for the value. |

## Definition shape

The skeleton from question 2 has this shape; keep it when writing steps by hand.

```json
"connectionReferences": {
  "shared_commondataserviceforapps": {
    "runtimeSource": "embedded",
    "connection": { "connectionReferenceLogicalName": "<logical name>" },
    "api": { "name": "shared_commondataserviceforapps" }
  }
},
"definition": { "actions": {
  "List_contacts": {
    "type": "OpenApiConnection",
    "inputs": {
      "host": { "apiId": "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps",
                "connectionName": "shared_commondataserviceforapps", "operationId": "ListRecords" },
      "parameters": { "entityName": "contacts", "$filter": "_parentcustomerid_value eq @{triggerOutputs()?['body/accountid']}" },
      "authentication": "@parameters('$authentication')"
    },
    "runAfter": {}
  }
} }
```

- `type` is the action type from question 2 (`OpenApiConnectionWebhook` for webhook triggers and waiting approvals).
- `inputs.parameters` keys are flat, slash-joined names exactly as listed; numbers and booleans as JSON literals.
- `host.connectionName` is the step's key in `connectionReferences`; every connector step has the `authentication` line.
- The scaffold (`workspace_component_create` with `componentType: "Flow"`) creates the file with a manual trigger; replace it as needed.

## What NOT to Do

- ❌ Don't write operation IDs, parameter keys or values from memory
- ❌ Don't pass `--use-connection` before the user agreed
- ❌ Don't use operations flagged deprecated
- ❌ Don't rename the flow JSON file — `JsonFileName` in the `.data.xml` must match it

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
