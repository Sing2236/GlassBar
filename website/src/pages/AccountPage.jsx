import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import StudioShell from "../components/StudioShell";
import { useStudioAuth } from "../auth/StudioAuth";

const emptyForm = { username: "", email: "", password: "" };

function friendlyError(error) {
  const message = error?.message || "Something went wrong. Please try again.";
  if (/database error saving new user/i.test(message)) return "That username may already be taken. Try another one.";
  return message;
}

export default function AccountPage() {
  const auth = useStudioAuth();
  const [searchParams] = useSearchParams();
  const [mode, setMode] = useState(searchParams.get("mode") === "signup" ? "signup" : "login");
  const [form, setForm] = useState(emptyForm);
  const [phone, setPhone] = useState("");
  const [factors, setFactors] = useState([]);
  const [enrollment, setEnrollment] = useState(null);
  const [challenge, setChallenge] = useState(null);
  const [code, setCode] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);

  const verifiedFactors = useMemo(() => factors.filter((factor) => factor.status === "verified"), [factors]);

  async function loadFactors() {
    const { data, error } = await auth.listFactors();
    if (error) throw error;
    setFactors([...(data?.totp || []), ...(data?.phone || [])]);
  }

  useEffect(() => {
    if (!auth.authenticated) {
      setFactors([]);
      setEnrollment(null);
      setChallenge(null);
      return;
    }
    loadFactors().catch((error) => setMessage(friendlyError(error)));
  }, [auth.authenticated]);

  function updateField(event) {
    setForm((current) => ({ ...current, [event.target.name]: event.target.value }));
  }

  async function submitCredentials(event) {
    event.preventDefault();
    setMessage("");
    if (mode === "signup" && !/^[a-zA-Z0-9_]{3,24}$/.test(form.username)) {
      return setMessage("Usernames must be 3–24 characters using letters, numbers, or underscores.");
    }
    setBusy(true);
    try {
      const result = mode === "signup"
        ? await auth.signup(form)
        : await auth.login(form);
      if (result.error) throw result.error;
      if (mode === "signup" && !result.data.session) {
        setMessage("Account created. Check your email to confirm it, then return here to finish two-factor setup.");
      } else {
        setMessage(mode === "signup" ? "Account created. Set up two-factor authentication next." : "Signed in.");
      }
      setForm((current) => ({ ...current, password: "" }));
    } catch (error) {
      setMessage(friendlyError(error));
    } finally {
      setBusy(false);
    }
  }

  async function beginChallenge(factor) {
    setBusy(true);
    setMessage("");
    setCode("");
    try {
      const { data, error } = await auth.challengeFactor(factor.id);
      if (error) throw error;
      setChallenge({ factorId: factor.id, challengeId: data.id, type: factor.factor_type });
      setMessage(factor.factor_type === "phone" ? "A verification code was sent to your phone." : "Enter the code from your authenticator app.");
    } catch (error) {
      setMessage(friendlyError(error));
    } finally {
      setBusy(false);
    }
  }

  async function beginTotpEnrollment() {
    setBusy(true);
    setMessage("");
    try {
      for (const factor of factors.filter((item) => item.factor_type === "totp" && item.status !== "verified")) {
        const { error } = await auth.unenrollFactor(factor.id);
        if (error) throw error;
      }
      const { data, error } = await auth.enrollTotp();
      if (error) throw error;
      setEnrollment({ id: data.id, type: "totp", ...data.totp });
      await beginChallenge({ id: data.id, factor_type: "totp" });
    } catch (error) {
      setMessage(friendlyError(error));
      setBusy(false);
    }
  }

  async function beginPhoneEnrollment(event) {
    event.preventDefault();
    setBusy(true);
    setMessage("");
    try {
      for (const factor of factors.filter((item) => item.factor_type === "phone" && item.status !== "verified")) {
        const { error } = await auth.unenrollFactor(factor.id);
        if (error) throw error;
      }
      const { data, error } = await auth.enrollPhone(phone.trim());
      if (error) throw error;
      setEnrollment({ id: data.id, type: "phone", phone: phone.trim() });
      await beginChallenge({ id: data.id, factor_type: "phone" });
    } catch (error) {
      setMessage(`${friendlyError(error)} Phone verification requires an SMS provider in Supabase.`);
      setBusy(false);
    }
  }

  async function verifyCode(event) {
    event.preventDefault();
    if (!challenge || !code.trim()) return;
    setBusy(true);
    setMessage("");
    try {
      const { error } = await auth.verifyFactor({ ...challenge, code: code.replace(/\s/g, "") });
      if (error) throw error;
      await loadFactors();
      setEnrollment(null);
      setChallenge(null);
      setCode("");
      setMessage("Two-factor verification complete. You can now publish designs.");
    } catch (error) {
      setMessage(friendlyError(error));
    } finally {
      setBusy(false);
    }
  }

  async function signOut() {
    setBusy(true);
    try {
      const { error } = await auth.logout();
      if (error) throw error;
    } catch (error) {
      setMessage(friendlyError(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <StudioShell eyebrow="CREATOR ACCOUNT" title="Your publishing identity">
      <section className="account-card">
        {!auth.configured ? <>
          <span className="account-icon">!</span>
          <h2>Authentication is not connected.</h2>
          <p>Add the Supabase project URL and publishable key to enable creator accounts.</p>
        </> : auth.loading ? <p>Loading your account…</p> : !auth.authenticated ? <>
          <span className="account-icon">↗</span>
          <h2>{mode === "signup" ? "Create your creator account" : "Sign in to GlassBar"}</h2>
          <p>Email confirmation protects the account. An authenticator app or phone code is required before publishing.</p>
          <form className="auth-form" onSubmit={submitCredentials}>
            {mode === "signup" && <label className="field-label">Username<input name="username" autoComplete="username" minLength="3" maxLength="24" required value={form.username} onChange={updateField} /></label>}
            <label className="field-label">Email<input name="email" type="email" autoComplete="email" required value={form.email} onChange={updateField} /></label>
            <label className="field-label">Password<input name="password" type="password" autoComplete={mode === "signup" ? "new-password" : "current-password"} minLength="8" required value={form.password} onChange={updateField} /></label>
            <button className="primary-button" disabled={busy}>{busy ? "Working…" : mode === "signup" ? "Create account" : "Sign in"}</button>
          </form>
          <button className="account-switch" type="button" onClick={() => { setMode(mode === "signup" ? "login" : "signup"); setMessage(""); }}>
            {mode === "signup" ? "Already have an account? Sign in" : "New to GlassBar? Create an account"}
          </button>
        </> : <>
          <span className="account-avatar">{(auth.user?.nickname || auth.user?.name || "U").slice(0, 1).toUpperCase()}</span>
          <h2>{auth.user?.nickname || auth.user?.name || "GlassBar creator"}</h2>
          <p>{auth.user?.email}</p>
          <dl>
            <div><dt>Email</dt><dd className={auth.user?.email_verified ? "good" : "warning"}>{auth.user?.email_verified ? "Verified" : "Needs verification"}</dd></div>
            <div><dt>Two-factor</dt><dd className={auth.mfaVerified ? "good" : "warning"}>{auth.mfaVerified ? "Verified for this session" : verifiedFactors.length ? "Verification required" : "Setup required"}</dd></div>
          </dl>

          {!auth.mfaVerified && <div className="mfa-panel">
            <h3>{verifiedFactors.length ? "Verify two-factor" : "Set up two-factor"}</h3>
            {verifiedFactors.length > 0 && !challenge && <div className="factor-list">{verifiedFactors.map((factor) => (
              <button className="secondary-button" type="button" disabled={busy} onClick={() => beginChallenge(factor)} key={factor.id}>
                Verify with {factor.factor_type === "phone" ? "phone" : "authenticator"}
              </button>
            ))}</div>}
            {verifiedFactors.length === 0 && !enrollment && <>
              <button className="secondary-button" type="button" disabled={busy} onClick={beginTotpEnrollment}>Use an authenticator app</button>
              <form className="phone-enrollment" onSubmit={beginPhoneEnrollment}>
                <label className="field-label">Phone number<input type="tel" autoComplete="tel" placeholder="+1 555 555 0123" required value={phone} onChange={(event) => setPhone(event.target.value)} /></label>
                <button className="secondary-button" disabled={busy}>Use a phone code</button>
              </form>
            </>}
            {enrollment?.type === "totp" && <div className="totp-setup">
              <p>Scan this code with your authenticator app, then enter its six-digit code.</p>
              <img className="mfa-qr" src={enrollment.qr_code} width="190" height="190" alt="Authenticator setup QR code" />
              <p className="mfa-secret">Manual key: <code>{enrollment.secret}</code></p>
            </div>}
            {challenge && <form className="verify-form" onSubmit={verifyCode}>
              <label className="field-label">Verification code<input inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,10}" required value={code} onChange={(event) => setCode(event.target.value)} /></label>
              <button className="primary-button" disabled={busy}>{busy ? "Verifying…" : "Verify"}</button>
            </form>}
          </div>}

          <div className="account-actions">
            <button className="secondary-button" disabled={busy} onClick={signOut}>Sign out</button>
          </div>
        </>}
        {message && <p className="account-message" role="status">{message}</p>}
      </section>
    </StudioShell>
  );
}
