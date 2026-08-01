using System.Text;
using System.Windows;

namespace MdReader.App.Services;

/// <summary>
/// Puts HTML on the clipboard in CF_HTML format so pasting into Word or Outlook
/// keeps formatting. CF_HTML requires a header with exact BYTE offsets into the
/// UTF-8 payload — the fiddly part this class exists to get right.
/// </summary>
public static class ClipboardHtml
{
    public static void SetHtml(string fragmentHtml, string plainTextFallback, string? inlineCss = null)
    {
        var cfHtml = BuildCfHtml(fragmentHtml, inlineCss);
        var data = new DataObject();
        data.SetData(DataFormats.Html, cfHtml);
        data.SetData(DataFormats.UnicodeText, plainTextFallback);
        Clipboard.SetDataObject(data, copy: true);
    }

    public static string BuildCfHtml(string fragmentHtml, string? inlineCss = null)
    {
        var style = inlineCss is null ? string.Empty : $"<style>{inlineCss}</style>";
        var pre = $"<html><head>{style}</head><body><!--StartFragment-->";
        const string Post = "<!--EndFragment--></body></html>";

        const string HeaderTemplate =
            "Version:0.9\r\n" +
            "StartHTML:{0:D10}\r\n" +
            "EndHTML:{1:D10}\r\n" +
            "StartFragment:{2:D10}\r\n" +
            "EndFragment:{3:D10}\r\n";

        // Header length is fixed because every number is zero-padded to 10 digits.
        var headerLength = string.Format(HeaderTemplate, 0, 0, 0, 0).Length;

        var preBytes = Encoding.UTF8.GetByteCount(pre);
        var fragmentBytes = Encoding.UTF8.GetByteCount(fragmentHtml);
        var postBytes = Encoding.UTF8.GetByteCount(Post);

        var startHtml = headerLength;
        var startFragment = startHtml + preBytes;
        var endFragment = startFragment + fragmentBytes;
        var endHtml = endFragment + postBytes;

        return string.Format(HeaderTemplate, startHtml, endHtml, startFragment, endFragment)
            + pre + fragmentHtml + Post;
    }
}
