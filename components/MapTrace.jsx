"use client";

import { useEffect, useRef, useState } from "react";

const LAGEN = [
  { id: "luchtfoto", label: "Luchtfoto", kleur: "#6b7280", standaardAan: false, type: "wmts", url: "https://service.pdok.nl/hwh/luchtfotorgb/wmts/v1_0/Actueel_ortho25/EPSG:3857/{z}/{x}/{y}.jpeg" },
  { id: "panden", label: "BAG Panden", kleur: "#ea580c", standaardAan: true, type: "wms", url: "https://service.pdok.nl/lv/bag/wms/v2_0", layers: "pand" },
  { id: "percelen", label: "Percelen", kleur: "#ca8a04", standaardAan: true, type: "wms", url: "https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0", layers: "Perceel" },
  { id: "waterdelen", label: "Waterdelen", kleur: "#0ea5e9", standaardAan: true, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "waterdeel" },
  { id: "kunstwerken", label: "Duikers & Kunstwerken", kleur: "#8b5cf6", standaardAan: true, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "kunstwerkdeel" },
  { id: "wegen", label: "Wegdelen", kleur: "#64748b", standaardAan: false, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "wegdeel" },
  { id: "begroeide", label: "Begroeid terrein", kleur: "#22c55e", standaardAan: false, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "begroeidterreindeel" },
  { id: "onbegroeide", label: "Onbegroeid terrein", kleur: "#a3a3a3", standaardAan: false, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "onbegroeidterreindeel" },
  { id: "berm", label: "Berm / Ondersteunend", kleur: "#84cc16", standaardAan: false, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "ondersteunendwegdeel" },
  { id: "spoor", label: "Spoorbaandelen", kleur: "#dc2626", standaardAan: true, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "spoor" },
  { id: "buisleidingen", label: "Buisleidingen", kleur: "#f97316", standaardAan: true, type: "wms", url: "https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0", layers: "buisleiding" },
  { id: "gemeenten", label: "Gemeentegrenzen", kleur: "#10b981", standaardAan: false, type: "wms", url: "https://service.pdok.nl/cbs/gebiedsindelingen/2024/wms/v1_0", layers: "gemeente_gegeneraliseerd" },
  { id: "ahn", label: "AHN Hoogte", kleur: "#84cc16", standaardAan: false, type: "wms", url: "https://service.pdok.nl/rws/ahn/wms/v1_0", layers: "dtm_05m" },
];

const BGT_LAGEN = [
  { typeName: "bgt:wegdeel", prop: "fysiekVoorkomen" },
  { typeName: "bgt:begroeidterreindeel", prop: "fysiekVoorkomen" },
  { typeName: "bgt:onbegroeidterreindeel", prop: "fysiekVoorkomen" },
  { typeName: "bgt:ondersteunendwegdeel", prop: "fysiekVoorkomen" },
  { typeName: "bgt:waterdeel", prop: "typeWater" },
];

async function haalOppervlakOp(lat, lng) {
  const delta = 0.0002;
  for (const { typeName, prop } of BGT_LAGEN) {
    try {
      const params = new URLSearchParams({
        service: "WFS", version: "2.0.0", request: "GetFeature", typeName,
        bbox: `${lat - delta},${lng - delta},${lat + delta},${lng + delta},urn:ogc:def:crs:EPSG::4326`,
        outputFormat: "application/json", count: "1", srsName: "EPSG:4326",
      });
      const res = await fetch(`https://service.pdok.nl/lv/bgt/wfs/v1_0?${params}`);
      if (!res.ok) continue;
      const json = await res.json();
      if (json.features?.length > 0) {
        const props = json.features[0].properties;
        const type = props[prop] || null;
        if (type && type !== "transitie") return { laag: typeName, type };
      }
    } catch (e) {}
  }
  return null;
}

async function haalAHNHoogteOp(lat, lng) {
  try {
    const params = new URLSearchParams({
      SERVICE: "WCS", VERSION: "2.0.1", REQUEST: "GetCoverage",
      COVERAGEID: "dtm_05m", FORMAT: "image/tiff",
      SUBSET: `Long(${lng - 0.0001},${lng + 0.0001})`,
      SUBSETCRS: "EPSG:4326",
    });
    // Gebruik PDOK locatieserver als fallback voor hoogte
    const url = `https://api.pdok.nl/bzk/locatieserver/search/v3_1/lookup?id=${lat},${lng}&fl=identificatie,weergavenaam`;
    // AHN WCS is complex — gebruik geschatte waarde op basis van Nederland (NAP)
    return Math.round((Math.random() * 2 - 0.5) * 10) / 10; // placeholder tot WCS geconfigureerd
  } catch (e) {
    return null;
  }
}

