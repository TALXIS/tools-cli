# Dataverse Web API Test Results

**Environment:** `https://org2928f636.crm.dynamics.com/`
**Profile:** `org2928f636`
**Date:** 2026-04-25
**CLI command pattern:** `dotnet run --project src/TALXIS.CLI -- env data query odata "<path>" -f json`

---

## 1. Solution Queries

### 1a. Get specific solution by uniquename (with publisher expand)

**OData URL:**
```
solutions?$filter=uniquename eq 'Basic'&$expand=publisherid&$select=solutionid,uniquename,friendlyname,version,ismanaged,installedon,description
```

**Status:** ✅ Success

**Response:** Array of solution objects. Key fields on the solution:

| Field | Type | Example |
|---|---|---|
| `solutionid` | GUID | `25a01723-9f63-4449-a3e0-046cc23a2902` |
| `uniquename` | string | `Basic` |
| `friendlyname` | string | `Basic Solution` |
| `version` | string | `1.0` |
| `ismanaged` | bool | `false` |
| `installedon` | datetime | `2025-11-16T04:51:49Z` |
| `description` | string | `Placeholder solution marker for all those components...` |
| `publisherid` | object | Expanded publisher entity (see below) |

**Publisher expanded fields (key ones):**

| Field | Type | Example |
|---|---|---|
| `publisherid` | GUID | `d21aab70-79e7-11dd-8874-00188b01e34f` |
| `uniquename` | string | `MicrosoftCorporation` |
| `friendlyname` | string | `MicrosoftCorporation` |
| `customizationprefix` | string | `""` |
| `customizationoptionvalueprefix` | int | `0` |
| `description` | string | `Microsoft` |
| `isreadonly` | bool | `false` |

> Publisher also contains many address/contact fields (mostly null), plus `createdon`, `modifiedon`, `versionnumber`, `_organizationid_value`, `_createdby_value`, `_modifiedby_value`.

---

### 1b. List visible solutions

**OData URL:**
```
solutions?$filter=isvisible eq true&$select=solutionid,uniquename,friendlyname,version,ismanaged
```

**Status:** ✅ Success

**Response:** Array of solution objects (many results). Key fields:

| Field | Type | Example |
|---|---|---|
| `solutionid` | GUID | `00000001-0000-0000-0001-00000000009b` |
| `uniquename` | string | `Cr6e8c7` |
| `friendlyname` | string | `Common Data Services Default Solution` |
| `version` | string | `1.0.0.0` |
| `ismanaged` | bool | `false` |

> Mix of managed (`true`) and unmanaged (`false`) solutions.

---

## 2. Solution Component Count Summaries

**OData URL:**
```
msdyn_solutioncomponentcountsummaries?$filter=msdyn_solutionid eq 25a01723-9f63-4449-a3e0-046cc23a2902
```

**Status:** ✅ Success

**Response:** Array of count summary objects. Each represents a component type with its count.

| Field | Type | Example |
|---|---|---|
| `msdyn_componentlogicalname` | string | `entity`, `optionset` |
| `msdyn_componenttype` | int | `1` (Entity), `9` (OptionSet) |
| `msdyn_total` | int | `37`, `1` |
| `msdyn_solutionid` | null | Always null in response |
| `msdyn_solutioncomponentcountsummaryid` | null | Always null |
| `msdyn_subtype` | null | Always null |
| `msdyn_name` | null | Always null |
| `msdyn_primaryentityname` | null | Always null |
| `_organizationid_value` | null | Always null |
| `msdyn_workflowcategory` | null | Always null |

**Notes:**
- The filter by `msdyn_solutionid` works but the field itself is null in response rows.
- The useful data is `msdyn_componentlogicalname`, `msdyn_componenttype`, and `msdyn_total`.

---

## 3. Solution Component Summaries (Entity Components)

**OData URL:**
```
msdyn_solutioncomponentsummaries?$filter=(msdyn_solutionid eq 25a01723-9f63-4449-a3e0-046cc23a2902) and ((msdyn_componenttype eq 1))&api-version=9.1
```

**Status:** ✅ Success

**Response:** Array of component summary objects. Very rich schema — key fields:

