"use client";

import { useState, useEffect, useRef } from "react";
import { updateProject, supabase } from "@/lib/supabase-queries";

// ─── Standaard laagkleuren per type ──────────────────────────────
const TYPE_KLEUR = {
  LS:    "#f59e0b",
  MS:    "#f97316",
  Gas:   "#eab308",
  Water: "#3b82f6",
  Data:  "#8b5cf6",
  KLIC:  "#ef4444",
};

function standaardInst(type) {
  return { zichtbaar: true, kleur: TYPE_KLEUR[type] ?? "#3b82f6", dikte: 2, helderheid: 0.85 };
}

// ─── Leaflet laden via CDN (geen npm pakket nodig) ────────────────
async function laadLeaflet() {
  if (typeof window === "undefined") return null;
  if (window.L?.version) return window.L;

  // CSS
  if (!document.querySelector('link[href*="leaflet"]')) {
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
    document.head.appendChild(link);
  }

  // JS
  await new Promise((ok, fout) => {
    if (window.L?.version) return ok();
    const s = document.createElement("script");
    s.src = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
    s.onload = ok;
    s.onerror = fout;
    document.head.appendChild(s);
  });

  return window.L;
}

// ─── RD New → WGS84 (geen externe library) ───────────────────────
function rdNaarWgs84(x, y) {
  if (Math.abs(x) <= 180 && Math.abs(y) <= 90) return [x, y]; // al WGS84
  const dX = (x - 155000) / 100000;
  const dY = (y - 463000) / 100000;
  const sumN =
     3235.65389 * dY   + -32.58297 * dX*dX + -0.24750 * dY*dY
    + -0.84978  * dX*dX*dY + -0.06550 * dY*dY*dY
    + -0.01709  * dX*dX*dY*dY + -0.00738 * dX*dX*dX*dX
    +  0.00530  * dX*dX*dY*dY*dY;
  const sumE =
     5260.52916 * dX   + 105.94684 * dX*dY  + 2.45656 * dX*dY*dY
    + -0.81885  * dX*dX*dX + 0.05594 * dX*dY*dY*dY
    + -0.05607  * dX*dX*dX*dY + 0.01199 * dX*dY*dY*dY*dY;
  return [5.38720621 + sumE / 3600, 52.15517440 + sumN / 3600];
}

// ─── Ingebouwde DXF-parser ────────────────────────────────────────
function dxfNaarGeoJson(tekst) {
  try {
    const features = [];
    const regels = tekst.split(/\r?\n/);
    let i = 0;

    while (i < regels.length) {
      if (regels[i].trim() !== "0") { i++; continue; }
      const type = regels[i + 1]?.trim();
      const start = i;
      let einde = i + 2;
      while (einde < regels.length && regels[einde].trim() !== "0") einde++;

      const blok = regels.slice(start, einde);

      const getW = (code) => {
        for (let k = 0; k < blok.length - 1; k++)
          if (blok[k].trim() === String(code)) return parseFloat(blok[k + 1].trim()) || 0;
        return 0;
      };
      const getLaag = () => {
        for (let k = 0; k < blok.length - 1; k++)
          if (blok[k].trim() === "8") return blok[k + 1].trim();
        return "0";
      };

      try {
        if (type === "LINE") {
          features.push({ type: "Feature", geometry: { type: "LineString", coordinates: [rdNaarWgs84(getW(10), getW(20)), rdNaarWgs84(getW(11), getW(21))] }, properties: { layer: getLaag() } });
        } else if (type === "LWPOLYLINE" || type === "POLYLINE") {
          const coords = [];
          for (let k = 0; k < blok.length - 3; k++)
            if (blok[k].trim() === "10" && blok[k + 2]?.trim() === "20") {
              const x = parseFloat(blok[k + 1].trim()), y = parseFloat(blok[k + 3].trim());
              if (!isNaN(x) && !isNaN(y)) coords.push(rdNaarWgs84(x, y));
            }
          if (coords.length >= 2) features.push({ type: "Feature", geometry: { type: "LineString", coordinates: coords }, properties: { layer: getLaag() } });
        } else if (type === "POINT") {
          features.push({ type: "Feature", geometry: { type: "Point", coordinates: rdNaarWgs84(getW(10), getW(20)) }, properties: { layer: getLaag() } });
        } else if (type === "ARC" || type === "CIRCLE") {
          const cx = getW(10), cy = getW(20), r = getW(40);
          const a0 = (type === "ARC" ? getW(50) : 0) * Math.PI / 180;
          const a1 = (type === "ARC" ? getW(51) : 360) * Math.PI / 180;
          const coords = Array.from({ length: 25 }, (_, idx) => {
            const a = a0 + (a1 - a0) * idx / 24;
            return rdNaarWgs84(cx + r * Math.cos(a), cy + r * Math.sin(a));
          });
          features.push({ type: "Feature", geometry: { type: "LineString", coordinates: coords }, properties: { layer: getLaag() } });
        }
      } catch { /* skip */ }
      i = einde;
    }
    return { type: "FeatureCollection", features };
  } catch { return null; }
}

