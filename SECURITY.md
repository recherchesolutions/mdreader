# Security

## Reporting a vulnerability

Please report security issues privately via
**GitHub Security Advisories** (Security tab → "Report a vulnerability") on
this repository. If that is not possible, open an issue asking for a private
contact channel — do not include exploit details in a public issue.

You can expect an acknowledgement within a week. Fixes ship as a patch release
with credit unless you prefer otherwise.

## Threat model

mdreader renders markdown files that arrive from repositories, downloads, LLM
output, and email attachments. **Every document is treated as untrusted
input.** The interesting attacker is a document author trying to execute code,
exfiltrate data, or mislead the reader — not a local attacker with the user's
privileges.

### Defenses, in layers

1. **Raw HTML is escaped by default.** The default Markdig pipeline renders
   raw HTML in documents as visible text. Rendering raw HTML is a per-document
   opt-in — and the sanitizer below still runs even then.
2. **Allowlist sanitization.** All rendered HTML passes through
   [HtmlSanitizer](https://github.com/mganss/HtmlSanitizer) configured with an
   explicit allowlist of tags and attributes
   ([MarkdownSanitizer.cs](src/MdReader.Core/MarkdownSanitizer.cs)). `script`,
   `iframe`, `object`, `embed`, `form`, `style`, `link`, `meta`, `base`, event
   handlers (`on*`), and `javascript:`/`vbscript:`/`data:` URIs are stripped.
   The only `<input>` allowed to survive is a disabled task-list checkbox.
3. **Content Security Policy.** The reader page runs with
   `default-src 'none'; script-src 'self'; connect-src 'none'` (see
   [reader.html](src/MdReader.Web/reader.html) for the documented, minimal
   relaxations). Document content cannot run script or phone home even if
   something got past the sanitizer.
4. **Remote resources blocked by default.** Remote images are a read-receipt/
   tracking vector; they render as placeholders until the user opts in (per
   document or globally). The opt-in swaps to a page whose CSP admits
   `img-src http: https:` — scripts stay impossible.
5. **Navigation interception.** The WebView never navigates away from the
   app's own pages. Link clicks are routed to the host: `http`/`https`/
   `mailto` open in the default browser, in-document anchors scroll, and every
   other scheme (`file:`, `ms-*`, custom protocols) is refused with a visible
   notice.
6. **Path containment.** Relative image paths resolve against the document's
   directory and are refused if they climb more than 3 parent levels
   (configurable). Parent-traversal counting is explicit — `Path.GetFullPath`'s
   silent clamping at the drive root is not trusted. Local content is served
   through read-only WebView2 virtual host mappings scoped to the app's asset
   folder and the document's root — never the whole filesystem.
7. **WebView2 hardening.** DevTools disabled in release builds, autofill and
   password saving disabled, browser accelerator keys disabled, new-window
   requests suppressed, host objects disallowed.
8. **Mermaid** runs with `securityLevel: 'strict'` and `htmlLabels: false`.
9. **No elevation.** The app never requests administrator rights; the
   installer defaults to per-user. File-type registration is additive
   (`OpenWithProgids`) and never writes the hash-protected `UserChoice` key.

### Known accepted risks

- The sanitizer and CSP defend the renderer; they do not stop a document from
  *displaying* misleading text. Phishing-style content is out of scope.
- `mdreader-convert` output opens in the user's browser without mdreader's
  navigation interception; it carries a strict CSP instead.

## Verification

The test suite includes an XSS corpus ([fixtures/xss/](fixtures/xss/)) covering
the standard markdown vectors — `javascript:` links, `onerror` images, raw
`<script>`/`<svg onload>`, HTML-entity-encoded and case-mixed variants — each
asserted structurally (DOM inspection, not string matching) in both pipeline
modes. See [XssSecurityTests.cs](tests/MdReader.Core.Tests/XssSecurityTests.cs).
