# Localization (Adding Languages to a Power Platform Project)

## Key Concept

Power Platform stores localized text **inline in the same XML files** as the source language. Adding a language means **adding parallel entries**, never replacing the existing ones. Each localizable element gets a sibling element with the new `languagecode="<LCID>"` (or `LCID="<LCID>"` for SiteMap) and a translated value attribute.

The `txc` workspace localization tools handle all the XML mechanics. Your job (or the LLM's job) is to translate strings in a flat JSON file — never to walk and edit XML by hand.

## Workflow Chain

1. **`workspace_localization_add`** — register the new LCID in every `Customizations.xml` of the workspace so solutions declare support for it.
2. **`workspace_localization_export`** — extract every untranslated source string into a directory of per-source-file JSONs (default: `./translations-<locale>/`). The output mirrors the workspace folder structure with `.json` instead of `.xml`, e.g. `translations-cs-CZ/src/Solutions.DataModel/Entities/udpp_warehouseitem/Entity.json`. One source XML produces one translation JSON. Each entry has a stable `id`, the `source` text, and `target: null`. Most files are small (3-10 entries); the largest entity files are ~50.
3. **Translate each JSON file in turn.** For every file in the output directory: Read it with the Read tool, set the `target` field of each entry using your own LLM knowledge of the target language, Write the file back. **Do this file-by-file** — do not write a script, do not use shell to read or aggregate, do not call an external translation API. Translation is the LLM's job. Move on to the next JSON when done.
4. **`workspace_localization_import`** — point `--file` at the output directory; the CLI walks all `*.json` recursively and applies them. The CLI inserts a `languagecode="<targetLcid>"` (or `LCID="<targetLcid>"`) sibling next to each English element. Existing entries are never modified or removed. Re-running is idempotent. **The `--file` path is deleted on a clean run** (no parse errors, no broken files) — the JSON has done its job. Pass `--keep` to retain it, or any error/broken JSON automatically retains the whole path for inspection.
5. **`workspace_localization_show`** — verify coverage. Reports total / translated / missing / coverage percent for the target language.

## Tools

| Tool | Purpose |
|---|---|
| `workspace_localization_add` | Register an LCID in `Customizations.xml`. Idempotent. |
| `workspace_localization_export` | Extract untranslated strings to JSON. Read-only on the workspace. |
| `workspace_localization_import` | Apply translated JSON back into XML. Idempotent: never replaces existing entries. |
| `workspace_localization_show` | Coverage report for a target language. Read-only. |

All four accept `--workspace <path>` (defaults to the workspace root) and language as either a locale (`cs-CZ`) or LCID (`1029`).

## System attributes are excluded by default

Power Platform entities inherit ~160 system attributes (createdon, modifiedon, owner, statecode, activity-related fields, status reasons, etc.) — Dataverse localizes these itself based on the user's locale, so they should not be translated in the solution. `export` and `show` filter them out automatically: a source string that matches a known system attribute is skipped if it appears inside an `Entity.xml`. Forms, ribbon, and saved queries are not affected by this filter.

Effect on a real project: an `Entity.json` that would have been ~50 entries (mostly system fields) collapses to ~10–15 entries — only the user's custom fields and the entity display name.

To include system attributes in the export (rare — only when explicitly retranslating platform-localized fields), pass `--add-system-attributes` on both `export` and `show` so the counts match.

## Translation Rules

When filling `target` fields:

- **Translate freely** — display names, descriptions, labels, tooltips, button titles, view names — using your knowledge of the target language. Power Platform metadata is not literary text; aim for what a native speaker would expect to see in a business app.
- **Keep as-is** — brand names (`Visa`, `Mastercard`), product/company identifiers (e.g. `UDPP`), schema/logical names, application slug names (`warehouseapp`), GUIDs, and anything that looks like an internal identifier.
- **Match casing conventions** of the target language for proper nouns and titles (Czech, German, French capitalize differently than English).
- **Don't invent translations for empty source strings** — leave them empty.

## Coverage Across XML Formats

The scanner finds localizable elements with either `languagecode="<LCID>"` (most Dataverse XML) or `LCID="<LCID>"` (SiteMap). Covered file types:

- `Solution.xml` — solution + publisher metadata
- `Customizations.xml` — language registration block
- `Entity.xml` — entity, collection, field, description names
- `OptionSets/*.xml` — option set + option labels and descriptions
- `FormXml/**/*.xml` — section, tab, field labels in main, card, and quick-view forms
- `SavedQueries/*.xml` — view names
- `RibbonDiff.xml` — ribbon button titles
- `AppModuleSiteMaps/**/AppModuleSiteMap.xml` — sitemap titles (via `LCID="..."`)
- `AppModules/**/AppModule.xml` — app display names
- `Other/Relationships/*.xml` — relationship descriptions

Files outside this set (PCF `ControlManifest.Input.xml` `*-key` references, Reqnroll `reqnroll.json`, plugin `.cs` source) are not auto-localized.

## Common Scenario — Add Czech Support End-to-End

```
1. workspace_localization_add { language: "cs-CZ" }
2. workspace_localization_export { language: "cs-CZ" }
   → produces a directory like translations-cs-CZ/ with ~30 small JSONs
3. For each JSON in that directory:
     - Read it
     - Set target on every entry using your Czech knowledge
     - Write it back
4. workspace_localization_import { file: "translations-cs-CZ" }
   → CLI walks the whole directory and applies every JSON
5. workspace_localization_show { language: "cs-CZ" }   # verify coverage = 100%
```

## What NOT to Do

- ❌ **Don't write a PowerShell/Python/Node script to add translations.** The whole point of this workflow is that the LLM translates in-context. A script can only do hardcoded string substitutions or call external APIs — both produce poor quality and miss the point.
- ❌ **Don't use shell to read, parse, deduplicate, or filter the translation JSON either** (`Get-Content`, `cat`, `jq`, `ConvertFrom-Json`, `Sort-Object -Unique`, etc.). Use the Read tool. If the file has duplicate sources, handle dedup mentally as you translate. Any pwsh/bash command that touches the translation JSON is a sign you're trying to short-circuit the in-context workflow.
- ❌ **Don't write the JSON back via shell either** (`Set-Content`, `Out-File`, `sed`, etc.). Use Write or Edit. No "one patch" scripts that apply translations from a lookup table — that is functionally a translation script.
- ❌ **Don't call Write twice in a row on the same JSON file.** The second Write must contain the **entire** updated file content, not just the new bits. Two partial writes produce `{...}{...}` concatenated objects that import cannot parse and will be reported as broken.
- ❌ **Don't edit XML files directly.** Always go through the four MCP tools. Manual edits will break stable IDs.
- ❌ **Don't delete or modify existing English (`languagecode="1033"`) entries.** Import only adds; preserve the source.
- ❌ **Don't translate identifiers, slugs, or schema names.** Leave application names like `warehouseapp` untranslated unless the user explicitly asks for a translated slug.
- ❌ **Don't run `import` before filling `target` values.** Empty targets are skipped, so you'll get coverage = 0%.
- ❌ **Don't copy English into target fields as a placeholder** — that hides untranslated entries from the next export's `--only-missing` filter.

See also: [project-structure](project-structure.md), [component-creation](component-creation.md)
