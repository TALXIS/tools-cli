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

It signs you in through the browser. Where no browser can complete the redirect (Codespaces, SSH, containers), `txc` shows a device code to enter at the Microsoft sign-in page instead. `txc config auth login` chooses the same way, and its `--device-code` flag forces it. Neither works in non-interactive mode; use the [headless workflow](#headless-and-ci-workflow) there.

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

## Environment type and the production guard

Each connection records the environment type from the Power Platform catalog (`Production`, `Sandbox`, `Developer`, ...). Destructive commands refuse to run against `Production` or `Default` environments, against environments whose type is unknown, and against any environment whose name or URL contains `prod`, `production`, `prd` or `live`, unless you pass `--allow-production`.

If a connection has no recorded type, `txc` looks it up from the catalog before deciding and saves the result. When that lookup fails, the command is blocked. To record the type yourself:

```sh
txc config profile validate --refresh-env-type
```

Pass `--allow-production` only for environments that really are production.

Connections created with `txc config connection create` (for example in CI) have no recorded type. The lookup needs an identity that can read the environment catalog; a service principal without that access is blocked and needs `--allow-production`.

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

For workload identity federation, register the app without a secret:

```sh
txc config auth add-federated \
  --tenant "$AZURE_TENANT_ID" \
  --client-id "$AZURE_CLIENT_ID" \
  --alias ci-wif
```

At token acquisition time, the Dataverse provider auto-detects the expected GitHub Actions and Azure DevOps token request environment variables (or `AZURE_FEDERATED_TOKEN_FILE`).

## Managed identity

On an Azure host with managed identity enabled, create a profile without browser login, device codes, or stored secrets:

```sh
txc config profile create --url "$DATAVERSE_URL" --identity
txc config profile validate
```

`--managedIdentity` and `--managed-identity` are equivalent to `--identity`. All three work in headless mode. The normal profile naming and selection rules apply, including `--no-select`.

For credential-only setup, register a reusable credential first:

```sh
txc config auth login --identity --alias azure-host
txc config connection create azure-target \
  --provider dataverse \
  --environment "$DATAVERSE_URL"
txc config profile create --name azure --auth azure-host --connection azure-target
```

Credential registration does not request a token or verify access. Tokens are acquired when a resource is accessed; use `txc config profile validate` to verify the selected profile. Without `--alias`, the credential alias defaults to `managed-identity` (collision-resolved), and repeated setup reuses a matching credential.

`txc config auth get <alias>` requests a token from the endpoint to show the identity's `tenantId`, `applicationId` (client ID), and `objectId`; these values are not saved. With `--check-token`, a credential not used by any profile is checked against the Power Platform admin API (`https://service.powerapps.com/`).

MSAL discovers the supported host endpoint automatically, including `MSI_ENDPOINT` and the standard App Service/IMDS endpoints. `txc` sends no identity selector: the endpoint chooses its default identity. There is no client-ID option for this flow, and `DEFAULT_IDENTITY_CLIENT_ID` is not read. Endpoints that require an explicit user-assigned identity selector are not supported by this mode.

`--identity` cannot be combined with `--device-code` or with explicit profile binding (`--auth` / `--connection`). Endpoint or token failures never fall back to interactive authentication. Tokens are cached only in memory; no managed-identity token or endpoint secret is persisted.

The identity still needs permission to access Dataverse and any other APIs a command uses. Environment-catalog discovery during profile creation remains best-effort; without catalog permissions, production guards may require `--allow-production` as described above.

## Override the target for one command

Use `--profile` when you want to hit a different environment without switching the active default:

```sh
txc env sln import ./Solutions/MySolution_managed.zip --profile customer-b-prod
```
