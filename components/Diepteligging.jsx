"use client";
import { useEffect, useRef, useState, useCallback, useMemo } from "react";

// ─── Geometry helpers ─────────────────────────────────────────────
function afstandM(p1,p2){const R=6371000,dLat=(p2[0]-p1[0])*Math.PI/180,dLng=(p2[1]-p1[1])*Math.PI/180,a=Math.sin(dLat/2)**2+Math.cos(p1[0]*Math.PI/180)*Math.cos(p2[0]*Math.PI/180)*Math.sin(dLng/2)**2;return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));}
function cumulatiefAfstanden(coords){const r=[0];for(let i=1;i<coords.length;i++)r.push(r[i-1]+afstandM(coords[i-1],coords[i]));return r;}
function totaalLengte(coords){return coords.length<2?0:coords.reduce((s,_,i)=>i===0?0:s+afstandM(coords[i-1],coords[i]),0);}

function interpoleerLijn(coords,stap=5){
  if(coords.length<2)return[];
  const cumul=cumulatiefAfstanden(coords);
  const tot=cumul[cumul.length-1];
  const punten=[];
  for(let d=0;d<=tot+0.1;d+=stap){
    const dd=Math.min(d,tot);
    let seg=cumul.findIndex((c,i)=>i>0&&cumul[i]>=dd)-1;
    if(seg<0)seg=coords.length-2;
    const segLen=cumul[seg+1]-cumul[seg];
    const t=segLen<0.001?0:(dd-cumul[seg])/segLen;
    punten.push({lat:coords[seg][0]+t*(coords[seg+1][0]-coords[seg][0]),lng:coords[seg][1]+t*(coords[seg+1][1]-coords[seg][1]),afstand:dd});
  }
  return punten;
}

// Geografische positie op `afstand` meter langs boorlijn
function positieOpAfstand(coords,afstand){
  const cumul=cumulatiefAfstanden(coords);
  const tot=cumul[cumul.length-1];
  const dd=Math.max(0,Math.min(afstand,tot));
  let seg=cumul.findIndex((c,i)=>i>0&&cumul[i]>=dd)-1;
  if(seg<0)seg=coords.length-2;
  const segLen=cumul[seg+1]-cumul[seg];
  const t=segLen<0.001?0:(dd-cumul[seg])/segLen;
  return{lat:coords[seg][0]+t*(coords[seg+1][0]-coords[seg][0]),lng:coords[seg][1]+t*(coords[seg+1][1]-coords[seg][1])};
}

// Interpoleer diepte (m onder maaiveld) op afstand via dieptepunten
function interpoleerDiepte(afstand,dieptePunten){
  if(!dieptePunten.length)return 0;
  const sorted=[...dieptePunten].sort((a,b)=>a.afstand-b.afstand);
  if(afstand<=sorted[0].afstand)return sorted[0].diepte;
  if(afstand>=sorted[sorted.length-1].afstand)return sorted[sorted.length-1].diepte;
  let i=0;while(i<sorted.length-1&&sorted[i+1].afstand<afstand)i++;
  const a=sorted[i],b=sorted[i+1];
  const t=(afstand-a.afstand)/(b.afstand-a.afstand);
  return a.diepte+t*(b.diepte-a.diepte);
}

// WGS84 → RD New
function latLngNaarRD(lat,lng){
  const dLat=0.36*(lat-52.15517440),dLon=0.36*(lng-5.38720621);
  return{x:155000+190094.945*dLon-11832.228*dLon*dLat-114.221*dLon*dLat*dLat-32.391*dLon*dLon*dLon,
         y:463000+309056.544*dLat+60940.388*dLon*dLon*dLat-9.941*dLon*dLon-2.340*dLat*dLat*dLat};
}

// ─── KLIC types ───────────────────────────────────────────────────
const KLIC_TYPES={ls:{label:"LS",kleur:"#ef4444",diepte:0.60},ms:{label:"MS",kleur:"#f97316",diepte:0.80},gas:{label:"Gas",kleur:"#eab308",diepte:0.80},water:{label:"Water",kleur:"#3b82f6",diepte:1.00},tele:{label:"Tele",kleur:"#8b5cf6",diepte:0.45},riool:{label:"Riool",kleur:"#6b7280",diepte:1.20}};
function detecteerKlicType(f){const n=(f.properties?.naam||f.properties?.thema||"").toLowerCase();if(n.includes("laagspan")||n.includes("ls "))return"ls";if(n.includes("middensp")||n.includes("ms "))return"ms";if(n.includes("gas"))return"gas";if(n.includes("water"))return"water";if(n.includes("tele")||n.includes("data")||n.includes("glas"))return"tele";if(n.includes("riool"))return"riool";return"tele";}

