import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import StudioShell from "../components/StudioShell";
import GlassBarPreview from "../components/GlassBarPreview";
import { useStudioAuth } from "../auth/StudioAuth";
import { normalizeDesign } from "../lib/designSchema";
import { listPendingDesigns, reviewDesign } from "../lib/moderationRepository";

function ReviewCard({ item, busy, onReview }) {
  const design = normalizeDesign(item.document);
  return (
    <article className="review-card">
      <GlassBarPreview design={design} compact label={`${item.name} review preview`} />
      <div className="review-card-copy">
        <div className="review-meta"><span>{item.kind}</span><time>{new Date(item.created_at).toLocaleString()}</time></div>
        <h2>{item.name}</h2>
        <p>{item.summary || "No description supplied."}</p>
        <dl>
          <div><dt>Creator</dt><dd>{item.profiles?.username || "creator"}</dd></div>
          <div><dt>Tags</dt><dd>{item.tags?.join(", ") || "None"}</dd></div>
        </dl>
        {design.kind === "widget" && design.widget.mode === "code" && (
          <div className="manual-code-review">
            <p className="manual-code-note">Code runs only after you choose “Run sandboxed preview.” Review all three source files before approval.</p>
            {["html", "css", "js"].map((part) => (
              <details key={part}><summary>{part === "js" ? "JavaScript" : part.toUpperCase()}</summary><pre><code>{design.widget.code[part]}</code></pre></details>
            ))}
          </div>
        )}
        <div className="review-actions">
          <button className="secondary-button reject-button" disabled={busy} onClick={() => onReview(item.id, "reject")}>Reject</button>
          <button className="primary-button" disabled={busy} onClick={() => onReview(item.id, "approve")}>Approve</button>
        </div>
      </div>
    </article>
  );
}

export default function ModerationPage() {
  const auth = useStudioAuth();
  const [designs, setDesigns] = useState([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState("");
  const [message, setMessage] = useState("");

  const loadQueue = useCallback(async () => {
    if (!auth.authenticated || !auth.isAdmin || !auth.mfaVerified) return setLoading(false);
    setLoading(true);
    try {
      const result = await listPendingDesigns();
      setDesigns(result.designs || []);
      setMessage("");
    } catch (error) {
      setMessage(error.message || "The review queue could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [auth.authenticated, auth.isAdmin, auth.mfaVerified]);

  useEffect(() => { loadQueue(); }, [loadQueue]);

  async function handleReview(id, action) {
    setBusyId(id);
    setMessage("");
    try {
      await reviewDesign(id, action);
      setDesigns((current) => current.filter((item) => item.id !== id));
      setMessage(action === "approve" ? "Design approved and published." : "Design rejected.");
    } catch (error) {
      setMessage(error.message || "The design could not be reviewed.");
    } finally {
      setBusyId("");
    }
  }

  if (auth.loading) return <StudioShell eyebrow="MODERATION" title="Loading your review queue…" />;
  if (!auth.authenticated) return (
    <StudioShell eyebrow="MODERATION" title="Sign in to review designs.">
      <div className="moderation-empty"><p>This queue is available only to the GlassBar reviewer account.</p><Link className="primary-button" to="/studio/account">Sign in</Link></div>
    </StudioShell>
  );
  if (!auth.isAdmin) return (
    <StudioShell eyebrow="MODERATION" title="This queue is private."><div className="moderation-empty"><p>Your account does not have permission to approve designs.</p></div></StudioShell>
  );
  if (!auth.mfaVerified) return (
    <StudioShell eyebrow="MODERATION" title="Verify before reviewing."><div className="moderation-empty"><p>Complete two-factor verification before approving or rejecting designs.</p><Link className="primary-button" to="/studio/account">Verify account</Link></div></StudioShell>
  );

  return (
    <StudioShell eyebrow="MODERATION" title="Review community submissions.">
      <div className="moderation-toolbar"><p>Every submission stays private until you approve it.</p><button className="secondary-button" onClick={loadQueue} disabled={loading}>Refresh queue</button></div>
      {message && <p className="moderation-message" role="status">{message}</p>}
      {loading ? <div className="moderation-empty"><p>Loading submissions…</p></div>
        : designs.length === 0 ? <div className="moderation-empty"><p>No designs are waiting for review.</p></div>
          : <div className="review-grid">{designs.map((item) => <ReviewCard item={item} busy={busyId === item.id} onReview={handleReview} key={item.id} />)}</div>}
    </StudioShell>
  );
}
