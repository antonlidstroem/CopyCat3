using CopyCat.Models;

namespace CopyCat.Services;

/// <summary>
/// Builds a hierarchical FileTreeNode tree from a flat list of file paths,
/// and provides helpers to flatten it into a display list respecting expansion state.
/// </summary>
public static class FileTreeBuilder
{
    // ── Tree construction ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a tree of FileTreeNode objects from a flat file list.
    /// Folder nodes are created on demand as path segments are encountered.
    /// File order is preserved (caller should sort before passing).
    /// </summary>
    public static List<FileTreeNode> Build(List<(string Path, string Content)> files)
    {
        var roots     = new List<FileTreeNode>();
        var folderMap = new Dictionary<string, FileTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, content) in files)
        {
            var normalised = path.Replace('\\', '/');
            var segments   = normalised.Split('/');

            FileTreeNode? parent = null;
            var currentPath = string.Empty;

            // Walk all but the last segment (filename), creating folder nodes
            for (int i = 0; i < segments.Length - 1; i++)
            {
                currentPath = i == 0 ? segments[i] : $"{currentPath}/{segments[i]}";

                if (!folderMap.TryGetValue(currentPath, out var folder))
                {
                    folder = new FileTreeNode
                    {
                        Name      = segments[i],
                        FullPath  = currentPath,
                        IsFolder  = true,
                        Depth     = i,
                        Parent    = parent,
                        IsExpanded = i == 0,   // root-level folders start expanded
                    };
                    folderMap[currentPath] = folder;

                    if (parent is null) roots.Add(folder);
                    else parent.Children.Add(folder);
                }
                parent = folder;
            }

            // Create the leaf file node
            var fileName = segments[^1];
            var fileNode = new FileTreeNode
            {
                Name     = fileName,
                FullPath = normalised,
                IsFolder = false,
                Depth    = segments.Length - 1,
                Parent   = parent,
                Content  = content,
            };

            if (parent is null) roots.Add(fileNode);
            else parent.Children.Add(fileNode);
        }

        return roots;
    }

    // ── Tree flattening ────────────────────────────────────────────────────

    /// <summary>
    /// Returns a flat list of ONLY the currently-visible nodes, honouring IsExpanded on all folders.
    /// Root nodes are always included. A node's children are included only when IsExpanded is true.
    /// </summary>
    public static List<FileTreeNode> Flatten(IEnumerable<FileTreeNode> roots)
    {
        var result = new List<FileTreeNode>();
        AppendVisible(roots, result);
        return result;
    }

    private static void AppendVisible(IEnumerable<FileTreeNode> nodes, List<FileTreeNode> result)
    {
        foreach (var node in nodes)
        {
            result.Add(node);
            if (node.IsFolder && node.IsExpanded)
                AppendVisible(node.Children, result);
        }
    }

    // ── Exclusion helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Recursively sets IsExcluded on all descendants of a folder node.
    /// Does not update the folder node itself (caller does that).
    /// </summary>
    public static void SetDescendantsExcluded(FileTreeNode folder, bool excluded)
    {
        foreach (var child in folder.Children)
        {
            child.IsExcluded          = excluded;
            child.IsPartiallyExcluded = false;
            if (child.IsFolder)
                SetDescendantsExcluded(child, excluded);
        }
    }

    /// <summary>
    /// Walks up the parent chain and recalculates IsPartiallyExcluded / IsExcluded
    /// for each ancestor folder based on its children's states.
    /// Call this after toggling a leaf file node.
    /// </summary>
    public static void UpdateAncestorState(FileTreeNode node)
    {
        var parent = node.Parent;
        while (parent is not null)
        {
            RecalculateFolderState(parent);
            parent = parent.Parent;
        }
    }

    /// <summary>
    /// Recalculates a folder's own exclusion state from its children.
    /// All excluded → IsExcluded=true, IsPartiallyExcluded=false
    /// None excluded → IsExcluded=false, IsPartiallyExcluded=false
    /// Mixed         → IsExcluded=false, IsPartiallyExcluded=true
    /// </summary>
    public static void RecalculateFolderState(FileTreeNode folder)
    {
        if (!folder.IsFolder || folder.Children.Count == 0) return;

        bool allExcluded  = folder.Children.All(c => c.IsExcluded);
        bool noneExcluded = folder.Children.All(c => !c.IsExcluded && !c.IsPartiallyExcluded);

        folder.IsExcluded          = allExcluded;
        folder.IsPartiallyExcluded = !allExcluded && !noneExcluded;
    }

    // ── File collection helpers ────────────────────────────────────────────

    /// <summary>
    /// Returns all leaf (file) nodes in the tree, regardless of visibility.
    /// </summary>
    public static IEnumerable<FileTreeNode> AllFileNodes(IEnumerable<FileTreeNode> roots)
    {
        foreach (var node in roots)
        {
            if (!node.IsFolder)
                yield return node;
            else
                foreach (var child in AllFileNodes(node.Children))
                    yield return child;
        }
    }

    /// <summary>
    /// Returns all folder nodes in the tree, regardless of visibility.
    /// </summary>
    public static IEnumerable<FileTreeNode> AllFolderNodes(IEnumerable<FileTreeNode> roots)
    {
        foreach (var node in roots)
        {
            if (node.IsFolder)
            {
                yield return node;
                foreach (var child in AllFolderNodes(node.Children))
                    yield return child;
            }
        }
    }
}
