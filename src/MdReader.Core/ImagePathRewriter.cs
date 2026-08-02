using AngleSharp.Dom;

namespace MdReader.Core;

/// <summary>
/// Post-sanitization pass over the document DOM that enforces the image policy:
/// relative paths resolve against the document's directory (refused if they climb
/// more than N parent levels), remote images are blocked into placeholders unless
/// explicitly allowed, and everything local is routed through the read-only
/// document virtual host.
/// </summary>
public static class ImagePathRewriter
{
    /// <summary>
    /// Rewrites img elements in place. Returns the folder the document virtual
    /// host must map to, or null when no local images were resolved.
    /// </summary>
    public static string? Rewrite(IElement body, RenderOptions options)
    {
        string? documentRoot = null;
        var documentDirectory = options.DocumentPath is null
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(options.DocumentPath));

        foreach (var img in body.QuerySelectorAll("img"))
        {
            var src = img.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(src))
            {
                continue;
            }

            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.AllowRemoteImages)
                {
                    // Blocked remote image: keep the URL for the per-document
                    // "Load remote images" action, but never let it load silently.
                    img.SetAttribute("data-remote-src", src);
                    img.RemoveAttribute("src");
                    img.ClassList.Add("remote-blocked");
                }

                continue;
            }

            if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                continue; // explicit local URL; CSP permits file: images
            }

            if (src.StartsWith('#') || src.Contains("://", StringComparison.Ordinal))
            {
                continue; // anchors and other schemes are not images we resolve
            }

            if (documentDirectory is null)
            {
                continue; // unsaved buffer: nothing to resolve against
            }

            documentRoot ??= ComputeAllowedRoot(documentDirectory, options.MaxImagePathParentLevels);

            var refused = true;
            try
            {
                var unescaped = Uri.UnescapeDataString(src.Split('?')[0].Split('#')[0]);
                // Path.GetFullPath silently clamps excess ".." segments at the
                // drive root, which would defeat the containment check below, so
                // parent traversals are counted explicitly first.
                var resolved = Path.GetFullPath(Path.Combine(documentDirectory, unescaped.Replace('/', Path.DirectorySeparatorChar)));
                if (!Path.IsPathRooted(unescaped) &&
                    CountParentTraversals(unescaped) <= options.MaxImagePathParentLevels &&
                    IsWithinRoot(resolved, documentRoot))
                {
                    if (!File.Exists(resolved))
                    {
                        img.SetAttribute("data-missing-src", src);
                        img.SetAttribute("title", $"Missing local image: {src}");
                        img.ClassList.Add("local-missing");
                    }

                    if (options.KeepRelativeImagePaths)
                    {
                        // Export path: the original relative src stays valid
                        // next to the document in a real browser.
                    }
                    else
                    {
                        var relative = Path.GetRelativePath(documentRoot, resolved).Replace(Path.DirectorySeparatorChar, '/');
                        img.SetAttribute("data-local-path", resolved);
                        img.SetAttribute("src", VirtualHosts.DocumentOrigin + "/" + Uri.EscapeDataString(relative).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase));
                    }

                    refused = false;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                // Invalid path characters etc. — treat as refused below.
            }

            if (refused)
            {
                img.SetAttribute("data-refused-src", src);
                img.RemoveAttribute("src");
                img.ClassList.Add("path-refused");
            }
        }

        return documentRoot;
    }

    /// <summary>
    /// The deepest ancestor a relative path is allowed to reach: the document's
    /// directory plus at most <paramref name="parentLevels"/> levels up.
    /// </summary>
    public static string ComputeAllowedRoot(string documentDirectory, int parentLevels)
    {
        var root = new DirectoryInfo(documentDirectory);
        for (var i = 0; i < parentLevels && root.Parent is not null; i++)
        {
            root = root.Parent;
        }

        return root.FullName;
    }

    /// <summary>
    /// The net number of levels a relative path climbs above its starting
    /// directory (0 when it never leaves it). "a/../../b" climbs 1.
    /// </summary>
    internal static int CountParentTraversals(string relativePath)
    {
        var depth = 0;
        var minDepth = 0;
        foreach (var segment in relativePath.Split('/', '\\'))
        {
            switch (segment)
            {
                case "" or ".":
                    continue;
                case "..":
                    depth--;
                    minDepth = Math.Min(minDepth, depth);
                    break;
                default:
                    depth++;
                    break;
            }
        }

        return -minDepth;
    }

    internal static bool IsWithinRoot(string fullPath, string root)
    {
        // Note: TrimEndingDirectorySeparator does not trim drive roots ("C:\"),
        // so build the separator-terminated prefix explicitly.
        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
    }
}
