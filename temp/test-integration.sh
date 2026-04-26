#!/usr/bin/env bash
# =============================================================================
# TALXIS CLI (txc) — Comprehensive Integration Test Suite
# =============================================================================
# Tests all new env commands against a live Dataverse environment.
#
# Usage:  bash temp/test-integration.sh
# Prereq: dotnet build the project first; auth token must be valid.
# =============================================================================

set -euo pipefail

# ── Configuration ────────────────────────────────────────────────────────────
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$REPO_ROOT/src/TALXIS.CLI"
TS="$(date +%s)"
TEST_PUBLISHER="txctest${TS}"
TEST_PUBLISHER_PREFIX="txct${TS: -2}"  # 4 letters + 2 digits = 6 chars, starts with letter
TEST_PUBLISHER_OVP="10001"            # fixed value in 10000-99999 range
TEST_SOLUTION="TxcTestSln${TS}"
ACCOUNT_ENTITY_ID="70816501-edb9-4740-a16c-6a5efbc05d84"
FIN_MYTABLE_ID="f84a27f8-e0c9-f011-8543-002248028cf4"

# ── Counters ─────────────────────────────────────────────────────────────────
PASS=0
FAIL=0
SKIP=0
TOTAL=0
RESULTS=()

# ── Helpers ──────────────────────────────────────────────────────────────────
txc() {
  # Run a txc command, suppressing structured JSON logs on stderr.
  dotnet run --project "$PROJECT" --no-build -- "$@" 2>/dev/null
}

txc_with_stderr() {
  # Run a txc command, keeping stderr for error-handling tests.
  dotnet run --project "$PROJECT" --no-build -- "$@"
}

