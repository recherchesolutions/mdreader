/* mdreader — editor.js
 *
 * Hosts the Monaco editor for Source mode. Talks to the WPF host over the
 * chrome.webview bridge: content in, edits/saves out, plus scroll-position
 * mapping so Ctrl+E lands on the same line the reader was showing.
 */
(function () {
  "use strict";

  var host = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  var editor = null;
  var suppressChangeEvents = false;
  var pending = []; // messages that arrived before Monaco finished loading

  function post(msg) {
    if (host) {
      host.postMessage(msg);
    }
  }

  // Monaco's AMD loader; everything is served from the bundled vendor folder.
  window.require.config({ paths: { vs: "/vendor/monaco/vs" } });

  window.require(["vs/editor/editor.main"], function () {
    editor = monaco.editor.create(document.getElementById("editor"), {
      language: "markdown",
      wordWrap: "on",
      lineNumbers: "on",
      matchBrackets: "always",
      minimap: { enabled: false },
      renderWhitespace: "none",
      fontFamily: "Cascadia Code, Consolas, monospace",
      fontSize: 14,
      scrollBeyondLastLine: false,
      automaticLayout: true,
      unicodeHighlight: { ambiguousCharacters: false },
      occurrencesHighlight: "off",
    });

    editor.onDidChangeModelContent(function () {
      if (!suppressChangeEvents) {
        post({ type: "contentChanged" });
      }
    });

    var scrollTimer = null;
    editor.onDidScrollChange(function () {
      if (scrollTimer) { return; }
      scrollTimer = setTimeout(function () {
        scrollTimer = null;
        post({ type: "scrollChanged", line: topVisibleLine() });
      }, 120);
    });

    // Ctrl+S inside Monaco routes to the host's save command.
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, function () {
      post({ type: "saveRequested" });
    });
    // Ctrl+E toggles back to Reader; the host owns mode switching.
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyE, function () {
      post({ type: "toggleModeRequested" });
    });

    // Markdown-aware Tab: inside a list item, Tab/Shift+Tab indent/outdent the
    // line instead of inserting a literal tab character.
    editor.addCommand(monaco.KeyCode.Tab, function () {
      if (inListLine()) {
        editor.trigger("mdreader", "editor.action.indentLines", null);
      } else {
        editor.trigger("keyboard", "tab", null);
      }
    }, "!suggestWidgetVisible && !inSnippetMode");
    editor.addCommand(monaco.KeyMod.Shift | monaco.KeyCode.Tab, function () {
      if (inListLine()) {
        editor.trigger("mdreader", "editor.action.outdentLines", null);
      } else {
        editor.trigger("keyboard", "outdent", null);
      }
    }, "!suggestWidgetVisible && !inSnippetMode");

    // App-level shortcuts not owned by Monaco route to the host.
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyE, function () {
      post({ type: "shortcut", name: "toggleSplit" });
    });
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyO, function () {
      post({ type: "shortcut", name: "openFile" });
    });
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyW, function () {
      post({ type: "shortcut", name: "closeTab" });
    });
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Equal, function () {
      post({ type: "shortcut", name: "zoomIn" });
    });
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Minus, function () {
      post({ type: "shortcut", name: "zoomOut" });
    });
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Digit0, function () {
      post({ type: "shortcut", name: "zoomReset" });
    });

    post({ type: "ready" });
    pending.forEach(handleMessage);
    pending = [];
  });

  function inListLine() {
    if (!editor) { return false; }
    var pos = editor.getPosition();
    if (!pos) { return false; }
    var line = editor.getModel().getLineContent(pos.lineNumber);
    return /^\s*(?:[-*+]|\d+[.)])\s/.test(line);
  }

  function topVisibleLine() {
    if (!editor) { return 1; }
    var ranges = editor.getVisibleRanges();
    return ranges.length ? ranges[0].startLineNumber : 1;
  }

  /* ------------------------------------------------------------------ *
   * Host messages
   * ------------------------------------------------------------------ */
  if (host) {
    host.addEventListener("message", function (e) {
      var msg = e.data;
      if (!msg || typeof msg.type !== "string") { return; }
      if (!editor) { pending.push(msg); return; }
      handleMessage(msg);
    });
  }

  function handleMessage(msg) {
    switch (msg.type) {
      case "setContent": {
        suppressChangeEvents = true;
        try {
          var model = editor.getModel();
          model.setEOL(msg.eol === "\n" ? monaco.editor.EndOfLineSequence.LF : monaco.editor.EndOfLineSequence.CRLF);
          model.setValue(msg.text);
          model.setEOL(msg.eol === "\n" ? monaco.editor.EndOfLineSequence.LF : monaco.editor.EndOfLineSequence.CRLF);
        } finally {
          suppressChangeEvents = false;
        }
        if (msg.line) { revealLine(msg.line); }
        break;
      }
      case "requestContent":
        post({ type: "content", text: editor.getValue(), requestId: msg.requestId || null });
        break;
      case "scrollToLine":
        revealLine(msg.line);
        break;
      case "requestScrollLine":
        post({ type: "scrollLine", line: topVisibleLine() });
        break;
      case "setTheme":
        monaco.editor.setTheme(msg.theme === "high-contrast" ? "hc-black" : (msg.theme === "dark" ? "vs-dark" : "vs"));
        break;
      case "find":
        editor.focus();
        editor.trigger("mdreader", "actions.find", null);
        break;
      case "focus":
        editor.focus();
        break;
      case "insertText": {
        var selection = editor.getSelection();
        editor.executeEdits("mdreader", [{ range: selection, text: msg.text || "", forceMoveMarkers: true }]);
        editor.pushUndoStop();
        editor.focus();
        break;
      }
    }
  }

  function revealLine(line) {
    var n = Math.max(1, line | 0);
    editor.revealLineNearTop(n);
    editor.setPosition({ lineNumber: n, column: 1 });
  }
})();
