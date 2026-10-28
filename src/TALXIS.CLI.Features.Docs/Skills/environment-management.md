# Environment Setup & Profiles

## Overview

`txc` connects to Dataverse through three parts:

```
Auth (credential alias)  ┐
                         ├→ Profile (pairs one auth with one connection; what commands run as)
Connection (provider + environment URL) ┘
```

One auth can serve several connections. Commands run as the active profile.

## First-Time Setup

### Interactive (a person at a keyboard)
```
txc config profile create --url https://<org>.crm.dynamics.com
```
Signs in interactively (device code when no browser is available), creates the credential, connection and profile, and makes it active (`--no-select` to keep the current one). The sign-in needs a person: pass on the link and code it prints and wait.

### Explicit (service principal; CI/CD and unattended use)
```
txc config auth add-service-principal --alias <alias> --tenant <tenant-id> --application-id <app-id> --secret-from-env <ENV_VAR>
txc config connection create <connection> --provider Dataverse --environment https://<org>.crm.dynamics.com
txc config profile create --name <profile> --auth <alias> --connection <connection>
txc config profile validate <profile>
```
- The secret is never a command-line argument: it comes from the variable named by `--secret-from-env`, else piped stdin, else a masked prompt. Never read it from a file in the repository.
- Federated credential (no secret): `txc config auth add-federated --alias <alias> --tenant <tenant-id> --application-id <app-id>`.
- Only the first profile created in this mode becomes active; use `txc config profile select <profile>` for others.
- `validate` tests profile → connection → auth → environment; `--skip-live` runs only structural checks.

## Profile Management

| Command | Purpose |
|---|---|
| `txc config profile list` | Show all profiles |
| `txc config profile get [<name>]` | Show the profile (the global active one by default, not the workspace pin) with its connection URL and credential |
| `txc config profile validate [<name>]` | Test that the profile can connect |
| `txc config profile select <name>` | Make a profile the global active one |
| `txc config auth list` / `txc config connection list` | Show registered credentials / connections |

`txc config profile pin [<name>]` makes a profile the default for this repository (writes `.txc/workspace.json` in the current directory, applying to that directory tree; the active profile when no name is given). `select` sets the global active profile, used where no pin applies. Order of use: `--profile`, `TXC_PROFILE`, the workspace pin, the global active profile.

## Best Practices

- Separate profiles per environment (dev, test, prod); validate after creation and credential rotation
- Don't connect to production for routine development
- Each person signs in with their own account or service principal; don't share credentials
- If a destructive command is blocked as production, check the target first: `txc config profile validate <profile> --refresh-env-type` records its real type. Add `--allow-production` only for an environment that really is production

## Switching Environments
```
txc config profile list
txc config profile get test       — verify the target
txc config profile select test
```
For one command, `--profile test` avoids changing the active profile.

## Troubleshooting Auth

If `txc config profile validate <profile>` fails:
1. Check the URL: `txc config profile get <profile>`, then `txc config connection get <connection>`
2. Verify the service principal exists in the target environment's Azure AD
3. Confirm it has a Dataverse security role (see [security-roles](security-roles.md)) and environment-level access (Power Platform admin center)
4. Check whether the client secret expired (create a new one, re-run `txc config auth add-service-principal` for the same alias)

See also: [troubleshooting](troubleshooting.md), [deployment-workflow](deployment-workflow.md)
