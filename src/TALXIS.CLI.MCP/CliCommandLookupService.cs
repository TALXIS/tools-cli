using System.Reflection;

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
                    CliCommandClass = cmdType
                });
            }

            // Traverse all children
            foreach (var child in allChildren)
            {
                EnumerateRecursive(child, segments, rootType, results);
            }
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
