const crypto = require("node:crypto");
const { scanWidget } = require("./lib/widgetSecurity");

function slugify(value) {
  return String(value || "widget").toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 60) || "widget";
}

function decodeClaims(token) {
  try { return JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString("utf8")); }
  catch { return {}; }
}

async function supabaseRequest(path, { token, serviceKey, method = "GET", body, prefer } = {}) {
  const url = process.env.SUPABASE_URL || process.env.VITE_SUPABASE_URL;
  const anonKey = process.env.SUPABASE_PUBLISHABLE_KEY || process.env.VITE_SUPABASE_PUBLISHABLE_KEY;
  if (!url || !anonKey) throw new Error("Supabase server configuration is missing.");
  const key = serviceKey || anonKey;
  return fetch(`${url}${path}`, {
    method,
    headers: {
      apikey: key,
      Authorization: `Bearer ${token || key}`,
      ...(body ? { "Content-Type": "application/json" } : {}),
      ...(prefer ? { Prefer: prefer } : {})
    },
    body: body ? JSON.stringify(body) : undefined
  });
}

module.exports = async function handler(request, response) {
  response.setHeader("Cache-Control", "no-store");
  if (request.method !== "POST") return response.status(405).json({ error: "Method not allowed." });
  const bearer = request.headers.authorization?.match(/^Bearer\s+(.+)$/i)?.[1];
  if (!bearer) return response.status(401).json({ error: "Sign in before publishing." });

  try {
    const authResponse = await supabaseRequest("/auth/v1/user", { token: bearer });
    if (!authResponse.ok) return response.status(401).json({ error: "Your session is no longer valid." });
    const user = await authResponse.json();
    if (decodeClaims(bearer).aal !== "aal2") return response.status(403).json({ error: "Two-factor verification is required." });

    const document = request.body?.document;
    if (!document || document.kind !== "widget" || document.widget?.mode !== "code")
      return response.status(400).json({ error: "This endpoint accepts coded widget packages only." });
    const name = String(document.metadata?.name || "").trim().slice(0, 60);
    const summary = String(document.metadata?.summary || "").trim().slice(0, 180);
    if (!name) return response.status(400).json({ error: "A widget name is required." });

    const security = await scanWidget(document.widget.code);
    if (!security.passed) return response.status(422).json({ error: "The widget did not pass security review.", security });

    const serviceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
    if (!serviceKey) throw new Error("SUPABASE_SERVICE_ROLE_KEY is not configured.");
    const requested = String(request.body?.username || user.email?.split("@")[0] || "creator").replace(/[^a-zA-Z0-9_]/g, "_").slice(0, 24);
    const username = (requested.length >= 3 ? requested : `creator_${user.id.slice(-8)}`).slice(0, 24);
    const profileResponse = await supabaseRequest("/rest/v1/profiles?on_conflict=user_id", {
      serviceKey, method: "POST", prefer: "resolution=merge-duplicates", body: { user_id: user.id, username }
    });
    if (!profileResponse.ok) throw new Error(`Profile update failed: ${await profileResponse.text()}`);

    const row = {
      author_id: user.id,
      kind: "widget",
      name,
      slug: `${slugify(name)}-${crypto.randomUUID().slice(0, 8)}`,
      summary,
      tags: Array.isArray(document.metadata?.tags) ? document.metadata.tags.map(String).slice(0, 8) : [],
      document,
      preview_url: null,
      is_published: true,
      status: "pending",
      security_status: "passed",
      security_report: security
    };
    const insertResponse = await supabaseRequest("/rest/v1/designs?select=id", {
      serviceKey, method: "POST", prefer: "return=representation", body: row
    });
    if (!insertResponse.ok) throw new Error(`Publish failed: ${await insertResponse.text()}`);
    const [created] = await insertResponse.json();
    return response.status(201).json({ id: created.id, security });
  } catch (error) {
    return response.status(503).json({ error: "Secure publishing is temporarily unavailable. No widget was published.", detail: error.message });
  }
};
