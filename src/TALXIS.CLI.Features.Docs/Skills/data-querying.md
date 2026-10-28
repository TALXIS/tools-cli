# Data Querying

## Choosing a Query Language

| Scenario | Command |
|---|---|
| Analytics, joins, aggregation, counts | `txc environment data query sql "<select>"` |
| Filter and select columns on one table; `$expand` / `$apply` via a full OData path | `txc environment data query odata <entity-set> ...` |
| Dataverse-native queries (linked entities, condition groups, aggregates) | `txc environment data query fetchxml "<fetch>...</fetch>"` or `--file <fetch.xml>` |
| One record by primary key | `txc environment data record get <record-guid> --entity <logicalName>` |

The three `query` commands are read-only and take `--top <n>` and `--include-annotations`.

## OData

`txc environment data query odata <entity-set-or-path>` takes the entity **set** name (plural, e.g. `accounts`). `--select`, `--filter`, `--order-by` and `--top` build the query string. For `$expand`, `$apply` or any other option, pass the whole OData path as the argument and no other options (they append `?...`). Examples with `$` use single quotes (bash, PowerShell).

```
txc environment data query odata accounts --select name,revenue --filter "revenue gt 1000000 and statecode eq 0" --order-by "createdon desc" --top 50
txc environment data query odata 'accounts?$select=name&$expand=primarycontactid($select=fullname,emailaddress1)'
txc environment data query odata 'accounts?$apply=groupby((statuscode),aggregate($count as count))'
```
The command follows `@odata.nextLink` pages itself and stops at `--top`. If the server rejects a path form, use SQL (`JOIN`, `GROUP BY`) or FetchXML (`<link-entity>`, `aggregate="true"`).

Lookups and option sets return raw GUIDs/integers; `--include-annotations` (also on `record get`, and with a full path) adds display values as `@OData.Community.Display.V1.FormattedValue`.

## SQL
```sql
SELECT a.name, c.fullname
FROM account a
JOIN contact c ON a.primarycontactid = c.contactid
WHERE a.statecode = 0
```

## FetchXML
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