run_test() {
  # run_test <test_number> <description> <expected_exit: 0|nonzero> <command...>
  local num="$1"; shift
  local desc="$1"; shift
  local expect_exit="$1"; shift

  TOTAL=$((TOTAL + 1))
  local exit_code=0
  local output
  output=$("$@" 2>&1) || exit_code=$?

  local status
  if [[ "$expect_exit" == "0" && "$exit_code" -eq 0 ]]; then
    status="PASS"
    PASS=$((PASS + 1))
  elif [[ "$expect_exit" == "nonzero" && "$exit_code" -ne 0 ]]; then
    status="PASS"
    PASS=$((PASS + 1))
  else
    status="FAIL"
    FAIL=$((FAIL + 1))
  fi

  RESULTS+=("$(printf "  %-4s %-60s [exit=%d expected=%s]" "$status" "#${num} ${desc}" "$exit_code" "$expect_exit")")
  printf "%-6s #%-3s %s  (exit=%d)\n" "$status" "$num" "$desc" "$exit_code"

  # On failure, show truncated output for debugging
  if [[ "$status" == "FAIL" ]]; then
    echo "        ↳ output (first 5 lines):"
    echo "$output" | head -5 | sed 's/^/          /'
  fi
}

run_test_output() {
  # Like run_test but also checks output contains a substring.
  # run_test_output <num> <desc> <expected_exit> <grep_pattern> <command...>
  local num="$1"; shift
  local desc="$1"; shift
  local expect_exit="$1"; shift
  local pattern="$1"; shift

  TOTAL=$((TOTAL + 1))
  local exit_code=0
  local output
  output=$("$@" 2>&1) || exit_code=$?

  local exit_ok=false
  if [[ "$expect_exit" == "0" && "$exit_code" -eq 0 ]]; then exit_ok=true; fi
  if [[ "$expect_exit" == "nonzero" && "$exit_code" -ne 0 ]]; then exit_ok=true; fi

  local grep_ok=false
  if echo "$output" | grep -qiE "$pattern"; then grep_ok=true; fi

  local status
  if $exit_ok && $grep_ok; then
    status="PASS"
    PASS=$((PASS + 1))
  else
    status="FAIL"
    FAIL=$((FAIL + 1))
  fi

  RESULTS+=("$(printf "  %-4s %-60s [exit=%d expected=%s match=%s]" "$status" "#${num} ${desc}" "$exit_code" "$expect_exit" "$grep_ok")")
  printf "%-6s #%-3s %s  (exit=%d, match=%s)\n" "$status" "$num" "$desc" "$exit_code" "$grep_ok"

  if [[ "$status" == "FAIL" ]]; then
    echo "        ↳ pattern: $pattern"
    echo "        ↳ output (first 8 lines):"
    echo "$output" | head -8 | sed 's/^/          /'
  fi
}

# ── Cleanup trap ─────────────────────────────────────────────────────────────
cleanup() {
  echo ""
  echo "═══════════════════════════════════════════════════════════════"
  echo "  CLEANUP"
  echo "═══════════════════════════════════════════════════════════════"

  # Try to delete test solution (ignore errors)
  echo "  Deleting test solution '$TEST_SOLUTION' (if exists)..."
  txc env sln delete "$TEST_SOLUTION" --yes -f text 2>/dev/null || true

  # Note: Dataverse doesn't support deleting publishers via API easily,
  # and there's no 'publisher delete' command. We leave the test publisher.
  echo "  (Test publisher '$TEST_PUBLISHER' left in env — no delete command available)"

  echo ""
  echo "═══════════════════════════════════════════════════════════════"
  echo "  SUMMARY"
  echo "═══════════════════════════════════════════════════════════════"
  for r in "${RESULTS[@]}"; do
    echo "$r"
  done
  echo ""
  printf "  Total: %d  |  Passed: %d  |  Failed: %d  |  Skipped: %d\n" \
    "$TOTAL" "$PASS" "$FAIL" "$SKIP"
  echo "═══════════════════════════════════════════════════════════════"

  if [[ "$FAIL" -gt 0 ]]; then
    exit 1
  fi
}
trap cleanup EXIT

# ── Auth check ───────────────────────────────────────────────────────────────
echo "═══════════════════════════════════════════════════════════════"
echo "  TALXIS CLI Integration Tests"
echo "  Environment: https://org2928f636.crm.dynamics.com"
echo "  Timestamp:   $TS"
echo "  Publisher:   $TEST_PUBLISHER (prefix: $TEST_PUBLISHER_PREFIX)"
echo "  Solution:    $TEST_SOLUTION"
echo "═══════════════════════════════════════════════════════════════"
echo ""
echo "── Auth pre-check ──"
AUTH_OUTPUT=$(txc env publisher list -f text 2>&1) || AUTH_EXIT=$?
if echo "$AUTH_OUTPUT" | grep -qi "Failed to connect"; then
  echo "FATAL: Authentication failed. Please refresh your Dataverse token."
  echo "$AUTH_OUTPUT" | head -5
  # Disable the cleanup trap — nothing to clean up
  trap - EXIT
  exit 2
fi
echo "Auth OK — connected to Dataverse."
echo ""

# ═════════════════════════════════════════════════════════════════════════════
# SECTION 1: Publisher Commands
# ═════════════════════════════════════════════════════════════════════════════
echo "── Publisher Commands ──"

# 1. List publishers
run_test_output 1 "env publisher list" \
  0 "MicrosoftCorporation|uniquename|Name" \
  txc env publisher list -f text

# 2. Show known publisher
run_test_output 2 "env publisher show MicrosoftCorporation" \
  0 "MicrosoftCorporation|microsoft|Microsoft" \
  txc env publisher show MicrosoftCorporation -f text

# 3. Show nonexistent publisher (error goes to stderr)
run_test 3 "env publisher show nonexistent (error)" \
  nonzero \
  txc env publisher show nonexistent_publisher_xyz -f text

# 4. Create test publisher
run_test 4 "env publisher create $TEST_PUBLISHER" \
  0 \
  txc env publisher create "$TEST_PUBLISHER" \
    --display-name "TXC Integration Test" \
    --prefix "$TEST_PUBLISHER_PREFIX" \
    --option-value-prefix "$TEST_PUBLISHER_OVP" \
    --description "Created by integration tests — safe to delete" \
    -f text

echo ""
echo "── Publisher Validation (error cases) ──"

# 5. Spaces in name → error
run_test_output 5 "publisher create: spaces in name → error" \
  nonzero "space|invalid|alphanumeric|name" \
  txc_with_stderr env publisher create "bad name" \
    --display-name "X" --prefix ab --option-value-prefix 10000 -f text

# 6. Prefix too short → error
run_test_output 6 "publisher create: prefix too short → error" \
  nonzero "prefix|short|2.*8|length|character|invalid" \
  txc_with_stderr env publisher create "txcvalid${TS}" \
    --display-name "X" --prefix a --option-value-prefix 10000 -f text

# 7. Prefix starts with mscrm → error
run_test_output 7 "publisher create: prefix starts with mscrm → error" \
  nonzero "mscrm|reserved|prefix|invalid" \
  txc_with_stderr env publisher create "txcvalid2${TS}" \
    --display-name "X" --prefix mscrm_test --option-value-prefix 10000 -f text

# 8. Option value prefix out of range → error (error goes to stderr)
run_test 8 "publisher create: OVP out of range → error" \
  nonzero \
  txc_with_stderr env publisher create "txcvalid3${TS}" \
    --display-name "X" --prefix ab --option-value-prefix 5000 -f text

# ═════════════════════════════════════════════════════════════════════════════
# SECTION 2: Solution CRUD
# ═════════════════════════════════════════════════════════════════════════════
echo ""
echo "── Solution CRUD ──"

# 9. Create solution
run_test 9 "env sln create $TEST_SOLUTION" \
  0 \
  txc env sln create "$TEST_SOLUTION" \
    --display-name "TXC Integration Test Solution" \
    --publisher "$TEST_PUBLISHER" \
    --version "1.0.0.0" \
    --description "Integration test solution — safe to delete" \
    -f text

# 10. Show solution
run_test_output 10 "env sln show $TEST_SOLUTION" \
  0 "$TEST_SOLUTION|Integration Test" \
  txc env sln show "$TEST_SOLUTION" -f text

# 11. List solutions (verify ours appears)
run_test_output 11 "env sln list (contains test solution)" \
  0 "$TEST_SOLUTION" \
  txc env sln list -f text

# 12. Publish all customizations
run_test 12 "env sln publish (all)" \
  0 \
  txc env sln publish -f text

# 13. Delete unmanaged solution
run_test 13 "env sln delete $TEST_SOLUTION" \
  0 \
  txc env sln delete "$TEST_SOLUTION" --yes -f text

echo ""
echo "── Solution Type Mismatch ──"

# 14. Uninstall an unmanaged solution (the Default solution is always unmanaged)
#     We need a known unmanaged solution — recreate our test solution briefly
txc env sln create "$TEST_SOLUTION" \
  --display-name "TXC Test (for uninstall mismatch)" \
  --publisher "$TEST_PUBLISHER" -f text 2>/dev/null || true

run_test_output 14 "env sln uninstall on unmanaged → type mismatch" \
  nonzero "unmanaged|managed|cannot uninstall|type|mismatch" \
  txc env sln uninstall "$TEST_SOLUTION" --yes -f text

# Clean up after test 14
txc env sln delete "$TEST_SOLUTION" --yes -f text 2>/dev/null || true

# 15. Delete on a managed solution → should reject
#     msdyn_RichTextEditor is a well-known managed solution
run_test_output 15 "env sln delete on managed → type mismatch" \
  nonzero "managed|unmanaged|cannot delete|type|mismatch|use.*uninstall" \
  txc env sln delete msdyn_RichTextEditor --yes -f text

# ═════════════════════════════════════════════════════════════════════════════
# SECTION 3: Solution Component Commands
# ═════════════════════════════════════════════════════════════════════════════
echo ""
echo "── Solution Component Commands ──"

# Find a solution with components for listing — try common ones
COMP_SOLUTION="Basic"

# 16. List components
run_test 16 "env sln component list $COMP_SOLUTION" \
  0 \
  txc env sln component list "$COMP_SOLUTION" --top 10 -f text

# 17. Count components
run_test_output 17 "env sln component count $COMP_SOLUTION" \
  0 "Entity|Attribute|total|count|[0-9]" \
  txc env sln component count "$COMP_SOLUTION" -f text

# 18. List components filtered by type
run_test 18 "env sln component list --type Entity" \
  0 \
  txc env sln component list "$COMP_SOLUTION" --type Entity --top 5 -f text

# 19-20: Add/remove a component to/from our test solution
echo ""
echo "── Component Add/Remove ──"

# Recreate the test solution for add/remove tests
txc env sln create "$TEST_SOLUTION" \
  --display-name "TXC Test (component ops)" \
  --publisher "$TEST_PUBLISHER" -f text 2>/dev/null || true

# 19. Add account entity to the test solution
run_test 19 "env sln component add (account entity)" \
  0 \
  txc env sln component add "$TEST_SOLUTION" \
    --component-id "$ACCOUNT_ENTITY_ID" --type Entity -f text

# 20. Remove account entity from the test solution
run_test 20 "env sln component remove (account entity)" \
  0 \
  txc env sln component remove "$TEST_SOLUTION" \
    --component-id "$ACCOUNT_ENTITY_ID" --type Entity --yes -f text

# ═════════════════════════════════════════════════════════════════════════════
# SECTION 4: Component Inspection (Layers & Dependencies)
# ═════════════════════════════════════════════════════════════════════════════
echo ""
echo "── Component Layers ──"

# 21. Layer list for fin_mytable
run_test 21 "env comp layer list (fin_mytable)" \
  0 \
  txc env comp layer list "$FIN_MYTABLE_ID" --type Entity -f text

# 22. Layer show (active layer JSON)
run_test 22 "env comp layer show (fin_mytable)" \
  0 \
  txc env comp layer show "$FIN_MYTABLE_ID" --type Entity -f text

echo ""
echo "── Component Dependencies ──"

# 23. Dependency list for account entity
run_test 23 "env comp dep list (account)" \
  0 \
  txc env comp dep list "$ACCOUNT_ENTITY_ID" --type Entity -f text

# 24. Required dependencies for account
run_test 24 "env comp dep required (account)" \
  0 \
  txc env comp dep required "$ACCOUNT_ENTITY_ID" --type Entity -f text

# 25. Delete-check for account entity (has 54+ blocking deps → exit 1)
run_test 25 "env comp dep delete-check (account, blocked)" \
  nonzero \
  txc env comp dep delete-check "$ACCOUNT_ENTITY_ID" --type Entity -f text

# ═════════════════════════════════════════════════════════════════════════════
# SECTION 5: Uninstall-Check
# ═════════════════════════════════════════════════════════════════════════════
echo ""
echo "── Uninstall-Check ──"

# 26. Uninstall-check on a safe solution (exit 0 = can uninstall)
#     "Model" is typically a simple solution; adjust if needed
run_test 26 "env sln uninstall-check Model (safe)" \
  0 \
  txc env sln uninstall-check Model -f text

# 27. Uninstall-check on a blocked solution (exit 1 = has blockers)
run_test_output 27 "env sln uninstall-check msdyn_RichTextEditor (blocked)" \
  nonzero "block|depend|cannot|required" \
  txc env sln uninstall-check msdyn_RichTextEditor -f text

# ═════════════════════════════════════════════════════════════════════════════
# SECTION 6: Error Handling & Edge Cases
# ═════════════════════════════════════════════════════════════════════════════
echo ""
echo "── Error Handling ──"

# 28. Show nonexistent solution (error goes to stderr)
run_test 28 "env sln show nonexistent_solution" \
  nonzero \
  txc env sln show nonexistent_solution_xyz -f text

# 29. Component list on nonexistent solution (error goes to stderr)
run_test 29 "env sln component list nonexistent" \
  nonzero \
  txc env sln component list nonexistent_solution_xyz -f text

# 30. Invalid GUID for dep list
run_test_output 30 "env comp dep list (invalid GUID)" \
  nonzero "guid|invalid|format|parse|not a valid" \
  txc_with_stderr env comp dep list not-a-guid --type Entity -f text

# 31. Invalid component type
run_test_output 31 "env comp dep list (invalid type)" \
  nonzero "type|invalid|unknown|not recognized|InvalidType" \
  txc_with_stderr env comp dep list 00000000-0000-0000-0000-000000000000 --type InvalidType -f text

# 32. Remove-customization on a component without an active unmanaged layer
#     Error output goes to stderr; just check exit code
run_test 32 "env comp layer remove-customization (nothing to remove)" \
  0 \
  txc env comp layer remove-customization "$ACCOUNT_ENTITY_ID" --type Entity --yes -f text

# ═════════════════════════════════════════════════════════════════════════════
# SECTION 7: JSON format validation
# ═════════════════════════════════════════════════════════════════════════════
echo ""
echo "── JSON Format Output ──"

# Bonus: verify -f json produces valid JSON for a few commands
validate_json_output() {
  local num="$1"; shift
  local desc="$1"; shift

  TOTAL=$((TOTAL + 1))
  local exit_code=0
  local output
  output=$("$@" 2>/dev/null) || exit_code=$?

  local status="FAIL"
  if [[ "$exit_code" -eq 0 ]] && echo "$output" | python3 -m json.tool >/dev/null 2>&1; then
    status="PASS"
    PASS=$((PASS + 1))
  else
    FAIL=$((FAIL + 1))
  fi

  RESULTS+=("$(printf "  %-4s %-60s [exit=%d json_valid=%s]" "$status" "#${num} ${desc}" "$exit_code" "$([[ $status == PASS ]] && echo true || echo false)")")
  printf "%-6s #%-3s %s  (exit=%d)\n" "$status" "$num" "$desc" "$exit_code"

  if [[ "$status" == "FAIL" ]]; then
    echo "        ↳ output (first 5 lines):"
    echo "$output" | head -5 | sed 's/^/          /'
  fi
}

validate_json_output 33 "publisher list -f json → valid JSON" \
  txc env publisher list -f json

validate_json_output 34 "sln list -f json → valid JSON" \
  txc env sln list -f json

validate_json_output 35 "sln component count $COMP_SOLUTION -f json → valid JSON" \
  txc env sln component count "$COMP_SOLUTION" -f json

echo ""
echo "── All tests executed ──"
# Cleanup + summary runs via the EXIT trap
