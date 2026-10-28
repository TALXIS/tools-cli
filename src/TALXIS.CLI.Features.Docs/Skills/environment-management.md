# Environment Setup & Profiles

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). If the TALXIS MCP server is connected instead, every command is also a tool named by joining its words with `_` (`txc workspace component create` is the tool `workspace_component_create`). The tool list is generated from the CLI commands, so each tool takes the arguments and options of its command, and the CLI help is the reference on either route.

## Overview

`txc` uses a three-part configuration model to connect to Dataverse environments:

```
Auth (credential alias)  ┐
                         ├→ Profile (pairs one auth with one connection; what commands run as)
Connection (provider + environment URL) ┘
```

A connection holds the provider and the environment URL, not credentials. The profile is what joins an auth entry to a connection, so one auth can serve several connections (different environments, same credentials). Every environment command runs against the active profile; pass `--profile <name>` (or set `TXC_PROFILE`) to override it for one command.

## First-Time Setup

### Interactive quickstart (a person at a keyboard)
```
txc config profile create --url https://<org>.crm.dynamics.com
```
Signs in interactively (device code when no browser is available), then creates the credential, the connection and the profile in one step and makes it active (`--no-select` to keep the current one). The sign-in needs a person: pass on the link and code it prints and wait for them.

### Explicit setup (service principal; scriptable, for CI/CD and unattended use)

#### 1. Add Authentication
```
txc config auth add-service-principal --alias <alias> --tenant <tenant-id> --application-id <app-id> --secret-from-env <ENV_VAR>
```
Registers a client-secret service principal under the alias. The secret is never a command-line argument: it is read from the environment variable named by `--secret-from-env`, otherwise from piped stdin, otherwise from a masked prompt. Set the variable from your shell or CI secret store, never from a file in the repository. For a federated credential (no secret) use `txc config auth add-federated --alias <alias> --tenant <tenant-id> --application-id <app-id>`.

#### 2. Create a Connection
```
txc config connection create <connection> --provider Dataverse --environment https://<org>.crm.dynamics.com
```
Records the provider and environment URL (`--url` is an alias of `--environment`). It does not reference an auth entry.

#### 3. Create a Profile
```
txc config profile create --name <profile> --auth <alias> --connection <connection>
```
Creates a named profile from an existing auth alias and connection. In this explicit mode only the first profile you create becomes the active one automatically; make any other active with `txc config profile select <profile>`.

#### 4. Validate the Profile
```
txc config profile validate <profile>
```
Tests the full chain: profile → connection → auth → environment. Confirms you can reach the Dataverse environment and authenticate successfully. `--skip-live` runs only the structural checks.

## Profile Management

| Command | Purpose |
|---|---|
| `txc config profile list` | Show all configured profiles |
| `txc config profile get [<name>]` | Show a profile's details (the active one when no name is given) |
| `txc config profile validate [<name>]` | Test that the profile can connect |
| `txc config profile select <name>` | Make a profile the active one for all commands |
| `txc config auth list` / `txc config connection list` | Show the registered credentials / connections |

## Workspace Profile Pinning

`txc config profile pin [<name>]` writes `.txc/workspace.json` in the current directory (the active profile when no name is given), so `txc` automatically uses that profile in this repository. This avoids accidentally running commands against the wrong environment. Which profile a command uses, in order: `--profile`, `TXC_PROFILE`, the workspace pin, the global active profile.

## Environment Types

| Type | Purpose | Protection Level |
|---|---|---|
| Development | Active development and testing | Low — unmanaged solutions |
| Test/UAT | Integration testing and user acceptance | Medium — managed solutions |
| Production | Live users and data | High — managed only, restricted access |

## Best Practices

- Use **service principal auth** for both dev and CI/CD (consistent, scriptable)
- Create **separate profiles** for each environment (dev, test, prod)
- **Pin profiles** to workspace directories to prevent cross-environment mistakes
- **Validate profiles** after creation and after credential rotation
- Never connect to production for routine development — use dev environments
- If a destructive command is blocked as production, check the target first. Run `txc config profile validate <profile> --refresh-env-type` to record its real type, and add `--allow-production` to the command only for an environment that really is production

## Common Scenarios

### Setting Up a New Environment Connection
```
1. txc config auth add-service-principal --alias dev-sp --tenant <tenant-id> --application-id <app-id> --secret-from-env TXC_SP_SECRET
2. txc config connection create dev-connection --provider Dataverse --environment https://<org>.crm.dynamics.com
3. txc config profile create --name dev --auth dev-sp --connection dev-connection
4. txc config profile validate dev
```
(`dev-sp`, `dev-connection` and `dev` are example names; the names are free.)

### Switching Between Environments
```
txc config profile list                  — see available profiles
txc config profile get test              — verify the target before switching
txc config profile select test           — make it the active profile
```
For a single command, `--profile test` avoids changing the active profile.

## Troubleshooting Auth

If `txc config profile validate <profile>` fails:
1. Check the environment URL is correct (`txc config profile get <profile>` shows its connection, then `txc config connection get <connection>`)
2. Verify the service principal exists in the target environment's Azure AD
3. Confirm the service principal has a Dataverse security role assigned (see
   [security-roles](security-roles.md) for creating the service principal and assigning roles)
4. Check if the client secret has expired (create a new secret, then re-run `txc config auth add-service-principal` for the same alias)

## What NOT to Do

- ❌ Don't connect to production for routine development — use dev environments
- ❌ Don't share credentials between team members — use per-developer service principals
- ❌ Don't skip profile validation after credential rotation — stale credentials cause confusing errors

See also: [troubleshooting](troubleshooting.md), [deployment-workflow](deployment-workflow.md)