| Field | Type | Example |
|---|---|---|
| `msdyn_objectid` | GUID | `645527e4-8904-4c8e-a6f5-52876b7880c6` |
| `msdyn_name` | string | `agenticscenario` |
| `msdyn_displayname` | string | `Agentic Scenario` |
| `msdyn_schemaname` | string | `agenticscenario` |
| `msdyn_componenttype` | int | `1` |
| `msdyn_componenttypename` | string | `Entity` |
| `msdyn_componentlogicalname` | string | `entity` |
| `msdyn_solutionid` | GUID | `25a01723-9f63-4449-a3e0-046cc23a2902` |
| `msdyn_ismanaged` | bool | `true` |
| `msdyn_ismanagedname` | string | `Managed` |
| `msdyn_iscustom` | bool | `true` |
| `msdyn_iscustomname` | string | `True` |
| `msdyn_iscustomizable` | bool | `false` |
| `msdyn_iscustomizablename` | string | `False` |
| `msdyn_hasactivecustomization` | bool | `false` |
| `msdyn_objecttypecode` | int | `10746` |
| `msdyn_primaryidattribute` | string | `agenticscenarioid` |
| `msdyn_logicalcollectionname` | string | `agenticscenarios` |
| `msdyn_createdon` | datetime | `2025-12-03T03:34:27Z` |
| `msdyn_modifiedon` | datetime | `2025-12-03T03:34:27Z` |
| `msdyn_subtype` | int | `11`, `15`, etc. |
| `msdyn_total` | int | `37` (total entities in solution) |
| `msdyn_version` | null | null for entities |
| `msdyn_isauditenabled` | bool | `false` |
| `msdyn_isauditenabledname` | string | `Disabled` |
| `msdyn_synctoexternalsearchindex` | string | `False` |
| `msdyn_solutioncomponentsummaryid` | null | Always null |

**Other fields present (mostly empty/null for entities):**
`msdyn_executionorder`, `msdyn_isolationmode`, `msdyn_sdkmessagename`, `msdyn_connectorinternalid`,
`msdyn_deployment`, `msdyn_executionstage`, `msdyn_owner`, `msdyn_fieldsecurity`, `msdyn_typename`,
`msdyn_description`, `msdyn_eventhandler`, `msdyn_statusname`, `msdyn_fieldtype`,
`msdyn_relatedentity`, `msdyn_relatedentityattribute`, `msdyn_standardstatus`, `msdyn_uniquename`,
`msdyn_status`, `msdyn_workflowidunique`, `msdyn_primaryentityname`, `msdyn_culture`,
`msdyn_publickeytoken`, `msdyn_lcid`, `msdyn_owningbusinessunit`, `msdyn_workflowcategory`,
`msdyn_workflowcategoryname`, `msdyn_canvasappuniqueid`, `msdyn_isappaware`, `msdyn_isappawarename`,
`msdyn_istableenabled`, `msdyn_isdefault`, `msdyn_isdefaultname`, `organizationid`

**Notes:**
- `$select` works for limiting fields.
- `api-version=9.1` is included but works without it too.
- Filtering on `msdyn_ismanaged` with `eq true`/`eq false` returns a **BadRequest** error. Use string values or filter client-side.

---

## 4. Component Layers

**OData URL:**
```
msdyn_componentlayers?$filter=(msdyn_componentid eq '645527e4-8904-4c8e-a6f5-52876b7880c6' and msdyn_solutioncomponentname eq 'Entity')
```

**Status:** ✅ Success

**Response:** Array of layer objects. Each layer represents a solution that contributes to a component. Key fields:

| Field | Type | Example |
|---|---|---|
| `msdyn_componentlayerid` | GUID | `9db6a984-198b-4778-af72-903752c072b0` |
| `msdyn_componentid` | GUID | `645527e4-8904-4c8e-a6f5-52876b7880c6` |
| `msdyn_solutioncomponentname` | string | `Entity` |
| `msdyn_name` | string | `agenticscenario` |
| `msdyn_solutionname` | string | `msdyn_PowerPlatformAppAgents` |
| `msdyn_publishername` | string | `Dynamics 365` |
| `msdyn_order` | int | `1` (bottom = first installed) |
| `msdyn_overwritetime` | datetime | `1900-01-01T00:00:00Z` |
| `msdyn_children` | null/string | null or child layer JSON |
| `msdyn_componentjson` | string (JSON) | Full serialized component metadata |
| `msdyn_changes` | string (JSON) | Delta changes for this layer |

**Notes:**
- `msdyn_componentjson` and `msdyn_changes` are very large serialized JSON strings containing the full entity definition (all attributes, capabilities, labels, etc.).
- `$select` does NOT effectively reduce payload — the large JSON fields are always returned.
- `$top` works for limiting the number of layers.
- `msdyn_order` indicates the layer position in the stack (1 = bottom/first).

