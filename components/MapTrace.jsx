"use client";

import { useEffect, useRef, useState } from "react";

const LAGEN = [
  {
    id: "luchtfoto",
    label: "Luchtfoto",
    kleur: "#6b7280",
    standaardAan: false,
    type: "wmts",
    url: "https://service.pdok.nl/hwh/luchtfotorgb/wmts/v1_0/Actueel_ortho25/EPSG:3857/{z}/{x}/{y}.jpeg",
  },
  {
    id: "panden",
    label: "BAG Panden",
    kleur: "#ea580c",
    standaardAan: true,
    type: "wms",
    url: "https://service.pdok.nl/lv/bag/wms/v2_0",
    layers: "pand",
  },
  {
    id: "percelen",
    label: "Percelen",
    kleur: "#ca8a04",
    standaardAan: true,
    type: "wms",
    url: "https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0",
    layers: "Perceel",
  },
  {
    id: "waterdelen",
    label: "Waterdelen",
    kleur: "#0ea5e9",
    standaardAan: true,
    type: "wms",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0",
    layers: "waterdeel",
  },
  {
    id: "kunstwerken",
    label: "Duikers & Kunstwerken",
    kleur: "#8b5cf6",
    standaardAan: true,
    type: "wms",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0",
    layers: "kunstwerkdeel",
  },
  {
    id: "wegen",
    label: "Wegdelen",
    kleur: "#64748b",
    standaardAan: false,
    type: "wms",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0",
    layers: "wegdeel",
  },
  {
    id: "begroeide",
    label: "Begroeid terrein",
    kleur: "#22c55e",
    standaardAan: false,
    type: "wms",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0",
    layers: "begroeidterreindeel",
  },
  {
    id: "onbegroeide",
    label: "Onbegroeid terrein",
    kleur: "#a3a3a3",
    standaardAan: false,
    type: "wms",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0",
    layers: "onbegroeidterreindeel",
  },
  {
    id: "berm",
    label: "Berm / Ondersteunend",
    kleur: "#84cc16",
    standaardAan: false,
    type: "wms",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0",
    layers: "ondersteunendwegdeel",
  },
  {
    id: "spoor",
    label: "Spoorbaandelen",
    kleur: "#dc2626",
    standaardAan: true,
    type: "wms",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0",
    layers: "spoor",
  },
  {
    id: "buisleidingen",
    label: "Buisleidingen",
    kleur: "#f97316",
    standaardAan: true,
    type: "wms",
    url: "https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0",
    layers: "buisleiding",
  },
  {
    id: "gemeenten",
    label: "Gemeentegrenzen",
    kleur: "#10b981",
    standaardAan: false,
    type: "wms",
    url: "https://service.pdok.nl/cbs/gebiedsindelingen/2024/wms/v1_0",
    layers: "gemeente_gegeneraliseerd",
  },
  {
    id: "ahn",
    label: "AHN Hoogte",
    kleur: "#84cc16",
    standaardAan: false,
    type: "wms",
    url: "https://service.pdok.nl/rws/ahn/wms/v1_0",
    layers: "dtm_05m",
  },
];

