# Data Querying

## Choosing a Query Language

| Scenario | Language | Command |
|---|---|---|
| Analytics, joins, aggregation | SQL | `txc environment data query sql "<select>"` |
| Filtering and column selection on one table; `$expand` / `$apply` via a full OData path | OData | `txc environment data query odata <entity-set> ...` |
| Complex Dataverse-native queries (linked entities, conditions, aggregates) | FetchXML | `txc environment data query fetchxml "<fetch>...</fetch>"` or `--file <fetch.xml>` |
| Single record by primary key | — | `txc environment data record get <record-guid> --entity <logicalName>` |
| Quick record listing with filters | OData | `txc environment data query odata <entity-set> --filter "<expr>" --top <n>` |
| Record counts | SQL | `txc environment data query sql "SELECT COUNT(*) FROM <table>"` |

The three `query` commands are read-only and take `--top <n>` and `--include-annotations`.

## OData Query Patterns

`txc environment data query odata <entity-set-or-path>` takes the entity **set** name (the plural Web API name, e.g. `accounts`). `--select` (`$select`), `--filter` (`$filter`), `--order-by` (`$orderby`) and `--top` (`$top`) build the query string for the simple case. For `$expand`, `$apply` or any other query option, pass the whole OData path as the argument and do not combine it with those four options (they append `?...` to the path). The path is sent as written.

Quoting: the examples that contain `$` use single quotes, which work in bash and PowerShell; the quoting rules of other shells differ.

### Selecting and Filtering
```
txc environment data query odata accounts --select name,revenue --filter "revenue gt 1000000 and statecode eq 0"
```

### Ordering and Limiting
```
txc environment data query odata accounts --select name --order-by "createdon desc" --top 50
```
The command follows `@odata.nextLink` pages itself and stops at `--top`; you do not need to page manually.

### Expanding Lookups
```
txc environment data query odata 'accounts?$select=name&$expand=primarycontactid($select=fullname,emailaddress1)'
```
Resolves lookup references inline, in one request. For several columns of the related record or for joins across tables, SQL `JOIN` or a FetchXML `<link-entity>` (below) may read more simply.

### Server-Side Aggregation ($apply)
```
txc environment data query odata 'accounts?$apply=groupby((statuscode),aggregate($count as count))'
```
Use `$apply` for GROUP BY equivalents; it runs server-side. SQL (`SELECT statuscode, COUNT(*) FROM account GROUP BY statuscode`) and FetchXML with `aggregate="true"` do the same. These path forms were not run against a live environment while writing this doc; if the server rejects one, fall back to SQL or FetchXML.

### Formatted Values
Lookups and option sets return raw GUIDs/integers by default. Pass `--include-annotations` (all three `query` commands and `record get` accept it; it also works with a full-path argument) to also get the display values, as `@OData.Community.Display.V1.FormattedValue` annotations next to each column — use these for human-readable output.

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
