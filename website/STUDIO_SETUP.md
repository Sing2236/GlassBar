# GlassBar Community Studio setup

The site runs locally without credentials. Editing, live previews, JSON import/export, and the seeded gallery work immediately. Publishing stays locked until Supabase is connected.

## 1. Supabase Auth and data

1. Create a Supabase project and apply `supabase/migrations/001_community_studio.sql`.
2. In **Authentication > URL Configuration**, set the production site URL and add both the production and local `/studio/account` URLs as redirects.
3. Keep email/password signups and email confirmation enabled. Configure custom SMTP before inviting public users; Supabase's default mail service is intended only for initial testing.
4. TOTP authenticator MFA works without another provider. Phone MFA also requires an SMS provider configured in Supabase.
5. Copy the project URL and publishable key. Never expose a secret or service-role key in the website.

The database policies require an `aal2` Supabase session for profile changes, design publishing, and preview uploads. Uploaded assets are limited to PNG, JPG, WebP, and GIF files up to 5 MB.

Every design is inserted as `pending`, including designs submitted by the reviewer account. The account configured by `STUDIO_ADMIN_EMAIL` gets a private **Review** tab where pending designs can be approved or rejected. Approval sets `status` to `approved` and adds `published_at`; rejected designs remain private.

## 2. Environment and local run

Copy `.env.example` to `.env.local`, fill in the two public values, then run:

```powershell
npm install
npm run dev
```

## 3. Review notification email

Set `RESEND_API_KEY`, `STUDIO_REVIEW_EMAIL`, and `STUDIO_EMAIL_FROM` in the server environment. Each successful submission sends a plain-text link to the private moderation queue. Email delivery is deliberately non-blocking: if the provider is down, the submission remains safely queued for review.

`STUDIO_ADMIN_EMAIL` controls server-side access to the moderation API. The current reviewer account is `ethanhuynh365@gmail.com`. Admin requests still require a valid Supabase session with two-factor verification.

## Package boundary

Community packages are JSON and never contain native binaries. Code-widget packages may include HTML, CSS, and JavaScript, but the website runs that code in an isolated preview with network and storage access disabled. Code widgets remain private until the reviewer manually approves them.
