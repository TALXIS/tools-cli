# Profiles and authentication

`txc` separates **who you are** from **where commands run**:

- **Credential**: how you authenticate.
- **Connection**: the target environment or provider endpoint.
- **Profile**: a named combination of credential and connection.

Every command that talks to a live environment takes one context flag: `--profile <name>`.
Instead of passing raw connection details on every command, you create profiles once and reuse them.

## Active profile resolution

`txc` resolves the active profile in this order:

```text
--profile flag > TXC_PROFILE env > <repo>/.txc/workspace.json > global active pointer (~/.txc/config.json)
```

## Secret storage

Credentials are not stored as plaintext in config files.

- Service principal secrets, PATs, and certificate passwords are stored in the OS credential vault.
- Config files keep opaque `vault://` references.
- MSAL tokens live in a separate cache protected by the same vault mechanism.

## Interactive workflow

The fastest setup on a developer machine is usually a single command:

```sh
txc config profile create --url https://contoso.crm4.dynamics.com/
```

That command creates the profile and selects it as the active profile.

If you want to bootstrap the profile without switching the active context:

```sh
txc config profile create --url https://contoso.crm4.dynamics.com/ --no-select
```

If you want the explicit building blocks, use the primitive commands instead:

```sh
# 1. Sign in interactively and create a credential entry.
txc config auth login

# 2. Register the environment you want to target.
txc config connection create customer-a-dev \
  --provider dataverse \
  --environment https://contoso.crm4.dynamics.com/

# 3. Combine credential + connection into a profile and select it.
txc config profile create --name customer-a-dev \
  --auth <upn-alias> \
  --connection customer-a-dev
txc config profile select customer-a-dev

# 4. Optionally pin the profile to the current repository.
txc config profile pin

# 5. Validate the setup end to end.
txc config profile validate
```

Unpin a repository-level profile with:

```sh
txc config profile unpin
```

## Headless and CI workflow

For CI runners and other non-interactive environments, isolate config and load the secret from an environment variable:

```sh
export TXC_CONFIG_DIR="$RUNNER_TEMP/txc-config"
export TXC_NON_INTERACTIVE=1
export SPN_SECRET='<client-secret>'

txc config auth add-service-principal \
  --tenant "$AZURE_TENANT_ID" \
  --client-id "$AZURE_CLIENT_ID" \
  --alias ci-spn \
  --secret-from-env SPN_SECRET

txc config connection create ci-target \
  --provider dataverse \
  --environment "$DATAVERSE_URL"

txc config profile create --name ci --auth ci-spn --connection ci-target
txc config profile select ci
```

After that, all subsequent `txc` commands use `TXC_CONFIG_DIR` plus the selected profile.

For workload identity federation, the Dataverse provider auto-detects the expected GitHub Actions and Azure DevOps token request environment variables at acquisition time.

## Override the target for one command

Use `--profile` when you want to hit a different environment without switching the active default:

```sh
txc env sln import ./Solutions/MySolution_managed.zip --profile customer-b-prod
```
