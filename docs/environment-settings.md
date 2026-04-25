# Environment settings

`txc env setting` provides a single interface for reading and updating environment-level settings across Power Platform. Under the hood, settings are stored in several different places — the CLI abstracts that away so you don't have to know which API backs each setting.

```sh
txc env setting list [--filter <substring>] [--format json|text]
txc env setting update --name <setting> --value <value>
```

All commands respect `--profile` for targeting a specific environment. See [profiles-and-authentication.md](profiles-and-authentication.md).

## Common recipes

### Disable AI and Copilot features

Copilot and AI features are enabled by default in new environments. You can turn them off individually or in bulk. This is common for production environments where you want to reduce noise and unexpected AI-generated content.

**Disable Copilot control in model-driven apps:**
```sh
txc env setting update --name appcopilotenabled --value 0
```

**Disable AI form fill assistance (automatic predictions on edit forms):**
```sh
txc env setting update --name FormPredictEnabled --value false
```

**Disable AI form insights:**
```sh
txc env setting update --name EnableFormInsights --value false
```

**Disable AI Builder preview scenarios:**
```sh
txc env setting update --name paipreviewscenarioenabled --value false
```

**Disable AI Prompts:**
```sh
txc env setting update --name aipromptsenabled --value false
```

**Disable the maker Copilot bot:**
```sh
txc env setting update --name powerappsmakerbotenabled --value false
```

You can verify the changes by listing the relevant settings:
```sh
txc env setting list --filter copilot
txc env setting list --filter ai
```

### Enable code-first apps

Power Apps code-first (custom pages, code components beyond PCF) requires an explicit opt-in at the environment level via the control plane:

```sh
txc env setting update --name PowerApps_AllowCodeApps --value true
```

Verify:
```sh
txc env setting list --filter AllowCodeApps
```

### Auditing

Enable or disable Dataverse auditing:

```sh
txc env setting update --name isauditenabled --value true
```

### File upload limits

Increase the maximum file upload size (in KB):

```sh
txc env setting update --name maxuploadfilesize --value 131072
```

### Blocked file extensions

Update the list of blocked attachment file extensions:

```sh
txc env setting update --name blockedattachments --value "exe;bat;com;cmd"
```

## Discovering settings

List all settings and filter by keyword:

```sh
# Show everything related to email
txc env setting list --filter email

# Show everything related to auditing
txc env setting list --filter audit

# Show all Power Pages settings
txc env setting list --filter powerPages

# Export all settings as JSON for comparison or backup
txc env setting list --format json > settings-backup.json
```

The `--filter` option does a case-insensitive substring match on the setting name.

## How it works

Power Platform stores environment settings across multiple APIs due to historical reasons. `txc` queries all of them in parallel and merges the results:

| Source | Examples |
|--------|----------|
| Control plane (`api.powerplatform.com`) | `PowerApps_AllowCodeApps`, `copilotStudio_ConnectedAgents`, SAS IP restrictions |
| Organization table (Dataverse) | `isauditenabled`, `maxuploadfilesize`, `blockedattachments`, `aipromptsenabled` |
| Solution settings (Dataverse) | `EnableFormInsights`, `appcopilotenabled`, `FormPredictEnabled` |
| Copilot governance | `PowerPlatform_Anthropic` and other AI model access toggles |

When updating, the CLI automatically routes to the correct backend based on the setting name. You don't need to know where a setting is stored.

> [!NOTE]
> Some settings from the solution settings source are read-only through `txc` in this release. If an update fails, check whether the setting requires a solution component change (e.g. via `make.powerapps.com` > Solutions > Setting definitions).
