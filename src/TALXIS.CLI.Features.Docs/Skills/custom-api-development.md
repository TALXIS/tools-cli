# Custom API Development

Custom APIs are custom messages (actions) that expose reusable business logic as callable endpoints with typed request/response, synchronous with a return value. Use one when several clients or flows need the same operation; use a plugin for reactions to data events (Create, Update, Delete) and for simple field calculations. A Custom API does nothing without a backing plugin.

## Registration Workflow

1. Create the backing plugin first ([plugin-development](plugin-development.md)).
2. `txc workspace component create pp-api-endpoint --output <project-dir> --param CustomizationPrefix=<prefix> --param ApiLogicalName=<Name> --param DisplayName="<Display name>" --param Description="<description>"`
3. Request parameters: the template creates one placeholder string parameter (`StringParameter`). Rename it and add more by copying its folder under `customapirequestparameters/` and editing the XML (the `type` code sets the data type).
4. Response properties: same, with `StringProperty` under `customapiresponseproperties/`.

`<project-dir>` is the solution project that owns the API; `<prefix>` is its customization prefix (`<CustomizationPrefix>` in `Other/Solution.xml`). No template exists for individual parameters or properties; `txc workspace component parameter list pp-api-endpoint` shows the rest.

## Naming

- API: `{prefix}_{apilogicalname}` (`prefix_CalculateDiscount`)
- Request parameter: `{prefix}_{apiname}.{ParamName}`; response property: `{prefix}_{apiname}.{PropertyName}`

See also: [plugin-development](plugin-development.md), [component-creation](component-creation.md)
