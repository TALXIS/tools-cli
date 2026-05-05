# M5 — Idempotency and Alternate-Key Resolution

> Goal: Make repeated imports converge to the same state and let sidecars (and, where possible, CMT itself) reference records by natural keys instead of GUIDs.

## Why

CMT's idempotency is weak by design:

- Hard-coded `enabledDuplicateDetection: false`.
- Dedup only by GUID, with `primarynamefield` as a single fallback.
- No alternate-key support at all.

Real legacy migrations almost never have stable GUIDs in the source system. Records carry natural keys (customer number, ledger code, email) that the migration must preserve as the matching axis across re-runs.

## Two complementary mechanisms

### Mechanism A — Deterministic GUID synthesis (existing primitive)

The `Transform` server already exposes `/ComputePrimaryKey` (MD5 over `(entity, sortedAlternateKeyTuple)`). Promote this to:

- A first-class library service `IRecordKeyService.ComputeId(entity, IDictionary<string,string> alt)` re-using the same MD5 algorithm so HTTP and library outputs match byte-for-byte.
- A pre-stage of `txc data package convert` that, when `data_keys.xml` declares a key for an entity and the key fields are filled, **rewrites the record `id` to the deterministic GUID** before emitting `data.xml`.

Result: subsequent CMT imports match by GUID (which CMT *does* support), and re-runs converge.

### Mechanism B — Runtime alternate-key resolution (new)

For sidecars and for `data.xml` records whose ids cannot be synthesized (e.g. lookups into existing-in-target records), txc resolves via FetchXML at apply time:

```
ReferenceResolver.Resolve("account", key="byNumber", value="ACC-001")
  -> FetchXML: <fetch><entity name="account"><attribute name="accountid" /><filter>
       <condition attribute="accountnumber" operator="eq" value="ACC-001" /></filter></entity></fetch>
  -> caches result; multiple references to the same key share one query
  -> 0 hits  -> error (or auto-create stub if --create-stubs is set, future)
  -> 2+ hits -> error (key was not actually unique)
```

Lookups are read-only and don't require Dataverse alternate keys to be physically defined — txc treats them as "convention".

## CLI surface

```
txc data package convert
  ...
  --keys data_keys.xml          # NEW: enables Mechanism A
  --keys-from-package <path>    # NEW: pulls data_keys.xml from a package folder

txc data record resolve-key
  --entity account
  --key byNumber
  --value ACC-001
  [--profile ...]               # required to query
```

`txc data record resolve-key` is a thin wrapper around `IRecordKeyService.ResolveAsync` for scripting and CI checks.

## Idempotency strategy across phases

| Operation source | Idempotency |
|---|---|
| `data.xml` records (CMT) | Match-by-GUID. With Mechanism A active, the GUID is derived from natural keys, so re-runs match the same row. |
| Sidecar references in `data_owners.xml`, etc. | Resolved via Mechanism B at apply time. M4 step probes ensure no-op when target state matches. |
| Custom API `<call>` ops | Opt-in idempotent via `<when>` FetchXML probe (M4). |
| Workflow `<execute>` ops | Always re-runs unless guarded by `<when>`. |

## Failure semantics

- Mechanism A on a record without all alt-key fields filled → hard error during `convert` (clear list of missing fields).
- Mechanism B with 0 hits → hard error during apply, with the offending key/value/entity in the message.
- Mechanism B with >1 hit → hard error; key declaration is wrong, not the data.

## Edge cases

- **Self-references / hierarchical data**: Mechanism A's deterministic GUIDs solve cycles for free — both ends of a self-reference resolve to the same predictable GUID.
- **Composite keys**: `data_keys.xml` allows multi-field keys. Hash inputs are sorted alphabetically by field logical name to keep hashing canonical.
- **Lookups across entities**: A lookup column whose value is `keyref:byNumber=ACC-001` is rewritten to the synthesized GUID for the target entity at convert time (when Mechanism A is enabled for the target entity), or left as a `keyref:` token resolved at apply time (Mechanism B).
- **Files / annotations**: Annotations and file columns inherit the parent record id and need no special handling.

## Implementation outline

```
Package/Keys/
├── IRecordKeyService.cs         # Compute(entity, dict) + Resolve(entity, key, value, profile, ct)
├── RecordKeyService.cs          # MD5 implementation (shared with HTTP server)
├── KeyDictionary.cs             # parsed data_keys.xml in-memory model
└── KeyAwareXmlRewriter.cs       # used by convert to rewrite ids and lookup tokens
```

Wire `IRecordKeyService` into DI; replace the inline MD5 in `ComputePrimaryKeyController` with a call to the same service.

## Tests

- Same alt-key tuple from HTTP and library returns the same GUID byte-for-byte.
- `KeyAwareXmlRewriter` rewrites both record ids and lookup tokens.
- Re-import test: import package twice into a clean env; second run shows zero CMT writes (verified via `--report`) for unchanged records.
- Resolver caching: 100 references to same key issue 1 query.

## Done when

- A package authored with only natural keys (no GUIDs) round-trips through `convert` → `import` → `import again` with zero net change.
- `txc data record resolve-key` is documented and exits non-zero on 0 / >1 hits.
- `docs/configuration-migration.md` gains an "Idempotency and Keys" section.
