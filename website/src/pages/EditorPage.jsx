import { useEffect, useMemo, useRef, useState } from "react";
import StudioShell from "../components/StudioShell";
import GlassBarPreview from "../components/GlassBarPreview";
import { cloneDesign, defaultDesign, downloadPackage, normalizeDesign } from "../lib/designSchema";
import { createStudioRepository } from "../lib/studioRepository";
import { useStudioAuth } from "../auth/StudioAuth";

const kindCopy = {
  glassbar: "Shape the full bar",
  widget: "Build one focused widget",
  animation: "Tune an ambient animation"
};
const allowedAssets = ["image/png", "image/jpeg", "image/webp", "image/gif"];

function RangeControl({ label, value, min, max, unit = "", onChange }) {
  return (
    <label className="range-control">
      <span><b>{label}</b><output>{value}{unit}</output></span>
      <input aria-label={label} type="range" min={min} max={max} value={value} onChange={(event) => onChange(Number(event.target.value))} />
    </label>
  );
}

function ColorControl({ label, value, onChange }) {
  return <label className="color-control"><span>{label}</span><input type="color" value={value} onChange={(event) => onChange(event.target.value)} /><code>{value}</code></label>;
}

function Segmented({ value, options, onChange, label }) {
  return (
    <fieldset className="segmented"><legend>{label}</legend><div>{options.map((option) => (
      <button type="button" className={value === option.value ? "active" : ""} onClick={() => onChange(option.value)} key={option.value}>{option.label}</button>
    ))}</div></fieldset>
  );
}

