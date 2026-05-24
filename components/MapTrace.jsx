"use client";
import { useEffect, useRef, useState } from "react";

// ─── RD New → WGS84 ──────────────────────────────────────────────
function rdNaarWgs84(x, y) {
  if (Math.abs(x) <= 180 && Math.abs(y) <= 90) return [x, y];
  if (typeof window !== "undefined" && window.proj4) {
    try { const w = proj4("EPSG:28992","EPSG:4326",[x,y]); return [w[0],w[1]]; } catch {}
  }
  const dX=(x-155000)/100000, dY=(y-463000)/100000;
  const sumN=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
  const sumE=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
  return [5.38720621+sumE/3600, 52.15517440+sumN/3600];
}

// ─── Liang-Barsky lijnclipping ───────────────────────────────────
function clipLijnen(coords, box) {
  if (!box || coords.length < 2) return box ? [] : [coords];
  const { lat1:yMin, lat2:yMax, lng1:xMin, lng2:xMax } = box;
  function lb(x1,y1,x2,y2) {
    const p=[-( x2-x1),x2-x1,-(y2-y1),y2-y1];
    const q=[x1-xMin,xMax-x1,y1-yMin,yMax-y1];
    let t0=0,t1=1;
    for(let i=0;i<4;i++){if(p[i]===0){if(q[i]<0)return null;}else{const t=q[i]/p[i];if(p[i]<0)t0=Math.max(t0,t);else t1=Math.min(t1,t);}}
    return t0>t1?null:[x1+t0*(x2-x1),y1+t0*(y2-y1),x1+t1*(x2-x1),y1+t1*(y2-y1)];
  }
  const segs=[]; let cur=null;
  for(let i=0;i<coords.length-1;i++){
    const[y1,x1]=coords[i],[y2,x2]=coords[i+1];
    const c=lb(x1,y1,x2,y2);
    if(!c){if(cur){segs.push(cur);cur=null;}continue;}
    const[cx1,cy1,cx2,cy2]=c;
    if(!cur){cur=[[cy1,cx1],[cy2,cx2]];}
    else{const l=cur[cur.length-1];if(Math.abs(l[0]-cy1)<1e-9&&Math.abs(l[1]-cx1)<1e-9)cur.push([cy2,cx2]);else{segs.push(cur);cur=[[cy1,cx1],[cy2,cx2]];}}
  }
  if(cur)segs.push(cur);
  return segs;
}

// ─── KLIC standaardkleuren ────────────────────────────────────────
const KLIC_KLEUR = {
  laagspanning:"#7B00AA", middenspanning:"#00CCFF", hoogspanning:"#FF4400",
  gasLageDruk:"#FFFF00", gasHogeDruk:"#FF0000", water:"#000080",
  datatransport:"#00CC00", rioolVrijverval:"#AA00CC", rioolOnderOverOfOnderdruk:"#AA00CC",
  warmte:"#FF6600", overig:"#888888", mantelbuis:"#4B5563", kabelbed:"#111827", duct:"#374151",
};
const KLIC_DASH = { mantelbuis:"10 5", kabelbed:"12 4", duct:"8 4" };

// ─── WGS84 → RD New (voor coördinatenkolom) ─────────────────────
function wgs84NaarRd(lng, lat) {
  if (typeof window !== "undefined" && window.proj4) {
    try { const r = proj4("EPSG:4326","EPSG:28992",[lng,lat]); return [Math.round(r[0]), Math.round(r[1])]; } catch {}
  }
  // Wiskundige benadering
  const dLat=(lat-52.15517440)*3600/3600, dLng=(lng-5.38720621)*3600/3600;
  const X=155000+190094.945*dLng-11832.228*dLat*dLng-144.221*dLng*dLat*dLat-32.391*dLng*dLng*dLng;
  const Y=463000+309056.544*dLat+3638.893*dLng*dLng-157.984*dLat*dLat*dLng-60.330*dLat*dLat*dLat;
  return [Math.round(X), Math.round(Y)];
}

// ─── Afstand (Haversine) ─────────────────────────────────────────
function afstandM([lat1,lng1],[lat2,lng2]) {
  const R=6371000, dLat=(lat2-lat1)*Math.PI/180, dLng=(lng2-lng1)*Math.PI/180;
  const a=Math.sin(dLat/2)**2+Math.cos(lat1*Math.PI/180)*Math.cos(lat2*Math.PI/180)*Math.sin(dLng/2)**2;
  return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
}

