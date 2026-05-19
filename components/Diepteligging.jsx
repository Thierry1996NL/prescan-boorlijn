"use client";
import { useEffect, useRef, useState, useCallback } from "react";

// ─── Geometry helpers ─────────────────────────────────────────────
function afstandM(p1, p2) {
  const R = 6371000;
  const dLat = (p2[0]-p1[0])*Math.PI/180;
  const dLng = (p2[1]-p1[1])*Math.PI/180;
  const a = Math.sin(dLat/2)**2 + Math.cos(p1[0]*Math.PI/180)*Math.cos(p2[0]*Math.PI/180)*Math.sin(dLng/2)**2;
  return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
}

function cumulatiefAfstanden(coords) {
  const r=[0];
  for(let i=1;i<coords.length;i++) r.push(r[i-1]+afstandM(coords[i-1],coords[i]));
  return r;
}

function totaalLengte(coords) {
  return coords.length<2?0:coords.reduce((s,_,i)=>i===0?0:s+afstandM(coords[i-1],coords[i]),0);
}

// Interpoleer punten elke `stap` meter langs de boorlijn
function interpoleerLijn(coords, stap=5) {
  if(coords.length<2) return [];
  const cumul=cumulatiefAfstanden(coords);
  const tot=cumul[cumul.length-1];
  const punten=[];
  for(let d=0;d<=tot+0.1;d+=stap){
    const dd=Math.min(d,tot);
    let seg=cumul.findIndex((c,i)=>i>0&&cumul[i]>=dd)-1;
    if(seg<0)seg=coords.length-2;
    const segLen=cumul[seg+1]-cumul[seg];
    const t=segLen<0.001?0:(dd-cumul[seg])/segLen;
    punten.push({
      lat:coords[seg][0]+t*(coords[seg+1][0]-coords[seg][0]),
      lng:coords[seg][1]+t*(coords[seg+1][1]-coords[seg][1]),
      afstand:dd,
    });
  }
  return punten;
}

// WGS84 → RD New (polynoom)
function latLngNaarRD(lat,lng){
  const dLat=0.36*(lat-52.15517440),dLon=0.36*(lng-5.38720621);
  return{
    x:155000+190094.945*dLon-11832.228*dLon*dLat-114.221*dLon*dLat*dLat-32.391*dLon*dLon*dLon,
    y:463000+309056.544*dLat+60940.388*dLon*dLon*dLat-9.941*dLon*dLon-2.340*dLat*dLat*dLat,
  };
}

// Snijpunt tussen twee lijnsegmenten (2D in RD meters)
function segSnijpunt(p1,p2,p3,p4){
  const dx1=p2.x-p1.x,dy1=p2.y-p1.y,dx2=p4.x-p3.x,dy2=p4.y-p3.y;
  const cross=dx1*dy2-dy1*dx2;
  if(Math.abs(cross)<1e-8)return null;
  const t=((p3.x-p1.x)*dy2-(p3.y-p1.y)*dx2)/cross;
  const u=((p3.x-p1.x)*dy1-(p3.y-p1.y)*dx1)/cross;
  if(t>=0&&t<=1&&u>=0&&u<=1)return{t,x:p1.x+t*dx1,y:p1.y+t*dy1};
  return null;
}

// ─── KLIC kleuren en diepten ──────────────────────────────────────
const KLIC_TYPES = {
  ls:    { label:"LS",   kleur:"#ef4444", diepte:0.60 },
  ms:    { label:"MS",   kleur:"#f97316", diepte:0.80 },
  gas:   { label:"Gas",  kleur:"#eab308", diepte:0.80 },
  water: { label:"Water",kleur:"#3b82f6", diepte:1.00 },
  tele:  { label:"Tele", kleur:"#8b5cf6", diepte:0.45 },
  riool: { label:"Riool",kleur:"#6b7280", diepte:1.20 },
};

function detecteerKlicType(feature) {
  const naam=(feature.properties?.naam||feature.properties?.thema||
              feature.properties?.kabelsoort||"").toLowerCase();
  if(naam.includes("laagspan")||naam.includes("ls "))return "ls";
  if(naam.includes("middensp")||naam.includes("ms ")||naam.includes("elektrici"))return "ms";
  if(naam.includes("gas"))return "gas";
  if(naam.includes("water")||naam.includes("drinkw"))return "water";
  if(naam.includes("tele")||naam.includes("data")||naam.includes("glas"))return "tele";
  if(naam.includes("riool")||naam.includes("afvalw"))return "riool";
  return "tele"; // default
}