// Lijnsnijpunt voor KLIC
function segSnijpunt(p1,p2,p3,p4){const dx1=p2.x-p1.x,dy1=p2.y-p1.y,dx2=p4.x-p3.x,dy2=p4.y-p3.y,cross=dx1*dy2-dy1*dx2;if(Math.abs(cross)<1e-8)return null;const t=((p3.x-p1.x)*dy2-(p3.y-p1.y)*dx2)/cross,u=((p3.x-p1.x)*dy1-(p3.y-p1.y)*dx1)/cross;if(t>=0&&t<=1&&u>=0&&u<=1)return{t,x:p1.x+t*dx1,y:p1.y+t*dy1};return null;}

// ─── CRS ─────────────────────────────────────────────────────────
function maakRdCrs(L){return new L.Proj.CRS("EPSG:28992","+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 +x_0=155000 +y_0=463000 +ellps=bessel +towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 +units=m +no_defs",{resolutions:[3440.640,1720.320,860.160,430.080,215.040,107.520,53.760,26.880,13.440,6.720,3.360,1.680,0.840,0.420,0.210,0.105,0.0525,0.02625,0.013125,0.00656,0.00328,0.00164,0.00082],origin:[-285401.920,903401.920],bounds:L.bounds([-285401.920,22598.080],[595401.920,903401.920])});}

