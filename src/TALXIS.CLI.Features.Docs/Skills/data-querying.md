# Data Querying

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## Choosing a Query Language

| Scenario | Language | Command |
|---|---|---|
| Analytics, joins, aggregation | SQL | `txc environment data query sql "<select>"` |
| Filtering and column selection on one table | OData | `txc environment data query odata <entity-set> ...` |
| Complex Dataverse-native queries (linked entities, conditions, aggregates) | FetchXML | `txc environment data query fetchxml "<fetch>...</fetch>"` or `--file <fetch.xml>` |
| Single record by primary key | — | `txc environment data record get <record-guid> --entity <logicalName>` |
| Quick record listing with filters | OData | `txc environment data query odata <entity-set> --filter "<expr>" --top <n>` |
| Record counts | SQL | `txc environment data query sql "SELECT COUNT(*) FROM <table>"` |

The three `query` commands are read-only and take `--top <n>` and `--include-annotations`.

## OData Query Patterns

`txc environment data query odata <entity-set>` takes the entity **set** name (the plural Web API name, e.g. `accounts`) and builds the query from four options: `--select` (`$select`), `--filter` (`$filter`), `--order-by` (`$orderby`) and `--top` (`$top`). There is no option for `$expand` or `$apply`: use SQL (joins, `GROUP BY`) or FetchXML (linked entities, aggregates) for those.

### Selecting and Filtering
```
txc environment data query odata accounts --select name,revenue --filter "revenue gt 1000000 and statecode eq 0"
```

### Ordering and Limiting
```
txc environment data query odata accounts --select name --order-by "createdon desc" --top 50
```
The command follows `@odata.nextLink` pages itself and stops at `--top`; you do not need to page manually.

### Expanding Lookups and Aggregation
Lookup values come back as GUIDs; to read related columns use a SQL `JOIN` or a FetchXML `<link-entity>` (see below). For GROUP BY equivalents use SQL (`SELECT statuscode, COUNT(*) FROM account GROUP BY statuscode`) or FetchXML with `aggregate="true"`; both run server-side.

### Formatted Values
Lookups and option sets return raw GUIDs/integers by default. Pass `--include-annotations` (all three `query` commands and `record get` accept it) to also get the display values, as `@OData.Community.Display.V1.FormattedValue` annotations next to each column — use these for human-readable output.

## SQL Patterns

SQL is best for ad-hoc analytics and JOINs across tables:
```sql
SELECT a.name, c.fullname
FROM account a
JOIN contact c ON a.primarycontactid = c.contactid
WHERE a.statecode = 0
```

## FetchXML Patterns

FetchXML supports Dataverse-native features like linked entities and complex condition groups:
```xml
<fetch top="10">
  <entity name="account">
    <attribute name="name" />
    <link-entity name="contact" from="contactid" to="primarycontactid">
      <attribute name="fullname" />
    </link-entity>
    <filter>
      <condition attribute="statecode" operator="eq" value="0" />
    </filter>
  </entity>
</fetch>
```