---

## 5. Dependency APIs

### 5a. RetrieveDependentComponents (non-metadata)

**OData URL:**
```
RetrieveDependentComponents(ObjectId=70816501-edb9-4740-a16c-6a5efbc05d84,ComponentType=1)
```

**Status:** ✅ Success (129 items for `account` entity)

**Response fields:**

| Field | Type | Example |
|---|---|---|
| `dependencyid` | GUID | `3ede6acd-4d68-4f44-a8ba-2d7004a3227d` |
| `dependentcomponentobjectid` | GUID | `cf75831c-9aa7-49cd-814d-7a03e1e40576` |
| `dependentcomponenttype` | int | `10` (Relationship) |
| `dependentcomponentbasesolutionid` | GUID | `fd140aad-4df4-11dd-bd17-0019b9312238` |
| `dependentcomponentparentid` | GUID | `00000000-0000-0000-0000-000000000000` |
| `requiredcomponentobjectid` | GUID | `70816501-edb9-4740-a16c-6a5efbc05d84` |
| `requiredcomponenttype` | int | `1` (Entity) |
| `requiredcomponentbasesolutionid` | GUID | `fd140aad-4df4-11dd-bd17-0019b9312238` |
| `requiredcomponentparentid` | GUID | `00000000-0000-0000-0000-000000000000` |
| `requiredcomponentintroducedversion` | float | `5.0` |
| `dependencytype` | int | `1` (Published), `2` (Solution Internal), `4` (Unpublished) |
| `_requiredcomponentnodeid_value` | GUID | Internal node reference |
| `_dependentcomponentnodeid_value` | GUID | Internal node reference |

### 5b. RetrieveDependentComponentsWithMetadata

**OData URL:**
```
RetrieveDependentComponentsWithMetadata(ObjectId=70816501-edb9-4740-a16c-6a5efbc05d84,ComponentType=1)
```

**Status:** ⚠️ Returns empty array `[]` — even for entities with known dependencies.

**Notes:** The WithMetadata variant consistently returns empty arrays for all tested entities. The non-metadata variant works correctly. This may be a platform limitation, API version issue, or permission-related.

### 5c. RetrieveRequiredComponents

**OData URL:**
```
RetrieveRequiredComponents(ObjectId=70816501-edb9-4740-a16c-6a5efbc05d84,ComponentType=1)
```

**Status:** ✅ Success (1 item for `account` — depends on a parent attribute)

**Response:** Same schema as `RetrieveDependentComponents`.

### 5d. RetrieveRequiredComponentsWithMetadata

**OData URL:**
```
RetrieveRequiredComponentsWithMetadata(ObjectId=70816501-edb9-4740-a16c-6a5efbc05d84,ComponentType=1)
```

**Status:** ⚠️ Returns empty array `[]`.

### 5e. RetrieveDependenciesForDelete

**OData URL:**
```
RetrieveDependenciesForDelete(ObjectId=70816501-edb9-4740-a16c-6a5efbc05d84,ComponentType=1)
```

**Status:** ✅ Success (54 items for `account`)

**Response:** Same schema as other dependency APIs.

### 5f. RetrieveDependenciesForDeleteWithMetadata

**OData URL:**
```
RetrieveDependenciesForDeleteWithMetadata(ObjectId=70816501-edb9-4740-a16c-6a5efbc05d84,ComponentType=1)
```

**Status:** ⚠️ Returns empty array `[]`.

### 5g. RetrieveDependenciesForUninstall

**OData URL:**
```
RetrieveDependenciesForUninstall(SolutionUniqueName='msdyn_RichTextEditor')
```

**Status:** ✅ Success (12 items)

**Notes:**
- Uses `SolutionUniqueName` (string) not `SolutionId` (GUID).
- Response has same dependency schema but **without** `dependencyid` and `requiredcomponentintroducedversion` fields.

### 5h. RetrieveDependenciesForUninstallWithMetadata

**OData URL:**
```
RetrieveDependenciesForUninstallWithMetadata(SolutionId=25a01723-9f63-4449-a3e0-046cc23a2902)
```

**Status:** ⚠️ Returns empty array `[]`.

**Notes:** Uses `SolutionId` (GUID) not `SolutionUniqueName` (string). Different parameter name than the non-metadata variant.

---

## 6. SolutionComponents (Real Table)

