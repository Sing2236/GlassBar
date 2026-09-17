import { createClient } from "@supabase/supabase-js";
import { normalizeDesign, slugify } from "./designSchema";

const supabaseUrl = import.meta.env.VITE_SUPABASE_URL;
const supabaseKey = import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY;
export const studioBackendConfigured = Boolean(supabaseUrl && supabaseKey);

export function createStudioRepository(getIdToken) {
  if (!studioBackendConfigured) return null;
  const client = createClient(supabaseUrl, supabaseKey, { accessToken: getIdToken });

  return {
    async listPublished() {
      const { data, error } = await client
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
      const authorId = user.sub;
      const requestedUsername = slugify(username || user.nickname || user.name || "creator").replaceAll("-", "_");
      const cleanUsername = (requestedUsername.length >= 3
        ? requestedUsername
        : `creator_${slugify(authorId).slice(-8)}`).slice(0, 24);
      const { error: profileError } = await client.from("profiles").upsert({
        user_id: authorId,
        username: cleanUsername,
        avatar_url: user.picture || null
      });
      if (profileError) throw profileError;

      let previewUrl = null;
      if (asset) {
        const extension = asset.name.split(".").pop().toLowerCase();
        const path = `${authorId}/${crypto.randomUUID()}.${extension}`;
        const { error: uploadError } = await client.storage.from("design-assets").upload(path, asset, {
          cacheControl: "3600",
          contentType: asset.type,
          upsert: false
        });
        if (uploadError) throw uploadError;
        previewUrl = client.storage.from("design-assets").getPublicUrl(path).data.publicUrl;
      }

      const { data, error } = await client.from("designs").insert({
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
