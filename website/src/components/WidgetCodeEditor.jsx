import { useState } from "react";
import { sampleWidgetCode } from "../lib/designSchema";

const tabs = [
  { key: "html", label: "HTML" },
  { key: "css", label: "CSS" },
  { key: "js", label: "JavaScript" }
];

export default function WidgetCodeEditor({ value, onChange }) {
  const [active, setActive] = useState("html");
  return (
    <div className="widget-ide">
      <div className="ide-toolbar">
        <div role="tablist" aria-label="Widget source files">{tabs.map((tab) => (
          <button type="button" role="tab" aria-selected={active === tab.key} className={active === tab.key ? "active" : ""} onClick={() => setActive(tab.key)} key={tab.key}>{tab.label}</button>
        ))}</div>
        <button type="button" className="load-sample" onClick={() => onChange({ ...sampleWidgetCode })}>Load sample</button>
      </div>
      <label className="sr-only" htmlFor={`widget-code-${active}`}>{tabs.find((tab) => tab.key === active)?.label} code</label>
      <textarea
        id={`widget-code-${active}`}
        value={value[active]}
        onChange={(event) => onChange({ ...value, [active]: event.target.value })}
        spellCheck="false"
        autoCapitalize="off"
        autoCorrect="off"
      />
      <footer><span>Sandboxed preview</span><span>{value[active].length.toLocaleString()} characters</span></footer>
    </div>
  );
}
