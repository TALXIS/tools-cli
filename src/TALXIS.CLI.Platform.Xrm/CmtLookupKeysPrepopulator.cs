using System.Reflection;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// Pre-seeds CMT's internal lookup-resolution state for package-internal
/// references, eliminating redundant "LOOKUP TO CRM" server round-trips
/// during import.
///
/// <para>
/// <b>Root cause:</b> CMT's <c>FindEntity()</c>
/// (<c>ImportCrmEntityActions.cs</c>) falls back to a live server query
/// whenever a referenced record's in-memory <c>newId</c> is empty.
/// <c>newId</c> is only populated once the batch response callback for
/// that specific record's create/upsert has fired. With
/// <c>--batch-mode</c> and <c>--connection-count &gt; 1</c>, CMT dispatches
/// an entity's batches asynchronously and can begin preprocessing the next
/// (dependent) entity before every parent batch has actually received its
/// response — so a perfectly valid, already-created parent record still
/// looks "unresolved" locally, triggering a costly live lookup that almost
/// always just confirms what CMT could already have known. This is a local
/// cache-staleness / async race inherent to CMT's batch+multi-connection
/// architecture, not a genuine missing-dependency or ordering problem.
/// </para>
///
/// <para>
/// <b>Fix:</b> because CMT always preserves source GUIDs (an upserted
/// record's target <c>Id</c> is set to the source record's GUID), the
/// target GUID is always equal to the source GUID for package-internal
/// references. We can therefore pre-seed both of <c>FindEntity()</c>'s
/// lookup paths — the <c>LookupKeys</c> cache and each record's own
/// <c>newId</c> field — before any batch is ever dispatched, removing the
/// dependency on batch-response timing entirely. This does not affect
/// external lookups (entities not present in the package); those still
/// resolve via the normal server call path.
/// </para>
///
/// <para>
/// Accesses CMT internals via reflection because
/// <c>Microsoft.Xrm.Tooling.Dmt.DataMigCommon</c> is a net462 legacy
/// assembly that is patched and loaded at runtime — it is not directly
/// referenceable from the net10 host at compile time.
/// </para>
/// </summary>
internal static class CmtLookupKeysPrepopulator
{
    private const string ImportCommonMethodsTypeName =
        "Microsoft.Xrm.Tooling.Dmt.DataMigCommon.DataInteraction.ImportCommonMethods";

    /// <summary>
    /// Result of a pre-population run, for logging/telemetry by the caller.
    /// </summary>
    public readonly record struct Result(int RecordCount, int EntityCount);