// ─── RD CRS voor Leaflet ─────────────────────────────────────────
function maakRdCrs(L){
  return new L.Proj.CRS("EPSG:28992",
    "+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 +x_0=155000 +y_0=463000 +ellps=bessel +towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 +units=m +no_defs",
    {resolutions:[3440.640,1720.320,860.160,430.080,215.040,107.520,53.760,26.880,13.440,6.720,3.360,1.680,0.840,0.420,0.210,0.105,0.0525,0.02625,0.013125,0.00656,0.00328,0.00164,0.00082],
     origin:[-285401.920,903401.920],bounds:L.bounds([-285401.920,22598.080],[595401.920,903401.920])}
  );
}

// ─── Dwarsprofiel SVG ─────────────────────────────────────────────
function Dwarsprofiel({ profielPunten, insteekDiepte, uitkomstDiepte, klicKruisingen, totM }) {
  if(!profielPunten||profielPunten.length<2) return (
    <div className="flex items-center justify-center h-48 text-sm text-gray-400">
      Klik op <strong className="mx-1">Analyseer hoogte</strong> om het dwarsprofiel te laden
    </div>
  );

  const geldigePunten=profielPunten.filter(p=>p.hoogte!==null);
  if(!geldigePunten.length) return null;

  const M={l:64,r:20,t:24,b:36};
  const W=900,H=280;
  const plotW=W-M.l-M.r,plotH=H-M.t-M.b;

  const hMin=Math.min(...geldigePunten.map(p=>p.hoogte))-(Math.max(insteekDiepte,uitkomstDiepte)+1.5);
  const hMax=Math.max(...geldigePunten.map(p=>p.hoogte))+1.2;
  const hSpan=hMax-hMin||1;

  const xP=d=>M.l+d/totM*plotW;
  const yP=h=>M.t+(hMax-h)/hSpan*plotH;

  // Maaiveld pad
  const maaiveldPts=geldigePunten.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");
  const vlakPts=`${xP(geldigePunten[0].afstand)},${H-M.b} ${maaiveldPts} ${xP(geldigePunten[geldigePunten.length-1].afstand)},${H-M.b}`;

  // Boorpad
  const startH=(geldigePunten[0]?.hoogte??0)-insteekDiepte;
  const eindH=(geldigePunten[geldigePunten.length-1]?.hoogte??0)-uitkomstDiepte;

  // Y-as grid stappen
  const stap=hSpan>8?2:hSpan>4?1:0.5;
  const yStarts=Math.ceil(hMin/stap)*stap;
  const yGridLijnen=[];
  for(let h=yStarts;h<=hMax;h+=stap) yGridLijnen.push(h);

  return (
    <div className="w-full overflow-x-auto">
      <svg viewBox={`0 0 ${W} ${H}`} className="w-full" style={{minWidth:600,height:280}}>
        {/* Grid */}
        {yGridLijnen.map(h=>(
          <g key={h}>
            <line x1={M.l} y1={yP(h)} x2={W-M.r} y2={yP(h)}
              stroke="#e5e7eb" strokeWidth={h===0?1.5:0.5} />
            <text x={M.l-4} y={yP(h)+4} textAnchor="end" fontSize={9} fill="#9ca3af">{h.toFixed(1)}</text>
          </g>
        ))}

        {/* NAP-lijn */}
        {hMin<0&&hMax>0&&(
          <line x1={M.l} y1={yP(0)} x2={W-M.r} y2={yP(0)}
            stroke="#3b82f6" strokeWidth={1} strokeDasharray="6,3" opacity={0.6}/>
        )}

        {/* Grond vlak (groen) */}
        <polygon points={vlakPts} fill="#bbf7d0" fillOpacity={0.5}/>
        <polyline points={maaiveldPts} fill="none" stroke="#16a34a" strokeWidth={2.5}/>
        <text x={M.l+4} y={M.t+11} fontSize={9} fill="#15803d">maaiveld (AHN4)</text>

        {/* KLIC kruisingen */}
        {klicKruisingen.map((k,i)=>{
          const kx=xP(k.afstand);
          const ky=yP(k.hoogte-k.diepte);
          const kType=KLIC_TYPES[k.type]??KLIC_TYPES.tele;
          return(
            <g key={i}>
              {/* Verticale stippellijn door maaiveld */}
              <line x1={kx} y1={M.t} x2={kx} y2={H-M.b}
                stroke={kType.kleur} strokeWidth={1.5} strokeDasharray="5,3" opacity={0.7}/>
              {/* Kabelpunt */}
              <circle cx={kx} cy={ky} r={5} fill={kType.kleur} fillOpacity={0.9}/>
              {/* Label */}
              <text x={kx+7} y={ky+4} fontSize={9} fill={kType.kleur} fontWeight="600">{kType.label}</text>
              <text x={kx+7} y={ky+14} fontSize={8} fill="#6b7280">-{k.diepte.toFixed(1)}m</text>
            </g>
          );
        })}

        {/* Boorpad */}
        <line x1={xP(0)} y1={yP(startH)} x2={xP(totM)} y2={yP(eindH)}
          stroke="#f97316" strokeWidth={3} strokeDasharray="10,5" strokeLinecap="round"/>
        <circle cx={xP(0)} cy={yP(startH)} r={5} fill="#f97316"/>
        <circle cx={xP(totM)} cy={yP(eindH)} r={5} fill="#f97316"/>
        <text x={xP(0)+6} y={yP(startH)-6} fontSize={9} fill="#ea580c" fontWeight="600">
          {startH.toFixed(2)}m NAP ({insteekDiepte.toFixed(1)}m)
        </text>
        <text x={xP(totM)-4} y={yP(eindH)-6} fontSize={9} fill="#ea580c" fontWeight="600" textAnchor="end">
          {eindH.toFixed(2)}m NAP ({uitkomstDiepte.toFixed(1)}m)
        </text>

        {/* Legenda */}
        <rect x={W-M.r-140} y={M.t+2} width={130} height={44} rx={4}
          fill="white" fillOpacity={0.85} stroke="#e5e7eb"/>
        <line x1={W-M.r-130} y1={M.t+14} x2={W-M.r-110} y2={M.t+14}
          stroke="#f97316" strokeWidth={2.5} strokeDasharray="6,3"/>
        <text x={W-M.r-106} y={M.t+18} fontSize={9} fill="#374151">Boorpad</text>
        <line x1={W-M.r-130} y1={M.t+30} x2={W-M.r-110} y2={M.t+30}
          stroke="#16a34a" strokeWidth={2.5}/>
        <text x={W-M.r-106} y={M.t+34} fontSize={9} fill="#374151">Maaiveld AHN</text>

        {/* X-as labels */}
        {[0,0.25,0.5,0.75,1].map(f=>{
          const d=f*totM;
          return(
            <g key={f}>
              <line x1={xP(d)} y1={H-M.b} x2={xP(d)} y2={H-M.b+4} stroke="#9ca3af"/>
              <text x={xP(d)} y={H-M.b+14} textAnchor="middle" fontSize={9} fill="#9ca3af">
                {d<1?0:d>=1000?`${(d/1000).toFixed(1)}km`:`${Math.round(d)}m`}
              </text>
            </g>
          );
        })}

        {/* As labels */}
        <text x={M.l-40} y={H/2} fontSize={10} fill="#6b7280"
          transform={`rotate(-90,${M.l-40},${H/2})`} textAnchor="middle">Hoogte (m NAP)</text>
        <text x={W/2} y={H-2} textAnchor="middle" fontSize={10} fill="#6b7280">Afstand langs boorlijn (m)</text>

        {/* Kaders */}
        <rect x={M.l} y={M.t} width={plotW} height={plotH}
          fill="none" stroke="#e5e7eb" strokeWidth={1}/>
      </svg>
    </div>
  );
}

