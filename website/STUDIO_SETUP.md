# GlassBar Community Studio setup

The site runs locally without credentials. Editing, live previews, JSON import/export, and the seeded gallery work immediately. Publishing stays locked until Supabase is connected.

## 1. Supabase Auth and data

1. Create a Supabase project and apply `supabase/migrations/001_community_studio.sql`.
2. In **Authentication > URL Configuration**, set the production site URL and add both the production and local `/studio/account` URLs as redirects.
3. Keep email/password signups and email confirmation enabled. Configure custom SMTP before inviting public users; Supabase's default mail service is intended only for initial testing.
4. TOTP authenticator MFA works without another provider. Phone MFA also requires an SMS provider configured in Supabase.
5. Copy the project URL and publishable key. Never expose a secret or service-role key in the website.

The database policies require an `aal2` Supabase session for profile changes, design publishing, and preview uploads. Uploaded assets are limited to PNG, JPG, WebP, and GIF files up to 5 MB.

New designs are inserted as `pending`. Approve them by changing `status` to `approved` and setting `published_at` after moderation.

## 2. Environment and local run

Copy `.env.example` to `.env.local`, fill in the two public values, then run:

```powershell
npm install
npm run dev
```

## Package boundary

Community packages are declarative JSON, not executable JavaScript or native binaries. They can control the full visual layout, approved widget data sources, and animation parameters without granting uploaded content code execution on another user's PC.
