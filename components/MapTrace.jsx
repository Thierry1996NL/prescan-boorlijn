"use client";

import { useEffect, useRef, useState } from "react";


// ─── KLIC ACHTERGROND (stap 3 → stap 4) ─────────────────────────
const KLIC_THEMA_KLEUR = {
  laagspanning:"#7B00AA",middenspanning:"#00CCFF",hoogspanning:"#FF4400",
  gasLageDruk:"#FFFF00",gasHogeDruk:"#FF0000",water:"#000080",
  datatransport:"#00CC00",rioolVrijverval:"#AA00CC",rioolOnderOverOfOnderdruk:"#AA00CC",
  warmte:"#FF6600",overig:"#888888",mantelbuis:"#4B5563",kabelbed:"#111827",duct:"#374151",
};


// Liang-Barsky lijnclipping
function liangBarskyKlic(x1,y1,x2,y2,xMin,xMax,yMin,yMax){
  const dx=x2-x1,dy=y2-y1;
  const p=[-dx,dx,-dy,dy],q=[x1-xMin,xMax-x1,y1-yMin,yMax-y1];
  let t0=0,t1=1;
  for(let i=0;i<4;i++){
    if(p[i]===0){if(q[i]<0)return null;}
    else{const t=q[i]/p[i];if(p[i]<0)t0=Math.max(t0,t);else t1=Math.min(t1,t);}
  }
  return t0>t1?null:[x1+t0*dx,y1+t0*dy,x1+t1*dx,y1+t1*dy];
}

function clipLijnsegmenten(coords, box){
  // coords = [[lat,lng], ...], box = {lat1,lat2,lng1,lng2}
  const{lat1:yMin,lat2:yMax,lng1:xMin,lng2:xMax}=box;
  const segmenten=[];let huidig=null;
  for(let i=0;i<coords.length-1;i++){
    const[y1,x1]=coords[i],[y2,x2]=coords[i+1];
    const c=liangBarskyKlic(x1,y1,x2,y2,xMin,xMax,yMin,yMax);
    if(!c){if(huidig){segmenten.push(huidig);huidig=null;}continue;}
    const[cx1,cy1,cx2,cy2]=c;
    if(!huidig){huidig=[[cy1,cx1],[cy2,cx2]];}
    else{
      const last=huidig[huidig.length-1];
      if(Math.abs(last[0]-cy1)<1e-9&&Math.abs(last[1]-cx1)<1e-9)huidig.push([cy2,cx2]);
      else{segmenten.push(huidig);huidig=[[cy1,cx1],[cy2,cx2]];}
    }
  }
  if(huidig)segmenten.push(huidig);
  return segmenten;
}

function rdNaarWgs84Klic(x, y) {
  if (Math.abs(x) <= 180 && Math.abs(y) <= 90) return [x, y];
  if (typeof window !== "undefined" && window.proj4) {
    try { const w = proj4("EPSG:28992","EPSG:4326",[x,y]); return [w[0],w[1]]; } catch {}
  }
  const dX=(x-155000)/100000, dY=(y-463000)/100000;
  const sumN=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
  const sumE=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
  return[5.38720621+sumE/3600,52.15517440+sumN/3600];
}