// PDOK BGT WFS endpoint voor oppervlaktype ophalen
async function haalOppervlakOp(lat, lng) {
  const delta = 0.00005;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;
  const lagen = ["wegdeel", "begroeidterreindeel", "onbegroeidterreindeel", "ondersteunendwegdeel"];

  for (const laag of lagen) {
    try {
      const url = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature&typeName=${laag}&bbox=${bbox}&outputFormat=application/json&count=1`;
      const res = await fetch(url);
      const json = await res.json();
      if (json.features && json.features.length > 0) {
        const props = json.features[0].properties;
        const type = props.fysiekVoorkomen || props.plus_fysiekVoorkomen || props.typeWater || null;
        if (type) return { laag, type };
      }
    } catch (e) {
      // stil falen
    }
  }
  return null;
}

// Vertaal BGT codes naar leesbare labels
function vertaalOppervlak(type) {
  const map = {
    "gesloten verharding": { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣" },
    "open verharding": { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱" },
    "half verhard": { label: "Half verhard", kleur: "#92400e", icoon: "🪨" },
    "onverhard": { label: "Onverhard", kleur: "#78350f", icoon: "🌱" },
    "gras- en kruidachtigen": { label: "Grasberm", kleur: "#16a34a", icoon: "🌿" },
    "groenvoorziening": { label: "Groen / Plantsoen", kleur: "#15803d", icoon: "🌳" },
    "struiken": { label: "Struiken", kleur: "#166534", icoon: "🌿" },
    "bos: gemengd bos": { label: "Bos", kleur: "#14532d", icoon: "🌲" },
    "bos: loofbos": { label: "Loofbos", kleur: "#14532d", icoon: "🌲" },
    "bos: naaldbos": { label: "Naaldbos", kleur: "#14532d", icoon: "🌲" },
    "zand": { label: "Zand", kleur: "#d97706", icoon: "🏖" },
    "rietland en moeras": { label: "Riet / Moeras", kleur: "#0369a1", icoon: "🌾" },
    "transitie": { label: "Overgangszone", kleur: "#9ca3af", icoon: "↔️" },
  };

  const lc = (type || "").toLowerCase();
  for (const [key, val] of Object.entries(map)) {
    if (lc.includes(key.toLowerCase())) return val;
  }
  return { label: type || "Onbekend", kleur: "#9ca3af", icoon: "❓" };
}

export default function MapTrace({ project, onTraceOpgeslagen }) {
  const mapRef = useRef(null);
  const leafletMapRef = useRef(null);
  const laagRefs = useRef({});
  const tekenLijnLaagRef = useRef(null);
  const markerRefs = useRef([]);
  const tekenModusRef = useRef(false);

  const [actieveLagen, setActieveLagen] = useState(
    Object.fromEntries(LAGEN.map(l => [l.id, l.standaardAan]))
  );
  const [tekenModus, setTekenModus] = useState(false);
  const [punten, setPunten] = useState([]);
  const [opgeslagen, setOpgeslagen] = useState(false);
  const [legendaOpen, setLegendaOpen] = useState(true);
  const [oppervlakken, setOppervlakken] = useState([]);
  const [analyseBezig, setAnalyseBezig] = useState(false);

  useEffect(() => {
    if (typeof window === "undefined") return;
    if (leafletMapRef.current) return;

    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
    document.head.appendChild(link);

    const script = document.createElement("script");
    script.src = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
    script.onload = () => initKaart();
    document.head.appendChild(script);

    return () => {
      if (leafletMapRef.current) {
        leafletMapRef.current.remove();
        leafletMapRef.current = null;
      }
    };
  }, []);

  function initKaart() {
    const L = window.L;
    if (!mapRef.current || leafletMapRef.current) return;

    const kaart = L.map(mapRef.current, { center: [52.15, 5.38], zoom: 8 });
    leafletMapRef.current = kaart;

    L.tileLayer(
      "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:3857/{z}/{x}/{y}.png",
      { attribution: "© PDOK BRT", maxZoom: 19 }
    ).addTo(kaart);

    LAGEN.forEach(laag => {
      let l;
      if (laag.type === "wmts") {
        l = L.tileLayer(laag.url, { maxZoom: 19, opacity: 0.9, attribution: "© PDOK" });
      } else {
        l = L.tileLayer.wms(laag.url, {
          layers: laag.layers,
          format: "image/png",
          transparent: true,
          opacity: 0.65,
          attribution: "© PDOK",
        });
      }
      laagRefs.current[laag.id] = l;
      if (laag.standaardAan) l.addTo(kaart);
    });

    if (project?.boortrace_geojson) {
      try {
        const geojson = typeof project.boortrace_geojson === "string"
          ? JSON.parse(project.boortrace_geojson)
          : project.boortrace_geojson;
        const traceLaag = L.geoJSON(geojson, {
          style: { color: "#2563eb", weight: 4, opacity: 1 },
        }).addTo(kaart);
        kaart.fitBounds(traceLaag.getBounds(), { padding: [40, 40] });
      } catch (e) {}
    }

    kaart.on("click", (e) => {
      if (!tekenModusRef.current) return;
      const { lat, lng } = e.latlng;
      setPunten(prev => {
        const nieuw = [...prev, [lat, lng]];
        tekenLijn(kaart, nieuw);
        return nieuw;
      });
    });
  }

  useEffect(() => {
    tekenModusRef.current = tekenModus;
    if (leafletMapRef.current) {
      leafletMapRef.current.getContainer().style.cursor = tekenModus ? "crosshair" : "";
    }
  }, [tekenModus]);

  function toggleLaag(id) {
    const kaart = leafletMapRef.current;
    const laag = laagRefs.current[id];
    if (!kaart || !laag) return;
    setActieveLagen(prev => {
      const nieuw = { ...prev, [id]: !prev[id] };
      if (nieuw[id]) laag.addTo(kaart);
      else kaart.removeLayer(laag);
      return nieuw;
    });
  }

  function tekenLijn(kaart, pts) {
    const L = window.L;
    if (tekenLijnLaagRef.current) kaart.removeLayer(tekenLijnLaagRef.current);
    if (pts.length < 2) return;
    tekenLijnLaagRef.current = L.polyline(pts, {
      color: "#2563eb", weight: 4, dashArray: "8 5", opacity: 0.9,
    }).addTo(kaart);
  }

  function resetTekenen() {
    setPunten([]);
    setOppervlakken([]);
    if (tekenLijnLaagRef.current && leafletMapRef.current) {
      leafletMapRef.current.removeLayer(tekenLijnLaagRef.current);
      tekenLijnLaagRef.current = null;
    }
  }

  async function analyseOppervlakken(pts) {
    setAnalyseBezig(true);
    // Sample elke ~10 meter — neem max 20 punten voor snelheid
    const stap = Math.max(1, Math.floor(pts.length / 20));
    const steekproef = pts.filter((_, i) => i % stap === 0);

    const resultaten = [];
    for (const [lat, lng] of steekproef) {
      const res = await haalOppervlakOp(lat, lng);
      if (res) {
        const vertaald = vertaalOppervlak(res.type);
        // Deduplicate opeenvolgende gelijke typen
        const vorige = resultaten[resultaten.length - 1];
        if (!vorige || vorige.label !== vertaald.label) {
          resultaten.push({ ...vertaald, origineel: res.type });
        }
      }
    }
    setOppervlakken(resultaten);
    setAnalyseBezig(false);
  }

  async function opslaanTrace() {
    if (punten.length < 2) return;
    const geojson = {
      type: "LineString",
      coordinates: punten.map(([lat, lng]) => [lng, lat]),
    };
    await onTraceOpgeslagen(geojson);
    await analyseOppervlakken(punten);
    setOpgeslagen(true);
    setTekenModus(false);
    setTimeout(() => setOpgeslagen(false), 3000);
  }

  return (
    <div className="flex flex-col h-full gap-3">

      {/* Toolbar */}
      <div className="flex items-center gap-2 flex-wrap">
        <div className="flex items-center gap-1 bg-white border border-gray-200 rounded-lg px-3 py-1.5">
          <span className="text-xs text-gray-400 font-medium mr-1">Lagen</span>
          <button
            onClick={() => setLegendaOpen(!legendaOpen)}
            className="text-xs text-blue-600 hover:text-blue-800 px-2 py-1 rounded hover:bg-blue-50 transition-colors"
          >
            {legendaOpen ? "Verbergen ▲" : "Tonen ▼"}
          </button>
        </div>

        <div className="flex items-center gap-1 bg-white border border-gray-200 rounded-lg px-3 py-1.5 ml-auto">
          {!tekenModus ? (
            <button
              onClick={() => { setTekenModus(true); resetTekenen(); }}
              className="flex items-center gap-1.5 text-xs font-medium text-blue-600 hover:text-blue-800 px-2.5 py-1 rounded-md hover:bg-blue-50 transition-colors"
            >
              ✏️ Tracé tekenen
            </button>
          ) : (
            <>
              <span className="text-xs text-blue-600 font-medium mr-2">
                {punten.length === 0 ? "Klik op kaart om te beginnen" : `${punten.length} punt${punten.length !== 1 ? "en" : ""} gezet`}
              </span>
              <button onClick={resetTekenen} className="text-xs text-gray-500 hover:text-gray-700 px-2.5 py-1 rounded-md hover:bg-gray-100 transition-colors">Reset</button>
              <button onClick={() => { setTekenModus(false); resetTekenen(); }} className="text-xs text-gray-500 hover:text-gray-700 px-2.5 py-1 rounded-md hover:bg-gray-100 transition-colors">Annuleren</button>
              {punten.length >= 2 && (
                <button onClick={opslaanTrace} className="text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 px-3 py-1 rounded-md transition-colors">
                  {opgeslagen ? "✓ Opgeslagen!" : "Opslaan & analyseren"}
                </button>
              )}
            </>
          )}
        </div>
      </div>

      {/* Lagen legenda */}
      {legendaOpen && (
        <div className="bg-white border border-gray-200 rounded-xl p-3 shadow-sm">
          <div className="grid grid-cols-2 gap-1 sm:grid-cols-3 lg:grid-cols-5">
            {LAGEN.map(laag => (
              <button
                key={laag.id}
                onClick={() => toggleLaag(laag.id)}
                className={`flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-medium transition-all duration-150 text-left ${
                  actieveLagen[laag.id]
                    ? "bg-gray-50 border border-gray-200 text-gray-800"
                    : "border border-transparent text-gray-400 hover:bg-gray-50"
                }`}
              >
                <span
                  className="w-2.5 h-2.5 rounded-sm flex-shrink-0"
                  style={{ backgroundColor: laag.kleur, opacity: actieveLagen[laag.id] ? 1 : 0.3 }}
                />
                <span className="truncate">{laag.label}</span>
                <span className={`ml-auto flex-shrink-0 w-3 h-3 rounded-full border transition-colors ${
                  actieveLagen[laag.id] ? "bg-blue-500 border-blue-500" : "border-gray-300"
                }`} />
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Kaart */}
      <div
        ref={mapRef}
        className="flex-1 rounded-xl border border-gray-200 overflow-hidden shadow-sm"
        style={{ minHeight: "440px" }}
      />

      {/* Oppervlakteanalyse */}
      {analyseBezig && (
        <div className="bg-blue-50 border border-blue-200 rounded-xl p-4 flex items-center gap-3">
          <div className="flex gap-1">
            {[0, 1, 2].map(i => (
              <span key={i} className="w-2 h-2 bg-blue-500 rounded-full animate-bounce" style={{ animationDelay: `${i * 0.15}s` }} />
            ))}
          </div>
          <span className="text-sm text-blue-700 font-medium">Oppervlaktetypen analyseren via PDOK BGT...</span>
        </div>
      )}

      {oppervlakken.length > 0 && (
        <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between mb-3">
            <h3 className="text-sm font-semibold text-gray-900">🗺 Oppervlakteanalyse tracé</h3>
            <span className="text-xs text-gray-400">{oppervlakken.length} zones gedetecteerd</span>
          </div>
          <div className="flex flex-wrap gap-2">
            {oppervlakken.map((o, i) => (
              <div
                key={i}
                className="flex items-center gap-2 px-3 py-1.5 rounded-lg border text-xs font-medium"
                style={{ borderColor: o.kleur + "40", backgroundColor: o.kleur + "10", color: o.kleur }}
              >
                <span>{o.icoon}</span>
                <span>{o.label}</span>
              </div>
            ))}
          </div>
          <p className="text-xs text-gray-400 mt-3">
            💡 Tip: vraag de AI Assistent om een hersteladvies op basis van deze oppervlaktetypen.
          </p>
        </div>
      )}

      {/* Legenda onderin */}
      <div className="flex items-center gap-4 text-xs text-gray-400 px-1 flex-wrap">
        <span className="flex items-center gap-1.5">
          <span className="w-6 border-t-2 border-blue-600 border-dashed" />
          Boortracé
        </span>
        {LAGEN.filter(l => actieveLagen[l.id]).map(l => (
          <span key={l.id} className="flex items-center gap-1.5">
            <span className="w-3 h-3 rounded-sm" style={{ backgroundColor: l.kleur, opacity: 0.7 }} />
            {l.label}
          </span>
        ))}
      </div>
    </div>
  );
}
