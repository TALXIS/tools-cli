# HAR Analysis Report: make.powerapps.com Solution Management API Calls

**Source**: `makesolutionops.har` (129 MB, 847 total entries)
**Filtered**: 338 Dataverse API calls (excluding CORS OPTIONS preflight)
**Environment**: `org2928f636.crm.dynamics.com`
**Observed entities**: solution `Model` (`c122871d-cce4-f011-8406-7ced8d3c75a7`), solution `Basic` (`25a01723-9f63-4449-a3e0-046cc23a2902`), table `fin_mytable` (`f84a27f8-e0c9-f011-8543-002248028cf4`)

---

## 1. Solution Layer Queries (4 calls)

### 1a. Get all layers for a component
```
GET /api/data/v9.0/msdyn_componentlayers
  ?$filter=(msdyn_componentid eq '{componentId}' and msdyn_solutioncomponentname eq 'Entity')
  Prefer: odata.include-annotations=*
```

**Response** — Array of layer objects:
| Field | Description |
|---|---|
| `msdyn_solutionname` | Layer name (e.g., "Active", "Model") |
| `msdyn_publishername` | Publisher of the layer |
| `msdyn_order` | Layer order (higher = wins) |
| `msdyn_componentid` | Component GUID |
| `msdyn_solutioncomponentname` | Component type string (e.g., "Entity") |
| `msdyn_changes` | **JSON string** containing full component attribute diff (key-value pairs) |
| `msdyn_componentjson` | Full component definition JSON |
| `msdyn_children` | Child component layers |
| `msdyn_overwritetime` | When the layer was written |

### 1b. Get Active layer only (for "Remove Active Customizations" check)
```
GET /api/data/v9.1/msdyn_componentlayers
  ?$filter=(msdyn_componentid eq '{componentId}'
    and msdyn_solutioncomponentname eq 'Entity'
    and msdyn_solutionname eq 'Active')
```

**CLI relevance**: ⭐⭐⭐ Core for `txc solution layers` command. Useful for showing what solutions customize a given component and diffing active vs managed layers.

---

## 2. Solution Component Summaries (71 calls) — `msdyn_solutioncomponentsummaries`

The portal's **primary** way to list what's inside a solution. Virtual entity, not a real table.

### 2a. List components in a solution by type
```
GET /api/data/v9.0/msdyn_solutioncomponentsummaries
  ?$filter=(msdyn_solutionid eq {solutionId}) and ((msdyn_componenttype eq {typeCode}))
  &api-version=9.1
  Prefer: odata.maxpagesize=5000
```

### 2b. List components filtered by parent entity
```
GET /api/data/v9.0/msdyn_solutioncomponentsummaries
  ?$filter=(msdyn_solutionid eq {solutionId})
    and ((msdyn_componenttype eq 10))          -- e.g., Entity Relationships
    and msdyn_primaryentityname eq 'fin_mytable'
  &api-version=9.1
```

### 2c. Check if a specific component exists in solution
```
GET /api/data/v9.0/msdyn_solutioncomponentsummaries
  ?$filter=(msdyn_solutionid eq {solutionId})
    and ((msdyn_componenttype eq 1))
    and msdyn_objectid eq '{componentId}'
  &api-version=9.1
```

### 2d. Find by component name
```
GET /api/data/v9.0/msdyn_solutioncomponentsummaries
  ?$filter=(msdyn_solutionid eq {solutionId})
    and ((msdyn_componenttype eq 1))
    and msdyn_name eq 'fin_mytable'
  &api-version=9.1
```

### 2e. List all available components (no solution filter, for "Add existing" dialog)
```
GET /api/data/v9.0/msdyn_solutioncomponentsummaries
  ?$filter=((msdyn_componenttype eq 1))
  &api-version=9.1
  Prefer: odata.maxpagesize=5000
```
Returns **ALL** entities in the org (713 items observed).

### 2f. AI models / settings in Default solution
```
GET /api/data/v9.0/msdyn_solutioncomponentsummaries
  ?$filter=(msdyn_solutionid eq {defaultSolutionId}
    and (msdyn_componentlogicalname eq 'settingdefinition'
      or msdyn_componentlogicalname eq 'organizationsetting'))
  &$select=msdyn_name,msdyn_uniquename,msdyn_componentlogicalname
  Prefer: odata.include-annotations=*
```

