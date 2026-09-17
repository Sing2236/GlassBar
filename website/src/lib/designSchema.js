export const DESIGN_KINDS = ["glassbar", "widget", "animation"];

export const sampleWidgetCode = {
  html: `<div class="focus-widget">
  <span class="pulse" aria-hidden="true"></span>
  <div>
    <small>FOCUS MODE</small>
    <strong id="clock">--:--:--</strong>
  </div>
</div>`,
  css: `:root { color-scheme: dark; font-family: Inter, "Segoe UI", sans-serif; }
* { box-sizing: border-box; }
body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: transparent; color: #f8fafc; }
.focus-widget { min-width: 210px; display: flex; align-items: center; gap: 14px; padding: 14px 18px; border: 1px solid rgba(255,255,255,.18); border-radius: 18px; background: rgba(17, 28, 43, .82); box-shadow: inset 0 1px rgba(255,255,255,.12); }
.pulse { width: 14px; height: 14px; border-radius: 50%; background: #67e8f9; box-shadow: 0 0 0 0 rgba(103,232,249,.55); animation: breathe 1.8s ease-out infinite; }
small, strong { display: block; }
small { color: #8fa5b8; font-size: 9px; letter-spacing: .16em; }
strong { margin-top: 3px; font-size: 22px; font-variant-numeric: tabular-nums; }
@keyframes breathe { 70% { box-shadow: 0 0 0 11px rgba(103,232,249,0); } 100% { box-shadow: 0 0 0 0 rgba(103,232,249,0); } }`,
  js: `const clock = document.querySelector("#clock");
function updateClock() {
  clock.textContent = new Intl.DateTimeFormat([], {
    hour: "2-digit", minute: "2-digit", second: "2-digit"
  }).format(new Date());
}
updateClock();
setInterval(updateClock, 1000);`
};

export const defaultDesign = {
  schemaVersion: 1,
  kind: "glassbar",
  metadata: {
    name: "Untitled GlassBar",
    summary: "A custom GlassBar community design.",
    tags: ["clean", "glass"]
  },
  glassbar: {
    orientation: "horizontal",
    width: 760,
    height: 70,
    radius: 22,
    opacity: 88,
    blur: 22,
    backgroundMode: "glass",
    effectMode: "ambient",
    backgroundStart: "#132233",
    backgroundEnd: "#111720",
    accent: "#60d7ff",
    border: "#ffffff",
    shadow: 36,
    modules: ["start", "search", "apps", "tray", "clock"]
  },
  widget: {
    mode: "visual",
    title: "Focus",
    value: "25:00",
    detail: "Deep work",
    icon: "timer",
    dataSource: "focus",
    width: 164,
    radius: 18,
    accent: "#7dd3fc",
    background: "#172235",
    showDivider: true,
    code: sampleWidgetCode
  },
  animation: {
    name: "Soft rain",
    shape: "line",
    motion: "fall",
    density: 48,
    speed: 56,
    size: 42,
    glow: 28,
    trail: 62,
    primaryColor: "#93e1ff",
    secondaryColor: "#8b5cf6"
  }
};

export function cloneDesign(design = defaultDesign) {
  return JSON.parse(JSON.stringify(design));
}

export function normalizeDesign(input) {
  const source = input && typeof input === "object" ? input : {};
  const merged = cloneDesign();
  merged.schemaVersion = 1;
  merged.kind = DESIGN_KINDS.includes(source.kind) ? source.kind : "glassbar";
  merged.metadata = { ...merged.metadata, ...(source.metadata || {}) };
  merged.glassbar = { ...merged.glassbar, ...(source.glassbar || {}) };
  merged.widget = { ...merged.widget, ...(source.widget || {}) };
  merged.widget.code = { ...sampleWidgetCode, ...(source.widget?.code || {}) };
  merged.animation = { ...merged.animation, ...(source.animation || {}) };
  merged.glassbar.modules = Array.isArray(merged.glassbar.modules)
    ? merged.glassbar.modules.filter((item) => ["start", "search", "apps", "tray", "clock"].includes(item))
    : cloneDesign().glassbar.modules;
  merged.glassbar.backgroundMode = ["glass", "transparent"].includes(merged.glassbar.backgroundMode) ? merged.glassbar.backgroundMode : "glass";
  merged.glassbar.effectMode = ["ambient", "none"].includes(merged.glassbar.effectMode) ? merged.glassbar.effectMode : "ambient";
  merged.widget.mode = ["visual", "code"].includes(merged.widget.mode) ? merged.widget.mode : "visual";
  merged.widget.code.html = String(merged.widget.code.html || "").slice(0, 20_000);
  merged.widget.code.css = String(merged.widget.code.css || "").slice(0, 30_000);
  merged.widget.code.js = String(merged.widget.code.js || "").slice(0, 50_000);
  return merged;
}

export function slugify(value) {
  return String(value || "design")
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 64) || "design";
}

export function downloadPackage(design) {
  const normalized = normalizeDesign(design);
  const blob = new Blob([JSON.stringify(normalized, null, 2)], { type: "application/json" });
  const href = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = href;
  anchor.download = `${slugify(normalized.metadata.name)}.glassbar.json`;
  anchor.click();
  URL.revokeObjectURL(href);
}
