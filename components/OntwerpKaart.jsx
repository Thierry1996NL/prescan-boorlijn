"use client";

import { useState, useEffect, useRef } from "react";
import { updateProject } from "@/lib/supabase-queries";

// ─── IMKL thema configuratie ──────────────────────────────────────
const THEMA = {
  laagspanning:              { label: "Laagspanning",        kleur: "#f59e0b" },
  middenspanning:            { label: "Middenspanning",       kleur: "#f97316" },
  hoogspanning:              { label: "Hoogspanning",         kleur: "#ef4444" },
  gasLageDruk:               { label: "Gas (lage druk)",      kleur: "#eab308" },
  gasHogeDruk:               { label: "Gas (hoge druk)",      kleur: "#ca8a04" },
  water:                     { label: "Water",                kleur: "#3b82f6" },
  datatransport:             { label: "Data / Telecom",       kleur: "#8b5cf6" },
  rioolVrijverval:           { label: "Riool (vrijverval)",   kleur: "#92400e" },
  rioolOnderOverOfOnderdruk: { label: "Riool (druk)",         kleur: "#78350f" },
  warmte:                    { label: "Warmte",               kleur: "#dc2626" },
  overig:                    { label: "Overig",               kleur: "#6b7280" },
};

// ─── Achtergrond- en overlaylagen ────────────────────────────────
// BRT via WMTS (snelle tiles, werkt nu origin correct is ingesteld)
// Luchtfoto via WMS (PDOK luchtfoto heeft geen betrouwbare WMTS voor deze CRS)
const WMTS_BRT = { minZoom: 0, maxNativeZoom: 13, maxZoom: 22, tileSize: 256, attribution: "© PDOK BRT, © Kadaster" };

const ACHTERGROND = [
  {
    id: "brt_standaard",
    label: "BRT Standaard",
    url: "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png",
    opties: { ...WMTS_BRT },
  },
  {
    id: "brt_grijs",
    label: "BRT Grijs",
    url: "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:28992/{z}/{x}/{y}.png",
    opties: { ...WMTS_BRT },
  },
  {
    id: "brt_pastel",
    label: "BRT Pastel",
    url: "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/pastel/EPSG:28992/{z}/{x}/{y}.png",
    opties: { ...WMTS_BRT },
  },
  {
    id: "luchtfoto",
    label: "Luchtfoto",
    wms: true,
    url: "https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",
    layers: "Actueel_ortho25",
    opties: { format: "image/jpeg", transparent: false, maxZoom: 22, attribution: "© PDOK, Beeldmateriaal NL" },
  },
];

const OVERLAYS = [
  {
    id: "kadaster",
    label: "Kadastrale percelen",
    url: "https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0",
    layers: "Perceel",
    kleur: "#8B4513",
  },
  {
    id: "bag_panden",
    label: "BAG Panden",
    url: "https://service.pdok.nl/lv/bag/wms/v2_0",
    layers: "pand",
    kleur: "#dc2626",
  },
  {
    id: "bag_adressen",
    label: "BAG Adressen",
    url: "https://service.pdok.nl/lv/bag/wms/v2_0",
    layers: "ligplaats,standplaats,verblijfsobject",
    kleur: "#2563eb",
  },
  {
    id: "bgt",
    label: "BGT (oppervlakten)",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0",
    layers: "wegdeel,waterdeel,ondersteunendwegdeel,begroeidterreindeel",
    kleur: "#16a34a",
  },
];

const TYPE_KLEUR = {
  LS: "#f59e0b", MS: "#f97316", Gas: "#eab308",
  Water: "#3b82f6", Data: "#8b5cf6", KLIC: "#6b7280",
};

function standaardInst(type)  { return { zichtbaar: true, kleur: TYPE_KLEUR[type] ?? "#3b82f6", dikte: 2, helderheid: 0.85 }; }
function standaardThemaInst(t){ return { zichtbaar: true, kleur: THEMA[t]?.kleur ?? "#6b7280", dikte: 2, helderheid: 0.85 }; }