### Component types observed in queries

| Type Code | Name | Logical Name |
|---|---|---|
| 1 | Entity | `entity` |
| 2 | Attribute | `attribute` |
| 9 | OptionSet | `optionset` |
| 10 | Entity Relationship | `entityrelationship` |
| 14 | Entity Key | `entitykey` |
| 22 | Display String / Localization | `displaystring` |
| 26 | View (SavedQuery) | `savedquery` |
| 29 | Process/Workflow | `workflow` |
| 59 | Chart | `savedqueryvisualization` |
| 60 | Form (SystemForm) | `systemform` |
| 80 | Model-Driven App | - |
| N/A | App Action | `appaction` |
| N/A | AI Model | `msdyn_aimodel` |
| N/A | Setting Definition | `settingdefinition` |
| N/A | Organization Setting | `organizationsetting` |

### Response key fields
`msdyn_name`, `msdyn_displayname`, `msdyn_objectid`, `msdyn_componenttype`, `msdyn_iscustomizable`, `msdyn_ismanaged`, `msdyn_iscustom`, `msdyn_primaryentityname`, `msdyn_owner`, `msdyn_createdon`, `msdyn_solutioncomponentsummaryid`, `msdyn_uniquename`, `msdyn_logicalcollectionname`, `msdyn_isappaware`, `msdyn_synctoexternalsearchindex`, `msdyn_fieldsecurity`, `msdyn_sdkmessagename`, `msdyn_executionstage`, `msdyn_executionorder`, `msdyn_isolationmode`, `msdyn_deployment`, `msdyn_workflowcategory`, `msdyn_subtype`

### Additional subtype filters observed
- Workflow category 2 = Business Rules: `msdyn_componenttype eq 29 and msdyn_workflowcategory eq '2'`
- Views excluding subtype 1024: `msdyn_componenttype eq 26 and msdyn_subtype ne '1024'`
- Forms with subtype 10 filter: `msdyn_componenttype eq 60 and msdyn_subtype eq '10'`
- Localization with LCID: `msdyn_componenttype eq 22 ... and msdyn_lcid eq 1033`

**CLI relevance**: ⭐⭐⭐ Essential for `txc solution list-components`. This is the single best API for listing solution contents with filtering.

---

## 3. Solution Component Count Summaries (11 calls) — `msdyn_solutioncomponentcountsummaries`

Quick count of how many components per type exist in a solution:

```
GET /api/data/v9.0/msdyn_solutioncomponentcountsummaries
  ?$filter=msdyn_solutionid eq {solutionId}
  Prefer: odata.include-annotations=*
```

**Response** — Array of count objects:
```json
{
  "msdyn_componentlogicalname": "optionset",
  "msdyn_componenttype": 9,
  "msdyn_total": 237,
  "msdyn_subtype": null,
  "msdyn_primaryentityname": null,
  "msdyn_workflowcategory": null
}
```

**CLI relevance**: ⭐⭐ Good for `txc solution info` overview showing component counts per type.

---

## 4. Solution Components — `solutioncomponents` (6 calls)

The **real table** (not virtual entity). Used to check if a specific object exists as a component in a solution:

```
GET /api/data/v9.0/solutioncomponents
  ?$filter=objectid eq {objectId} and _solutionid_value eq {solutionId}
```

**Response key fields**: `solutioncomponentid`, `componenttype`, `objectid`, `_solutionid_value`, `rootcomponentbehavior`, `rootsolutioncomponentid`, `ismetadata`, `createdon`

**CLI relevance**: ⭐⭐ Use when you need the actual `solutioncomponentid` (e.g., for `RemoveSolutionComponent`).

---

## 5. Dependency Operations (7 calls)

Four distinct dependency functions observed, all using the `WithMetadata` variant:

### 5a. RetrieveDependentComponentsWithMetadata — "What depends on this?"
```
GET /api/data/v9.0/RetrieveDependentComponentsWithMetadata(
  ObjectId={componentId},ComponentType={typeCode})
```

### 5b. RetrieveRequiredComponentsWithMetadata — "What does this require?"
```
GET /api/data/v9.0/RetrieveRequiredComponentsWithMetadata(
  ObjectId={componentId},ComponentType={typeCode})
```

