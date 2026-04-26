# Gateway API (`smartdataimport/multientity/createdirect`) — Feasibility Report

> Investigated from HAR captures of Power Apps Vibe Coding and Data Workspace.

---

## 1. Auth Analysis

### Token for `createdirect`

| Claim | Value |
|-------|-------|
| **Audience (`aud`)** | `https://api.insightsplatform.microsoft.com` |
| **Issuer** | `https://sts.windows.net/{tenantId}/` |
| **Scope** | `user_impersonation` |
| **Requested MSAL scope** | `https://api.insightsplatform.microsoft.com/.default` |
| **Client app (Vibe)** | `423e1d3b-eada-4c4b-9afd-ad3bbc1932d3` |
| **Client app (Make)** | `a8f7a65c-f5ba-4859-b2d6-df772c264e9d` |
| **Token version** | v1.0 |

### Can we obtain this token?

**Yes, with a caveat.** The existing `DataverseAccessTokenService.AcquireForResourceAsync()` already accepts an arbitrary `Uri resourceUri` and builds `{resource}//.default` scope. We would need:

1. Call `AcquireForResourceAsync` with `new Uri("https://api.insightsplatform.microsoft.com")` to get a gateway token.
2. The MSAL client ID `9cee029c-6210-4654-90bb-17e6e9d36617` (pac CLI's app ID) must have consent for `https://api.insightsplatform.microsoft.com/user_impersonation`. This is **not guaranteed** — the Vibe/Make clients (`423e1d3b` / `a8f7a65c`) are 1st-party Microsoft apps with pre-consented permissions.

**Risk:** If Microsoft hasn't exposed `user_impersonation` on the `api.insightsplatform.microsoft.com` resource for 3rd-party apps, our MSAL login will fail with `AADSTS650053` (app not authorized). Testing required.

**Note:** The Dataverse scope uses `//.default` (double-slash). The gateway scope in the HAR is `https://api.insightsplatform.microsoft.com/.default` (single-slash, standard). The `DataverseScope.BuildDefault()` uses double-slash, so we'd need a separate scope builder or a parameter to control this.

### Required request headers

| Header | Source |
|--------|--------|
| `Authorization: Bearer {token}` | MSAL token for `api.insightsplatform.microsoft.com` |
| `x-ms-organization-id` | Environment's `resourceId` from BAP API or `WhoAmI` |
| `x-ms-organization-tenant-id` | Tenant ID (from token or config) |
| `x-ms-client-request-id` | New GUID per request |
| `x-ms-client-session-id` | New GUID per session |
| `scenarioname` | `powerapps.oneworkspace` (may need to match) |
| `mscrm.instantentitymode` | `false` |

---

## 2. Gateway URL Construction

### URL pattern

```
https://insightsplatform.{region}.gateway.prod.island.powerapps.com/api/scopes/service/smartdataimport/multientity/createdirect
```

### How is `{region}` determined?

From the HARs, the region is `us-il301` for all gateway services. **There is no discovery endpoint** that returns the gateway URL — it is not present in the BAP environment metadata response (`runtimeEndpoints` does not include it).

The region appears to be derived from the environment's Azure region. Possible approaches:

1. **Hardcode a region mapping** — map Dataverse `instanceUrl` domains (`crm.dynamics.com` → `us`, `crm4.dynamics.com` → `emea`, etc.) to gateway region codes.
2. **Parse from BAP metadata** — the environment's `azureRegion` or `location` field may correlate.
3. **Probe/discover** — try known region patterns.

**Observed gateway regions in HARs:**

| Service | Region code |
|---------|-------------|
| `insightsplatform` | `us-il301` |
| `aibuildertextapiservice` | `us-il301` |
| `pada` | `us-ia301` |
| `orchard` | `wus-il301` |
| `harvest-*` | `wus-il301` |

**Note:** Different services use different region codes even for the same environment. This is a significant complexity factor.

### BAP discovery endpoint

```
GET https://preview.api.bap.microsoft.com/api/invoke
x-ms-path-query: providers/Microsoft.BusinessAppPlatform/environments/{envId}?api-version=2020-10-01-alpha
```

Returns environment metadata including:
- `linkedEnvironmentMetadata.resourceId` → org ID (for `x-ms-organization-id`)
- `linkedEnvironmentMetadata.instanceUrl` → Dataverse URL
- `properties.azureRegion` → could be used for region derivation

---

## 3. Endpoint Catalog

### A. Gateway Endpoints (powerapps.com)

#### Smart Data Import (insightsplatform)
| Method | URL | Purpose |
|--------|-----|---------|
| POST | `insightsplatform.{region}.gateway.prod.island.powerapps.com/api/scopes/service/smartdataimport/multientity/createdirect` | Bulk create entities + attributes + relationships in one call |

**Request body:**
```json
{
  "SolutionUniqueName": "Cr6e8c7",
  "UserSettings": {
    "LocaleId": 1033,
    "NegativeFormatCode": 1,
    "NumberSeparator": ",",
    "DecimalSymbol": ".",
    "NumberGroupFormat": "123,456,789"
  },
  "EntitiesCollection": [
    {
      "EntityDefinition": {
        "@odata.type": "Microsoft.Dynamics.CRM.EntityMetadata",
        "SchemaName": "cr509_Employee",
        "DisplayName": { "LocalizedLabels": [{ "Label": "Employee", "LanguageCode": 1033 }] },
        "DisplayCollectionName": { "LocalizedLabels": [{ "Label": "Employees", "LanguageCode": 1033 }] },
        "Description": { "LocalizedLabels": [{ "Label": "...", "LanguageCode": 1033 }] },
        "OwnershipType": "UserOwned",
        "HasActivities": false,
        "HasNotes": false,
        "ChangeTrackingEnabled": true,
        "Attributes": [ "/* full attribute definitions */" ],
        "OneToManyRelationships": [],
        "ManyToOneRelationships": [ "/* lookup definitions */" ],
        "ManyToManyRelationships": []
      },
      "SampleData": [ "/* optional sample rows */" ]
    }
  ]
}
```

#### AI Builder Text API
| Method | URL | Purpose |
|--------|-----|---------|
| POST | `aibuildertextapiservice.{region}.gateway.prod.island.powerapps.com/v1.0/{envId}/skillstore/execute/stream` | Copilot AI streaming (entity generation from NL) |

#### Orchard / Harvest (App building — not relevant for entity ops)
| Method | URL | Purpose |
|--------|-----|---------|
| POST | `orchard.{region}.gateway.prod.island.powerapps.com/api/harvest/save2` | Save Power App |
| GET | `harvest-{appId}.sh.{region}.gateway.prod.island.powerapps.com/api-proxy/{appId}/api/status` | App preview status |

### B. Dataverse Endpoints (dynamics.com)

#### Entity Creation (warm-up)
| Method | URL | Purpose |
|--------|-----|---------|
| POST | `/api/data/v9.0/CreateInstantEntityWarmUp` | Pre-warms entity creation pipeline (empty body `{}`) |

#### Entity Metadata CRUD
| Method | URL | Purpose |
|--------|-----|---------|
| GET | `/api/data/v9.0/EntityDefinitions?$filter=...` | List/query entity metadata |
| GET | `/api/data/v9.2/EntityDefinitions(LogicalName='{name}')/Attributes` | Get entity attributes |
| POST | `/api/data/v9.0/EntityDefinitions({id})/Attributes` | Create attribute (standard Dataverse) |
| PUT | `/api/data/v9.0/EntityDefinitions({id})/Attributes({attrId})/...` | Update attribute |

#### Solution Management
| Method | URL | Purpose |
|--------|-----|---------|
| GET | `/api/data/v9.0/GetPreferredSolution` | Get user's preferred/active solution |
| GET | `/api/data/v9.0/solutions?$filter=uniquename eq '{name}'` | Query solution by name |
| GET | `/api/data/v9.0/solutions({id})?$expand=publisherid` | Get solution with publisher |
| GET | `/api/data/v9.0/solutions?$filter=_parentsolutionid_value eq {id}` | Get solution patches |
| GET | `/api/data/v9.0/msdyn_solutioncomponentsummaries?$filter=...` | Query solution components by type |
| GET | `/api/data/v9.0/solutioncomponentdefinitions` | List component type definitions |
| GET | `/api/data/v9.1/solutioncomponentconfigurations?$filter=isdisplayable eq true` | Displayable component configs |

#### Key finding: No `AddSolutionComponent` / `RemoveSolutionComponent`
Components are added **implicitly** via the `MSCRM.SolutionUniqueName` header on write operations. No explicit add/remove calls found in any HAR.

#### AI / Copilot
| Method | URL | Purpose |
|--------|-----|---------|
| POST | `/api/data/v9.2/AssistedSearch` | Copilot assisted search |
| POST | `/api/data/v9.2/EntitySkill` | AI entity definition generation |

#### `MSCRM.SolutionUniqueName` header usage
Used on all write operations (attribute create/update, entity patch, plan artifact updates) to implicitly add components to the active solution. Value example: `Cr6e8c7`.

---

## 4. Replacement Map

| Current `txc` command | Current approach | Could use `createdirect`? | Benefit |
|---|---|---|---|
| `entity create` | `CreateEntityRequest` via SDK | ✅ **Yes** — primary use case | Bulk create N entities + all attrs + relationships in ~16-21s vs minutes sequential |
| `attribute create` | `CreateAttributeRequest` via SDK | ⚠️ **Only for new entities** — inline in EntityDefinition.Attributes | Eliminates separate attr creation calls during entity scaffolding |
| `relationship create` | `Create*Request` via SDK | ⚠️ **Only for new entities** — inline in EntityDefinition.*Relationships | Same-call relationship setup |
| `attribute update` | Web API PUT | ❌ **No** — `createdirect` is create-only | N/A (HAR shows standard Dataverse PUT for updates) |
| `attribute delete` | Web API DELETE | ❌ **No** | N/A |
| `entity update` | SDK | ❌ **No** | N/A |
| `entity delete` | SDK | ❌ **No** | N/A |
| `solution import/list/uninstall` | SDK | ❌ **No** — not a gateway feature | N/A |

### Proposed new command

```
txc environment entity create-bulk
    --solution <name>
    --definition <path-to-json-or-yaml>
    [--sample-data <path>]
    [--use-gateway]        # opt-in to gateway API
    [--region <code>]      # override gateway region detection
```

This would accept a multi-entity definition file (matching the `createdirect` schema) and create everything in one call. The `--use-gateway` flag makes it opt-in while the API is undocumented.

---

## 5. Solution Command Surface Proposal

### What the HARs reveal about solution operations

The Data Workspace uses **only standard Dataverse Web API** for solution management — no gateway endpoints. All operations are already achievable with the existing `txc` Dataverse connection.

### Current `txc` solution commands

```
txc environment solution
├── list       ✅ exists
├── import     ✅ exists
└── uninstall  ✅ exists
```

### Proposed additions

```
txc environment solution (alias: sln)
├── list                    # ✅ exists
├── get <name>              # NEW — query + expand publisher
├── create                  # NEW — standard CreateRequest
├── delete                  # NEW — standard DeleteRequest
├── export                  # NEW — ExportSolutionRequest
├── import                  # ✅ exists
├── uninstall               # ✅ exists
├── preferred               # NEW — GetPreferredSolution function
├── component
│   ├── list                # NEW — msdyn_solutioncomponentsummaries query
│   ├── add                 # NEW — AddSolutionComponentRequest (standard SDK)
│   └── remove              # NEW — RemoveSolutionComponentRequest (standard SDK)
└── publish                 # NEW — PublishAllXmlRequest
```

**Note:** The `MSCRM.SolutionUniqueName` header approach (implicit add on create) should remain the **default** for all entity/attribute/relationship create commands. The `--solution` parameter already sets this header.

### Implementation priority

1. **`solution get`** + **`solution preferred`** — trivial, high utility
2. **`solution component list`** — enables solution inspection
3. **`solution export`** — high demand, standard SDK call
4. **`solution create`** / **`solution delete`** — standard CRUD
5. **`solution component add/remove`** — advanced scenarios
6. **`solution publish`** — already partially exists (PublishXml on entity ops)

---

## 6. Adoption Assessment

### Can we authenticate?

| Factor | Status |
|--------|--------|
| Token acquisition code | ✅ `AcquireForResourceAsync()` already supports arbitrary resources |
| Scope format | ⚠️ Needs `/.default` (single slash), not `//.default` (Dataverse double-slash) |
| App registration consent | ❓ **Unknown** — `9cee029c` (pac CLI app) may not have consent for `api.insightsplatform.microsoft.com`. Must test. |
| Fallback to 1P app ID | 🔴 Can't use Vibe/Make client IDs — they're 1st-party Microsoft apps |

**Action required:** Test token acquisition for `https://api.insightsplatform.microsoft.com/.default` with our MSAL client. If it fails, we cannot use the gateway without a custom app registration with admin-granted consent.

### Risk of depending on undocumented API

| Risk | Severity | Mitigation |
|------|----------|------------|
| API removed/changed without notice | **High** | Feature-flag (`--use-gateway`), graceful fallback to SDK |
| Endpoint URL format changes | **Medium** | Centralize URL construction, easy to update |
| Auth requirements change | **Medium** | Already modular token acquisition |
| Rate limiting / throttling | **Low** | Unlikely worse than Dataverse SDK |
| Region discovery breaks | **Medium** | Allow `--region` override, maintain mapping table |

### Recommendation

| Approach | Verdict |
|----------|---------|
| **Primary path** | ❌ Too risky for undocumented API |
| **Opt-in accelerator** | ✅ **Recommended** — `--use-gateway` flag on `entity create-bulk` |
| **Fallback** | Sequential `CreateEntityRequest` (current approach, always works) |

### Implementation strategy

**Phase 1 (Safe, immediate value):**
- Add `solution get`, `solution preferred`, `solution export`, `solution component list` using standard Dataverse Web API
- Add `entity create-bulk` command that accepts multi-entity JSON and creates sequentially via SDK
- Ensure `--solution` is consistently supported on all create commands

**Phase 2 (Gateway experiment):**
- Test gateway token acquisition with pac CLI client ID
- If successful, add `--use-gateway` flag to `entity create-bulk`
- Implement gateway client with proper error handling and fallback
- Map region from environment metadata

**Phase 3 (If gateway proves stable):**
- Make gateway the default for `entity create-bulk` with SDK fallback
- Add `CreateInstantEntityWarmUp` pre-call for reliability
- Consider exposing AI Builder entity generation (`EntitySkill`) as `entity generate`

### Parameters to reserve on existing commands

| Command | Parameter | Purpose |
|---------|-----------|---------|
| All create commands | `--solution` | ✅ Already exists |
| `entity create` | `--bulk` / `--definition` | Multi-entity definition file |
| `entity create` | `--use-gateway` | Opt-in to `createdirect` API |
| `entity create` | `--region` | Override gateway region detection |
| `entity create` | `--sample-data` | Include sample data (gateway supports this) |
| `entity create` | `--warm-up` | Call `CreateInstantEntityWarmUp` before creation |
