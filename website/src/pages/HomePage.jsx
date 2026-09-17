import { Link } from "react-router-dom";
import GlassBarPreview from "../components/GlassBarPreview";
import { cloneDesign } from "../lib/designSchema";

const homeDesign = cloneDesign();

export default function HomePage() {
  return (
    <main className="home-stage" aria-label="GlassBar download">
      <GlassBarPreview design={homeDesign} label="Example GlassBar" />
      <nav className="home-links" aria-label="GlassBar links">
        <a href="https://github.com/Sing2236/GlassBar/releases/latest/download/GlassBarSetup.exe">Download</a>
        <Link to="/studio">Community</Link>
        <a href="https://www.paypal.com/cgi-bin/webscr?cmd=_donations&amp;business=Ethanhuynh365%40gmail.com&amp;currency_code=USD" target="_blank" rel="noreferrer">Support</a>
      </nav>
    </main>
  );
}
