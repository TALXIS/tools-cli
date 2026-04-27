#pragma warning disable MCPEXP001

using ModelContextProtocol.Protocol;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Manages the set of currently active MCP tools (always-on + dynamically injected).
/// Thread-safe for concurrent access from guide, list_tools, and call_tools handlers.
/// </summary>
public class ActiveToolSet
{
    private readonly object _lock = new();

    /// <summary>
    /// Always-on tools that cannot be removed. Keyed by tool name.
    /// </summary>
    private readonly Dictionary<string, Tool> _alwaysOn = new();

    /// <summary>
    /// Dynamically injected tools from guide calls. Ordered by insertion for LRU eviction.
    /// </summary>
    private readonly LinkedList<(string Name, Tool Tool)> _injected = new();
    private readonly Dictionary<string, LinkedListNode<(string Name, Tool Tool)>> _injectedIndex = new();

    /// <summary>
    /// Maximum number of injected tools before LRU eviction kicks in.
    /// </summary>
    public int MaxInjectedTools { get; set; } = 40;

    /// <summary>
    /// Registers an always-on tool. These are never removed by eviction.
    /// </summary>
    public void AddAlwaysOn(Tool tool)
    {
        lock (_lock)
        {
            _alwaysOn[tool.Name] = tool;
        }
    }

    /// <summary>
    /// Injects tools discovered by a guide call. These become visible when clients
    /// re-fetch tools/list on subsequent turns.
    /// Uses LRU eviction when the injected set exceeds <see cref="MaxInjectedTools"/>.
    /// </summary>
    public void InjectTools(IEnumerable<Tool> tools)
    {
        lock (_lock)
        {
            foreach (var tool in tools)
            {
                // Don't inject if it's an always-on tool
                if (_alwaysOn.ContainsKey(tool.Name))
                    continue;

                // If already injected, move to end (most recently used)
                if (_injectedIndex.TryGetValue(tool.Name, out var existingNode))
                {
                    _injected.Remove(existingNode);
                    _injectedIndex.Remove(tool.Name);
                }

                // Add to end of list (most recently used)
                var node = _injected.AddLast((tool.Name, tool));
                _injectedIndex[tool.Name] = node;
            }

            // Evict oldest injected tools if over cap
            while (_injected.Count > MaxInjectedTools && _injected.First is not null)
            {
                var oldest = _injected.First.Value;
                _injected.RemoveFirst();
                _injectedIndex.Remove(oldest.Name);
            }
        }
    }

    /// <summary>
    /// Returns all currently active tools (always-on + injected).
    /// Used by ListToolsAsync handler.
    /// </summary>
    public List<Tool> ListActiveTools()
    {
        lock (_lock)
        {
            var result = new List<Tool>(_alwaysOn.Count + _injected.Count);
            result.AddRange(_alwaysOn.Values);
            result.AddRange(_injected.Select(n => n.Tool));
            return result;
        }
    }

    /// <summary>
    /// Checks if a tool name is in the active set (always-on or injected).
    /// </summary>
    public bool IsActive(string toolName)
    {
        lock (_lock)
        {
            return _alwaysOn.ContainsKey(toolName) || _injectedIndex.ContainsKey(toolName);
        }
    }

    /// <summary>
    /// Total number of currently active tools.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _alwaysOn.Count + _injected.Count;
            }
        }
    }

    /// <summary>
    /// Number of always-on tools.
    /// </summary>
    public int AlwaysOnCount
    {
        get
        {
            lock (_lock)
            {
                return _alwaysOn.Count;
            }
        }
    }

    /// <summary>
    /// Number of dynamically injected tools.
    /// </summary>
    public int InjectedCount
    {
        get
        {
            lock (_lock)
            {
                return _injected.Count;
            }
        }
    }
}