// ─── GML-parser ───────────────────────────────────────────────────
function gmlNaarGeoJson(tekst) {
  try {
    const doc = new DOMParser().parseFromString(tekst, "text/xml");
    const features = [];
    doc.querySelectorAll("LineString, gml\\:LineString").forEach(el => {
      const raw = (el.querySelector("posList, gml\\:posList") ?? el.querySelector("coordinates"))?.textContent?.trim();
      if (!raw) return;
      const nums = raw.split(/[\s,]+/).map(Number).filter(n => !isNaN(n));
      const coords = [];
      for (let i = 0; i < nums.length - 1; i += 2) coords.push(rdNaarWgs84(nums[i], nums[i + 1]));
      if (coords.length >= 2) features.push({ type: "Feature", geometry: { type: "LineString", coordinates: coords }, properties: {} });
    });
    return { type: "FeatureCollection", features };
  } catch { return null; }
}

// ════════════════════════════════════════════════════════════════
//  OntwerpKaart component
// ════════════════════════════════════════════════════════════════
export default function OntwerpKaart({ project, projectId, onOpgeslagen }) {
  const mapElRef = useRef(null);
  const kaartRef = useRef(null);
  const LRef     = useRef(null);
  const lagenRef = useRef({});

  const bestanden = (() => { try { return JSON.parse(project.bestanden_meta || "[]"); } catch { return []; } })();

  const initInst = (() => {
    const op = (() => { try { return JSON.parse(project.laag_instellingen || "{}"); } catch { return {}; } })();
    const r = {};
    for (const b of bestanden) r[b.id] = op[b.id] ?? standaardInst(b.type);
    return r;
  })();

  const [instellingen,  setInstellingen]  = useState(initInst);
  const [bestandStatus, setBestandStatus] = useState({});
  const [opslaanActief, setOpslaanActief] = useState(false);
  const [ingeslagen,    setIngeslagen]    = useState(false);

  // ── Kaart initialiseren ───────────────────────────────────────
  useEffect(() => {
    let actief = true;
    (async () => {
      if (typeof window === "undefined" || !mapElRef.current || kaartRef.current) return;

      const L = await laadLeaflet();
      if (!L || !actief || !mapElRef.current) return;
      LRef.current = L;

      // Fix standaard marker-iconen
      delete L.Icon.Default.prototype._getIconUrl;
      L.Icon.Default.mergeOptions({
        iconRetinaUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",
        iconUrl:       "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
        shadowUrl:     "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
      });

      const kaart = L.map(mapElRef.current, { zoomControl: true, preferCanvas: true }).setView([52.3, 5.3], 13);
      kaartRef.current = kaart;

      L.tileLayer(
        "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:3857/{z}/{x}/{y}.png",
        { maxZoom: 22, maxNativeZoom: 19, attribution: "© PDOK BRT, © PDOK" }
      ).addTo(kaart);

      // Laad alle bestanden
      let eersteGeladen = false;
      for (const bestand of bestanden) {
        const inst = instellingen[bestand.id] ?? standaardInst(bestand.type);
        const laag = await laadBestandOp(bestand, inst);
        if (laag) {
          if (inst.zichtbaar) laag.addTo(kaart);
          lagenRef.current[bestand.id] = laag;
          if (!eersteGeladen) {
            try { kaart.fitBounds(laag.getBounds().pad(0.15)); eersteGeladen = true; } catch {}
          }
        }
      }
    })();

    return () => {
      actief = false;
      if (kaartRef.current) { kaartRef.current.remove(); kaartRef.current = null; }
    };
  }, []);

  // ── Bestand ophalen & parsen ──────────────────────────────────
  async function laadBestandOp(bestand, inst) {
    if (!bestand.url) return null;
    setBestandStatus(s => ({ ...s, [bestand.id]: "Laden…" }));
    try {
      const res = await fetch(bestand.url);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const ext = bestand.naam.split(".").pop().toLowerCase();
      let geoJson = null;

      if (ext === "dxf")                    { geoJson = dxfNaarGeoJson(await res.text()); }
      else if (ext === "gml" || ext === "xml") { geoJson = gmlNaarGeoJson(await res.text()); }
      else if (ext === "geojson" || ext === "json") { geoJson = await res.json(); }

      if (!geoJson?.features?.length) {
        setBestandStatus(s => ({ ...s, [bestand.id]: "Geen geometrieën gevonden" }));
        return null;
      }

      const L = LRef.current;
      if (!L) return null;

      const laag = L.geoJSON(geoJson, {
        style: () => ({ color: inst.kleur, weight: inst.dikte, opacity: inst.helderheid, fillOpacity: inst.helderheid * 0.25 }),
        pointToLayer: (_, ll) => L.circleMarker(ll, { radius: 4, color: inst.kleur, weight: 1, fillOpacity: 0.7 }),
      });

      setBestandStatus(s => ({ ...s, [bestand.id]: `✓ ${geoJson.features.length} objecten` }));
      return laag;
    } catch (err) {
      setBestandStatus(s => ({ ...s, [bestand.id]: `✗ ${err.message}` }));
      return null;
    }
  }

  // ── Live instelling bijwerken ─────────────────────────────────
  function wijzig(bestandId, sleutel, waarde) {
    setInstellingen(prev => {
      const nieuw = { ...prev, [bestandId]: { ...(prev[bestandId] ?? standaardInst("")), [sleutel]: waarde } };
      const kaart = kaartRef.current;
      const laag  = lagenRef.current[bestandId];
      if (kaart && laag) {
        if (sleutel === "zichtbaar") {
          if (waarde) { if (!kaart.hasLayer(laag)) kaart.addLayer(laag); }
          else        { if ( kaart.hasLayer(laag)) kaart.removeLayer(laag); }
        } else {
          const i = nieuw[bestandId];
          laag.setStyle({ color: i.kleur, weight: i.dikte, opacity: i.helderheid, fillOpacity: i.helderheid * 0.25 });
        }
      }
      return nieuw;
    });
  }

  // ── Opslaan ───────────────────────────────────────────────────
  async function handleOpslaan() {
    setOpslaanActief(true);
    try {
      await updateProject(projectId, { laag_instellingen: JSON.stringify(instellingen) });
      onOpgeslagen?.();
      setIngeslagen(true);
      setTimeout(() => setIngeslagen(false), 2500);
    } catch (err) { console.error(err); }
    finally { setOpslaanActief(false); }
  }

  // ── Render ────────────────────────────────────────────────────
  return (
    <div className="flex gap-4" style={{ height: "calc(100vh - 168px)", minHeight: 480 }}>

      {/* Lagenpaneel */}
      <div className="w-72 flex-shrink-0 bg-white border border-gray-200 rounded-xl flex flex-col overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100">
          <span className="text-sm font-semibold text-gray-800">Lagen</span>
          <button
            onClick={handleOpslaan}
            disabled={opslaanActief}
            className={`px-3 py-1 text-xs rounded-lg font-medium transition-colors ${
              ingeslagen ? "bg-green-500 text-white" : "bg-orange-500 text-white hover:bg-orange-600 disabled:opacity-50"
            }`}
          >
            {ingeslagen ? "✓ Opgeslagen" : opslaanActief ? "Opslaan…" : "Instellingen opslaan"}
          </button>
        </div>

        <div className="flex-1 overflow-y-auto">
          {bestanden.length === 0 ? (
            <div className="p-6 text-center space-y-2">
              <div className="text-2xl">📂</div>
              <p className="text-sm text-gray-600 font-medium">Geen bestanden</p>
              <p className="text-xs text-gray-400">Upload ontwerpen in stap 2.</p>
            </div>
          ) : (
            <div className="divide-y divide-gray-100">
              {bestanden.map(b => {
                const inst = instellingen[b.id] ?? standaardInst(b.type);
                return (
                  <div key={b.id} className="px-4 py-3 space-y-2">
                    {/* Naam + toggle */}
                    <div className="flex items-center gap-2">
                      <div className="w-3 h-3 rounded-full flex-shrink-0 border border-white shadow" style={{ background: inst.kleur }} />
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-gray-800 truncate">{b.naam}</div>
                        <div className="text-xs text-gray-400">{b.type}</div>
                      </div>
                      <button
                        onClick={() => wijzig(b.id, "zichtbaar", !inst.zichtbaar)}
                        className={`relative w-9 h-5 rounded-full transition-colors flex-shrink-0 ${inst.zichtbaar ? "bg-orange-500" : "bg-gray-200"}`}
                      >
                        <span className={`absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform ${inst.zichtbaar ? "translate-x-4" : "translate-x-0.5"}`} />
                      </button>
                    </div>

                    {/* Status */}
                    {bestandStatus[b.id] && (
                      <div className={`text-xs pl-5 ${bestandStatus[b.id].startsWith("✓") ? "text-green-600" : bestandStatus[b.id].startsWith("✗") ? "text-red-500" : "text-gray-400"}`}>
                        {bestandStatus[b.id]}
                      </div>
                    )}

                    {/* Sliders */}
                    <div className={`space-y-2 pl-5 ${!inst.zichtbaar ? "opacity-40 pointer-events-none" : ""}`}>
                      <div className="flex items-center gap-2">
                        <span className="text-xs text-gray-500 w-16">Kleur</span>
                        <input type="color" value={inst.kleur} onChange={e => wijzig(b.id, "kleur", e.target.value)}
                          className="w-8 h-5 rounded cursor-pointer border-0 p-0 bg-transparent" />
                        <span className="text-xs text-gray-400">{inst.kleur}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs text-gray-500 w-16">Dikte</span>
                        <input type="range" min="0.5" max="8" step="0.5" value={inst.dikte}
                          onChange={e => wijzig(b.id, "dikte", Number(e.target.value))}
                          className="flex-1 accent-orange-500 h-1" />
                        <span className="text-xs text-gray-400 w-7 text-right">{inst.dikte}px</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs text-gray-500 w-16">Helderheid</span>
                        <input type="range" min="0.1" max="1" step="0.05" value={inst.helderheid}
                          onChange={e => wijzig(b.id, "helderheid", Number(e.target.value))}
                          className="flex-1 accent-orange-500 h-1" />
                        <span className="text-xs text-gray-400 w-7 text-right">{Math.round(inst.helderheid * 100)}%</span>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        <div className="border-t border-gray-100 px-4 py-2">
          <p className="text-xs text-gray-400">Instellingen worden meegenomen als ondergrond in stap 4 t/m 8.</p>
        </div>
      </div>

      {/* Kaart */}
      <div className="flex-1 bg-white border border-gray-200 rounded-xl overflow-hidden">
        <div ref={mapElRef} className="w-full h-full" />
      </div>
    </div>
  );
}