export default function EditorPage() {
  const auth = useStudioAuth();
  const repository = useMemo(() => createStudioRepository(auth.getIdToken), [auth.getIdToken]);
  const importRef = useRef(null);
  const [design, setDesign] = useState(() => {
    try { return normalizeDesign(JSON.parse(localStorage.getItem("glassbar-studio-draft"))); }
    catch { return cloneDesign(defaultDesign); }
  });
  const [asset, setAsset] = useState(null);
  const [message, setMessage] = useState("");
  const [publishing, setPublishing] = useState(false);

  useEffect(() => { localStorage.setItem("glassbar-studio-draft", JSON.stringify(design)); }, [design]);

  function setKind(kind) {
    setDesign((current) => ({
      ...current,
      kind,
      metadata: { ...current.metadata, name: current.metadata.name.startsWith("Untitled") ? `Untitled ${kind === "glassbar" ? "GlassBar" : kind[0].toUpperCase() + kind.slice(1)}` : current.metadata.name }
    }));
  }

  function update(section, key, value) {
    setDesign((current) => ({ ...current, [section]: { ...current[section], [key]: value } }));
  }

  function toggleModule(module) {
    const active = design.glassbar.modules.includes(module);
    update("glassbar", "modules", active ? design.glassbar.modules.filter((item) => item !== module) : [...design.glassbar.modules, module]);
  }

  function moveModule(module, direction) {
    const modules = [...design.glassbar.modules];
    const index = modules.indexOf(module);
    const next = index + direction;
    if (index < 0 || next < 0 || next >= modules.length) return;
    [modules[index], modules[next]] = [modules[next], modules[index]];
    update("glassbar", "modules", modules);
  }

  async function importPackage(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file || file.size > 200_000) return setMessage("That package is too large.");
    try {
      setDesign(normalizeDesign(JSON.parse(await file.text())));
      setMessage("Package imported into the editor.");
    } catch { setMessage("That file is not a valid GlassBar package."); }
  }

  function chooseAsset(event) {
    const file = event.target.files?.[0] || null;
    if (!file) return setAsset(null);
    if (!allowedAssets.includes(file.type) || file.size > 5_000_000) {
      event.target.value = "";
      setAsset(null);
      return setMessage("Preview assets must be PNG, JPG, WebP, or GIF under 5 MB.");
    }
    setAsset(file);
    setMessage(`${file.name} will be submitted with this design.`);
  }

  async function publish() {
    setMessage("");
    if (!auth.configured || !repository) return setMessage("Connect Auth0 and Supabase before publishing. Local editing and exports still work.");
    if (!auth.authenticated) return auth.signup();
    if (!auth.mfaVerified) return setMessage("Publishing requires a fresh two-factor sign-in. Complete email or SMS MFA, then return here.");
    setPublishing(true);
    try {
      await repository.publish({ design, user: auth.user, username: auth.user?.nickname, asset });
      setMessage("Submitted for review. It will appear in the library after moderation.");
    } catch (error) { setMessage(error.message || "The design could not be published."); }
    finally { setPublishing(false); }
  }

  return (
    <StudioShell eyebrow="PACKAGE EDITOR" title={kindCopy[design.kind]}>
      <div className="editor-kind-tabs" role="tablist" aria-label="Design type">
        {["glassbar", "widget", "animation"].map((kind) => <button role="tab" aria-selected={design.kind === kind} className={design.kind === kind ? "active" : ""} onClick={() => setKind(kind)} key={kind}>{kind === "glassbar" ? "GlassBar" : `${kind[0].toUpperCase()}${kind.slice(1)}`}</button>)}
      </div>

      <div className="editor-layout">
        <aside className="editor-controls" aria-label="Design controls">
          <section className="control-section">
            <h2>Package</h2>
            <label className="field-label">Name<input maxLength="60" value={design.metadata.name} onChange={(event) => update("metadata", "name", event.target.value)} /></label>
            <label className="field-label">Description<textarea maxLength="180" value={design.metadata.summary} onChange={(event) => update("metadata", "summary", event.target.value)} /></label>
            <label className="field-label">Tags<input value={design.metadata.tags.join(", ")} onChange={(event) => update("metadata", "tags", event.target.value.split(",").map((tag) => tag.trim()).filter(Boolean).slice(0, 8))} /></label>
          </section>

          {design.kind === "glassbar" && <>
            <section className="control-section"><h2>Frame</h2>
              <Segmented label="Direction" value={design.glassbar.orientation} options={[{ value: "horizontal", label: "Horizontal" }, { value: "vertical", label: "Vertical" }]} onChange={(value) => update("glassbar", "orientation", value)} />
              <RangeControl label="Length" value={design.glassbar.width} min={520} max={1100} unit="px" onChange={(value) => update("glassbar", "width", value)} />
              <RangeControl label="Thickness" value={design.glassbar.height} min={58} max={94} unit="px" onChange={(value) => update("glassbar", "height", value)} />
              <RangeControl label="Corner radius" value={design.glassbar.radius} min={4} max={40} unit="px" onChange={(value) => update("glassbar", "radius", value)} />
              <RangeControl label="Opacity" value={design.glassbar.opacity} min={35} max={100} unit="%" onChange={(value) => update("glassbar", "opacity", value)} />
              <RangeControl label="Blur" value={design.glassbar.blur} min={0} max={40} unit="px" onChange={(value) => update("glassbar", "blur", value)} />
            </section>
            <section className="control-section"><h2>Material</h2>
              <ColorControl label="Start" value={design.glassbar.backgroundStart} onChange={(value) => update("glassbar", "backgroundStart", value)} />
              <ColorControl label="End" value={design.glassbar.backgroundEnd} onChange={(value) => update("glassbar", "backgroundEnd", value)} />
              <ColorControl label="Accent" value={design.glassbar.accent} onChange={(value) => update("glassbar", "accent", value)} />
              <ColorControl label="Border" value={design.glassbar.border} onChange={(value) => update("glassbar", "border", value)} />
              <RangeControl label="Shadow" value={design.glassbar.shadow} min={0} max={80} unit="px" onChange={(value) => update("glassbar", "shadow", value)} />
            </section>
            <section className="control-section"><h2>Modules</h2><p className="section-note">Show, hide, and reorder the bar’s building blocks.</p>
              <div className="module-list">{["start", "search", "apps", "tray", "clock"].map((module) => {
                const active = design.glassbar.modules.includes(module);
                return <div className={!active ? "disabled" : ""} key={module}><label><input type="checkbox" checked={active} onChange={() => toggleModule(module)} />{module}</label><span><button onClick={() => moveModule(module, -1)} aria-label={`Move ${module} left`}>←</button><button onClick={() => moveModule(module, 1)} aria-label={`Move ${module} right`}>→</button></span></div>;
              })}</div>
            </section>
          </>}

          {design.kind === "widget" && <section className="control-section"><h2>Widget</h2>
            <label className="field-label">Title<input value={design.widget.title} onChange={(event) => update("widget", "title", event.target.value)} /></label>
            <label className="field-label">Main value<input value={design.widget.value} onChange={(event) => update("widget", "value", event.target.value)} /></label>
            <label className="field-label">Detail<input value={design.widget.detail} onChange={(event) => update("widget", "detail", event.target.value)} /></label>
            <label className="field-label">Safe data source<select value={design.widget.dataSource} onChange={(event) => update("widget", "dataSource", event.target.value)}><option value="static">Static text</option><option value="clock">Clock</option><option value="date">Date</option><option value="focus">Focus timer</option><option value="weather">Weather</option><option value="system">System status</option><option value="media">Media</option></select></label>
            <Segmented label="Icon" value={design.widget.icon} options={[{ value: "timer", label: "Timer" }, { value: "weather", label: "Weather" }, { value: "pulse", label: "Pulse" }]} onChange={(value) => update("widget", "icon", value)} />
            <RangeControl label="Width" value={design.widget.width} min={110} max={280} unit="px" onChange={(value) => update("widget", "width", value)} />
            <RangeControl label="Corner radius" value={design.widget.radius} min={4} max={32} unit="px" onChange={(value) => update("widget", "radius", value)} />
            <ColorControl label="Background" value={design.widget.background} onChange={(value) => update("widget", "background", value)} />
            <ColorControl label="Accent" value={design.widget.accent} onChange={(value) => update("widget", "accent", value)} />
          </section>}

          {design.kind === "animation" && <section className="control-section"><h2>Animation</h2>
            <Segmented label="Shape" value={design.animation.shape} options={[{ value: "line", label: "Line" }, { value: "orb", label: "Orb" }, { value: "spark", label: "Spark" }]} onChange={(value) => update("animation", "shape", value)} />
            <Segmented label="Motion" value={design.animation.motion} options={[{ value: "fall", label: "Fall" }, { value: "rise", label: "Rise" }, { value: "drift", label: "Drift" }]} onChange={(value) => update("animation", "motion", value)} />
            <RangeControl label="Density" value={design.animation.density} min={5} max={100} unit="%" onChange={(value) => update("animation", "density", value)} />
            <RangeControl label="Speed" value={design.animation.speed} min={5} max={100} unit="%" onChange={(value) => update("animation", "speed", value)} />
            <RangeControl label="Size" value={design.animation.size} min={5} max={100} unit="%" onChange={(value) => update("animation", "size", value)} />
            <RangeControl label="Glow" value={design.animation.glow} min={0} max={100} unit="%" onChange={(value) => update("animation", "glow", value)} />
            <RangeControl label="Trail" value={design.animation.trail} min={0} max={100} unit="%" onChange={(value) => update("animation", "trail", value)} />
            <ColorControl label="Primary" value={design.animation.primaryColor} onChange={(value) => update("animation", "primaryColor", value)} />
            <ColorControl label="Secondary" value={design.animation.secondaryColor} onChange={(value) => update("animation", "secondaryColor", value)} />
          </section>}
        </aside>

        <section className="editor-preview-panel">
          <div className="preview-toolbar"><span>LIVE PREVIEW</span><span>Changes save locally</span></div>
          <GlassBarPreview design={design} />
          <div className="package-actions">
            <button className="secondary-button" onClick={() => importRef.current?.click()}>Import JSON</button>
            <button className="secondary-button" onClick={() => downloadPackage(design)}>Export package</button>
            <input ref={importRef} type="file" accept="application/json,.json" onChange={importPackage} hidden />
          </div>
          <div className="publish-card">
            <div><span className="status-dot" /><h2>Publish to Community</h2><p>New submissions are format-validated and held for moderation before going public.</p></div>
            <label className="asset-picker">Optional preview image or GIF<input type="file" accept="image/png,image/jpeg,image/webp,image/gif" onChange={chooseAsset} /></label>
            <button className="primary-button" onClick={publish} disabled={publishing}>{publishing ? "Submitting…" : auth.authenticated ? "Submit design" : "Sign up to publish"}</button>
            {message && <p className="editor-message" role="status">{message}</p>}
          </div>
        </section>
      </div>
    </StudioShell>
  );
}