// ─── Dwarsprofiel SVG (interactief) ──────────────────────────────
function Dwarsprofiel({profielPunten,dieptePunten,setDieptePunten,klicKruisingen,totM,onHoverAfstand,onHoverLeave}){
  const svgRef=useRef(null);
  const dragRef=useRef(null); // {idx, startY, startDiepte}

  if(!profielPunten||profielPunten.length<2)return(
    <div className="flex items-center justify-center h-48 text-sm text-gray-400">
      Klik <strong className="mx-1">⛰ Analyseer hoogte</strong> om het profiel te laden
    </div>
  );
  const geldig=profielPunten.filter(p=>p.hoogte!==null);
  if(!geldig.length)return null;

  const M={l:64,r:24,t:28,b:40};
  const W=900,H=300;
  const plotW=W-M.l-M.r,plotH=H-M.t-M.b;

  // Y-bereik: maaiveld max + 0.5m boven, max diepte + 1m onder
  const maxDiepte=Math.max(...dieptePunten.map(p=>p.diepte),0)+1;
  const hMin=Math.min(...geldig.map(p=>p.hoogte))-maxDiepte-0.5;
  const hMax=Math.max(...geldig.map(p=>p.hoogte))+0.5;
  const hSpan=hMax-hMin||1;

  const xP=d=>M.l+d/totM*plotW;
  const yP=h=>M.t+(hMax-h)/hSpan*plotH;
  const yNaarDiepte=(svgY,maaiveld)=>maaiveld-(hMax-((svgY-M.t)/plotH*hSpan));

  // Boorpad: start en einde op maaiveld, intermediate punten op ingestelde diepte
  const boorPadPts=geldig.map(pp=>{
    const diepte=interpoleerDiepte(pp.afstand,dieptePunten);
    return{afstand:pp.afstand,hoogte:pp.hoogte-diepte};
  });
  const boorPolyline=boorPadPts.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");

  // Maaiveld polyline + vlak
  const maaiveldPts=geldig.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");
  const vlakPts=`${xP(geldig[0].afstand)},${H-M.b} ${maaiveldPts} ${xP(geldig[geldig.length-1].afstand)},${H-M.b}`;

  // Y-gridlijnen
  const stap=hSpan>8?2:hSpan>4?1:0.5;
  const yGrid=[];for(let h=Math.ceil(hMin/stap)*stap;h<=hMax;h+=stap)yGrid.push(h);

  // Dieptepunten voor weergave (zonder start/einde want die zitten altijd op maaiveld)
  const tussenPunten=dieptePunten.filter((_,i)=>i>0&&i<dieptePunten.length-1);

  // SVG coördinaat uit event
  function svgCoords(e){
    const r=svgRef.current.getBoundingClientRect();
    return{x:(e.clientX-r.left)/r.width*W,y:(e.clientY-r.top)/r.height*H};
  }

  function handleMouseMove(e){
    const{x,y}=svgCoords(e);
    const afstand=Math.max(0,Math.min(totM,(x-M.l)/plotW*totM));
    // Hover → positie op kaart
    onHoverAfstand?.(afstand);
    // Drag dieptepunt
    if(dragRef.current!==null){
      const{idx,maaiveldHoogte}=dragRef.current;
      const napHoogte=hMax-((y-M.t)/plotH*hSpan);
      const nieuweDiepte=Math.max(0,maaiveldHoogte-napHoogte);
      setDieptePunten(prev=>prev.map((p,i)=>i===idx?{...p,diepte:nieuweDiepte}:p));
    }
  }

  function handleMouseLeave(){
    onHoverLeave?.();
    dragRef.current=null;
  }

  function handleMouseUp(){dragRef.current=null;}

  function handleMouseDown(e,idx){
    e.stopPropagation();
    const{x}=svgCoords(e);
    const afstand=(x-M.l)/plotW*totM;
    // Zoek maaiveld hoogte op deze positie
    const pp=geldig.reduce((a,b)=>Math.abs(a.afstand-afstand)<Math.abs(b.afstand-afstand)?a:b);
    dragRef.current={idx,maaiveldHoogte:pp.hoogte};
  }

  // Klik op boorpad-lijn → voeg tussenpunt in
  function handleBoorpadKlik(e){
    if(dragRef.current!==null)return;
    const{x,y}=svgCoords(e);
    const afstand=Math.max(0.5,Math.min(totM-0.5,(x-M.l)/plotW*totM));
    // Controleer of er al een punt in de buurt is
    if(dieptePunten.some(p=>Math.abs(p.afstand-afstand)<totM*0.02))return;
    const pp=geldig.reduce((a,b)=>Math.abs(a.afstand-afstand)<Math.abs(b.afstand-afstand)?a:b);
    const napHoogte=hMax-((y-M.t)/plotH*hSpan);
    const diepte=Math.max(0,pp.hoogte-napHoogte);
    const nieuw=[...dieptePunten,{afstand,diepte}].sort((a,b)=>a.afstand-b.afstand);
    setDieptePunten(nieuw);
  }

  // Dubbelklik op tussenpunt → verwijder
  function handleDubbelKlik(e,idx){
    e.stopPropagation();
    setDieptePunten(prev=>prev.filter((_,i)=>i!==idx));
  }

  return(
    <div className="w-full overflow-x-auto select-none">
      <div className="text-xs text-gray-400 text-right pr-2 mb-1">
        💡 Klik op de oranje lijn om dieptepunten toe te voegen · sleep omhoog/omlaag · dubbelklik = verwijder
      </div>
      <svg ref={svgRef} viewBox={`0 0 ${W} ${H}`} className="w-full cursor-crosshair"
        style={{minWidth:600,height:300}}
        onMouseMove={handleMouseMove}
        onMouseLeave={handleMouseLeave}
        onMouseUp={handleMouseUp}>

        {/* Grid */}
        {yGrid.map(h=>(
          <g key={h}>
            <line x1={M.l} y1={yP(h)} x2={W-M.r} y2={yP(h)} stroke={h===0?"#93c5fd":"#e5e7eb"} strokeWidth={h===0?1.5:0.5}/>
            <text x={M.l-4} y={yP(h)+4} textAnchor="end" fontSize={9} fill="#9ca3af">{h.toFixed(1)}</text>
          </g>
        ))}
        {hMin<0&&hMax>0&&<text x={M.l-4} y={yP(0)+4} textAnchor="end" fontSize={8} fill="#93c5fd">NAP</text>}

        {/* Grondvlak */}
        <polygon points={vlakPts} fill="#bbf7d0" fillOpacity={0.45}/>
        <polyline points={maaiveldPts} fill="none" stroke="#16a34a" strokeWidth={2.5}/>
        <text x={M.l+4} y={M.t+11} fontSize={9} fill="#15803d" fontWeight="600">Maaiveld (AHN4)</text>

        {/* KLIC kruisingen */}
        {klicKruisingen.map((k,i)=>{
          const kt=KLIC_TYPES[k.type]??KLIC_TYPES.tele;
          const kx=xP(k.afstand);
          const ky=yP(k.hoogte-k.diepte);
          return(
            <g key={i}>
              <line x1={kx} y1={M.t} x2={kx} y2={H-M.b} stroke={kt.kleur} strokeWidth={1.5} strokeDasharray="5,3" opacity={0.7}/>
              <circle cx={kx} cy={ky} r={5} fill={kt.kleur} fillOpacity={0.9}/>
              <text x={kx+7} y={ky+4} fontSize={9} fill={kt.kleur} fontWeight="600">{kt.label}</text>
              <text x={kx+7} y={ky+14} fontSize={8} fill="#6b7280">-{k.diepte.toFixed(1)}m</text>
            </g>
          );
        })}

        {/* Boorpad — klikbaar om punten toe te voegen */}
        <polyline points={boorPolyline} fill="none" stroke="#f97316" strokeWidth={8} opacity={0}
          style={{cursor:"copy"}} onClick={handleBoorpadKlik}/>
        <polyline points={boorPolyline} fill="none" stroke="#f97316" strokeWidth={3}
          strokeDasharray="10,5" strokeLinecap="round" onClick={handleBoorpadKlik} style={{cursor:"copy"}}/>

        {/* Start/einde marker (op maaiveld) */}
        {geldig.length>0&&(()=>{
          const start=geldig[0],einde=geldig[geldig.length-1];
          return(
            <>
              <circle cx={xP(start.afstand)} cy={yP(start.hoogte)} r={6} fill="#16a34a" stroke="white" strokeWidth={2}/>
              <text x={xP(start.afstand)+8} y={yP(start.hoogte)-6} fontSize={9} fill="#15803d" fontWeight="700">S (maaiveld)</text>
              <circle cx={xP(einde.afstand)} cy={yP(einde.hoogte)} r={6} fill="#dc2626" stroke="white" strokeWidth={2}/>
              <text x={xP(einde.afstand)-8} y={yP(einde.hoogte)-6} fontSize={9} fill="#dc2626" fontWeight="700" textAnchor="end">E (maaiveld)</text>
            </>
          );
        })()}

        {/* Draggable dieptepunten (tussenliggende waypoints) */}
        {tussenPunten.map((dp,relIdx)=>{
          const absIdx=dieptePunten.findIndex((p,i)=>i>0&&i<dieptePunten.length-1&&Math.abs(p.afstand-dp.afstand)<0.01&&Math.abs(p.diepte-dp.diepte)<0.001);
          const pp=geldig.reduce((a,b)=>Math.abs(a.afstand-dp.afstand)<Math.abs(b.afstand-dp.afstand)?a:b);
          const napHoogte=pp.hoogte-dp.diepte;
          return(
            <g key={relIdx} style={{cursor:"ns-resize"}}
              onMouseDown={e=>handleMouseDown(e,absIdx)}
              onDoubleClick={e=>handleDubbelKlik(e,absIdx)}>
              {/* Verticale lijn tot maaiveld */}
              <line x1={xP(dp.afstand)} y1={yP(pp.hoogte)} x2={xP(dp.afstand)} y2={yP(napHoogte)}
                stroke="#f97316" strokeWidth={1.5} strokeDasharray="3,2" opacity={0.6}/>
              {/* Punt */}
              <circle cx={xP(dp.afstand)} cy={yP(napHoogte)} r={7} fill="#f97316" stroke="white" strokeWidth={2.5}/>
              {/* Diepte label */}
              <rect x={xP(dp.afstand)-18} y={yP(napHoogte)+10} width={36} height={14} rx={3} fill="white" fillOpacity={0.85}/>
              <text x={xP(dp.afstand)} y={yP(napHoogte)+21} textAnchor="middle" fontSize={9} fill="#ea580c" fontWeight="700">
                -{dp.diepte.toFixed(1)}m
              </text>
            </g>
          );
        })}

        {/* X-as labels */}
        {[0,0.25,0.5,0.75,1].map(f=>{
          const d=f*totM;
          return(
            <g key={f}>
              <line x1={xP(d)} y1={H-M.b} x2={xP(d)} y2={H-M.b+4} stroke="#9ca3af"/>
              <text x={xP(d)} y={H-M.b+14} textAnchor="middle" fontSize={9} fill="#9ca3af">
                {d<1?"0":d>=1000?`${(d/1000).toFixed(1)}km`:`${Math.round(d)}m`}
              </text>
            </g>
          );
        })}
        <text x={M.l-42} y={H/2} fontSize={10} fill="#6b7280" transform={`rotate(-90,${M.l-42},${H/2})`} textAnchor="middle">Hoogte (m NAP)</text>
        <text x={W/2} y={H-2} textAnchor="middle" fontSize={10} fill="#6b7280">Afstand langs boorlijn (m)</text>
        <rect x={M.l} y={M.t} width={plotW} height={plotH} fill="none" stroke="#e5e7eb" strokeWidth={1}/>
      </svg>
    </div>
  );
}

