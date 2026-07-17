import type { Env } from "./types";

export async function notifyOwner(
  env: Env,
  subject: string,
  lines: string[]
): Promise<void> {
  if (!env.RESEND_API_KEY || !env.OWNER_NOTIFY_EMAIL || !env.RESEND_FROM_EMAIL) {
    console.warn("Resend not configured — skip owner notify:", subject);
    return;
  }

  const html = `<pre style="font-family:ui-monospace,monospace;font-size:13px">${escapeHtml(
    lines.join("\n")
  )}</pre>`;

  const res = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${env.RESEND_API_KEY}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      from: env.RESEND_FROM_EMAIL,
      to: [env.OWNER_NOTIFY_EMAIL],
      subject: `[${env.APP_NAME}] ${subject}`,
      html,
      text: lines.join("\n"),
    }),
  });

  if (!res.ok) {
    console.error("Resend failed", res.status, await res.text());
  }
}

export async function emailBuyer(
  env: Env,
  to: string,
  subject: string,
  text: string
): Promise<void> {
  if (!env.RESEND_API_KEY || !env.RESEND_FROM_EMAIL) {
    console.warn("Resend not configured — skip buyer email:", subject);
    return;
  }

  const res = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${env.RESEND_API_KEY}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      from: env.RESEND_FROM_EMAIL,
      to: [to],
      subject,
      text,
      html: `<p style="font-family:system-ui,sans-serif;line-height:1.5">${escapeHtml(text).replace(/\n/g, "<br/>")}</p>`,
    }),
  });

  if (!res.ok) {
    console.error("Resend buyer failed", res.status, await res.text());
  }
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
