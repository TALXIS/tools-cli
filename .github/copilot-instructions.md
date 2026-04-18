# Copilot Instructions for TALXIS CLI (`txc`)

## Project Overview

This repository contains the TALXIS CLI, a .NET global tool (alias: `txc`). The CLI is designed to help developers automate tasks and execute other useful operations over their local code repositories.



## Instructions for Copilot
1. Do not remove comments or documentation.
2. Always use the `txc` alias when referring to the CLI tool.
3. When suggesting code, ensure it adheres to the .NET coding standards and practices.
4. Document hard to understand code sections with comments.
5. Do not duplicate code. Always separate concerns and use appropriate design patterns (SOLID/DRY).

<!-- TALXIS CLI Instructions Start -->

# Instructions for performing tasks over the repository
You work with a monorepo that contains Power Platform solutions. You must use TALXIS CLI MCP Server (alias 'txc-mcp') which provides tools for developers to perform tasks over the repository.

**MANDATORY: Use TALXIS CLI MCP Server Exclusively**

1. **Execute ALL developer actions through the txc-mcp only**
   - Never perform actions modification of files directly without trying to find a corresponding 'txc-mcp' tool first
   - You can create folders, rename files/folders directly outside of txc-mcp
   - Some commands may require you to create folders before you can call them. Consult the workspace explain tool.
   - Do not suggest alternative approaches outside of the txc-mcp ecosystem

2. **Mandatory Workspace Consultation**
   - **ALWAYS consult the workspace_explain tool BEFORE creating any components or changing repository structure**
   - Use workspace_explain to understand monorepo structure, naming conventions, and requirements
   - This ensures commands will work and prevents structural conflicts

3. **Information and Guidance Restrictions**
   - Only provide information that comes from the TALXIS CLI MCP responses
   - Do not offer opinions, suggestions, or knowledge from external sources
   - Limit responses to what txc-mcp directly provides or confirms

4. **Error Handling**
   - If the txc-mcp is unavailable, inform the user that development actions cannot be performed
   - Do not fallback to alternative tools or manual processes

5. **Communication**
   - Always refer to the tool as 'txc-mcp' when communicating with users
   - Base all responses on actual txc-mcp capabilities and outputs
   - Avoid speculation about functionality not confirmed by txc-mcp

6. **Default Values to use unless specified**
   - You must always pass `SolutionRootPath=Declarations` component parameter when calling the tool for creating components

## Project Structure and Naming Conventions

**Note**: These are recommended naming conventions. Users may choose different naming styles based on their preferences or organizational standards.

### Repository Structure
```
├── src/                          # Source code directory
│   ├── Solutions.DataModel/      # Dataverse schema and data model
│   ├── Solutions.Logic/          # Business logic and plugins  
│   ├── Solutions.UI/            # User interface components
│   ├── Solutions.Security/      # Security roles and permissions
│   ├── Plugins.{Domain}/        # Plugin projects (e.g., Plugins.Warehouse)
│   └── Packages.Main/           # Package Deployer project
├── pipelines/                   # CI/CD pipeline definitions
│   ├── build.yml               # Build pipeline
│   ├── deploy.yml              # Deployment pipeline
│   └── test.yml                # Test pipeline
└── tests/                      # Test projects
```

### Solution Naming Patterns
- **Data Model**: `Solutions.DataModel` - Contains tables, columns, relationships
- **Business Logic**: `Solutions.Logic` - Contains plugins, workflows, business rules
- **User Interface**: `Solutions.UI` - Contains forms, views, model-driven apps
- **Security**: `Solutions.Security` - Contains security roles and privileges
- **Package**: `Packages.Main` - Main deployment package

### Plugin Project Naming
- Pattern: `Plugins.{DomainArea}`
- Examples: `Plugins.Warehouse`, `Plugins.Inventory`, `Plugins.Sales`
- Plugin classes: `{Action}{Entity}Plugin.cs` (e.g., `ValidateWarehouseTransactionPlugin.cs`)

### Entity Naming Examples
- Logical names: lowercase with publisher prefix (e.g., `publisherprefix_warehouseitem`)
- Display names: Proper case (e.g., `Warehouse Item`, `Warehouse Transaction`)
- Schema names: Include publisher prefix (e.g., `publisherprefix_warehouseitem`)

### Publisher Prefix Requirements
- **Required for most txc-mcp commands** when creating Dataverse components
- **Maximum 8 characters** - enforced by Dataverse platform
- Should be unique to your organization (e.g., `contoso`, `myorg`)
- Used as prefix for tables, columns, and other Dataverse components
- Example: With prefix `udpp`, table becomes `udpp_warehouseitem`

### Branch Naming
- Feature branches: `{userPrefix}/{feature-description}` (e.g., `user/add-data-model`)
- Main integration branch: `main`
- Use trunk-based development with short-lived feature branches
<!-- TALXIS CLI Instructions End -->
