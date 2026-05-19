# Data Plane

The data plane in `txc` is everything that reads from or writes to **records** in a live Dataverse environment — as opposed to the **application plane** (solutions, schema, packages) or the **control plane** (environment settings). This guide walks through every way to get data in and out of Dataverse with `txc`, when to pick which, and how the pieces fit together.

> All commands below assume you have an active profile. See [profiles-and-authentication.md](profiles-and-authentication.md). Pass `--profile <name>` to override for a single call.

---

## Mental model

There are **three ways to read** and **three ways to write**.

**Read:**

| Method | When to use |
|--------|-------------|
| `txc env data query odata` | Familiar OData syntax, simple filters, paging, `$select`. |
| `txc env data query fetchxml` | Aggregations, linked entities, fiscal date filters, anything FetchXML-only. |
| `txc env data query sql` | T-SQL via the Dataverse SQL endpoint — `SELECT ... WHERE ... JOIN ...`. |

**Write:**

| Method | Shape | When to use |
|--------|-------|-------------|
| `txc env data record … --apply` | One record, one call | Ad-hoc edits, scripts, quick fixes. |
| `txc env data record … --stage` then `txc env changeset apply` | Many records (or mixed entities / mixed operations), queued locally, submitted as one batch | Coding agents stitching together multiple writes, scripts that build up a workload step-by-step, anything you want to review before sending. |
| `txc env data bulk create\|update\|upsert` | Many records of **one entity**, **one operation**, from a JSON array | "Load this file into this table" — the classic ETL shape, no local staging state needed. |

