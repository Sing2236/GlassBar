import { NavLink } from "react-router-dom";
import { useStudioAuth } from "../auth/StudioAuth";

export default function StudioShell({ children, title, eyebrow, actions }) {
  const auth = useStudioAuth();
  return (
    <div className="studio-app">
      <header className="studio-header">
        <NavLink to="/" className="wordmark" aria-label="GlassBar home"><span className="mini-mark"><i /><i /><i /><i /></span>GlassBar</NavLink>
        <nav aria-label="Studio navigation">
          <NavLink to="/studio" end>Explore</NavLink>
          <NavLink to="/studio/editor">Create</NavLink>
        </nav>
        <div className="header-actions">
          {actions}
          {auth.authenticated ? (
            <NavLink className="avatar-link" to="/studio/account" aria-label="Account">{(auth.user?.nickname || auth.user?.name || "U").slice(0, 1).toUpperCase()}</NavLink>
          ) : (
            <button className="text-button" onClick={auth.login} disabled={!auth.configured}>Sign in</button>
          )}
        </div>
      </header>
      <main className="studio-main">
        {(title || eyebrow) && <div className="page-heading"><p>{eyebrow}</p><h1>{title}</h1></div>}
        {children}
      </main>
    </div>
  );
}
