# Data Querying

## Choosing a Query Language

| Scenario | Language | Tool |
|---|---|---|
| Analytics, joins, aggregation | SQL | `environment_data_query_sql` |
| CRUD, filtering, lookup expansion | OData | `environment_data_query_odata` |
| Complex Dataverse-native queries (linked entities, conditions) | FetchXML | `environment_data_query_fetchxml` |
| Quick record listing with filters | — | `environment_data_record_list` |
| Record counts | — | `environment_data_record_count` |

## OData Query Patterns

### Selecting and Filtering
```
?$select=name,revenue&$filter=revenue gt 1000000 and statecode eq 0
```

### Expanding Lookups
```
?$select=name&$expand=primarycontactid($select=fullname,emailaddress1)
```
Resolves lookup references inline — avoids separate requests.

### Ordering and Paging
```
?$select=name&$orderby=createdon desc&$top=50
```
For large result sets, follow `@odata.nextLink` in the response to fetch subsequent pages.

### Server-Side Aggregation ($apply)
```
?$apply=groupby((statuscode),aggregate($count as count))
```
Use `$apply` for GROUP BY equivalents — runs server-side, avoids pulling all records.

### Formatted Values
Lookups and option sets return raw GUIDs/integers by default. The response includes formatted display values in `@OData.Community.Display.V1.FormattedValue` annotations — use these for human-readable output.

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
