import StudioShell from "../components/StudioShell";
import { useStudioAuth } from "../auth/StudioAuth";

export default function AccountPage() {
  const auth = useStudioAuth();
  return (
    <StudioShell eyebrow="CREATOR ACCOUNT" title="Your publishing identity">
      <section className="account-card">
        {!auth.configured ? <>
          <span className="account-icon">!</span><h2>Authentication setup is not connected yet.</h2>
          <p>Add the Auth0 environment values, enable username/password registration, and require email or SMS MFA before launch.</p>
        </> : !auth.authenticated ? <>
          <span className="account-icon">↗</span><h2>Sign in to manage your designs.</h2>
          <p>Registration uses a username and password, followed by a required email or phone verification step.</p>
          <div className="account-actions"><button className="primary-button" onClick={auth.signup}>Create account</button><button className="secondary-button" onClick={auth.login}>Sign in</button></div>
        </> : <>
          <span className="account-avatar">{(auth.user?.nickname || auth.user?.name || "U").slice(0, 1).toUpperCase()}</span>
          <h2>{auth.user?.nickname || auth.user?.name || "GlassBar creator"}</h2><p>{auth.user?.email}</p>
          <dl><div><dt>Email</dt><dd>{auth.user?.email_verified ? "Verified" : "Needs verification"}</dd></div><div><dt>Two-factor</dt><dd className={auth.mfaVerified ? "good" : "warning"}>{auth.mfaVerified ? "Verified for this session" : "Required before publishing"}</dd></div></dl>
          <button className="secondary-button" onClick={auth.logout}>Sign out</button>
        </>}
      </section>
    </StudioShell>
  );
}
