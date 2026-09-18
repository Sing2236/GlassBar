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
      let previewUrl = null;
      let uploadedPath = null;
      if (asset) {
        const extension = asset.name.split(".").pop().toLowerCase();
        uploadedPath = `${authorId}/${crypto.randomUUID()}.${extension}`;
        const { error: uploadError } = await supabase.storage.from("design-assets").upload(uploadedPath, asset, {
          cacheControl: "3600",
          contentType: asset.type,
          upsert: false
        });
        if (uploadError) throw uploadError;
        previewUrl = supabase.storage.from("design-assets").getPublicUrl(uploadedPath).data.publicUrl;
      }

      try {
        const { data: sessionData } = await supabase.auth.getSession();
        const token = sessionData.session?.access_token;
        if (!token) throw new Error("Your session expired. Sign in again before publishing.");
        const response = await fetch("/api/publish-design", {
          method: "POST",
          headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
          body: JSON.stringify({ document, username: cleanUsername, previewUrl })
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.error || "The design could not be submitted.");
        return result;
      } catch (error) {
        if (uploadedPath) await supabase.storage.from("design-assets").remove([uploadedPath]).catch(() => {});
        throw error;
      }
    }
  };
}
