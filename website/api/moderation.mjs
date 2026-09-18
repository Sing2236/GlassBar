import { authenticateRequest, isAdmin, supabaseRequest } from "./_lib/studioServer.mjs";

function validId(value) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(String(value || ""));
}

export default async function handler(request, response) {
  response.setHeader("Cache-Control", "no-store");
  if (!['GET', 'POST'].includes(request.method)) return response.status(405).json({ error: "Method not allowed." });

  try {
    const auth = await authenticateRequest(request);
    if (auth.error) return response.status(auth.error.status).json({ error: auth.error.message });
    if (!isAdmin(auth.user)) return response.status(403).json({ error: "This account cannot moderate designs." });

    if (request.method === "GET") {
      const select = encodeURIComponent("id,kind,name,summary,tags,document,preview_url,status,created_at,profiles(username)");
      const result = await supabaseRequest(`/rest/v1/designs?status=eq.pending&select=${select}&order=created_at.asc`, { serviceRole: true });
      if (!result.ok) throw new Error(`Queue load failed with ${result.status}.`);
      return response.status(200).json({ designs: await result.json() });
    }

    const id = request.body?.id;
    const action = request.body?.action;
    if (!validId(id) || !["approve", "reject"].includes(action))
      return response.status(400).json({ error: "Choose a valid pending design and review action." });

    const approved = action === "approve";
    const result = await supabaseRequest(`/rest/v1/designs?id=eq.${encodeURIComponent(id)}&status=eq.pending`, {
      serviceRole: true,
      method: "PATCH",
      prefer: "return=representation",
      body: {
        status: approved ? "approved" : "rejected",
        is_published: approved,
        published_at: approved ? new Date().toISOString() : null,
        updated_at: new Date().toISOString()
      }
    });
    if (!result.ok) throw new Error(`Review update failed with ${result.status}.`);
    const rows = await result.json();
    if (!rows.length) return response.status(409).json({ error: "That design is no longer pending." });
    return response.status(200).json({ id, status: approved ? "approved" : "rejected" });
  } catch (error) {
    console.error("Moderation request failed", error instanceof Error ? error.message : "Unknown error");
    return response.status(503).json({ error: "The moderation queue is temporarily unavailable." });
  }
}
