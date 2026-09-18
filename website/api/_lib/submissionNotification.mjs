import { adminEmail } from "./studioServer.mjs";

function cleanLine(value, fallback = "") {
  return String(value || fallback).replace(/[\r\n]+/g, " ").trim();
}

async function sendSubmissionNotification({ design, submissionId, submitter }) {
  const apiKey = process.env.RESEND_API_KEY;
  if (!apiKey) return { sent: false, reason: "not_configured" };

  const recipient = String(process.env.STUDIO_REVIEW_EMAIL || adminEmail()).trim();
  const from = String(process.env.STUDIO_EMAIL_FROM || "GlassBar Studio <onboarding@resend.dev>").trim();
  const name = cleanLine(design.metadata?.name, "Untitled design").slice(0, 60);
  const kind = cleanLine(design.kind, "design").slice(0, 20);
  const author = cleanLine(submitter?.email, "Unknown creator").slice(0, 160);
  const reviewUrl = `${String(process.env.PUBLIC_SITE_URL || "https://get-glassbar.vercel.app").replace(/\/$/, "")}/studio/moderation`;

  const result = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: { Authorization: `Bearer ${apiKey}`, "Content-Type": "application/json" },
    body: JSON.stringify({
      from,
      to: [recipient],
      subject: `GlassBar review: ${name}`,
      text: [
        "A new GlassBar Studio design is waiting for review.",
        "",
        `Name: ${name}`,
        `Type: ${kind}`,
        `Submitted by: ${author}`,
        `Submission ID: ${submissionId}`,
        "",
        `Review it: ${reviewUrl}`
      ].join("\n")
    })
  });

  if (!result.ok) {
    const detail = await result.text();
    console.error("Design notification failed", result.status, detail.slice(0, 500));
    return { sent: false, reason: "delivery_failed" };
  }
  return { sent: true };
}

export { cleanLine, sendSubmissionNotification };
