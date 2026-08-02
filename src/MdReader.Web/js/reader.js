/* mdreader — reader.js
 *
 * Runs inside the WebView2 reader document. All document HTML arriving here has
 * already been sanitized by the host; this script only enhances it (syntax
 * highlighting, diagrams, math, copy buttons, TOC, find) and talks to the host
 * over the chrome.webview message bridge. It never navigates the page: link
 * clicks are intercepted and routed to the host.
 */
(function () {
  "use strict";

  var host = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  var content = document.getElementById("content");
  var tocEl = document.getElementById("toc");

  var state = {
    headings: [],
    theme: "light",
    largeDoc: false,
    tocOpen: false,
    tocEligible: false,
    find: { query: "", marks: [], index: -1 },
    mermaidCounter: 0,
  };

  function post(msg) {
    if (host) {
      host.postMessage(msg);
    }
  }

  /* ------------------------------------------------------------------ *
   * Host message handling
   * ------------------------------------------------------------------ */
  if (host) {
    host.addEventListener("message", function (e) {
      var msg = e.data;
      if (!msg || typeof msg.type !== "string") { return; }
      switch (msg.type) {
        case "setBody": setBody(msg); break;
        case "setTheme": setTheme(msg.theme); break;
        case "setFont": setFont(msg); break;
        case "scrollToLine": scrollToSourceLine(msg.line); break;
        case "requestScrollLine": post({ type: "scrollLine", line: topVisibleSourceLine() }); break;
        case "setToc": setTocOpen(!!msg.open, !!msg.focus); break;
        case "find": handleFind(msg); break;
        case "scrollToAnchor": scrollToAnchor(msg.id); break;
        case "setCustomCss": setCustomCss(msg.css || ""); break;
      }
    });
  }

  /* ------------------------------------------------------------------ *
   * Body swap — re-renders replace only the content, never the page
   * ------------------------------------------------------------------ */
  function setBody(msg) {
    var restoreLine = msg.preserveScroll ? topVisibleSourceLine() : null;
    clearFind();

    content.innerHTML = msg.html;
    state.headings = msg.headings || [];
    state.largeDoc = !!msg.largeDoc;

    enhanceCodeBlocks();
    var mermaidDone = maybeRenderMermaid();
    var mathDone = maybeRenderMath();
    wrapTables();
    buildImagePlaceholders();
    buildToc();

    // Export and tests wait for this: everything asynchronous is finished.
    Promise.all([mermaidDone, mathDone]).then(function () {
      post({ type: "bodyRendered" });
    });

    if (restoreLine !== null) {
      scrollToSourceLine(restoreLine);
    } else if (msg.scrollToLine) {
      scrollToSourceLine(msg.scrollToLine);
    } else {
      window.scrollTo(0, 0);
    }

    if (state.largeDoc) {
      showNotice("Large document — syntax highlighting deferred.", 4000);
    }
  }

  /* ------------------------------------------------------------------ *
   * Code blocks: highlight.js + hover copy button
   * ------------------------------------------------------------------ */
  function enhanceCodeBlocks() {
    var blocks = content.querySelectorAll("pre > code");
    blocks.forEach(function (code) {
      var lang = languageOf(code);
      if (lang === "mermaid" || lang === "math") { return; }

      // Copy button (always, even when highlighting is deferred).
      var pre = code.parentElement;
      var wrap = document.createElement("div");
      wrap.className = "code-block-wrap";
      pre.parentNode.insertBefore(wrap, pre);
      wrap.appendChild(pre);

      var btn = document.createElement("button");
      btn.type = "button";
      btn.className = "copy-code";
      btn.textContent = "Copy";
      btn.addEventListener("click", function () {
        copyText(code.innerText).then(function () {
          btn.textContent = "Copied";
          setTimeout(function () { btn.textContent = "Copy"; }, 1500);
        });
      });
      wrap.appendChild(btn);

      if (!state.largeDoc && window.hljs) {
        if (lang && window.hljs.getLanguage(lang)) {
          code.classList.add("language-" + lang);
          window.hljs.highlightElement(code);
        } else if (lang) {
          // Unknown language: leave as plain text rather than guessing wrong.
        } else {
          // No language info: cheap auto-detect only for short blocks.
          if (code.textContent.length < 5000) {
            window.hljs.highlightElement(code);
          }
        }
      }
    });
  }

  function languageOf(code) {
    var m = /(?:^|\s)language-([\w+-]+)/.exec(code.className);
    return m ? m[1].toLowerCase() : null;
  }

  function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text).catch(function () { return fallbackCopy(text); });
    }
    return Promise.resolve(fallbackCopy(text));
  }

  function fallbackCopy(text) {
    var ta = document.createElement("textarea");
    ta.value = text;
    ta.style.position = "fixed";
    ta.style.opacity = "0";
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand("copy"); } finally { ta.remove(); }
  }

  /* ------------------------------------------------------------------ *
   * Lazy vendor loading — mermaid (3.4MB) and katex (1MB) parse slowly, so
   * they load only when a document actually needs them
   * ------------------------------------------------------------------ */
  var vendorLoads = {};

  function loadVendorScript(src) {
    if (!vendorLoads[src]) {
      vendorLoads[src] = new Promise(function (resolve, reject) {
        var script = document.createElement("script");
        script.src = src;
        script.onload = resolve;
        script.onerror = function () { reject(new Error("failed to load " + src)); };
        document.head.appendChild(script);
      });
    }
    return vendorLoads[src];
  }

  function maybeRenderMermaid() {
    var needed = content.querySelector("pre > code.language-mermaid, .mermaid-diagram[data-mermaid-source]");
    if (!needed) { return Promise.resolve(); }
    return loadVendorScript("/vendor/mermaid/mermaid.min.js")
      .then(renderMermaid)
      .catch(function () { return undefined; });
  }

  function maybeRenderMath() {
    var needed = content.querySelector("span.math, div.math");
    if (!needed) { return Promise.resolve(); }
    return loadVendorScript("/vendor/katex/katex.min.js")
      .then(renderMath)
      .catch(function () { return undefined; });
  }

  /* ------------------------------------------------------------------ *
   * Mermaid — strict security, graceful failure
   * ------------------------------------------------------------------ */
  function renderMermaid() {
    if (!window.mermaid) { return Promise.resolve(); }
    window.mermaid.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      htmlLabels: false,
      theme: state.theme === "dark" ? "dark" : "default",
    });

    // Fresh blocks are <pre><code class="language-mermaid">; blocks already
    // rendered once carry their source on the container so a theme change can
    // re-render them with the matching mermaid theme.
    var pending = [];
    content.querySelectorAll(".mermaid-diagram[data-mermaid-source]").forEach(function (container) {
      pending.push(renderOneMermaid(container, container.getAttribute("data-mermaid-source"), null));
    });

    var blocks = content.querySelectorAll("pre > code.language-mermaid");
    blocks.forEach(function (code) {
      var pre = code.parentElement;
      var source = code.textContent;
      var container = document.createElement("div");
      container.className = "mermaid-diagram";
      container.setAttribute("data-mermaid-source", source);
      pending.push(renderOneMermaid(container, source, pre));
    });

    return Promise.all(pending);
  }

  function renderOneMermaid(container, source, preToReplace) {
    var id = "mermaid-" + (++state.mermaidCounter);
    return window.mermaid
      .render(id, source)
      .then(function (result) {
        container.innerHTML = result.svg;
        if (preToReplace) { preToReplace.replaceWith(container); }
      })
        .catch(function (err) {
        // Never a blank space: keep the code block, add an inline error note.
        var note = document.createElement("p");
        note.className = "mermaid-error";
        note.textContent = "Mermaid diagram failed to render: " + (err && err.message ? err.message : err);
        var anchor = preToReplace || container;
        if (anchor.parentNode && !anchor.nextElementSibling?.classList?.contains("mermaid-error")) {
          anchor.insertAdjacentElement("afterend", note);
        }
        // Mermaid can leave an orphaned temp element behind on failure.
        var temp = document.getElementById("d" + id) || document.getElementById(id);
        if (temp && !container.contains(temp)) { temp.remove(); }
      });
  }

  /* ------------------------------------------------------------------ *
   * Math — Markdig emits <span class="math">\(…\)</span> / <div class="math">\[…\]</div>
   * ------------------------------------------------------------------ */
  function renderMath() {
    if (!window.katex) { return; }
    var nodes = content.querySelectorAll("span.math, div.math");
    nodes.forEach(function (el) {
      var tex = el.textContent;
      var display = el.tagName === "DIV";
      tex = tex.replace(/^\s*\\[([]/, "").replace(/\\[)\]]\s*$/, "");
      try {
        window.katex.render(tex, el, { displayMode: display, throwOnError: true });
      } catch (err) {
        el.textContent = tex;
        el.title = "KaTeX: " + (err && err.message ? err.message : err);
      }
    });
  }

  /* ------------------------------------------------------------------ *
   * Tables get a horizontal scroll container so they never break layout
   * ------------------------------------------------------------------ */
  function wrapTables() {
    content.querySelectorAll("table").forEach(function (table) {
      if (table.closest(".table-wrap") || table.closest("details.front-matter")) { return; }
      var wrap = document.createElement("div");
      wrap.className = "table-wrap";
      table.parentNode.insertBefore(wrap, table);
      wrap.appendChild(table);
    });
  }

  /* ------------------------------------------------------------------ *
   * Blocked images → visible placeholders with a per-document action
   * ------------------------------------------------------------------ */
  function buildImagePlaceholders() {
    content.querySelectorAll("img.remote-blocked").forEach(function (img) {
      var url = img.getAttribute("data-remote-src") || "";
      var box = document.createElement("span");
      box.className = "image-placeholder";
      var label = document.createElement("span");
      label.textContent = "Remote image blocked: " + url;
      var btn = document.createElement("button");
      btn.type = "button";
      btn.textContent = "Load remote images in this document";
      btn.addEventListener("click", function () { post({ type: "requestRemoteImages" }); });
      box.appendChild(label);
      box.appendChild(btn);
      img.replaceWith(box);
    });

    content.querySelectorAll("img.path-refused").forEach(function (img) {
      var src = img.getAttribute("data-refused-src") || "";
      var box = document.createElement("span");
      box.className = "image-placeholder";
      box.textContent = "Image path refused (escapes the allowed document root): " + src;
      img.replaceWith(box);
    });
  }

  /* ------------------------------------------------------------------ *
   * Links: everything goes to the host; in-document anchors scroll here
   * ------------------------------------------------------------------ */
  document.addEventListener("click", function (e) {
    var a = e.target && e.target.closest ? e.target.closest("a[href]") : null;
    if (!a) { return; }
    e.preventDefault();
    var href = a.getAttribute("href") || "";
    if (href.charAt(0) === "#") {
      // Deliberate jump: tell the host where we came from (back/forward history).
      post({ type: "jumped", from: topVisibleSourceLine() });
      scrollToAnchor(decodeURIComponent(href.slice(1)));
      return;
    }
    post({ type: "link", href: href });
  });

  function scrollToAnchor(id) {
    if (!id) { return; }
    var target = document.getElementById(id);
    if (target) {
      target.scrollIntoView({ block: "start" });
    }
  }

  /* ------------------------------------------------------------------ *
   * Scroll ↔ source line mapping (data-source-line anchors)
   * ------------------------------------------------------------------ */
  function anchorElements() {
    return content.querySelectorAll("[data-source-line]");
  }

  function topVisibleSourceLine() {
    var els = anchorElements();
    var best = null;
    for (var i = 0; i < els.length; i++) {
      var rect = els[i].getBoundingClientRect();
      if (rect.bottom >= 8) { best = els[i]; break; }
    }
    if (!best && els.length) { best = els[els.length - 1]; }
    return best ? parseInt(best.getAttribute("data-source-line"), 10) || 1 : 1;
  }

  function scrollToSourceLine(line) {
    var els = anchorElements();
    var best = null;
    var bestLine = -1;
    for (var i = 0; i < els.length; i++) {
      var l = parseInt(els[i].getAttribute("data-source-line"), 10);
      if (isNaN(l)) { continue; }
      if (l <= line && l > bestLine) { best = els[i]; bestLine = l; }
      if (l > line) { break; }
    }
    if (!best && els.length) { best = els[0]; }
    if (best) {
      var y = best.getBoundingClientRect().top + window.scrollY - 16;
      window.scrollTo(0, Math.max(0, y));
    }
  }

  var scrollTimer = null;
  window.addEventListener("scroll", function () {
    if (scrollTimer) { return; }
    scrollTimer = setTimeout(function () {
      scrollTimer = null;
      post({ type: "scrollChanged", line: topVisibleSourceLine() });
      updateActiveTocEntry();
    }, 120);
  }, { passive: true });

  /* ------------------------------------------------------------------ *
   * Table of contents rail — auto-hidden under 3 headings
   * ------------------------------------------------------------------ */
  function buildToc() {
    tocEl.innerHTML = "";
    state.tocEligible = state.headings.length >= 3;
    if (!state.tocEligible) {
      setTocOpen(false);
      post({ type: "tocEligibility", eligible: false });
      return;
    }
    post({ type: "tocEligibility", eligible: true });

    var title = document.createElement("h2");
    title.textContent = "Contents";
    tocEl.appendChild(title);

    var ul = document.createElement("ul");
    state.headings.forEach(function (h) {
      if (!h.id) { return; }
      var li = document.createElement("li");
      li.setAttribute("data-level", String(h.level));
      var a = document.createElement("a");
      a.href = "#" + h.id;
      a.textContent = h.text;
      a.title = h.text;
      li.appendChild(a);
      ul.appendChild(li);
    });
    tocEl.appendChild(ul);
  }

  function setTocOpen(open, focus) {
    state.tocOpen = open && state.tocEligible;
    document.body.classList.toggle("toc-open", state.tocOpen);
    updateActiveTocEntry();
    if (state.tocOpen && focus) {
      var target = tocEl.querySelector("a.active") || tocEl.querySelector("a");
      if (target) { target.focus(); }
    }
  }

  // Keyboard navigation inside the TOC rail: arrows move, Enter follows
  // (native link behavior), Escape returns focus to the document.
  tocEl.addEventListener("keydown", function (e) {
    var links = Array.prototype.slice.call(tocEl.querySelectorAll("a"));
    var index = links.indexOf(document.activeElement);
    if (e.key === "ArrowDown" && index < links.length - 1) {
      e.preventDefault(); links[index + 1].focus();
    } else if (e.key === "ArrowUp" && index > 0) {
      e.preventDefault(); links[index - 1].focus();
    } else if (e.key === "Escape") {
      e.preventDefault();
      setTocOpen(false, false);
      post({ type: "tocClosed" });
      window.focus();
    }
  });

  function updateActiveTocEntry() {
    if (!state.tocOpen) { return; }
    var line = topVisibleSourceLine();
    var currentId = null;
    for (var i = 0; i < state.headings.length; i++) {
      if (state.headings[i].line <= line) { currentId = state.headings[i].id; } else { break; }
    }
    tocEl.querySelectorAll("a").forEach(function (a) {
      a.classList.toggle("active", currentId !== null && a.getAttribute("href") === "#" + currentId);
    });
  }

  /* ------------------------------------------------------------------ *
   * Find in Reader mode: highlight all, navigate current
   * ------------------------------------------------------------------ */
  function handleFind(msg) {
    switch (msg.action) {
      case "start": startFind(msg.query || "", !!msg.matchCase); break;
      case "next": moveFind(1); break;
      case "prev": moveFind(-1); break;
      case "clear": clearFind(); post({ type: "findResult", total: 0, current: 0 }); break;
    }
  }

  function startFind(query, matchCase) {
    clearFind();
    state.find.query = query;
    if (!query) { post({ type: "findResult", total: 0, current: 0 }); return; }

    var walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT, {
      acceptNode: function (node) {
        if (!node.nodeValue || !node.nodeValue.trim()) { return NodeFilter.FILTER_REJECT; }
        var p = node.parentElement;
        if (p && (p.closest("script") || p.closest("style"))) { return NodeFilter.FILTER_REJECT; }
        return NodeFilter.FILTER_ACCEPT;
      },
    });

    var needle = matchCase ? query : query.toLowerCase();
    var textNodes = [];
    while (walker.nextNode()) { textNodes.push(walker.currentNode); }

    textNodes.forEach(function (node) {
      var hay = matchCase ? node.nodeValue : node.nodeValue.toLowerCase();
      var idx = hay.indexOf(needle);
      if (idx === -1) { return; }

      var current = node;
      while (idx !== -1) {
        var match = current.splitText(idx);
        var rest = match.splitText(query.length);
        var mark = document.createElement("mark");
        mark.className = "find-hit";
        match.parentNode.insertBefore(mark, match);
        mark.appendChild(match);
        state.find.marks.push(mark);
        current = rest;
        hay = matchCase ? current.nodeValue : current.nodeValue.toLowerCase();
        idx = hay.indexOf(needle);
      }
    });

    state.find.index = state.find.marks.length ? 0 : -1;
    updateCurrentMark();
  }

  function moveFind(delta) {
    var n = state.find.marks.length;
    if (!n) { post({ type: "findResult", total: 0, current: 0 }); return; }
    state.find.index = ((state.find.index + delta) % n + n) % n;
    updateCurrentMark();
  }

  function updateCurrentMark() {
    state.find.marks.forEach(function (m, i) {
      m.classList.toggle("find-hit-current", i === state.find.index);
    });
    var current = state.find.marks[state.find.index];
    if (current) {
      current.scrollIntoView({ block: "center" });
    }
    post({ type: "findResult", total: state.find.marks.length, current: state.find.index + 1 });
  }

  function clearFind() {
    state.find.marks.forEach(function (mark) {
      var parent = mark.parentNode;
      if (!parent) { return; }
      while (mark.firstChild) { parent.insertBefore(mark.firstChild, mark); }
      parent.removeChild(mark);
      parent.normalize();
    });
    state.find.marks = [];
    state.find.index = -1;
    state.find.query = "";
  }

  /* ------------------------------------------------------------------ *
   * Theme / font overrides
   * ------------------------------------------------------------------ */
  function setTheme(theme) {
    state.theme = theme === "dark" || theme === "high-contrast" ? theme : "light";
    document.documentElement.setAttribute("data-theme", state.theme);
    // Mermaid bakes its theme into rendered SVG, so re-render diagrams.
    maybeRenderMermaid();
  }

  /* ------------------------------------------------------------------ *
   * Custom theme (a CSS file from %APPDATA%\mdreader\themes)
   * ------------------------------------------------------------------ */
  function setCustomCss(css) {
    var el = document.getElementById("custom-theme");
    if (!el) {
      el = document.createElement("style");
      el.id = "custom-theme";
      document.head.appendChild(el);
    }
    el.textContent = css;
  }

  function setFont(msg) {
    var root = document.documentElement;
    if (msg.family) { root.style.setProperty("--font-prose", msg.family); }
    else { root.style.removeProperty("--font-prose"); }
    if (msg.size) { root.style.setProperty("--font-size", msg.size + "px"); }
    else { root.style.removeProperty("--font-size"); }
    if (msg.contentWidth) { root.style.setProperty("--content-width", msg.contentWidth + "px"); }
    else { root.style.removeProperty("--content-width"); }
    if (msg.lineSpacing) { root.style.setProperty("--line-height", msg.lineSpacing); }
    if (msg.paragraphSpacing) { root.style.setProperty("--paragraph-spacing", msg.paragraphSpacing + "em"); }
  }

  /* ------------------------------------------------------------------ *
   * Transient notice
   * ------------------------------------------------------------------ */
  var noticeTimer = null;
  function showNotice(text, ms) {
    var existing = document.querySelector(".reader-notice");
    if (existing) { existing.remove(); }
    var div = document.createElement("div");
    div.className = "reader-notice";
    div.textContent = text;
    document.body.appendChild(div);
    if (noticeTimer) { clearTimeout(noticeTimer); }
    noticeTimer = setTimeout(function () { div.remove(); }, ms || 3000);
  }

  /* ------------------------------------------------------------------ *
   * Keyboard shortcuts — the WebView has focus, so app-level shortcuts
   * are captured here and routed to the host
   * ------------------------------------------------------------------ */
  document.addEventListener("keydown", function (e) {
    // Alt+Left / Alt+Right: back / forward through jump history.
    if (e.altKey && !e.ctrlKey && !e.shiftKey) {
      if (e.key === "ArrowLeft") { e.preventDefault(); post({ type: "shortcut", name: "navBack" }); }
      if (e.key === "ArrowRight") { e.preventDefault(); post({ type: "shortcut", name: "navForward" }); }
      return;
    }
    if (!e.ctrlKey || e.altKey) { return; }
    var name = null;
    switch (e.key.toLowerCase()) {
      case "e": name = e.shiftKey ? "toggleSplit" : "toggleMode"; break;
      case "o": name = e.shiftKey ? "toggleToc" : "openFile"; break;
      case "f": if (!e.shiftKey) { name = "find"; } break;
      case "g": if (!e.shiftKey) { name = "goTo"; } break;
      case "s": if (!e.shiftKey) { name = "save"; } break;
      case "p": if (!e.shiftKey) { name = "print"; } break;
      case "w": if (!e.shiftKey) { name = "closeTab"; } break;
      case "=": case "+": name = "zoomIn"; break;
      case "-": name = "zoomOut"; break;
      case "0": name = "zoomReset"; break;
    }
    if (name) {
      e.preventDefault();
      post({ type: "shortcut", name: name });
    }
  });

  post({ type: "ready" });
})();
