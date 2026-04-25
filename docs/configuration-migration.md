# Configuration Migration Tool (CMT) — `txc data package`

## 1. Overview

The **Configuration Migration Tool (CMT)** is a Microsoft utility for migrating configuration and reference data between Dataverse environments. It serializes entity records, lookups, many-to-many relationships, and file columns into a portable XML-based package that can be imported into another environment.

`txc data package` wraps the CMT engine so you can export, import, and convert data packages entirely from the command line — no GUI required.

> **Microsoft docs:** [Manage configuration data](https://learn.microsoft.com/en-us/power-platform/admin/manage-configuration-data)

### `txc data package` vs PAC CLI

| Capability | `txc data package` | `pac data` |
|---|---|---|
| Export with schema file | ✅ `txc data package export` | ✅ `pac data export` |
| Import data package | ✅ `txc data package import` | ✅ `pac data import` |
| File column support | ✅ `--export-files` | ❌ Not supported |
| Parallel connections | ✅ `--connection-count` | ❌ Single connection |
| Batch mode (ExecuteMultiple / UpsertMultiple) | ✅ `--batch-mode` | ❌ One-by-one |
| Safety-check override | ✅ `--override-safety-checks` | ❌ |
| Prefetch tuning | ✅ `--prefetch-limit` | ❌ |
| XLSX → CMT XML conversion | ✅ `txc data package convert` | ❌ |
| Authentication | `txc` profiles | PAC auth profiles |

### When to Use CMT

Use CMT / `txc data package` when you need to:

- Move **reference/configuration data** (currencies, business units, security roles, option-set seed data) between environments.
- Preserve **record GUIDs** across environments so that lookups and relationships remain intact.
- Automate data seeding in CI/CD pipelines.
- Migrate **file columns** and **image columns** between environments.

For **bulk transactional data** or **ETL workloads**, consider the Dataverse Import Data Wizard, Azure Data Factory, or SSIS instead.

---

## 2. Quick Start

### Prerequisites

1. Install `txc` (the TALXIS CLI).
2. Authenticate: `txc config auth login --profile myprofile`
3. Have a CMT schema file (`data_schema.xml`). You can create one with the [Configuration Migration Tool GUI](https://learn.microsoft.com/en-us/power-platform/admin/manage-configuration-data) or write it by hand (see [Schema File Reference](#4-schema-file-reference-data_schemaxml)).

### Export data

```bash
txc data package export \
  --schema data_schema.xml \
  --output data.zip \
  --profile myprofile
```

### Import data

```bash
txc data package import data.zip --profile myprofile
```

### Typical workflow

```
┌──────────────────────────────────────┐
│  1. Create data_schema.xml           │
│     (CMT GUI or hand-written)        │
└──────────────┬───────────────────────┘
               ▼
┌──────────────────────────────────────┐
│  2. Export from source environment    │
│     txc data package export          │
│       --schema data_schema.xml       │
│       --output data.zip              │
│       --profile source-env           │
└──────────────┬───────────────────────┘
               ▼
┌──────────────────────────────────────┐
│  3. Import into target environment   │
│     txc data package import data.zip │
│       --profile target-env           │
└──────────────────────────────────────┘
```

---

## 3. Command Reference

### `txc data package export`

Export data from a Dataverse environment using a CMT schema file.

```
txc data package export --schema <path> --output <path> [options]
```

| Option | Alias | Required | Default | Description |
|---|---|---|---|---|
| `--schema <path>` | `-s` | **Yes** | — | Path to the schema file (`data_schema.xml`) that defines which entities, fields, and relationships to export. You can create this file using the Configuration Migration Tool GUI or write it by hand. |
| `--output <path>` | `-o` | **Yes** | — | Where to save the exported data package (`.zip` file). The zip will contain `data.xml` with the records and a copy of the schema. |
| `--export-files` | — | No | `false` | Also download binary file and image columns (e.g. profile pictures, attachments). These are saved inside the zip in a `files/` folder. Off by default because it can be slow for large files. |
| `--overwrite` | — | No | `false` | Allow overwriting the output file if it already exists. Without this flag, the command will refuse to overwrite. |
| `--profile <name>` | `-p` | No | *(active profile)* | Profile name to resolve (falls back to `TXC_PROFILE`, workspace pin, or global active). |
| `--verbose` | — | No | `false` | Emit verbose logging for this invocation. |

**Example — export with file columns:**

```bash
txc data package export \
  --schema data_schema.xml \
  --output data.zip \
  --export-files \
  --overwrite \
  --profile dev
```

### `txc data package import`

Import a CMT data package into a Dataverse environment.

```
txc data package import <path> [options]
```

| Argument / Option | Alias | Required | Default | Description |
|---|---|---|---|---|
| `<path>` *(argument)* | — | **Yes** | — | Path to the CMT data package (`.zip` file or folder containing `data.xml` and `data_schema.xml`). |
| `--connection-count <N>` | — | No | `1` | How many parallel connections to open against the environment. More connections = faster import for large datasets. Each connection authenticates separately. |
| `--batch-mode` | — | No | `false` | Send records in batches instead of one-by-one. Much faster for large imports. Batches use `ExecuteMultiple` or `UpsertMultiple` depending on org version. |
| `--batch-size <N>` | — | No | `600` | How many records to send per batch request. Only used when `--batch-mode` is on. Lower values are safer, higher values are faster. |
| `--override-safety-checks` | — | No | `false` | **DANGEROUS:** Skip all duplicate checking. Every record will be created as new, even if it already exists. Use only when importing into a clean empty environment. |
| `--prefetch-limit <N>` | — | No | `4000` | How many existing records to load into memory per entity for faster duplicate detection. If an entity has more records than this limit, each record is checked individually against the server (slower). Increase for large entities. |
| `--profile <name>` | `-p` | No | *(active profile)* | Profile name to resolve (falls back to `TXC_PROFILE`, workspace pin, or global active). |
| `--verbose` | — | No | `false` | Emit verbose logging for this invocation. |

**Example — fast import with parallelism and batching:**

```bash
txc data package import data.zip \
  --connection-count 4 \
  --batch-mode \
  --batch-size 200 \
  --profile staging
```

### `txc data package convert`

Convert tables from an XLSX file to CMT data package XML.

```
txc data package convert --input <xlsx> --output <xml>
```

| Option | Required | Description |
|---|---|---|
| `--input <path>` | **Yes** | Path to the input XLSX file. Each named table in the workbook becomes an `<entity>` in the output. |
| `--output <path>` | **Yes** | Path to the output XML file (CMT `data.xml` format). |

The converter reads every named table in the workbook, uses the table name as the entity `name`, the first row as field names, and generates a new GUID for each record.

**Example:**

```bash
txc data package convert --input seed-data.xlsx --output data.xml
```

---

## 4. Schema File Reference (`data_schema.xml`)

The schema file tells CMT **what** to export — which entities, which fields, how to filter, and in what order to import.

### Root element: `<entities>`

```xml
<entities dateMode="absolute">
  <entityImportOrder>
    <entityName>businessunit</entityName>
    <entityName>account</entityName>
    <entityName>contact</entityName>
  </entityImportOrder>

  <entity name="account" displayname="Account" etc="1"
          primaryidfield="accountid" primarynamefield="name"
          disableplugins="true">
    <!-- fields, filter, relationships -->
  </entity>
</entities>
```

| Attribute | Type | Description |
|---|---|---|
| `dateMode` | `absolute` \| `relative` \| `relativeDaily` | Global date handling mode. See [Date Handling Modes](#10-date-handling-modes). Optional. |

#### `<entityImportOrder>`

Defines the order in which entities are imported. This is critical when entities have lookup dependencies on each other — parent entities must be imported first.

```xml
<entityImportOrder>
  <entityName>businessunit</entityName>
  <entityName>transactioncurrency</entityName>
  <entityName>account</entityName>
  <entityName>contact</entityName>
</entityImportOrder>
```

### Entity element: `<entity>`

| Attribute | Type | Required | Description |
|---|---|---|---|
| `name` | string | **Yes** | Entity logical name (e.g. `account`, `contact`, `cr4c2_customentity`). |
| `displayname` | string | **Yes** | Display name (used in logging and UI). |
| `etc` | int | **Yes** | Entity type code. |
| `primaryidfield` | string | **Yes** | Logical name of the primary key field (e.g. `accountid`). |
| `primarynamefield` | string | **Yes** | Logical name of the primary name field (e.g. `name`). Used as a deduplication fallback — if a record can't be matched by ID, CMT tries matching by this field. |
| `disableplugins` | bool | No | If `true`, bypasses plugin execution during import (sets the `BypassCustomPluginExecution` flag). Requires the caller to have the `prvBypassCustomPlugins` privilege. |
| `skipupdate` | bool | No | If `true`, existing records that match during dedup are skipped entirely — no update is performed. Useful for "insert-if-missing" scenarios. |
| `forcecreate` | bool | No | If `true`, all records are always created as new — deduplication is skipped entirely for this entity. Use when you know every record is new. |

### Field element: `<field>`

Fields are nested inside a `<fields>` collection on each entity.

```xml
<fields>
  <field name="accountid" displayname="Account" type="guid" primaryKey="true" />
  <field name="name" displayname="Account Name" type="string" />
  <field name="primarycontactid" displayname="Primary Contact" type="entityreference"
         lookupType="contact" />
  <field name="cr4c2_dedup_key" displayname="Dedup Key" type="string"
         updateCompare="true" customfield="true" />
  <field name="createdon" displayname="Created On" type="datetime"
         dateMode="relative" />
</fields>
```

| Attribute | Type | Required | Description |
|---|---|---|---|
| `name` | string | **Yes** | Field logical name. |
| `displayname` | string | **Yes** | Display name. |
| `type` | string | **Yes** | Data type identifier. See [Supported Data Types](#6-supported-data-types). |
| `updateCompare` | bool | No | If `true`, this field is used as a custom deduplication key. Records are matched by this field's value during import. Note: `updateCompare` fields **always** query the server — they bypass the in-memory cache. |
| `primaryKey` | bool | No | Mark this field as the primary key. Typically set on the entity's ID field (e.g. `accountid`). |
| `lookupType` | string | No | Target entity logical name for lookup/customer/owner fields (e.g. `contact`, `systemuser`). |
| `customfield` | bool | No | Whether the field is a custom field (publisher-prefixed). Informational. |
| `dateMode` | `absolute` \| `relative` \| `relativeDaily` | No | Per-field date handling override. Overrides the global `dateMode` on `<entities>`. |

### Filter element: `<filter>`

An optional FetchXML filter to limit which records are exported. The filter content is placed inside the `<filter>` element as a CDATA section or raw XML.

```xml
<entity name="account" displayname="Account" etc="1"
        primaryidfield="accountid" primarynamefield="name"
        disableplugins="true">
  <fields>
    <!-- ... -->
  </fields>
  <filter>
    <fetch>
      <entity name="account">
        <filter>
          <condition attribute="statecode" operator="eq" value="0" />
        </filter>
      </entity>
    </fetch>
  </filter>
</entity>
```

### Relationship element: `<relationship>`

Relationships are nested inside a `<relationships>` collection on each entity. They describe both many-to-one (lookup) and many-to-many relationships.

```xml
<relationships>
  <!-- Many-to-one (lookup) -->
  <relationship name="account_primary_contact"
                manyToMany="false"
                relatedEntityName="contact"
                referencedAttribute="contactid"
                referencedEntity="contact"
                referencingAttribute="primarycontactid"
                referencingEntity="account" />

  <!-- Many-to-many -->
  <relationship name="accountleads_association"
                manyToMany="true"
                m2mTargetEntity="lead"
                m2mTargetEntityPrimaryKey="leadid"
                isreflexive="false">
    <fields>
      <field name="accountid" type="guid" primaryKey="true" />
      <field name="leadid" type="guid" />
    </fields>
  </relationship>
</relationships>
```

**Relationship attributes:**

| Attribute | Type | Description |
|---|---|---|
| `name` | string | Relationship schema name. |
| `manyToMany` | bool | `true` for N:N relationships, `false` for N:1 lookups. |
| `isreflexive` | bool | `true` if both sides of the M2M reference the same entity (e.g. `account ↔ account`). |
| `relatedEntityName` | string | Related entity logical name (N:1 relationships). |
| `referencedAttribute` | string | Primary key of the referenced (parent) entity. |
| `referencedEntity` | string | Referenced (parent) entity logical name. |
| `referencingAttribute` | string | Lookup field on the referencing (child) entity. |
| `referencingEntity` | string | Referencing (child) entity logical name. |
| `m2mTargetEntity` | string | Target entity for M2M relationships. |
| `m2mTargetEntityPrimaryKey` | string | Primary key field of the M2M target entity. |

### Complete Example

```xml
<?xml version="1.0" encoding="utf-8"?>
<entities dateMode="absolute">
  <entityImportOrder>
    <entityName>transactioncurrency</entityName>
    <entityName>businessunit</entityName>
    <entityName>account</entityName>
    <entityName>contact</entityName>
  </entityImportOrder>

  <entity name="transactioncurrency" displayname="Currency" etc="9105"
          primaryidfield="transactioncurrencyid" primarynamefield="currencyname"
          disableplugins="true">
    <fields>
      <field name="transactioncurrencyid" displayname="Currency" type="guid"
             primaryKey="true" />
      <field name="currencyname" displayname="Currency Name" type="string" />
      <field name="isocurrencycode" displayname="Currency Code" type="string"
             updateCompare="true" />
      <field name="currencysymbol" displayname="Currency Symbol" type="string" />
      <field name="exchangerate" displayname="Exchange Rate" type="decimal" />
    </fields>
  </entity>

  <entity name="account" displayname="Account" etc="1"
          primaryidfield="accountid" primarynamefield="name"
          disableplugins="true">
    <fields>
      <field name="accountid" displayname="Account" type="guid"
             primaryKey="true" />
      <field name="name" displayname="Account Name" type="string" />
      <field name="accountnumber" displayname="Account Number" type="string" />
      <field name="primarycontactid" displayname="Primary Contact" type="entityreference"
             lookupType="contact" />
      <field name="transactioncurrencyid" displayname="Currency" type="entityreference"
             lookupType="transactioncurrency" />
      <field name="statecode" displayname="Status" type="state" />
      <field name="statuscode" displayname="Status Reason" type="status" />
      <field name="revenue" displayname="Annual Revenue" type="money" />
      <field name="numberofemployees" displayname="Number of Employees"
             type="number" />
      <field name="cr4c2_externalid" displayname="External ID" type="string"
             updateCompare="true" customfield="true" />
    </fields>
    <filter>
      <fetch>
        <entity name="account">
          <filter>
            <condition attribute="statecode" operator="eq" value="0" />
          </filter>
        </entity>
      </fetch>
    </filter>
    <relationships>
      <relationship name="account_primary_contact" manyToMany="false"
                    relatedEntityName="contact"
                    referencedAttribute="contactid"
                    referencedEntity="contact"
                    referencingAttribute="primarycontactid"
                    referencingEntity="account" />
    </relationships>
  </entity>

  <entity name="contact" displayname="Contact" etc="2"
          primaryidfield="contactid" primarynamefield="fullname"
          disableplugins="true" skipupdate="true">
    <fields>
      <field name="contactid" displayname="Contact" type="guid"
             primaryKey="true" />
      <field name="fullname" displayname="Full Name" type="string" />
      <field name="firstname" displayname="First Name" type="string" />
      <field name="lastname" displayname="Last Name" type="string" />
      <field name="emailaddress1" displayname="Email" type="string" />
      <field name="parentcustomerid" displayname="Company Name" type="entityreference"
             lookupType="account" />
    </fields>
  </entity>
</entities>
```

---

## 5. Data File Reference (`data.xml`)

The `data.xml` file contains the actual records exported by CMT. It is generated automatically during export — you normally don't write it by hand, but understanding its structure is useful for debugging and for the `convert` command.

### Root element: `<entities>`

```xml
<entities xmlns:xsd="http://www.w3.org/2001/XMLSchema"
          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          timestamp="2024-06-15T14:30:00.0000000Z">
  <entity name="account" displayname="Account">
    <records>
      <!-- records here -->
    </records>
    <m2mrelationships>
      <!-- M2M associations here -->
    </m2mrelationships>
  </entity>
</entities>
```

The `timestamp` attribute on the root records when the export was performed. It is used by the `relative` and `relativeDaily` date modes to compute the time shift during import.

### Record element: `<record>`

Each record has an `id` attribute containing the record's primary key GUID.

```xml
<record id="a1b2c3d4-e5f6-7890-abcd-ef1234567890">
  <field name="accountid" value="a1b2c3d4-e5f6-7890-abcd-ef1234567890" />
  <field name="name" value="Contoso Ltd" />
  <field name="revenue" value="1500000.00" />
  <field name="statecode" value="0" />
  <field name="statuscode" value="1" />
  <field name="primarycontactid"
         value="11111111-2222-3333-4444-555555555555"
         lookupentity="contact"
         lookupentityname="John Smith" />
</record>
```

### Field element attributes

| Attribute | Description |
|---|---|
| `name` | Field logical name (matches the schema). |
| `value` | The field value, serialized as a string. Format depends on type — see [Supported Data Types](#6-supported-data-types). |
| `lookupentity` | *(Lookups only)* Logical name of the target entity for the lookup. |
| `lookupentityname` | *(Lookups only)* Display name of the referenced record. Used for logging and as a dedup fallback. |
| `filename` | *(File columns only)* Original file name of the attached file. |

### Many-to-many relationships: `<m2mrelationships>`

```xml
<m2mrelationships>
  <m2mrelationship m2mrelationshipname="accountleads_association"
                   sourceid="a1b2c3d4-e5f6-7890-abcd-ef1234567890">
    <targetids>
      <targetid>aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee</targetid>
      <targetid>ffffffff-0000-1111-2222-333333333333</targetid>
    </targetids>
  </m2mrelationship>
</m2mrelationships>
```

Each `<m2mrelationship>` groups all target records associated with a single source record through a specific N:N relationship.

### Complete Example

```xml
<?xml version="1.0" encoding="utf-8"?>
<entities xmlns:xsd="http://www.w3.org/2001/XMLSchema"
          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          timestamp="2024-06-15T14:30:00.0000000Z">

  <entity name="transactioncurrency" displayname="Currency">
    <records>
      <record id="b1d0f273-4f56-ec11-8c62-000d3a8c5dab">
        <field name="transactioncurrencyid"
               value="b1d0f273-4f56-ec11-8c62-000d3a8c5dab" />
        <field name="currencyname" value="US Dollar" />
        <field name="isocurrencycode" value="USD" />
        <field name="currencysymbol" value="$" />
        <field name="exchangerate" value="1.0000000000" />
      </record>
    </records>
    <m2mrelationships />
  </entity>

  <entity name="account" displayname="Account">
    <records>
      <record id="a1b2c3d4-e5f6-7890-abcd-ef1234567890">
        <field name="accountid"
               value="a1b2c3d4-e5f6-7890-abcd-ef1234567890" />
        <field name="name" value="Contoso Ltd" />
        <field name="accountnumber" value="ACC-001" />
        <field name="revenue" value="1500000.00" />
        <field name="numberofemployees" value="250" />
        <field name="primarycontactid"
               value="11111111-2222-3333-4444-555555555555"
               lookupentity="contact"
               lookupentityname="John Smith" />
        <field name="transactioncurrencyid"
               value="b1d0f273-4f56-ec11-8c62-000d3a8c5dab"
               lookupentity="transactioncurrency"
               lookupentityname="US Dollar" />
        <field name="statecode" value="0" />
        <field name="statuscode" value="1" />
      </record>
    </records>
    <m2mrelationships>
      <m2mrelationship m2mrelationshipname="accountleads_association"
                       sourceid="a1b2c3d4-e5f6-7890-abcd-ef1234567890">
        <targetids>
          <targetid>aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee</targetid>
        </targetids>
      </m2mrelationship>
    </m2mrelationships>
  </entity>

  <entity name="contact" displayname="Contact">
    <records>
      <record id="11111111-2222-3333-4444-555555555555">
        <field name="contactid"
               value="11111111-2222-3333-4444-555555555555" />
        <field name="fullname" value="John Smith" />
        <field name="firstname" value="John" />
        <field name="lastname" value="Smith" />
        <field name="emailaddress1" value="john.smith@contoso.com" />
        <field name="parentcustomerid"
               value="a1b2c3d4-e5f6-7890-abcd-ef1234567890"
               lookupentity="account"
               lookupentityname="Contoso Ltd" />
      </record>
    </records>
    <m2mrelationships />
  </entity>
</entities>
```

---

## 6. Supported Data Types

The `type` attribute on `<field>` in the schema determines how CMT serializes and deserializes each value.

| Schema Type | Description | XML Value Format | Example |
|---|---|---|---|
| `string` | Single-line or multi-line text | Raw text | `value="Hello World"` |
| `number` | Whole number (int32) | Integer string | `value="42"` |
| `datetime` | Date and time | ISO 8601 (UTC) | `value="2024-01-15T10:30:00.0000000Z"` |
| `decimal` | Decimal number (high precision) | Decimal string | `value="123.456789"` |
| `float` | Floating-point number | Float string | `value="50.0755"` |
| `money` | Currency amount | Decimal string | `value="1234567.89"` |
| `bool` | Yes/No (two-option) | `True` / `False` | `value="True"` |
| `guid` | Unique identifier | GUID string | `value="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"` |
| `optionsetvalue` | Choice (single-select) | Integer (option value) | `value="1"` |
| `lookup` | Lookup reference | GUID + lookup attributes | `value="<guid>" lookupentity="account" lookupentityname="Contoso"` |
| `customer` | Customer lookup (polymorphic) | Same as `lookup` | `value="<guid>" lookupentity="account" lookupentityname="Contoso"` |
| `owner` | Owner lookup (polymorphic) | Same as `lookup` | `value="<guid>" lookupentity="systemuser" lookupentityname="Admin"` |
| `entityreference` | Entity reference | Same as `lookup` | Same as `lookup` |
| `state` | State code (Active/Inactive) | Integer via Raw | `value="0"` |
| `status` | Status reason code | Integer via Raw | `value="1"` |
| `optionsetvaluecollection` | Choices (multi-select) | Sentinel-wrapped array | `value="[-1,100000000,100000001,-1]"` |
| `partylist` | Activity party list | Nested XML | Nested `activitypointerrecords` element |
| `imagedata` | Image column data | Base64-encoded | `value="iVBORw0KGgo..."` |
| `filedata` | File column | GUID + filename | `value="<guid>" filename="invoice.pdf"` |

### Notes on special types

**`optionsetvaluecollection` (multi-select choices):** Values are wrapped with `-1` sentinels at the start and end of the array. For example, a multi-select field with values `100000000` and `100000001` selected is serialized as `[-1,100000000,100000001,-1]`.

**`partylist` (activity parties):** Activity parties are serialized as nested `activitypointerrecords` XML elements rather than simple values. Each party entry contains the party's entity reference.

**`state` / `status`:** These are applied in a **second pass** after all records are created/updated. This ensures that records aren't accidentally deactivated before their lookups are resolved.

**`lookup` / `customer` / `owner`:** All lookup-like types share the same serialization. The `lookupentity` attribute identifies the target entity, and `lookupentityname` provides the display name for logging and dedup fallback.

---

## 7. File Column Support

CMT supports exporting and importing **file columns** (`filedata`) and **image columns** (`imagedata`) — binary data attached to Dataverse records.

### Export

Use the `--export-files` flag to include file columns in the export:

```bash
txc data package export \
  --schema data_schema.xml \
  --output data.zip \
  --export-files \
  --profile myprofile
```

During export:
1. CMT identifies fields with type `filedata` in the schema.
2. For each record that has a file attached, CMT downloads the file binary using the Dataverse chunked download API.
3. Files are stored in a `files/` directory inside the zip, named by their GUID (e.g. `files/{guid}.bin`).
4. The `data.xml` records the file's GUID in the `value` attribute and the original filename in the `filename` attribute.

### Zip structure

```
export.zip
├── data.xml              # Record data
├── data_schema.xml       # Schema definition (copy of input)
└── files/                # Binary files (only when --export-files is used)
    ├── {guid1}.bin
    ├── {guid2}.bin
    └── ...
```

### Import

File columns are imported automatically when the data package contains a `files/` directory:

1. CMT reads the `filedata` fields from each record.
2. For each file reference, it locates the corresponding `{guid}.bin` in the `files/` directory.
3. The file binary is uploaded to the target environment using the Dataverse chunked upload API.

### Schema configuration

To include a file column in migration, add it to your schema with `type="filedata"`:

```xml
<field name="cr4c2_attachment" displayname="Attachment" type="filedata"
       customfield="true" />
```

For image columns, use `type="imagedata"`:

```xml
<field name="entityimage" displayname="Entity Image" type="imagedata" />
```

> **Note:** Image columns are serialized as base64-encoded strings directly in `data.xml` (no separate files). File columns use the `files/` directory approach because they can be much larger.

---

## 8. Import Tuning Options

The import command exposes several options that control parallelism, batching, and duplicate detection. Understanding what these do internally helps you pick the right values.

### `--connection-count N`

**Default:** `1`

Opens **N** parallel `CrmServiceClient` connections to the target environment. During the preprocessing phase, records are distributed round-robin across the available connections. Each connection authenticates separately.

**When to increase:** Large imports (thousands of records). Typical sweet spot is 2–8 connections. Going beyond 8 rarely helps and may trigger throttling.

```bash
txc data package import data.zip --connection-count 4
```

### `--batch-mode`

**Default:** `false` (records sent one-by-one)

When enabled, records are sent in batches using `ExecuteMultiple` requests instead of individual `Create` / `Update` calls. If the target organization is version **9.2.23083 or higher**, CMT automatically upgrades to `UpsertMultiple` for even better throughput.

**When to use:** Any import with more than a few hundred records. The performance difference is dramatic — a 10,000-record import that takes 30 minutes one-by-one can complete in 2–3 minutes with batch mode.

```bash
txc data package import data.zip --batch-mode
```

### `--batch-size N`

**Default:** `600`

Controls how many records are included in each `ExecuteMultiple` / `UpsertMultiple` request. Only applies when `--batch-mode` is enabled.

**Trade-offs:**
- **Higher values** (500–1000): Fewer HTTP round-trips, faster overall. Risk of hitting the 16 MB request-size limit or longer per-request timeouts.
- **Lower values** (50–200): More resilient to transient errors (fewer records to retry), but more HTTP round-trips.

```bash
txc data package import data.zip --batch-mode --batch-size 200
```

### `--override-safety-checks`

**Default:** `false`

Sets `OverrideDataImportSafetyChecks=true` in the CMT engine. This **skips `CheckAndRetrieveRecordId` entirely** — no duplicate detection is performed, and every record is created as a new record regardless of whether it already exists.

> ⚠️ **DANGEROUS:** This will create duplicate records if the target environment already contains matching data. Use **only** when importing into a guaranteed-empty environment (e.g. freshly provisioned dev/test).

```bash
txc data package import data.zip --override-safety-checks
```

### `--prefetch-limit N`

**Default:** `4000`

Controls the `PrefetchRecordLimitSize` — the maximum number of existing records CMT will preload into an in-memory cache (the `EntityReaderCache`) per entity for duplicate detection.

**How it works:**
- If an entity in the target has **≤ N** records, CMT loads all of them into memory and performs dedup lookups against the cache (fast).
- If an entity has **> N** records, CMT skips the cache entirely and checks each incoming record individually via an API call (much slower, but uses less memory).

**When to increase:** If you're importing into an entity that has 5,000–50,000 existing records and dedup performance is slow, raise this limit. Keep in mind that higher values use more memory.

```bash
txc data package import data.zip --prefetch-limit 10000
```

---

## 9. How Deduplication Works (Technical Deep-Dive)

Understanding CMT's dedup pipeline helps you troubleshoot unexpected creates/updates and tune import performance.

### Preprocessing phase

Before any records are written to Dataverse, CMT runs a preprocessing pipeline:

#### Step 1: `CreatePreParseWorkingData`

For each entity in the data package, CMT loads existing records from the target environment into an in-memory `EntityReaderCache`.

- If the entity has **≤ `--prefetch-limit`** records, all are loaded into the cache.
- If the entity exceeds the limit, the cache is skipped — every dedup check will be an API call.

#### Step 2: `CheckAndRetrieveRecordId`

For each incoming record, CMT performs **tiered matching** to determine whether the record already exists:

1. **Match by primary ID** — CMT checks if the record's `id` (GUID) already exists in the target. This is the fastest and most reliable match.

2. **Fallback to primary name field** — If the ID doesn't match, CMT tries to find a record with the same value in the `primarynamefield` (e.g. `name` for accounts, `fullname` for contacts). This is a fuzzy fallback and can produce false positives if names aren't unique.

3. **Match by `updateCompare` custom keys** — If any fields have `updateCompare="true"` in the schema, CMT queries the target for a record matching those field values. **Important:** `updateCompare` lookups **always** go through the API — they never use the in-memory cache, regardless of the prefetch limit. This makes them slower but more precise.

If a match is found, the incoming record is flagged for **update**. If no match is found, it's flagged for **create**.

#### The `ForceNew` flag

During dedup, if a record's comparison key references a lookup that can't be resolved (e.g. the parent account doesn't exist yet), CMT sets `ForceNew = true` on that record. This forces the record to be **created** on the first pass, and the unresolved lookup is deferred to the second pass.

### Two-pass import

CMT imports data in two passes:

**First pass — Create and Update:**
- Records flagged for create are inserted.
- Records flagged for update are patched.
- Lookups that can't be resolved yet (because the target record hasn't been imported) are **deferred**.

**Second pass — Deferred lookups and state changes:**
- Deferred lookup fields are resolved and updated now that all records exist.
- `state` and `status` fields are applied. These are intentionally deferred because deactivating a record too early could block updates to its child records.

### Entity import order

The `<entityImportOrder>` in the schema controls the sequence. For best results, order entities so that referenced (parent) entities come before referencing (child) entities:

```
transactioncurrency → businessunit → account → contact → opportunity
```

---

## 10. Date Handling Modes

CMT supports three modes for handling date/time values during import. This is useful when migrating demo or sample data where you want dates to appear "recent" rather than stale.

| Mode | Behavior |
|---|---|
| `absolute` | Dates are imported **exactly as-is** from the data file. No transformation. This is the default. |
| `relative` | Dates are **shifted forward** by the difference between the export timestamp and the current import time. A record exported with `createdon = 2024-01-01` and imported 6 months later becomes `createdon = 2024-07-01`. |
| `relativeDaily` | Similar to `relative`, but the shift is rounded to preserve the **time of day**. Useful for demo scenarios where you want appointments to remain at their original times. |

### Setting date mode globally

```xml
<entities dateMode="relative">
  <!-- all datetime fields in all entities will use relative mode -->
</entities>
```

### Setting date mode per field

```xml
<field name="createdon" displayname="Created On" type="datetime"
       dateMode="relative" />
<field name="scheduledstart" displayname="Start Time" type="datetime"
       dateMode="relativeDaily" />
```

Per-field settings **override** the global setting.

### How the shift is calculated

The export timestamp is stored in the `timestamp` attribute of the `<entities>` root element in `data.xml`:

```xml
<entities timestamp="2024-01-15T10:30:00.0000000Z">
```

**Relative mode shift:**
```
shift = now - export_timestamp
new_date = original_date + shift
```

**RelativeDaily mode shift:**
```
shift = floor((now - export_timestamp) / 1 day) * 1 day
new_date = original_date + shift
```

This preserves the hour/minute/second component while shifting the date.

> **Microsoft docs:** [Configure date settings for demo data](https://learn.microsoft.com/en-us/power-platform/admin/configure-date-settings-for-demo-data)

---

## 11. Known Limitations and Gotchas

### `deleteBeforeAdd` is dead code

The CMT API accepts a `deleteBeforeAdd` parameter that is supposed to delete all existing records before importing. **This parameter exists in the code but is never actually executed** — the delete logic is unreachable. Do not rely on it. If you need a clean slate, truncate the target entity manually before import.

### Image columns work despite documentation

Microsoft's official documentation states that "Image column migration is not supported." However, decompilation of the CMT engine reveals that `imagedata` fields **are** handled — they are serialized as base64-encoded strings using the Raw data type and imported via standard attribute updates. Image migration works in practice.

### `Calendar` entity is not supported

The `Calendar` entity is explicitly excluded from CMT migration. Attempting to include it in your schema will result in an error. This is a confirmed limitation from Microsoft.

### `role` entity is force-excluded from import

The `role` (Security Role) entity is force-excluded during import. You can export security role data, but the import engine will silently skip it. Use solution import for security role migration instead.

### `updateCompare` bypasses the cache

Fields marked with `updateCompare="true"` **always** perform a live API query to find matching records. They never use the in-memory `EntityReaderCache`, regardless of the `--prefetch-limit` setting. This means:

- If you have many records with `updateCompare` keys, dedup will be significantly slower.
- The benefit is precision — the API query always reflects the current state of the target.

### Schema validation failures

If entity or field metadata differs between the source and target environments (e.g. a custom field exists in source but not in target), CMT will throw a schema validation error. Ensure your target environment has all the entities and fields referenced in the schema before importing.

### Lookup resolution order matters

If entity A has a lookup to entity B, but entity B is imported *after* entity A, the lookup will fail on the first pass and be deferred to the second pass. While CMT handles this gracefully, it's slower. Use `<entityImportOrder>` to minimize deferred lookups.

### Multi-select option set sentinel values

The `[-1,...,-1]` sentinel wrapping in `optionsetvaluecollection` values is required. If you hand-edit data.xml and omit the sentinels, the import will fail or produce incorrect values.

### JSON `data record create` does not auto-coerce complex types

JSON `data record create` does not auto-coerce integer values to `OptionSetValue` or `Money` types. For choice columns, the Dataverse API may reject plain integers. **Workaround:** use the CMT import for complex type handling, or ensure your data includes properly formatted values.

### Connection timeout

Connection timeout is 2 minutes. Long-running schema operations (entity creation) may approach this limit on heavily loaded environments.

---

## 12. Troubleshooting

### Enable verbose logging

Use `--verbose` to get full CMT trace output, including per-record dedup decisions, API calls, and error details:

```bash
txc data package import data.zip --verbose --profile myprofile
```

### Common errors

| Error | Cause | Fix |
|---|---|---|
| **Schema validation failed** | Entity or field in schema doesn't exist in target | Verify target environment has all entities/fields. Check for typos in logical names. |
| **Duplicate key error** | Record with same primary key already exists | Use dedup (`updateCompare`) or `--override-safety-checks` if target is empty. |
| **Lookup resolution failed** | Referenced record doesn't exist in target | Check `<entityImportOrder>` — parent entities must come first. Verify the referenced record was exported. |
| **Interactive authentication required** | Token expired or not cached | Run `txc config auth login --profile <name>` and retry. |
| **Request size exceeded** | Batch too large (>16 MB) | Reduce `--batch-size` (try 100–200). |
| **Throttling / 429 errors** | Too many parallel requests | Reduce `--connection-count`. Add retry logic in pipeline. |
| **File not found in package** | `files/{guid}.bin` missing from zip | Re-export with `--export-files`. Ensure the zip wasn't modified after export. |

### Authentication

CMT commands require an authenticated connection to Dataverse. Before running export or import:

```bash
# Login and cache credentials for a profile
txc config auth login --profile myprofile

# Verify the profile is working
txc config auth status --profile myprofile
```

If you get an "Interactive authentication required" error, your cached token has expired. Re-run `txc config auth login`.

### Debugging dedup issues

If records are being created when you expect updates (or vice versa):

1. Run with `--verbose` to see which dedup tier matched each record.
2. Verify `primaryidfield` and `primarynamefield` in your schema match the target entity.
3. If using `updateCompare`, confirm the field values in `data.xml` exactly match existing records in the target.
4. Check that the `--prefetch-limit` is high enough — if the target entity exceeds the limit, dedup falls back to API calls which may behave differently.

### Performance tuning checklist

For large imports (10,000+ records):

1. ✅ Enable `--batch-mode`
2. ✅ Set `--connection-count` to 4–8
3. ✅ Set `--batch-size` to 200–600
4. ✅ Increase `--prefetch-limit` if target entities are large
5. ✅ Order entities in `<entityImportOrder>` to minimize deferred lookups
6. ✅ Use `disableplugins="true"` on entities where safe to do so
7. ✅ Use `--override-safety-checks` only if importing into an empty environment
