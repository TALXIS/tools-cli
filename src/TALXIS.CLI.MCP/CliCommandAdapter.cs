using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace TALXIS.CLI.MCP
{
    public class CliCommandAdapter
    {
        public List<string> BuildCliArgs(string toolName, IReadOnlyDictionary<string, JsonElement>? arguments)
        {
            var cliArgs = toolName.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();

            // Try to find the command type for argument ordering
            var commandType = new McpToolRegistry().FindCommandTypeByToolName(toolName);
            var positionalArgs = new List<string>();
            var optionArgs = new List<string>();

            if (arguments != null && commandType != null)
            {
                var props = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                // Handle CliArgument (positional) in order
                var argProps = props
                    .Select(p => new {
                        Prop = p,
                        Attr = p.GetCustomAttribute(typeof(DotMake.CommandLine.CliArgumentAttribute)) as DotMake.CommandLine.CliArgumentAttribute
                    })
                    .Where(x => x.Attr != null)
                    .ToList();
                foreach (var arg in argProps)
                {
                    var name = arg.Attr?.Name ?? arg.Prop.Name;
                    if (arguments.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null)
                    {
                        positionalArgs.Add(value.ToString());
                    }
                }

                // Handle CliOption (named)
                foreach (var entry in arguments)
                {
                    // Only add as option if not already used as positional
                    if (argProps.Any(x => (x.Attr?.Name ?? x.Prop.Name) == entry.Key))
                        continue;
                    if (entry.Value.ValueKind != JsonValueKind.Null)
                        optionArgs.Add($"--{entry.Key}={entry.Value}");
                }
            }
            else if (arguments != null)
            {
                // Fallback: treat all as options
                foreach (var entry in arguments)
                {
                    if (entry.Value.ValueKind != JsonValueKind.Null)
                        optionArgs.Add($"--{entry.Key}={entry.Value}");
                }
            }

            cliArgs.AddRange(positionalArgs);
            cliArgs.AddRange(optionArgs);
            return cliArgs;
        }

        public JsonElement BuildInputSchema(Type commandType)
        {
            var properties = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var required = new List<string>();
            var schemaProperties = new Dictionary<string, object?>();

            // Add CliArgument (positional) properties
            foreach (var prop in properties)
            {
                var argAttr = prop.GetCustomAttribute(typeof(DotMake.CommandLine.CliArgumentAttribute)) as DotMake.CommandLine.CliArgumentAttribute;
                if (argAttr != null)
                {
                    var type = prop.PropertyType == typeof(bool) ? "boolean" : "string";
                    var argName = argAttr.Name ?? prop.Name;
                    schemaProperties[argName] = new Dictionary<string, object?>
                    {
                        ["type"] = type,
                        ["description"] = argAttr.Description
                    };
                    required.Add(argName);
                }
            }

            // Add CliOption (named) properties
            foreach (var prop in properties)
            {
                var optionAttr = prop.GetCustomAttribute(typeof(DotMake.CommandLine.CliOptionAttribute)) as DotMake.CommandLine.CliOptionAttribute;
                if (optionAttr != null)
                {
                    var type = prop.PropertyType == typeof(bool) ? "boolean" : "string";
                    var optionName = (optionAttr.Name ?? prop.Name).TrimStart('-');
                    schemaProperties[optionName] = new Dictionary<string, object?>
                    {
                        ["type"] = type,
                        ["description"] = optionAttr.Description
                    };
                    if (optionAttr.Required)
                        required.Add(optionName);
                }
            }

            var schema = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = schemaProperties,
                ["required"] = required
            };
            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(schema));
        }
    }
}
