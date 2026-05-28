"use client";
import { useState, useEffect, useRef } from "react";

const TYPE_CFG = {
  cpt:  { label:"Sondering (CPT)",    kleur:"#F97316", achtergrond:"#FFF7ED", icoontje:"▼", omschrijving:"Kegelweerstand & grondopbouw" },
  bhrp: { label:"Boorprofiel",        kleur:"#7C3AED", achtergrond:"#F5F3FF", icoontje:"⬡", omschrijving:"Grondlagen met beschrijving" },
  bhrg: { label:"Geotechn. boring",   kleur:"#059669", achtergrond:"#ECFDF5", icoontje:"◈", omschrijving:"Geotechnisch booronderzoek" },
};

function RisicoChip({ waarde }) {
  const kleur = waarde === "klasse1" || waarde === "klasse2" ? "#16A34A" : waarde === "klasse3" ? "#F59E0B" : "#9CA3AF";
  return waarde ? <span style={{ fontSize:10, background:kleur+"20", color:kleur, border:`1px solid ${kleur}50`, borderRadius:10, padding:"1px 6px" }}>{waarde}</span> : null;
}

export default function BROlaag({ project, kaartRef }) {
  const [data,       setData]       = useState(null);   // { cpt:[], bhrp:[], bhrg:[] }
  const [bezig,      setBezig]      = useState(false);
  const [fout,       setFout]       = useState(null);
  const [zichtbaar,  setZichtbaar]  = useState({ cpt: true, bhrp: true, bhrg: true });
  const [geselecteerd, setGeselecteerd] = useState(null);
  const [detail,     setDetail]     = useState(null);
  const [detailBezig, setDetailBezig] = useState(false);
  const lagenRef = useRef({});

  // Haal BRO data op langs het tracé
  async function laadBRO() {
    if (!project?.boortrace_geojson) { setFout("Teken eerst een boorlijn (stap 4)"); return; }
    setBezig(true); setFout(null); setData(null);
    verwijderAlleMarkers();

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
      setData(json);
      // Teken markers
      Object.entries(json).forEach(([type, items]) => toonOpKaart(type, items));
    } catch (e) {
      setFout(e.message);
    } finally {
      setBezig(false);
    }
  }

  function verwijderAlleMarkers() {
    const map = kaartRef?.current;
    if (!map) return;
    Object.values(lagenRef.current).forEach(lg => map.removeLayer(lg));
    lagenRef.current = {};
  }

  function toonOpKaart(type, items) {
    const map = kaartRef?.current;
    if (!map || !window.L || !items?.length) return;
    const L = window.L;
    const cfg = TYPE_CFG[type];

    const markers = items.map(item => {
      const icon = L.divIcon({
        className: "",
        html: `<div style="width:22px;height:22px;background:${cfg.kleur};border:2px solid white;border-radius:50% 50% 50% 0;transform:rotate(-45deg);box-shadow:0 2px 6px rgba(0,0,0,0.25);display:flex;align-items:center;justify-content:center">
          <span style="transform:rotate(45deg);font-size:9px;color:white;font-weight:700">${cfg.icoontje}</span>
        </div>`,
        iconSize: [22, 22],
        iconAnchor: [11, 22],
      });

      return L.marker([item.lat, item.lng], { icon })
        .on("click", () => {
          setGeselecteerd(item);
          setDetail(null);
          laadDetail(type, item.id);
        })
        .bindTooltip(`<strong>${cfg.label}</strong><br/>${item.id}${item.diepte ? `<br/>Diepte: ${item.diepte}m` : ""}`, { direction: "top" });
    });

    const groep = L.layerGroup(markers);
    if (zichtbaar[type]) groep.addTo(map);
    lagenRef.current[type] = groep;
  }

  async function laadDetail(type, broId) {
    setDetailBezig(true);
    try {
      const res = await fetch(`/api/bro?detail=true&type=${type}&broId=${encodeURIComponent(broId)}`);
      const json = await res.json();
      setDetail(json);
    } catch {
      setDetail({ error: "Detail niet beschikbaar" });
    } finally {
      setDetailBezig(false);
    }
  }

  function toggleType(type) {
    const map = kaartRef?.current;
    const laag = lagenRef.current[type];
    const nieuw = !zichtbaar[type];
    setZichtbaar(v => ({ ...v, [type]: nieuw }));
    if (!map || !laag) return;
    if (nieuw) laag.addTo(map); else map.removeLayer(laag);
  }

  // Cleanup bij unmount
  useEffect(() => () => verwijderAlleMarkers(), []);

  const totaal = data ? Object.values(data).reduce((s, arr) => s + arr.length, 0) : 0;

  return (
    <div style={{ fontFamily: "system-ui, sans-serif", fontSize: 12 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 12 }}>
        <div>
          <div style={{ fontWeight: 700, fontSize: 14, color: "#1F2937" }}>🏗️ BRO Geotechnisch</div>
          <div style={{ fontSize: 11, color: "#6B7280" }}>Sonderingen en boorprofielen nabij het tracé (gratis BRO API)</div>
        </div>
        <button onClick={laadBRO} disabled={bezig} style={{
          padding: "7px 14px", background: bezig ? "#E5E7EB" : "#F97316",
          color: bezig ? "#9CA3AF" : "white", border: "none", borderRadius: 8,
          cursor: bezig ? "default" : "pointer", fontWeight: 600, fontSize: 12,
          display: "flex", alignItems: "center", gap: 6,
        }}>
          {bezig ? <><div style={{ width: 12, height: 12, border: "2px solid rgba(0,0,0,0.1)", borderTop: "2px solid #9CA3AF", borderRadius: "50%", animation: "spin 0.8s linear infinite" }}/> Ophalen...</> : "🔍 Ophalen uit BRO"}
        </button>
      </div>

      {fout && <div style={{ background: "#FEF2F2", border: "1px solid #FCA5A5", borderRadius: 8, padding: "8px 12px", color: "#DC2626", marginBottom: 12 }}>❌ {fout}</div>}

      {/* Resultaten */}
      {data && (
        <>
          {/* Samenvatting + toggle */}
          <div style={{ display: "flex", gap: 8, marginBottom: 12, flexWrap: "wrap" }}>
            {Object.entries(TYPE_CFG).map(([type, cfg]) => {
              const n = data[type]?.length ?? 0;
              const aan = zichtbaar[type];
              return (
                <button key={type} onClick={() => n > 0 && toggleType(type)} style={{
                  display: "flex", alignItems: "center", gap: 6, padding: "5px 10px",
                  background: aan && n > 0 ? cfg.achtergrond : "#F9FAFB",
                  border: `1.5px solid ${aan && n > 0 ? cfg.kleur : "#E5E7EB"}`,
                  borderRadius: 8, cursor: n > 0 ? "pointer" : "default",
                  opacity: n > 0 ? 1 : 0.5,
                }}>
                  <div style={{ width: 10, height: 10, borderRadius: "50%", background: n > 0 ? cfg.kleur : "#D1D5DB" }}/>
                  <span style={{ fontSize: 11, fontWeight: 600, color: n > 0 ? cfg.kleur : "#9CA3AF" }}>{cfg.label}</span>
                  <span style={{ fontSize: 10, background: n > 0 ? cfg.kleur : "#E5E7EB", color: n > 0 ? "white" : "#9CA3AF", borderRadius: 10, padding: "0 5px", fontWeight: 700 }}>{n}</span>
                </button>
              );
            })}
            <div style={{ fontSize: 11, color: "#6B7280", alignSelf: "center" }}>{totaal} objecten gevonden</div>
          </div>

          {/* Lijst per type */}
          {Object.entries(TYPE_CFG).map(([type, cfg]) => {
            const items = data[type] ?? [];
            if (!items.length) return null;
            return (
              <div key={type} style={{ marginBottom: 12, border: `1px solid ${cfg.kleur}30`, borderLeft: `4px solid ${cfg.kleur}`, borderRadius: 8, overflow: "hidden" }}>
                <div style={{ background: cfg.achtergrond, padding: "7px 12px", fontWeight: 700, fontSize: 11, color: cfg.kleur }}>
                  {cfg.icoontje} {cfg.label} ({items.length})
                </div>
                <div style={{ maxHeight: 180, overflowY: "auto" }}>
                  {items.map(item => (
                    <div key={item.id}
                      onClick={() => { setGeselecteerd(item); setDetail(null); laadDetail(type, item.id); }}
                      style={{
                        padding: "7px 12px", borderBottom: "1px solid #F9FAFB", cursor: "pointer",
                        background: geselecteerd?.id === item.id ? cfg.achtergrond : "white",
                        transition: "background 0.1s",
                      }}
                    >
                      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                        <span style={{ fontWeight: 600, color: "#374151" }}>{item.id}</span>
                        <div style={{ display: "flex", gap: 4 }}>
                          <RisicoChip waarde={item.kwaliteit}/>
                          {item.diepte && <span style={{ fontSize: 10, color: "#6B7280" }}>⬇ {item.diepte}m</span>}
                        </div>
                      </div>
                      {(item.datum || item.bronhouder) && (
                        <div style={{ fontSize: 10, color: "#9CA3AF", marginTop: 1 }}>
                          {item.datum && `📅 ${item.datum}`}{item.datum && item.bronhouder && " · "}{item.bronhouder && `🏢 ${item.bronhouder}`}
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

      {/* Detail paneel */}
      {geselecteerd && (
        <div style={{ border: "1.5px solid #E5E7EB", borderRadius: 10, overflow: "hidden", marginTop: 8 }}>
          <div style={{ background: TYPE_CFG[geselecteerd.type]?.achtergrond, padding: "8px 12px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <span style={{ fontWeight: 700, color: TYPE_CFG[geselecteerd.type]?.kleur }}>
              {TYPE_CFG[geselecteerd.type]?.icoontje} {geselecteerd.id}
            </span>
            <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
              <a href={`https://www.broloket.nl/ondergrondgegevens?zoekveld=${geselecteerd.id}`} target="_blank" rel="noopener noreferrer"
                style={{ fontSize: 10, color: "#3B82F6", textDecoration: "none" }}>🔗 BROloket</a>
              <button onClick={() => { setGeselecteerd(null); setDetail(null); }} style={{ background: "none", border: "none", cursor: "pointer", fontSize: 14, color: "#9CA3AF" }}>✕</button>
            </div>
          </div>
          <div style={{ padding: "10px 12px" }}>
            {detailBezig && <div style={{ textAlign: "center", color: "#9CA3AF", padding: "12px 0" }}>⏳ Detail ophalen...</div>}
            {detail && !detailBezig && (
              <DetailWeergave data={detail} type={geselecteerd.type}/>
            )}
          </div>
        </div>
      )}

      {!data && !bezig && (
        <div style={{ textAlign: "center", padding: "32px 0", color: "#9CA3AF" }}>
          <div style={{ fontSize: 32, marginBottom: 8 }}>🏗️</div>
          <div style={{ fontWeight: 600, marginBottom: 4 }}>BRO geotechnisch onderzoek</div>
          <div style={{ fontSize: 11 }}>Klik "Ophalen uit BRO" om sonderingen en boorprofielen<br/>nabij het boortracé te laden (straal ~600m)</div>
        </div>
      )}

      <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
    </div>
  );
}

// ─── Detail weergave per type ──────────────────────────────────────
function DetailWeergave({ data, type }) {
  if (data?.error) return <div style={{ color: "#DC2626", fontSize: 11 }}>❌ {data.error}</div>;

  // CPT samenvatting
  if (type === "cpt") {
    const diepte = data?.registrationObject?.finalDepth ?? data?.finalDepth;
    const kl = data?.registrationObject?.qualityClass ?? data?.qualityClass;
    const lagen = data?.registrationObject?.conePenetrationTest?.dissipationTestStep ?? [];
    return (
      <div style={{ fontSize: 11 }}>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 6, marginBottom: 8 }}>
          {diepte && <Kv label="Einddiepte" waarde={`${diepte} m`}/>}
          {kl && <Kv label="Kwaliteitsklasse" waarde={kl}/>}
        </div>
        <CptGrafiek data={data}/>
        <div style={{ fontSize: 10, color: "#9CA3AF", marginTop: 6 }}>
          Volledig rapport beschikbaar op <a href={`https://www.broloket.nl`} target="_blank" rel="noopener noreferrer" style={{ color: "#3B82F6" }}>broloket.nl</a>
        </div>
      </div>
    );
  }

  // Boorprofiel samenvatting
  if (type === "bhrp" || type === "bhrg") {
    const lagen = data?.registrationObject?.boreholeSampleDescription?.descriptionReportDate
      ?? data?.soilLayers ?? [];
    const diepte = data?.registrationObject?.finalDepth ?? data?.finalDepth;
    return (
      <div style={{ fontSize: 11 }}>
        {diepte && <Kv label="Einddiepte" waarde={`${diepte} m`}/>}
        <BodemLagen data={data}/>
        <div style={{ fontSize: 10, color: "#9CA3AF", marginTop: 6 }}>
          <a href={`https://www.broloket.nl`} target="_blank" rel="noopener noreferrer" style={{ color: "#3B82F6" }}>Volledig rapport op broloket.nl</a>
        </div>
      </div>
    );
  }

  return <div style={{ fontSize: 11, color: "#6B7280" }}>Detail beschikbaar op <a href="https://www.broloket.nl" target="_blank" rel="noopener noreferrer" style={{ color: "#3B82F6" }}>broloket.nl</a></div>;
}

function Kv({ label, waarde }) {
  return (
    <div style={{ background: "#F9FAFB", borderRadius: 6, padding: "4px 8px" }}>
      <div style={{ fontSize: 9, color: "#9CA3AF", textTransform: "uppercase" }}>{label}</div>
      <div style={{ fontWeight: 600, color: "#1F2937" }}>{waarde}</div>
    </div>
  );
}

// Vereenvoudigde CPT staafgrafiek (qc per laag)
function CptGrafiek({ data }) {
  // Probeer meetdata te vinden
  const meting = data?.registrationObject?.conePenetrationTest?.cptCommonPart?.parameters;
  if (!meting) return <div style={{ color: "#9CA3AF", fontSize: 10 }}>Grafiekdata niet beschikbaar in samenvatting — zie broloket.nl voor volledig profiel</div>;

  return (
    <div style={{ background: "#F9FAFB", borderRadius: 6, padding: "6px 8px" }}>
      <div style={{ fontSize: 10, color: "#6B7280", marginBottom: 4 }}>Meetparameters aanwezig: {Object.keys(meting).join(", ")}</div>
      <div style={{ fontSize: 10, color: "#9CA3AF" }}>Download volledig GEF-bestand via broloket.nl voor de kegelweerstand-grafiek</div>
    </div>
  );
}

// Grondlagen visualisatie
function BodemLagen({ data }) {
  const GRONDKLEUR = {
    zand: "#F59E0B", klei: "#7C3AED", veen: "#78350F", grind: "#6B7280",
    leem: "#D97706", organisch: "#059669", "": "#E5E7EB",
  };

  // Probeer lagen te vinden in de response structuur
  const lagen = data?.registrationObject?.boreholeSampleDescription?.layer
    ?? data?.layers ?? data?.soilLayers ?? [];

  if (!lagen?.length) {
    return <div style={{ color: "#9CA3AF", fontSize: 10, padding: "8px 0" }}>Laagbeschrijving niet beschikbaar in samenvatting — zie broloket.nl</div>;
  }

  return (
    <div style={{ marginTop: 6 }}>
      <div style={{ fontSize: 10, fontWeight: 600, color: "#374151", marginBottom: 4 }}>Grondlagen:</div>
      {lagen.slice(0, 10).map((laag, i) => {
        const naam = (laag?.soil?.standardName ?? laag?.mainSoilType ?? "").toLowerCase();
        const kleur = Object.entries(GRONDKLEUR).find(([k]) => naam.includes(k))?.[1] ?? "#E5E7EB";
        const van = laag?.upperBoundary ?? laag?.top ?? "?";
        const tot = laag?.lowerBoundary ?? laag?.bottom ?? "?";
        return (
          <div key={i} style={{ display: "flex", alignItems: "center", gap: 6, padding: "2px 0" }}>
            <div style={{ width: 12, height: 14, background: kleur, borderRadius: 2, flexShrink: 0 }}/>
            <span style={{ color: "#374151" }}>{van}–{tot}m</span>
            <span style={{ color: "#6B7280" }}>{laag?.soil?.standardName ?? laag?.mainSoilType ?? "onbekend"}</span>
          </div>
        );
      })}
      {lagen.length > 10 && <div style={{ fontSize: 10, color: "#9CA3AF" }}>+{lagen.length - 10} meer lagen</div>}
    </div>
  );
}
