# UX Improvements — Notes from Integration Testing

Observations from running 35 integration tests against a live environment.

## Error Reporting

### 1. Error messages go to stderr only — stdout is empty on failure
**Impact:** High. Scripts piping stdout get no output when commands fail. The error is in stderr as structured JSON logs, which is correct for machine parsing but poor for human troubleshooting in scripts.

**Suggestion:** Consider writing a brief human-readable error line to stdout (in addition to the structured log on stderr) when `--format text` is active. Something like:
```
Error: Solution 'nonexistent' not found.
```
This way `txc env sln show nonexistent 2>/dev/null` produces useful output instead of nothing.

### 2. Auth errors show generic "Failed to connect to Dataverse" before the root cause
**Impact:** Medium. The `TxcLeafCommand` already surfaces the innermost exception ("Run `txc config auth login`"), but the two-line pattern is confusing:
```
[ERROR] Command failed: Failed to connect to Dataverse
[ERROR] Cause: No cached sign-in found for credential '...'. Run 'txc config auth login' and retry.
```
**Suggestion:** When the cause IS the auth issue, suppress the generic outer message and only show the actionable one.

### 3. `ConfigurationResolver` log line is noisy
**Impact:** Low. Every command prints:
```
[INFO] Resolved target environment 'https://...' using profile '...'
```
This is useful for debugging but clutters interactive output. It should be at `Debug` level (only visible with `--verbose`), not `Information`.

## Command Output

### 4. `sln show` — description field is missing from text output when null
**Impact:** Low. The text output skips the description line entirely when null. This is fine, but it would be more consistent to show `Description: (none)` like other fields do for null values.

### 5. `comp dep list/required/delete-check` — raw GUIDs are hard to use
**Impact:** High. The dependency commands output component GUIDs without display names:
```
EntityRelationship | c8b0f425-97d8-... | Entity | 70816501-edb9-... | Published
```
Users can't tell which relationship or entity this refers to without a separate lookup. This is the `ComponentNameResolver` gap — deferred from this PR but important for usability.

### 6. `comp layer show` — very large JSON output with no filtering
**Impact:** Medium. The active layer JSON for an entity can be 100+ KB. No way to filter to specific properties. Consider adding `--property` or `--jq` filtering.

### 7. `sln component list` — no total count in text output footer
**Impact:** Low. The command shows the count but `sln component count` shows a nicer summary. When listing without `--type`, the output can be long with no indication of total.

## Validation & Guards

### 8. `sln create` — no validation of solution unique name format
**Impact:** Medium. Publisher create validates the unique name (`[A-Za-z_][A-Za-z0-9_]*`), but `sln create` doesn't validate the solution name. Dataverse will reject invalid names, but the error message is cryptic. Should validate client-side.

### 9. `sln component add/remove` — no check if solution is unmanaged
**Impact:** Medium. Adding to a managed solution will fail server-side with a cryptic error. Should pre-check `ismanaged` and give a clear message: "Cannot add components to managed solution 'X'."

### 10. `comp layer remove-customization` — "no active layer" returns exit 0
**Impact:** Low. When there's no active layer to remove, the command returns success with a warning log. This is debatable — idempotent behavior (nothing to do = success) vs. informational error. Current behavior is reasonable.

### 11. `sln uninstall` — dep pre-check is a warning, not a blocking check
**Impact:** Medium. The uninstall command now warns about blocking dependencies but proceeds anyway (if `--yes` is given). Consider making it a blocking check — if deps exist and `--force` is not given, refuse to proceed.

## Missing Commands (discovered during testing)

### 12. No `publisher delete` command
**Impact:** Low. Test cleanup can't delete the test publisher. The `publisher` entity supports `DeleteRequest`. Add it.

### 13. No `sln component list` without `--type` shows ALL components
**Impact:** This works correctly (returns everything), but for large solutions (500+ components) it's slow and produces a wall of text. Consider adding `--top` default or pagination hint.

## Developer Experience

### 14. `--type` accepts both names and codes, but `ComponentTypeResolver` doesn't report available types on error
**Impact:** Medium. When a user types `--type Formmm`, the error is:
```
Unknown component type 'Formmm'. Use a type code (e.g. 1) or name (e.g. Entity).
```
It would be more helpful to list the available friendly names or suggest the closest match.

### 15. Double `ConfigurationResolver` log on commands that make 2 connections
**Impact:** Low. Commands like `comp layer remove-customization` (which calls both `ISolutionLayerQueryService` and `ISolutionLayerMutationService`) print the resolver log twice. Each service method creates its own connection. Not a bug, just noisy.