// ─── CDN: Leaflet + proj4 + proj4leaflet ─────────────────────────
async function laadKaartBibliotheken() {
  if (typeof window === "undefined") return null;

  // 1. Leaflet CSS
  if (!document.querySelector('link[href*="leaflet"]')) {
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
    document.head.appendChild(link);
  }

  // 2. Leaflet JS
  if (!window.L?.version) {
    await new Promise((ok, err) => {
      const s = document.createElement("script");
      s.src = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
      s.onload = ok; s.onerror = err;
      document.head.appendChild(s);
    });
  }

  // 3. proj4
  if (!window.proj4) {
    await new Promise((ok, err) => {
      const s = document.createElement("script");
      s.src = "https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js";
      s.onload = ok; s.onerror = err;
      document.head.appendChild(s);
    });
  }

  // 4. proj4leaflet
  if (!window.L?.Proj) {
    await new Promise((ok, err) => {
      const s = document.createElement("script");
      s.src = "https://cdnjs.cloudflare.com/ajax/libs/proj4leaflet/1.0.2/proj4leaflet.js";
      s.onload = ok; s.onerror = err;
      document.head.appendChild(s);
    });
  }

  return window.L;
}

// ─── CDN: JSZip ──────────────────────────────────────────────────
async function laadJSZip() {
  if (window.JSZip) return window.JSZip;
  await new Promise((ok, err) => {
    const s = document.createElement("script");
    s.src = "https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";
    s.onload = ok; s.onerror = err;
    document.head.appendChild(s);
  });
  return window.JSZip;
}

// ─── RD New CRS definitie (EPSG:28992) ───────────────────────────
// Coördinaten blijven in meters — geen conversie nodig
function maakRdCrs(L) {
  return new L.Proj.CRS(
    "EPSG:28992",
    "+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 " +
    "+x_0=155000 +y_0=463000 +ellps=bessel " +
    "+towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 " +
    "+units=m +no_defs",
    {
      // Standaard PDOK zoom-niveaus voor EPSG:28992
      // 14 PDOK-niveaus (0-13) + 9 extra voor dieper inzoomen (14-22)
      // Extra niveaus halveren de resolutie telkens — tiles worden opgeschaald
      resolutions: [
        3440.640, 1720.320, 860.160, 430.080, 215.040,  // 0-4
        107.520,   53.760,   26.880,  13.440,   6.720,  // 5-9
          3.360,    1.680,    0.840,   0.420,   0.210,  // 10-14
          0.105,   0.0525,  0.02625, 0.013125, 0.00656, // 15-19
          0.00328, 0.00164, 0.00082,                    // 20-22
      ],
      // TopLeftCorner van PDOK WMTS GetCapabilities voor EPSG:28992
      // FOUT was: 22598.080 (onderste Y), CORRECT is: 903401.920 (bovenste Y)
      origin: [-285401.920, 903401.920],
      bounds: L.bounds(
        [-285401.920, 22598.080],
        [595401.920, 903401.920]
      ),
    }
  );
}

// ─── RD New → WGS84 voor vectordata display ──────────────────────
// proj4leaflet projiceert de kaart, maar L.geoJSON verwacht WGS84 coördinaten.
// Nadat proj4leaflet geladen is, gebruiken we proj4 voor de omrekening.
function rdNaarWgs84(x, y) {
  // Fallback wiskundige benadering als proj4 nog niet geladen is
  if (typeof window !== "undefined" && window.proj4) {
    try {
      const wgs = proj4("EPSG:28992", "EPSG:4326", [x, y]);
      return [wgs[0], wgs[1]]; // [lng, lat]
    } catch {}
  }
  // Benadering (nauwkeurig tot ~1m)
  if (Math.abs(x) <= 180 && Math.abs(y) <= 90) return [x, y];
  const dX = (x - 155000) / 100000, dY = (y - 463000) / 100000;
  const sumN = 3235.65389*dY + -32.58297*dX*dX + -0.24750*dY*dY
    + -0.84978*dX*dX*dY + -0.06550*dY*dY*dY;
  const sumE = 5260.52916*dX + 105.94684*dX*dY + 2.45656*dX*dY*dY
    + -0.81885*dX*dX*dX;
  return [5.38720621 + sumE / 3600, 52.15517440 + sumN / 3600];
}

// coordsToLatLng: RD [easting, northing] → L.latLng(wgs84_lat, wgs84_lng)
const rdCoordsNaarLatLng = (L) => ([x, y]) => {
  const [lng, lat] = rdNaarWgs84(x, y);
  return L.latLng(lat, lng);
};

