// Public package lookup for the desktop glassbar:// importer.
const seedPackages = {
  midnight: { kind: "glassbar", metadata: { name: "Midnight Current", summary: "Deep blue glass with a cool cyan edge.", tags: ["dark", "blue"] }, glassbar: { backgroundStart: "#0f2636", backgroundEnd: "#11141d", accent: "#64ddff", opacity: 91, radius: 24 } },
  paper: { kind: "glassbar", metadata: { name: "Paper Glass", summary: "A bright, quiet bar for light desktops.", tags: ["light", "minimal"] }, glassbar: { backgroundStart: "#edf7fb", backgroundEnd: "#cbd8e3", accent: "#1688c8", opacity: 78 } },
  focus: { kind: "widget", metadata: { name: "Focus Capsule", summary: "A compact focus timer.", tags: ["productivity"] }, widget: { mode: "visual", title: "Focus", value: "42:18", detail: "Do not disturb", icon: "timer", accent: "#8be9fd" } },
  weather: { kind: "widget", metadata: { name: "Weather Line", summary: "Temperature and conditions.", tags: ["weather"] }, widget: { mode: "visual", title: "Chicago", value: "68°", detail: "Clear skies", icon: "weather", dataSource: "weather" } },
  rain: { kind: "animation", metadata: { name: "Fine Rain", summary: "Soft diagonal rain.", tags: ["rain", "calm"] }, animation: { name: "Fine Rain", shape: "line", motion: "fall", density: 52, speed: 61, size: 42, glow: 28, trail: 70, primaryColor: "#93e1ff", secondaryColor: "#8b5cf6" } },
  embers: { kind: "animation", metadata: { name: "Quiet Embers", summary: "Warm rising particles.", tags: ["warm", "particles"] }, animation: { name: "Quiet Embers", shape: "orb", motion: "rise", density: 35, speed: 29, size: 42, glow: 62, trail: 40, primaryColor: "#fb923c", secondaryColor: "#fde68a" } }
};

export default async function handler(request, response) {
  response.setHeader("Cache-Control", "public, max-age=60, s-maxage=300");
  if (request.method !== "GET") return response.status(405).json({ error: "Method not allowed." });
  const id = String(request.query?.id || "").slice(0, 100);
  if (seedPackages[id]) return response.status(200).json({ schemaVersion: 1, ...seedPackages[id] });
  if (!/^[0-9a-f-]{36}$/i.test(id)) return response.status(404).json({ error: "Design not found." });
  const url = process.env.SUPABASE_URL || process.env.VITE_SUPABASE_URL;
  const key = process.env.SUPABASE_PUBLISHABLE_KEY || process.env.VITE_SUPABASE_PUBLISHABLE_KEY;
  if (!url || !key) return response.status(503).json({ error: "Design storage is unavailable." });
  const result = await fetch(`${url}/rest/v1/designs?id=eq.${encodeURIComponent(id)}&status=eq.approved&is_published=eq.true&select=document`, {
    headers: { apikey: key, Authorization: `Bearer ${key}` }
  });
  if (!result.ok) return response.status(502).json({ error: "Design storage did not respond." });
  const [row] = await result.json();
  if (!row) return response.status(404).json({ error: "Design not found." });
  return response.status(200).json(row.document);
};