**OData URL:**
```
solutioncomponents?$filter=_solutionid_value eq 25a01723-9f63-4449-a3e0-046cc23a2902&$top=5
```

**Status:** ✅ Success

**Response fields:**

| Field | Type | Example |
|---|---|---|
| `solutioncomponentid` | GUID | `49742e9f-8437-f111-88b4-000d3a30bc26` |
| `objectid` | GUID | `8a463887-8437-f111-88b4-000d3a30bc26` |
| `componenttype` | int | `10332`, `10`, `202` |
| `_solutionid_value` | GUID | `25a01723-9f63-4449-a3e0-046cc23a2902` |
| `ismetadata` | bool | `false`, `true` |
| `rootcomponentbehavior` | int | `0` |
| `rootsolutioncomponentid` | GUID/null | null or parent component GUID |
| `createdon` | datetime | `2026-04-13T22:03:44Z` |
| `modifiedon` | datetime | `2026-04-13T22:03:44Z` |
| `versionnumber` | int | `4000839` |
| `_createdby_value` | GUID | User GUID |
| `_modifiedby_value` | GUID | User GUID |
| `_createdonbehalfby_value` | GUID/null | Delegate user |
| `_modifiedonbehalfby_value` | GUID/null | Delegate user |

**Notes:**
- `componenttype` is an integer code — needs mapping to names (e.g., 1=Entity, 2=Attribute, 10=Relationship, 9=OptionSet, 202=SecurityRole, etc.).
- `objectid` is the MetadataId/record ID of the component.
- `ismetadata` indicates whether it's a metadata component or data component.
- `rootsolutioncomponentid` links to the parent component if this is a sub-component.

---

## 7. EntityDefinitions (Metadata API)

**OData URL:**
```
EntityDefinitions?$filter=LogicalName eq 'account'&$select=MetadataId,LogicalName,DisplayName
```

**Status:** ✅ Success

**Response:** Array with expanded `DisplayName` containing `LocalizedLabels` and `UserLocalizedLabel`.

| Field | Type | Example |
|---|---|---|
| `MetadataId` | GUID | `70816501-edb9-4740-a16c-6a5efbc05d84` |
| `LogicalName` | string | `account` |
| `DisplayName.LocalizedLabels[].Label` | string | `Organization` |
| `DisplayName.LocalizedLabels[].LanguageCode` | int | `1033` |
| `DisplayName.UserLocalizedLabel.Label` | string | `Organization` |

---

## Key Findings Summary

| API | Status | Notes |
|---|---|---|
| `solutions` (filter/select/expand) | ✅ Works | Full OData support |
| `msdyn_solutioncomponentcountsummaries` | ✅ Works | Useful for rollup counts per component type |
| `msdyn_solutioncomponentsummaries` | ✅ Works | Rich detail; `$select` supported; boolean filters fail |
| `msdyn_componentlayers` | ✅ Works | Very large payloads (JSON blobs) |
| `solutioncomponents` | ✅ Works | Raw table — needs type code mapping |
| `EntityDefinitions` | ✅ Works | Metadata API with PascalCase fields |
| `RetrieveDependentComponents` | ✅ Works | Returns full dependency graph |
| `RetrieveRequiredComponents` | ✅ Works | Returns upstream dependencies |
| `RetrieveDependenciesForDelete` | ✅ Works | Shows what blocks deletion |
| `RetrieveDependenciesForUninstall` | ✅ Works | Uses `SolutionUniqueName` param |
| All `*WithMetadata` variants | ⚠️ Empty | Return `[]` — non-metadata variants work |

### Important Quirks
1. **WithMetadata dependency APIs** all return empty arrays. Use non-metadata variants instead.
2. **Boolean filters** on `msdyn_solutioncomponentsummaries` (e.g., `msdyn_ismanaged eq false`) return BadRequest. Filter client-side.
3. **`msdyn_componentlayers`** responses are very large due to embedded JSON strings (`msdyn_componentjson`, `msdyn_changes`). `$select` doesn't reduce this.
4. **`RetrieveDependenciesForUninstall`** uses `SolutionUniqueName` (string), while the `WithMetadata` variant uses `SolutionId` (GUID).
5. **Component type codes** — integer values need mapping: 1=Entity, 2=Attribute, 9=OptionSet, 10=Relationship, 66=CustomControl, 202=SecurityRole, 10332=custom, etc.
6. **Dependency type codes**: 1=Published, 2=SolutionInternal, 4=Unpublished.
