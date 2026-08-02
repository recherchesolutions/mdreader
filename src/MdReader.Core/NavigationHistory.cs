namespace MdReader.Core;

/// <summary>
/// Per-tab back/forward history for document jumps (TOC selections, in-document
/// anchors, Ctrl+G). Positions are source lines. Normal scrolling is never
/// recorded — only deliberate jumps enter history, mirroring browser behavior.
/// </summary>
public sealed class NavigationHistory
{
    public const int MaxDepth = 100;

    private readonly List<int> _back = [];
    private readonly List<int> _forward = [];

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>Records the position being jumped away from; a new jump clears the forward stack.</summary>
    public void RecordJump(int fromLine)
    {
        // Consecutive jumps from the same spot collapse into one entry.
        if (_back.Count > 0 && _back[^1] == fromLine)
        {
            _forward.Clear();
            return;
        }

        _back.Add(fromLine);
        if (_back.Count > MaxDepth)
        {
            _back.RemoveAt(0);
        }

        _forward.Clear();
    }

    /// <summary>Returns the line to go back to, or null. The current position goes onto the forward stack.</summary>
    public int? GoBack(int currentLine)
    {
        if (_back.Count == 0)
        {
            return null;
        }

        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(currentLine);
        if (_forward.Count > MaxDepth)
        {
            _forward.RemoveAt(0);
        }

        return target;
    }

    /// <summary>Returns the line to go forward to, or null. The current position goes onto the back stack.</summary>
    public int? GoForward(int currentLine)
    {
        if (_forward.Count == 0)
        {
            return null;
        }

        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(currentLine);
        if (_back.Count > MaxDepth)
        {
            _back.RemoveAt(0);
        }

        return target;
    }
}