// ─── Hoofd-component ──────────────────────────────────────────────
export default function Diepteligging({project,onNaar,opgeslagenDiepte,onSave}){
  const mapRef=useRef(null);
  const kaartRef=useRef(null);
  const basisLaagRef=useRef(null);
  const boorCoordRef=useRef([]);

  const [boorCoords,setBoorCoords]=useState(()=>{
    try{const g=project?.boortrace_geojson;if(!g)return[];const p=typeof g==="string"?JSON.parse(g):g;return p.coordinates?.map(([lng,lat])=>[lat,lng])??[];}catch{return[];}
  });

  const totM=useMemo(()=>boorCoords.length>=2?totaalLengte(boorCoords):0,[boorCoords]);

  // Dieptepunten: start en einde ALTIJD op maaiveld (diepte=0), tussenliggende vrij
  const [dieptePunten,setDieptePunten]=useState(()=>{
    try{const s=project?.diepte_profiel;if(s){const p=typeof s==="string"?JSON.parse(s):s;if(Array.isArray(p)&&p.length>=2)return p;}
    }catch{}
    // Default: alleen start en einde op maaiveld
    return [{afstand:0,diepte:0},{afstand:0,diepte:0}]; // wordt bijgewerkt na totM
  });

  // Update start/einde afstand als totM verandert
  useEffect(()=>{
    if(totM>0){
      setDieptePunten(prev=>{
        const pts=[...prev];
        pts[0]={afstand:0,diepte:0};
        pts[pts.length-1]={afstand:totM,diepte:0};
        return pts;
      });
    }
  },[totM]);

  // AHN profiel
  const [profielPunten,setProfielPunten]=useState(()=>{
    try{const s=project?.ahn_profiel;if(!s)return[];const p=typeof s==="string"?JSON.parse(s):s;return Array.isArray(p)?p:[];}catch{return[];}
  });
  const [hoogteBezig,setHoogteBezig]=useState(false);
  const [hoogteInfo,setHoogteInfo]=useState(()=>{
    try{const p=typeof project?.ahn_profiel==="string"?JSON.parse(project.ahn_profiel):project?.ahn_profiel;if(!Array.isArray(p)||!p.length)return null;const g=p.filter(x=>x.hoogte!==null);return g.length?`${g.length}/${p.length} punten (opgeslagen) · ${Math.min(...g.map(x=>x.hoogte)).toFixed(2)}–${Math.max(...g.map(x=>x.hoogte)).toFixed(2)}m NAP`:null;}catch{return null;}
  });

  // KLIC kruisingen
  const [klicKruisingen,setKlicKruisingen]=useState([]);

  boorCoordRef.current=boorCoords;

  // Bereken KLIC kruisingen
  useEffect(()=>{
    if(boorCoords.length<2)return;
    try{
      const klicSets=[];
      ["klic_ls","klic_ms","klic_gas","klic_water","klic_tele","klic_riool"].forEach(k=>{const raw=project?.[k];if(!raw)return;const gj=typeof raw==="string"?JSON.parse(raw):raw;(gj.features??[gj]).forEach(f=>{if(f?.geometry)klicSets.push({...f,_klicKey:k});});});
      try{const ss=sessionStorage.getItem("klic_features");if(ss)JSON.parse(ss).forEach(f=>klicSets.push(f));}catch{}
      const kruisingen=[];
      const cumul=cumulatiefAfstanden(boorCoords);
      klicSets.forEach(feat=>{
        const type=detecteerKlicType(feat);
        const kType=KLIC_TYPES[type]??KLIC_TYPES.tele;
        const geom=feat.geometry;if(!geom?.coordinates)return;
        const klicSegs=[];
        const flatten=coords=>{for(let i=0;i<coords.length-1;i++){const a=coords[i],b=coords[i+1];if(Array.isArray(a[0])){flatten(coords);return;}const rdA=latLngNaarRD(a[1]??a[0],a[0]??a[1]),rdB=latLngNaarRD(b[1]??b[0],b[0]??b[1]);klicSegs.push([rdA,rdB]);}};
        if(geom.type==="LineString")flatten(geom.coordinates);
        else if(geom.type==="MultiLineString")geom.coordinates.forEach(flatten);
        for(let bi=0;bi<boorCoords.length-1;bi++){
          const bA=latLngNaarRD(boorCoords[bi][0],boorCoords[bi][1]),bB=latLngNaarRD(boorCoords[bi+1][0],boorCoords[bi+1][1]),segLen=afstandM(boorCoords[bi],boorCoords[bi+1]);
          for(const[kA,kB]of klicSegs){const sn=segSnijpunt(bA,bB,kA,kB);if(sn){const afstand=cumul[bi]+sn.t*segLen,pp=profielPunten.find(p=>Math.abs(p.afstand-afstand)<2.5),hoogte=pp?.hoogte??0;kruisingen.push({afstand,hoogte,type,diepte:kType.diepte,label:kType.label,kleur:kType.kleur});}}
        }
      });
      const uniek=kruisingen.filter((k,i)=>!kruisingen.slice(0,i).some(k2=>Math.abs(k2.afstand-k.afstand)<3&&k2.type===k.type));
      setKlicKruisingen(uniek.sort((a,b)=>a.afstand-b.afstand));
    }catch(e){console.warn("KLIC kruisingen:",e);}
  },[boorCoords,profielPunten,project]);

  // Kaart init
  useEffect(()=>{
    if(typeof window==="undefined"||kaartRef.current||!mapRef.current)return;
    let actief=true;
    (async()=>{
      const ls=src=>new Promise((ok,er)=>{if(document.querySelector(`script[src="${src}"]`))return ok();const s=document.createElement("script");s.src=src;s.onload=ok;s.onerror=er;document.head.appendChild(s);});
      if(!document.querySelector('link[href*="leaflet"]')){const c=document.createElement("link");c.rel="stylesheet";c.href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";document.head.appendChild(c);}
      await ls("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js");
      await ls("https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js");
      await ls("https://cdnjs.cloudflare.com/ajax/libs/proj4leaflet/1.0.2/proj4leaflet.js");
      if(!actief||!mapRef.current)return;
      const L=window.L;
      const crs=maakRdCrs(L);
      const center=boorCoordRef.current[0]??[52.15,5.39];
      const kaart=L.map(mapRef.current,{crs,center,zoom:14,maxZoom:22,zoomControl:true});
      kaartRef.current=kaart;
      basisLaagRef.current=L.tileLayer("https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png",{maxZoom:22,maxNativeZoom:13,tileSize:256,attribution:"© PDOK BRT",zIndex:1}).addTo(kaart);
      L.tileLayer.wms("https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0",{layers:"buisleiding",format:"image/png",transparent:true,opacity:0.8,zIndex:10,attribution:"© Kadaster KLIC"}).addTo(kaart);

      // Hover marker
      let hoverMk=null;
      kaart._zetHoverMarker=(lat,lng)=>{
        if(hoverMk)hoverMk.setLatLng([lat,lng]);
        else{hoverMk=L.circleMarker([lat,lng],{radius:9,fillColor:"#f97316",fillOpacity:0.9,color:"white",weight:2.5,interactive:false,zIndexOffset:999}).addTo(kaart);}
      };
      kaart._verwijderHoverMarker=()=>{if(hoverMk){kaart.removeLayer(hoverMk);hoverMk=null;}};

      // Bewerkbare boorlijn
      let boorPoly=null,editMks=[],tussenMks=[],isDrag=false;
      function markerIcon(kleur,groot){const sz=groot?20:14;return L.divIcon({html:`<div style="width:${sz}px;height:${sz}px;border-radius:50%;background:${kleur};border:2.5px solid white;box-shadow:0 1px 4px rgba(0,0,0,.35);cursor:grab"></div>`,className:"",iconSize:[sz,sz],iconAnchor:[sz/2,sz/2]});}
      function tussenIcon(){return L.divIcon({html:`<div style="width:10px;height:10px;border-radius:50%;background:#f97316;border:2px solid white;opacity:0.55"></div>`,className:"",iconSize:[10,10],iconAnchor:[5,5]});}
      function updateKaartLaag(){
        editMks.forEach(m=>kaart.removeLayer(m));editMks=[];
        tussenMks.forEach(m=>kaart.removeLayer(m));tussenMks=[];
        if(boorPoly){kaart.removeLayer(boorPoly);boorPoly=null;}
        const coords=boorCoordRef.current;if(!coords.length)return;
        if(coords.length>=2)boorPoly=L.polyline(coords,{color:"#f97316",weight:4,opacity:0.9,lineCap:"round"}).addTo(kaart);
        coords.forEach((coord,idx)=>{
          const isS=idx===0,isE=idx===coords.length-1;
          const mk=L.marker(coord,{draggable:true,icon:markerIcon(isS?"#16a34a":isE?"#dc2626":"#f97316",isS||isE),zIndexOffset:isS||isE?200:100}).addTo(kaart);
          mk.on("drag",e=>{isDrag=true;boorCoordRef.current[idx]=[e.latlng.lat,e.latlng.lng];if(boorPoly)boorPoly.setLatLngs(boorCoordRef.current);});
          mk.on("dragend",()=>{setBoorCoords([...boorCoordRef.current]);setTimeout(()=>{isDrag=false;},100);});
          mk.on("dblclick",e=>{L.DomEvent.stop(e);if(isS||isE||boorCoordRef.current.length<=2)return;boorCoordRef.current.splice(idx,1);setBoorCoords([...boorCoordRef.current]);});
          editMks.push(mk);
        });
        if(coords.length>=2){for(let i=0;i<coords.length-1;i++){const midLat=(coords[i][0]+coords[i+1][0])/2,midLng=(coords[i][1]+coords[i+1][1])/2;const tm=L.marker([midLat,midLng],{icon:tussenIcon(),zIndexOffset:50}).addTo(kaart);const ci=i;tm.on("click",e=>{if(isDrag)return;L.DomEvent.stop(e);boorCoordRef.current.splice(ci+1,0,[midLat,midLng]);setBoorCoords([...boorCoordRef.current]);});tussenMks.push(tm);}}
      }
      kaart._updateBoorLaag=(coords)=>{boorCoordRef.current=coords;updateKaartLaag();};
      updateKaartLaag();
      if(boorCoordRef.current.length>=2)try{kaart.fitBounds(L.latLngBounds(boorCoordRef.current).pad(0.15),{maxZoom:16});}catch{}
    })();
    return()=>{actief=false;if(kaartRef.current){try{kaartRef.current.remove();}catch{}kaartRef.current=null;}};
  },[]);

  useEffect(()=>{kaartRef.current?._updateBoorLaag?.(boorCoords);},[boorCoords]);

  // Hover callback
  const handleHoverAfstand=useCallback((afstand)=>{
    if(boorCoords.length<2)return;
    const pos=positieOpAfstand(boorCoords,afstand);
    kaartRef.current?._zetHoverMarker?.(pos.lat,pos.lng);
  },[boorCoords]);

  // AHN ophalen
  const haalHoogteOp=useCallback(async()=>{
    if(boorCoords.length<2)return;
    setHoogteBezig(true);setHoogteInfo("Bezig met ophalen…");
    try{
      const punten=interpoleerLijn(boorCoords,5);
      const rdPunten=punten.map(p=>{const rd=latLngNaarRD(p.lat,p.lng);return{x:rd.x,y:rd.y};});
      const res=await fetch("/api/ahn-hoogte",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({punten:rdPunten})});
      if(!res.ok){const t=await res.text().catch(()=>"");throw new Error(`HTTP ${res.status}${t?" — "+t.slice(0,80):""}`);}
      const data=await res.json();
      const metHoogte=punten.map((p,i)=>({...p,hoogte:data.hoogtes?.[i]??null}));
      setProfielPunten(metHoogte);
      const geldig=metHoogte.filter(p=>p.hoogte!==null);
      setHoogteInfo(geldig.length?`${geldig.length}/${punten.length} punten · ${Math.min(...geldig.map(p=>p.hoogte)).toFixed(2)}–${Math.max(...geldig.map(p=>p.hoogte)).toFixed(2)}m NAP`:"❌ Geen hoogte-data — controleer /api/ahn-hoogte");
      if(geldig.length&&onSave)try{await onSave({ahn_profiel:metHoogte});}catch(e){console.warn("AHN opslaan:",e);}
    }catch(e){setHoogteInfo(`❌ ${e.message}`);}
    setHoogteBezig(false);
  },[boorCoords,onSave]);

  return(
    <div className="space-y-4">
      <div className="flex gap-4" style={{height:"calc(100vh - 260px)",minHeight:420}}>
        {/* Sidebar */}
        <div className="w-72 flex-shrink-0 bg-white border border-gray-200 rounded-xl overflow-y-auto flex flex-col">
          <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-100">
            <div><span className="text-sm font-semibold text-gray-900">6. Diepteligging</span><div className="text-xs text-gray-400">Dwarsprofiel & bodem</div></div>
          </div>
          <div className="flex-1 overflow-y-auto px-4 py-3 space-y-4">
            <div className="bg-orange-50 rounded-lg px-3 py-2">
              <div className="flex items-center gap-2 mb-1"><div className="w-2 h-2 rounded-full bg-orange-500"/><span className="text-xs font-semibold text-orange-700">Boorlijn</span></div>
              <div className="text-xs text-orange-600">{boorCoords.length>=2?`${Math.round(totM)}m · ${boorCoords.length} punten`:"Geen boorlijn"}</div>
              <div className="text-xs text-gray-400 mt-0.5">🟢 Start op maaiveld · 🔴 Einde op maaiveld · ⚪ tussenpunt invoegen · dubbelklik = verwijder</div>
            </div>

            <div className="bg-blue-50 rounded-lg px-3 py-2">
              <div className="text-xs font-semibold text-blue-700 mb-1">Boorpad dieptepunten</div>
              <div className="text-xs text-blue-600">
                {dieptePunten.filter((_,i)=>i>0&&i<dieptePunten.length-1).length===0
                  ?"Start en einde liggen op maaiveld. Klik op de oranje lijn in het profiel om dieptepunten toe te voegen."
                  :`${dieptePunten.length-2} tussenpunt${dieptePunten.length-2!==1?"en":""} · sleep omhoog/omlaag · dubbelklik = verwijder`}
              </div>
              {dieptePunten.filter((_,i)=>i>0&&i<dieptePunten.length-1).length>0&&(
                <div className="mt-1.5 space-y-0.5">
                  {dieptePunten.filter((_,i)=>i>0&&i<dieptePunten.length-1).map((dp,i)=>(
                    <div key={i} className="flex items-center justify-between text-xs text-gray-600">
                      <span className="text-gray-400">@{Math.round(dp.afstand)}m</span>
                      <span className="font-semibold text-orange-600">-{dp.diepte.toFixed(2)}m</span>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <button onClick={haalHoogteOp} disabled={hoogteBezig||boorCoords.length<2}
              className={`w-full py-2.5 rounded-xl text-sm font-semibold transition-all ${hoogteBezig||boorCoords.length<2?"bg-gray-100 text-gray-400 cursor-not-allowed":"bg-orange-500 hover:bg-orange-600 text-white shadow-sm"}`}>
              {hoogteBezig?<span className="flex items-center justify-center gap-2"><span className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"/>Bezig…</span>:"⛰ Analyseer hoogte (AHN4)"}
            </button>
            {hoogteInfo&&<div className={`text-xs rounded-lg px-3 py-2 leading-snug ${hoogteInfo.startsWith("❌")?"bg-red-50 text-red-600":"bg-green-50 text-green-700"}`}>{hoogteInfo}</div>}

            {klicKruisingen.length>0&&(
              <div>
                <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">KLIC kruisingen ({klicKruisingen.length})</div>
                <div className="space-y-0.5">
                  {klicKruisingen.map((k,i)=>{const kt=KLIC_TYPES[k.type]??KLIC_TYPES.tele;return(
                    <div key={i} className="flex items-center gap-2 text-xs py-1 border-b border-gray-50">
                      <div className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{background:kt.kleur}}/>
                      <span className="font-medium text-gray-700">{kt.label}</span>
                      <span className="text-gray-400 flex-1">@{Math.round(k.afstand)}m</span>
                      <span className="text-gray-500">-{k.diepte.toFixed(1)}m</span>
                    </div>
                  );})}
                </div>
              </div>
            )}

            <div className="border-t border-gray-100 pt-3">
              <button onClick={()=>{if(boorCoords.length<2)return;const gj={type:"Feature",geometry:{type:"LineString",coordinates:boorCoords.map(([lat,lng])=>[lng,lat])},properties:{dieptePunten}};const blob=new Blob([JSON.stringify(gj,null,2)],{type:"application/json"});const a=document.createElement("a");a.href=URL.createObjectURL(blob);a.download="boorlijn_diepte.geojson";a.click();}}
                className="w-full py-2 rounded-lg border border-gray-200 text-xs text-gray-500 hover:bg-gray-50">⬇ Download GeoJSON</button>
            </div>
          </div>
        </div>

        {/* Kaart */}
        <div className="flex-1 min-w-0 rounded-xl border border-gray-200 overflow-hidden shadow-sm relative">
          <div ref={mapRef} className="w-full h-full"/>
          <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-[400] pointer-events-none">
            <div className="bg-white/90 backdrop-blur-sm rounded-full px-3 py-1 text-xs text-gray-500 shadow border border-gray-100">
              🟢 Start · 🔴 Einde · ⚪ tussenpunt invoegen · dubbelklik = verwijder · 🟠 = positie cursor in profiel
            </div>
          </div>
        </div>
      </div>

      {/* Dwarsprofiel */}
      <div className="bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100">
          <h3 className="text-sm font-semibold text-gray-900">⛰ Dwarsprofiel langs boorlijn</h3>
          <div className="flex items-center gap-3 text-xs text-gray-400">
            {profielPunten.length>0&&<span>{profielPunten.filter(p=>p.hoogte!==null).length} meetpunten · AHN4</span>}
            {klicKruisingen.length>0&&<span>{klicKruisingen.length} KLIC kruisingen</span>}
          </div>
        </div>
        <div className="p-3">
          <Dwarsprofiel
            profielPunten={profielPunten}
            dieptePunten={dieptePunten}
            setDieptePunten={setDieptePunten}
            klicKruisingen={klicKruisingen}
            totM={totM}
            onHoverAfstand={handleHoverAfstand}
            onHoverLeave={()=>kaartRef.current?._verwijderHoverMarker?.()}
          />
        </div>
        {klicKruisingen.length>0&&(
          <div className="px-4 pb-3 flex flex-wrap gap-3">
            {Object.entries(KLIC_TYPES).map(([k,v])=>{if(!klicKruisingen.some(kr=>kr.type===k))return null;return(<div key={k} className="flex items-center gap-1.5 text-xs text-gray-600"><div className="w-3 h-0.5 rounded" style={{background:v.kleur}}/><div className="w-2 h-2 rounded-full" style={{background:v.kleur}}/>{v.label} (-{v.diepte.toFixed(1)}m nom.)</div>);})}
            <div className="text-xs text-gray-400 ml-auto">* Nominale kabeldiepten (KLIC-standaard)</div>
          </div>
        )}
      </div>
    </div>
  );
}
