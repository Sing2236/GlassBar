export const DESIGN_KINDS = ["glassbar", "widget", "animation"];

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
    backgroundStart: "#132233",
    backgroundEnd: "#111720",
    accent: "#60d7ff",
    border: "#ffffff",
    shadow: 36,
    modules: ["start", "search", "apps", "tray", "clock"]
  },
  widget: {
    title: "Focus",
    value: "25:00",
    detail: "Deep work",
    icon: "timer",
    dataSource: "focus",
    width: 164,
    radius: 18,
    accent: "#7dd3fc",
    background: "#172235",
    showDivider: true
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
  merged.animation = { ...merged.animation, ...(source.animation || {}) };
  merged.glassbar.modules = Array.isArray(merged.glassbar.modules)
    ? merged.glassbar.modules.filter((item) => ["start", "search", "apps", "tray", "clock"].includes(item))
    : cloneDesign().glassbar.modules;
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
