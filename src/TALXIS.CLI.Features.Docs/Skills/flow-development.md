# Cloud Flow Development

Cloud flows are **solution components authored locally**: a definition JSON under `Workflows/` plus its `.json.data.xml`, shipped by the normal solution deployment. Never create, edit or test a flow in the environment. Connector operation IDs, parameter keys, values and output expressions come from the commands below, never from memory.

Commands are shown as CLI; the MCP tool name is in brackets (spaces become `_`).

## Three questions per connector step

1. **Which operation?** → `txc environment connector operation search "<what the step does>"` (add `--kind triggers` for a trigger) [`environment_connector_operation_search`]. Take a hit that is not deprecated.
2. **Which parameters does it need, and what does it return?** → `txc environment connector operation get <connector> <operation>` [`environment_connector_operation_get`]. One answer lists:
   - every parameter key with type, `required` and allowed values; a dynamic one also has `dependsOn` and `getValues`, the exact command for question 3;
   - every output with the expression that reads it in later steps (`@triggerOutputs()?['body/…']`, `@outputs('Step')?['body/value']`); a dynamic output (e.g. the columns of the chosen table) has `getFields`;
   - the action type and a JSON skeleton.
3. **What values can a dynamic parameter take / which fields does a dynamic output have?** → run the `getValues` or `getFields` command it printed, filling each `--value` [`environment_connector_operation_parameter_get`, `environment_connector_operation_output_get`]. Add `--search <text>` to narrow long lists. The answer's `status`:
   - `values` — allowed values with labels; use the value.
   - `fields` — sub-fields: input keys to set (`item/lastname`, `body/recipient`) or outputs with their read expression (`@items('<loop>')?['emailaddress1']`).
   - `needs-values` — choose the listed parameters first; the message has the command for each.
   - `needs-consent` — the answer is read through the user's connection (the message says which and whose). Ask once per connection, e.g. *"May I use your Dataverse connection mtj_dataverse to read table metadata? It only reads; nothing is changed."* If they agree, re-run with `--use-connection` and keep passing it for that connection for the rest of the task.

Ask question 3 only for parameters you set and outputs you read. Then write the step into the definition and `dotnet build`.

## When it stops you

| Says | Do |
|---|---|
| No bound connection reference for the connector | Ask the user to create one (in a solution: New → More → Connection reference) and re-run. Don't create connections yourself; never ask for a sample flow. |
| Several bound connection references | The message describes each (display name, owner, status); ask the user which the flow should use, then pass `--connection-reference <logical name>`. `txc environment connection-reference list` shows them too. |
| `unavailable` (e.g. a folder picker) | Ask the user for the value. |

## Workspace

- New repository: `git init`, `dotnet new sln`, `mkdir src`, then `txc workspace component create pp-solution -o src/<Project> --param PublisherName=<name> --param PublisherPrefix=<prefix>`.
- Flow: `txc workspace component create pp-flow -o src/<Project> --param PublisherPrefix=<prefix> --param LogicalName=<name>`. It creates the definition with a manual trigger; replace it as needed. Parameters are passed as `--param Name=value` (`txc workspace component parameter list pp-flow` lists them).

## Definition shape

The skeleton from question 2 has this shape. The `connectionReferences` entry goes into `properties.connectionReferences`, the step into `properties.definition.triggers` or `properties.definition.actions`.

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
  },
  "For_each_contact": {
    "type": "Foreach",
    "foreach": "@outputs('List_contacts')?['body/value']",
    "actions": { },
    "runAfter": { "List_contacts": [ "Succeeded" ] }
  }
} }
```

- `type` is the action type from question 2 (`OpenApiConnectionWebhook` for webhook triggers and waiting approvals).
- `inputs.parameters` keys are flat, slash-joined names exactly as listed; numbers and booleans as JSON literals.
- `host.connectionName` is the step's key in `connectionReferences`; every connector step has the `authentication` line.
- Outputs of an array (`body/value[]/…`) are read inside a `Foreach` over it with `@items('<Foreach name>')?['<field>']`; replace `<loop over …>` in the printed expression with the loop's name. Use `@{...}` to embed an expression inside text.
- Dataverse lookup columns are filtered as `_<column>_value` (`_parentcustomerid_value`).

## What NOT to Do

- ❌ Don't write operation IDs, parameter keys, values or output keys from memory
- ❌ Don't pass `--use-connection` before the user agreed
- ❌ Don't use operations flagged deprecated
- ❌ Don't rename the flow JSON file — `JsonFileName` in the `.data.xml` must match it

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
