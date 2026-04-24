# Architecture

The `txc` CLI is organized around four architectural planes. Each plane maps to a distinct set of projects and responsibilities.

## Planes

| Plane | Responsibility | Projects |
|-------|---------------|----------|
| **Management** | CLI command groups, orchestration, user-facing workflows | `TALXIS.CLI.Features.*` |
| **Control** | Power Platform admin APIs, environment provisioning, governance | `TALXIS.CLI.Platform.PowerPlatform.Control` |
| **Application** | Solution/package/deployment operations inside a Dataverse environment | `TALXIS.CLI.Platform.Dataverse.Application` |
| **Data** | Dataverse instance data access and runtime connectivity | `TALXIS.CLI.Platform.Dataverse` |

## Project Map

```
src/
  TALXIS.CLI                              # CLI host (entry point, composition root reference)
  TALXIS.CLI.Core                         # Provider-agnostic abstractions, model, storage, bootstrapping
  TALXIS.CLI.Logging                      # Structured logging infrastructure

  TALXIS.CLI.Features.Config              # txc config: profiles, auth, connections, settings
  TALXIS.CLI.Features.Environment         # txc environment: solution/package/deployment commands
  TALXIS.CLI.Features.Data                # txc data: model conversion, data packages, transforms
  TALXIS.CLI.Features.Docs                # txc docs (placeholder)
  TALXIS.CLI.Features.Workspace           # txc workspace: scaffolding, templates, validation

  TALXIS.CLI.Platform.PowerPlatform.Control   # Control plane: Power Platform admin API client
  TALXIS.CLI.Platform.Dataverse.Application   # Application plane: solution/package/deployment services
  TALXIS.CLI.Platform.Dataverse               # Data plane: runtime, auth, MSAL, connection factory
  TALXIS.CLI.Platform.Xrm                     # Legacy Xrm Tooling runners (Package Deployer, CMT)
  TALXIS.CLI.Platform.XrmShim                 # Compatibility shims for legacy Xrm assemblies

  TALXIS.CLI.MCP                          # MCP server (txc-mcp)
```

## Dependency Rules

- **Features** projects depend on **Core** and may reference platform projects for DI wiring.
- **Platform** projects depend on **Core** and optionally on each other when one plane consumes another (e.g. Application → Dataverse runtime).
- **Core** has no platform or feature dependencies. Provider-agnostic abstractions (`IAccessTokenService`, `IConnectionProvider`, `IConnectionProviderBootstrapper`) live here.
- The **control plane** (`PowerPlatform.Control`) depends only on **Core**, not on Dataverse — it uses `IAccessTokenService` for token acquisition.

## Where Does New Code Go?

| If the code... | Put it in... |
|----------------|-------------|
| Is a CLI command or user workflow orchestration | `TALXIS.CLI.Features.*` |
| Talks to the Power Platform admin API | `TALXIS.CLI.Platform.PowerPlatform.Control` |
| Manages solutions, packages, or deployments inside an environment | `TALXIS.CLI.Platform.Dataverse.Application` |
| Handles Dataverse auth, tokens, MSAL, connection creation | `TALXIS.CLI.Platform.Dataverse` |
| Is a provider-agnostic abstraction or model | `TALXIS.CLI.Core` |
| Adds a new provider (Jira, Azure DevOps, etc.) | New `TALXIS.CLI.Platform.<Provider>` project, implementing Core abstractions |

## Adding a New Provider

1. Create `TALXIS.CLI.Platform.<Provider>` referencing `TALXIS.CLI.Core`.
2. Implement `IConnectionProvider` and `IConnectionProviderBootstrapper`.
3. Add a `HostSuffixRule` to `ProviderUrlResolver.DefaultRules` in Core.
4. Register DI services from a new `Add<Provider>Provider()` extension method.
5. Call the new extension from the composition root in `TxcServicesBootstrap`.
