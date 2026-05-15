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

// ─── Afstand (Haversine) ─────────────────────────────────────────
function afstandM([lat1,lng1],[lat2,lng2]) {
  const R=6371000, dLat=(lat2-lat1)*Math.PI/180, dLng=(lng2-lng1)*Math.PI/180;
  const a=Math.sin(dLat/2)**2+Math.cos(lat1*Math.PI/180)*Math.cos(lat2*Math.PI/180)*Math.sin(dLng/2)**2;
  return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
}

// ════════════════════════════════════════════════════════════════
export default function MapTrace({ project, onTraceOpgeslagen }) {
  const mapRef     = useRef(null);
  const kaartRef   = useRef(null);
  const polyRef    = useRef(null);   // blauwe boorlijn
  const markersRef = useRef([]);     // sleepbare punten
  const klicRef    = useRef([]);     // KLIC achtergrond lagen
  const boxRef     = useRef(null);   // filterbox rechthoek
  const tekenRef   = useRef(false);  // mirror van tekenModus voor Leaflet handlers
  const puntenRef  = useRef([]);     // mirror van controlePunten voor handlers

  const [controlePunten, setControlePunten] = useState([]);
  const [tekenModus,     setTekenModus]     = useState(false);
  const [opgeslagen,     setOpgeslagen]     = useState(false);
  const [opslaat,        setOpslaat]        = useState(false);
  const [verwijdert,     setVerwijdert]     = useState(false);
  const [isLaden,        setIsLaden]        = useState(false);
  const [laadBericht,    setLaadBericht]    = useState("");
  const [legendaOpen,    setLegendaOpen]    = useState(true);
  const [klicLagen,      setKlicLagen]      = useState([]); // [{label,kleur,aan}]

  // Lees stap-3 instellingen
  const s3 = (() => { try { return JSON.parse(project?.laag_instellingen||"{}"); } catch { return {}; } })();

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

  // Houd puntenRef gesynchroniseerd
  puntenRef.current = actievePunten;
  tekenRef.current  = tekenModus;

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
    const laadProj4 = () => new Promise(ok => {
      if (window.proj4) return ok();
      const s = document.createElement("script");
      s.src = "https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js";
      s.onload = ok; s.onerror = ok;
      document.head.appendChild(s);
    });

    const laadLeaflet = () => new Promise((ok, err) => {
      if (window.L) return ok(window.L);
      const s = document.createElement("script");
      s.src = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
      s.onload = () => ok(window.L);
      s.onerror = err;
      document.head.appendChild(s);
    });

    const laadJSZip = () => new Promise((ok, err) => {
      if (window.JSZip) return ok(window.JSZip);
      const s = document.createElement("script");
      s.src = "https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";
      s.onload = () => ok(window.JSZip);
      s.onerror = err;
      document.head.appendChild(s);
    });

    (async () => {
      const [L] = await Promise.all([laadLeaflet(), laadProj4()]);
      if (!actief || !mapRef.current) return;

      // Startpositie vanuit stap 3
      const pos    = s3.__kaartPositie;
      const center = pos ? [pos.lat, pos.lng] : [52.156, 5.387];
      const zoom   = pos?.zoom ?? 13;

      const kaart = L.map(mapRef.current, { center, zoom, maxZoom:22, zoomControl:true });
      kaartRef.current = kaart;

      // Achtergrond (zelfde als stap 3)
      const brtUrls = {
        brt_standaard: "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:3857/{z}/{x}/{y}.png",
        brt_grijs:     "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:3857/{z}/{x}/{y}.png",
        brt_pastel:    "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/pastel/EPSG:3857/{z}/{x}/{y}.png",
      };
      const achtergrond = s3.__achtergrond ?? "brt_standaard";
      if (achtergrond === "luchtfoto") {
        L.tileLayer.wms("https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",
          { layers:"Actueel_ortho25", format:"image/jpeg", transparent:false, maxZoom:22, attribution:"© PDOK" }
        ).addTo(kaart);
      } else {
        L.tileLayer(brtUrls[achtergrond] ?? brtUrls.brt_standaard,
          { maxZoom:22, attribution:"© PDOK BRT" }
        ).addTo(kaart);
      }

      // PDOK overlays (zelfde als stap 3)
      const overlayWMS = {
        kadaster:   { url:"https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0", layers:"Perceel" },
        bag_panden: { url:"https://service.pdok.nl/lv/bag/wms/v2_0", layers:"pand" },
        bag_adres:  { url:"https://service.pdok.nl/lv/bag/wms/v2_0", layers:"ligplaats,standplaats,verblijfsobject" },
        bgt:        { url:"https://service.pdok.nl/lv/bgt/wms/v1_0", layers:"wegdeel,waterdeel,ondersteunendwegdeel,begroeidterreindeel" },
      };
      (s3.__overlays ?? []).forEach(id => {
        const c = overlayWMS[id]; if (!c) return;
        L.tileLayer.wms(c.url, { layers:c.layers, format:"image/png", transparent:true, opacity:0.7, zIndex:200, attribution:"© PDOK" }).addTo(kaart);
      });

      // Filterbox tonen (grijs, geen vulling)
      const box = s3.__kaartBox ?? null;
      if (box) {
        boxRef.current = L.rectangle([[box.lat1,box.lng1],[box.lat2,box.lng2]], {
          color:"#6b7280", weight:2, fillOpacity:0, interactive:false,
        }).addTo(kaart);
      }

      // Bestaand tracé tekenen
      if (bestaandTrace.length >= 2) {
        tekenLijn(L, kaart, bestaandTrace);
        maakMarkers(L, kaart, bestaandTrace);
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

  // ── Tekenen: lijn en markers ────────────────────────────────────
  function tekenLijn(L, kaart, pts) {
    try {
      if (polyRef.current) { kaart.removeLayer(polyRef.current); polyRef.current = null; }
      if (pts.length < 2) return;
      const lijn = L.polyline(pts, { color:"#2563eb", weight:5, opacity:0.9, zIndex:500 }).addTo(kaart);
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
          setControlePunten(prev => {
            const nieuw = [...prev.slice(0, besteIdx+1), [lat,lng], ...prev.slice(besteIdx+1)];
            puntenRef.current = nieuw;
            tekenLijn(L, kaart, nieuw);
            maakMarkers(L, kaart, nieuw);
            return nieuw;
          });
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
            setControlePunten(prev => {
              const nieuw = [...prev]; nieuw[idx] = [nLat, nLng];
              puntenRef.current = nieuw;
              tekenLijn(L, kaart, nieuw);
              return nieuw;
            });
          } catch (err) { console.error("Drag:", err); }
        });
        // Rechtsklik verwijdert het punt
        marker.on("contextmenu", e => {
          L.DomEvent.stopPropagation(e);
          try {
            setControlePunten(prev => {
              const nieuw = prev.filter((_,i) => i !== idx);
              puntenRef.current = nieuw;
              tekenLijn(L, kaart, nieuw);
              maakMarkers(L, kaart, nieuw);
              return nieuw;
            });
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
                const coords = (feat.geometry?.coordinates ?? []).map(([x,y]) => { const[lng,lat]=rdNaarWgs84(x,y); return[lat,lng]; });
                if (coords.length < 2) continue;
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
            for (let i = 0; i+1 < nums.length; i+=2) { const[lng,lat]=rdNaarWgs84(nums[i],nums[i+1]); coords.push([lat,lng]); }
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
              if(pts.length>=2){const coords=pts.map(([x,y])=>{const[lng,lat]=rdNaarWgs84(x,y);return[lat,lng];});const segs=box?clipLijnen(coords,box):[coords];segs.forEach(seg=>{if(seg.length<2)return;const laag=L.polyline(seg,{color:kleur,weight:dikte,opacity:h*0.85,interactive:false,zIndexOffset:-500});laag.addTo(kaart);lagenRef.current.push(laag);});}
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

        {/* Header */}
        <div className="px-4 py-3 border-b border-gray-100 flex items-center justify-between">
          <span className="text-sm font-semibold text-gray-800">Boorlijn tekenen</span>
          <button onClick={() => setLegendaOpen(o => !o)} className="text-xs text-gray-400 hover:text-gray-600 px-1">
            {legendaOpen ? "▲" : "▼"}
          </button>
        </div>

        {/* Acties */}
        <div className="px-4 py-3 border-b border-gray-100 space-y-2">
          {!tekenModus ? (
            <button onClick={startTekenen}
              className="w-full flex items-center justify-center gap-2 px-3 py-2 text-sm font-medium bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><path d="M12 5l0 14M5 12l14 0"/></svg>
              {heeftTrace ? "Nieuwe boorlijn tekenen" : "Boorlijn tekenen"}
            </button>
          ) : (
            <div className="space-y-1.5">
              <div className="flex items-center gap-2 text-xs text-blue-600 bg-blue-50 rounded-lg px-3 py-2 font-medium">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="12" r="10"/></svg>
                Klik op kaart om punten te zetten
              </div>
              <div className="flex gap-2">
                {controlePunten.length >= 2 && (
                  <button onClick={slaOp} disabled={opslaat}
                    className="flex-1 py-1.5 text-xs font-semibold bg-orange-500 text-white rounded-lg hover:bg-orange-600 disabled:opacity-50 transition-colors">
                    {opgeslagen ? "✓ Opgeslagen" : opslaat ? "Opslaan…" : "💾 Opslaan"}
                  </button>
                )}
                <button onClick={() => {
                  stopTekenen();
                  // Herstel opgeslagen tracé als aanwezig
                  const L = window.L; const kaart = kaartRef.current;
                  if (L && kaart && bestaandTrace.length >= 2) {
                    tekenLijn(L, kaart, bestaandTrace);
                    maakMarkers(L, kaart, bestaandTrace);
                  } else {
                    if (polyRef.current && kaart) { kaart.removeLayer(polyRef.current); polyRef.current = null; }
                    markersRef.current.forEach(m => { try { kaart?.removeLayer(m); } catch {} });
                    markersRef.current = [];
                  }
                  setControlePunten([]);
                  puntenRef.current = [];
                }} className="px-3 py-1.5 text-xs border border-gray-200 rounded-lg text-gray-500 hover:bg-gray-50 transition-colors">
                  Annuleren
                </button>
              </div>
            </div>
          )}

          {heeftTrace && !tekenModus && (
            <div className="flex gap-2">
              <button onClick={() => {
                // Bewerken: laad bestaand als controlepunten
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

        {/* KLIC lagen legenda */}
        {legendaOpen && (
          <div className="flex-1 overflow-y-auto px-4 py-3 space-y-2">
            <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide">KLIC achtergrond</div>
            {klicLagen.length === 0 && (
              <p className="text-xs text-gray-400 italic">
                {isLaden ? laadBericht : "Geen KLIC bestanden — upload in stap 2."}
              </p>
            )}
            {klicLagen.map((l, i) => (
              <div key={i} className="flex items-center gap-2 text-xs text-gray-600">
                <div className="w-3 h-3 rounded-full flex-shrink-0" style={{ background:l.kleur }} />
                <span className="capitalize">{l.label}</span>
              </div>
            ))}
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