// ─── Hoofd-component ──────────────────────────────────────────────
export default function Diepteligging({ project, onNaar, opgeslagenDiepte, onSave }) {
  const mapRef       = useRef(null);
  const kaartRef     = useRef(null);
  const basisLaagRef = useRef(null);
  const boorCoordRef = useRef([]);  // live ref voor map-closures

  // ── Boorlijn uit project ──
  const [boorCoords, setBoorCoords] = useState(() => {
    try {
      const g=project?.boortrace_geojson;
      if(!g) return [];
      const p=typeof g==="string"?JSON.parse(g):g;
      return p.coordinates?.map(([lng,lat])=>[lat,lng])??[];
    } catch { return []; }
  });

  // ── Dieptes ──
  const opgeslagen = (() => { try{return typeof opgeslagenDiepte==="string"?JSON.parse(opgeslagenDiepte):opgeslagenDiepte??{};}catch{return {};} })();
  const [insteekDiepte,  setInsteekDiepte]  = useState(opgeslagen.insteek??1.5);
  const [uitkomstDiepte, setUitkomstDiepte] = useState(opgeslagen.uitkomst??1.5);

  // ── AHN hoogte — laad opgeslagen data bij mount ──
  const [profielPunten,  setProfielPunten]  = useState(() => {
    try {
      const saved = project?.ahn_profiel;
      if(!saved) return [];
      const p = typeof saved==="string" ? JSON.parse(saved) : saved;
      return Array.isArray(p) ? p : [];
    } catch { return []; }
  });
  const [hoogteBezig,    setHoogteBezig]    = useState(false);
  const [hoogteInfo,     setHoogteInfo]     = useState(() => {
    try {
      const saved = project?.ahn_profiel;
      if(!saved) return null;
      const p = typeof saved==="string" ? JSON.parse(saved) : saved;
      if(!Array.isArray(p)||!p.length) return null;
      const geldig = p.filter(pt=>pt.hoogte!==null);
      if(!geldig.length) return null;
      return `${geldig.length}/${p.length} punten (opgeslagen) · min ${Math.min(...geldig.map(pt=>pt.hoogte)).toFixed(2)}m · max ${Math.max(...geldig.map(pt=>pt.hoogte)).toFixed(2)}m NAP`;
    } catch { return null; }
  });

  // ── KLIC kruisingen ──
  const [klicKruisingen, setKlicKruisingen] = useState([]);

  const totM = boorCoords.length>=2 ? totaalLengte(boorCoords) : 0;

  // sync ref
  boorCoordRef.current = boorCoords;

  // ── Bereken KLIC-kruisingen ──────────────────────────────────────
  useEffect(() => {
    if(boorCoords.length<2) return;
    try {
      // Laad KLIC features uit project
      const klicSets = [];
      ["klic_ls","klic_ms","klic_gas","klic_water","klic_tele","klic_riool"].forEach(k=>{
        const raw=project?.[k];
        if(!raw) return;
        const gj=typeof raw==="string"?JSON.parse(raw):raw;
        (gj.features??[gj]).forEach(f=>{
          if(f?.geometry) klicSets.push({...f,_klicKey:k});
        });
      });
      // Ook sessionStorage
      const ss=sessionStorage.getItem("klic_features");
      if(ss) JSON.parse(ss).forEach(f=>klicSets.push(f));

      const kruisingen=[];
      const cumul=cumulatiefAfstanden(boorCoords);

      klicSets.forEach(feat => {
        const type=detecteerKlicType(feat);
        const kType=KLIC_TYPES[type]??KLIC_TYPES.tele;
        const geom=feat.geometry;
        if(!geom?.coordinates) return;

        // Flatten alle segmenten van de KLIC feature
        const klicSegs=[];
        const flatten=coords=>{
          for(let i=0;i<coords.length-1;i++){
            const a=coords[i],b=coords[i+1];
            if(Array.isArray(a[0])){flatten(coords);return;}
            // Zet WGS84 om naar RD voor exacte intersectie-berekening
            const rdA=latLngNaarRD(a[1]??a[0],a[0]??a[1]);
            const rdB=latLngNaarRD(b[1]??b[0],b[0]??b[1]);
            klicSegs.push([rdA,rdB]);
          }
        };
        if(geom.type==="LineString") flatten(geom.coordinates);
        else if(geom.type==="MultiLineString") geom.coordinates.forEach(flatten);

        // Controleer elk boor-segment tegen elk KLIC-segment
        for(let bi=0;bi<boorCoords.length-1;bi++){
          const bA=latLngNaarRD(boorCoords[bi][0],boorCoords[bi][1]);
          const bB=latLngNaarRD(boorCoords[bi+1][0],boorCoords[bi+1][1]);
          const segLen=afstandM(boorCoords[bi],boorCoords[bi+1]);

          for(const [kA,kB] of klicSegs){
            const sn=segSnijpunt(bA,bB,kA,kB);
            if(sn){
              const afstand=cumul[bi]+sn.t*segLen;
              // Maaiveld hoogte op kruispunt (interpoleer uit profiel als beschikbaar)
              const pp=profielPunten.find(p=>Math.abs(p.afstand-afstand)<2.5);
              const hoogte=pp?.hoogte??0;
              kruisingen.push({afstand,hoogte,type,diepte:kType.diepte,label:kType.label,kleur:kType.kleur});
            }
          }
        }
      });

      // Verwijder duplicaten (KLIC-lijnen die meerdere keren snijden op bijna dezelfde afstand)
      const uniek=kruisingen.filter((k,i)=>
        !kruisingen.slice(0,i).some(k2=>Math.abs(k2.afstand-k.afstand)<3&&k2.type===k.type)
      );
      uniek.sort((a,b)=>a.afstand-b.afstand);
      setKlicKruisingen(uniek);
    } catch(e) { console.warn("KLIC kruisingen fout:", e); }
  }, [boorCoords, profielPunten, project]);

  // ── Kaart initialisatie ──────────────────────────────────────────
  useEffect(() => {
    if(typeof window==="undefined"||kaartRef.current||!mapRef.current) return;
    let actief=true;
    (async()=>{
      const ls=src=>new Promise((ok,er)=>{
        if(document.querySelector(`script[src="${src}"]`))return ok();
        const s=document.createElement("script");s.src=src;s.onload=ok;s.onerror=er;document.head.appendChild(s);
      });
      if(!document.querySelector('link[href*="leaflet"]')){
        const c=document.createElement("link");c.rel="stylesheet";
        c.href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";document.head.appendChild(c);
      }
      await ls("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js");
      await ls("https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js");
      await ls("https://cdnjs.cloudflare.com/ajax/libs/proj4leaflet/1.0.2/proj4leaflet.js");
      if(!actief||!mapRef.current) return;
      const L=window.L;
      const crs=maakRdCrs(L);
      const center=boorCoordRef.current[0]??[52.15,5.39];
      const kaart=L.map(mapRef.current,{crs,center,zoom:14,maxZoom:22,zoomControl:true});
      kaartRef.current=kaart;

      // BRT achtergrond
      basisLaagRef.current=L.tileLayer(
        "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png",
        {maxZoom:22,maxNativeZoom:13,tileSize:256,attribution:"© PDOK BRT, © Kadaster",zIndex:1}
      ).addTo(kaart);

      // ── Bewerkbare boorlijn ──────────────────────────────────────
      let boorPolyline=null;
      let editMarkers=[];
      let tussenMarkers=[]; // midpoints for inserting
      let isDragging=false;

      function markerIcon(kleur="#f97316",groot=false){
        const sz=groot?20:14;
        return L.divIcon({
          html:`<div style="width:${sz}px;height:${sz}px;border-radius:50%;background:${kleur};border:2.5px solid white;box-shadow:0 1px 4px rgba(0,0,0,.35);cursor:grab"></div>`,
          className:"",iconSize:[sz,sz],iconAnchor:[sz/2,sz/2],
        });
      }
      function tussenIcon(){
        return L.divIcon({
          html:`<div style="width:10px;height:10px;border-radius:50%;background:#f97316;border:2px solid white;opacity:0.55;cursor:pointer"></div>`,
          className:"",iconSize:[10,10],iconAnchor:[5,5],
        });
      }

      function updateKaartLaag(){
        editMarkers.forEach(m=>kaart.removeLayer(m));
        editMarkers=[];
        tussenMarkers.forEach(m=>kaart.removeLayer(m));
        tussenMarkers=[];
        if(boorPolyline){kaart.removeLayer(boorPolyline);boorPolyline=null;}
        const coords=boorCoordRef.current;
        if(coords.length<1) return;

        // Boorlijn
        if(coords.length>=2){
          boorPolyline=L.polyline(coords,{color:"#f97316",weight:4,opacity:0.9,lineCap:"round"}).addTo(kaart);
        }

        // Punten-markers (bewerkbaar)
        coords.forEach((coord,idx)=>{
          const isStart=idx===0,isEinde=idx===coords.length-1;
          const kleur=isStart?"#16a34a":isEinde?"#dc2626":"#f97316";
          const mk=L.marker(coord,{draggable:true,icon:markerIcon(kleur,isStart||isEinde),zIndexOffset:isStart||isEinde?200:100}).addTo(kaart);
          mk.on("drag",e=>{
            isDragging=true;
            boorCoordRef.current[idx]=[e.latlng.lat,e.latlng.lng];
            if(boorPolyline) boorPolyline.setLatLngs(boorCoordRef.current);
          });
          mk.on("dragend",()=>{
            setBoorCoords([...boorCoordRef.current]);
            setTimeout(()=>{isDragging=false;},100);
          });
          // Dubbelklik = verwijder punt (niet start/eind)
          mk.on("dblclick",e=>{
            L.DomEvent.stop(e);
            if(isStart||isEinde||boorCoordRef.current.length<=2) return;
            boorCoordRef.current.splice(idx,1);
            setBoorCoords([...boorCoordRef.current]);
          });
          editMarkers.push(mk);
        });

        // Tussenpunten voor invoegen
        if(coords.length>=2){
          for(let i=0;i<coords.length-1;i++){
            const midLat=(coords[i][0]+coords[i+1][0])/2;
            const midLng=(coords[i][1]+coords[i+1][1])/2;
            const tm=L.marker([midLat,midLng],{icon:tussenIcon(),zIndexOffset:50}).addTo(kaart);
            const captureI=i;
            tm.on("click",e=>{
              if(isDragging) return;
              L.DomEvent.stop(e);
              boorCoordRef.current.splice(captureI+1,0,[midLat,midLng]);
              setBoorCoords([...boorCoordRef.current]);
            });
            tussenMarkers.push(tm);
          }
        }
      }

      kaart._updateBoorLaag=updateBoorLaag;
      function updateBoorLaag(coords){
        boorCoordRef.current=coords;
        updateKaartLaag();
      }
      updateKaartLaag();

      // Zoom naar boorlijn
      if(boorCoordRef.current.length>=2){
        try{ kaart.fitBounds(L.latLngBounds(boorCoordRef.current).pad(0.15),{maxZoom:16}); }catch{}
      }

      // KLIC als WMS overlay
      L.tileLayer.wms("https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0",{
        layers:"buisleiding",format:"image/png",transparent:true,
        opacity:0.8,zIndex:10,attribution:"© Kadaster KLIC",
      }).addTo(kaart);
    })();
    return()=>{
      actief=false;
      if(kaartRef.current){try{kaartRef.current.remove();}catch{}kaartRef.current=null;}
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  },[]);

  // Sync boorlijn naar kaart als boorCoords verandert
  useEffect(()=>{
    kaartRef.current?._updateBoorLaag?.(boorCoords);
  },[boorCoords]);

  // ── AHN hoogte ophalen + opslaan ──────────────────────────────
  const haalHoogteOp = useCallback(async()=>{
    if(boorCoords.length<2) return;
    setHoogteBezig(true);
    setHoogteInfo("Bezig met ophalen…");
    try {
      const punten=interpoleerLijn(boorCoords,5);
      const rdPunten=punten.map(p=>{const rd=latLngNaarRD(p.lat,p.lng);return{x:rd.x,y:rd.y};});
      const res=await fetch("/api/ahn-hoogte",{
        method:"POST",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({punten:rdPunten}),
      });
      if(!res.ok){
        const errTxt = await res.text().catch(()=>"");
        throw new Error(`HTTP ${res.status}${errTxt?" — "+errTxt.slice(0,80):""}`);
      }
      const data=await res.json();
      const metHoogte=punten.map((p,i)=>({...p,hoogte:data.hoogtes?.[i]??null}));
      setProfielPunten(metHoogte);
      const geldig=metHoogte.filter(p=>p.hoogte!==null);
      const info=geldig.length
        ? `${geldig.length}/${punten.length} punten · min ${Math.min(...geldig.map(p=>p.hoogte)).toFixed(2)}m · max ${Math.max(...geldig.map(p=>p.hoogte)).toFixed(2)}m NAP`
        : "Geen hoogte-data ontvangen — controleer /api/ahn-hoogte";
      setHoogteInfo(info);
      // Sla op in project zodat het bij herlaad beschikbaar blijft
      if(geldig.length && onSave) {
        try { await onSave({ ahn_profiel: metHoogte }); } catch(e){ console.warn("AHN opslaan:", e); }
      }
    } catch(e){
      setHoogteInfo(`❌ Fout: ${e.message}`);
    }
    setHoogteBezig(false);
  },[boorCoords, onSave]);

  // ── UI render ────────────────────────────────────────────────────
  return(
    <div className="space-y-4">
      <div className="flex gap-4" style={{height:"calc(100vh - 260px)",minHeight:420}}>

        {/* ── Sidebar ── */}
        <div className="w-72 flex-shrink-0 bg-white border border-gray-200 rounded-xl overflow-y-auto flex flex-col">
          <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-100">
            <div>
              <span className="text-sm font-semibold text-gray-900">6. Diepteligging</span>
              <div className="text-xs text-gray-400">Dwarsprofiel & bodem</div>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto px-4 py-3 space-y-4">

            {/* Boorlijn info */}
            <div className="bg-orange-50 rounded-lg px-3 py-2">
              <div className="flex items-center gap-2 mb-1">
                <div className="w-2 h-2 rounded-full bg-orange-500"/>
                <span className="text-xs font-semibold text-orange-700">Boorlijn</span>
              </div>
              <div className="text-xs text-orange-600">
                {boorCoords.length>=2
                  ? `${Math.round(totM)}m · ${boorCoords.length} punten`
                  : "Geen boorlijn gevonden"}
              </div>
              <div className="text-xs text-gray-400 mt-0.5">
                🟢 Start · 🔴 Einde · ⚪ tussenklikt voegen punt in · dubbelklik verwijdert
              </div>
            </div>

            {/* Diepte-instellingen */}
            <div>
              <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Insteekdiepte</div>
              <div className="flex items-center gap-2">
                <input type="range" min={0.5} max={8} step={0.1}
                  value={insteekDiepte} onChange={e=>setInsteekDiepte(+e.target.value)}
                  className="flex-1 accent-orange-500"/>
                <span className="text-sm font-semibold text-gray-700 w-12 text-right">{insteekDiepte.toFixed(1)}m</span>
              </div>
              <div className="text-xs text-gray-400 mt-0.5">Diepte onder maaiveld bij start (S)</div>
            </div>

            <div>
              <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Uitkomstdiepte</div>
              <div className="flex items-center gap-2">
                <input type="range" min={0.5} max={8} step={0.1}
                  value={uitkomstDiepte} onChange={e=>setUitkomstDiepte(+e.target.value)}
                  className="flex-1 accent-orange-500"/>
                <span className="text-sm font-semibold text-gray-700 w-12 text-right">{uitkomstDiepte.toFixed(1)}m</span>
              </div>
              <div className="text-xs text-gray-400 mt-0.5">Diepte onder maaiveld bij einde (E)</div>
            </div>

            {/* Profiel analyse knop */}
            <button onClick={haalHoogteOp} disabled={hoogteBezig||boorCoords.length<2}
              className={`w-full py-2.5 rounded-xl text-sm font-semibold transition-all ${
                hoogteBezig||boorCoords.length<2
                  ?"bg-gray-100 text-gray-400 cursor-not-allowed"
                  :"bg-orange-500 hover:bg-orange-600 text-white shadow-sm"
              }`}>
              {hoogteBezig ? (
                <span className="flex items-center justify-center gap-2">
                  <span className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"/>
                  AHN hoogte ophalen…
                </span>
              ) : "⛰ Analyseer hoogte (AHN4)"}
            </button>

            {hoogteInfo&&(
              <div className={`text-xs rounded-lg px-3 py-2 ${hoogteInfo.startsWith("Fout")?"bg-red-50 text-red-600":"bg-green-50 text-green-700"}`}>
                {hoogteInfo}
              </div>
            )}

            {/* KLIC kruisingen samenvatting */}
            {klicKruisingen.length>0&&(
              <div>
                <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
                  KLIC kruisingen ({klicKruisingen.length})
                </div>
                <div className="space-y-1">
                  {klicKruisingen.map((k,i)=>{
                    const kt=KLIC_TYPES[k.type]??KLIC_TYPES.tele;
                    return(
                      <div key={i} className="flex items-center gap-2 text-xs py-1 border-b border-gray-50">
                        <div className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{background:kt.kleur}}/>
                        <span className="font-medium text-gray-700">{kt.label}</span>
                        <span className="text-gray-400 flex-1">@{Math.round(k.afstand)}m</span>
                        <span className="text-gray-500">-{k.diepte.toFixed(1)}m</span>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {/* Boorlijn opslaan */}
            <div className="border-t border-gray-100 pt-3">
              <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Boorlijn exporteren</div>
              <button
                onClick={()=>{
                  if(boorCoords.length<2)return;
                  const gj={type:"Feature",geometry:{type:"LineString",
                    coordinates:boorCoords.map(([lat,lng])=>[lng,lat])},
                    properties:{insteekDiepte,uitkomstDiepte}};
                  const blob=new Blob([JSON.stringify(gj,null,2)],{type:"application/json"});
                  const a=document.createElement("a");a.href=URL.createObjectURL(blob);
                  a.download="boorlijn_diepte.geojson";a.click();
                }}
                className="w-full py-2 rounded-lg border border-gray-200 text-xs text-gray-500 hover:bg-gray-50">
                ⬇ Download GeoJSON
              </button>
            </div>
          </div>
        </div>

        {/* ── Kaart ── */}
        <div className="flex-1 min-w-0 rounded-xl border border-gray-200 overflow-hidden shadow-sm relative">
          <div ref={mapRef} className="w-full h-full"/>
          <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-[400] pointer-events-none">
            <div className="bg-white/90 backdrop-blur-sm rounded-full px-3 py-1 text-xs text-gray-500 shadow border border-gray-100">
              🟢 Start · 🔴 Einde · ⚪ tussenpunt invoegen · dubbelklik op punt = verwijderen
            </div>
          </div>
        </div>
      </div>

      {/* ── Dwarsprofiel ── */}
      <div className="bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100">
          <h3 className="text-sm font-semibold text-gray-900">⛰ Dwarsprofiel langs boorlijn</h3>
          <div className="flex items-center gap-3 text-xs text-gray-400">
            {profielPunten.length>0&&<span>{profielPunten.length} meetpunten · AHN4</span>}
            {klicKruisingen.length>0&&(
              <span className="flex items-center gap-1">
                {Object.entries(KLIC_TYPES).map(([k,v])=>{
                  const n=klicKruisingen.filter(kr=>kr.type===k).length;
                  if(!n) return null;
                  return <span key={k} style={{color:v.kleur}} className="font-medium">{v.label}×{n}</span>;
                })}
              </span>
            )}
          </div>
        </div>
        <div className="p-2">
          <Dwarsprofiel
            profielPunten={profielPunten}
            insteekDiepte={insteekDiepte}
            uitkomstDiepte={uitkomstDiepte}
            klicKruisingen={klicKruisingen}
            totM={totM}
          />
        </div>

        {/* Legenda KLIC */}
        {klicKruisingen.length>0&&(
          <div className="px-4 pb-3 flex flex-wrap gap-3">
            {Object.entries(KLIC_TYPES).map(([k,v])=>{
              if(!klicKruisingen.some(kr=>kr.type===k)) return null;
              return(
                <div key={k} className="flex items-center gap-1.5 text-xs text-gray-600">
                  <div className="w-3 h-0.5 rounded" style={{background:v.kleur}}/>
                  <div className="w-2 h-2 rounded-full" style={{background:v.kleur}}/>
                  {v.label} (nom. -{v.diepte.toFixed(1)}m)
                </div>
              );
            })}
            <div className="flex items-center gap-1.5 text-xs text-gray-400 ml-auto">
              * Kabeldepths zijn nominale waarden (KLIC-standaard)
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