    /// <summary>
    /// Pre-seeds the LookupKeys cache and newId fields for every record in
    /// the already-loaded package. Safe to call multiple times; each call
    /// re-scans the current in-memory package data.
    /// </summary>
    /// <param name="handlerRuntimeType">
    /// Runtime type of the ImportCrmDataHandler instance in use. Used to
    /// navigate to the correct runtime-loaded DataMigCommon assembly, which
    /// may differ from the net462 compile-time reference if the assembly
    /// resolver returned a different instance (there can be two loaded
    /// copies of DataMigCommon with independent static state).
    /// </param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>
    /// The number of records/entities updated, or <c>null</c> if
    /// pre-population could not run (e.g. package not loaded yet).
    /// </returns>
    public static Result? Prepopulate(Type? handlerRuntimeType, ILogger logger)
    {
        try
        {
            Type? importCommonType = ResolveImportCommonMethodsType(handlerRuntimeType);
            if (importCommonType == null)
            {
                logger.LogInformation(
                    "LookupKeys pre-population skipped — ImportCommonMethods type not found in loaded assemblies");
                return null;
            }

            logger.LogInformation(
                "LookupKeys pre-population: using type from assembly {Assembly}",
                importCommonType.Assembly.FullName);

            if (!TryGetLookupKeysTryAdd(importCommonType, logger, out object? lookupKeys, out MethodInfo? tryAdd))
                return null;

            if (!TryGetDataEntities(importCommonType, logger, out System.Collections.IEnumerable? entityArray))
                return null;

            Result result = PrepopulateRecords(entityArray!, lookupKeys!, tryAdd!);

            logger.LogInformation(
                "Pre-populated LookupKeys cache and set newId for {Count} records across {EntityCount} entities — "
                + "eliminates server lookup calls for internal package references",
                result.RecordCount, result.EntityCount);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex,
                "LookupKeys pre-population failed — import will proceed with standard CMT lookup behavior");
            return null;
        }
    }

    /// <summary>
    /// Navigates to the RUNTIME-loaded DataMigCommon assembly via the
    /// handler's type. AppDomain.GetAssemblies() may contain both the
    /// compile-time net462 copy and the runtime-loaded copy; they carry
    /// separate static state, so we must find the one CMT actually uses.
    /// </summary>
    private static Type? ResolveImportCommonMethodsType(Type? handlerRuntimeType)
    {
        if (handlerRuntimeType != null)
        {
            foreach (AssemblyName asmRef in handlerRuntimeType.Assembly.GetReferencedAssemblies())
            {
                if (asmRef.Name?.Contains("DataMigCommon", StringComparison.OrdinalIgnoreCase) != true)
                    continue;

                Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, asmRef.Name, StringComparison.OrdinalIgnoreCase));
                if (loaded == null) continue;

                Type? candidate = loaded.GetType(ImportCommonMethodsTypeName);
                if (candidate != null) return candidate;
            }
        }

        // Fallback: scan all loaded assemblies (less precise but still useful).
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.Contains("DataMigCommon", StringComparison.OrdinalIgnoreCase) == true)
            .Select(a => a.GetType(ImportCommonMethodsTypeName))
            .FirstOrDefault(t => t != null);
    }

    /// <summary>
    /// Retrieves the static <c>LookupKeys</c> ConcurrentDictionary&lt;string, Guid&gt;
    /// and its <c>TryAdd(string, Guid)</c> method.
    /// </summary>
    private static bool TryGetLookupKeysTryAdd(
        Type importCommonType,
        ILogger logger,
        out object? lookupKeys,
        out MethodInfo? tryAdd)
    {
        lookupKeys = null;
        tryAdd = null;

        FieldInfo? lookupKeysField = importCommonType.GetField(
            "LookupKeys", BindingFlags.Public | BindingFlags.Static);
        lookupKeys = lookupKeysField?.GetValue(null);
        if (lookupKeys == null)
        {
            logger.LogInformation("LookupKeys pre-population skipped — LookupKeys field not found or null");
            return false;
        }

        tryAdd = lookupKeys.GetType().GetMethod("TryAdd", [typeof(string), typeof(Guid)]);
        if (tryAdd == null)
        {
            logger.LogInformation("LookupKeys pre-population skipped — TryAdd method not found on LookupKeys");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Retrieves the static <c>dataEntities</c> property and its <c>entity</c> array.
    /// </summary>
    private static bool TryGetDataEntities(
        Type importCommonType,
        ILogger logger,
        out System.Collections.IEnumerable? entityArray)
    {
        entityArray = null;

        PropertyInfo? dataEntitiesProp = importCommonType.GetProperty(
            "dataEntities", BindingFlags.Public | BindingFlags.Static);
        object? dataEntities = dataEntitiesProp?.GetValue(null);
        if (dataEntities == null)
        {
            logger.LogInformation("LookupKeys pre-population skipped — dataEntities not loaded yet");
            return false;
        }

        PropertyInfo? entityArrayProp = dataEntities.GetType().GetProperty("entity");
        entityArray = entityArrayProp?.GetValue(dataEntities) as System.Collections.IEnumerable;
        if (entityArray == null)
        {
            logger.LogDebug("LookupKeys pre-population skipped — entity array not found");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Walks every entity/record in the package and applies both
    /// pre-population strategies.
    /// </summary>
    private static Result PrepopulateRecords(
        System.Collections.IEnumerable entityArray,
        object lookupKeys,
        MethodInfo tryAdd)
    {
        int count = 0;
        int entityCount = 0;

        foreach (object? entity in entityArray)
        {
            if (entity == null) continue;
            entityCount++;

            string? entityName = entity.GetType().GetProperty("name")?.GetValue(entity) as string;
            if (string.IsNullOrEmpty(entityName)) continue;

            if (entity.GetType().GetProperty("records")?.GetValue(entity)
                is not System.Collections.IEnumerable records)
            {
                continue;
            }

            count += PrepopulateEntityRecords(entityName, records, lookupKeys, tryAdd);
        }

        return new Result(count, entityCount);
    }

    private static int PrepopulateEntityRecords(
        string entityName,
        System.Collections.IEnumerable records,
        object lookupKeys,
        MethodInfo tryAdd)
    {
        int count = 0;
        PropertyInfo? idProp = null;
        PropertyInfo? newIdProp = null;

        foreach (object? record in records)
        {
            if (record == null) continue;

            // Cache property lookups after first record.
            if (idProp == null)
            {
                idProp = record.GetType().GetProperty("id");
                newIdProp = record.GetType().GetProperty("newId");
            }

            string? id = idProp?.GetValue(record) as string;
            if (string.IsNullOrEmpty(id)) continue;
            if (!Guid.TryParse(id, out Guid guid)) continue;

            // Strategy A: pre-seed LookupKeys so FindEntity() short-circuits
            // at the cache check (line 2851 of ImportCrmEntityActions.cs).
            string key = string.Concat(entityName, ":", id);
            tryAdd.Invoke(lookupKeys, [key, guid]);

            // Strategy B: set newId = id on the record object so that even
            // if the LookupKeys check misses, FindEntity() at line 2912 finds
            // a non-empty newId and returns without a server round-trip.
            // CMT preserves source GUIDs (UpsertRequest.Target.Id = source GUID),
            // so newId == id is always correct for package-internal references.
            if (newIdProp != null)
            {
                string? existingNewId = newIdProp.GetValue(record) as string;
                if (string.IsNullOrWhiteSpace(existingNewId))
                    newIdProp.SetValue(record, id);
            }

            count++;
        }

        return count;
    }
}
