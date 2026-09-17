import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import StudioShell from "../components/StudioShell";
import GlassBarPreview from "../components/GlassBarPreview";
import { seedDesigns } from "../data/seedDesigns";
import { useStudioAuth } from "../auth/StudioAuth";
import { createStudioRepository } from "../lib/studioRepository";
import { downloadPackage } from "../lib/designSchema";

const sections = [
  { kind: "glassbar", title: "GlassBars", copy: "Complete bar layouts, materials, sizing, and module arrangements." },
  { kind: "widget", title: "Widgets", copy: "Focused additions for time, system status, weather, media, and more." },
  { kind: "animation", title: "Animations", copy: "Particle motion, ambient effects, trails, colors, and timing." }
];

function DesignCard({ item }) {
  return (
    <article className="design-card">
      <GlassBarPreview design={item.document} compact label={`${item.name} preview`} />
      <div className="design-card-copy">
        <div><span className="kind-label">{item.kind}</span><span className="download-count">↓ {Number(item.downloads || 0).toLocaleString()}</span></div>
        <h3>{item.name}</h3>
        <p>{item.summary}</p>
        <footer><span>by {item.author}</span><button onClick={() => downloadPackage(item.document)}>Get design</button></footer>
      </div>
    </article>
  );
}

export default function ExplorePage() {
  const auth = useStudioAuth();
  const repository = useMemo(() => createStudioRepository(), []);
  const [designs, setDesigns] = useState(seedDesigns);
  const [query, setQuery] = useState("");

  useEffect(() => {
    if (!repository) return;
    repository.listPublished().then((remote) => {
      if (remote.length) setDesigns(remote);
    }).catch(() => {});
  }, [repository]);

  const filtered = designs.filter((item) => `${item.name} ${item.summary} ${item.author}`.toLowerCase().includes(query.toLowerCase()));

  return (
    <StudioShell
      eyebrow="COMMUNITY LIBRARY"
      title="Make the bar yours."
      actions={<Link className="primary-button small" to="/studio/editor">New design</Link>}
    >
      <div className="explore-toolbar">
        <p>Install a design as-is, or open its package and keep editing.</p>
        <label className="search-field"><span>⌕</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search designs" aria-label="Search designs" /></label>
      </div>
      {sections.map((section) => {
        const items = filtered.filter((item) => item.kind === section.kind);
        return (
          <section className="gallery-section" key={section.kind} aria-labelledby={`${section.kind}-title`}>
            <div className="section-heading"><div><h2 id={`${section.kind}-title`}>{section.title}</h2><p>{section.copy}</p></div><span>{items.length} designs</span></div>
            <div className="design-grid">{items.map((item) => <DesignCard item={item} key={item.id} />)}</div>
          </section>
        );
      })}
    </StudioShell>
  );
}
