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

async function haalOppervlakOp(lat, lng) {
  const delta = 0.00005;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;
  const lagen = ["wegdeel", "begroeidterreindeel", "onbegroeidterreindeel", "ondersteunendwegdeel"];

  for (const laag of lagen) {
    try {
      const url = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature&typeName=${laag}&bbox=${bbox}&outputFormat=application/json&count=1`;
      const res = await fetch(url);
      const json = await res.json();
      if (json.features?.length > 0) {
        const props = json.features[0].properties;
        const type = props.fysiekVoorkomen || props.plus_fysiekVoorkomen || props.typeWater || null;
        if (type) return { laag, type };
      }
    } catch (e) {}
  }
  return null;
}

function vertaalOppervlak(type) {
  const map = {
    "gesloten verharding": { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣" },
    "open verharding": { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱" },
    "half verhard": { label: "Half verhard", kleur: "#92400e", icoon: "🪨" },
    "onverhard": { label: "Onverhard", kleur: "#78350f", icoon: "🌱" },
    "gras- en kruidachtigen": { label: "Grasberm", kleur: "#16a34a", icoon: "🌿" },
    "groenvoorziening": { label: "Groen / Plantsoen", kleur: "#15803d", icoon: "🌳" },
    "struiken": { label: "Struiken", kleur: "#166534", icoon: "🌿" },
    "bos": { label: "Bos", kleur: "#14532d", icoon: "🌲" },
    "zand": { label: "Zand", kleur: "#d97706", icoon: "🏖" },
    "rietland": { label: "Riet / Moeras", kleur: "#0369a1", icoon: "🌾" },
  };
  const lc = (type || "").toLowerCase();
  for (const [key, val] of Object.entries(map)) {
    if (lc.includes(key)) return val;
  }
  return { label: type || "Onbekend", kleur: "#9ca3af", icoon: "❓" };
}

export default function MapTrace({ project, onTraceOpgeslagen }) {
  const mapRef = useRef(null);
  const leafletMapRef = useRef(null);
  const laagRefs = useRef({});
  const tekenLijnLaagRef = useRef(null);
  const tekenModusRef = useRef(false);
  const puntenRef = useRef([]);

  const [actieveLagen, setActieveLagen] = useState(
    Object.fromEntries(LAGEN.map(l => [l.id, l.standaardAan]))
  );
  const [tekenModus, setTekenModus] = useState(false);
  const [punten, setPunten] = useState([]);
  const [opgeslagen, setOpgeslagen] = useState(false);
  const [legendaOpen, setLegendaOpen] = useState(true);

  // Live oppervlakken — per punt bijgehouden
  const [livePunten, setLivePunten] = useState([]);
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

    kaart.on("click", async (e) => {
      if (!tekenModusRef.current) return;
      const { lat, lng } = e.latlng;

      // Punt toevoegen aan lijn
      const nieuwePunten = [...puntenRef.current, [lat, lng]];
      puntenRef.current = nieuwePunten;
      setPunten([...nieuwePunten]);
      tekenLijn(kaart, nieuwePunten);

      // Direct BGT oppervlak ophalen voor dit punt
      setAnalyseBezig(true);
      const result = await haalOppervlakOp(lat, lng);
      const vertaald = result ? vertaalOppervlak(result.type) : { label: "Onbekend", kleur: "#9ca3af", icoon: "❓" };

      setLivePunten(prev => {
        const nieuw = [...prev, { lat, lng, ...vertaald, origineel: result?.type }];
        // Toon popup op kaart
        if (leafletMapRef.current) {
          L.circleMarker([lat, lng], {
            radius: 6,
            fillColor: vertaald.kleur,
            color: "#fff",
            weight: 2,
            fillOpacity: 1,
          })
            .bindTooltip(`${vertaald.icoon} ${vertaald.label}`, { permanent: false, direction: "top" })
            .addTo(leafletMapRef.current);
        }
        return nieuw;
      });
      setAnalyseBezig(false);
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
    puntenRef.current = [];
    setPunten([]);
    setLivePunten([]);
    if (tekenLijnLaagRef.current && leafletMapRef.current) {
      leafletMapRef.current.removeLayer(tekenLijnLaagRef.current);
      tekenLijnLaagRef.current = null;
    }
  }

  async function opslaanTrace() {
    if (punten.length < 2) return;
    const geojson = {
      type: "LineString",
      coordinates: punten.map(([lat, lng]) => [lng, lat]),
    };
    await onTraceOpgeslagen(geojson);
    setOpgeslagen(true);
    setTekenModus(false);
    setTimeout(() => setOpgeslagen(false), 3000);
  }

  // Unieke oppervlakken samenvatten
  const uniekeOppervlakken = livePunten.reduce((acc, p) => {
    if (!acc.find(a => a.label === p.label)) acc.push(p);
    return acc;
  }, []);

  return (
    <div className="flex gap-4 h-full">

      {/* Kaart kolom */}
      <div className="flex-1 flex flex-col gap-3 min-w-0">

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
                  {punten.length === 0 ? "Klik op kaart om te beginnen" : `${punten.length} punt${punten.length !== 1 ? "en" : ""}`}
                </span>
                {analyseBezig && <span className="text-xs text-gray-400 mr-1">⏳</span>}
                <button onClick={resetTekenen} className="text-xs text-gray-500 px-2.5 py-1 rounded-md hover:bg-gray-100 transition-colors">Reset</button>
                <button onClick={() => { setTekenModus(false); resetTekenen(); }} className="text-xs text-gray-500 px-2.5 py-1 rounded-md hover:bg-gray-100 transition-colors">Annuleren</button>
                {punten.length >= 2 && (
                  <button onClick={opslaanTrace} className="text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 px-3 py-1 rounded-md transition-colors">
                    {opgeslagen ? "✓ Opgeslagen!" : "Opslaan"}
                  </button>
                )}
              </>
            )}
          </div>
        </div>

        {/* Lagen legenda */}
        {legendaOpen && (
          <div className="bg-white border border-gray-200 rounded-xl p-3 shadow-sm">
            <div className="grid grid-cols-2 gap-1 sm:grid-cols-3 lg:grid-cols-4">
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
                  <span className="w-2.5 h-2.5 rounded-sm flex-shrink-0" style={{ backgroundColor: laag.kleur, opacity: actieveLagen[laag.id] ? 1 : 0.3 }} />
                  <span className="truncate">{laag.label}</span>
                  <span className={`ml-auto flex-shrink-0 w-3 h-3 rounded-full border transition-colors ${actieveLagen[laag.id] ? "bg-blue-500 border-blue-500" : "border-gray-300"}`} />
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Kaart */}
        <div ref={mapRef} className="flex-1 rounded-xl border border-gray-200 overflow-hidden shadow-sm" style={{ minHeight: "440px" }} />
      </div>

      {/* Analyse paneel rechts */}
      {tekenModus && (
        <div className="w-64 flex-shrink-0 flex flex-col gap-3">
          <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm flex-1">
            <h3 className="text-xs font-semibold text-gray-700 uppercase tracking-wide mb-3">
              🗺 Live oppervlakteanalyse
            </h3>

            {livePunten.length === 0 ? (
              <p className="text-xs text-gray-400">Klik op de kaart om het oppervlaktype te analyseren.</p>
            ) : (
              <>
                {/* Unieke types */}
                <div className="mb-4">
                  <div className="text-xs text-gray-400 mb-2">Gedetecteerde typen</div>
                  <div className="flex flex-col gap-1.5">
                    {uniekeOppervlakken.map((o, i) => (
                      <div
                        key={i}
                        className="flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-medium"
                        style={{ backgroundColor: o.kleur + "12", color: o.kleur, border: `1px solid ${o.kleur}30` }}
                      >
                        <span className="text-sm">{o.icoon}</span>
                        <span>{o.label}</span>
                      </div>
                    ))}
                  </div>
                </div>

                {/* Per punt log */}
                <div>
                  <div className="text-xs text-gray-400 mb-2">Punten ({livePunten.length})</div>
                  <div className="flex flex-col gap-1 max-h-64 overflow-y-auto">
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
                        <span>⏳</span>
                        <span>Analyseren...</span>
                      </div>
                    )}
                  </div>
                </div>
              </>
            )}
          </div>

          {uniekeOppervlakken.length > 0 && (
            <div className="bg-blue-50 border border-blue-100 rounded-xl p-3">
              <p className="text-xs text-blue-700">
                💡 Sla het tracé op en vraag de AI Assistent om hersteladvies per oppervlaktype.
              </p>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
