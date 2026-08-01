namespace MdReader.Core;

/// <summary>Markdown file extensions the app knows about.</summary>
public static class FileTypes
{
    /// <summary>Registered by default.</summary>
    public static readonly string[] DefaultExtensions = [".md", ".markdown"];

    /// <summary>Opt-in extensions (installer checkboxes and settings). ".mdx" renders as plain markdown with JSX shown as-is.</summary>
    public static readonly string[] OptionalExtensions = [".mdown", ".mkd", ".mkdn", ".mdtxt", ".mdtext", ".mdx"];

    public static bool IsMarkdown(string path)
    {
        var ext = Path.GetExtension(path);
        return DefaultExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)
            || OptionalExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
