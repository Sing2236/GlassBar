import crypto from "node:crypto";
import { normalizeDesign } from "../src/lib/designSchema.js";
import { authenticateRequest, serverConfig, slugify, supabaseRequest } from "./_lib/studioServer.mjs";

const MAX_PACKAGE_BYTES = 160_000;

function validPreviewUrl(value) {
  if (!value) return null;
  const { url } = serverConfig();
  const preview = String(value);
  return preview.startsWith(`${url}/storage/v1/object/public/design-assets/`) ? preview : null;
}

export default async function handler(request, response) {
  response.setHeader("Cache-Control", "no-store");
  if (request.method !== "POST") return response.status(405).json({ error: "Method not allowed." });

  try {
    const auth = await authenticateRequest(request);
    if (auth.error) return response.status(auth.error.status).json({ error: auth.error.message });

    const rawDocument = request.body?.document;
    if (!rawDocument || typeof rawDocument !== "object")
      return response.status(400).json({ error: "A valid GlassBar package is required." });
    if (Buffer.byteLength(JSON.stringify(rawDocument), "utf8") > MAX_PACKAGE_BYTES)
      return response.status(413).json({ error: "That package is too large." });

    const document = normalizeDesign(rawDocument);
    const name = String(document.metadata?.name || "").trim().slice(0, 60);
    const summary = String(document.metadata?.summary || "").trim().slice(0, 180);
    if (!name) return response.status(400).json({ error: "A design name is required." });

    const requested = String(request.body?.username || auth.user.user_metadata?.username || auth.user.email?.split("@")[0] || "creator")
      .replace(/[^a-zA-Z0-9_]/g, "_").slice(0, 24);
    const username = (requested.length >= 3 ? requested : `creator_${auth.user.id.slice(-8)}`).slice(0, 24);
    const profileResponse = await supabaseRequest("/rest/v1/profiles?on_conflict=user_id", {
      serviceRole: true,
      method: "POST",
      prefer: "resolution=merge-duplicates",
      body: { user_id: auth.user.id, username, avatar_url: auth.user.user_metadata?.avatar_url || null }
    });
    if (!profileResponse.ok) throw new Error(`Profile update failed with ${profileResponse.status}.`);

    const row = {
      author_id: auth.user.id,
      kind: document.kind,
      name,
      slug: `${slugify(name)}-${crypto.randomUUID().slice(0, 8)}`,
      summary,
      tags: Array.isArray(document.metadata?.tags) ? document.metadata.tags.map(String).slice(0, 8) : [],
      document,
      preview_url: validPreviewUrl(request.body?.previewUrl),
      is_published: true,
      status: "pending",
      security_status: "not_required",
      security_report: null
    };
    const insertResponse = await supabaseRequest("/rest/v1/designs?select=id", {
      serviceRole: true,
      method: "POST",
      prefer: "return=representation",
      body: row
    });
    if (!insertResponse.ok) throw new Error(`Publish failed with ${insertResponse.status}.`);
    const [created] = await insertResponse.json();

    return response.status(201).json({ id: created.id, status: "pending" });
  } catch (error) {
    console.error("Design publishing failed", error instanceof Error ? error.message : "Unknown error");
    return response.status(503).json({ error: "Publishing is temporarily unavailable. No design was submitted." });
  }
}
