import { supabase } from "./supabaseClient";

async function request(path, options = {}) {
  const { data } = await supabase.auth.getSession();
  const token = data.session?.access_token;
  if (!token) throw new Error("Sign in again to open the review queue.");
  const response = await fetch(path, {
    ...options,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(options.body ? { "Content-Type": "application/json" } : {})
    }
  });
  const result = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(result.error || "The review request failed.");
  return result;
}

function listPendingDesigns() {
  return request("/api/moderation");
}

function reviewDesign(id, action) {
  return request("/api/moderation", { method: "POST", body: JSON.stringify({ id, action }) });
}

export { listPendingDesigns, reviewDesign };
