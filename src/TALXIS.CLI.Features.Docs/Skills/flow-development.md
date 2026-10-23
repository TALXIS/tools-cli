# Cloud Flow Development

Cloud flows are **solution components authored locally**: a definition JSON under `Workflows/` plus its `.json.data.xml`, shipped by the normal solution deployment. Never create, edit or test a flow in the environment. Every connector trigger and action comes from the tools below, never from memory: they return the exact JSON to paste.

## Fast Path

Per connector step, at most three calls:

1. **Find it** → `environment_connector_operation_search "<what the step does>"` (add `--kind triggers` for a trigger). Pick a hit that is not deprecated; note its connector and operation.
2. **Get the snippet** → `environment_connector_operation_get <connector> <operation>`. Its `snippet` holds the `connectionReferences` entry and the trigger or action, with every required parameter present.
   - No `<placeholder>` left except plain values (a filter, a message) → fill them and paste. Done.
   - A placeholder says *fields depend on other choices*, or the value must be one of a fetched list (`values from:`) → step 3.
3. **Resolve it** (only for dynamic parameters) → `environment_connector_operation_resolve <connector> <operation> --value <name>=<value> ...`, passing **every** choice you already know in one call (`--value entityName=contacts`, `--value "poster=Flow bot" --value "location=Chat with Flow bot"`). The bound connection reference is picked automatically.
   - This first call only **plans**. Tell the user in one or two sentences whose connection it is (owner shown in the output), that read-only metadata lookups would run as them, and that nothing is created or changed; ask to go ahead.
   - If they agree, repeat the same call with `--use-connection`. The snippet now has the real keys (`item/<column>`, `body/recipient`) and your values; act on any `WARNING:` (e.g. `contact` vs `contacts`), then paste.

Then scaffold/merge and build:

- **Scaffold once** → `workspace_component_create` with `componentType: "Flow"`; it creates the definition with a manual trigger. Replace `triggers` and fill `actions` from the snippets; merge every snippet's `connectionReferences` entry into `properties.connectionReferences`.
- **Build** → `dotnet build`, then the [deployment workflow](deployment-workflow.md).

## When a tool stops you

| Output says | Do |
|---|---|
| No bound connection reference for the connector | Ask the user to create one (in a solution: New → More → Connection reference) and re-run. Don't create connections yourself, and never ask for a sample flow. |
| Several bound connection references | Ask which one, pass `--connection-reference <logical name>`. |
| Lookup `skipped` — needs a value for `'x'` | Add `--value x=...` (choose it from the resolved allowed values) and re-run. |
| Lookup `skipped` for another reason | Folder pickers and non-GET/POST lookups are not resolved; ask the user for the value. |

Without any connection, Dataverse `item/<column>` keys can still be taken from the table: `environment_entity_describe` (deployed) or the workspace's table definition. Lookups bind as `item/<lookup>_<table>@odata.bind`.

## Rules the snippet already follows

Keep them when editing by hand:

- `type` is the operation's action type; webhook operations (approvals that wait, the Dataverse row trigger) are `OpenApiConnectionWebhook`.
- `inputs.parameters` keys are flat, slash-joined names (`emailMessage/To`); never nest them.
- Every connector step has `"authentication": "@parameters('$authentication')"` and `host.connectionName` equal to its key in `connectionReferences`.
- Dataverse actions take the entity set name (`contacts`); the row trigger takes the logical name (`account`).
- `runAfter` names existing actions; the first action uses `{}`. Expressions: `@{triggerOutputs()?['body/accountid']}`, `@{items('<Loop>')?['emailaddress1']}`, `@outputs('<Action>')?['body/value']`.

## What NOT to Do

- ❌ Don't hand-write connector JSON — paste the snippet
- ❌ Don't run `--use-connection` before the user agreed to the plan
- ❌ Don't use operations flagged `[deprecated]`
- ❌ Don't rename the flow JSON file — `JsonFileName` in the `.data.xml` must match it
- ❌ Don't use premium triggers (e.g. HTTP request) without checking licensing with the user

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
