/**
 * Auth0 Post Login Action for GlassBar Studio.
 * Enable Email and SMS factors in Security > Multi-factor Auth, then set the
 * MFA policy to Always. This action supplies the Supabase database role claim.
 */
exports.onExecutePostLogin = async (event, api) => {
  api.idToken.setCustomClaim("role", "authenticated");

  const usedMfa = event.authentication?.methods?.some((method) => method.name === "mfa");
  if (!usedMfa) {
    api.multifactor.enable("any", { allowRememberBrowser: false });
  }
};