function vertaalOppervlak(type) {
  if (!type) return { label: "Onbekend", kleur: "#9ca3af", icoon: "❓", herstel: "?" };
  const lc = type.toLowerCase();
  if (lc.includes("gesloten")) return { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣", herstel: "Hoog" };
  if (lc.includes("open verharding")) return { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱", herstel: "Midden" };
  if (lc.includes("half verhard")) return { label: "Half verhard", kleur: "#92400e", icoon: "🪨", herstel: "Laag" };
  if (lc.includes("onverhard")) return { label: "Onverhard", kleur: "#78350f", icoon: "🌱", herstel: "Laag" };
  if (lc.includes("gras")) return { label: "Grasberm", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" };
  if (lc.includes("groenvoorziening")) return { label: "Groen", kleur: "#15803d", icoon: "🌳", herstel: "Laag" };
  if (lc.includes("bos")) return { label: "Bos", kleur: "#14532d", icoon: "🌲", herstel: "Laag" };
  if (lc.includes("zand")) return { label: "Zand", kleur: "#d97706", icoon: "🏖", herstel: "Laag" };
  if (lc.includes("water") || lc.includes("sloot") || lc.includes("kanaal")) return { label: "Water", kleur: "#0284c7", icoon: "💧", herstel: "Speciaal" };
  return { label: type, kleur: "#6b7280", icoon: "📍", herstel: "?" };
}

// Haversine afstand in meters
function afstandTussenPunten(p1, p2) {
  const R = 6371000;
  const dLat = (p2[0] - p1[0]) * Math.PI / 180;
  const dLng = (p2[1] - p1[1]) * Math.PI / 180;
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(p1[0] * Math.PI / 180) * Math.cos(p2[0] * Math.PI / 180) * Math.sin(dLng / 2) ** 2;
  return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

// Dwarsprofiel SVG component
function Dwarsprofiel({ punten, livePunten, project }) {
  if (punten.length < 2) return null;

  const W = 900;
  const H = 200;
  const PADDING = { top: 20, right: 20, bottom: 40, left: 50 };
  const plotW = W - PADDING.left - PADDING.right;
  const plotH = H - PADDING.top - PADDING.bottom;

  // Totale tracélengte
  let totaalM = 0;
  const cumulatief = [0];
  for (let i = 1; i < punten.length; i++) {
    totaalM += afstandTussenPunten(punten[i - 1], punten[i]);
    cumulatief.push(totaalM);
  }

  // Dieptebereik: 0m tot -5m
  const minDiepte = -5;
  const maxDiepte = 1;
  const diepteBereik = maxDiepte - minDiepte;

  function xPos(m) { return PADDING.left + (m / totaalM) * plotW; }
  function yPos(d) { return PADDING.top + ((maxDiepte - d) / diepteBereik) * plotH; }

  // Boringdiepte
  const boringDiepte = -1.5;
  const boringDiameter = project?.diameter_mm ? project.diameter_mm / 1000 : 0.315;

  // Oppervlaktestroken
  const oppervlakSegmenten = livePunten.map((p, i) => {
    const m = cumulatief[i] ?? (i / livePunten.length) * totaalM;
    return { m, ...p };
  });

  // Kruisingen uit project (mock als er geen zijn)
  const kruisingen = project?.kruisingen?.length > 0 ? project.kruisingen : [];

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
      <div className="flex items-center justify-between mb-3">
        <h3 className="text-sm font-semibold text-gray-900">📐 Dwarsprofiel boortracé</h3>
        <div className="flex items-center gap-4 text-xs text-gray-400">
          <span className="flex items-center gap-1.5"><span className="w-4 border-t-2 border-blue-600" /> Boring ({project?.diameter_mm ?? "—"} mm)</span>
          <span className="flex items-center gap-1.5"><span className="w-4 border-t-2 border-dashed border-red-400" /> Kruisingen</span>
          <span className="flex items-center gap-1.5"><span className="w-3 h-3 bg-gray-200 rounded-sm" /> Maaiveld</span>
          <span className="text-gray-300">Totaal: {Math.round(totaalM)} m</span>
        </div>
      </div>

      <svg viewBox={`0 0 ${W} ${H}`} className="w-full" style={{ height: 220 }}>

        {/* Achtergrond */}
        <rect x={PADDING.left} y={PADDING.top} width={plotW} height={plotH} fill="#f8fafc" rx="4" />

        {/* Grondvlak — grijs blok */}
        <rect x={PADDING.left} y={yPos(0)} width={plotW} height={yPos(minDiepte) - yPos(0)} fill="#e5e7eb" opacity="0.4" />

        {/* Oppervlaktestroken bovenaan */}
        {oppervlakSegmenten.map((seg, i) => {
          const volgende = oppervlakSegmenten[i + 1];
          const x1 = xPos(seg.m);
          const x2 = volgende ? xPos(volgende.m) : xPos(totaalM);
          const breedte = Math.max(x2 - x1, 2);
          return (
            <g key={i}>
              <rect x={x1} y={PADDING.top - 14} width={breedte} height={12} fill={seg.kleur} opacity="0.8" rx="1" />
              {breedte > 30 && (
                <text x={x1 + breedte / 2} y={PADDING.top - 5} textAnchor="middle" fontSize="7" fill={seg.kleur} fontWeight="600">
                  {seg.icoon} {seg.label}
                </text>
              )}
            </g>
          );
        })}

        {/* Maaiveld lijn */}
        <line x1={PADDING.left} y1={yPos(0)} x2={PADDING.left + plotW} y2={yPos(0)} stroke="#9ca3af" strokeWidth="1.5" strokeDasharray="4 2" />

        {/* Y-as labels (diepte) */}
        {[0, -1, -2, -3, -4, -5].map(d => (
          <g key={d}>
            <line x1={PADDING.left - 4} y1={yPos(d)} x2={PADDING.left} y2={yPos(d)} stroke="#d1d5db" strokeWidth="1" />
            <text x={PADDING.left - 6} y={yPos(d) + 3} textAnchor="end" fontSize="9" fill="#9ca3af">{d}m</text>
            <line x1={PADDING.left} y1={yPos(d)} x2={PADDING.left + plotW} y2={yPos(d)} stroke="#f3f4f6" strokeWidth="0.5" />
          </g>
        ))}

        {/* X-as labels (positie) */}
        {[0, 0.25, 0.5, 0.75, 1].map(frac => {
          const m = frac * totaalM;
          const x = xPos(m);
          return (
            <g key={frac}>
              <line x1={x} y1={PADDING.top + plotH} x2={x} y2={PADDING.top + plotH + 4} stroke="#d1d5db" strokeWidth="1" />
              <text x={x} y={PADDING.top + plotH + 13} textAnchor="middle" fontSize="9" fill="#9ca3af">{Math.round(m)}m</text>
            </g>
          );
        })}

        {/* Boring lijn */}
        <line
          x1={PADDING.left} y1={yPos(boringDiepte)}
          x2={PADDING.left + plotW} y2={yPos(boringDiepte)}
          stroke="#2563eb" strokeWidth="3" strokeLinecap="round"
        />
        {/* Boring buis dikte indicatie */}
        <rect
          x={PADDING.left} y={yPos(boringDiepte) - 2}
          width={plotW} height={4}
          fill="#2563eb" opacity="0.15" rx="2"
        />
        <text x={PADDING.left + plotW - 4} y={yPos(boringDiepte) - 5} textAnchor="end" fontSize="8" fill="#2563eb" fontWeight="600">
          {project?.materiaal ?? "PE"} Ø{project?.diameter_mm ?? "—"}mm
        </text>

        {/* Kruisingen */}
        {kruisingen.map((k, i) => {
          const posM = k.kruising_positie_m ?? (totaalM * (i + 1) / (kruisingen.length + 1));
          const diepteM = k.diepte_m ? -k.diepte_m : -1.2;
          const x = xPos(posM);
          const y = yPos(diepteM);
          const kleur = k.risico === "rood" ? "#ef4444" : k.risico === "oranje" ? "#f97316" : "#22c55e";
          return (
            <g key={i}>
              <line x1={x} y1={PADDING.top} x2={x} y2={PADDING.top + plotH} stroke={kleur} strokeWidth="1" strokeDasharray="4 3" opacity="0.6" />
              <circle cx={x} cy={y} r="5" fill={kleur} opacity="0.9" />
              <text x={x} y={y - 8} textAnchor="middle" fontSize="8" fill={kleur} fontWeight="600">
                {k.leidingtype?.split(" ")[0] ?? "Leiding"}
              </text>
              <text x={x} y={PADDING.top + plotH + 25} textAnchor="middle" fontSize="8" fill={kleur}>
                {Math.round(posM)}m
              </text>
            </g>
          );
        })}

        {/* Assen */}
        <line x1={PADDING.left} y1={PADDING.top} x2={PADDING.left} y2={PADDING.top + plotH} stroke="#d1d5db" strokeWidth="1.5" />
        <line x1={PADDING.left} y1={PADDING.top + plotH} x2={PADDING.left + plotW} y2={PADDING.top + plotH} stroke="#d1d5db" strokeWidth="1.5" />

        {/* Labels assen */}
        <text x={PADDING.left - 40} y={PADDING.top + plotH / 2} textAnchor="middle" fontSize="9" fill="#6b7280" transform={`rotate(-90, ${PADDING.left - 40}, ${PADDING.top + plotH / 2})`}>
          Diepte (m NAP)
        </text>
        <text x={PADDING.left + plotW / 2} y={H - 2} textAnchor="middle" fontSize="9" fill="#6b7280">
          Positie langs tracé (m)
        </text>
      </svg>

      {/* Samenvatting onder profiel */}
      {oppervlakSegmenten.length > 0 && (
        <div className="mt-3 pt-3 border-t border-gray-100 flex flex-wrap gap-2">
          {[...new Map(oppervlakSegmenten.map(s => [s.label, s])).values()].map((s, i) => (
            <span key={i} className="flex items-center gap-1.5 text-xs px-2.5 py-1 rounded-full font-medium"
              style={{ backgroundColor: s.kleur + "15", color: s.kleur, border: `1px solid ${s.kleur}30` }}>
              {s.icoon} {s.label}
              {s.herstel && <span className="opacity-60 ml-0.5">· herstel: {s.herstel}</span>}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

export default function MapTrace({ project, onTraceOpgeslagen }) {
  const mapRef = useRef(null);
  const leafletMapRef = useRef(null);
  const laagRefs = useRef({});
  const tekenLijnLaagRef = useRef(null);
  const tekenModusRef = useRef(false);
  const puntenRef = useRef([]);

  const [actieveLagen, setActieveLagen] = useState(Object.fromEntries(LAGEN.map(l => [l.id, l.standaardAan])));
  const [tekenModus, setTekenModus] = useState(false);
  const [punten, setPunten] = useState([]);
  const [opgeslagen, setOpgeslagen] = useState(false);
  const [legendaOpen, setLegendaOpen] = useState(true);
  const [livePunten, setLivePunten] = useState([]);
  const [analyseBezig, setAnalyseBezig] = useState(false);
  const [toonDwarsprofiel, setToonDwarsprofiel] = useState(false);

  useEffect(() => {
    if (typeof window === "undefined" || leafletMapRef.current) return;
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
    document.head.appendChild(link);
    const script = document.createElement("script");
    script.src = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
    script.onload = () => initKaart();
    document.head.appendChild(script);
    return () => { if (leafletMapRef.current) { leafletMapRef.current.remove(); leafletMapRef.current = null; } };
  }, []);

  function initKaart() {
    const L = window.L;
    if (!mapRef.current || leafletMapRef.current) return;
    const kaart = L.map(mapRef.current, { center: [52.15, 5.38], zoom: 8 });
    leafletMapRef.current = kaart;
    L.tileLayer("https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:3857/{z}/{x}/{y}.png", { attribution: "© PDOK BRT", maxZoom: 19 }).addTo(kaart);
    LAGEN.forEach(laag => {
      const l = laag.type === "wmts"
        ? L.tileLayer(laag.url, { maxZoom: 19, opacity: 0.9, attribution: "© PDOK" })
        : L.tileLayer.wms(laag.url, { layers: laag.layers, format: "image/png", transparent: true, opacity: 0.65, attribution: "© PDOK" });
      laagRefs.current[laag.id] = l;
      if (laag.standaardAan) l.addTo(kaart);
    });
    if (project?.boortrace_geojson) {
      try {
        const geojson = typeof project.boortrace_geojson === "string" ? JSON.parse(project.boortrace_geojson) : project.boortrace_geojson;
        const traceLaag = L.geoJSON(geojson, { style: { color: "#2563eb", weight: 4, opacity: 1 } }).addTo(kaart);
        kaart.fitBounds(traceLaag.getBounds(), { padding: [40, 40] });
      } catch (e) {}
    }
    kaart.on("click", async (e) => {
      if (!tekenModusRef.current) return;
      const { lat, lng } = e.latlng;
      const nieuwePunten = [...puntenRef.current, [lat, lng]];
      puntenRef.current = nieuwePunten;
      setPunten([...nieuwePunten]);
      tekenLijn(kaart, nieuwePunten);
      setAnalyseBezig(true);
      const result = await haalOppervlakOp(lat, lng);
      const vertaald = result ? vertaalOppervlak(result.type) : { label: "Geen BGT data", kleur: "#9ca3af", icoon: "❓", herstel: "?" };
      L.circleMarker([lat, lng], { radius: 7, fillColor: vertaald.kleur, color: "#fff", weight: 2, fillOpacity: 1 })
        .bindTooltip(`${vertaald.icoon} ${vertaald.label}`, { permanent: false, direction: "top" })
        .addTo(kaart);
      setLivePunten(prev => [...prev, { lat, lng, ...vertaald, origineel: result?.type }]);
      setAnalyseBezig(false);
    });
  }

  useEffect(() => {
    tekenModusRef.current = tekenModus;
    if (leafletMapRef.current) leafletMapRef.current.getContainer().style.cursor = tekenModus ? "crosshair" : "";
  }, [tekenModus]);

  function toggleLaag(id) {
    const kaart = leafletMapRef.current;
    const laag = laagRefs.current[id];
    if (!kaart || !laag) return;
    setActieveLagen(prev => {
      const nieuw = { ...prev, [id]: !prev[id] };
      if (nieuw[id]) laag.addTo(kaart); else kaart.removeLayer(laag);
      return nieuw;
    });
  }

  function tekenLijn(kaart, pts) {
    const L = window.L;
    if (tekenLijnLaagRef.current) kaart.removeLayer(tekenLijnLaagRef.current);
    if (pts.length < 2) return;
    tekenLijnLaagRef.current = L.polyline(pts, { color: "#2563eb", weight: 4, dashArray: "8 5", opacity: 0.9 }).addTo(kaart);
  }

  function resetTekenen() {
    puntenRef.current = [];
    setPunten([]);
    setLivePunten([]);
    setToonDwarsprofiel(false);
    if (tekenLijnLaagRef.current && leafletMapRef.current) { leafletMapRef.current.removeLayer(tekenLijnLaagRef.current); tekenLijnLaagRef.current = null; }
  }

  async function opslaanTrace() {
    if (punten.length < 2) return;
    await onTraceOpgeslagen({ type: "LineString", coordinates: punten.map(([lat, lng]) => [lng, lat]) });
    setOpgeslagen(true);
    setTekenModus(false);
    setToonDwarsprofiel(true);
    setTimeout(() => setOpgeslagen(false), 3000);
  }

  const uniekeOppervlakken = livePunten.reduce((acc, p) => {
    if (!acc.find(a => a.label === p.label)) acc.push(p);
    return acc;
  }, []);

  // Toon dwarsprofiel ook als er een bestaand tracé is
  const heeftTrace = project?.boortrace_geojson || punten.length >= 2;

  return (
    <div className="flex gap-4 h-full">
      {/* Kaart kolom */}
      <div className="flex-1 flex flex-col gap-3 min-w-0">

        {/* Toolbar */}
        <div className="flex items-center gap-2 flex-wrap">
          <div className="flex items-center gap-1 bg-white border border-gray-200 rounded-lg px-3 py-1.5">
            <span className="text-xs text-gray-400 font-medium mr-1">Lagen</span>
            <button onClick={() => setLegendaOpen(!legendaOpen)} className="text-xs text-blue-600 px-2 py-1 rounded hover:bg-blue-50 transition-colors">
              {legendaOpen ? "Verbergen ▲" : "Tonen ▼"}
            </button>
          </div>
          <div className="flex items-center gap-1 bg-white border border-gray-200 rounded-lg px-3 py-1.5 ml-auto">
            {!tekenModus ? (
              <button onClick={() => { setTekenModus(true); resetTekenen(); }} className="flex items-center gap-1.5 text-xs font-medium text-blue-600 hover:text-blue-800 px-2.5 py-1 rounded-md hover:bg-blue-50 transition-colors">
                ✏️ Tracé tekenen
              </button>
            ) : (
              <>
                <span className="text-xs text-blue-600 font-medium mr-2">
                  {punten.length === 0 ? "Klik op kaart" : `${punten.length} punt${punten.length !== 1 ? "en" : ""}`}
                  {analyseBezig && " ⏳"}
                </span>
                <button onClick={resetTekenen} className="text-xs text-gray-500 px-2.5 py-1 rounded-md hover:bg-gray-100">Reset</button>
                <button onClick={() => { setTekenModus(false); resetTekenen(); }} className="text-xs text-gray-500 px-2.5 py-1 rounded-md hover:bg-gray-100">Annuleren</button>
                {punten.length >= 2 && (
                  <button onClick={opslaanTrace} className="text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 px-3 py-1 rounded-md transition-colors">
                    {opgeslagen ? "✓ Opgeslagen!" : "Opslaan"}
                  </button>
                )}
              </>
            )}
          </div>
        </div>

        {/* Lagen */}
        {legendaOpen && (
          <div className="bg-white border border-gray-200 rounded-xl p-3 shadow-sm">
            <div className="grid grid-cols-2 gap-1 sm:grid-cols-3 lg:grid-cols-4">
              {LAGEN.map(laag => (
                <button key={laag.id} onClick={() => toggleLaag(laag.id)}
                  className={`flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-medium transition-all text-left ${actieveLagen[laag.id] ? "bg-gray-50 border border-gray-200 text-gray-800" : "border border-transparent text-gray-400 hover:bg-gray-50"}`}>
                  <span className="w-2.5 h-2.5 rounded-sm flex-shrink-0" style={{ backgroundColor: laag.kleur, opacity: actieveLagen[laag.id] ? 1 : 0.3 }} />
                  <span className="truncate">{laag.label}</span>
                  <span className={`ml-auto flex-shrink-0 w-3 h-3 rounded-full border ${actieveLagen[laag.id] ? "bg-blue-500 border-blue-500" : "border-gray-300"}`} />
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Kaart */}
        <div ref={mapRef} className="rounded-xl border border-gray-200 overflow-hidden shadow-sm" style={{ height: 400 }} />

        {/* Dwarsprofiel */}
        {(toonDwarsprofiel || heeftTrace) && (
          <Dwarsprofiel
            punten={punten.length >= 2 ? punten : (() => {
              try {
                const g = project?.boortrace_geojson;
                if (!g) return [];
                const parsed = typeof g === "string" ? JSON.parse(g) : g;
                return parsed.coordinates?.map(([lng, lat]) => [lat, lng]) ?? [];
              } catch { return []; }
            })()}
            livePunten={livePunten}
            project={project}
          />
        )}
      </div>

      {/* Analyse paneel rechts */}
      {tekenModus && (
        <div className="w-60 flex-shrink-0 flex flex-col gap-3">
          <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm flex-1">
            <h3 className="text-xs font-semibold text-gray-700 uppercase tracking-wide mb-3">🗺 Live oppervlakteanalyse</h3>
            {livePunten.length === 0 ? (
              <p className="text-xs text-gray-400">Klik op de kaart om het oppervlaktype te analyseren.</p>
            ) : (
              <>
                <div className="mb-4">
                  <div className="text-xs text-gray-400 mb-2">Gedetecteerde typen</div>
                  <div className="flex flex-col gap-1.5">
                    {uniekeOppervlakken.map((o, i) => (
                      <div key={i} className="flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-medium"
                        style={{ backgroundColor: o.kleur + "12", color: o.kleur, border: `1px solid ${o.kleur}30` }}>
                        <span>{o.icoon}</span>
                        <span className="flex-1">{o.label}</span>
                        {o.herstel && <span className="opacity-50 text-xs">{o.herstel}</span>}
                      </div>
                    ))}
                  </div>
                </div>
                <div>
                  <div className="text-xs text-gray-400 mb-2">Punten ({livePunten.length})</div>
                  <div className="flex flex-col gap-1 max-h-48 overflow-y-auto">
                    {livePunten.map((p, i) => (
                      <div key={i} className="flex items-center gap-2 text-xs py-1 border-b border-gray-50">
                        <span className="text-gray-400 font-mono w-4">{i + 1}</span>
                        <span>{p.icoon}</span>
                        <span className="text-gray-600 truncate">{p.label}</span>
                      </div>
                    ))}
                    {analyseBezig && (
                      <div className="flex items-center gap-2 text-xs py-1 text-gray-400">
                        <span className="font-mono w-4">{livePunten.length + 1}</span>
                        <span>⏳</span><span>Analyseren...</span>
                      </div>
                    )}
                  </div>
                </div>
              </>
            )}
          </div>
          {uniekeOppervlakken.length > 0 && (
            <div className="bg-blue-50 border border-blue-100 rounded-xl p-3">
              <p className="text-xs text-blue-700">💡 Sla op en vraag de AI om hersteladvies per oppervlaktype.</p>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
