import { normalizeDesign, slugify } from "./designSchema";
import { studioBackendConfigured, supabase } from "./supabaseClient";

export function createStudioRepository() {
  if (!studioBackendConfigured) return null;

  return {
    async listPublished() {
      const { data, error } = await supabase
        .from("designs")
        .select("id,kind,name,summary,document,downloads,status,profiles(username)")
        .eq("status", "approved")
        .eq("is_published", true)
        .order("published_at", { ascending: false });
      if (error) throw error;
      return (data || []).map((item) => ({ ...item, author: item.profiles?.username || "creator" }));
    },

    async publish({ design, user, username, asset }) {
      const document = normalizeDesign(design);
      const authorId = user.id;
      const requestedUsername = slugify(username || user.nickname || user.name || "creator").replaceAll("-", "_");
      const cleanUsername = (requestedUsername.length >= 3
        ? requestedUsername
        : `creator_${slugify(authorId).slice(-8)}`).slice(0, 24);
      const { error: profileError } = await supabase.from("profiles").upsert({
        user_id: authorId,
        username: cleanUsername,
        avatar_url: user.picture || null
      });
      if (profileError) throw profileError;

      if (document.kind === "widget" && document.widget.mode === "code") {
        const { data: sessionData } = await supabase.auth.getSession();
        const token = sessionData.session?.access_token;
        if (!token) throw new Error("Your session expired. Sign in again before publishing.");
        const response = await fetch("/api/publish-widget", {
          method: "POST",
          headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
          body: JSON.stringify({ document, username: cleanUsername })
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) {
          const reasons = result.security?.reasons?.join(" ");
          throw new Error(reasons || result.error || "The widget did not pass security review.");
        }
        return result;
      }

      let previewUrl = null;
      if (asset) {
        const extension = asset.name.split(".").pop().toLowerCase();
        const path = `${authorId}/${crypto.randomUUID()}.${extension}`;
        const { error: uploadError } = await supabase.storage.from("design-assets").upload(path, asset, {
          cacheControl: "3600",
          contentType: asset.type,
          upsert: false
        });
        if (uploadError) throw uploadError;
        previewUrl = supabase.storage.from("design-assets").getPublicUrl(path).data.publicUrl;
      }

      const { data, error } = await supabase.from("designs").insert({
        author_id: authorId,
        kind: document.kind,
        name: document.metadata.name,
        slug: `${slugify(document.metadata.name)}-${crypto.randomUUID().slice(0, 8)}`,
        summary: document.metadata.summary,
        tags: document.metadata.tags,
        document,
        preview_url: previewUrl,
        is_published: true,
        status: "pending"
      }).select("id").single();
      if (error) throw error;
      return data;
    }
  };
}