// ─── IMKL XML parser ─────────────────────────────────────────────
// Coördinaten blijven in RD New [easting, northing] — geen conversie
function imklNaarGeoJson(xmlTekst, onVoortgang) {
  const doc = new DOMParser().parseFromString(xmlTekst, "text/xml");

  // 1. Netwerk-ID → thema
  const netThema = {};
  doc.querySelectorAll("Utiliteitsnet").forEach(net => {
    const id = net.getAttributeNS("http://www.opengis.net/gml/3.2", "id") ||
               net.getAttribute("gml:id") || "";
    const href = net.querySelector("thema")
      ?.getAttributeNS("http://www.w3.org/1999/xlink", "href") || "";
    if (id) netThema[id] = href.split("/").pop() || "overig";
  });
  onVoortgang?.("Netwerken gelezen…");

  // 2. UtilityLink geometrieën → groepeer per thema
  const themalagen = {};
  const links = doc.querySelectorAll("UtilityLink");
  let teller = 0;

  links.forEach(link => {
    teller++;
    if (teller % 100 === 0) onVoortgang?.(`${teller} / ${links.length} leidingen…`);

    const netHref = (link.querySelector("inNetwork")
      ?.getAttributeNS("http://www.w3.org/1999/xlink", "href") || "").replace(/^#/, "");
    const thema = netThema[netHref] ||
                  netThema["nl.imkl-" + netHref] ||
                  "overig";

    const posListEl = link.querySelector("posList");
    if (!posListEl) return;

    const nums = posListEl.textContent.trim().split(/\s+/).map(Number);
    // posList bevat X Y paren in RD New (meters) — direct gebruiken
    const coords = [];
    for (let i = 0; i + 1 < nums.length; i += 2) {
      coords.push([nums[i], nums[i + 1]]); // [easting, northing] in RD
    }
    if (coords.length < 2) return;

    if (!themalagen[thema]) themalagen[thema] = [];
    themalagen[thema].push({
      type: "Feature",
      geometry: { type: "LineString", coordinates: coords },
      properties: { thema },
    });
  });

  onVoortgang?.("GeoJSON bouwen…");

  const result = {};
  for (const [thema, features] of Object.entries(themalagen)) {
    result[thema] = { type: "FeatureCollection", features };
  }
  return result;
}

// ─── DXF parser — coördinaten blijven in RD ──────────────────────
function dxfNaarGeoJson(tekst) {
  try {
    const features = [];
    const regels = tekst.split(/\r?\n/);
    let i = 0;
    while (i < regels.length) {
      if (regels[i].trim() !== "0") { i++; continue; }
      const type = regels[i + 1]?.trim();
      let einde = i + 2;
      while (einde < regels.length && regels[einde].trim() !== "0") einde++;
      const blok = regels.slice(i, einde);
      const getW = c => { for (let k=0;k<blok.length-1;k++) if(blok[k].trim()===String(c)) return parseFloat(blok[k+1].trim())||0; return 0; };
      try {
        if (type === "LINE") {
          // [X, Y] = [easting, northing] in RD
          features.push({ type:"Feature", geometry:{type:"LineString",
            coordinates:[[getW(10),getW(20)],[getW(11),getW(21)]]}, properties:{} });
        } else if (type === "LWPOLYLINE" || type === "POLYLINE") {
          const coords = [];
          for (let k=0;k<blok.length-3;k++) {
            if (blok[k].trim()==="10" && blok[k+2]?.trim()==="20") {
              const x=parseFloat(blok[k+1].trim()), y=parseFloat(blok[k+3].trim());
              if (!isNaN(x)&&!isNaN(y)) coords.push([x, y]);
            }
          }
          if (coords.length>=2) features.push({type:"Feature",geometry:{type:"LineString",coordinates:coords},properties:{}});
        } else if (type === "POINT") {
          features.push({type:"Feature",geometry:{type:"Point",coordinates:[getW(10),getW(20)]},properties:{}});
        }
      } catch {}
      i = einde;
    }
    return { type:"FeatureCollection", features };
  } catch { return null; }
}

// ─── GML parser — RD coördinaten bewaren ─────────────────────────
function gmlNaarGeoJson(tekst) {
  try {
    const doc = new DOMParser().parseFromString(tekst, "text/xml");
    const features = [];
    doc.querySelectorAll("LineString, gml\\:LineString").forEach(el => {
      const raw = (el.querySelector("posList, gml\\:posList") || el.querySelector("coordinates"))?.textContent?.trim();
      if (!raw) return;
      const nums = raw.split(/[\s,]+/).map(Number).filter(n => !isNaN(n));
      const coords = [];
      for (let i=0;i<nums.length-1;i+=2) coords.push([nums[i], nums[i+1]]); // [X, Y] RD
      if (coords.length>=2) features.push({type:"Feature",geometry:{type:"LineString",coordinates:coords},properties:{}});
    });
    return { type:"FeatureCollection", features };
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
  const opgeslagenInst = (() => { try { return JSON.parse(project.laag_instellingen || "{}"); } catch { return {}; } })();

  const initInst = {};
  for (const b of bestanden) initInst[b.id] = opgeslagenInst[b.id] ?? standaardInst(b.type);
  for (const [k, v] of Object.entries(opgeslagenInst)) if (k.startsWith("klic_")) initInst[k] = v;

  const [instellingen,  setInstellingen]  = useState(initInst);
  const [klicLagen,     setKlicLagen]     = useState({});
  const [bestandStatus, setBestandStatus] = useState({});
  const [opslaanActief, setOpslaanActief] = useState(false);
  const [ingeslagen,    setIngeslagen]    = useState(false);
  const [foutmelding,   setFoutmelding]   = useState(null);
  const [rdCursor,      setRdCursor]      = useState(null);
  const [actieveAchtergrond, setActieveAchtergrond] = useState(opgeslagenInst.__achtergrond ?? "brt_standaard");
  const [actieveOverlays,    setActieveOverlays]    = useState(opgeslagenInst.__overlays ?? []);
  const basisLaagRef  = useRef(null);
  const overlayRefs   = useRef({});

  // ── Kaart init ────────────────────────────────────────────────
  useEffect(() => {
    let actief = true;
    (async () => {
      if (typeof window === "undefined" || !mapElRef.current || kaartRef.current) return;

      const L = await laadKaartBibliotheken();
      if (!L || !actief || !mapElRef.current) return;
      LRef.current = L;

      delete L.Icon.Default.prototype._getIconUrl;
      L.Icon.Default.mergeOptions({
        iconRetinaUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",
        iconUrl:       "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
        shadowUrl:     "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
      });

      // RD New CRS
      const rdCrs = maakRdCrs(L);

      // setView altijd in WGS84 (ook met RD CRS — proj4leaflet vereiste)
      const pos = opgeslagenInst.__kaartPositie;
      const startCenter = pos?.lat ? [pos.lat, pos.lng] : [52.156, 5.387]; // Nederland
      const startZoom   = pos?.zoom ?? 8;

      const kaart = L.map(mapElRef.current, {
        crs: rdCrs,
        zoomControl: true,
        preferCanvas: true,
        maxZoom: 22,  // Verder inzoomen dan PDOK native (13) via tile-upscaling
      }).setView(startCenter, startZoom);

      kaartRef.current = kaart;

      // Achtergrondlaag toevoegen
      function voegAchtergrondToe(id) {
        if (basisLaagRef.current) {
          kaart.removeLayer(basisLaagRef.current);
          basisLaagRef.current = null;
        }
        const config = ACHTERGROND.find(a => a.id === id) ?? ACHTERGROND[0];
        // Luchtfoto via WMS, rest via WMTS — zIndex 1 zodat overlays (200) altijd bovenop komen
        const laag = config.wms
          ? L.tileLayer.wms(config.url, { layers: config.layers, ...config.opties, zIndex: 1 })
          : L.tileLayer(config.url, { ...config.opties, zIndex: 1 });
        laag.addTo(kaart);
        basisLaagRef.current = laag;
      }

      function voegOverlayToe(id) {
        if (overlayRefs.current[id]) return;
        const config = OVERLAYS.find(o => o.id === id);
        if (!config) return;
        const laag = L.tileLayer.wms(config.url, {
          layers: config.layers,
          format: "image/png",
          transparent: true,
          opacity: 0.8,
          attribution: "© PDOK",
          zIndex: 200,  // Altijd boven de achtergrond
        });
        laag.addTo(kaart);
        overlayRefs.current[id] = laag;
      }

      function verwijderOverlay(id) {
        if (overlayRefs.current[id]) {
          kaart.removeLayer(overlayRefs.current[id]);
          delete overlayRefs.current[id];
        }
      }

      // Sla functies op in ref zodat state-updates ze kunnen aanroepen
      kaart._voegAchtergrondToe = voegAchtergrondToe;
      kaart._voegOverlayToe     = voegOverlayToe;
      kaart._verwijderOverlay   = verwijderOverlay;

      // Laad initiële achtergrond en overlays
      voegAchtergrondToe(opgeslagenInst.__achtergrond ?? "brt_standaard");
      for (const id of (opgeslagenInst.__overlays ?? [])) voegOverlayToe(id);

      // Live RD-coördinaten tonen via proj4 (WGS84 → EPSG:28992)
      kaart.on("mousemove", e => {
        try {
          const rd = proj4("EPSG:4326", "EPSG:28992", [e.latlng.lng, e.latlng.lat]);
          setRdCursor({ x: Math.round(rd[0]), y: Math.round(rd[1]) });
        } catch {}
      });
      kaart.on("mouseout", () => setRdCursor(null));

      // Laad bestanden
      const instSnapshot = { ...initInst };
      let eersteGeladen = false;

      for (const bestand of bestanden) {
        const ext = bestand.naam.split(".").pop().toLowerCase();
        const inst = instSnapshot[bestand.id] ?? standaardInst(bestand.type);

        if (ext === "zip") {
          await laadKlicBestand(bestand, inst, instSnapshot);
        } else {
          const laag = await laadEnkelBestand(bestand, inst);
          if (laag) {
            if (inst.zichtbaar) laag.addTo(kaart);
            lagenRef.current[bestand.id] = laag;
            if (!eersteGeladen) {
              try { kaart.fitBounds(laag.getBounds().pad(0.1)); eersteGeladen = true; } catch {}
            }
          }
        }
      }
    })();

    return () => {
      actief = false;
      if (kaartRef.current) { kaartRef.current.remove(); kaartRef.current = null; }
    };
  }, []);

  // ── KLIC ZIP laden ────────────────────────────────────────────
  async function laadKlicBestand(bestand, inst, instSnapshot) {
    if (!bestand.url) return;
    setBestandStatus(s => ({ ...s, [bestand.id]: "ZIP ophalen…" }));
    try {
      const res = await fetch(bestand.url);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const blob = await res.blob();

      setBestandStatus(s => ({ ...s, [bestand.id]: "ZIP uitpakken…" }));
      const JSZip = await laadJSZip();
      const zip   = await JSZip.loadAsync(blob);

      const xmlNaam = Object.keys(zip.files).find(n =>
        n.includes("GI_gebiedsinformatie") && n.endsWith(".xml")
      );
      if (!xmlNaam) throw new Error("Geen IMKL XML in ZIP");

      setBestandStatus(s => ({ ...s, [bestand.id]: "IMKL parsen…" }));
      const xmlTekst = await zip.files[xmlNaam].async("string");

      const lagen = imklNaarGeoJson(xmlTekst, msg =>
        setBestandStatus(s => ({ ...s, [bestand.id]: msg }))
      );

      setKlicLagen(lagen);

      const L    = LRef.current;
      const kaart = kaartRef.current;
      if (!L || !kaart) return;

      const nieuweInst = { ...instellingen };
      let eersteGeladen = false;

      for (const [thema, geoJson] of Object.entries(lagen)) {
        const lagId = `klic_${thema}`;
        const inst2 = instSnapshot[lagId] ?? standaardThemaInst(thema);
        nieuweInst[lagId] = inst2;

        // coordsToLatLng: RD [easting, northing] → L.latLng(northing, easting)
        const laag = L.geoJSON(geoJson, {
          coordsToLatLng: rdCoordsNaarLatLng(L),
          style: () => ({ color: inst2.kleur, weight: inst2.dikte, opacity: inst2.helderheid, fillOpacity: inst2.helderheid * 0.2 }),
        });

        if (inst2.zichtbaar) laag.addTo(kaart);
        lagenRef.current[lagId] = laag;

        if (!eersteGeladen) {
          try { kaart.fitBounds(laag.getBounds().pad(0.05)); eersteGeladen = true; } catch {}
        }
      }

      setInstellingen(nieuweInst);

      const totaal = Object.values(lagen).reduce((s, g) => s + g.features.length, 0);
      setBestandStatus(s => ({ ...s, [bestand.id]: `✓ ${totaal} objecten · ${Object.keys(lagen).length} lagen` }));
    } catch (err) {
      console.error("KLIC:", err);
      setBestandStatus(s => ({ ...s, [bestand.id]: `✗ ${err.message}` }));
    }
  }

  // ── Enkel bestand laden ───────────────────────────────────────
  async function laadEnkelBestand(bestand, inst) {
    if (!bestand.url) return null;
    setBestandStatus(s => ({ ...s, [bestand.id]: "Laden…" }));
    try {
      const res = await fetch(bestand.url);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const ext = bestand.naam.split(".").pop().toLowerCase();
      let geoJson = null;

      if (ext === "dxf")                       geoJson = dxfNaarGeoJson(await res.text());
      else if (ext === "gml" || ext === "xml")  geoJson = gmlNaarGeoJson(await res.text());
      else if (ext === "geojson" || ext === "json") geoJson = await res.json();

      if (!geoJson?.features?.length) {
        setBestandStatus(s => ({ ...s, [bestand.id]: "Geen geometrieën" }));
        return null;
      }

      const L = LRef.current;
      if (!L) return null;

      const laag = L.geoJSON(geoJson, {
        coordsToLatLng: rdCoordsNaarLatLng(L),
        style: () => ({ color: inst.kleur, weight: inst.dikte, opacity: inst.helderheid, fillOpacity: inst.helderheid * 0.2 }),
        pointToLayer: (_, ll) => L.circleMarker(ll, { radius: 4, color: inst.kleur, weight: 1, fillOpacity: 0.7 }),
      });

      setBestandStatus(s => ({ ...s, [bestand.id]: `✓ ${geoJson.features.length} objecten` }));
      return laag;
    } catch (err) {
      setBestandStatus(s => ({ ...s, [bestand.id]: `✗ ${err.message}` }));
      return null;
    }
  }

  // ── Achtergrond wisselen ─────────────────────────────────────
  function wisselAchtergrond(id) {
    setActieveAchtergrond(id);
    kaartRef.current?._voegAchtergrondToe?.(id);
  }

  function toggleOverlay(id) {
    setActieveOverlays(prev => {
      const actief = prev.includes(id);
      if (actief) {
        kaartRef.current?._verwijderOverlay?.(id);
        return prev.filter(o => o !== id);
      } else {
        kaartRef.current?._voegOverlayToe?.(id);
        return [...prev, id];
      }
    });
  }

  // ── Live laagstijl wijzigen ───────────────────────────────────
  function wijzig(lagId, sleutel, waarde) {
    setInstellingen(prev => {
      const isKlic = lagId.startsWith("klic_");
      const huidig = prev[lagId] ?? (isKlic ? standaardThemaInst(lagId.replace("klic_", "")) : standaardInst(""));
      const nieuw = { ...prev, [lagId]: { ...huidig, [sleutel]: waarde } };
      const kaart = kaartRef.current;
      const laag  = lagenRef.current[lagId];
      if (kaart && laag) {
        if (sleutel === "zichtbaar") {
          if (waarde) { if (!kaart.hasLayer(laag)) kaart.addLayer(laag); }
          else        { if ( kaart.hasLayer(laag)) kaart.removeLayer(laag); }
        } else {
          const i = nieuw[lagId];
          laag.setStyle({ color: i.kleur, weight: i.dikte, opacity: i.helderheid, fillOpacity: i.helderheid * 0.2 });
        }
      }
      return nieuw;
    });
  }

  // ── Opslaan (positie in RD New) ───────────────────────────────
  async function handleOpslaan() {
    setOpslaanActief(true);
    setFoutmelding(null);
    try {
      const teOpslaan = { ...instellingen };
      if (kaartRef.current) {
        const c = kaartRef.current.getCenter();
        // Sla op als WGS84 (proj4leaflet setView verwacht WGS84)
        teOpslaan.__kaartPositie = {
          lat:  c.lat,
          lng:  c.lng,
          zoom: kaartRef.current.getZoom(),
        };
        teOpslaan.__achtergrond = actieveAchtergrond;
        teOpslaan.__overlays    = actieveOverlays;
      }
      await updateProject(projectId, { laag_instellingen: JSON.stringify(teOpslaan) });
      onOpgeslagen?.();
      setIngeslagen(true);
      setTimeout(() => setIngeslagen(false), 2500);
    } catch (err) {
      console.error("Opslaan:", err);
      setFoutmelding(err?.message ?? "Onbekende fout");
      setTimeout(() => setFoutmelding(null), 6000);
    } finally {
      setOpslaanActief(false);
    }
  }

  // ── Sub-component: slider controls ───────────────────────────
  function LaagControls({ lagId, inst }) {
    return (
      <div className={`space-y-1.5 pl-5 ${!inst.zichtbaar ? "opacity-40 pointer-events-none" : ""}`}>
        <div className="flex items-center gap-2">
          <span className="text-xs text-gray-500 w-16">Kleur</span>
          <input type="color" value={inst.kleur} onChange={e => wijzig(lagId, "kleur", e.target.value)}
            className="w-8 h-5 rounded cursor-pointer border-0 p-0" />
          <span className="text-xs text-gray-400">{inst.kleur}</span>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs text-gray-500 w-16">Dikte</span>
          <input type="range" min="0.5" max="8" step="0.5" value={inst.dikte}
            onChange={e => wijzig(lagId, "dikte", Number(e.target.value))}
            className="flex-1 accent-orange-500 h-1" />
          <span className="text-xs text-gray-400 w-7 text-right">{inst.dikte}px</span>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs text-gray-500 w-16">Helderheid</span>
          <input type="range" min="0.1" max="1" step="0.05" value={inst.helderheid}
            onChange={e => wijzig(lagId, "helderheid", Number(e.target.value))}
            className="flex-1 accent-orange-500 h-1" />
          <span className="text-xs text-gray-400 w-7 text-right">{Math.round(inst.helderheid * 100)}%</span>
        </div>
      </div>
    );
  }

  function Toggle({ lagId, inst }) {
    return (
      <button onClick={() => wijzig(lagId, "zichtbaar", !inst.zichtbaar)}
        className={`relative flex-shrink-0 rounded-full transition-colors ${inst.zichtbaar ? "bg-orange-500" : "bg-gray-200"}`}
        style={{ width: 34, height: 18 }}>
        <span className={`absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform ${inst.zichtbaar ? "translate-x-4" : "translate-x-0.5"}`} />
      </button>
    );
  }

  const gewoneLagen   = bestanden.filter(b => !b.naam.toLowerCase().endsWith(".zip"));
  const klicBestanden = bestanden.filter(b => b.naam.toLowerCase().endsWith(".zip"));
  const klicThemas    = Object.keys(klicLagen).length > 0
    ? Object.keys(klicLagen)
    : Object.keys(instellingen).filter(k => k.startsWith("klic_")).map(k => k.replace("klic_", ""));

  return (
    <div className="flex gap-4" style={{ height: "calc(100vh - 168px)", minHeight: 480 }}>

      {/* ── Lagenpaneel ──────────────────────────────────────── */}
      <div className="flex-shrink-0 bg-white border border-gray-200 rounded-xl flex flex-col overflow-hidden" style={{ width: 296 }}>
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100">
          <span className="text-sm font-semibold text-gray-800">Lagen</span>
          <button onClick={handleOpslaan} disabled={opslaanActief}
            className={`px-3 py-1 text-xs rounded-lg font-medium transition-colors ${
              ingeslagen ? "bg-green-500 text-white" : "bg-orange-500 text-white hover:bg-orange-600 disabled:opacity-50"
            }`}>
            {ingeslagen ? "✓ Opgeslagen" : opslaanActief ? "Opslaan…" : "📍 Positie & lagen opslaan"}
          </button>
        </div>

        <div className="flex-1 overflow-y-auto">
          {/* ── Achtergrondkaart ── */}
          <div className="px-4 py-3 border-b border-gray-100">
            <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Achtergrond</div>
            <div className="space-y-1">
              {ACHTERGROND.map(a => (
                <button key={a.id} onClick={() => wisselAchtergrond(a.id)}
                  className={`flex items-center gap-2 w-full px-2 py-1.5 rounded-lg text-left transition-colors ${
                    actieveAchtergrond === a.id ? "bg-orange-50 text-orange-700" : "text-gray-600 hover:bg-gray-50"
                  }`}>
                  <div className={`w-3 h-3 rounded-full border-2 flex-shrink-0 ${actieveAchtergrond === a.id ? "border-orange-500 bg-orange-500" : "border-gray-300"}`} />
                  <span className="text-xs font-medium">{a.label}</span>
                </button>
              ))}
            </div>
          </div>

          {/* ── Overlay lagen (WMS) ── */}
          <div className="px-4 py-3 border-b border-gray-200">
            <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Overlays</div>
            <div className="space-y-1">
              {OVERLAYS.map(o => {
                const aan = actieveOverlays.includes(o.id);
                return (
                  <button key={o.id} onClick={() => toggleOverlay(o.id)}
                    className={`flex items-center gap-2 w-full px-2 py-1.5 rounded-lg text-left transition-colors ${
                      aan ? "bg-blue-50" : "hover:bg-gray-50"
                    }`}>
                    <div className="w-3 h-3 rounded flex-shrink-0 border border-gray-200"
                      style={{ background: aan ? o.kleur : "transparent" }} />
                    <span className={`text-xs font-medium ${aan ? "text-blue-700" : "text-gray-600"}`}>{o.label}</span>
                    {aan && <span className="ml-auto text-xs text-blue-400">aan</span>}
                  </button>
                );
              })}
            </div>
          </div>

          {bestanden.length === 0 ? (
            <div className="p-6 text-center space-y-2">
              <div className="text-2xl">📂</div>
              <p className="text-sm text-gray-600 font-medium">Geen bestanden</p>
              <p className="text-xs text-gray-400">Upload ontwerpen in stap 2.</p>
            </div>
          ) : (
            <div className="divide-y divide-gray-100">
              {/* Gewone DXF/GML bestanden */}
              {gewoneLagen.map(b => {
                const inst = instellingen[b.id] ?? standaardInst(b.type);
                return (
                  <div key={b.id} className="px-4 py-3 space-y-2">
                    <div className="flex items-center gap-2">
                      <div className="w-3 h-3 rounded-full flex-shrink-0" style={{ background: inst.kleur }} />
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-gray-800 truncate">{b.naam}</div>
                        <div className="text-xs text-gray-400">{b.type}</div>
                      </div>
                      <Toggle lagId={b.id} inst={inst} />
                    </div>
                    {bestandStatus[b.id] && (
                      <div className={`text-xs pl-5 ${bestandStatus[b.id].startsWith("✓") ? "text-green-600" : bestandStatus[b.id].startsWith("✗") ? "text-red-500" : "text-gray-400"}`}>
                        {bestandStatus[b.id]}
                      </div>
                    )}
                    <LaagControls lagId={b.id} inst={inst} />
                  </div>
                );
              })}

              {/* KLIC ZIP → sub-lagen per thema */}
              {klicBestanden.map(b => (
                <div key={b.id}>
                  <div className="px-4 py-2 bg-gray-50">
                    <div className="flex items-center gap-2">
                      <span>🗂️</span>
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-semibold text-gray-700 truncate">{b.naam}</div>
                        <div className="text-xs text-gray-400">KLIC-melding · IMKL</div>
                      </div>
                    </div>
                    {bestandStatus[b.id] && (
                      <div className={`text-xs mt-1 ml-6 ${
                        bestandStatus[b.id].startsWith("✓") ? "text-green-600" :
                        bestandStatus[b.id].startsWith("✗") ? "text-red-500" : "text-orange-500"
                      }`}>{bestandStatus[b.id]}</div>
                    )}
                  </div>

                  {klicThemas.length > 0 ? klicThemas.map(thema => {
                    const lagId = `klic_${thema}`;
                    const inst  = instellingen[lagId] ?? standaardThemaInst(thema);
                    const config = THEMA[thema] ?? { label: thema };
                    const n = klicLagen[thema]?.features?.length;
                    return (
                      <div key={lagId} className="px-4 py-2 space-y-1.5 border-t border-gray-50">
                        <div className="flex items-center gap-2">
                          <div className="w-2.5 h-2.5 rounded-full flex-shrink-0 ml-3" style={{ background: inst.kleur }} />
                          <div className="flex-1 min-w-0">
                            <div className="text-xs font-medium text-gray-700">{config.label}</div>
                            {n && <div className="text-xs text-gray-400">{n} objecten</div>}
                          </div>
                          <Toggle lagId={lagId} inst={inst} />
                        </div>
                        <LaagControls lagId={lagId} inst={inst} />
                      </div>
                    );
                  }) : (
                    <div className="px-4 py-3 text-xs text-gray-400 italic pl-8">
                      {bestandStatus[b.id] ? "Laden…" : "Geen lagen geladen"}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Footer: RD cursor + foutmelding */}
        <div className="border-t border-gray-100 px-4 py-2 space-y-1">
          {rdCursor && (
            <div className="text-xs font-mono text-gray-500 bg-gray-50 rounded px-2 py-1">
              RD X: {rdCursor.x.toLocaleString("nl-NL")} · Y: {rdCursor.y.toLocaleString("nl-NL")}
            </div>
          )}
          {foutmelding && <p className="text-xs text-red-500 font-medium">✗ {foutmelding}</p>}
          <p className="text-xs text-gray-400">Kaart in RD New (EPSG:28992) · instellingen gelden ook in stap 4 t/m 8.</p>
        </div>
      </div>

      {/* ── Kaart ────────────────────────────────────────────── */}
      <div className="flex-1 bg-white border border-gray-200 rounded-xl overflow-hidden">
        <div ref={mapElRef} className="w-full h-full" />
      </div>
    </div>
  );
}
