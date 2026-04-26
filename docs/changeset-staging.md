# Changeset Staging

`txc` supports a **changeset staging** workflow that lets you queue multiple mutations locally and apply them to a live environment in a single, optimised batch. This is especially useful when scaffolding new entities with many attributes — instead of hitting the server once per operation, you stage everything first and apply once.

---

## Concepts

A **changeset** is a local collection of staged operations (schema changes, data writes, file uploads) that have not yet been sent to the server. Operations are persisted in `.txc/changeset.json` in the workspace root, so they survive CLI restarts and are scoped to the current working directory.

Every mutating command that extends `StagedCliCommand` exposes two execution-mode flags:

| Flag | Behaviour |
|------|-----------|
| `--apply` | Execute the operation immediately against the live environment |
| `--stage` | Save the operation to the local changeset for later batch apply |

Exactly one of `--apply` or `--stage` must be provided — omitting both or passing both is a validation error.

---

## Staging Commands

### Stage a change

Any mutating command that inherits `StagedCliCommand` accepts `--stage`. For example:

```sh
# Stage an entity creation
txc env entity create --name tom_project \
  --display-name "Project" --plural-name "Projects" \
  --ownership user --stage

# Stage attributes on the new entity
txc env entity attribute create tom_project \
  --name tom_name --type string --display-name "Name" --stage

txc env entity attribute create tom_project \
  --name tom_budget --type money --display-name "Budget" --stage
```

### View staged changes

```sh
txc env changeset status
```

Displays a table of all pending operations with columns: **#**, **Category**, **Operation**, **Target**, **Details**, **Staged At**. The footer shows hints about available apply strategies.

### Discard staged changes

```sh
txc env changeset discard --yes
```

Clears all staged operations from the changeset. This is a destructive action — without `--yes`, the CLI will prompt for confirmation.

### Apply the changeset

```sh
txc env changeset apply --strategy batch
```

Sends all staged operations to the live environment using the selected strategy. On full success the changeset is cleared automatically.

**Options:**

| Option | Required | Description |
|--------|----------|-------------|
| `--strategy` | Yes | `batch`, `transaction`, or `bulk` (see [Strategies](#strategies) below) |
| `--continue-on-error` | No | For the `batch` strategy, continue processing after a failure |

---

## Strategies

The `--strategy` flag on `txc env changeset apply` controls how data-plane operations are sent to the server:

| Strategy | Behaviour |
|----------|-----------|
| `batch` | Uses `ExecuteMultiple` with the `ContinueOnError` flag (when `--continue-on-error` is set). Operations are sent in a single round-trip but are not transactional — some may succeed while others fail. |
| `transaction` | Uses `ExecuteTransaction` — all-or-nothing. If any operation fails, the entire batch is rolled back and the response reports which operations were rolled back. |
| `bulk` | Groups operations by (entity, operation type) and uses `CreateMultiple` / `UpdateMultiple` for maximum throughput. Best for large data loads. |

---

## The 4-Phase Apply Pipeline

When you run `txc env changeset apply`, the `ChangesetApplier` processes operations in four ordered phases:

### Phase 1a — Schema batch (new entities)

New entity creation operations are grouped and sent via a single `ExecuteMultiple` request. Simple, batchable attribute types (string, number, money, bool, datetime, decimal, float, file, image) are included inline. This mirrors the `CreateEntities` optimisation described in [dataverse-metadata-performance.md](dataverse-metadata-performance.md).

### Phase 1b — Schema (remaining operations)

All remaining schema operations — entity updates/deletes, attribute creates/updates/deletes, relationship creates/deletes, and global option-set creates — are dispatched individually. After all schema operations complete, a **single `PublishXml`** call publishes all affected entities at once, avoiding the per-operation publish overhead.

### Phase 2 — Data

Record-level operations (create, update, delete, associate, disassociate) are applied using the selected strategy (`batch`, `transaction`, or `bulk`).

### Phase 3 — File uploads

File and image upload operations are processed sequentially. Dataverse's chunked block-upload API does not support batching, so each file is uploaded individually.

---

## Persistence

The changeset is stored at **`.txc/changeset.json`** relative to the current working directory. This means:

- Each workspace has its own independent changeset.
- The file is created on the first `--stage` call and deleted when the changeset is cleared (via `discard` or successful `apply`).
- The store is thread-safe — concurrent CLI invocations in the same workspace will not corrupt the file.

---

## Optimisations

### CreateEntities batching

When the changeset contains multiple new entity operations, Phase 1a batches them into a single `ExecuteMultiple` call with inline attributes. This reduces wall-clock time from minutes (sequential `CreateEntityRequest`) to seconds.

### Single PublishXml

Instead of publishing after every individual schema mutation, the pipeline collects all affected entity names and issues one targeted `PublishXml` at the end of Phase 1b. Publishing once instead of N times avoids repeatedly acquiring the organisation-wide exclusive publish lock.

---

## Example: scaffolding a new table with attributes

```sh
# 1. Stage the entity
txc env entity create --name tom_invoice \
  --display-name "Invoice" --plural-name "Invoices" \
  --ownership user --has-notes --stage

# 2. Stage attributes
txc env entity attribute create tom_invoice \
  --name tom_number --type string --display-name "Invoice Number" --stage

txc env entity attribute create tom_invoice \
  --name tom_amount --type money --display-name "Amount" --stage

txc env entity attribute create tom_invoice \
  --name tom_issuedate --type datetime --display-name "Issue Date" \
  --datetime-format dateonly --stage

txc env entity attribute create tom_invoice \
  --name tom_status --type choice --display-name "Status" \
  --options "Draft,Sent,Paid,Cancelled" --stage

# 3. Review what will be applied
txc env changeset status

# 4. Apply everything in one batch
txc env changeset apply --strategy batch
```

All five operations (one entity + four attributes) are sent to the server in optimised batches, with a single publish at the end.
