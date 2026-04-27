# Environment Setup & Profiles

## Overview

`txc` uses a three-tier configuration model to connect to Dataverse environments:

```
Auth (credentials) → Connection (auth + environment URL) → Profile (active config)
```

## First-Time Setup

### 1. Add Authentication
```
Tool: config_auth_add-service-principal
```
Registers a service principal (client ID + secret or certificate) for authenticating with Dataverse. This is the recommended auth method for development and CI/CD.

### 2. Create a Connection
```
Tool: config_connection_create
```
Combines an auth entry with a specific environment URL. One auth can be used across multiple connections (different environments, same credentials).

### 3. Create a Profile
```
Tool: config_profile_create
```
Creates a named profile that references a connection. Profiles are what you switch between when working with different environments.

### 4. Validate the Profile
```
Tool: config_profile_validate
```
Tests the full chain: profile → connection → auth → environment. Confirms you can reach the Dataverse environment and authenticate successfully.

## Profile Management

| Tool | Purpose |
|---|---|
| `config_profile_show` | Display current profile details (URL, auth method) |
| `config_profile_validate` | Test that the profile can connect |
| `config_profile_list` | Show all configured profiles |

## Workspace Profile Pinning

Profiles can be pinned to a workspace directory, so `txc` automatically uses the correct profile when you're in that repository. This avoids accidentally running commands against the wrong environment.

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

## Troubleshooting Auth

If `config_profile_validate` fails:
1. Check the environment URL is correct (`config_profile_show`)
2. Verify the service principal exists in the target environment's Azure AD
3. Confirm the service principal has a Dataverse security role assigned
4. Check if credentials (secret/certificate) have expired

See also: [troubleshooting](troubleshooting.md), [deployment-workflow](deployment-workflow.md)