async function laadKlicAchtergrondOpKaart(kaart, project, klicLagenRef) {
  if (!kaart || typeof window === "undefined") return;
  (klicLagenRef.current || []).forEach(l => { try { kaart.removeLayer(l); } catch {} });
  klicLagenRef.current = [];
  const bestanden = (() => { try { return JSON.parse(project?.bestanden_meta || "[]"); } catch { return []; } })();
  const inst = (() => { try { return JSON.parse(project?.laag_instellingen || "{}"); } catch { return {}; } })();
  const kaartBox = inst.__kaartBox ?? null;
  if (bestanden.length === 0) return;

  // Laad JSZip als nodig
  if (!window.JSZip) {
    await new Promise((ok, err) => {
      const s = document.createElement("script");
      s.src = "https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";
      s.onload = ok; s.onerror = err;
      document.head.appendChild(s);
    });
  }

  for (const b of bestanden) {
    if (!b.url) continue;
    const ext = b.naam.split(".").pop().toLowerCase();
    try {
      const res = await fetch(b.url); if (!res.ok) continue;

      if (ext === "zip") {
        const blob = await res.blob();
        const zip = await window.JSZip.loadAsync(blob);
        const xmlNaam = Object.keys(zip.files).find(n => n.includes("GI_gebiedsinformatie") && n.endsWith(".xml"));
        if (!xmlNaam) continue;
        const xmlTekst = await zip.files[xmlNaam].async("string");
        const doc = new DOMParser().parseFromString(xmlTekst, "text/xml");
        const netThema = {};
        doc.querySelectorAll("Utiliteitsnet").forEach(net => {
          const id = net.getAttributeNS?.("http://www.opengis.net/gml/3.2","id") || net.getAttribute("gml:id") || "";
          const href = net.querySelector("thema")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href") || "";
          if (id) netThema[id] = href.split("/").pop() || "overig";
        });
        doc.querySelectorAll("UtilityLink").forEach(link => {
          const netHref = (link.querySelector("inNetwork")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href") || "").replace(/^#/,"");
          const thema = netThema[netHref] || "overig";
          const lagId = `klic_${thema}`;
          const li = inst[lagId]; if (li?.zichtbaar === false) return;
          const kleur = li?.kleur ?? KLIC_THEMA_KLEUR[thema] ?? "#888";
          const dikte = li?.dikte ?? 2;
          const helderheid = li?.helderheid ?? 0.7;
          const posListEl = link.querySelector("posList"); if (!posListEl) return;
          const nums = posListEl.textContent.trim().split(/\s+/).map(Number);
          const coords = [];
          for (let i = 0; i+1 < nums.length; i+=2) {
            const [lng, lat] = rdNaarWgs84Klic(nums[i], nums[i+1]);
            coords.push([lat, lng]);
          }
          if (coords.length < 2) return;
          // Clip naar filterbox (Liang-Barsky)
          const teRenderen = kaartBox ? clipLijnsegmenten(coords, kaartBox) : [coords];
          teRenderen.forEach(seg => {
            if (seg.length < 2) return;
            const laag = window.L.polyline(seg, { color:kleur, weight:dikte, opacity:helderheid*0.8, interactive:false, zIndexOffset:-1000 });
            laag.addTo(kaart);
            klicLagenRef.current.push(laag);
          });
        });

      } else if (ext === "dxf") {
        const li = inst[b.id]; if (li?.zichtbaar === false) continue;
        const kleur = li?.kleur ?? "#888"; const dikte = li?.dikte ?? 2; const helderheid = li?.helderheid ?? 0.7;
        const tekst = await res.text();
        const regels = tekst.split(/\r?\n/); let i = 0;
        while (i < regels.length) {
          if (regels[i].trim() !== "0") { i++; continue; }
          const type = regels[i+1]?.trim(); let einde = i+2;
          while (einde < regels.length && regels[einde].trim() !== "0") einde++;
          const blok = regels.slice(i, einde);
          const getW = c => { for (let k=0;k<blok.length-1;k++) if(blok[k].trim()===String(c)) return parseFloat(blok[k+1].trim())||0; return 0; };
          if (type === "LINE" || type === "LWPOLYLINE") {
            const pts = type === "LINE" ? [[getW(10),getW(20)],[getW(11),getW(21)]] : [];
            if (type === "LWPOLYLINE") for (let k=0;k<blok.length-3;k++) if(blok[k].trim()==="10"&&blok[k+2]?.trim()==="20") pts.push([parseFloat(blok[k+1].trim()),parseFloat(blok[k+3].trim())]);
            if (pts.length >= 2) {
              const coords = pts.map(([x,y]) => { const [lng,lat] = rdNaarWgs84Klic(x,y); return [lat,lng]; });
              const teRenderenDxf = kaartBox ? clipLijnsegmenten(coords, kaartBox) : [coords];
              teRenderenDxf.forEach(seg => {
                if (seg.length < 2) return;
                const laag = window.L.polyline(seg, { color:kleur, weight:dikte, opacity:helderheid*0.8, interactive:false, zIndexOffset:-1000 });
                laag.addTo(kaart); klicLagenRef.current.push(laag);
              });
            }
          }
          i = einde;
        }
      }
    } catch (err) { console.warn("KLIC achtergrond:", b.naam, err.message); }
  }
}

// ─── PDOK LAGEN ───────────────────────────────────────────────
const LAGEN = [
  { id: "luchtfoto", label: "Luchtfoto", kleur: "#6b7280", standaardAan: false, type: "wmts", url: "https://service.pdok.nl/hwh/luchtfotorgb/wmts/v1_0/Actueel_ortho25/EPSG:3857/{z}/{x}/{y}.jpeg" },
  { id: "panden", label: "BAG Panden", kleur: "#ea580c", standaardAan: true, type: "wms", url: "https://service.pdok.nl/lv/bag/wms/v2_0", layers: "pand" },
  { id: "percelen", label: "Percelen", kleur: "#ca8a04", standaardAan: true, type: "wms", url: "https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0", layers: "Perceel" },
  { id: "waterdelen", label: "Waterdelen", kleur: "#0ea5e9", standaardAan: true, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "waterdeel" },
  { id: "kunstwerken", label: "Duikers & Kunstwerken", kleur: "#8b5cf6", standaardAan: true, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "kunstwerkdeel" },
  { id: "wegen", label: "Wegdelen", kleur: "#64748b", standaardAan: false, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "wegdeel" },
  { id: "begroeide", label: "Begroeid terrein", kleur: "#22c55e", standaardAan: false, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "begroeidterreindeel" },
  { id: "berm", label: "Berm / Ondersteunend", kleur: "#84cc16", standaardAan: false, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "ondersteunendwegdeel" },
  { id: "spoor", label: "Spoorbaandelen", kleur: "#dc2626", standaardAan: true, type: "wms", url: "https://service.pdok.nl/lv/bgt/wms/v1_0", layers: "spoor" },
  { id: "buisleidingen", label: "Buisleidingen", kleur: "#f97316", standaardAan: true, type: "wms", url: "https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0", layers: "buisleiding" },
  { id: "gemeenten", label: "Gemeentegrenzen", kleur: "#10b981", standaardAan: false, type: "wms", url: "https://service.pdok.nl/cbs/gebiedsindelingen/2024/wms/v1_0", layers: "gemeente_gegeneraliseerd" },
  { id: "ahn", label: "AHN Hoogte", kleur: "#84cc16", standaardAan: false, type: "wms", url: "https://service.pdok.nl/rws/ahn/wms/v1_0", layers: "dtm_05m" },
];

// ─── BGT OPPERVLAK ────────────────────────────────────────────
const BGT_NAAR_NL = {
  "gesloten verharding": { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣", herstel: "Hoog" },
  "open verharding": { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱", herstel: "Midden" },
  "half verhard": { label: "Half verhard", kleur: "#92400e", icoon: "🪨", herstel: "Laag" },
  "onverhard": { label: "Onverhard", kleur: "#78350f", icoon: "🌱", herstel: "Laag" },
  "gras- en kruidachtigen": { label: "Grasberm", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" },
  "groenvoorziening": { label: "Groen / Plantsoen", kleur: "#15803d", icoon: "🌳", herstel: "Laag" },
  "struiken": { label: "Struiken", kleur: "#166534", icoon: "🌿", herstel: "Laag" },
  "zand": { label: "Zand", kleur: "#d97706", icoon: "🏖", herstel: "Laag" },
  "rietland en moeras": { label: "Riet / Moeras", kleur: "#0369a1", icoon: "🌾", herstel: "Speciaal" },
};

async function haalOppervlakOp(lat, lng) {
  try {
    const res = await fetch(`/api/bgt?lat=${lat}&lng=${lng}`);
    if (!res.ok) return null;
    const data = await res.json();
    if (data.type) {
      const lc = data.type.toLowerCase();
      const vertaald = data.vertaald ?? BGT_NAAR_NL[lc] ?? { label: data.type, kleur: "#6b7280", icoon: "📍", herstel: "?" };
      return { type: data.type, vertaald };
    }
  } catch (e) {}
  return null;
}

// ─── DWARSPROFIEL ─────────────────────────────────────────────
function afstandM(p1, p2) {
  const R = 6371000;
  const dLat = (p2[0] - p1[0]) * Math.PI / 180;
  const dLng = (p2[1] - p1[1]) * Math.PI / 180;
  const a = Math.sin(dLat/2)**2 + Math.cos(p1[0]*Math.PI/180)*Math.cos(p2[0]*Math.PI/180)*Math.sin(dLng/2)**2;
  return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1-a));
}

function Dwarsprofiel({ controlePunten, analysePunten, project, onAnalysePuntVerplaatst, dieptePunten, setDieptePunten, diepteModus, setDiepteModus }) {
  const svgRef = useRef(null);
  const [dragIdx, setDragIdx] = useState(null);
  const [diepteSlepen, setDiepteSlepen] = useState(null);

  const gesorteerdeAnalyse = [...analysePunten].sort((a, b) => a.positieM - b.positieM);

  if (controlePunten.length < 2) {
    return (
      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
        <div className="px-5 py-3 border-b border-gray-100">
          <h3 className="text-sm font-semibold text-gray-900">📐 Dwarsprofiel boorlijn</h3>
        </div>
        <div className="flex flex-col items-center justify-center py-10 text-center">
          <div className="text-3xl mb-3">📐</div>
          <p className="text-sm text-gray-400 font-medium">Nog geen boorlijn getekend</p>
          <p className="text-xs text-gray-300 mt-1">Teken een boorlijn om het dwarsprofiel te zien.</p>
        </div>
      </div>
    );
  }

  const W = 900, H = 240;
  const PAD = { top: 20, right: 20, bottom: 45, left: 55 };
  const plotW = W - PAD.left - PAD.right;
  const plotH = H - PAD.top - PAD.bottom;

  let totaalM = 0;
  for (let i = 1; i < controlePunten.length; i++) totaalM += afstandM(controlePunten[i-1], controlePunten[i]);

  const minD = -6, maxD = 1, bereik = maxD - minD;
  const xPos = m => PAD.left + (Math.min(m, totaalM) / totaalM) * plotW;
  const yPos = d => PAD.top + ((maxD - d) / bereik) * plotH;
  const BAR_H = 28, BAR_Y = 8;

  // Zorg dat dieptepunten altijd start en eind hebben
  const alleDieptePunten = (() => {
    const punten = dieptePunten.map(p => ({
      ...p,
      positieM: p.id === "eind" ? totaalM : p.positieM,
    }));
    return punten.sort((a, b) => a.positieM - b.positieM);
  })();

  // Interpoleer boring diepte op positie m
  function diepteOpM(m) {
    const pts = alleDieptePunten;
    for (let i = 0; i < pts.length - 1; i++) {
      if (m >= pts[i].positieM && m <= pts[i+1].positieM) {
        const t = (m - pts[i].positieM) / (pts[i+1].positieM - pts[i].positieM);
        return pts[i].diepte + t * (pts[i+1].diepte - pts[i].diepte);
      }
    }
    return pts[pts.length-1]?.diepte ?? -1.5;
  }

  // Boring lijn path
  const boringPath = (() => {
    const stappen = 50;
    const punten = Array.from({ length: stappen + 1 }, (_, i) => {
      const m = (i / stappen) * totaalM;
      return `${xPos(m)},${yPos(diepteOpM(m))}`;
    });
    return `M ${punten.join(' L ')}`;
  })();

  // Intrede/uittrede hoek
  const inD = alleDieptePunten[0]?.diepte ?? -1.5;
  const uitD = alleDieptePunten[alleDieptePunten.length-1]?.diepte ?? -1.5;
  const inHoek = Math.abs(Math.atan2(Math.abs(inD), Math.min(totaalM * 0.2, 10)) * 180 / Math.PI).toFixed(1);
  const uitHoek = Math.abs(Math.atan2(Math.abs(uitD), Math.min(totaalM * 0.2, 10)) * 180 / Math.PI).toFixed(1);

  // Groepen voor straatwerk balk
  const groepen = [];
  if (gesorteerdeAnalyse.length > 0) {
    if (gesorteerdeAnalyse[0].positieM > 0) groepen.push({ label: "?", kleur: "#e5e7eb", icoon: "", startM: 0, eindeM: gesorteerdeAnalyse[0].positieM });
    gesorteerdeAnalyse.forEach((p, i) => {
      const volgende = gesorteerdeAnalyse[i + 1];
      groepen.push({ label: p.vertaald?.label ?? "?", kleur: p.vertaald?.kleur ?? "#9ca3af", icoon: p.vertaald?.icoon ?? "📍", startM: p.positieM, eindeM: volgende ? volgende.positieM : totaalM });
    });
  }
  const overgangen = groepen.slice(1).map((g, i) => ({ m: groepen[i].eindeM }));

  // SVG drag helpers
  function getSvgCoords(e) {
    if (!svgRef.current) return { x: 0, y: 0 };
    const rect = svgRef.current.getBoundingClientRect();
    return {
      x: (e.clientX - rect.left) * (W / rect.width),
      y: (e.clientY - rect.top) * (H / rect.height),
    };
  }

  function svgXNaarM(svgX) { return Math.max(0, Math.min(totaalM, ((svgX - PAD.left) / plotW) * totaalM)); }
  function svgYNaarD(svgY) { return Math.max(minD, Math.min(maxD, maxD - ((svgY - PAD.top) / plotH) * bereik)); }

  function handleMouseMove(e) {
    const { x, y } = getSvgCoords(e);
    if (dragIdx !== null) onAnalysePuntVerplaatst?.(dragIdx, svgXNaarM(x));
    if (diepteSlepen !== null) {
      const { idx, mode } = diepteSlepen;
      const nieuweDiepte = Math.round(Math.max(-6, Math.min(0, svgYNaarD(y))) * 10) / 10;
      const nieuweM = Math.round(Math.max(1, Math.min(totaalM - 1, svgXNaarM(x))));
      setDieptePunten(prev => prev.map((p, i) => {
        if (i !== idx) return p;
        if (mode === "y") return { ...p, diepte: nieuweDiepte };
        return { ...p, diepte: nieuweDiepte, positieM: nieuweM };
      }));
    }
  }

  function handleMouseUp() { setDragIdx(null); setDiepteSlepen(null); }

  // Klik op profiel om dieptepunt toe te voegen (alleen in diepteModus)
  function handleProfielKlik(e) {
    if (!diepteModus) return;
    if (dragIdx !== null || diepteSlepen !== null) return;
    const { x, y } = getSvgCoords(e);
    const m = Math.round(svgXNaarM(x));
    const d = Math.round(Math.max(-6, Math.min(0, svgYNaarD(y))) * 10) / 10;
    if (m > 1 && m < totaalM - 1) {
      const nieuweId = `dp-${Date.now()}`;
      setDieptePunten(prev => [
        ...prev.filter(p => Math.abs((p.id === "eind" ? totaalM : p.positieM) - m) > 2),
        { id: nieuweId, positieM: m, diepte: d, vast: false },
      ]);
    }
  }

  return (
    <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
      {/* Header */}
      <div className="flex items-center justify-between px-5 py-3 border-b border-gray-100 flex-wrap gap-2">
        <h3 className="text-sm font-semibold text-gray-900">📐 Dwarsprofiel boorlijn</h3>
        <div className="flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-3 text-xs text-gray-400">
            <span className="flex items-center gap-1"><span className="w-4 border-t-2 border-blue-600 inline-block" /> Boring {project?.materiaal ?? ""} Ø{project?.diameter_mm ?? "—"}mm</span>
            <span className="text-blue-600 font-medium">↘ Intrede {alleDieptePunten[0]?.diepte}m / {inHoek}°</span>
            <span className="text-blue-600 font-medium">↗ Uittrede {alleDieptePunten[alleDieptePunten.length-1]?.diepte}m / {uitHoek}°</span>
            <span className="text-gray-300">Totaal: {Math.round(totaalM)}m</span>
          </div>
          <button
            onClick={() => setDiepteModus(v => !v)}
            className={`flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg border transition-colors ${
              diepteModus
                ? "bg-blue-600 text-white border-blue-600"
                : "text-blue-600 border-blue-200 hover:bg-blue-50"
            }`}
          >
            {diepteModus ? "✓ Klik op profiel om punt toe te voegen" : "+ Dieptepunt toevoegen"}
          </button>
          {dieptePunten.filter(p => !p.vast).length > 0 && (
            <button
              onClick={() => setDieptePunten(prev => prev.filter(p => p.vast))}
              className="text-xs text-red-400 hover:text-red-600 px-2 py-1 rounded hover:bg-red-50 transition-colors"
            >
              Wis dieptepunten
            </button>
          )}
        </div>
      </div>

      {/* Straatwerk analyse balk */}
      {groepen.length > 0 && (
        <div className="px-5 pt-4 pb-2 border-b border-gray-100">
          <div className="text-xs font-medium text-gray-500 mb-2">Straatwerk analyse</div>
          <svg viewBox={`0 0 ${W} ${BAR_Y + BAR_H + 22}`} className="w-full" style={{ height: BAR_Y + BAR_H + 26 }}>
            {groepen.map((g, i) => {
              const x1 = xPos(g.startM), x2 = xPos(g.eindeM), breedte = Math.max(x2-x1, 2);
              return (
                <g key={i}>
                  <rect x={x1} y={BAR_Y} width={breedte} height={BAR_H} fill={g.kleur} opacity="0.85" />
                  {breedte > 40 && <text x={x1+breedte/2} y={BAR_Y+BAR_H/2+1} textAnchor="middle" fontSize="9" fill="white" fontWeight="700" dominantBaseline="middle">{g.icoon} {g.label}</text>}
                  {breedte > 25 && <text x={x1+breedte/2} y={BAR_Y+BAR_H+9} textAnchor="middle" fontSize="8" fill={g.kleur} fontWeight="500">{Math.round(g.eindeM-g.startM)}m</text>}
                </g>
              );
            })}
            <circle cx={xPos(0)} cy={BAR_Y+BAR_H/2} r="5" fill="white" stroke="#374151" strokeWidth="1.5" />
            <text x={xPos(0)} y={BAR_Y+BAR_H+9} textAnchor="middle" fontSize="8" fill="#374151" fontWeight="600">0m</text>
            <circle cx={xPos(totaalM)} cy={BAR_Y+BAR_H/2} r="5" fill="white" stroke="#374151" strokeWidth="1.5" />
            <text x={xPos(totaalM)} y={BAR_Y+BAR_H+9} textAnchor="middle" fontSize="8" fill="#374151" fontWeight="600">{Math.round(totaalM)}m</text>
            {overgangen.map((o, i) => (
              <g key={i}>
                <line x1={xPos(o.m)} y1={BAR_Y-2} x2={xPos(o.m)} y2={BAR_Y+BAR_H+2} stroke="white" strokeWidth="2" />
                <polygon points={`${xPos(o.m)},${BAR_Y-6} ${xPos(o.m)-4},${BAR_Y-2} ${xPos(o.m)+4},${BAR_Y-2}`} fill="#374151" />
                <text x={xPos(o.m)} y={BAR_Y-8} textAnchor="middle" fontSize="7.5" fill="#374151" fontWeight="600">{Math.round(o.m)}m</text>
              </g>
            ))}
            {/* Analyse bolletjes in balk — gesorteerd */}
            {gesorteerdeAnalyse.map((p, i) => {
              const x = xPos(p.positieM ?? 0);
              const kleur = p.vertaald?.kleur ?? "#9ca3af";
              return (
                <g key={i} style={{ cursor: "ew-resize" }} onMouseDown={(ev) => { ev.preventDefault(); setDragIdx(i); }}>
                  <circle cx={x} cy={BAR_Y+BAR_H/2} r="11" fill={kleur} stroke="white" strokeWidth="2.5" />
                  <text x={x} y={BAR_Y+BAR_H/2+1} textAnchor="middle" fontSize="9" fill="white" fontWeight="700" dominantBaseline="middle">{i+1}</text>
                </g>
              );
            })}
          </svg>
        </div>
      )}

      {/* Profiel SVG */}
      <div className="px-5 pt-3 pb-4"
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        style={{ cursor: dragIdx !== null ? "ew-resize" : diepteSlepen?.mode === "y" ? "ns-resize" : diepteSlepen ? "move" : "default" }}
      >
        <svg ref={svgRef} viewBox={`0 0 ${W} ${H}`} className="w-full" style={{ height: 260 }}
          onDoubleClick={handleProfielKlik}
        >
          {/* Achtergrond grond */}
          <rect x={PAD.left} y={PAD.top} width={plotW} height={plotH} fill="#f8fafc" rx="4" />
          <rect x={PAD.left} y={yPos(0)} width={plotW} height={yPos(minD)-yPos(0)} fill="#d6c5a0" opacity="0.3" />

          {/* Oppervlak stroken */}
          {groepen.map((g, i) => <rect key={i} x={xPos(g.startM)} y={yPos(0)-6} width={Math.max(xPos(g.eindeM)-xPos(g.startM),2)} height={6} fill={g.kleur} opacity="0.6" />)}

          {/* Maaiveld lijn */}
          <line x1={PAD.left} y1={yPos(0)} x2={PAD.left+plotW} y2={yPos(0)} stroke="#9ca3af" strokeWidth="1.5" strokeDasharray="4 2" />
          <text x={PAD.left-4} y={yPos(0)+4} textAnchor="end" fontSize="8" fill="#9ca3af">0m</text>

          {/* Grid */}
          {[-1,-2,-3,-4,-5].map(d => (
            <g key={d}>
              <line x1={PAD.left-4} y1={yPos(d)} x2={PAD.left} y2={yPos(d)} stroke="#d1d5db" strokeWidth="1" />
              <text x={PAD.left-6} y={yPos(d)+3} textAnchor="end" fontSize="9" fill="#9ca3af">{d}m</text>
              <line x1={PAD.left} y1={yPos(d)} x2={PAD.left+plotW} y2={yPos(d)} stroke="#f3f4f6" strokeWidth="0.5" />
            </g>
          ))}
          {[0,0.25,0.5,0.75,1].map(f => {
            const m = f*totaalM, x = xPos(m);
            return <g key={f}><line x1={x} y1={PAD.top+plotH} x2={x} y2={PAD.top+plotH+4} stroke="#d1d5db" strokeWidth="1" /><text x={x} y={PAD.top+plotH+13} textAnchor="middle" fontSize="9" fill="#9ca3af">{Math.round(m)}m</text></g>;
          })}

          {/* Boring buis */}
          <path d={`${boringPath} L ${xPos(totaalM)},${yPos(minD)} L ${PAD.left},${yPos(minD)} Z`} fill="#2563eb" opacity="0.06" />
          <path d={boringPath} fill="none" stroke="#2563eb" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />

          {/* Segmentlengtes en hoeken */}
          {alleDieptePunten.map((p, i) => {
            if (i >= alleDieptePunten.length - 1) return null;
            const q = alleDieptePunten[i + 1];
            const dM = q.positieM - p.positieM;
            const dD = q.diepte - p.diepte;
            const segLen = Math.sqrt(dM * dM + dD * dD).toFixed(1);
            const hoek = Math.abs(Math.atan2(dD, dM) * 180 / Math.PI).toFixed(1);
            const midX = (xPos(p.positieM) + xPos(q.positieM)) / 2;
            const midY = (yPos(p.diepte) + yPos(q.diepte)) / 2 - 6;
            return (
              <g key={`seg-${i}`}>
                <rect x={midX-26} y={midY-6} width={52} height={13} rx="3" fill="white" opacity="0.9" />
                <text x={midX} y={midY+4} textAnchor="middle" fontSize="8" fill="#374151" fontWeight="600">{segLen}m / {hoek}°</text>
              </g>
            );
          })}

          {/* Intredepunt 1 */}
          {(() => {
            const p = alleDieptePunten[0];
            if (!p) return null;
            return (
              <g style={{ cursor: "ns-resize" }} onMouseDown={(ev) => { ev.preventDefault(); ev.stopPropagation(); const idx = dieptePunten.findIndex(x => x.id === "start"); setDiepteSlepen({ idx, mode: "y" }); }}>
                <circle cx={xPos(0)} cy={yPos(p.diepte)} r="10" fill="#2563eb" stroke="white" strokeWidth="2.5" />
                <text x={xPos(0)} y={yPos(p.diepte)+1} textAnchor="middle" fontSize="9" fill="white" fontWeight="700" dominantBaseline="middle">1</text>
                <text x={xPos(0)+14} y={yPos(p.diepte)-8} fontSize="8" fill="#2563eb" fontWeight="700">↘ {p.diepte}m</text>
              </g>
            );
          })()}

          {/* Tussenpunten — genummerd, sleepbaar XY */}
          {alleDieptePunten.filter(p => !p.vast).map((p, fi) => {
            const nummer = fi + 2;
            const idx = dieptePunten.findIndex(x => x.id === p.id);
            const pIdx = alleDieptePunten.findIndex(x => x.id === p.id);
            const pr = alleDieptePunten[pIdx - 1];
            const nx = alleDieptePunten[pIdx + 1];
            const hoekIn = pr ? Math.abs(Math.atan2(p.diepte - pr.diepte, p.positieM - pr.positieM) * 180 / Math.PI).toFixed(1) : "0";
            const hoekUit = nx ? Math.abs(Math.atan2(nx.diepte - p.diepte, nx.positieM - p.positieM) * 180 / Math.PI).toFixed(1) : "0";
            return (
              <g key={p.id} style={{ cursor: "move" }}
                onMouseDown={(ev) => { ev.preventDefault(); ev.stopPropagation(); setDiepteSlepen({ idx, mode: "xy" }); }}
                onDoubleClick={(ev) => { ev.stopPropagation(); setDieptePunten(prev => prev.filter(x => x.id !== p.id)); }}>
                <line x1={xPos(p.positieM)} y1={PAD.top} x2={xPos(p.positieM)} y2={PAD.top+plotH} stroke="#2563eb" strokeWidth="1" strokeDasharray="4 3" opacity="0.3" />
                <circle cx={xPos(p.positieM)} cy={yPos(p.diepte)} r="10" fill="#2563eb" stroke="white" strokeWidth="2.5" />
                <text x={xPos(p.positieM)} y={yPos(p.diepte)+1} textAnchor="middle" fontSize="9" fill="white" fontWeight="700" dominantBaseline="middle">{nummer}</text>
                <rect x={xPos(p.positieM)-30} y={yPos(p.diepte)-30} width={60} height={14} rx="3" fill="#1e40af" opacity="0.92" />
                <text x={xPos(p.positieM)} y={yPos(p.diepte)-21} textAnchor="middle" fontSize="7.5" fill="white" fontWeight="700">{p.diepte}m · {p.positieM}m · ↓{hoekIn}° ↑{hoekUit}°</text>
              </g>
            );
          })}

          {/* Uittredepunt — genummerd */}
          {(() => {
            const p = alleDieptePunten[alleDieptePunten.length - 1];
            const nummer = alleDieptePunten.length;
            if (!p) return null;
            return (
              <g style={{ cursor: "ns-resize" }} onMouseDown={(ev) => { ev.preventDefault(); ev.stopPropagation(); const idx = dieptePunten.findIndex(x => x.id === "eind"); setDiepteSlepen({ idx, mode: "y" }); }}>
                <circle cx={xPos(totaalM)} cy={yPos(p.diepte)} r="10" fill="#2563eb" stroke="white" strokeWidth="2.5" />
                <text x={xPos(totaalM)} y={yPos(p.diepte)+1} textAnchor="middle" fontSize="9" fill="white" fontWeight="700" dominantBaseline="middle">{nummer}</text>
                <text x={xPos(totaalM)-14} y={yPos(p.diepte)-8} textAnchor="end" fontSize="8" fill="#2563eb" fontWeight="700">{p.diepte}m ↗</text>
              </g>
            );
          })()}

          {/* Analyse bolletjes in profiel — gesorteerd */}
          {gesorteerdeAnalyse.map((p, i) => {
            const x = xPos(p.positieM ?? 0);
            const kleur = p.vertaald?.kleur ?? "#9ca3af";
            const d = diepteOpM(p.positieM ?? 0);
            return (
              <g key={i} style={{ cursor: "ew-resize" }} onMouseDown={(ev) => { ev.preventDefault(); setDragIdx(i); }}>
                <line x1={x} y1={PAD.top} x2={x} y2={PAD.top+plotH} stroke={kleur} strokeWidth="1.5" strokeDasharray="4 3" opacity="0.5" />
                {/* Bolletje op maaiveld */}
                <circle cx={x} cy={yPos(0)+16} r="11" fill={kleur} stroke="white" strokeWidth="2.5" />
                <text x={x} y={yPos(0)+17} textAnchor="middle" fontSize="9" fill="white" fontWeight="700" dominantBaseline="middle">{i+1}</text>
                {/* Bolletje op boring diepte */}
                <circle cx={x} cy={yPos(d)} r="4" fill={kleur} stroke="white" strokeWidth="1.5" opacity="0.8" />
                <text x={x} y={PAD.top+plotH+27} textAnchor="middle" fontSize="8" fill={kleur} fontWeight="600">{p.positieM}m</text>
              </g>
            );
          })}

          {/* Assen */}
          <line x1={PAD.left} y1={PAD.top} x2={PAD.left} y2={PAD.top+plotH} stroke="#d1d5db" strokeWidth="1.5" />
          <line x1={PAD.left} y1={PAD.top+plotH} x2={PAD.left+plotW} y2={PAD.top+plotH} stroke="#d1d5db" strokeWidth="1.5" />
          <text x={PAD.left-40} y={PAD.top+plotH/2} textAnchor="middle" fontSize="9" fill="#6b7280" transform={`rotate(-90,${PAD.left-40},${PAD.top+plotH/2})`}>Diepte (m NAP)</text>
          <text x={PAD.left+plotW/2} y={H-2} textAnchor="middle" fontSize="9" fill="#6b7280">Positie langs boorlijn (m)</text>
        </svg>

        {/* Diepte invoer voor start/eind */}
        <div className="flex items-center gap-6 mt-3 pt-3 border-t border-gray-100 flex-wrap">
          <div className="flex items-center gap-2">
            <span className="text-xs text-blue-600 font-semibold">↘ Intredediepte</span>
            <input type="number" step="0.1" min="-6" max="0"
              value={alleDieptePunten[0]?.diepte ?? -1.5}
              onChange={e => setDieptePunten(prev => prev.map(p => p.id === "start" ? { ...p, diepte: parseFloat(e.target.value) } : p))}
              className="w-20 text-xs border border-gray-200 rounded px-2 py-1 text-center focus:border-blue-500 outline-none" />
            <span className="text-xs text-gray-400">m</span>
          </div>
          <div className="flex items-center gap-2">
            <span className="text-xs text-blue-600 font-semibold">↗ Uittredediepte</span>
            <input type="number" step="0.1" min="-6" max="0"
              value={alleDieptePunten[alleDieptePunten.length-1]?.diepte ?? -1.5}
              onChange={e => setDieptePunten(prev => prev.map(p => p.id === "eind" ? { ...p, diepte: parseFloat(e.target.value) } : p))}
              className="w-20 text-xs border border-gray-200 rounded px-2 py-1 text-center focus:border-blue-500 outline-none" />
            <span className="text-xs text-gray-400">m</span>
          </div>
          {diepteModus && <span className="text-xs text-blue-600">Klik in het profiel om een dieptepunt toe te voegen</span>}
        </div>
      </div>
    </div>
  );
}


// ─── HOOFD COMPONENT ──────────────────────────────────────────
export default function MapTrace({ project, onTraceOpgeslagen }) {
  const mapRef = useRef(null);
  const leafletMapRef = useRef(null);
  const laagRefs = useRef({});
  const polylineRef = useRef(null);
  const controlepuntMarkersRef = useRef([]);
  const traceLaagRef = useRef(null);
  const modeRef = useRef("niets");
  const dieptepuntMarkersRef = useRef([]); // kaartmarkers voor dieptepunten
  const klicLagenRef = useRef([]); // KLIC achtergrond lagen (stap 3 → stap 4)

  const [actieveLagen, setActieveLagen] = useState(Object.fromEntries(LAGEN.map(l => [l.id, l.standaardAan])));
  const [modus, setModus] = useState("niets");
  const [controlePunten, setControlePunten] = useState([]);
  const [analysePunten, setAnalysePunten] = useState([]);
  const analysePuntenRef = useRef([]); // mirror voor gebruik in Leaflet handlers
  const [opgeslagen, setOpgeslagen] = useState(false);
  const [legendaOpen, setLegendaOpen] = useState(true);
  const [analyseBezig, setAnalyseBezig] = useState(false);
  const [toonVerwijderPopup, setToonVerwijderPopup] = useState(false);
  const [verwijderBezig, setVerwijderBezig] = useState(false);
  const [kaartTab, setKaartTab] = useState("boorlijn"); // "boorlijn" | "analyse" | "diepte"
  const [diepteModus, setDiepteModus] = useState(false);
  const opstellingRef = useRef({ boorMachine: null, bentonietTank: null });
  const opstellingKlikRef = useRef(null);

  // Laad opgeslagen dieptepunten uit project
  const [dieptePunten, setDieptePunten] = useState(() => {
    try {
      const saved = project?.diepte_punten;
      if (saved) return typeof saved === "string" ? JSON.parse(saved) : saved;
    } catch {}
    return [
      { id: "start", positieM: 0, diepte: -1.5, vast: true },
      { id: "eind", positieM: null, diepte: -1.5, vast: true },
    ];
  });

  // Auto-save dieptePunten wanneer ze veranderen
  const dieptePuntenSaveTimer = useRef(null);
  useEffect(() => {
    clearTimeout(dieptePuntenSaveTimer.current);
    dieptePuntenSaveTimer.current = setTimeout(() => {
      onTraceOpgeslagen?.({ _alleenDiepte: true, diepte_punten: dieptePunten });
    }, 1500);
  }, [dieptePunten]);

  // Auto-save analysePunten (zonder _marker refs)
  const analysePuntenSaveTimer = useRef(null);
  useEffect(() => {
    clearTimeout(analysePuntenSaveTimer.current);
    analysePuntenSaveTimer.current = setTimeout(() => {
      const opSlaanData = analysePunten.map(({ _marker, ...rest }) => rest);
      if (opSlaanData.length > 0) {
        onTraceOpgeslagen?.({ _alleenAnalyse: true, analyse_punten: opSlaanData });
      }
    }, 1500);
  }, [analysePunten]);

  // DOM event listener voor analyse klikken — altijd nieuwste versie van plaatsAnalysePunt
  useEffect(() => {
    const handler = (e) => {
      const { lat, lng, positieM } = e.detail;
      plaatsAnalysePunt(lat, lng, positieM);
    };
    window.addEventListener("prescan-analyse-klik", handler);
    return () => window.removeEventListener("prescan-analyse-klik", handler);
  }); // Geen dependency array = altijd re-register met nieuwste plaatsAnalysePunt
  // Bestaand tracé laden
  const bestaandTrace = (() => {
    try {
      const g = project?.boortrace_geojson;
      if (!g) return [];
      const p = typeof g === "string" ? JSON.parse(g) : g;
      return p.coordinates?.map(([lng, lat]) => [lat, lng]) ?? [];
    } catch { return []; }
  })();

  const actievePunten = controlePunten.length >= 2 ? controlePunten : bestaandTrace;

  // Converteer positieM naar lat/lng op de boorlijn
  function positieMNaarLatLng(positieM, pts) {
    if (pts.length < 2) return null;
    let afstand = 0;
    for (let i = 0; i < pts.length - 1; i++) {
      const segLen = afstandM(pts[i], pts[i + 1]);
      if (afstand + segLen >= positieM) {
        const t = (positieM - afstand) / segLen;
        return [pts[i][0] + t * (pts[i+1][0] - pts[i][0]), pts[i][1] + t * (pts[i+1][1] - pts[i][1])];
      }
      afstand += segLen;
    }
    return pts[pts.length - 1];
  }

  // Totale lijnlengte
  function totaaleLijnLengte(pts) {
    let t = 0;
    for (let i = 1; i < pts.length; i++) t += afstandM(pts[i-1], pts[i]);
    return t;
  }

  // Update dieptepunt markers op de kaart
  useEffect(() => {
    const kaart = leafletMapRef.current;
    if (!kaart || typeof window === "undefined" || !window.L) return;
    const L = window.L;
    const pts = actievePunten;

    dieptepuntMarkersRef.current.forEach(m => kaart.removeLayer(m));
    dieptepuntMarkersRef.current = [];

    const totaalM = totaaleLijnLengte(pts);
    if (pts.length < 2 || totaalM === 0) return;

    dieptePunten.forEach((dp, dpIdx) => {
      const posM = dp.id === "eind" ? totaalM : (dp.positieM ?? 0);
      const latLng = positieMNaarLatLng(posM, pts);
      if (!latLng) return;

      const maakIcon = (d, isVast) => L.divIcon({
        className: "",
        html: `<div style="width:22px;height:22px;background:#2563eb;border:2.5px solid white;border-radius:${isVast ? "50%" : "3px"};transform:${isVast ? "none" : "rotate(45deg)"};box-shadow:0 2px 6px rgba(0,0,0,0.4);display:flex;align-items:center;justify-content:center;cursor:${isVast ? "ns-resize" : "move"};">
          <span style="transform:${isVast ? "none" : "rotate(-45deg)"};color:white;font-size:7px;font-weight:700;white-space:nowrap;">${d}m</span>
        </div>`,
        iconSize: [22, 22], iconAnchor: [11, 11],
      });

      const marker = L.marker(latLng, {
        icon: maakIcon(dp.diepte, dp.vast),
        draggable: !dp.vast,
        zIndexOffset: 500,
      }).addTo(kaart);

      if (!dp.vast) {
        marker.on("drag", (e) => {
          const snap = snapNaarLijn(e.latlng.lat, e.latlng.lng, pts);
          marker.setLatLng([snap.lat, snap.lng]);
          setDieptePunten(prev => prev.map((p, i) => i === dpIdx ? { ...p, positieM: Math.round(snap.positieM) } : p));
        });
      }

      marker.bindTooltip(`${dp.vast ? (dp.id === "start" ? "↘ Intrede" : "↗ Uittrede") : "Dieptepunt"}: ${dp.diepte}m`, { direction: "top" });
      dieptepuntMarkersRef.current.push(marker);
    });
  }, [dieptePunten, actievePunten.length]);
  // Init kaart
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
    const kaart = L.map(mapRef.current, { center: [52.15, 5.38], zoom: 8, maxZoom: 22 });
    leafletMapRef.current = kaart;
    L.tileLayer("https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:3857/{z}/{x}/{y}.png", { attribution: "© PDOK BRT", maxZoom: 22 }).addTo(kaart);
    LAGEN.forEach(laag => {
      const l = laag.type === "wmts"
        ? L.tileLayer(laag.url, { maxZoom: 19, opacity: 0.9, attribution: "© PDOK" })
        : L.tileLayer.wms(laag.url, { layers: laag.layers, format: "image/png", transparent: true, opacity: 0.65, attribution: "© PDOK" });
      laagRefs.current[laag.id] = l;
      if (laag.standaardAan) l.addTo(kaart);
    });

    // Laad bestaand tracé
    if (project?.boortrace_geojson) {
      try {
        const geojson = typeof project.boortrace_geojson === "string" ? JSON.parse(project.boortrace_geojson) : project.boortrace_geojson;
        traceLaagRef.current = L.geoJSON(geojson, { style: { color: "#2563eb", weight: 4, opacity: 1 } }).addTo(kaart);
        kaart.fitBounds(traceLaagRef.current.getBounds(), { padding: [40, 40] });

        // Maak een onzichtbare brede polyline voor klik/hover events op opgeslagen lijn
        const pts = geojson.coordinates?.map(([lng, lat]) => [lat, lng]) ?? [];
        if (pts.length >= 2) {
          voegEventPolylineToe(kaart, pts, L);
        }
      } catch (e) {}
    }

    // KLIC lagen als achtergrond laden (uit stap 3 opgeslagen instellingen)
    setTimeout(() => {
      laadKlicAchtergrondOpKaart(kaart, project, klicLagenRef).catch(() => {});
    }, 800);

    // Kaart klik handler
    kaart.on("click", async (e) => {
      const { lat, lng } = e.latlng;
      const mode = modeRef.current;

      if (mode === "tekenen") {
        setControlePunten(prev => {
          const nieuw = [...prev, [lat, lng]];
          tekenPolyline(kaart, nieuw);
          hertekenAlleMarkers(kaart, nieuw);
          return nieuw;
        });
      }

      // Analyse: dispatch DOM event (geen closure issue)
      if (mode === "analyse") {
        const pts2 = modeRef._controlePunten ?? [];
        if (pts2.length < 2) return;
        const snap = snapNaarLijn(lat, lng, pts2);
        if (afstandM([lat, lng], [snap.lat, snap.lng]) > 50) return;
        window.dispatchEvent(new CustomEvent("prescan-analyse-klik", { detail: { lat: snap.lat, lng: snap.lng, positieM: snap.positieM } }));
        return;
      }

      if (mode === "boor_machine" || mode === "bentoniet_tank") {
        const kleur = mode === "boor_machine" ? "#f97316" : "#92400e";
        const label = mode === "boor_machine" ? "🔶 Boormachine" : "🟫 Bentoniet tank";
        const sleutel = mode === "boor_machine" ? "boorMachine" : "bentonietTank";

        if (!opstellingKlikRef.current) {
          // Eerste klik — sla op
          opstellingKlikRef.current = { lat, lng };
          // Toon tijdelijk marker
          const tmpMarker = L.circleMarker([lat, lng], { radius: 6, color: kleur, fillColor: kleur, fillOpacity: 0.7 }).addTo(kaart);
          opstellingKlikRef.current._tmpMarker = tmpMarker;
        } else {
          // Tweede klik — teken rechthoek
          const p1 = opstellingKlikRef.current;
          if (p1._tmpMarker) kaart.removeLayer(p1._tmpMarker);
          opstellingKlikRef.current = null;

          const bounds = [[Math.min(p1.lat, lat), Math.min(p1.lng, lng)], [Math.max(p1.lat, lat), Math.max(p1.lng, lng)]];

          // Verwijder vorige
          if (opstellingRef.current[sleutel]?._layer) kaart.removeLayer(opstellingRef.current[sleutel]._layer);

          const rect = L.rectangle(bounds, {
            color: kleur, weight: 2, fillColor: kleur, fillOpacity: 0.15, dashArray: "6 4"
          }).addTo(kaart);
          rect.bindTooltip(label, { permanent: true, direction: "center", className: "text-xs font-semibold" });
          opstellingRef.current[sleutel] = { bounds, _layer: rect };
          setModus("niets");
          setKaartTab("opstelling");
        }
        return;
      }

    });
  }

  // Snap een punt naar de dichtstbijzijnde positie OP de lijn
  // Geeft { lat, lng, positieM } terug
  function snapNaarLijn(kLat, kLng, pts) {
    if (pts.length < 2) return { lat: kLat, lng: kLng, positieM: 0 };

    let besteSnap = null;
    let besteDist = Infinity;
    let positieTot = 0;

    for (let i = 0; i < pts.length - 1; i++) {
      const [lat1, lng1] = pts[i];
      const [lat2, lng2] = pts[i + 1];
      const segLen = afstandM(pts[i], pts[i + 1]);

      // Project klikpunt op segment (lineaire interpolatie)
      const dx = lat2 - lat1, dy = lng2 - lng1;
      const lenSq = dx * dx + dy * dy;
      let t = lenSq === 0 ? 0 : ((kLat - lat1) * dx + (kLng - lng1) * dy) / lenSq;
      t = Math.max(0, Math.min(1, t));

      const snapLat = lat1 + t * dx;
      const snapLng = lng1 + t * dy;
      const dist = afstandM([kLat, kLng], [snapLat, snapLng]);

      if (dist < besteDist) {
        besteDist = dist;
        besteSnap = { lat: snapLat, lng: snapLng, positieM: Math.round(positieTot + t * segLen) };
      }
      positieTot += segLen;
    }

    return besteSnap ?? { lat: kLat, lng: kLng, positieM: 0 };
  }

  function berekenPositieOpLijn(lat, lng, pts) {
    return snapNaarLijn(lat, lng, pts).positieM;
  }


  // Analyse punt plaatsen — synchroon marker, async BGT
  function plaatsAnalysePunt(lat, lng, positieM) {
    const kaart = leafletMapRef.current;
    const L = window.L;
    if (!kaart || !L) return;

    // 1. Direct een grijze placeholder marker plaatsen
    const nr = (analysePuntenRef.current.length + 1);
    const placeholder = L.circleMarker([lat, lng], {
      radius: 11, fillColor: "#9ca3af", color: "#fff", weight: 2.5, fillOpacity: 1,
    }).bindTooltip(`${nr} — ⏳ Laden...`, { direction: "top" }).addTo(kaart);

    const nieuwPunt = {
      lat, lng, positieM,
      vertaald: { label: "Laden...", kleur: "#9ca3af", icoon: "⏳", herstel: "?" },
      _marker: placeholder,
    };

    // 2. Direct state updaten
    const huidig = [...analysePuntenRef.current, nieuwPunt].sort((a, b) => a.positieM - b.positieM);
    analysePuntenRef.current = huidig;
    setAnalysePunten([...huidig]);

    // 3. Klik handler voor verwijderen
    placeholder.on("click", (ev) => {
      L.DomEvent.stopPropagation(ev);
      kaart.removeLayer(placeholder);
      const nieuw = analysePuntenRef.current.filter(p => p._marker !== placeholder)
        .sort((a, b) => a.positieM - b.positieM);
      analysePuntenRef.current = nieuw;
      setAnalysePunten([...nieuw]);
    });

    // 4. Async: BGT data ophalen en marker updaten
    haalOppervlakOp(lat, lng).then(result => {
      const vertaald = result?.vertaald ?? { label: "Geen data", kleur: "#9ca3af", icoon: "❓", herstel: "?" };
      // Update marker kleur en tooltip
      placeholder.setStyle({ fillColor: vertaald.kleur });
      placeholder.setTooltipContent(`${nr} — ${vertaald.icoon} ${vertaald.label}`);
      // Update state
      const bijgewerkt = analysePuntenRef.current.map(p =>
        p._marker === placeholder ? { ...p, vertaald } : p
      );
      analysePuntenRef.current = bijgewerkt;
      setAnalysePunten([...bijgewerkt]);
    }).catch(() => {});
  }


  const eventPolylineRef = useRef(null); // onzichtbare brede lijn voor events

  // Voeg event capture polyline toe — onzichtbaar maar breed voor hover/klik
  function voegEventPolylineToe(kaart, pts, L) {
    if (eventPolylineRef.current) kaart.removeLayer(eventPolylineRef.current);
    if (pts.length < 2) return;

    const eventLijn = L.polyline(pts, {
      color: "transparent",
      weight: 20,
      opacity: 0,
    }).addTo(kaart);

    eventPolylineRef.current = eventLijn;

    // Maak preview marker
    const preview = L.marker([0, 0], {
      icon: L.divIcon({
        className: "",
        html: `<div style="width:26px;height:26px;background:#16a34a;border:3px solid white;border-radius:50%;box-shadow:0 2px 10px rgba(0,0,0,0.5);display:flex;align-items:center;justify-content:center;pointer-events:none;opacity:0.85;"><span style="color:white;font-size:13px;font-weight:700;line-height:1;">+</span></div>`,
        iconSize: [26, 26], iconAnchor: [13, 13],
      }),
      interactive: false, zIndexOffset: 3000,
    });
    hoverMarkerRef.current = preview;

    eventLijn.on("mousemove", (e) => {
      if (modeRef.current !== "analyse") {
        if (kaart.hasLayer(preview)) kaart.removeLayer(preview);
        return;
      }
      const snap = snapNaarLijn(e.latlng.lat, e.latlng.lng, pts);
      if (!kaart.hasLayer(preview)) preview.addTo(kaart);
      preview.setLatLng([snap.lat, snap.lng]);
    });

    eventLijn.on("mouseout", () => {
      if (kaart.hasLayer(preview)) kaart.removeLayer(preview);
    });

    eventLijn.on("click", async (e) => {
      L.DomEvent.stopPropagation(e);
      const mode = modeRef.current;
      const { lat, lng } = e.latlng;

      if (mode === "bewerken" || mode === "tekenen") {
        setControlePunten(prev => {
          let besteIdx = prev.length - 1, minDist = Infinity;
          for (let i = 0; i < prev.length - 1; i++) {
            const mid = [(prev[i][0]+prev[i+1][0])/2, (prev[i][1]+prev[i+1][1])/2];
            const d = afstandM([lat, lng], mid);
            if (d < minDist) { minDist = d; besteIdx = i; }
          }
          const nieuw = [...prev.slice(0, besteIdx+1), [lat, lng], ...prev.slice(besteIdx+1)];
          tekenPolyline(kaart, nieuw);
          hertekenAlleMarkers(kaart, nieuw);
          return nieuw;
        });
        return;
      }

      if (mode === "analyse") { const _s = snapNaarLijn(lat, lng, modeRef._controlePunten ?? []); window.dispatchEvent(new CustomEvent("prescan-analyse-klik", { detail: { lat: _s.lat, lng: _s.lng, positieM: _s.positieM } })); }
    });
  }

  function tekenPolyline(kaart, pts) {
    const L = window.L;
    if (polylineRef.current) {
      kaart.removeLayer(polylineRef.current);
      if (hoverMarkerRef.current && kaart.hasLayer(hoverMarkerRef.current)) {
        kaart.removeLayer(hoverMarkerRef.current);
      }
    }
    if (pts.length < 2) return;
    const lijn = L.polyline(pts, { color: "#2563eb", weight: 6, opacity: 0.9 }).addTo(kaart);
    polylineRef.current = lijn;

    // Hover preview marker — toont het echte analysepunt preview
    const preview = L.marker([0, 0], {
      icon: L.divIcon({
        className: "",
        html: `<div style="width:26px;height:26px;background:#16a34a;border:3px solid white;border-radius:50%;box-shadow:0 2px 10px rgba(0,0,0,0.5);display:flex;align-items:center;justify-content:center;pointer-events:none;opacity:0.85;transition:opacity 0.1s;">
          <span style="color:white;font-size:13px;font-weight:700;line-height:1;">+</span>
        </div>`,
        iconSize: [26, 26], iconAnchor: [13, 13],
      }),
      interactive: false, zIndexOffset: 3000,
    });
    hoverMarkerRef.current = preview;

    lijn.on("mousemove", (e) => {
      if (modeRef.current !== "analyse") {
        if (kaart.hasLayer(preview)) kaart.removeLayer(preview);
        return;
      }
      const snap = snapNaarLijn(e.latlng.lat, e.latlng.lng, pts);
      if (!kaart.hasLayer(preview)) preview.addTo(kaart);
      preview.setLatLng([snap.lat, snap.lng]);
    });

    lijn.on("mouseout", () => {
      if (kaart.hasLayer(preview)) kaart.removeLayer(preview);
    });

    // Klik op lijn — controlepunt invoegen (bewerken/tekenen) of analysepunt plaatsen (analyse)
    lijn.on("click", async (e) => {
      L.DomEvent.stopPropagation(e);
      const mode = modeRef.current;
      const { lat, lng } = e.latlng;

      if (mode === "bewerken" || mode === "tekenen") {
        setControlePunten(prev => {
          let besteIdx = prev.length - 1, minDist = Infinity;
          for (let i = 0; i < prev.length - 1; i++) {
            const mid = [(prev[i][0] + prev[i+1][0]) / 2, (prev[i][1] + prev[i+1][1]) / 2];
            const d = afstandM([lat, lng], mid);
            if (d < minDist) { minDist = d; besteIdx = i; }
          }
          const nieuw = [...prev.slice(0, besteIdx + 1), [lat, lng], ...prev.slice(besteIdx + 1)];
          tekenPolyline(kaart, nieuw);
          hertekenAlleMarkers(kaart, nieuw);
          return nieuw;
        });
      }

      if (mode === "analyse") { const _s = snapNaarLijn(lat, lng, modeRef._controlePunten ?? []); window.dispatchEvent(new CustomEvent("prescan-analyse-klik", { detail: { lat: _s.lat, lng: _s.lng, positieM: _s.positieM } })); }
    });
  }

  // Maak genummerd icoon
  function maakControlepuntIcon(L, nummer, isEerste, isLaatste) {
    const kleur = isEerste ? "#15803d" : isLaatste ? "#dc2626" : "#2563eb";
    const vorm = isEerste || isLaatste ? "50%" : "3px";
    return L.divIcon({
      className: "",
      html: `<div style="
        width:20px;height:20px;
        background:${kleur};
        border:2px solid white;
        border-radius:${vorm};
        cursor:move;
        box-shadow:0 1px 4px rgba(0,0,0,0.4);
        display:flex;align-items:center;justify-content:center;
        font-size:9px;font-weight:700;color:white;
        font-family:sans-serif;
      ">${nummer}</div>`,
      iconSize: [20, 20],
      iconAnchor: [10, 10],
    });
  }

  // Maak genummerd analyse icoon
  function maakAnalyseIcon(L, nummer, kleur) {
    return L.divIcon({
      className: "",
      html: `<div style="
        width:22px;height:22px;
        background:${kleur};
        border:2.5px solid white;
        border-radius:50%;
        cursor:pointer;
        box-shadow:0 1px 4px rgba(0,0,0,0.4);
        display:flex;align-items:center;justify-content:center;
        font-size:9px;font-weight:700;color:white;
        font-family:sans-serif;
      ">${nummer}</div>`,
      iconSize: [22, 22],
      iconAnchor: [11, 11],
    });
  }

  // Verwijder en hermaak alle controlepunt markers met juiste nummers
  function hertekenAlleMarkers(kaart, pts) {
    const L = window.L;
    controlepuntMarkersRef.current.forEach(m => kaart.removeLayer(m));
    controlepuntMarkersRef.current = [];
    pts.forEach((_, i) => voegControlepuntMarkerToe(kaart, pts, i));
  }

  function voegControlepuntMarkerToe(kaart, allePunten, index) {
    const L = window.L;
    const [lat, lng] = allePunten[index];
    const isEerste = index === 0;
    const isLaatste = index === allePunten.length - 1;
    const nummer = index + 1;

    const marker = L.marker([lat, lng], {
      draggable: true,
      icon: maakControlepuntIcon(L, nummer, isEerste, isLaatste),
      zIndexOffset: 1000,
    }).addTo(kaart);

    marker.on("drag", (e) => {
      const { lat: nLat, lng: nLng } = e.latlng;
      setControlePunten(prev => {
        const nieuw = [...prev];
        nieuw[index] = [nLat, nLng];
        tekenPolyline(kaart, nieuw);
        return nieuw;
      });
    });

    marker.on("contextmenu", (e) => {
      L.DomEvent.stopPropagation(e);
      setControlePunten(prev => {
        const nieuw = prev.filter((_, i) => i !== index);
        tekenPolyline(kaart, nieuw);
        hertekenAlleMarkers(kaart, nieuw);
        return nieuw;
      });
    });

    const tooltip = isEerste ? `<b>1</b> — Startpunt` : isLaatste ? `<b>${nummer}</b> — Eindpunt` : `<b>${nummer}</b> — Sleep om aan te passen<br><small>Rechtsklik om te verwijderen</small>`;
    marker.bindTooltip(tooltip, { direction: "top" });
    controlepuntMarkersRef.current.push(marker);
  }

  function startTekenen() {
    wisControlepunten();
    setControlePunten([]);
    setModus("tekenen");
    if (traceLaagRef.current && leafletMapRef.current) {
      leafletMapRef.current.removeLayer(traceLaagRef.current);
      traceLaagRef.current = null;
    }
  }

  function startBewerken() {
    const kaart = leafletMapRef.current;
    if (!kaart) return;
    wisControlepunten();
    const pts = bestaandTrace;
    setControlePunten(pts);
    setModus("bewerken");
    if (traceLaagRef.current) { kaart.removeLayer(traceLaagRef.current); traceLaagRef.current = null; }
    tekenPolyline(kaart, pts);
    hertekenAlleMarkers(kaart, pts);
  }

  function wisControlepunten() {
    const kaart = leafletMapRef.current;
    if (!kaart) return;
    controlepuntMarkersRef.current.forEach(m => kaart.removeLayer(m));
    controlepuntMarkersRef.current = [];
    if (polylineRef.current) { kaart.removeLayer(polylineRef.current); polylineRef.current = null; }
  }

  async function handleAnalysePuntVerplaatst(idx, nieuwM) {
    const pts = controlePunten.length >= 2 ? controlePunten : bestaandTrace;
    if (pts.length < 2) return;
    let afstand = 0;
    for (let i = 0; i < pts.length - 1; i++) {
      const segLen = afstandM(pts[i], pts[i + 1]);
      if (afstand + segLen >= nieuwM) {
        const t = (nieuwM - afstand) / segLen;
        const nLat = pts[i][0] + t * (pts[i+1][0] - pts[i][0]);
        const nLng = pts[i][1] + t * (pts[i+1][1] - pts[i][1]);
        setAnalysePunten(prev => {
          const gesorteerd = [...prev].sort((a, b) => a.positieM - b.positieM);
          const punt = gesorteerd[idx];
          if (!punt) return prev;
          if (punt._marker) punt._marker.setLatLng([nLat, nLng]);
          punt.lat = nLat; punt.lng = nLng; punt.positieM = Math.round(nieuwM);
          return [...prev];
        });
        return;
      }
      afstand += segLen;
    }
  }

  function wisAnalysePunten() {
    const kaart = leafletMapRef.current;
    analysePuntenRef.current.forEach(p => { if (p._marker && kaart) kaart.removeLayer(p._marker); });
    analysePuntenRef.current = [];
    setAnalysePunten([]);
  }

  function wisAlles() {
    wisControlepunten();
    wisAnalysePunten();
    setControlePunten([]);
    setModus("niets");
    setDieptePunten([
      { id: "start", positieM: 0, diepte: -1.5, vast: true },
      { id: "eind", positieM: null, diepte: -1.5, vast: true },
    ]);
    const kaart = leafletMapRef.current;
    dieptepuntMarkersRef.current.forEach(m => kaart?.removeLayer(m));
    dieptepuntMarkersRef.current = [];
    if (hoverMarkerRef.current && kaart?.hasLayer(hoverMarkerRef.current)) kaart.removeLayer(hoverMarkerRef.current);
    if (traceLaagRef.current && kaart) { kaart.removeLayer(traceLaagRef.current); traceLaagRef.current = null; }
  }

  async function opslaanTrace() {
    if (controlePunten.length < 2) return;
    const geojson = { type: "LineString", coordinates: controlePunten.map(([lat, lng]) => [lng, lat]) };
    const kaart = leafletMapRef.current;
    const L = window.L;
    wisControlepunten();
    if (traceLaagRef.current && kaart) kaart.removeLayer(traceLaagRef.current);
    traceLaagRef.current = L.geoJSON(geojson, { style: { color: "#2563eb", weight: 4, opacity: 1 } }).addTo(kaart);

    // Herbereken positie van alle analysepunten op de nieuwe boorlijn
    if (analysePunten.length > 0) {
      setAnalysePunten(prev => prev.map(p => {
        const snap = snapNaarLijn(p.lat, p.lng, controlePunten);
        if (p._marker) p._marker.setLatLng([snap.lat, snap.lng]);
        return { ...p, lat: snap.lat, lng: snap.lng, positieM: snap.positieM };
      }).sort((a, b) => a.positieM - b.positieM));
    }

    await onTraceOpgeslagen(geojson);
    setOpgeslagen(true);
    setModus("niets");
    setTimeout(() => setOpgeslagen(false), 3000);
  }

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

  // Update cursor op basis van modus
  useEffect(() => {
    if (!leafletMapRef.current) return;
    const cursors = { tekenen: "crosshair", bewerken: "default", analyse: "cell", niets: "" };
    leafletMapRef.current.getContainer().style.cursor = cursors[modus] ?? "";
  }, [modus]);

  // Sync analyse punten controlePunten ref voor positie berekening
  useEffect(() => {
    modeRef._controlePunten = controlePunten.length >= 2 ? controlePunten : bestaandTrace;
  }, [controlePunten, bestaandTrace.length]);

  const heeftBestaandTrace = bestaandTrace.length >= 2 || controlePunten.length >= 2;

  return (
    <div className="flex gap-4 h-full">
      {/* Kaart kolom */}
      <div className="flex-1 flex flex-col gap-3 min-w-0">

        {/* Toolbar */}
        <div className="flex items-center gap-2 flex-wrap">
          {/* Lagen toggle */}
          <div className="flex items-center gap-1 bg-white border border-gray-200 rounded-lg px-3 py-1.5">
            <span className="text-xs text-gray-400 font-medium mr-1">Lagen</span>
            <button onClick={() => setLegendaOpen(!legendaOpen)} className="text-xs text-blue-600 px-2 py-1 rounded hover:bg-blue-50 transition-colors">
              {legendaOpen ? "▲" : "▼"}
            </button>
          </div>

          {/* Tracé knoppen */}
          <div className="flex items-center gap-1 bg-white border border-gray-200 rounded-lg px-3 py-1.5">
            <span className="text-xs text-gray-400 font-medium mr-2">Boorlijn:</span>

            {modus === "niets" && (
              <>
                {heeftBestaandTrace ? (
                  <>
                    <button onClick={startBewerken} className="flex items-center gap-1 text-xs font-medium text-blue-600 hover:text-blue-800 px-2.5 py-1 rounded-md hover:bg-blue-50 transition-colors">
                      <span className="w-3 h-3 bg-blue-600 rounded-sm inline-block" /> Bewerken
                    </button>
                    <button onClick={() => setToonVerwijderPopup(true)} className="flex items-center gap-1 text-xs font-medium text-red-500 hover:text-red-700 px-2.5 py-1 rounded-md hover:bg-red-50 transition-colors">
                      🗑 Verwijderen
                    </button>
                  </>
                ) : (
                  <button onClick={startTekenen} className="flex items-center gap-1 text-xs font-medium text-blue-600 hover:text-blue-800 px-2.5 py-1 rounded-md hover:bg-blue-50 transition-colors">
                    <span className="w-3 h-3 bg-blue-600 rounded-sm inline-block" /> Nieuwe boorlijn tekenen
                  </button>
                )}
              </>
            )}

            {(modus === "tekenen" || modus === "bewerken") && (
              <>
                <span className="text-xs text-blue-600 font-medium mr-2">
                  {modus === "tekenen" ? "Klik om punten te zetten" : "Sleep punten om aan te passen"}
                  {controlePunten.length > 0 && ` · ${controlePunten.length} punt${controlePunten.length !== 1 ? "en" : ""}`}
                </span>
                <button onClick={() => { wisControlepunten(); setControlePunten([]); setModus("niets"); if (bestaandTrace.length >= 2) { const L = window.L; const kaart = leafletMapRef.current; if (kaart && !traceLaagRef.current) { const g = project.boortrace_geojson; const parsed = typeof g === "string" ? JSON.parse(g) : g; traceLaagRef.current = L.geoJSON(parsed, { style: { color: "#2563eb", weight: 4, opacity: 1 } }).addTo(kaart); } } }} className="text-xs text-gray-500 px-2.5 py-1 rounded-md hover:bg-gray-100">Annuleren</button>
                {controlePunten.length >= 2 && (
                  <button onClick={opslaanTrace} className="text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 px-3 py-1 rounded-md transition-colors">
                    {opgeslagen ? "✓ Opgeslagen!" : "Opslaan"}
                  </button>
                )}
              </>
            )}
          </div>

          {/* Analyse knoppen */}
          <div className="flex items-center gap-1 bg-white border border-gray-200 rounded-lg px-3 py-1.5">
            <span className="text-xs text-gray-400 font-medium mr-2">Analyse:</span>
            {modus !== "analyse" ? (
              <button
                onClick={() => setModus("analyse")}
                disabled={!heeftBestaandTrace}
                className="flex items-center gap-1 text-xs font-medium text-green-600 hover:text-green-800 px-2.5 py-1 rounded-md hover:bg-green-50 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <span className="w-3 h-3 bg-green-500 rounded-full inline-block" /> Punt toevoegen
              </button>
            ) : (
              <>
                <span className="text-xs text-green-600 font-medium mr-2">
                  Zweef over boorlijn · klik om punt te plaatsen{analyseBezig ? " ⏳" : analysePunten.length > 0 ? ` · ${analysePunten.length} punt${analysePunten.length !== 1 ? "en" : ""}` : ""}
                </span>
                <button onClick={() => setModus("niets")} className="text-xs text-gray-500 px-2.5 py-1 rounded-md hover:bg-gray-100">Klaar</button>
              </>
            )}
            {analysePunten.length > 0 && modus === "niets" && (
              <span className="text-xs text-gray-400 ml-1">· {analysePunten.length} punt{analysePunten.length !== 1 ? "en" : ""} — sleep om te verplaatsen</span>
            )}
            {analysePunten.length > 0 && (
              <button onClick={wisAnalysePunten} className="text-xs text-red-400 hover:text-red-600 px-2.5 py-1 rounded-md hover:bg-red-50 transition-colors ml-2">
                Wis punten
              </button>
            )}
          </div>
        </div>

        {/* Legenda */}
        {legendaOpen && (
          <div className="bg-white border border-gray-200 rounded-xl p-3 shadow-sm">
            <div className="flex items-center gap-4 mb-2 text-xs text-gray-400 font-medium">
              <span className="flex items-center gap-1.5"><span className="w-5 h-5 bg-blue-600 rounded-sm inline-flex items-center justify-center text-white text-xs font-bold">1</span> Sleepbaar punt boorlijn (rechtsklik = verwijderen, klik op lijn = invoegen)</span>
              <span className="flex items-center gap-1.5"><span className="w-5 h-5 bg-green-500 rounded-full inline-flex items-center justify-center text-white text-xs font-bold">1</span> Analysepunt (klik om te verwijderen)</span>
            </div>
            <div className="grid grid-cols-2 gap-1 sm:grid-cols-3 lg:grid-cols-4 border-t border-gray-100 pt-2">
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
        <div ref={mapRef} className="rounded-xl border border-gray-200 overflow-hidden shadow-sm" style={{ height: 420 }} />

        {/* KLIC achtergrond status */}
        {project?.bestanden_meta && project.bestanden_meta !== "[]" && (
          <div className="text-xs text-gray-400 px-1">
            🗂 KLIC-ondergrond actief — lagen uit stap 3 worden als achtergrond getoond
          </div>
        )}

        {/* Dwarsprofiel */}
        <Dwarsprofiel
          controlePunten={actievePunten}
          analysePunten={analysePunten}
          project={project}
          onAnalysePuntVerplaatst={handleAnalysePuntVerplaatst}
          dieptePunten={dieptePunten}
          setDieptePunten={setDieptePunten}
          diepteModus={diepteModus}
          setDiepteModus={setDiepteModus}
        />

        {/* Profiel details tabel */}
        {actievePunten.length >= 2 && (() => {
          let totaalM = 0;
          for (let i = 1; i < actievePunten.length; i++) totaalM += afstandM(actievePunten[i-1], actievePunten[i]);
          const allePts = dieptePunten.map(p => ({ ...p, positieM: p.id === "eind" ? totaalM : (p.positieM ?? 0) })).sort((a,b) => a.positieM - b.positieM);
          return (
            <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
              <div className="px-5 py-3 border-b border-gray-100 flex items-center justify-between">
                <h3 className="text-sm font-semibold text-gray-900">📋 Boorlijn profiel — coördinaten</h3>
                <span className="text-xs text-gray-400">Bewerk X (positie), Z (diepte) direct in de tabel</span>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-xs">
                  <thead>
                    <tr className="border-b border-gray-100 bg-gray-50">
                      <th className="px-4 py-2 text-left text-gray-500 font-medium">#</th>
                      <th className="px-4 py-2 text-left text-gray-500 font-medium">Type</th>
                      <th className="px-4 py-2 text-left text-gray-500 font-medium">X — positie (m)</th>
                      <th className="px-4 py-2 text-left text-gray-500 font-medium">Z — diepte (m)</th>
                      <th className="px-4 py-2 text-left text-gray-500 font-medium">Hoek ↓</th>
                      <th className="px-4 py-2 text-left text-gray-500 font-medium">Hoek ↑</th>
                      <th className="px-4 py-2 text-left text-gray-500 font-medium">Segmentlengte</th>
                      <th className="px-4 py-2 text-left text-gray-500 font-medium"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {allePts.map((p, i) => {
                      const prev = allePts[i-1];
                      const next = allePts[i+1];
                      const hoekIn = prev ? (Math.abs(Math.atan2(p.diepte - prev.diepte, p.positieM - prev.positieM) * 180 / Math.PI)).toFixed(1) : "—";
                      const hoekUit = next ? (Math.abs(Math.atan2(next.diepte - p.diepte, next.positieM - p.positieM) * 180 / Math.PI)).toFixed(1) : "—";
                      const dM = prev ? p.positieM - prev.positieM : 0;
                      const dD = prev ? p.diepte - prev.diepte : 0;
                      const segLen = prev ? Math.sqrt(dM*dM + dD*dD).toFixed(2) : "—";
                      return (
                        <tr key={p.id} className="border-b border-gray-50 hover:bg-blue-50/30 transition-colors">
                          <td className="px-4 py-2 font-bold text-blue-600">{i+1}</td>
                          <td className="px-4 py-2 text-gray-500">{p.id === "start" ? "↘ Intrede" : p.id === "eind" ? "↗ Uittrede" : "Dieptepunt"}</td>
                      <td className="px-4 py-2">
                            {p.vast ? <span className="text-gray-400">{Math.round(p.positieM)}m</span> :
                              <div className="flex items-center gap-1">
                                <button onClick={() => setDieptePunten(prev2 => prev2.map(x => x.id === p.id ? { ...x, positieM: Math.max(0.1, Math.round((x.positieM ?? 0) * 10 - 1) / 10) } : x))} className="w-5 h-5 text-xs bg-gray-100 hover:bg-gray-200 rounded flex items-center justify-center font-bold">←</button>
                                <input type="number" step="0.1" min="0.1" max={totaalM - 0.1}
                                  value={p.positieM}
                                  onChange={e => setDieptePunten(prev2 => prev2.map(x => x.id === p.id ? { ...x, positieM: parseFloat(e.target.value) } : x))}
                                  className="w-14 border border-gray-200 rounded px-1 py-0.5 text-center focus:border-blue-500 outline-none text-xs" />
                                <button onClick={() => setDieptePunten(prev2 => prev2.map(x => x.id === p.id ? { ...x, positieM: Math.min(Math.round((totaalM - 0.1)*10)/10, Math.round(((x.positieM ?? 0) * 10 + 1)) / 10) } : x))} className="w-5 h-5 text-xs bg-gray-100 hover:bg-gray-200 rounded flex items-center justify-center font-bold">→</button>
                              </div>
                            }
                          </td>
                          <td className="px-4 py-2">
                            <div className="flex items-center gap-1">
                              <button onClick={() => setDieptePunten(prev2 => prev2.map(x => x.id === p.id ? { ...x, diepte: Math.min(0, Math.round((x.diepte + 0.1)*10)/10) } : x))} className="w-5 h-5 text-xs bg-gray-100 hover:bg-gray-200 rounded flex items-center justify-center font-bold">↑</button>
                              <input type="number" step="0.1" min="-6" max="0"
                                value={p.diepte}
                                onChange={e => setDieptePunten(prev2 => prev2.map(x => x.id === p.id ? { ...x, diepte: parseFloat(e.target.value) } : x))}
                                className="w-14 border border-gray-200 rounded px-1 py-0.5 text-center focus:border-blue-500 outline-none text-xs" />
                              <button onClick={() => setDieptePunten(prev2 => prev2.map(x => x.id === p.id ? { ...x, diepte: Math.max(-6, Math.round((x.diepte - 0.1)*10)/10) } : x))} className="w-5 h-5 text-xs bg-gray-100 hover:bg-gray-200 rounded flex items-center justify-center font-bold">↓</button>
                            </div>
                          </td>
                          <td className="px-4 py-2 text-gray-500">{hoekIn}°</td>
                          <td className="px-4 py-2 text-gray-500">{hoekUit}°</td>
                          <td className="px-4 py-2 text-gray-500">{segLen}{prev ? "m" : ""}</td>
                          <td className="px-4 py-2">
                            {!p.vast && <button onClick={() => setDieptePunten(prev2 => prev2.filter(x => x.id !== p.id))} className="text-red-400 hover:text-red-600 text-xs px-1">✕</button>}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          );
        })()}
      </div>

      {/* Rechter paneel — inhoud op basis van actief tabblad */}
      <div className="w-60 flex-shrink-0 flex flex-col gap-3">
        <div className="bg-white border border-gray-200 rounded-xl shadow-sm flex flex-col overflow-hidden" style={{ maxHeight: 680 }}>

          {/* Tab header */}
          <div className="flex border-b border-gray-100">
            {[
              { id: "boorlijn", icon: "🔵", label: "Boorlijn" },
              { id: "analyse", icon: "🟢", label: "Analyse" },
              { id: "diepte", icon: "🔷", label: "Diepte" },
              { id: "opstelling", icon: "🔶", label: "Opstelling" },
            ].map(tab => (
              <button key={tab.id} onClick={() => setKaartTab(tab.id)}
                className={`flex-1 text-xs py-2.5 font-medium transition-colors border-r border-gray-100 last:border-r-0 ${
                  kaartTab === tab.id ? "bg-blue-50 text-blue-700 border-b-2 border-b-blue-600" : "text-gray-400 hover:text-gray-600 hover:bg-gray-50"
                }`}>
                {tab.icon} {tab.label}
              </button>
            ))}
          </div>

          <div className="flex-1 overflow-y-auto">

            {/* Boorlijn tab */}
            {kaartTab === "boorlijn" && (
              <div className="p-4">
                <div className="text-xs text-gray-400 font-medium mb-3 uppercase tracking-wide">Boorlijn info</div>
                {heeftBestaandTrace ? (
                  <div className="flex flex-col gap-2">
                    <div className="bg-blue-50 rounded-lg p-3 text-xs text-blue-700">
                      <div className="font-semibold mb-1">✓ Boorlijn aanwezig</div>
                      <div>Punten: {(controlePunten.length >= 2 ? controlePunten : bestaandTrace).length}</div>
                      <div>Lengte: {Math.round((() => { const pts = controlePunten.length >= 2 ? controlePunten : bestaandTrace; let t = 0; for(let i=1;i<pts.length;i++) t+=afstandM(pts[i-1],pts[i]); return t; })())} m</div>
                    </div>
                    <div className="text-xs text-gray-400 leading-relaxed">Sleep de blauwe vierkante punten om de lijn aan te passen. Klik op de lijn om een punt in te voegen. Rechtsklik om een punt te verwijderen.</div>
                  </div>
                ) : (
                  <div className="text-center py-6">
                    <div className="text-2xl mb-2">📍</div>
                    <p className="text-xs text-gray-400">Nog geen boorlijn. Klik op "Nieuwe boorlijn tekenen" om te starten.</p>
                  </div>
                )}
              </div>
            )}

            {/* Analyse tab */}
            {kaartTab === "analyse" && (
              <div className="p-4 flex flex-col gap-3">
                {analysePunten.length === 0 ? (
                  <div className="text-center py-6">
                    <div className="text-2xl mb-2">🔬</div>
                    <p className="text-xs text-gray-400 leading-relaxed">
                      {heeftBestaandTrace ? "Zet modus op 'Punt toevoegen' en zweef over de boorlijn." : "Teken eerst een boorlijn."}
                    </p>
                  </div>
                ) : (
                  <>
                    <div>
                      <div className="text-xs text-gray-400 font-medium mb-2">Gedetecteerde typen</div>
                      <div className="flex flex-col gap-1.5">
                        {[...new Map(analysePunten.map(p => [p.vertaald?.label, p])).values()].map((p, i) => (
                          <div key={i} className="flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-medium"
                            style={{ backgroundColor: (p.vertaald?.kleur ?? "#9ca3af") + "12", color: p.vertaald?.kleur ?? "#9ca3af", border: `1px solid ${(p.vertaald?.kleur ?? "#9ca3af")}30` }}>
                            <span>{p.vertaald?.icoon ?? "📍"}</span>
                            <span className="flex-1">{p.vertaald?.label ?? "Geen data"}</span>
                            {p.vertaald?.herstel && <span className="opacity-50">{p.vertaald.herstel}</span>}
                          </div>
                        ))}
                      </div>
                    </div>
                    <div>
                      <div className="text-xs text-gray-400 font-medium mb-2">Punten ({analysePunten.length})</div>
                      <div className="flex flex-col gap-0.5">
                        {[...analysePunten].sort((a,b) => a.positieM - b.positieM).map((p, i) => (
                          <div key={i} className="flex items-center gap-2 text-xs py-1.5 px-2 rounded hover:bg-gray-50">
                            <span className="text-gray-300 font-mono w-4">{i+1}</span>
                            <span>{p.vertaald?.icoon ?? "📍"}</span>
                            <span className="text-gray-600 truncate flex-1">{p.vertaald?.label ?? "?"}</span>
                            <span className="text-gray-300">{p.positieM}m</span>
                          </div>
                        ))}
                        {analyseBezig && <div className="flex items-center gap-2 text-xs py-1.5 px-2 text-gray-400"><span>⏳</span><span>Analyseren...</span></div>}
                      </div>
                    </div>
                  </>
                )}
              </div>
            )}

            {/* Diepte tab */}
            {kaartTab === "diepte" && (
              <div className="p-4 flex flex-col gap-3">
                <div className="text-xs text-gray-400 font-medium uppercase tracking-wide">Dieptepunten</div>
                {dieptePunten.length === 0 ? (
                  <p className="text-xs text-gray-400">Nog geen dieptepunten.</p>
                ) : (
                  <div className="flex flex-col gap-1.5">
                    {dieptePunten.map((p, i) => (
                      <div key={p.id} className="flex items-center gap-2 text-xs py-1.5 px-2 rounded-lg bg-blue-50">
                        <span className="text-blue-600 font-bold w-4">{i+1}</span>
                        <span className="text-gray-500">{p.id === "start" ? "↘" : p.id === "eind" ? "↗" : "◆"}</span>
                        <span className="text-gray-600 flex-1">{p.diepte}m</span>
                        <span className="text-gray-400">{p.id === "eind" ? "eind" : `${p.positieM ?? 0}m`}</span>
                      </div>
                    ))}
                  </div>
                )}
                <div className="text-xs text-gray-300 leading-relaxed">Gebruik "+ Dieptepunt toevoegen" in het dwarsprofiel om punten toe te voegen en te slepen.</div>
              </div>
            )}

            {/* Opstelling tab */}
            {kaartTab === "opstelling" && (
              <div className="p-4 flex flex-col gap-3">
                <div className="text-xs text-gray-400 font-medium uppercase tracking-wide mb-1">Opstelling intekenen</div>
                <p className="text-xs text-gray-400 leading-relaxed">Klik op de kaart om de hoekpunten van elk vlak te tekenen.</p>

                <div className="flex flex-col gap-2">
                  <button
                    onClick={() => setModus(modus === "boor_machine" ? "niets" : "boor_machine")}
                    className={`flex items-center gap-2 text-xs font-medium px-3 py-2.5 rounded-lg border transition-colors ${
                      modus === "boor_machine" ? "bg-orange-500 text-white border-orange-500" : "text-orange-600 border-orange-200 hover:bg-orange-50"
                    }`}>
                    <span className="w-4 h-4 bg-orange-500 rounded-sm inline-block opacity-70" />
                    {modus === "boor_machine" ? "✓ Klik 2 hoekpunten..." : "HDD Boormachine tekenen"}
                  </button>

                  <button
                    onClick={() => setModus(modus === "bentoniet_tank" ? "niets" : "bentoniet_tank")}
                    className={`flex items-center gap-2 text-xs font-medium px-3 py-2.5 rounded-lg border transition-colors ${
                      modus === "bentoniet_tank" ? "bg-amber-600 text-white border-amber-600" : "text-amber-700 border-amber-200 hover:bg-amber-50"
                    }`}>
                    <span className="w-4 h-4 bg-amber-600 rounded-sm inline-block opacity-70" />
                    {modus === "bentoniet_tank" ? "✓ Klik 2 hoekpunten..." : "Bentoniet tank tekenen"}
                  </button>
                </div>

                {(opstellingRef.current.boorMachine || opstellingRef.current.bentonietTank) && (
                  <div className="flex flex-col gap-1 mt-2">
                    <div className="text-xs text-gray-400 font-medium mb-1">Ingetekend:</div>
                    {opstellingRef.current.boorMachine && (
                      <div className="flex items-center gap-2 text-xs py-1.5 px-2 rounded-lg bg-orange-50 text-orange-700">
                        <span>🔶</span><span className="flex-1">Boormachine</span>
                        <button onClick={() => {
                          const kaart = leafletMapRef.current;
                          if (opstellingRef.current.boorMachine?._layer) kaart?.removeLayer(opstellingRef.current.boorMachine._layer);
                          opstellingRef.current.boorMachine = null;
                          setKaartTab("opstelling");
                        }} className="text-red-400 hover:text-red-600 ml-1">✕</button>
                      </div>
                    )}
                    {opstellingRef.current.bentonietTank && (
                      <div className="flex items-center gap-2 text-xs py-1.5 px-2 rounded-lg bg-amber-50 text-amber-700">
                        <span>🟫</span><span className="flex-1">Bentoniet tank</span>
                        <button onClick={() => {
                          const kaart = leafletMapRef.current;
                          if (opstellingRef.current.bentonietTank?._layer) kaart?.removeLayer(opstellingRef.current.bentonietTank._layer);
                          opstellingRef.current.bentonietTank = null;
                          setKaartTab("opstelling");
                        }} className="text-red-400 hover:text-red-600 ml-1">✕</button>
                      </div>
                    )}
                  </div>
                )}

                <div className="text-xs text-gray-300 leading-relaxed mt-2">Klik twee diagonaal tegenoverliggende hoekpunten om een rechthoek te tekenen.</div>
              </div>
            )}

          </div>
          {kaartTab === "analyse" && analysePunten.length > 0 && (
            <div className="px-4 py-3 border-t border-gray-100 bg-gray-50">
              <div className="text-xs text-gray-400 font-medium mb-1.5">Herstelklasse</div>
              {[{ label: "Hoog — asfalt/beton", icoon: "🔴" }, { label: "Midden — klinkers", icoon: "🟠" }, { label: "Laag — gras/onverhard", icoon: "🟢" }].map((r,i) => (
                <div key={i} className="flex items-center gap-1.5 text-xs text-gray-500 mb-0.5"><span>{r.icoon}</span><span>{r.label}</span></div>
              ))}
            </div>
          )}
        </div>

        {kaartTab === "analyse" && analysePunten.length > 0 && (
          <div className="bg-blue-50 border border-blue-100 rounded-xl p-3">
            <p className="text-xs text-blue-700 leading-relaxed">💡 Vraag de AI Assistent om hersteladvies en kostenraming per oppervlaktype.</p>
          </div>
        )}
      </div>

      {/* Verwijder popup */}
      {toonVerwijderPopup && (
        <div className="fixed inset-0 bg-black/30 flex items-center justify-center z-[9999]">
          <div className="bg-white rounded-xl shadow-xl p-6 max-w-sm w-full mx-4">
            <div className="text-2xl mb-3 text-center">🗑</div>
            <h3 className="text-sm font-semibold text-gray-900 text-center mb-2">Boorlijn verwijderen?</h3>
            <p className="text-xs text-gray-500 text-center mb-5 leading-relaxed">De boorlijn, alle analysepunten en het dwarsprofiel worden permanent verwijderd.</p>
            <div className="flex gap-3">
              <button onClick={() => setToonVerwijderPopup(false)} className="flex-1 border border-gray-200 text-gray-500 text-sm py-2.5 rounded-lg hover:bg-gray-50">Annuleren</button>
              <button
                onClick={async () => { setVerwijderBezig(true); await onTraceOpgeslagen(null); wisAlles(); setToonVerwijderPopup(false); setVerwijderBezig(false); }}
                disabled={verwijderBezig}
                className="flex-1 bg-red-500 hover:bg-red-600 text-white text-sm font-semibold py-2.5 rounded-lg transition-colors disabled:opacity-50"
              >
                {verwijderBezig ? "Bezig..." : "Verwijderen"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
