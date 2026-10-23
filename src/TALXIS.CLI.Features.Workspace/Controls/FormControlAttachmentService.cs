using System.Xml.Linq;

namespace TALXIS.CLI.Features.Workspace.Controls;

/// <summary>Request to overlay a custom control on an existing form control.</summary>
public sealed class ControlAttachmentRequest
{
    public required string FormFilePath { get; init; }
    /// <summary>FormXml control id of the host control (e.g. <c>subgrid</c>).</summary>
    public required string TargetControlId { get; init; }
    public required ControlManifestInfo Manifest { get; init; }
    /// <summary>Publisher-prefixed control name for FormXml (e.g. <c>talxis_TALXIS.PCF.Grid</c>).</summary>
    public required string ControlName { get; init; }
    /// <summary>Control parameter values keyed by manifest property name.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    /// <summary>Replace an existing attachment on the same host control instead of failing.</summary>
    public bool Force { get; init; }
}

public sealed class ControlAttachmentResult
{
    public required string FormFilePath { get; init; }
    public required string ControlName { get; init; }
    public required string HostControlUniqueId { get; init; }
    public bool ReplacedExisting { get; init; }
}

/// <summary>
/// Overlays a PCF custom control on an existing subgrid of a form by inserting the
/// <c>controlDescriptions/controlDescription</c> node (base control block + one
/// <c>customControl</c> block per form factor), mirroring the shape produced by the
/// Dataverse form designer. Parameter values are validated against the control manifest,
/// and dataset binding values (ViewId, TargetEntityType, RelationshipName) are read from
/// the host subgrid so they never need to be supplied by hand.
/// </summary>
public static class FormControlAttachmentService
{
    private const string SubgridClassId = "{E7A81278-8635-4D9E-8D4D-59480B391C5B}";

    public static ControlAttachmentResult Attach(ControlAttachmentRequest request)
    {
        var doc = XDocument.Load(request.FormFilePath);
        var form = doc.Descendants("form").FirstOrDefault()
            ?? throw new InvalidOperationException($"No <form> element in '{request.FormFilePath}'.");

        var host = FindHostControl(form, request.TargetControlId);
        var uniqueId = host.Attribute("uniqueid")?.Value
            ?? throw new InvalidOperationException($"Host control '{request.TargetControlId}' has no uniqueid attribute.");

        ValidateParameters(request.Manifest, request.Parameters);

        var parameters = BuildParametersElement(request, host);
        var description = new XElement("controlDescription",
            new XAttribute("forControl", uniqueId),
            new XElement("customControl",
                new XAttribute("id", SubgridClassId),
                new XElement("parameters")));
        foreach (var formFactor in new[] { "0", "1", "2" })
        {
            description.Add(new XElement("customControl",
                new XAttribute("formFactor", formFactor),
                new XAttribute("name", request.ControlName),
                new XElement(parameters)));
        }

        var replaced = InsertDescription(form, description, uniqueId, request.Force);
        doc.Save(request.FormFilePath);

        return new ControlAttachmentResult
        {
            FormFilePath = request.FormFilePath,
            ControlName = request.ControlName,
            HostControlUniqueId = uniqueId,
            ReplacedExisting = replaced,
        };
    }

    private static XElement FindHostControl(XElement form, string targetControlId)
    {
        var host = form.Descendants("control").FirstOrDefault(c => c.Attribute("id")?.Value == targetControlId)
            ?? throw new InvalidOperationException(
                $"Control '{targetControlId}' not found on the form. Available controls: " +
                string.Join(", ", form.Descendants("control").Select(c => c.Attribute("id")?.Value).Where(id => id != null)));

        if (!string.Equals(host.Attribute("indicationOfSubgrid")?.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Control '{targetControlId}' is not a subgrid. Only subgrid-bound controls are supported; " +
                "field-bound controls (e.g. VirtualDataset on a placeholder column) are not supported yet.");
        }

        return host;
    }

    private static void ValidateParameters(ControlManifestInfo manifest, IReadOnlyDictionary<string, string> values)
    {
        var byName = manifest.Properties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var (name, value) in values)
        {
            if (!byName.TryGetValue(name, out var property))
            {
                errors.Add($"'{name}' is not a parameter of {manifest.QualifiedName}. Valid parameters: {string.Join(", ", manifest.Properties.Select(p => p.Name))}.");
                continue;
            }
            if (property.EnumValues.Count > 0 && !property.EnumValues.Contains(value, StringComparer.Ordinal))
                errors.Add($"'{name}': '{value}' is not an allowed value. Allowed: {string.Join(", ", property.EnumValues)}.");
            else if (property.OfType.StartsWith("Whole.", StringComparison.Ordinal) && !long.TryParse(value, out _))
                errors.Add($"'{name}': '{value}' is not a whole number ({property.OfType}).");
            else if (property.OfType == "TwoOptions" && value is not ("true" or "false"))
                errors.Add($"'{name}': '{value}' must be 'true' or 'false' (TwoOptions).");
        }

        if (errors.Count > 0)
            throw new ArgumentException("Invalid control parameters:\n  " + string.Join("\n  ", errors));
    }

    private static XElement BuildParametersElement(ControlAttachmentRequest request, XElement host)
    {
        var parameters = new XElement("parameters");

        // Dataset binding mirrors the host subgrid; the control's primary data-set gets the same view.
        var primaryDataSet = request.Manifest.DataSets.FirstOrDefault();
        if (primaryDataSet != null)
        {
            var hostParameters = host.Element("parameters");
            var viewId = hostParameters?.Element("ViewId")?.Value ?? "";
            parameters.Add(new XElement("data-set",
                new XAttribute("name", primaryDataSet),
                new XElement("ViewId", viewId),
                new XElement("TargetEntityType", hostParameters?.Element("TargetEntityType")?.Value ?? ""),
                new XElement("IsUserView", "false"),
                new XElement("EnableViewPicker", hostParameters?.Element("EnableViewPicker")?.Value ?? "false"),
                new XElement("RelationshipName", hostParameters?.Element("RelationshipName")?.Value ?? ""),
                new XElement("FilteredViewIds", viewId)));
        }

        var byName = request.Manifest.Properties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in request.Parameters)
        {
            var property = byName[name];
            parameters.Add(new XElement(property.Name,
                new XAttribute("type", property.OfType),
                new XAttribute("static", "true"),
                value));
        }

        return parameters;
    }

    /// <returns>True when an existing attachment for the same host control was replaced.</returns>
    private static bool InsertDescription(XElement form, XElement description, string uniqueId, bool force)
    {
        var container = form.Element("controlDescriptions");
        if (container == null)
        {
            container = new XElement("controlDescriptions");
            // Match the designer's element order: controlDescriptions precedes
            // DisplayConditions / formLibraries / events.
            var anchor = form.Element("DisplayConditions") ?? form.Element("formLibraries") ?? form.Element("events") as XNode;
            if (anchor != null) anchor.AddBeforeSelf(container);
            else form.Add(container);
        }

        var existing = container.Elements("controlDescription")
            .FirstOrDefault(d => string.Equals(d.Attribute("forControl")?.Value, uniqueId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (!force)
            {
                throw new InvalidOperationException(
                    $"A custom control is already attached to control '{uniqueId}'. Use --force to replace it.");
            }
            existing.ReplaceWith(description);
            return true;
        }

        container.Add(description);
        return false;
    }
}
