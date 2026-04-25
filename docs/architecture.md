# Architecture

The `txc` CLI is organized around five architectural planes. Each plane maps to a distinct set of projects and responsibilities.

## Planes

| Plane | Responsibility | Projects |
|-------|---------------|----------|
| **Management** | CLI command groups, orchestration, user-facing workflows | `TALXIS.CLI.Features.*` |
| **Control** | Power Platform admin APIs, environment provisioning, governance | `TALXIS.CLI.Platform.PowerPlatform.Control` |
| **Application** | Solution/package/deployment operations inside a Dataverse environment | `TALXIS.CLI.Platform.Dataverse.Application` |
| **Runtime** | Dataverse-specific auth, tokens, connection creation, live checking | `TALXIS.CLI.Platform.Dataverse.Runtime` |
| **Data** | Dataverse instance data access (CRUD, queries, imports), changeset apply pipeline | `TALXIS.CLI.Platform.Dataverse.Data` |

## Project Map

```
src/
  TALXIS.CLI                                    # CLI host (entry point)
  TALXIS.CLI.Core                               # Provider-agnostic: model, storage, bootstrapping
    Identity/                                    # Shared MSAL/Entra: MsalClientFactory, MsalTokenCacheBinder,
                                                 #   EntraCloudMap, FederatedAssertionCallbacks
    Contracts/Dataverse/                         # Service contracts (ISolutionImportService, etc.)
    Contracts/Packaging/                         # NuGet packaging contracts
  TALXIS.CLI.Logging                            # Structured logging infrastructure

  TALXIS.CLI.Features.Config                    # txc config: profiles, auth, connections, settings
  TALXIS.CLI.Features.Environment               # txc environment: solution/package/deployment commands
  TALXIS.CLI.Features.Data                      # txc data: model conversion, data packages, transforms
  TALXIS.CLI.Features.Docs                      # txc docs (placeholder)
  TALXIS.CLI.Features.Workspace                 # txc workspace: scaffolding, templates, validation

  TALXIS.CLI.Platform.PowerPlatform.Control     # Control plane: Power Platform admin API client
  TALXIS.CLI.Platform.Dataverse.Runtime         # Dataverse runtime: auth, MSAL, connection factory
  TALXIS.CLI.Platform.Dataverse.Application     # Application plane: services + SDK orchestration
    Sdk/                                         # Low-level SDK helpers (SolutionImporter, etc.)
  TALXIS.CLI.Platform.Dataverse.Data            # Data plane: ChangesetApplier, data strategy execution
  TALXIS.CLI.Analyzers                          # Custom Roslyn analyzers (TXC001–TXC009)
  TALXIS.CLI.Platform.Xrm                       # Legacy Xrm Tooling runners (Package Deployer, CMT)
  TALXIS.CLI.Platform.XrmShim                   # Compatibility shims for legacy Xrm assemblies

  TALXIS.CLI.MCP                                # MCP server (txc-mcp)
```

## Dependency Rules

- **Features** projects depend on **Core** and may reference platform projects for DI wiring.
- **Platform** projects depend on **Core** and optionally on each other when one plane consumes another (e.g. Application → Dataverse.Runtime).
- **Core** has no platform or feature dependencies. Shared MSAL/Entra identity infrastructure, provider-agnostic abstractions (`IAccessTokenService`, `IConnectionProvider`, `IConnectionProviderBootstrapper`), and service contracts all live here.
- The **control plane** (`PowerPlatform.Control`) depends only on **Core**, not on Dataverse — it uses `IAccessTokenService` for token acquisition.

## Where Does New Code Go?

| If the code... | Put it in... |
|----------------|-------------|
| Is a CLI command or user workflow orchestration | `TALXIS.CLI.Features.*` |
| Talks to the Power Platform admin API | `TALXIS.CLI.Platform.PowerPlatform.Control` |
| Manages solutions, packages, or deployments inside an environment | `TALXIS.CLI.Platform.Dataverse.Application` |
| Handles Dataverse-specific auth, tokens, or connections | `TALXIS.CLI.Platform.Dataverse.Runtime` |
| Reads or writes Dataverse table data (CRUD, queries) | `TALXIS.CLI.Platform.Dataverse.Data` |
| Is shared MSAL/Entra infrastructure (token cache, assertions, authority) | `TALXIS.CLI.Core/Identity/` |
| Is a provider-agnostic abstraction, model, or service contract | `TALXIS.CLI.Core` |
| Adds a new provider (Jira, Azure DevOps, etc.) | New `TALXIS.CLI.Platform.<Provider>` project, implementing Core abstractions |
| Is a custom Roslyn analyzer rule (TXC0xx) | `TALXIS.CLI.Analyzers` |

## Adding a New Provider

1. Create `TALXIS.CLI.Platform.<Provider>` referencing `TALXIS.CLI.Core`.
2. Implement `IConnectionProvider` and `IConnectionProviderBootstrapper`.
3. Add a `HostSuffixRule` to `ProviderUrlResolver.DefaultRules` in Core.
4. Reuse `Core/Identity/` for MSAL client construction (shared `MsalClientFactory`, `EntraCloudMap`, `MsalTokenCacheBinder`).
5. Register DI services from a new `Add<Provider>Provider()` extension method.
6. Call the new extension from the composition root in `TxcServicesBootstrap`.
