# Troubleshooting — Tool Selection Logic

<!-- Internal reasoning skill: contains ONLY diagnostic routing and escalation paths. -->
<!-- For detailed troubleshooting steps, see the public troubleshooting skill. -->

## First-Response Routing

Match the user's symptom to the correct FIRST tool to run:

```
"import failed" / "deployment error"   → environment_deployment_get --latest (or --async-operation-id <id>)
"component won't update" / "conflict"  → environment_component_layer_list
"can't delete"                         → environment_component_dependency_delete-check
"missing dependency"                   → environment_component_dependency_required
"auth error" / "401" / "403"           → config_profile_validate
"wrong data" / "stale"                 → config_profile_get (verify target env)
"changes not visible"                  → environment_solution_publish (was publish skipped?)
```
→ ALWAYS run the first-response tool BEFORE asking the user for more details
→ The tool output will clarify the actual problem

## Escalation Paths

### Deployment failure escalation
```
environment_deployment_get → findings?
  ├─ Component error → environment_component_layer_list → environment_component_layer_get
  ├─ Missing dependency → environment_component_dependency_required → import missing solution
  ├─ Version conflict → increment version locally → rebuild → retry
  └─ Generic/timeout → retry once with --wait → if still fails, check env health
```

### Auth failure escalation
```
config_profile_validate → result?
  ├─ Invalid → config_profile_get → config_connection_get → fix credentials
  └─ Valid but failing → check security roles in Power Platform admin center (outside txc)
```

## Diagnostic Priority Order
When unsure where to start:
1. `config_profile_validate` — eliminate auth issues first (cheapest check)
2. `config_profile_get` — confirm correct environment
3. `environment_deployment_get --latest` — check last deployment (or `--async-operation-id <id>` if a specific import is in flight)
4. `environment_component_layer_list` — inspect component ownership
5. `environment_component_dependency_required` or `_delete_check` — dependency issues

→ STOP as soon as you find the root cause — don't run all tools prophylactically

## Anti-Patterns
- ❌ Asking the user "what error did you get?" before running diagnostic tools → run the tool first
- ❌ Retrying imports without checking deployment findings → repeats the same error
- ❌ Querying the `asyncoperation` table via `environment_data_query_sql` to check import status → call `environment_deployment_get --async-operation-id <id>` instead — raw SQL gives unstructured statuscodes, the proper tool returns parsed findings and human-readable errors
- ❌ Jumping to layer inspection before checking basic auth/connectivity
- ❌ Using environment schema tools to "fix" what should be fixed locally and redeployed
