"use client";

import { useEffect, useRef, useState } from "react";

export default function MapTrace({ project, onTraceOpgeslagen }) {
  const mapRef = useRef(null);
  const leafletMapRef = useRef(null);
  const pandLaagRef = useRef(null);
  const perceelLaagRef = useRef(null);
  const traceLaagRef = useRef(null);
  const [pandAan, setPandAan] = useState(true);
  const [perceelAan, setPerceelAan] = useState(true);
  const [tekenModus, setTekenModus] = useState(false);
  const [punten, setPunten] = useState([]);
  const [opgeslagen, setOpgeslagen] = useState(false);

  useEffect(() => {
    if (typeof window === "undefined") return;
    if (leafletMapRef.current) return;

    // Laad Leaflet dynamisch
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

    // Startpositie Nederland
    const kaart = L.map(mapRef.current, {
      center: [52.15, 5.38],
      zoom: 8,
      zoomControl: true,
    });

    leafletMapRef.current = kaart;

    // PDOK achtergrondkaart (BRT)
    L.tileLayer(
      "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:3857/{z}/{x}/{y}.png",
      {
        attribution: "© PDOK / Kadaster",
        maxZoom: 19,
      }
    ).addTo(kaart);

    // PDOK BAG Panden WMS
    const pandLaag = L.tileLayer.wms(
      "https://service.pdok.nl/lv/bag/wms/v2_0",
      {
        layers: "pand",
        format: "image/png",
        transparent: true,
        opacity: 0.6,
        attribution: "© PDOK BAG",
      }
    );
    pandLaag.addTo(kaart);
    pandLaagRef.current = pandLaag;

    // PDOK Kadastrale Percelen WMS
    const perceelLaag = L.tileLayer.wms(
      "https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0",
      {
        layers: "Perceel",
        format: "image/png",
        transparent: true,
        opacity: 0.5,
        attribution: "© PDOK Kadaster",
      }
    );
    perceelLaag.addTo(kaart);
    perceelLaagRef.current = perceelLaag;

    // Bestaand tracé tekenen als dat er al is
    if (project?.boortrace_geojson) {
      try {
        const geojson = typeof project.boortrace_geojson === "string"
          ? JSON.parse(project.boortrace_geojson)
          : project.boortrace_geojson;

        const traceLaag = L.geoJSON(geojson, {
          style: { color: "#2563eb", weight: 3, opacity: 0.9 },
        }).addTo(kaart);

        traceLaagRef.current = traceLaag;
        kaart.fitBounds(traceLaag.getBounds(), { padding: [40, 40] });
      } catch (e) {
        console.error("GeoJSON fout:", e);
      }
    }

    // Klik handler voor tekenen
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

  // Ref voor tekenModus zodat de event handler altijd de laatste waarde heeft
  const tekenModusRef = useRef(false);
  useEffect(() => {
    tekenModusRef.current = tekenModus;
    if (leafletMapRef.current) {
      leafletMapRef.current.getContainer().style.cursor = tekenModus ? "crosshair" : "";
    }
  }, [tekenModus]);

  // Tijdelijke tekenlijn
  const tekenLijnLaagRef = useRef(null);
  function tekenLijn(kaart, pts) {
    const L = window.L;
    if (tekenLijnLaagRef.current) kaart.removeLayer(tekenLijnLaagRef.current);
    if (pts.length < 2) return;
    const lijn = L.polyline(pts, { color: "#2563eb", weight: 3, dashArray: "6 4", opacity: 0.8 });
    lijn.addTo(kaart);
    tekenLijnLaagRef.current = lijn;
  }

  function togglePanden() {
    const L = window.L;
    if (!leafletMapRef.current || !pandLaagRef.current) return;
    if (pandAan) {
      leafletMapRef.current.removeLayer(pandLaagRef.current);
    } else {
      pandLaagRef.current.addTo(leafletMapRef.current);
    }
    setPandAan(!pandAan);
  }

  function togglePercelen() {
    if (!leafletMapRef.current || !perceelLaagRef.current) return;
    if (perceelAan) {
      leafletMapRef.current.removeLayer(perceelLaagRef.current);
    } else {
      perceelLaagRef.current.addTo(leafletMapRef.current);
    }
    setPerceelAan(!perceelAan);
  }

  function resetTekenen() {
    setPunten([]);
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

  return (
    <div className="flex flex-col h-full gap-3">
      {/* Toolbar */}
      <div className="flex items-center gap-2 flex-wrap">
        {/* Lagen */}
        <div className="flex items-center gap-1 bg-white border border-gray-200 rounded-lg px-3 py-1.5">
          <span className="text-xs text-gray-400 mr-1">Lagen:</span>
          <button
            onClick={togglePanden}
            className={`flex items-center gap-1.5 text-xs font-medium px-2.5 py-1 rounded-md transition-colors ${
              pandAan ? "bg-orange-100 text-orange-700" : "bg-gray-100 text-gray-400"
            }`}
          >
            <span className="w-2 h-2 rounded-sm bg-current opacity-70" />
            Panden
          </button>
          <button
            onClick={togglePercelen}
            className={`flex items-center gap-1.5 text-xs font-medium px-2.5 py-1 rounded-md transition-colors ${
              perceelAan ? "bg-yellow-100 text-yellow-700" : "bg-gray-100 text-gray-400"
            }`}
          >
            <span className="w-2 h-2 rounded-sm bg-current opacity-70" />
            Percelen
          </button>
        </div>

        {/* Teken toolbar */}
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
                {punten.length === 0 ? "Klik op de kaart om te beginnen" : `${punten.length} punt${punten.length !== 1 ? "en" : ""}`}
              </span>
              <button
                onClick={resetTekenen}
                className="text-xs text-gray-500 hover:text-gray-700 px-2.5 py-1 rounded-md hover:bg-gray-100 transition-colors"
              >
                Reset
              </button>
              <button
                onClick={() => { setTekenModus(false); resetTekenen(); }}
                className="text-xs text-gray-500 hover:text-gray-700 px-2.5 py-1 rounded-md hover:bg-gray-100 transition-colors"
              >
                Annuleren
              </button>
              {punten.length >= 2 && (
                <button
                  onClick={opslaanTrace}
                  className="text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 px-3 py-1 rounded-md transition-colors"
                >
                  {opgeslagen ? "✓ Opgeslagen!" : "Opslaan"}
                </button>
              )}
            </>
          )}
        </div>
      </div>

      {/* Legenda */}
      <div className="flex items-center gap-4 text-xs text-gray-400 px-1">
        <span className="flex items-center gap-1.5">
          <span className="w-6 border-t-2 border-blue-600" />
          Boortracé
        </span>
        {pandAan && (
          <span className="flex items-center gap-1.5">
            <span className="w-3 h-3 bg-orange-200 border border-orange-400 rounded-sm" />
            BAG Panden
          </span>
        )}
        {perceelAan && (
          <span className="flex items-center gap-1.5">
            <span className="w-3 h-3 bg-yellow-100 border border-yellow-500 rounded-sm" />
            Kadastrale percelen
          </span>
        )}
      </div>

      {/* Kaart */}
      <div
        ref={mapRef}
        className="flex-1 rounded-xl border border-gray-200 overflow-hidden shadow-sm"
        style={{ minHeight: "500px" }}
      />
    </div>
  );
}
