"use client";
import { useState, useEffect, useRef } from "react";

// ─── WMS Model lagen (Dinoloket / PDOK) ──────────────────────────────────────
const BRO_WMS_LAGEN = [
  {
    id: "dgm",
    label: "BRO DGM v2.2.1",
    subtitel: "Digitaal Geologisch Model",
    kleur: "#B45309",
    wmsUrl: "https://service.pdok.nl/bzk/bro-dgm/wms/v1_0",
    layer: "dgm",
    opacity: 0.6,
    icon: "🪨",
  },
  {
    id: "geotop",
    label: "BRO GeoTOP v1.6.1",
    subtitel: "3D voxelmodel geologie",
    kleur: "#7C3AED",
    wmsUrl: "https://service.pdok.nl/bzk/bro-geotop/wms/v1_0",
    layer: "geotop",
    opacity: 0.6,
    icon: "🧭",
  },
  {
    id: "regis",
    label: "BRO REGIS II v2.2.3",
    subtitel: "Hydrogeologisch model",
    kleur: "#0891B2",
    wmsUrl: "https://service.pdok.nl/bzk/bro-hydrogeology/wms/v1_0",
    layer: "regisii",
    opacity: 0.6,
    icon: "💎",
  },
  {
    id: "geomorf",
    label: "BRO Geomorfologie 2025",
    subtitel: "Landvormen en processen",
    kleur: "#059669",
    wmsUrl: "https://service.pdok.nl/bzk/bro-geomorfologischekaart/wms/v2_0",
    layer: "geomorfologischekaart",
    opacity: 0.6,
    icon: "🏔️",
  },
  {
    id: "bodemkaart",
    label: "BRO Bodemkaart 2025",
    subtitel: "Bodemopbouw tot 1.2m",
    kleur: "#84CC16",
    wmsUrl: "https://service.pdok.nl/bzk/bro-bodemkaart/wms/v1_0",
    layer: "bodemkaart",
    opacity: 0.6,
    icon: "🌱",
  },
  {
    id: "gwsd",
    label: "BRO Grondwaterspiegeldiepte",
    subtitel: "Gemiddelde GHG/GLG",
    kleur: "#3B82F6",
    wmsUrl: "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v1_0",
    layer: "grondwaterspiegeldiepte",
    opacity: 0.6,
    icon: "💧",
  },
];

// ─── Puntdata typen ───────────────────────────────────────────────────────────
const PUNT_TYPEN = {
  cpt: { label: "Sonderingen (CPT)", kleur: "#F97316", icon: "▼", omschrijving: "Kegelweerstand per diepte" },
  bhr: { label: "Boorprofielen",     kleur: "#7C3AED", icon: "⬡", omschrijving: "Grondlagen met beschrijving" },
  gmw: { label: "Peilbuizen (GMW)",  kleur: "#2563EB", icon: "◉", omschrijving: "Grondwaterstand monitoring" },
};

