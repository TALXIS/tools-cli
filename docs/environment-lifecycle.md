# Environment Lifecycle

`txc env list` and `txc env create` manage Power Platform environments at the **tenant level** — they use the active profile's credential and cloud for admin authority, not a target environment URL.

## Listing environments

```sh
txc env list [--filter <substring>] [--type <type>] [--format json|text]
```

Returns every Dataverse-backed environment visible to the caller. Results include environment id, display name, URL, unique name, and lifecycle type.

| Option | Description |
|--------|-------------|
| `--filter` | Case-insensitive substring match against display name, unique name, or URL. |
| `--type`, `-t` | Filter to a single lifecycle type: `Production`, `Sandbox`, `Trial`, `Developer`, `Default`, `Teams`, `SubscriptionBasedTrial`. |
| `--profile`, `-p` | Profile supplying the admin identity and cloud. Falls back to the active profile. |
| `--format`, `-f` | `json` or `text` (auto-detected when omitted). |

### Examples

```sh
# List all environments
txc env list

# Only sandboxes, as JSON
txc env list --type Sandbox -f json

# Search by name
txc env list --filter "contoso"
```

## Creating environments

```sh
txc env create --type <type> [options]
```

Provisions a new environment via the Power Platform BAP admin API. By default the command returns immediately after the create request is accepted (fire-and-forget); pass `--wait` to block until provisioning completes.

| Option | Alias | Required | Default | Description |
|--------|-------|----------|---------|-------------|
| `--type` | `-t` | **Yes** | — | `Production`, `Sandbox`, `Trial`, `Developer`, `Teams`, or `SubscriptionBasedTrial`. |
| `--name` | `-n` | Yes* | — | Display name. Required for all types except `Teams`. |
| `--region` | `-r` | No | `unitedstates` | Azure geo region slug (e.g. `europe`, `asia`, `unitedstates`). |
| `--currency` | `-c` | No | `USD` | ISO currency code, validated against the region's catalog. |
| `--language` | `-l` | No | `1033` | LCID integer or localized name (e.g. `English (United States)`). |
| `--domain` | `-d` | No | auto | Subdomain for the environment URL (2-32 chars). |
| `--templates` | — | No | — | Comma-separated Dynamics 365 app template names. |
| `--security-group-id` | `-sg` | Teams: yes | — | Entra security group id. Required for `Teams` environments. |
| `--user` | `-u` | No | — | Owning user's Entra object id. Only valid for `Developer` environments. |
| `--wait` | — | No | `false` | Block until provisioning completes. |
| `--max-wait-minutes` | — | No | `60` | Timeout in minutes when `--wait` is set. |
| `--profile` | `-p` | No | active | Profile supplying the admin identity and cloud. |

> \* `--name` is ignored for `Teams` environments (the name derives from the linked group).

### Examples

```sh
# Quick sandbox — returns immediately
txc env create --type Sandbox --name "Feature Branch 42" --region europe

# Developer environment owned by a specific user, wait for completion
txc env create --type Developer --name "Jan's Dev Box" --user 00000000-0000-0000-0000-000000000001 --wait

# Trial with a Dynamics 365 Sales template
txc env create --type Trial --name "Sales Demo" --templates D365_Sales
```

### Type-specific rules

| Type | Notes |
|------|-------|
| `Default` | **Not creatable** — this is the tenant's auto-provisioned environment. |
| `Teams` | Requires `--security-group-id`. Name is derived from the group; `--name` is ignored. |
| `Developer` | Only type that accepts `--user`. When omitted, owned by the caller. |
| `SubscriptionBasedTrial` | Behaves like `Trial` but tied to a subscription. |

### Known limitations

- **`--user` accepts only Entra object ids (GUIDs).** UPN-to-objectId resolution (which PAC CLI supports via Microsoft Graph) is not implemented. Use `az ad user show --id user@contoso.com --query id -o tsv` to look up the id.
- **No `--description` option.** The BAP create API does not accept a description field, so a CLI flag would be a silent no-op.
- **Currency, language, and template validation is region-specific.** The CLI fetches the per-region catalog and fails fast with the valid values when a mismatch is detected.

## Authentication

Both commands use the active profile (or `--profile`) to resolve a credential and cloud instance. The credential acquires a BAP admin token scoped to `https://service.powerapps.com/`. No target environment URL is needed — these are tenant-level operations.

See [profiles-and-authentication.md](profiles-and-authentication.md) for how profiles work.

## MCP integration

Both commands are automatically exposed as MCP tools:

| CLI command | MCP tool name | Access hint |
|-------------|--------------|-------------|
| `txc env list` | `environment_list` | `ReadOnlyHint` |
| `txc env create` | `environment_create` | `IdempotentHint` |

No special MCP configuration is needed — tool registration is reflection-driven from the CLI command tree.
