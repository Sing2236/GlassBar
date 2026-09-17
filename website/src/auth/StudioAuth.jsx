import { createContext, useContext, useMemo } from "react";
import { Auth0Provider, useAuth0 } from "@auth0/auth0-react";

const AuthContext = createContext(null);
const domain = import.meta.env.VITE_AUTH0_DOMAIN;
const clientId = import.meta.env.VITE_AUTH0_CLIENT_ID;
export const authConfigured = Boolean(domain && clientId);

function Auth0Bridge({ children }) {
  const auth = useAuth0();
  const amr = Array.isArray(auth.user?.amr) ? auth.user.amr : [];
  const value = useMemo(() => ({
    configured: true,
    loading: auth.isLoading,
    authenticated: auth.isAuthenticated,
    mfaVerified: amr.includes("mfa"),
    user: auth.user,
    getIdToken: async () => (await auth.getIdTokenClaims())?.__raw,
    login: () => auth.loginWithRedirect(),
    signup: () => auth.loginWithRedirect({ authorizationParams: { screen_hint: "signup" } }),
    logout: () => auth.logout({ logoutParams: { returnTo: window.location.origin } })
  }), [auth, amr.join("|")]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

function UnconfiguredAuth({ children }) {
  const value = useMemo(() => ({
    configured: false,
    loading: false,
    authenticated: false,
    mfaVerified: false,
    user: null,
    getIdToken: async () => null,
    login: () => {},
    signup: () => {},
    logout: () => {}
  }), []);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function StudioAuthProvider({ children }) {
  if (!authConfigured) return <UnconfiguredAuth>{children}</UnconfiguredAuth>;
  return (
    <Auth0Provider
      domain={domain}
      clientId={clientId}
      authorizationParams={{ redirect_uri: window.location.origin, scope: "openid profile email" }}
      useRefreshTokens
      useRefreshTokensFallback
    >
      <Auth0Bridge>{children}</Auth0Bridge>
    </Auth0Provider>
  );
}

export function useStudioAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useStudioAuth must be used inside StudioAuthProvider");
  return value;
}