### 5c. RetrieveDependenciesForDeleteWithMetadata — "Can I delete this safely?"
```
GET /api/data/v9.0/RetrieveDependenciesForDeleteWithMetadata(
  ObjectId={componentId},ComponentType={typeCode})
```

### 5d. RetrieveDependenciesForUninstallWithMetadata — "Can I uninstall this solution?"
```
GET /api/data/v9.0/RetrieveDependenciesForUninstallWithMetadata(
  SolutionId={solutionId})
```

### Dependency response structure
All return `DependencyMetadataCollection.DependencyMetadataInfoCollection[]`:

```json
{
  "requiredcomponentobjectid": "f84a27f8-...",
  "requiredcomponentdisplayname": "MyTable",
  "requiredcomponenttype": 1,
  "requiredcomponentname": "fin_MyTable",
  "requiredcomponenttypename": "Entity",
  "requiredcomponentbasesolutionid": "fd140aae-...",
  "requiredcomponentbasesolutionname": "Active Solution",
  "requiredcomponentbasesolutionuniquename": "Active",
  "requiredcomponentbasesolutionversion": "1.0",
  "requiredcomponentparentid": "00000000-...",
  "requiredcomponentparentdisplayname": null,
  "dependentcomponentobjectid": "fe80a82f-...",
  "dependentcomponentdisplayname": "fin_mytable",
  "dependentcomponenttype": 10332,
  "dependentcomponentname": "fin_mytable",
  "dependentcomponenttypename": "Restore Deleted Records Configuration",
  "dependentcomponentbasesolutionid": "25a01723-...",
  "dependentcomponentbasesolutionname": "Basic Solution",
  "dependentcomponentbasesolutionuniquename": "Basic",
  "dependentcomponentbasesolutionversion": "1.0",
  "dependencyid": "7f050505-...",
  "dependencytype": 2,
  "dependencytypename": "Published",
  "isdependencyremovalenabled": false,
  "dependentcomponententitysetname": "recyclebinconfigs",
  "dependentcomponententitylogicalname": "recyclebinconfig",
  "requiredcomponententitylogicalname": "entity"
}
```

**Key insight**: The `WithMetadata` variants (not documented in official SDK) return enriched data with display names, solution info, and entity set names — much more useful than the bare `RetrieveDependentComponents`.

**CLI relevance**: ⭐⭐⭐ Critical for `txc solution dependencies`, pre-delete checks, and safe component removal workflows.

---

## 6. Solution CRUD (44 calls)

### 6a. Check solution existence by unique name
```
GET /api/data/v9.0/solutions
  ?$filter=uniquename eq 'msdynce_CRMExtensions'
  Prefer: odata.maxpagesize=5000
```
Called for multiple solutions: `msdynce_CRMExtensions`, `PowerVirtualAgents`, `CRMSettingsAPIs`, `msdyn_SmartDataImportBase`, `MetadataExtension`

### 6b. Get solution with publisher (expand)
```
GET /api/data/v9.0/solutions({solutionId})
  ?$expand=publisherid
```

**Response key fields**: `solutionid`, `uniquename`, `version`, `ismanaged`, `isvisible`, `installedon`, `_publisherid_value`, `publisherid` (expanded object)

### 6c. List solutions with complex visibility filter (for upgrade detection)
```
GET /api/data/v9.0/solutions
  ?$filter=((isvisible eq true)
    and (_parentsolutionid_value ne null
      or (ismanaged eq true and endswith(uniquename, '_Upgrade'))
      or solutionid eq {specificId})
    or uniquename eq 'msdynce_ServiceAnchor'
    or uniquename eq 'msdyn_PowerAppsChecker'
    or ...)
  &$select=solutionid,ismanaged,uniquename,_parentsolutionid_value,version
```

**CLI relevance**: ⭐⭐⭐ The expand+filter patterns are directly reusable.

---

## 7. Entity/Metadata Operations (66 calls)

### 7a. List all entities (for "Add existing" entity picker)
```
GET /api/data/v9.0/EntityDefinitions
  ?$filter=(IsIntersect eq false and IsLogicalEntity eq false
    and PrimaryNameAttribute ne null and PrimaryNameAttribute ne ''
    and ObjectTypeCode gt 0 and ObjectTypeCode ne 4712 ...)
  &$select=MetadataId,IsCustomEntity,SchemaName,IconVectorName,LogicalName,EntitySetName,...
  &api-version=9.1
  Prefer: odata.maxpagesize=5000
```
Returns 732 entities.

