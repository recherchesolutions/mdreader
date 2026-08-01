namespace MdReader.Core;

/// <summary>
/// The two WebView2 virtual host names. Assets (reader.html, css, js, vendor
/// libraries) are served read-only from the install folder; document images are
/// served read-only from the document's root folder. Keeping them on separate
/// origins means document content can never read app assets or vice versa.
/// </summary>
public static class VirtualHosts
{
    public const string Assets = "mdreader-assets";
    public const string Document = "mdreader-doc";

    public const string AssetsOrigin = "https://" + Assets;
    public const string DocumentOrigin = "https://" + Document;
}