export default function BROlaag({ project, kaartRef }) {
  const [puntData,   setPuntData]   = useState(null);
  const [bezig,      setBezig]      = useState(false);
  const [fout,       setFout]       = useState(null);
  const [wmsAan,     setWmsAan]     = useState({});         // { dgm: true, ... }
  const [puntAan,    setPuntAan]    = useState({ cpt: true, bhr: true, gmw: true });
  const [geselecteerd, setGeselecteerd] = useState(null);
  const [laagFout,   setLaagFout]   = useState({});         // { dgm: "fout", ... }
  const wmsLagenRef = useRef({});
  const puntLagenRef = useRef({});

  // ── WMS laag aanzetten ────────────────────────────────────────────
  function toggleWms(id) {
    const map = kaartRef?.current;
    if (!map || !window.L) return;
    const L = window.L;
    const def = BRO_WMS_LAGEN.find(l => l.id === id);
    if (!def) return;

    if (wmsLagenRef.current[id]) {
      map.removeLayer(wmsLagenRef.current[id]);
      delete wmsLagenRef.current[id];
      setWmsAan(v => ({ ...v, [id]: false }));
      return;
    }

    // Maak bgtPane-achtige pane voor BRO (onder boorlijn)
    if (!map.getPane("broWmsPane")) {
      map.createPane("broWmsPane");
      map.getPane("broWmsPane").style.zIndex = 280;
    }

    const laag = L.tileLayer.wms(def.wmsUrl, {
      layers: def.layer,
      format: "image/png",
      transparent: true,
      opacity: def.opacity,
      version: "1.1.1",
      pane: "broWmsPane",
    });

    laag.on("tileerror", () => {
      setLaagFout(v => ({ ...v, [id]: "WMS niet bereikbaar — probeer later" }));
    });
    laag.on("tileload", () => {
      setLaagFout(v => { const n = { ...v }; delete n[id]; return n; });
    });

    laag.addTo(map);
    wmsLagenRef.current[id] = laag;
    setWmsAan(v => ({ ...v, [id]: true }));
  }

  // ── Puntdata ophalen ──────────────────────────────────────────────
  async function laadPuntData() {
    if (!project?.boortrace_geojson) { setFout("Teken eerst een boorlijn (stap 4)"); return; }
    setBezig(true); setFout(null);
    verwijderPuntMarkers();

    try {
      const gj = typeof project.boortrace_geojson === "string"
        ? JSON.parse(project.boortrace_geojson) : project.boortrace_geojson;
      const coords = gj?.coordinates ?? [];
      if (coords.length < 2) { setFout("Boorlijn heeft te weinig punten"); return; }

      const lats = coords.map(c => c[1]), lngs = coords.map(c => c[0]);
      const params = new URLSearchParams({
        minLat: Math.min(...lats), maxLat: Math.max(...lats),
        minLng: Math.min(...lngs), maxLng: Math.max(...lngs),
      });
      const res = await fetch(`/api/bro?${params}`);
      if (!res.ok) throw new Error("BRO API niet bereikbaar");
      const json = await res.json();
      if (json.error) throw new Error(json.error);
      setPuntData(json);
      Object.entries(json).forEach(([type, items]) => {
        if (items?.length) toonPuntMarkers(type, items);
      });
    } catch (e) {
      setFout(e.message);
    } finally {
      setBezig(false);
    }
  }

  function verwijderPuntMarkers() {
    const map = kaartRef?.current;
    if (!map) return;
    Object.values(puntLagenRef.current).forEach(lg => { try { map.removeLayer(lg); } catch {} });
    puntLagenRef.current = {};
  }

  function toonPuntMarkers(type, items) {
    const map = kaartRef?.current;
    if (!map || !window.L) return;
    const L = window.L;
    const cfg = PUNT_TYPEN[type];

    const markers = items.map(item => {
      const icon = L.divIcon({
        className: "",
        html: `<div style="width:20px;height:20px;background:${cfg.kleur};border:2.5px solid white;border-radius:50%;box-shadow:0 2px 6px rgba(0,0,0,0.3);display:flex;align-items:center;justify-content:center;font-size:8px;color:white;font-weight:900">${cfg.icon}</div>`,
        iconSize: [20, 20],
        iconAnchor: [10, 10],
      });
      return L.marker([item.lat, item.lng], { icon })
        .on("click", () => setGeselecteerd({ ...item, type }))
        .bindTooltip(`<strong>${cfg.label}</strong><br/>${item.id}${item.diepte ? `<br/>⬇ ${item.diepte}m` : ""}`, { direction: "top" });
    });

    const groep = L.layerGroup(markers);
    if (puntAan[type]) groep.addTo(map);
    puntLagenRef.current[type] = groep;
  }

  function togglePuntType(type) {
    const map = kaartRef?.current;
    const laag = puntLagenRef.current[type];
    const nieuw = !puntAan[type];
    setPuntAan(v => ({ ...v, [type]: nieuw }));
    if (!map || !laag) return;
    if (nieuw) laag.addTo(map); else map.removeLayer(laag);
  }

  useEffect(() => () => {
    verwijderPuntMarkers();
    const map = kaartRef?.current;
    if (map) Object.values(wmsLagenRef.current).forEach(l => { try { map.removeLayer(l); } catch {} });
  }, []);

  const totaalPunten = puntData ? Object.values(puntData).reduce((s, a) => s + a.length, 0) : 0;

  return (
    <div style={{ fontFamily: "system-ui, sans-serif", fontSize: 12 }}>

      {/* ── WMS Model lagen ──────────────────────────────────── */}
      <div style={{ marginBottom: 16 }}>
        <div style={{ fontWeight: 700, fontSize: 13, color: "#1F2937", marginBottom: 4 }}>🗺️ BRO Modellagen (Dinoloket)</div>
        <div style={{ fontSize: 11, color: "#6B7280", marginBottom: 10 }}>Klik om een laag aan/uit te zetten op de kaart</div>
        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
          {BRO_WMS_LAGEN.map(laag => {
            const aan = !!wmsAan[laag.id];
            return (
              <div key={laag.id} style={{
                display: "flex", alignItems: "center", gap: 10,
                padding: "8px 12px",
                background: aan ? laag.kleur + "15" : "white",
                border: `1.5px solid ${aan ? laag.kleur : "#E5E7EB"}`,
                borderRadius: 8, cursor: "pointer", transition: "all 0.15s",
              }} onClick={() => toggleWms(laag.id)}>
                <span style={{ fontSize: 18 }}>{laag.icon}</span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600, fontSize: 11, color: aan ? laag.kleur : "#374151" }}>{laag.label}</div>
                  <div style={{ fontSize: 10, color: "#9CA3AF" }}>{laag.subtitel}</div>
                  {laagFout[laag.id] && <div style={{ fontSize: 10, color: "#DC2626" }}>⚠️ {laagFout[laag.id]}</div>}
                </div>
                {/* Toggle switch */}
                <div style={{
                  width: 36, height: 20, borderRadius: 10,
                  background: aan ? laag.kleur : "#E5E7EB",
                  position: "relative", flexShrink: 0, transition: "background 0.15s",
                }}>
                  <div style={{
                    width: 16, height: 16, borderRadius: "50%", background: "white",
                    position: "absolute", top: 2,
                    left: aan ? 18 : 2, transition: "left 0.15s",
                    boxShadow: "0 1px 3px rgba(0,0,0,0.2)",
                  }}/>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* ── Puntdata: CPT, Boringen, Peilbuizen ──────────────── */}
      <div style={{ borderTop: "1px solid #E5E7EB", paddingTop: 14, marginTop: 4 }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 8 }}>
          <div>
            <div style={{ fontWeight: 700, fontSize: 13, color: "#1F2937" }}>📍 BRO Puntdata</div>
            <div style={{ fontSize: 10, color: "#6B7280" }}>Sonderingen · Boringen · Peilbuizen nabij tracé (~800m)</div>
          </div>
          <button onClick={laadPuntData} disabled={bezig} style={{
            padding: "6px 12px", background: bezig ? "#E5E7EB" : "#F97316",
            color: bezig ? "#9CA3AF" : "white", border: "none",
            borderRadius: 7, cursor: bezig ? "default" : "pointer",
            fontWeight: 600, fontSize: 11, display: "flex", alignItems: "center", gap: 5,
          }}>
            {bezig
              ? <><div style={{ width: 10, height: 10, border: "2px solid rgba(0,0,0,0.1)", borderTop: "2px solid #9CA3AF", borderRadius: "50%", animation: "spin 0.8s linear infinite" }}/>Ophalen...</>
              : "🔍 Ophalen"}
          </button>
        </div>

        {fout && (
          <div style={{ background: "#FEF2F2", border: "1px solid #FCA5A5", borderRadius: 8, padding: "8px 12px", color: "#DC2626", marginBottom: 8, fontSize: 11 }}>
            ❌ {fout}
            <div style={{ marginTop: 4, fontSize: 10, color: "#9CA3AF" }}>
              Tip: PDOK WFS diensten zijn soms tijdelijk niet beschikbaar. Probeer opnieuw.
            </div>
          </div>
        )}

        {puntData && (
          <>
            <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginBottom: 10 }}>
              {Object.entries(PUNT_TYPEN).map(([type, cfg]) => {
                const n = puntData[type]?.length ?? 0;
                const aan = puntAan[type];
                return (
                  <button key={type} onClick={() => n > 0 && togglePuntType(type)} style={{
                    display: "flex", alignItems: "center", gap: 5, padding: "5px 10px",
                    background: aan && n > 0 ? cfg.kleur + "15" : "#F9FAFB",
                    border: `1.5px solid ${aan && n > 0 ? cfg.kleur : "#E5E7EB"}`,
                    borderRadius: 7, cursor: n > 0 ? "pointer" : "default", opacity: n > 0 ? 1 : 0.6,
                  }}>
                    <div style={{ width: 8, height: 8, borderRadius: "50%", background: n > 0 ? cfg.kleur : "#D1D5DB" }}/>
                    <span style={{ fontSize: 10, fontWeight: 600, color: n > 0 ? cfg.kleur : "#9CA3AF" }}>{cfg.label}</span>
                    <span style={{ fontSize: 9, fontWeight: 700, background: n > 0 ? cfg.kleur : "#E5E7EB", color: n > 0 ? "white" : "#9CA3AF", borderRadius: 8, padding: "0 4px" }}>{n}</span>
                  </button>
                );
              })}
              {totaalPunten === 0 && (
                <div style={{ fontSize: 11, color: "#9CA3AF", fontStyle: "italic" }}>
                  Geen puntdata gevonden in dit gebied. Dit kan kloppen voor gebieden zonder sonderingen in BRO.
                </div>
              )}
            </div>

            {/* Lijst */}
            {Object.entries(PUNT_TYPEN).map(([type, cfg]) => {
              const items = puntData[type] ?? [];
              if (!items.length) return null;
              return (
                <div key={type} style={{ marginBottom: 10, border: `1px solid ${cfg.kleur}30`, borderLeft: `4px solid ${cfg.kleur}`, borderRadius: 8, overflow: "hidden" }}>
                  <div style={{ background: cfg.kleur + "15", padding: "6px 10px", fontWeight: 700, fontSize: 11, color: cfg.kleur }}>
                    {cfg.icon} {cfg.label} ({items.length})
                  </div>
                  <div style={{ maxHeight: 160, overflowY: "auto" }}>
                    {items.map(item => (
                      <div key={item.id}
                        onClick={() => setGeselecteerd({ ...item, type })}
                        style={{
                          padding: "6px 10px", borderBottom: "1px solid #F9FAFB",
                          cursor: "pointer", background: geselecteerd?.id === item.id ? cfg.kleur + "10" : "white",
                        }}>
                        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                          <span style={{ fontWeight: 600, color: "#374151", fontSize: 11 }}>{item.id}</span>
                          <div style={{ display: "flex", gap: 6 }}>
                            {item.kwaliteit && <span style={{ fontSize: 9, background: "#F3F4F6", color: "#6B7280", borderRadius: 4, padding: "1px 5px" }}>{item.kwaliteit}</span>}
                            {item.diepte && <span style={{ fontSize: 10, color: "#9CA3AF" }}>⬇ {item.diepte}m</span>}
                          </div>
                        </div>
                        {(item.datum || item.bronhouder) && (
                          <div style={{ fontSize: 10, color: "#9CA3AF", marginTop: 1 }}>
                            {item.datum && `📅 ${item.datum.split("T")[0]}`}{item.datum && item.bronhouder && " · "}{item.bronhouder && `🏢 ${item.bronhouder}`}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              );
            })}
          </>
        )}

        {!puntData && !bezig && (
          <div style={{ textAlign: "center", padding: "16px 0", color: "#9CA3AF", fontSize: 11 }}>
            Klik "Ophalen" voor sonderingen, boringen en peilbuizen nabij het tracé
          </div>
        )}
      </div>

      {/* ── Geselecteerd detail ───────────────────────────────── */}
      {geselecteerd && (
        <div style={{ marginTop: 12, border: "1.5px solid #E5E7EB", borderRadius: 10, overflow: "hidden" }}>
          <div style={{ background: PUNT_TYPEN[geselecteerd.type]?.kleur + "15", padding: "8px 12px", display: "flex", justifyContent: "space-between" }}>
            <span style={{ fontWeight: 700, color: PUNT_TYPEN[geselecteerd.type]?.kleur, fontSize: 12 }}>
              {PUNT_TYPEN[geselecteerd.type]?.icon} {geselecteerd.id}
            </span>
            <div style={{ display: "flex", gap: 10 }}>
              <a href={`https://www.dinoloket.nl/ondergrondgegevens?zoekveld=${geselecteerd.id}`}
                target="_blank" rel="noopener noreferrer"
                style={{ fontSize: 10, color: "#7C3AED", textDecoration: "none", fontWeight: 600 }}>
                🔗 DINOloket
              </a>
              <a href={`https://www.broloket.nl/ondergrondgegevens?zoekveld=${geselecteerd.id}`}
                target="_blank" rel="noopener noreferrer"
                style={{ fontSize: 10, color: "#3B82F6", textDecoration: "none", fontWeight: 600 }}>
                🔗 BROloket
              </a>
              <button onClick={() => setGeselecteerd(null)} style={{ background: "none", border: "none", cursor: "pointer", color: "#9CA3AF", fontSize: 14 }}>✕</button>
            </div>
          </div>
          <div style={{ padding: "10px 12px", display: "grid", gridTemplateColumns: "1fr 1fr", gap: 6 }}>
            {geselecteerd.diepte     && <KV label="Diepte"       waarde={`${geselecteerd.diepte}m`}/>}
            {geselecteerd.kwaliteit  && <KV label="Kwaliteit"    waarde={geselecteerd.kwaliteit}/>}
            {geselecteerd.bronhouder && <KV label="Bronhouder"   waarde={geselecteerd.bronhouder}/>}
            {geselecteerd.datum      && <KV label="Datum"        waarde={geselecteerd.datum.split("T")[0]}/>}
            {geselecteerd.naam       && <KV label="Naam/locatie" waarde={geselecteerd.naam}/>}
            <div style={{ gridColumn: "span 2", fontSize: 10, color: "#9CA3AF", marginTop: 4 }}>
              Volledige meetdata beschikbaar via DINOloket en BROloket
            </div>
          </div>
        </div>
      )}

      <style>{`@keyframes spin{to{transform:rotate(360deg)}}`}</style>
    </div>
  );
}

function KV({ label, waarde }) {
  return (
    <div style={{ background: "#F9FAFB", borderRadius: 6, padding: "4px 8px" }}>
      <div style={{ fontSize: 9, color: "#9CA3AF", textTransform: "uppercase", letterSpacing: "0.04em" }}>{label}</div>
      <div style={{ fontWeight: 600, color: "#1F2937", fontSize: 11 }}>{waarde}</div>
    </div>
  );
}
