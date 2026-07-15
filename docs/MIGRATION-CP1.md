# CP1 migration notes for BDD agents

When CP1 is available in the CLI, the CP0 BDD agents should stop launching raw Playwright sessions themselves and delegate browser-session lifecycle to `txc`.

## What to change in `bdd-agent-v2`

1. Replace raw Playwright browser launch in `Hooks.cs` with:

```bash
txc ui session open --type AppModule --param name=WarehouseApp --profile <profile>
```

2. Have agents call `txc ui session open` from shell steps instead of owning Playwright bootstrap logic directly.
3. Keep using `guide_testing` to discover binding signatures and available steps.
4. Point follow-up browser inspection to:

```bash
txc ui session status
txc ui browser eval --eval "<javascript>"
```

## Expected impact

- Agent instructions become shorter because Playwright launch, recovery, and auth reuse move into the CLI.
- Session reuse becomes profile-scoped and consistent with the rest of `txc`.
- Mid-session recovery logic stays centralized in the CLI instead of being duplicated across agent prompts.