### 7b. Get entity metadata by ID
```
GET /api/data/v9.0/EntityDefinitions({MetadataId})
```

### 7c. Get entity with all relationships expanded
```
GET /api/data/v9.0/EntityDefinitions(LogicalName = '{logicalName}')
  ?$expand=Attributes,OneToManyRelationships,ManyToOneRelationships,ManyToManyRelationships,Keys
  &api-version=9.1
```

### 7d. Get typed attributes (for column editors)
```
GET /api/data/v9.0/EntityDefinitions(LogicalName = '{name}')/Attributes/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$expand=OptionSet
GET .../MultiSelectPicklistAttributeMetadata?$expand=OptionSet
GET .../StateAttributeMetadata?$expand=OptionSet
GET .../StatusAttributeMetadata?$expand=OptionSet
GET .../BooleanAttributeMetadata?$expand=OptionSet
GET .../EntityNameAttributeMetadata?$expand=OptionSet
GET .../AttributeMetadata?$filter=IsPrimaryName eq true
```

### 7e. Get entity keys
```
GET /api/data/v9.0/EntityDefinitions({id})/Keys
  ?$select=AsyncJob,MetadataId,EntityLogicalName,DisplayName,LogicalName,IsManaged,IsCustomizable,EntityKeyIndexStatus,KeyAttributes
```

### 7f. **UPDATE entity definition** (PUT with solution context header)
```
PUT /api/data/v9.0/EntityDefinitions({MetadataId})?api-version=9.1

Headers:
  mscrm.mergelabels: true
  mscrm.solutionuniquename: Model        ← adds to this solution!
  Prefer: return=representation, odata.include-annotations= "*"

Body: { MetadataId, managed properties (IsCustomizable, CanCreateAttributes, etc.) }
```

**Key insight**: The `mscrm.solutionuniquename` header is how the portal adds a component to a solution while updating it. The `mscrm.mergelabels` header prevents overwriting labels not included in the update.

**CLI relevance**: ⭐⭐⭐ The `mscrm.solutionuniquename` and `mscrm.mergelabels` headers are essential for any metadata write operation in solution context.

---

## 8. Forms/Views/Charts (Unpublished) (7 calls)

Portal uses `RetrieveUnpublishedMultiple` to show draft/unpublished versions:

### Views
```
GET /api/data/v9.0/savedqueries/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple()
  ?$filter=(returnedtypecode eq 'fin_mytable'
    and (querytype eq 0 or querytype eq 1 or querytype eq 2 or querytype eq 4 or querytype eq 64))
  &$select=canbedeleted,savedqueryid,description,iscustom,iscustomizable,isdefault,ismanaged,name,querytype,solutionid,statecode
```

### Forms
```
GET /api/data/v9.0/systemforms/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple()
  ?$filter=(objecttypecode eq 'fin_mytable')
    and (type eq 1 or type eq 2 or type eq 4 or type eq 5 or type eq 6 or type eq 7 or type eq 11 or type eq 12)
  &$select=formid,canbedeleted,iscustomizable,name,ismanaged,isdefault,solutionid,type,description,formactivationstate
```

### Charts
```
GET /api/data/v9.0/savedqueryvisualizations/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple()
  ?$filter=(primaryentitytypecode eq 'fin_mytable')
```

**CLI relevance**: ⭐⭐ Useful if implementing form/view listing that includes unpublished drafts.

---

## 9. Batch Operations (6 calls) — `$batch`

Used for **Solution Checker analysis** queries:

```
POST /api/data/v9.0/$batch?api-version=9.1
Content-Type: multipart/mixed; boundary=batch_xxx

--batch_xxx
Content-Type: application/http
Content-Transfer-Encoding: binary

GET msdyn_analysiscomponents?fetchXml=<fetch aggregate="true">
  <entity name="msdyn_analysiscomponent">
    <attribute name="msdyn_componentname" alias="solutionname" groupby="true" />
    <attribute name="msdyn_analysiscomponentid" alias="count" aggregate="count" />
    ...
```

**CLI relevance**: ⭐ Low priority — Solution Checker integration is niche.

---

## 10. Other Interesting Patterns

