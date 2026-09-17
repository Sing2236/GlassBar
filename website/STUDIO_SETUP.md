# GlassBar Community Studio setup

The site runs locally without credentials. Editing, live previews, JSON import/export, and the seeded gallery work immediately. Publishing stays locked until Auth0 and Supabase are connected.

## 1. Auth0

1. Create a **Single Page Application** and add the production site plus `http://localhost:5173` to Allowed Callback URLs, Logout URLs, and Web Origins.
2. In the database connection, enable username/password accounts and set **Requires Username**.
3. Enable email and SMS under **Security > Multi-factor Auth** and set the policy to **Always**. Configure an SMS provider before production.
4. Add the Post Login Action from `auth0/post-login-action.js` to the Login flow. It adds the literal `role: authenticated` claim required by Supabase.
5. Use RS256 signing. Supabase third-party auth does not support HS256 or PS256 Auth0 tenants.

Auth0 Universal Login owns password entry, verification, recovery, rate limiting, and MFA. The GlassBar site never receives or stores user passwords.

## 2. Supabase

1. Create a project and run `supabase/migrations/001_community_studio.sql` in the SQL editor.
2. In **Authentication > Third-Party Auth**, add the Auth0 tenant integration.
3. Copy the project URL and publishable key. Never expose a service-role key in the website.
4. Review Storage malware scanning options before opening public submissions. The current policy accepts only PNG, JPG, WebP, and GIF files up to 5 MB, and every database or upload write requires an Auth0 token whose `amr` claim contains `mfa`.

New designs are inserted as `pending`. Approve them by changing `status` to `approved` and setting `published_at` after moderation.

## 3. Environment and local run

Copy `.env.example` to `.env.local`, fill in the four public values, then run:

```powershell
npm install
npm run dev
```

## Package boundary

Community packages are declarative JSON, not executable JavaScript or native binaries. They can control the full visual layout, approved widget data sources, and animation parameters without granting uploaded content code execution on another user's PC.
