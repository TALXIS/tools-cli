# Playwright setup for `TALXIS.CLI.Platform.Playwright`

Before running the Playwright-backed tests or `txc ui` commands on a fresh machine, install Chromium for Playwright:

```bash
npx playwright install chromium
```

If your CI agent does not already have the OS dependencies that Playwright needs, install them together with the browser binaries:

```bash
npx playwright install --with-deps chromium
```

## CI notes

- Cache `~/.cache/ms-playwright/` to avoid downloading browser binaries on every run.
- `PLAYWRIGHT_BROWSERS_PATH` can be used to override the browser cache location.
- If test bootstrap ever needs to install browsers programmatically, use `Microsoft.Playwright.Program.Main(new[] { "install", "chromium" })` in setup code rather than ad-hoc shell logic.