### 10a. Org Settings check (lockdown detection)
```
POST /api/data/v9.0/GetOrgDbOrgSetting
Body: { "SettingName": "IsLockdownOfUnmanagedCustomizationEnabled" }
```
This setting controls whether unmanaged customizations are locked. Critical for managed-only environments.

### 10b. SDK Message existence check
```
GET /api/data/v9.0/sdkmessages
  ?$filter=Microsoft.Dynamics.CRM.In(PropertyName=@p1,PropertyValues=@p2)
  &$select=name
  &@p1='name'
  &@p2=['FetchDataSourceInfo','InferEntityDefinition','msdyn_CreateEntity']
```
Uses the `Microsoft.Dynamics.CRM.In()` OData function — checks if virtual table features are available.

### 10c. Table data preview (FetchXML via GET)
```
GET /api/data/v9.0/fin_mytables
  ?fetchXml=<fetch mapping="logical" returntotalrecordcount="true" page="1" count="10">...</fetch>
  Prefer: odata.include-annotations="*"
```
The portal passes FetchXML as a query parameter in GET requests.

### 10d. Process triggers for entity
```
GET /api/data/v9.0/processtriggers
  ?$expand=formid($select=name),processid($select=workflowid,name,...)
  &$filter=primaryentitytypecode eq 'fin_mytable'
```

### 10e. AI Copilot skill check
```
GET /api/data/v9.1/aiskillconfigs
  ?$filter=_aimodel_value ne null and aiskill eq 'RowSummary' and _entity_value eq '{metadataId}'
```

---

## Summary: Headers Cheat Sheet

| Header | Value | Purpose |
|---|---|---|
| `Prefer` | `odata.maxpagesize=5000` | Get all results in one page |
| `Prefer` | `odata.include-annotations=*` | Include formatted values, lookup names |
| `Prefer` | `return=representation` | Return updated entity in response |
| `mscrm.solutionuniquename` | `{solutionName}` | **Write operations in solution context** |
| `mscrm.mergelabels` | `true` | Don't overwrite missing labels on update |

---

## Priority Endpoints for `txc` Implementation

| Priority | Operation | Endpoint | Use Case |
|---|---|---|---|
| ⭐⭐⭐ | List solution components | `msdyn_solutioncomponentsummaries` | `txc solution list-components` |
| ⭐⭐⭐ | Component layers | `msdyn_componentlayers` | `txc solution layers` |
| ⭐⭐⭐ | Dependencies | `RetrieveDependentComponentsWithMetadata` | `txc solution dependencies` |
| ⭐⭐⭐ | Dependencies for delete | `RetrieveDependenciesForDeleteWithMetadata` | Pre-removal safety check |
| ⭐⭐⭐ | Dependencies for uninstall | `RetrieveDependenciesForUninstallWithMetadata` | Pre-uninstall safety check |
| ⭐⭐⭐ | Required components | `RetrieveRequiredComponentsWithMetadata` | Show what a component needs |
| ⭐⭐⭐ | Solution with publisher | `solutions({id})?$expand=publisherid` | `txc solution info` |
| ⭐⭐ | Component counts | `msdyn_solutioncomponentcountsummaries` | Quick solution overview |
| ⭐⭐ | Check component in solution | `solutioncomponents?$filter=objectid eq...` | Before add/remove |
| ⭐⭐ | Entity metadata update | `PUT EntityDefinitions + mscrm.solutionuniquename` | Add entity to solution |
| ⭐⭐ | List unpublished forms/views | `RetrieveUnpublishedMultiple()` | Form/view management |
| ⭐ | Org lockdown check | `GetOrgDbOrgSetting` | Environment capability check |
| ⭐ | Solution checker | `$batch` + `msdyn_analysiscomponents` | Solution quality |

---

## Key GUIDs from this session

| GUID | Meaning |
|---|---|
| `fd140aaf-4df4-11dd-bd17-0019b9312238` | Default Solution |
| `fd140aae-4df4-11dd-bd17-0019b9312238` | Active Solution |
| `c122871d-cce4-f011-8406-7ced8d3c75a7` | Solution "Model" (user's unmanaged solution) |
| `25a01723-9f63-4449-a3e0-046cc23a2902` | Solution "Basic" |
| `f84a27f8-e0c9-f011-8543-002248028cf4` | Entity `fin_mytable` MetadataId |
