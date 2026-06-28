using System.Text;
using System.Text.RegularExpressions;

namespace TALXIS.CLI.Features.Workspace;

/// <summary>
/// Decides whether a file inside the workspace should be excluded from
/// validation. Wraps two layers of rules:
/// <list type="number">
///   <item>A built-in list of well-known throwaway directories
///         (<c>node_modules</c>, <c>bin</c>, <c>obj</c>, ...). Always
///         active unless the caller opts out.</item>
///   <item>Patterns parsed from a root-level <c>.gitignore</c>, when one
///         exists. Subset of the gitignore spec — enough to cover the
///         common "skip everything under foo/" pattern, deliberately
///         skips negations (<c>!pattern</c>).</item>
/// </list>
/// </summary>
internal sealed class WorkspaceFileFilter
{
    /// <summary>
    /// Throwaway directory names that should be skipped by default. Common
    /// across .NET, Node.js, IDE, and build-output directories — none of
    /// these ever contain hand-authored Power Platform metadata.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultIgnoredDirectories = new[]
    {
        "node_modules",
        "bin",
        "obj",
        "out",
        "dist",
        "coverage",
        ".git",
        ".vs",
        ".idea",
        ".vscode",
        ".cache",
        ".nuget",
    };

    private readonly string _workspaceRoot;
    private readonly HashSet<string> _ignoredDirNames;
    private readonly List<GitignoreRule> _gitignoreRules;
    private readonly bool _skipNodeProjects;
    private readonly Dictionary<string, bool> _nodeProjectCache;

    public WorkspaceFileFilter(string workspaceRoot, bool applyDefaults, bool readGitignore, bool skipNodeProjects = true)
    {
        _workspaceRoot = NormalizeDirectory(workspaceRoot);
        _ignoredDirNames = applyDefaults
            ? new HashSet<string>(DefaultIgnoredDirectories, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _gitignoreRules = new List<GitignoreRule>();
        _skipNodeProjects = skipNodeProjects;
        _nodeProjectCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (readGitignore)
        {
            var gitignorePath = Path.Combine(_workspaceRoot, ".gitignore");
            if (File.Exists(gitignorePath))
                LoadGitignore(gitignorePath);
        }
    }

    /// <summary>
    /// Number of patterns parsed from <c>.gitignore</c>. Useful for verbose
    /// logging.
    /// </summary>
    public int GitignorePatternCount => _gitignoreRules.Count;

    /// <summary>
    /// <c>true</c> when <paramref name="absolutePath"/> falls under any of
    /// the configured ignore rules.
    /// </summary>
    public bool IsIgnored(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return false;

        // Quick check: any path component is a default-ignored directory.
        if (_ignoredDirNames.Count > 0)
        {
            var relative = GetRelativePath(absolutePath);
            foreach (var segment in SplitSegments(relative))
            {
                if (_ignoredDirNames.Contains(segment))
                    return true;
            }
        }

        if (_gitignoreRules.Count > 0)
        {
            var relative = GetRelativePath(absolutePath).Replace('\\', '/');
            foreach (var rule in _gitignoreRules)
            {
                if (rule.Matches(relative))
                    return true;
            }
        }

        // Node/TypeScript project trees: if any ancestor of the file
        // contains a package.json, treat the whole subtree as not-our-stuff.
        // Covers tsconfig.json, package-lock.json, .eslintrc.json, etc.
        // without having to maintain a basename allowlist.
        if (_skipNodeProjects && IsInsideNodeProject(absolutePath))
            return true;

        return false;
    }

    private bool IsInsideNodeProject(string absolutePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(absolutePath));
        var root = _workspaceRoot;

        while (!string.IsNullOrEmpty(dir))
        {
            if (_nodeProjectCache.TryGetValue(dir, out var cached))
                return cached;

            bool isNodeProject = File.Exists(Path.Combine(dir, "package.json"));
            _nodeProjectCache[dir] = isNodeProject;
            if (isNodeProject)
                return true;

            // Stop at the workspace root — we don't want to walk above it.
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
                return false;

            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                return false;
            dir = parent;
        }
        return false;
    }

    private string GetRelativePath(string absolutePath)
    {
        if (Path.IsPathRooted(absolutePath))
        {
            try
            {
                return Path.GetRelativePath(_workspaceRoot, absolutePath);
            }
            catch (ArgumentException)
            {
                return absolutePath;
            }
        }
        return absolutePath;
    }

    private static IEnumerable<string> SplitSegments(string path)
    {
        foreach (var segment in path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            yield return segment;
    }

    private static string NormalizeDirectory(string dir)
        => Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void LoadGitignore(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            // Negations are intentionally unsupported. Treat them as no-ops
            // rather than try to invert prior matches.
            if (line.StartsWith('!'))
                continue;

            var rule = GitignoreRule.TryParse(line);
            if (rule is not null)
                _gitignoreRules.Add(rule);
        }
    }

    /// <summary>
    /// Single parsed line from a <c>.gitignore</c>. Implements a subset of
    /// the gitignore spec that covers the most common patterns:
    /// directory-only suffix, leading-slash anchoring, <c>**</c> wildcards.
    /// </summary>
    private sealed class GitignoreRule
    {
        private readonly Regex _regex;
        private readonly bool _directoryOnly;

        private GitignoreRule(Regex regex, bool directoryOnly)
        {
            _regex = regex;
            _directoryOnly = directoryOnly;
        }

        public bool Matches(string relativePath)
        {
            if (_directoryOnly)
            {
                // For "pattern/" patterns, match if the rule hits any
                // directory portion of the path. We don't know from the
                // path alone whether a leaf is a file or a directory, so
                // require the match to consume a path segment followed by
                // either end-of-string or another slash.
                var match = _regex.Match(relativePath);
                while (match.Success)
                {
                    var end = match.Index + match.Length;
                    if (end == relativePath.Length || relativePath[end] == '/')
                        return true;
                    match = match.NextMatch();
                }
                return false;
            }
            return _regex.IsMatch(relativePath);
        }

        public static GitignoreRule? TryParse(string line)
        {
            var directoryOnly = false;
            if (line.EndsWith('/'))
            {
                directoryOnly = true;
                line = line.TrimEnd('/');
            }

            if (line.Length == 0)
                return null;

            var anchored = line.StartsWith('/');
            if (anchored)
                line = line.TrimStart('/');

            var pattern = BuildRegex(line, anchored);
            try
            {
                var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                return new GitignoreRule(regex, directoryOnly);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string BuildRegex(string glob, bool anchored)
        {
            var sb = new StringBuilder();
            sb.Append('^');
            if (!anchored && !glob.Contains('/'))
            {
                // Floating name pattern: matches anywhere in the path.
                sb.Append("(.*/)?");
            }
            else if (!anchored)
            {
                // Floating path pattern (e.g. "src/foo"): allow any prefix.
                sb.Append("(.*/)?");
            }

            for (int i = 0; i < glob.Length; i++)
            {
                var c = glob[i];
                if (c == '*')
                {
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        // ** — any number of path segments.
                        sb.Append(".*");
                        i++;
                        // Consume a following slash if present, so "**/foo" matches "foo" too.
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                            i++;
                    }
                    else
                    {
                        // * — any chars except '/'.
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                }
                else if (c == '.' || c == '+' || c == '(' || c == ')' || c == '|' || c == '^' || c == '$'
                         || c == '{' || c == '}' || c == '[' || c == ']' || c == '\\')
                {
                    sb.Append('\\').Append(c);
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.Append("(/.*)?$");
            return sb.ToString();
        }
    }
}
