import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { studioBackendConfigured, supabase } from "../lib/supabaseClient";

const AuthContext = createContext(null);
export const authConfigured = studioBackendConfigured;

export function StudioAuthProvider({ children }) {
  const [session, setSession] = useState(null);
  const [loading, setLoading] = useState(authConfigured);
  const [assuranceLevel, setAssuranceLevel] = useState("aal1");

  const refreshAssurance = useCallback(async () => {
    if (!supabase) return setAssuranceLevel("aal1");
    const { data } = await supabase.auth.mfa.getAuthenticatorAssuranceLevel();
    setAssuranceLevel(data?.currentLevel || "aal1");
  }, []);

  useEffect(() => {
    if (!supabase) return;
    let active = true;
    supabase.auth.getSession()
      .then(({ data }) => {
        if (!active) return;
        setSession(data.session);
        setLoading(false);
        if (data.session) refreshAssurance();
      })
      .catch(() => {
        if (active) setLoading(false);
      });
    const { data: listener } = supabase.auth.onAuthStateChange((_event, nextSession) => {
      setSession(nextSession);
      setLoading(false);
      if (nextSession) queueMicrotask(refreshAssurance);
      else setAssuranceLevel("aal1");
    });
    return () => {
      active = false;
      listener.subscription.unsubscribe();
    };
  }, [refreshAssurance]);

  const rawUser = session?.user || null;
  const user = rawUser ? {
    ...rawUser,
    nickname: rawUser.user_metadata?.username || rawUser.email?.split("@")[0],
    name: rawUser.user_metadata?.display_name || rawUser.user_metadata?.username,
    picture: rawUser.user_metadata?.avatar_url || null,
    email_verified: Boolean(rawUser.email_confirmed_at)
  } : null;

  const value = useMemo(() => ({
    configured: authConfigured,
    loading,
    authenticated: Boolean(session),
    mfaVerified: assuranceLevel === "aal2",
    user,
    signup: async ({ email, password, username }) => supabase.auth.signUp({
      email,
      password,
      options: {
        emailRedirectTo: `${window.location.origin}/studio/account`,
        data: { username }
      }
    }),
    login: async ({ email, password }) => supabase.auth.signInWithPassword({ email, password }),
    logout: async () => supabase.auth.signOut(),
    listFactors: async () => supabase.auth.mfa.listFactors(),
    enrollTotp: async () => supabase.auth.mfa.enroll({ factorType: "totp", friendlyName: "GlassBar Studio" }),
    enrollPhone: async (phone) => supabase.auth.mfa.enroll({ factorType: "phone", phone, friendlyName: "GlassBar Studio phone" }),
    challengeFactor: async (factorId) => supabase.auth.mfa.challenge({ factorId }),
    unenrollFactor: async (factorId) => supabase.auth.mfa.unenroll({ factorId }),
    verifyFactor: async ({ factorId, challengeId, code }) => {
      const result = await supabase.auth.mfa.verify({ factorId, challengeId, code });
      if (!result.error) await refreshAssurance();
      return result;
    },
    refreshAssurance
  }), [assuranceLevel, loading, refreshAssurance, session, user]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useStudioAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useStudioAuth must be used inside StudioAuthProvider");
  return value;
}