There is also `txc data pkg` (Configuration Migration Tool) for full schema-driven dataset migration — see [CMT](#cmt-configuration-migration-tool) below.

---

## Querying

All three query commands print the same JSON shape on stdout (see [output-contract.md](output-contract.md) for the contract).

### OData

```sh
txc env data query odata accounts \
  --select "name,revenue" \
  --filter "revenue gt 1000000" \
  --orderby "name asc" \
  --top 10
```

Common flags: `--select`, `--filter`, `--orderby`, `--top`, `--expand`. The entity name is the **collection** name (e.g. `accounts`, `contacts`).

### FetchXML

```sh
txc env data query fetchxml '<fetch top="5"><entity name="contact"><attribute name="fullname"/></entity></fetch>'
```

Pass FetchXML inline or via `--file`. Use this when you need aggregations, linked entities, or fiscal date operators that OData doesn't expose.

### T-SQL

```sh
txc env data query sql "SELECT fullname, emailaddress1 FROM contact WHERE statecode = 0" --top 20
```

Uses the Dataverse SQL endpoint. Convenient for analytics-style reads, joins, and anyone more comfortable in SQL than OData.

---

## Single-record CRUD

Every record-mutating command supports both `--apply` (send now) and `--stage` (queue locally — see [staged bulk writes](#bulk-writes-via-staging) below). Exactly one of the two must be provided.

```sh
# Create
txc env data record create --entity account \
  --data '{"name":"Contoso Ltd","revenue":5000000}' --apply

# Get (read-only, no --apply/--stage)
txc env data record get $ID --entity account --select "name,revenue"

# Update
txc env data record update $ID --entity account \
  --data '{"revenue":7500000}' --apply

# Delete
txc env data record delete $ID --entity account --yes --apply

# Associate / disassociate (N:N relationships)
txc env data record associate $ID --entity account \
  --target $TARGET_ID --target-entity contact \
  --relationship accountleads_association --apply

txc env data record disassociate $ID --entity account \
  --target $TARGET_ID --target-entity contact \
  --relationship accountleads_association --apply

# File / image columns
txc env data record upload-file --entity account $ID \
  --column logo --file ./logo.png --apply

txc env data record download-file --entity account $ID \
  --column logo --output ./logo.png
```

### JSON value formats

Column types are auto-detected. Pass values in their natural JSON form:

| Column type | JSON value | Example |
|-------------|------------|---------|
| String, integer, decimal, boolean, datetime | Plain JSON primitive | `"name":"Contoso"`, `"revenue":5000000` |
| Money | Decimal number | `"revenue":5000000.00` |
| Option set / choice | Plain integer | `"statecode":0`, `"prioritycode":375970000` |
| Lookup (single-target) | Bare GUID string **or** `{Id, LogicalName}` | `"primarycontactid":"a1b2…"` |
| Lookup (polymorphic, e.g. customer/owner) | `{Id, LogicalName}` | `"customerid":{"Id":"a1b2…","LogicalName":"account"}` |

---

## Bulk writes via staging

The **changeset** is a local queue of pending operations stored in `.txc/changeset.json` in the workspace root. It is the same mechanism schema commands use — but it is **cross-plane**: schema, data, and file uploads can all be staged together and submitted in one optimised pipeline. See [changeset-staging.md](changeset-staging.md) for the full reference (4-phase apply pipeline, persistence, strategies).

Use staging for the data plane when you have:

- **Heterogeneous operations** — mix of `create`, `update`, `delete`, `associate` across one or more entities.
- **A review step** — you want to see `txc env changeset status` and approve before anything hits the server.
- **A long-running session** — a coding agent or script builds the workload incrementally over many CLI calls; staging survives restarts.

### Workflow

```sh
# 1. Stage operations — each command returns immediately, nothing is sent yet
txc env data record create --entity account \
  --data '{"name":"Contoso Ltd"}' --stage

txc env data record create --entity account \
  --data '{"name":"Fabrikam Inc"}' --stage

txc env data record update $EXISTING_ID --entity contact \
  --data '{"jobtitle":"VP Sales"}' --stage

# 2. Review what's queued
txc env changeset status

# 3. Apply everything in one optimised submission
txc env changeset apply --strategy bulk
```

### Choosing a strategy

`txc env changeset apply --strategy <batch|transaction|bulk>` controls how data-plane operations are sent. (Schema and file phases are unaffected — they always run in their own phases.)

| Strategy | SDK message | Behaviour |
|----------|-------------|-----------|
| `batch` | `ExecuteMultiple` | One round-trip, mixed operations, **not** transactional. Optionally `--continue-on-error`. |
| `transaction` | `ExecuteTransaction` | All-or-nothing — any failure rolls back the whole batch. |
| `bulk` | `CreateMultiple` / `UpdateMultiple` | Operations grouped by (entity, operation type) for maximum throughput. Best for large loads. |

> **Note:** `--strategy bulk` is the same SDK message as `txc env data bulk` — see the comparison [below](#staging--strategy-bulk-vs-env-data-bulk).

See [changeset-staging.md](changeset-staging.md#strategies) for behavioural details and the 4-phase apply pipeline.

---

## Homogeneous bulk writes via `env data bulk`

When you already have a JSON array of records for **one entity** and want to perform **one operation** on all of them, `txc env data bulk` is the most direct path. No staging state, no changeset file — just push the array and get a result.

```sh
# Create many of the same entity in one CreateMultiple request
txc env data bulk create --entity contact --file ./new-contacts.json

# Update many existing records (each record must include its primary key)
txc env data bulk update --entity contact --file ./contact-updates.json

# Upsert by primary or alternate key
txc env data bulk upsert --entity contact --file ./contacts.json
```

Input shape is a JSON array (see [JSON value formats](#json-value-formats) above):

```json
[
  { "firstname": "Ada",   "lastname": "Lovelace",   "emailaddress1": "ada@example.com" },
  { "firstname": "Alan",  "lastname": "Turing",     "emailaddress1": "alan@example.com" }
]
```

Pass inline with `--data '[…]'` or from a file with `--file ./contacts.json`.

### Staging + `--strategy bulk` vs. `env data bulk`

Both ultimately call the same `CreateMultiple` / `UpdateMultiple` / `UpsertMultiple` SDK messages — the difference is the **UX around them**:

| | `env data bulk create\|update\|upsert` | `record … --stage` + `changeset apply --strategy bulk` |
|---|---|---|
| Input | One JSON array per call | Many individual `--stage` calls accumulated locally |
| Scope per call | One entity, one operation | Any mix of entities and operations |
| Local state | None — stateless one-shot | `.txc/changeset.json` queue, survives restarts |
| Review before submit | n/a — fires immediately | `txc env changeset status` |
| Best for | Loading a prepared file into a table | Agents/scripts assembling a heterogeneous workload step by step |

Pick `env data bulk` when you already have the array. Pick staging when you don't — when the workload is built up over multiple commands or needs to be reviewed first.

---

## CMT (Configuration Migration Tool)

For schema-driven dataset migration — exporting a curated slice of configuration data from one environment, committing it to a repo, and importing it elsewhere — use `txc data pkg`. It runs natively on macOS, Linux, and Windows (no Windows VM needed) and exports to a folder by default so the data is diff-friendly.

```sh
txc data pkg export --schema ./data_schema.xml --output ./data-package --export-files
txc data pkg import ./data-package
txc data pkg convert --input export.xlsx --output data.xml
```

See [configuration-migration.md](configuration-migration.md) for the full deep-dive: deduplication logic, batching, parallel channels, prefetch tuning, and other options not exposed by PAC CLI or the CMT GUI.

---

## Decision matrix

| Situation | Use |
|-----------|-----|
| Read records | `txc env data query odata\|fetchxml\|sql` |
| Edit one record interactively | `txc env data record <verb> … --apply` |
| Insert/update N records of **one** entity from a prepared JSON array | `txc env data bulk create\|update\|upsert` |
| Mixed operations across one or more entities, assembled step-by-step | `record … --stage` × N, then `txc env changeset apply --strategy bulk` |
| All-or-nothing semantics (rollback on any failure) | `record … --stage` × N, then `txc env changeset apply --strategy transaction` |
| Heterogeneous mix, no rollback, but want a single round-trip | `record … --stage` × N, then `txc env changeset apply --strategy batch` |
| Schema-driven dataset migration between environments | `txc data pkg export` / `import` (CMT) |

---

## See also

- [changeset-staging.md](changeset-staging.md) — staging mechanics, strategies, 4-phase pipeline, persistence
- [configuration-migration.md](configuration-migration.md) — CMT internals and tuning
- [output-contract.md](output-contract.md) — JSON output shape for queries and write results
- [profiles-and-authentication.md](profiles-and-authentication.md) — `--profile`, credentials, headless/CI
