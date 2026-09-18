const DEFAULT_ADMIN_EMAIL = "ethanhuynh365@gmail.com";

function serverConfig() {
  const url = process.env.SUPABASE_URL || process.env.VITE_SUPABASE_URL;
  const anonKey = process.env.SUPABASE_PUBLISHABLE_KEY || process.env.VITE_SUPABASE_PUBLISHABLE_KEY;
  const serviceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
  if (!url || !anonKey || !serviceKey) throw new Error("Supabase server configuration is missing.");
  return { url: url.replace(/\/$/, ""), anonKey, serviceKey };
}

function bearerToken(request) {
  return request.headers.authorization?.match(/^Bearer\s+(.+)$/i)?.[1] || "";
}

function decodeClaims(token) {
  try { return JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString("utf8")); }
  catch { return {}; }
}

async function supabaseRequest(path, { token, serviceRole = false, method = "GET", body, prefer } = {}) {
  const config = serverConfig();
  const key = serviceRole ? config.serviceKey : config.anonKey;
  return fetch(`${config.url}${path}`, {
    method,
    headers: {
      apikey: key,
      Authorization: `Bearer ${token || key}`,
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(prefer ? { Prefer: prefer } : {})
    },
    body: body !== undefined ? JSON.stringify(body) : undefined
  });
}

async function authenticateRequest(request) {
  const token = bearerToken(request);
  if (!token) return { error: { status: 401, message: "Sign in before continuing." } };

  const authResponse = await supabaseRequest("/auth/v1/user", { token });
  if (!authResponse.ok) return { error: { status: 401, message: "Your session is no longer valid." } };
  const user = await authResponse.json();
  if (decodeClaims(token).aal !== "aal2")
    return { error: { status: 403, message: "Two-factor verification is required." } };
  return { token, user };
}

function adminEmail() {
  return String(process.env.STUDIO_ADMIN_EMAIL || DEFAULT_ADMIN_EMAIL).trim().toLowerCase();
}

function isAdmin(user) {
  return String(user?.email || "").trim().toLowerCase() === adminEmail();
}

function slugify(value) {
  return String(value || "design").toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 60) || "design";
}

export { adminEmail, authenticateRequest, isAdmin, serverConfig, slugify, supabaseRequest };
