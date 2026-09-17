const moduleLabels = {
  start: "start",
  search: "Search",
  apps: "apps",
  tray: "⌃",
  clock: "4:51"
};

function StartMark() {
  return <span className="preview-start" aria-hidden="true"><i /><i /><i /><i /></span>;
}

function Apps() {
  return <span className="preview-apps" aria-hidden="true"><i className="folder" /><i className="browser" /><i className="store" /></span>;
}

function Particles({ animation }) {
  return (
    <span className={`preview-particles motion-${animation.motion} shape-${animation.shape}`} aria-hidden="true">
      {Array.from({ length: 18 }, (_, index) => (
        <i key={index} style={{
          "--i": index,
          left: `${(index * 17 + 5) % 100}%`,
          "--top": `${(index * 13 + 7) % 86}%`,
          "--particle": animation.primaryColor,
          "--particle-two": animation.secondaryColor,
          "--speed": `${Math.max(1.2, 5.2 - animation.speed / 20)}s`,
          "--size": `${Math.max(2, animation.size / 12)}px`,
          "--glow": `${animation.glow / 10}px`,
          opacity: Math.max(.15, animation.density / 100)
        }} />
      ))}
    </span>
  );
}

function WidgetPreview({ widget }) {
  return (
    <div className="preview-widget" style={{
      width: widget.width,
      borderRadius: widget.radius,
      background: widget.background,
      "--widget-accent": widget.accent
    }}>
      <span className={`widget-symbol symbol-${widget.icon}`} aria-hidden="true" />
      <span><small>{widget.title}</small><strong>{widget.value}</strong><em>{widget.detail}</em></span>
    </div>
  );
}

export default function GlassBarPreview({ design, label = "GlassBar design preview", compact = false }) {
  const { glassbar, widget, animation, kind } = design;
  const vertical = glassbar.orientation === "vertical";
  const style = {
    width: vertical ? Math.min(90, glassbar.height + 12) : glassbar.width,
    height: vertical ? Math.min(500, glassbar.width * .62) : glassbar.height,
    borderRadius: glassbar.radius,
    opacity: glassbar.opacity / 100,
    background: `linear-gradient(135deg, ${glassbar.backgroundStart}, ${glassbar.backgroundEnd})`,
    borderColor: `${glassbar.border}99`,
    boxShadow: `0 ${Math.round(glassbar.shadow / 2)}px ${glassbar.shadow}px rgba(21,34,52,.2)`,
    backdropFilter: `blur(${glassbar.blur}px) saturate(150%)`,
    "--preview-accent": glassbar.accent
  };

  return (
    <div className={`preview-stage ${compact ? "is-compact" : ""}`} aria-label={label}>
      <div className={`preview-bar ${vertical ? "is-vertical" : ""}`} style={style}>
        {(kind === "animation" || kind === "glassbar") && <Particles animation={animation} />}
        {kind === "widget" ? <WidgetPreview widget={widget} /> : glassbar.modules.map((module) => {
          if (module === "start") return <StartMark key={module} />;
          if (module === "apps") return <Apps key={module} />;
          if (module === "search") return <span className="preview-search" key={module}>⌕ <span>{moduleLabels[module]}</span></span>;
          if (module === "clock") return <time className="preview-clock" key={module}>4:51<small>9/16/2026</small></time>;
          return <span className="preview-tray" key={module}>{moduleLabels[module]}</span>;
        })}
      </div>
    </div>
  );
}
