# Schema Management

`txc` provides a comprehensive set of commands for managing Dataverse schema — entities (tables), attributes (columns), relationships, and option sets — directly from the command line. All schema commands live under `txc environment entity` (alias: `txc env entity`).

Every mutating schema command extends `StagedCliCommand`, so it accepts either `--apply` (execute immediately) or `--stage` (queue for batch apply). See [changeset-staging.md](changeset-staging.md) for details on the staging workflow.

---

## Table of Contents

- [Entity CRUD](#entity-crud)
- [Attribute CRUD](#attribute-crud)
  - [Attribute types](#attribute-types)
  - [Type-specific parameters](#type-specific-parameters)
- [Attribute Type Introspection](#attribute-type-introspection)
- [Relationship Commands](#relationship-commands)
- [OptionSet Commands](#optionset-commands)

---

## Entity CRUD

### `txc env entity create`

Creates a new Dataverse entity (table). Uses the `CreateEntities` batch API internally when staged — see [dataverse-metadata-performance.md](dataverse-metadata-performance.md).

| Option | Required | Default | Description |
|--------|----------|---------|-------------|
| `--name` | Yes | — | Logical name (including publisher prefix) |
| `--display-name` | Yes | — | Display name shown in the UI |
| `--plural-name` | Yes | — | Plural display name |
| `--description` | No | — | Entity description |
| `--solution` | No | — | Solution unique name to add the entity to |
| `--ownership` | No | `user` | `user` or `organization` |
| `--type` | No | `standard` | Entity type (`standard`, etc.) |
| `--has-notes` | No | `false` | Enable notes (annotations) |
| `--has-activities` | No | `false` | Enable activity associations |
| `--enable-audit` | No | `false` | Enable auditing |
| `--enable-change-tracking` | No | `false` | Enable change tracking |

```sh
txc env entity create --name tom_project \
  --display-name "Project" --plural-name "Projects" \
  --ownership user --has-notes --enable-audit --apply
```

### `txc env entity get`

Retrieves detailed metadata for a single entity.

| Argument/Option | Required | Description |
|-----------------|----------|-------------|
| `entity` (positional) | Yes | Entity logical name |

```sh
txc env entity get account
```

### `txc env entity update`

Updates an existing entity's display metadata. At least one of the optional flags must be provided.

| Option | Required | Description |
|--------|----------|-------------|
| `--entity` | Yes | Entity logical name |
| `--display-name` | No | New display name |
| `--plural-name` | No | New plural display name |
| `--description` | No | New description |

```sh
txc env entity update --entity tom_project --display-name "Initiative" --apply
```

### `txc env entity delete`

Deletes an entity from the environment. Destructive — requires `--yes` to skip confirmation.

| Option | Required | Description |
|--------|----------|-------------|
| `--entity` | Yes | Entity logical name |
| `--yes` | No | Skip confirmation prompt |

```sh
txc env entity delete --entity tom_project --yes --apply
```

### `txc env entity list`

Lists entities in the environment.

| Option | Required | Default | Description |
|--------|----------|---------|-------------|
| `--search` | No | — | Filter entities by name substring |
| `--include-system` | No | `false` | Include system entities |

```sh
txc env entity list --search project
```

### `txc env entity describe`

Shows a detailed description of an entity's schema — attributes, relationships, and metadata.

| Argument/Option | Required | Default | Description |
|-----------------|----------|---------|-------------|
| `entity` (positional) | Yes | — | Entity logical name |
| `--include-system` | No | `false` | Include system-managed attributes |

```sh
txc env entity describe account --include-system
```

---

## Attribute CRUD

### `txc env entity attribute create`

Creates a new attribute (column) on an entity. The `--type` flag determines which type-specific parameters are available.

**Common options (all types):**

| Option | Required | Default | Description |
|--------|----------|---------|-------------|
| `--entity` | Yes | — | Target entity logical name |
| `--name` | Yes | — | Attribute logical name |
| `--type` | Yes | — | Attribute type (see [types](#attribute-types)) |
| `--display-name` | No | — | Display name |
| `--description` | No | — | Description |
| `--required` | No | `none` | Requirement level: `none`, `recommended`, `required` |
| `--solution` | No | — | Solution unique name |
| `--is-auditable` | No | `false` | Enable auditing |
| `--is-searchable` | No | `true` | Include in quick-find/relevance search |
| `--is-secured` | No | `false` | Enable field-level security |

#### Attribute types

| Type value | Description |
|-----------|-------------|
| `string` | Single-line text |
| `memo` | Multi-line text |
| `number` | Whole number (integer) |
| `decimal` | Decimal number |
| `float` | Floating-point number |
| `money` | Currency |
| `bool` | Yes/No (two-option) |
| `datetime` | Date and time |
| `choice` | Single-select option set |
| `multichoice` | Multi-select option set |
| `lookup` | Single-entity lookup |
| `polymorphic-lookup` | Multi-table lookup |
| `customer` | Customer lookup (account + contact) |
| `image` | Image column |
| `file` | File column |
| `bigint` | Big integer |

#### Type-specific parameters

**String / Memo:**

| Option | Default | Description |
|--------|---------|-------------|
| `--max-length` | — | Maximum character length |
| `--string-format` | — | Format hint (e.g. `email`, `url`, `phone`) |

**Number (whole number):**

| Option | Default | Description |
|--------|---------|-------------|
| `--min-value` | — | Minimum value |
| `--max-value` | — | Maximum value |
| `--number-format` | — | Display format |

**Decimal / Float / Money:**

| Option | Default | Description |
|--------|---------|-------------|
| `--min-value` | — | Minimum value |
| `--max-value` | — | Maximum value |
| `--precision` | — | Number of decimal places |
| `--precision-source` | — | Precision source (money only) |

**Bool:**

| Option | Default | Description |
|--------|---------|-------------|
| `--true-label` | `Yes` | Label for the true value |
| `--false-label` | `No` | Label for the false value |

**DateTime:**

| Option | Default | Description |
|--------|---------|-------------|
| `--datetime-format` | — | `dateonly` or `dateandtime` |
| `--datetime-behavior` | — | `uselocal`, `utc`, or `timezoneneutral` |

**Choice / Multichoice:**

| Option | Default | Description |
|--------|---------|-------------|
| `--options` | — | Comma-separated list of option labels (creates a local option set) |
| `--global-optionset` | — | Name of an existing global option set to bind |

**Lookup:**

| Option | Default | Description |
|--------|---------|-------------|
| `--target-entity` | — | Target entity logical name |
| `--cascade-delete` | `removelink` | Cascade delete behaviour |

**Polymorphic Lookup:**

| Option | Default | Description |
|--------|---------|-------------|
| `--target-entities` | — | Comma-separated target entity logical names |

**Customer:** No additional options — automatically targets `account` and `contact`.

**Image:**

| Option | Default | Description |
|--------|---------|-------------|
| `--max-size-kb` | — | Maximum image size in KB |
| `--can-store-full-image` | `true` | Store full-resolution image |

**File:**

| Option | Default | Description |
|--------|---------|-------------|
| `--max-size-kb` | — | Maximum file size in KB |

**BigInt:** No additional type-specific options.

```sh
# String attribute
txc env entity attribute create --entity tom_project \
  --name tom_code --type string --display-name "Code" \
  --max-length 20 --required required --apply

# Money attribute
txc env entity attribute create --entity tom_project \
  --name tom_budget --type money --display-name "Budget" \
  --precision 2 --apply

# Lookup attribute
txc env entity attribute create --entity tom_project \
  --name tom_accountid --type lookup --display-name "Account" \
  --target-entity account --apply

# Choice attribute
txc env entity attribute create --entity tom_project \
  --name tom_priority --type choice --display-name "Priority" \
  --options "Low,Medium,High,Critical" --apply
```

### `txc env entity attribute get`

Retrieves detailed metadata for a single attribute.

| Option | Required | Description |
|--------|----------|-------------|
| `--entity` | Yes | Entity logical name |
| `--name` | Yes | Attribute logical name |

```sh
txc env entity attribute get --entity account --name name
```

### `txc env entity attribute update`

Updates an existing attribute's display metadata.

| Option | Required | Description |
|--------|----------|-------------|
| `--entity` | Yes | Entity logical name |
| `--name` | Yes | Attribute logical name |
| `--display-name` | No | New display name |
| `--description` | No | New description |
| `--required` | No | New requirement level |

> **Note:** Updating `RequiredLevel` uses a Web API `PUT` (full replacement) internally, because the SDK's `UpdateAttributeRequest` silently ignores `RequiredLevel` changes. See [dataverse-metadata-performance.md](dataverse-metadata-performance.md) for details.

```sh
txc env entity attribute update --entity account --name name \
  --required required --apply
```

### `txc env entity attribute delete`

Deletes an attribute. Destructive — requires `--yes`.

| Option | Required | Description |
|--------|----------|-------------|
| `--entity` | Yes | Entity logical name |
| `--name` | Yes | Attribute logical name |
| `--yes` | No | Skip confirmation prompt |

```sh
txc env entity attribute delete --entity tom_project --name tom_code --yes --apply
```

---

## Attribute Type Introspection

These commands help MCP clients and interactive users discover available attribute types and their parameters.

### `txc env entity attribute type list`

Lists all supported attribute types.

```sh
txc env entity attribute type list
```

### `txc env entity attribute type describe`

Outputs a JSON schema describing the parameters for a specific attribute type. Useful for MCP tool integration where clients need to know which options apply to a given type.

| Argument | Required | Description |
|----------|----------|-------------|
| `type` (positional) | Yes | Attribute type name (e.g. `string`, `lookup`) |

```sh
txc env entity attribute type describe lookup
```

---

## Relationship Commands

Manage many-to-many (N:N) relationships between entities.

### `txc env entity relationship create`

Creates a new N:N relationship.

| Option | Required | Description |
|--------|----------|-------------|
| `--entity1` | Yes | First entity logical name |
| `--entity2` | Yes | Second entity logical name |
| `--name` | Yes | Relationship schema name |
| `--display-name` | No | Display name |

```sh
txc env entity relationship create \
  --entity1 tom_project --entity2 account \
  --name tom_project_account --display-name "Project Accounts" --apply
```

### `txc env entity relationship list`

Lists relationships for an entity.

| Option | Required | Description |
|--------|----------|-------------|
| `--entity` | Yes | Entity logical name |

```sh
txc env entity relationship list --entity account
```

### `txc env entity relationship delete`

Deletes a relationship. Destructive — requires `--yes`.

| Option | Required | Description |
|--------|----------|-------------|
| `--name` | Yes | Relationship schema name |
| `--yes` | No | Skip confirmation prompt |

```sh
txc env entity relationship delete --name tom_project_account --yes --apply
```

---

## OptionSet Commands

Manage global option sets and individual options on both local and global option sets.

### `txc env entity optionset create-global`

Creates a new global option set.

| Option | Required | Description |
|--------|----------|-------------|
| `--name` | Yes | Schema name |
| `--display-name` | Yes | Display name |
| `--options` | Yes | Comma-separated list of option labels |
| `--description` | No | Description |
| `--solution` | No | Solution unique name |

```sh
txc env entity optionset create-global \
  --name tom_priority --display-name "Priority" \
  --options "Low,Medium,High,Critical" --apply
```

### `txc env entity optionset delete-global`

Deletes a global option set. Destructive — requires `--yes`.

| Option | Required | Description |
|--------|----------|-------------|
| `--name` | Yes | Global option set name |
| `--yes` | No | Skip confirmation prompt |

### `txc env entity optionset add-option`

Adds an option to a local or global option set. Target the option set by providing either `--entity` + `--attribute` (local) or `--global-optionset` (global).

| Option | Required | Description |
|--------|----------|-------------|
| `--entity` | No* | Entity logical name (for local option set) |
| `--attribute` | No* | Attribute logical name (for local option set) |
| `--global-optionset` | No* | Global option set name |
| `--label` | Yes | Label for the new option |
| `--value` | No | Explicit integer value (auto-assigned if omitted) |

\* Provide either `--entity` + `--attribute` or `--global-optionset`.

```sh
txc env entity optionset add-option \
  --global-optionset tom_priority --label "Urgent" --apply
```

### `txc env entity optionset delete-option`

Removes an option from a local or global option set. Destructive — requires `--yes`.

| Option | Required | Description |
|--------|----------|-------------|
| `--entity` | No* | Entity logical name (for local option set) |
| `--attribute` | No* | Attribute logical name (for local option set) |
| `--global-optionset` | No* | Global option set name |
| `--value` | Yes | Integer value of the option to remove |
| `--yes` | No | Skip confirmation prompt |

### `txc env entity optionset list-global`

Lists all global option sets in the environment.

```sh
txc env entity optionset list-global
```
