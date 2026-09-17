import { useState } from "react";

function buildDocument(code) {
  const safeScript = String(code.js || "").replace(/<\/script/gi, "<\\/script");
  return `<!doctype html>
<html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data: blob:; font-src data:; connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'">
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>${String(code.css || "")}</style></head>
<body>${String(code.html || "")}<script>"use strict";\n${safeScript}</script></body></html>`;
}

export default function SandboxedWidgetPreview({ code, title = "Custom widget preview" }) {
  const [srcDoc, setSrcDoc] = useState("");
  if (!srcDoc) return (
    <div className="widget-preview-gate">
      <span>Code is never run automatically.</span>
      <button type="button" onClick={() => setSrcDoc(buildDocument(code || {}))}>Run sandboxed preview</button>
    </div>
  );
  return (
    <div className="widget-preview-running">
      <button type="button" onClick={() => setSrcDoc(buildDocument(code || {}))}>Refresh preview</button>
      <iframe className="widget-sandbox" title={title} sandbox="allow-scripts" srcDoc={srcDoc} referrerPolicy="no-referrer" />
    </div>
  );
}
