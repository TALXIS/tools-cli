# UI Testing Workflow

<!-- Internal reasoning skill: contains ONLY test-generation guidance. -->
<!-- For available step bindings, see the TestingBindingsCatalog prompt. -->

## User wants to "write a UI test" / "test a form" / "test navigation"

-> STRUCTURE: Feature file (.feature) with Scenario(s) using Given/When/Then
-> ALWAYS start with a Given step for login: `Given I am logged in to the '{app}' app as '{user}'`
-> ALWAYS use pre-built step bindings from TALXIS.TestKit.Bindings where available
-> ONLY write custom step bindings for app-specific logic not covered by the library

## Test Structure Best Practices

-> Feature files group related scenarios by business capability
-> Each scenario should be independent (no shared state between scenarios)
-> Use Background for shared Given steps across all scenarios in a feature
-> Keep scenarios focused on ONE behavior/assertion
-> Use Scenario Outline for data-driven tests

## Given/When/Then Conventions

-> Given: Setup preconditions (login, create test data, navigate to starting point)
-> When: Perform the action being tested (click, enter data, navigate)
-> Then: Assert the expected outcome (field values, visibility, error messages)
-> Avoid multiple When steps — split into separate scenarios instead

## Test Data Setup

-> Use `Given I have created '{alias}'` with JSON data files in a /data folder
-> Data files use Web API deep-insert syntax with @logicalName, @alias, @extends
-> Use faker.js templates for dynamic data ({{name.firstName}}, {{finance.amount}})
-> Set `deleteTestData: true` in appsettings.json for cleanup after scenarios

## Common Patterns

### Testing form field entry:
```gherkin
When I enter '{value}' into the '{field label}' field on the form
```

### Testing navigation:
```gherkin
When I open the sub area '{subarea}' under the '{area}' area
When I open the '{subarea}' sub area of the '{group}' group
```

### Testing command bar:
```gherkin
When I select the '{command}' command
Then I should be able to see the '{command}' command
```

### Testing grids/views:
```gherkin
When I open the record at position '{n}' in the grid
Then I can see '{n}' records in the grid
```

### Testing lookups:
```gherkin
When I select '{value}' from the '{field}' lookup field
```

## Error Recovery

-> If a step binding fails with a timeout: check if Driver.WaitForTransaction() is needed
-> If login fails: verify user credentials in appsettings.json and OTP token configuration
-> If element not found: the app may need WaitForPageToLoad before interaction