// ─── Achtergrond en overlay configuratie ─────────────────────────
const ACHTERGRONDEN = [
  { id:"brt_standaard", label:"BRT Standaard", url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png" },
  { id:"brt_grijs",     label:"BRT Grijs",     url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:28992/{z}/{x}/{y}.png" },
  { id:"brt_pastel",    label:"BRT Pastel",    url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/pastel/EPSG:28992/{z}/{x}/{y}.png" },
  { id:"luchtfoto",     label:"Luchtfoto",     url:null, wms:true },
];

// ─── RD New CRS helpers (zelfde als stap 3) ───────────────────────
function maakRdCrs(L) {
  return new L.Proj.CRS("EPSG:28992",
    "+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 +x_0=155000 +y_0=463000 +ellps=bessel +towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 +units=m +no_defs",
    {
      resolutions:[3440.640,1720.320,860.160,430.080,215.040,107.520,53.760,26.880,13.440,6.720,3.360,1.680,0.840,0.420,0.210,0.105,0.0525,0.02625,0.013125,0.00656,0.00328,0.00164,0.00082],
      origin:[-285401.920,903401.920],
      bounds:L.bounds([-285401.920,22598.080],[595401.920,903401.920]),
    }
  );
}
// RD [x,y] → Leaflet LatLng via proj4 (met fallback)
function rdNaarLatLng(L, x, y) {
  if (typeof window !== "undefined" && window.proj4) {
    try { const w = proj4("EPSG:28992","EPSG:4326",[x,y]); return L.latLng(w[1],w[0]); } catch {}
  }
  const dX=(x-155000)/100000,dY=(y-463000)/100000;
  const sumN=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
  const sumE=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
  return L.latLng(52.15517440+sumN/3600, 5.38720621+sumE/3600);
}

const OVERLAYS = [
  { id:"kadaster",   label:"Kadastrale percelen", kleur:"#f59e0b", url:"https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0", layers:"Perceel" },
  { id:"bag_panden", label:"BAG Panden",           kleur:"#dc2626", url:"https://service.pdok.nl/lv/bag/wms/v2_0",                  layers:"pand" },
  { id:"bag_adres",  label:"BAG Adressen",         kleur:"#2563eb", url:"https://service.pdok.nl/lv/bag/wms/v2_0",                  layers:"ligplaats,standplaats,verblijfsobject" },
  { id:"bgt",        label:"BGT oppervlakten",     kleur:"#16a34a", url:"https://service.pdok.nl/lv/bgt/wms/v1_0",                  layers:"wegdeel,waterdeel,ondersteunendwegdeel,begroeidterreindeel" },
  { id:"kunstwerken",label:"Duikers & Kunstwerken",kleur:"#8b5cf6", url:"https://service.pdok.nl/lv/bgt/wms/v1_0",                  layers:"kunstwerkdeel" },
  { id:"wegen",      label:"Wegdelen",             kleur:"#64748b", url:"https://service.pdok.nl/lv/bgt/wms/v1_0",                  layers:"wegdeel" },
  { id:"spoor",      label:"Spoorbaandelen",       kleur:"#dc2626", url:"https://service.pdok.nl/lv/bgt/wms/v1_0",                  layers:"spoor" },
  { id:"buisleid",   label:"Buisleidingen",        kleur:"#f97316", url:"https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0",  layers:"buisleiding" },
  { id:"ahn",        label:"AHN Hoogte",           kleur:"#84cc16", url:"https://service.pdok.nl/rws/ahn/wms/v1_0",                 layers:"dtm_05m" },
  { id:"gemeenten",  label:"Gemeentegrenzen",      kleur:"#10b981", url:"https://service.pdok.nl/cbs/gebiedsindelingen/2024/wms/v1_0", layers:"gemeente_gegeneraliseerd" },
];

// ════════════════════════════════════════════════════════════════
export default function MapTrace({ project, onTraceOpgeslagen, boringConfig }) {
  const mapRef        = useRef(null);
  const kaartRef      = useRef(null);
  const polyRef       = useRef(null);
  const markersRef    = useRef([]);
  const klicRef       = useRef([]);
  const boxRef        = useRef(null);
  const tekenRef      = useRef(false);
  const puntenRef     = useRef([]);
  const basisLaagRef  = useRef(null);  // achtergrond tile layer
  const overlayRefs   = useRef({});    // id → tile layer

  // s3 VÓÓR useState zodat we direct kunnen initialiseren
  const s3 = (() => { try { return JSON.parse(project?.laag_instellingen||"{}"); } catch { return {}; } })();

  const [controlePunten, setControlePunten] = useState([]);
  const [tekenModus,     setTekenModus]     = useState(false);
  const [opgeslagen,     setOpgeslagen]     = useState(false);
  const [opslaat,        setOpslaat]        = useState(false);
  const [verwijdert,     setVerwijdert]     = useState(false);
  const [isLaden,        setIsLaden]        = useState(false);
  const [laadBericht,    setLaadBericht]    = useState("");
  const [legendaOpen,    setLegendaOpen]    = useState(true);
  const [klicLagen,      setKlicLagen]      = useState([]);
  const [actieveAchtergrond, setActieveAchtergrond] = useState(s3.__achtergrond ?? "brt_standaard");
  const [actieveOverlays,    setActieveOverlays]    = useState(s3.__overlays    ?? []);
  const actieveOverlaysRef = useRef(s3.__overlays ?? []);

  // Bestaand tracé uit project
  const bestaandTrace = (() => {
    try {
      const g = project?.boortrace_geojson;
      if (!g) return [];
      const p = typeof g === "string" ? JSON.parse(g) : g;
      return p.coordinates?.map(([lng,lat]) => [lat,lng]) ?? [];
    } catch { return []; }
  })();

  const actievePunten = controlePunten.length >= 2 ? controlePunten : bestaandTrace;
  const heeftTrace    = actievePunten.length >= 2;

  // Houd refs gesynchroniseerd
  puntenRef.current        = actievePunten;
  tekenRef.current         = tekenModus;
  actieveOverlaysRef.current = actieveOverlays;

  // ── Kaart initialiseren ─────────────────────────────────────────
  useEffect(() => {
    if (typeof window === "undefined" || kaartRef.current) return;
    let actief = true;

    // Laad Leaflet CSS
    if (!document.querySelector('link[href*="leaflet"]')) {
      const css = document.createElement("link");
      css.rel = "stylesheet";
      css.href = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
      document.head.appendChild(css);
    }

    // Laad proj4 voor nauwkeurige RD→WGS84
    const laadScript = (src) => new Promise((ok,err) => {
      if (document.querySelector(`script[src="${src}"]`)) return ok();
      const s = document.createElement("script");
      s.src = src; s.onload = ok; s.onerror = err;
      document.head.appendChild(s);
    });

    const laadLeaflet = async () => {
      if (!document.querySelector('link[href*="leaflet"]')) {
        const css = document.createElement("link");
        css.rel = "stylesheet";
        css.href = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
        document.head.appendChild(css);
      }
      await laadScript("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js");
      // proj4 vóór proj4leaflet laden
      await laadScript("https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js");
      await laadScript("https://cdnjs.cloudflare.com/ajax/libs/proj4leaflet/1.0.2/proj4leaflet.js");
      return window.L;
    };

    const laadJSZip = () => new Promise((ok, err) => {
      if (window.JSZip) return ok(window.JSZip);
      const s = document.createElement("script");
      s.src = "https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";
      s.onload = () => ok(window.JSZip);
      s.onerror = err;
      document.head.appendChild(s);
    });

    (async () => {
      const L = await laadLeaflet();
      if (!actief || !mapRef.current) return;

      // Startpositie vanuit stap 3
      const pos    = s3.__kaartPositie;
      const center = pos ? [pos.lat, pos.lng] : [52.156, 5.387];
      const zoom   = pos?.zoom ?? 13;

      // RD New CRS — exact gelijk aan stap 3 (proj4leaflet vereist)
      let rdCrs;
      try { rdCrs = maakRdCrs(L); } catch(e) { console.warn("RD CRS:", e.message); }
      const kaart = L.map(mapRef.current, { ...(rdCrs ? { crs:rdCrs } : {}), center, zoom, maxZoom:22, zoomControl:true });
      kaartRef.current = kaart;

      // Achtergrond/overlays vanuit state (al geïnitialiseerd vanuit s3)
      // Gebruik de state-waarden zodat alles consistent is
      const initAchtergrond = s3.__achtergrond ?? "brt_standaard";
      const initOverlays    = s3.__overlays    ?? [];
      // Helper: zet achtergrond laag
      function zetAchtergrond(id) {
        if (basisLaagRef.current) { kaart.removeLayer(basisLaagRef.current); basisLaagRef.current = null; }
        if (id === "luchtfoto") {
          basisLaagRef.current = L.tileLayer.wms("https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",
            { layers:"Actueel_ortho25", format:"image/jpeg", transparent:false, maxZoom:22, attribution:"© PDOK", zIndex:1 }
          ).addTo(kaart);
        } else {
          const cfg = ACHTERGRONDEN.find(a => a.id === id) ?? ACHTERGRONDEN[0];
          basisLaagRef.current = L.tileLayer(cfg.url,
            { maxZoom:22, maxNativeZoom:13, minZoom:0, tileSize:256, attribution:"© PDOK BRT, © Kadaster", zIndex:1 }
          ).addTo(kaart);
        }
      }

      // Helper: zet overlay aan/uit
      function zetOverlay(id, aan) {
        if (aan) {
          if (overlayRefs.current[id]) return;
          const cfg = OVERLAYS.find(o => o.id === id); if (!cfg) return;
          overlayRefs.current[id] = L.tileLayer.wms(cfg.url,
            { layers:cfg.layers, format:"image/png", transparent:true, opacity:0.7, zIndex:200, attribution:"© PDOK" }
          ).addTo(kaart);
        } else {
          if (overlayRefs.current[id]) { kaart.removeLayer(overlayRefs.current[id]); delete overlayRefs.current[id]; }
        }
      }

      // Sla helpers op op kaart-object voor gebruik in state handlers
      kaart._zetAchtergrond = zetAchtergrond;
      kaart._zetOverlay     = zetOverlay;

      zetAchtergrond(initAchtergrond);
      initOverlays.forEach(id => zetOverlay(id, true));

      // Filterbox tonen (grijs, geen vulling)
      const box = s3.__kaartBox ?? null;
      if (box) {
        boxRef.current = L.rectangle([[box.lat1,box.lng1],[box.lat2,box.lng2]], {
          color:"#6b7280", weight:2, fillOpacity:0, interactive:false,
        }).addTo(kaart);
      }

      // Bestaand tracé tekenen + laden in controlePunten zodat drag werkt
      if (bestaandTrace.length >= 2) {
        const pts = [...bestaandTrace];
        setControlePunten(pts);
        puntenRef.current = pts;
        tekenLijn(L, kaart, pts);
        maakMarkers(L, kaart, pts);
      }

      // Kaart klik handler
      kaart.on("click", e => {
        if (!tekenRef.current) return;
        try {
          const { lat, lng } = e.latlng;
          setControlePunten(prev => {
            const nieuw = [...prev, [lat, lng]];
            puntenRef.current = nieuw;
            tekenRef.current && tekenLijn(L, kaart, nieuw);
            tekenRef.current && maakMarkers(L, kaart, nieuw);
            return nieuw;
          });
        } catch (err) { console.error("Klik:", err); }
      });

      // KLIC achtergrond laden na kaart init
      setTimeout(() => {
        if (!actief) return;
        laadKlicAchtergrond(L, kaart, project, s3, laadJSZip, setIsLaden, setLaadBericht, setKlicLagen, klicRef).catch(err => {
          console.warn("KLIC:", err);
          setIsLaden(false);
        });
      }, 500);
    })();

    return () => {
      actief = false;
      if (kaartRef.current) { kaartRef.current.remove(); kaartRef.current = null; }
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Achtergrond en overlay wisselen ─────────────────────────────
  function wisselAchtergrond(id) {
    setActieveAchtergrond(id);
    // Direct Leaflet aanroepen — niet in setState callback
    kaartRef.current?._zetAchtergrond?.(id);
  }

  function toggleOverlay(id) {
    // Gebruik ref voor huidige staat (voorkomt stale closure in setState)
    const huidig = actieveOverlaysRef.current;
    const aan    = !huidig.includes(id);
    const nieuw  = aan ? [...huidig, id] : huidig.filter(o => o !== id);
    actieveOverlaysRef.current = nieuw;
    setActieveOverlays(nieuw);
    // Leaflet BUITEN setState aanroepen
    kaartRef.current?._zetOverlay?.(id, aan);
  }

  // ── Tekenen: lijn en markers ────────────────────────────────────
  function tekenLijn(L, kaart, pts) {
    try {
      if (polyRef.current) { kaart.removeLayer(polyRef.current); polyRef.current = null; }
      if (pts.length < 2) return;
      const boorGewicht = boringConfig?.boringD ? Math.max(4, Math.min(18, Math.round(boringConfig.boringD / 25))) : 5;
      const lijn = L.polyline(pts, { color:"#2563eb", weight:boorGewicht, opacity:0.9, zIndex:500 }).addTo(kaart);
      // Klik op lijn om punt in te voegen
      lijn.on("click", e => {
        L.DomEvent.stopPropagation(e);
        if (!tekenRef.current) return;
        try {
          const { lat, lng } = e.latlng;
          const huidig = puntenRef.current;
          let besteIdx = 0, minDist = Infinity;
          for (let i = 0; i < huidig.length - 1; i++) {
            const mid = [(huidig[i][0]+huidig[i+1][0])/2, (huidig[i][1]+huidig[i+1][1])/2];
            const d = afstandM([lat,lng], mid);
            if (d < minDist) { minDist = d; besteIdx = i; }
          }
          const nieuw = [...huidig.slice(0, besteIdx+1), [lat,lng], ...huidig.slice(besteIdx+1)];
          puntenRef.current = nieuw;
          setControlePunten(nieuw);
          tekenLijn(L, kaart, nieuw);
          maakMarkers(L, kaart, nieuw);
        } catch (err) { console.error("Invoegen:", err); }
      });
      polyRef.current = lijn;
    } catch (err) { console.error("tekenLijn:", err); }
  }

  function maakMarkers(L, kaart, pts) {
    try {
      markersRef.current.forEach(m => { try { kaart.removeLayer(m); } catch {} });
      markersRef.current = [];
      pts.forEach(([lat, lng], idx) => {
        const isEerste = idx === 0, isLaatste = idx === pts.length - 1;
        const kleur = isEerste ? "#15803d" : isLaatste ? "#dc2626" : "#2563eb";
        const icon = L.divIcon({
          className: "",
          html: `<div style="width:18px;height:18px;background:${kleur};border:2px solid white;border-radius:${isEerste||isLaatste?"50%":"3px"};cursor:move;box-shadow:0 1px 4px rgba(0,0,0,0.4);display:flex;align-items:center;justify-content:center;font-size:8px;font-weight:700;color:white;">${idx+1}</div>`,
          iconSize:[18,18], iconAnchor:[9,9],
        });
        const marker = L.marker([lat,lng], { draggable:true, icon, zIndexOffset:1000 }).addTo(kaart);
        marker.on("drag", e => {
          try {
            const { lat:nLat, lng:nLng } = e.latlng;
            // puntenRef.current gebruiken — prev kan leeg zijn als bestaandTrace geladen was
            const nieuw = [...puntenRef.current];
            if (idx < 0 || idx >= nieuw.length) return;
            nieuw[idx] = [nLat, nLng];
            puntenRef.current = nieuw;
            setControlePunten(nieuw);
            tekenLijn(L, kaart, nieuw);
          } catch (err) { console.error("Drag:", err); }
        });
        // Rechtsklik verwijdert het punt
        marker.on("contextmenu", e => {
          L.DomEvent.stopPropagation(e);
          try {
            const nieuw = puntenRef.current.filter((_,i) => i !== idx);
            puntenRef.current = nieuw;
            setControlePunten(nieuw);
            tekenLijn(L, kaart, nieuw);
            maakMarkers(L, kaart, nieuw);
          } catch (err) { console.error("Verwijder punt:", err); }
        });
        markersRef.current.push(marker);
      });
    } catch (err) { console.error("maakMarkers:", err); }
  }

  // ── Tekenmodus aan/uit ──────────────────────────────────────────
  function startTekenen() {
    const L = window.L; const kaart = kaartRef.current;
    if (!kaart) return;
    // Wis bestaand indien aanwezig
    if (polyRef.current) { try { kaart.removeLayer(polyRef.current); } catch {} polyRef.current = null; }
    markersRef.current.forEach(m => { try { kaart.removeLayer(m); } catch {} });
    markersRef.current = [];
    setControlePunten([]);
    puntenRef.current = [];
    setTekenModus(true);
    tekenRef.current = true;
    kaart.getContainer().style.cursor = "crosshair";
  }

  function stopTekenen() {
    setTekenModus(false);
    tekenRef.current = false;
    if (kaartRef.current) kaartRef.current.getContainer().style.cursor = "";
  }

  // ── Opslaan ─────────────────────────────────────────────────────
  async function slaOp() {
    const pts = controlePunten.length >= 2 ? controlePunten : null;
    if (!pts) return;
    setOpslaat(true);
    try {
      const geojson = { type:"LineString", coordinates: pts.map(([lat,lng]) => [lng,lat]) };
      await onTraceOpgeslagen(geojson);
      stopTekenen();
      setOpgeslagen(true);
      setTimeout(() => setOpgeslagen(false), 2500);
    } catch (err) {
      console.error("Opslaan:", err);
      alert("Opslaan mislukt: " + (err?.message ?? "onbekende fout"));
    } finally {
      setOpslaat(false);
    }
  }

  // ── Verwijderen ──────────────────────────────────────────────────
  async function verwijder() {
    if (!confirm("Boorlijn definitief verwijderen?")) return;
    setVerwijdert(true);
    try {
      // Kaart leeg maken
      if (polyRef.current && kaartRef.current) { kaartRef.current.removeLayer(polyRef.current); polyRef.current = null; }
      markersRef.current.forEach(m => { try { kaartRef.current?.removeLayer(m); } catch {} });
      markersRef.current = [];
      setControlePunten([]);
      puntenRef.current = [];
      stopTekenen();
      await onTraceOpgeslagen(null);
    } catch (err) {
      console.error("Verwijderen:", err);
      alert("Verwijderen mislukt: " + (err?.message ?? "onbekende fout"));
    } finally {
      setVerwijdert(false);
    }
  }

  // ── KLIC achtergrond ─────────────────────────────────────────────
  async function laadKlicAchtergrond(L, kaart, project, s3, laadJSZip, setLaden, setBericht, setLagen, lagenRef) {
    setLaden(true); setBericht("KLIC laden…");
    // Verwijder oude lagen
    lagenRef.current.forEach(l => { try { kaart.removeLayer(l); } catch {} });
    lagenRef.current = [];

    const bestanden = (() => { try { return JSON.parse(project?.bestanden_meta||"[]"); } catch { return []; } })();
    const box = s3.__kaartBox ?? null;
    const lagenInfo = [];

    for (const b of bestanden) {
      if (!b.url) continue;
      const ext = b.naam.split(".").pop().toLowerCase();
      try {
        if (ext === "zip") {
          // Probeer eerst sessionStorage cache
          const cache = sessionStorage.getItem(`klic_parsed_${b.id}`);
          if (cache) {
            const { lagen } = JSON.parse(cache);
            for (const [thema, geoJson] of Object.entries(lagen ?? {})) {
              const lagId = `klic_${thema}`;
              const li = s3[lagId] ?? {};
              if (li.zichtbaar === false) continue;
              const kleur = li.kleur ?? KLIC_KLEUR[thema] ?? "#888";
              const dikte = li.dikte ?? 2;
              const h = li.helderheid ?? 0.75;
              const dash = KLIC_DASH[thema] ?? null;
              lagenInfo.push({ label: thema, kleur, aan: true });
              for (const feat of (geoJson?.features ?? [])) {
                const latlngs = (feat.geometry?.coordinates ?? []).map(([x,y]) => rdNaarLatLng(L,x,y));
                if (latlngs.length < 2) continue;
                const coords = latlngs.map(ll => [ll.lat, ll.lng]);
                const segs = box ? clipLijnen(coords, box) : [coords];
                segs.forEach(seg => {
                  if (seg.length < 2) return;
                  const laag = L.polyline(seg, { color:kleur, weight:dikte, opacity:h*0.85, interactive:false, zIndexOffset:-500, ...(dash?{dashArray:dash}:{}) });
                  laag.addTo(kaart);
                  lagenRef.current.push(laag);
                });
              }
            }
            setBericht(`✓ ${lagenRef.current.length} KLIC-objecten`);
            setLagen(lagenInfo);
            setLaden(false);
            continue;
          }

          // Geen cache: download ZIP
          setBericht("ZIP downloaden…");
          const JSZip = await laadJSZip();
          const res = await fetch(b.url); if (!res.ok) continue;
          const zip = await JSZip.loadAsync(await res.blob());
          const xmlNaam = Object.keys(zip.files).find(n => n.includes("GI_gebiedsinformatie") && n.endsWith(".xml"));
          if (!xmlNaam) continue;
          setBericht("IMKL parsen…");
          const xmlTekst = await zip.files[xmlNaam].async("string");
          const doc = new DOMParser().parseFromString(xmlTekst, "text/xml");
          const netThema = {};
          doc.querySelectorAll("Utiliteitsnet").forEach(net => {
            const id = net.getAttributeNS?.("http://www.opengis.net/gml/3.2","id") || net.getAttribute("gml:id") || "";
            const href = net.querySelector("thema")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href") || "";
            if (id) netThema[id] = href.split("/").pop() || "overig";
          });
          let cnt = 0;
          doc.querySelectorAll("UtilityLink").forEach(link => {
            const netHref = (link.querySelector("inNetwork")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href")||"").replace(/^#/,"");
            const thema = netThema[netHref] || "overig";
            const lagId = `klic_${thema}`;
            const li = s3[lagId] ?? {};
            if (li.zichtbaar === false) return;
            const kleur = li.kleur ?? KLIC_KLEUR[thema] ?? "#888";
            const dikte = li.dikte ?? 2;
            const h = li.helderheid ?? 0.75;
            const dash = KLIC_DASH[thema] ?? null;
            const posEl = link.querySelector("posList"); if (!posEl) return;
            const nums = posEl.textContent.trim().split(/\s+/).map(Number);
            const coords = [];
            for (let i = 0; i+1 < nums.length; i+=2) {
              const ll = rdNaarLatLng(L, nums[i], nums[i+1]);
              coords.push([ll.lat, ll.lng]);
            }
            if (coords.length < 2) return;
            const segs = box ? clipLijnen(coords, box) : [coords];
            segs.forEach(seg => {
              if (seg.length < 2) return;
              const laag = L.polyline(seg, { color:kleur, weight:dikte, opacity:h*0.85, interactive:false, zIndexOffset:-500, ...(dash?{dashArray:dash}:{}) });
              laag.addTo(kaart); lagenRef.current.push(laag); cnt++;
            });
          });
          setBericht(`✓ ${cnt} KLIC-objecten geladen`);

        } else if (ext === "dxf") {
          const li = s3[b.id] ?? {};
          if (li.zichtbaar === false) continue;
          const kleur = li.kleur ?? "#888"; const dikte = li.dikte ?? 2; const h = li.helderheid ?? 0.75;
          const tekst = await (await fetch(b.url)).text();
          const regels = tekst.split("\n"); let i = 0;
          while (i < regels.length) {
            if (regels[i].trim() !== "0") { i++; continue; }
            const type = regels[i+1]?.trim(); let einde = i+2;
            while (einde < regels.length && regels[einde].trim() !== "0") einde++;
            const blok = regels.slice(i, einde);
            const gW = c => { for(let k=0;k<blok.length-1;k++) if(blok[k].trim()===String(c)) return parseFloat(blok[k+1].trim())||0; return 0; };
            if (type==="LINE"||type==="LWPOLYLINE") {
              const pts = type==="LINE"?[[gW(10),gW(20)],[gW(11),gW(21)]]:[];
              if(type==="LWPOLYLINE") for(let k=0;k<blok.length-3;k++) if(blok[k].trim()==="10"&&blok[k+2]?.trim()==="20") pts.push([parseFloat(blok[k+1].trim()),parseFloat(blok[k+3].trim())]);
              if(pts.length>=2){
                const coords=pts.map(([x,y])=>{const ll=rdNaarLatLng(L,x,y);return[ll.lat,ll.lng];});
                const segs=box?clipLijnen(coords,box):[coords];
                segs.forEach(seg=>{if(seg.length<2)return;const laag=L.polyline(seg,{color:kleur,weight:dikte,opacity:h*0.85,interactive:false,zIndexOffset:-500});laag.addTo(kaart);lagenRef.current.push(laag);});
              }
            }
            i = einde;
          }
        }
      } catch (err) { console.warn("KLIC bestand:", b.naam, err.message); }
    }

    setLagen(lagenInfo);
    setBericht(lagenRef.current.length > 0 ? `✓ ${lagenRef.current.length} objecten` : "Geen KLIC data");
    setLaden(false);
  }

  // ─── Render ──────────────────────────────────────────────────────
  const totaalM = actievePunten.length >= 2
    ? actievePunten.reduce((s,_,i) => i===0 ? 0 : s + afstandM(actievePunten[i-1], actievePunten[i]), 0)
    : 0;

  return (
    <div className="flex gap-4" style={{ height:"calc(100vh - 168px)", minHeight:480 }}>

      {/* ── Linkerpaneel ──────────────────────────────────────────── */}
      <div className="flex-shrink-0 bg-white border border-gray-200 rounded-xl flex flex-col overflow-hidden" style={{ width:280 }}>

        {/* Header + opslaan */}
        <div className="flex items-center gap-2 px-3 py-2.5 border-b border-gray-100">
          <span className="text-sm font-semibold text-gray-800 flex-1">Boorlijn tekenen</span>
          {/* Opslaan knop — altijd zichtbaar wanneer er een tracé is */}
          {controlePunten.length >= 2 && (
            <button onClick={slaOp} disabled={opslaat}
              className={`flex items-center gap-1.5 px-3 py-1 text-xs font-medium rounded-lg transition-colors ${opgeslagen ? "bg-green-500 text-white" : "bg-orange-500 text-white hover:bg-orange-600 disabled:opacity-50"}`}>
              {opgeslagen ? (
                <><svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round"><polyline points="20 6 9 17 4 12"/></svg>Opgeslagen</>
              ) : (
                <><svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/></svg>{opslaat ? "Opslaan…" : "Opslaan"}</>
              )}
            </button>
          )}
          <button onClick={() => setLegendaOpen(o => !o)} className="text-xs text-gray-400 hover:text-gray-600 w-6 text-center flex-shrink-0">
            {legendaOpen ? "▲" : "▼"}
          </button>
        </div>

        {/* Acties */}
        <div className="px-4 py-3 border-b border-gray-100 space-y-2">
          {/* Tekenmodus status */}
          {tekenModus && (
            <div className="flex items-center gap-2 text-xs text-blue-600 bg-blue-50 rounded-lg px-3 py-2 font-medium">
              <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="12" r="10"/></svg>
              {controlePunten.length === 0 ? "Klik voor het startpunt" : `${controlePunten.length} punt${controlePunten.length!==1?"en":""} — klik door`}
            </div>
          )}

          {!tekenModus ? (
            <button onClick={startTekenen}
              className="w-full flex items-center justify-center gap-2 px-3 py-2 text-sm font-medium bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><path d="M12 5l0 14M5 12l14 0"/></svg>
              {heeftTrace ? "Nieuwe boorlijn tekenen" : "Boorlijn tekenen"}
            </button>
          ) : (
            <button onClick={() => {
              stopTekenen();
              const L = window.L; const kaart = kaartRef.current;
              if (L && kaart && bestaandTrace.length >= 2) {
                const pts = [...bestaandTrace];
                setControlePunten(pts);
                puntenRef.current = pts;
                tekenLijn(L, kaart, pts);
                maakMarkers(L, kaart, pts);
              } else {
                if (polyRef.current && kaart) { kaart.removeLayer(polyRef.current); polyRef.current = null; }
                markersRef.current.forEach(m => { try { kaart?.removeLayer(m); } catch {} });
                markersRef.current = [];
                setControlePunten([]);
                puntenRef.current = [];
              }
            }} className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg text-gray-500 hover:bg-gray-50 transition-colors">
              ✓ Klaar met tekenen
            </button>
          )}

          {heeftTrace && (
            <div className="flex gap-2">
              {!tekenModus && (
                <button onClick={() => {
                  const L = window.L; const kaart = kaartRef.current; if (!L || !kaart) return;
                  if (polyRef.current) { kaart.removeLayer(polyRef.current); polyRef.current = null; }
                  markersRef.current.forEach(m => { try { kaart.removeLayer(m); } catch {} });
                  markersRef.current = [];
                  const pts = [...bestaandTrace];
                  setControlePunten(pts);
                  puntenRef.current = pts;
                  tekenLijn(L, kaart, pts);
                  maakMarkers(L, kaart, pts);
                  setTekenModus(true);
                  tekenRef.current = true;
                  kaart.getContainer().style.cursor = "crosshair";
                }} className="flex-1 py-1.5 text-xs font-medium border border-blue-200 text-blue-600 rounded-lg hover:bg-blue-50 transition-colors">
                  ✏️ Bewerken
                </button>
              )}
              <button onClick={verwijder} disabled={verwijdert}
                className="flex-1 py-1.5 text-xs font-medium border border-red-200 text-red-500 rounded-lg hover:bg-red-50 disabled:opacity-50 transition-colors">
                {verwijdert ? "⏳ Bezig…" : "🗑 Verwijderen"}
              </button>
            </div>
          )}
        </div>

        {/* Boorlijn info */}
        {heeftTrace && (
          <div className="px-4 py-3 border-b border-gray-100">
            <div className="bg-blue-50 rounded-lg px-3 py-2 space-y-1">
              <div className="flex items-center justify-between text-xs">
                <span className="text-blue-600 font-medium">✓ Boorlijn</span>
                <span className="text-blue-500 font-mono">{Math.round(totaalM)} m</span>
              </div>
              <div className="text-xs text-blue-400">{actievePunten.length} punten · sleep om aan te passen · rechtsklik = verwijder</div>
            </div>
          </div>
        )}

        {/* Coördinaten tabel */}
        {actievePunten.length >= 1 && (
          <div className="px-4 py-3 border-b border-gray-100">
            <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
              Punten — RD New coördinaten
            </div>
            <div className="overflow-auto max-h-48 rounded-lg border border-gray-100">
              <table className="w-full text-xs border-collapse">
                <thead>
                  <tr className="bg-gray-50 sticky top-0">
                    <th className="px-2 py-1.5 text-left text-gray-500 font-medium border-b border-gray-100 w-7">#</th>
                    <th className="px-2 py-1.5 text-right text-gray-500 font-medium border-b border-gray-100">X (m)</th>
                    <th className="px-2 py-1.5 text-right text-gray-500 font-medium border-b border-gray-100">Y (m)</th>
                  </tr>
                </thead>
                <tbody>
                  {actievePunten.map(([lat, lng], i) => {
                    const [rdX, rdY] = wgs84NaarRd(lng, lat);
                    const isEerste = i === 0;
                    const isLaatste = i === actievePunten.length - 1;
                    return (
                      <tr key={i} className={`border-b border-gray-50 ${isEerste ? "bg-green-50" : isLaatste ? "bg-red-50" : "hover:bg-gray-50"}`}>
                        <td className="px-2 py-1 font-bold" style={{ color: isEerste ? "#15803d" : isLaatste ? "#dc2626" : "#2563eb" }}>{i+1}</td>
                        <td className="px-2 py-1 text-right font-mono text-gray-700">{rdX.toLocaleString("nl-NL")}</td>
                        <td className="px-2 py-1 text-right font-mono text-gray-700">{rdY.toLocaleString("nl-NL")}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {actievePunten.length >= 2 && (
              <div className="mt-1.5 text-xs text-gray-400 text-right">
                Totaal: {Math.round(totaalM)} m · {actievePunten.length} punten
              </div>
            )}
          </div>
        )}

        {/* Lagen paneel */}
        {legendaOpen && (
          <div className="flex-1 overflow-y-auto">

            {/* Achtergrond */}
            <div className="px-4 py-3 border-b border-gray-100">
              <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">Achtergrond</div>
              <div className="space-y-1">
                {ACHTERGRONDEN.map(a => (
                  <button key={a.id} onClick={() => wisselAchtergrond(a.id)}
                    className={`flex items-center gap-2 w-full px-2 py-1.5 rounded-lg text-left transition-colors ${actieveAchtergrond===a.id ? "bg-orange-50 text-orange-700" : "text-gray-600 hover:bg-gray-50"}`}>
                    <div className={`w-3 h-3 rounded-full border-2 flex-shrink-0 ${actieveAchtergrond===a.id ? "border-orange-500 bg-orange-500" : "border-gray-300"}`}/>
                    <span className="text-xs font-medium">{a.label}</span>
                  </button>
                ))}
              </div>
            </div>

            {/* Overlays */}
            <div className="px-4 py-3 border-b border-gray-100">
              <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">Overlays</div>
              <div className="space-y-1">
                {OVERLAYS.map(o => {
                  const aan = actieveOverlays.includes(o.id);
                  return (
                    <button key={o.id} onClick={() => toggleOverlay(o.id)}
                      className={`flex items-center gap-2 w-full px-2 py-1.5 rounded-lg text-left transition-colors ${aan ? "bg-blue-50" : "hover:bg-gray-50"}`}>
                      <div className="w-3 h-3 rounded flex-shrink-0 border border-gray-200 transition-colors"
                        style={{ background: aan ? o.kleur : "transparent" }}/>
                      <span className={`text-xs font-medium ${aan ? "text-blue-700" : "text-gray-600"}`}>{o.label}</span>
                      {aan && <span className="ml-auto text-xs text-blue-400">aan</span>}
                    </button>
                  );
                })}
              </div>
            </div>

            {/* KLIC achtergrond */}
            <div className="px-4 py-3">
              <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">KLIC achtergrond</div>
              {klicLagen.length === 0 ? (
                <p className="text-xs text-gray-400 italic">
                  {isLaden ? laadBericht : "Geen KLIC bestanden — upload in stap 2."}
                </p>
              ) : (
                <div className="space-y-1">
                  {klicLagen.map((l, i) => (
                    <div key={i} className="flex items-center gap-2 text-xs text-gray-600">
                      <div className="w-3 h-3 rounded-full flex-shrink-0" style={{ background:l.kleur }}/>
                      <span className="capitalize">{l.label}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}

        {/* Footer */}
        <div className="border-t border-gray-100 px-4 py-2">
          {isLaden ? (
            <div className="flex items-center gap-2 text-xs text-orange-500">
              <div className="w-3 h-3 border-2 border-orange-200 border-t-orange-500 rounded-full animate-spin flex-shrink-0" />
              <span>{laadBericht}</span>
            </div>
          ) : (
            <p className="text-xs text-gray-400">Instellingen uit stap 3 · {s3.__kaartBox ? "Filterbox actief" : "Volledig gebied"}</p>
          )}
        </div>
      </div>

      {/* ── Kaart ────────────────────────────────────────────────── */}
      <div className="flex-1 relative min-w-0">
        <div ref={mapRef} className="w-full h-full rounded-xl border border-gray-200 overflow-hidden shadow-sm" />

        {/* Laadspinner over kaart */}
        {isLaden && (
          <div className="absolute inset-0 bg-white/60 flex flex-col items-center justify-center rounded-xl pointer-events-none z-[400]">
            <div className="w-10 h-10 border-4 border-orange-200 border-t-orange-500 rounded-full animate-spin mb-2" />
            <p className="text-sm text-gray-600 font-medium">{laadBericht}</p>
          </div>
        )}

        {/* Hint tijdens tekenen */}
        {tekenModus && (
          <div className="absolute top-3 left-1/2 -translate-x-1/2 z-[400] bg-blue-600 text-white text-xs font-medium px-4 py-2 rounded-xl shadow pointer-events-none">
            {controlePunten.length === 0 ? "Klik voor het startpunt" : `${controlePunten.length} punt${controlePunten.length!==1?"en":""} — klik door om lijn te tekenen`}
          </div>
        )}
      </div>
    </div>
  );
}
