using System.Reflection;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.MCP
{
    public class CliCommandLookupService
    {
        public IEnumerable<McpToolDescriptor> EnumerateAllCommands(Type rootType)
        {
            var results = new List<McpToolDescriptor>();
            EnumerateRecursive(rootType, new List<string>(), rootType, results);
            return results;
        }

        private void EnumerateRecursive(Type cmdType, List<string> parentSegments, Type rootType, List<McpToolDescriptor> results)
        {
            var attr = Attribute.GetCustomAttribute(cmdType, typeof(DotMake.CommandLine.CliCommandAttribute)) as DotMake.CommandLine.CliCommandAttribute;
            if (attr == null) return;

            // Skip commands (and their entire sub-tree) marked as not relevant for MCP.
            if (Attribute.IsDefined(cmdType, typeof(McpIgnoreAttribute))) return;
            var cliCommandNameResolver = new CliCommandNameResolver();
            string name = cliCommandNameResolver.ResolveCommandName(cmdType, attr);
            bool isRoot = cmdType == rootType;
            var segments = isRoot ? new List<string>() : new List<string>(parentSegments) { name };
            var fullName = string.Join("_", segments);

            // Find children via attribute
            var childrenViaAttribute = attr.Children ?? Array.Empty<Type>();
            // Find children via nested types
            var childrenViaNested = cmdType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(t => Attribute.GetCustomAttribute(t, typeof(DotMake.CommandLine.CliCommandAttribute)) != null)
                .ToArray();
            var allChildren = childrenViaAttribute.Concat(childrenViaNested).ToArray();
            bool hasChildren = allChildren.Length > 0;

            // Only register as MCP tool if not root and has no children
            if (!isRoot && !hasChildren)
            {
                results.Add(new McpToolDescriptor
                {
                    Name = fullName,
                    Description = attr.Description,
                    CliCommandClass = cmdType,
                    Annotations = BuildAnnotations(cmdType)
                });
            }

            // Traverse all children
            foreach (var child in allChildren)
            {
                EnumerateRecursive(child, segments, rootType, results);
            }
        }
        /// <summary>
        /// Reads <see cref="CliDestructiveAttribute"/>, <see cref="CliReadOnlyAttribute"/>,
        /// and <see cref="CliIdempotentAttribute"/> from the command type and converts them
        /// to an MCP protocol <see cref="ToolAnnotations"/> instance. Returns null when none
        /// of the attributes are present.
        /// </summary>
        private static ToolAnnotations? BuildAnnotations(Type cmdType)
        {
            var destructive = cmdType.GetCustomAttribute<CliDestructiveAttribute>();
            var readOnly = cmdType.GetCustomAttribute<CliReadOnlyAttribute>();
            var idempotent = cmdType.GetCustomAttribute<CliIdempotentAttribute>();

            if (destructive is null && readOnly is null && idempotent is null)
                return null;

            return new ToolAnnotations
            {
                Title = destructive?.Impact,
                DestructiveHint = destructive is not null ? true : null,
                ReadOnlyHint = readOnly is not null ? true : null,
                IdempotentHint = idempotent is not null ? true : null,
            };
        }

        public Type? FindCommandTypeByToolName(string toolName, Type rootType)
        {
            var segments = toolName.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return FindCommandTypeBySegments(segments, 0, rootType, skipRoot: true);
        }

        private Type? FindCommandTypeBySegments(string[] segments, int index, Type type, bool skipRoot = false)
        {
            var attr = Attribute.GetCustomAttribute(type, typeof(DotMake.CommandLine.CliCommandAttribute)) as DotMake.CommandLine.CliCommandAttribute;
            if (attr == null) return null;
            var cliCommandNameResolver = new CliCommandNameResolver();
            string cmdName = cliCommandNameResolver.ResolveCommandName(type, attr);

            // Only compare segment if not skipping root
            if (!skipRoot) {
                if (!string.Equals(cmdName, segments[index], StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            // Find children via attribute
            var childrenViaAttribute = attr.Children ?? Array.Empty<Type>();
            // Find children via nested types
            var childrenViaNested = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(t => Attribute.GetCustomAttribute(t, typeof(DotMake.CommandLine.CliCommandAttribute)) != null)
                .ToArray();
            var allChildren = childrenViaAttribute.Concat(childrenViaNested).ToArray();

            if (index == segments.Length - 1 && allChildren.Length == 0)
                return type;

            if (allChildren.Length > 0)
            {
                foreach (var child in allChildren)
                {
                    var found = FindCommandTypeBySegments(segments, skipRoot ? index : index + 1, child, false);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
