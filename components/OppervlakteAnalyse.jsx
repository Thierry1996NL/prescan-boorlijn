"use client";
import { useEffect, useRef, useState } from "react";

// ─── BGT oppervlak typen ─────────────────────────────────────────
const BGT_TYPEN = {
  "gesloten verharding":  { label:"Gesloten verharding",  kleur:"#6b7280", hex:"#6b7280", risico:"Hoog",   icoon:"🚗" },
  "open verharding":      { label:"Open verharding",      kleur:"#f59e0b", hex:"#f59e0b", risico:"Middel", icoon:"🧱" },
  "onverhard":            { label:"Onverhard",             kleur:"#d97706", hex:"#d97706", risico:"Middel", icoon:"🌿" },
  "groenvoorziening":     { label:"Groenvoorziening",      kleur:"#16a34a", hex:"#16a34a", risico:"Laag",   icoon:"🌱" },
  "water":                { label:"Water",                  kleur:"#2563eb", hex:"#2563eb", risico:"Hoog",   icoon:"💧" },
  "spoor":                { label:"Spoor",                  kleur:"#dc2626", hex:"#dc2626", risico:"Hoog",   icoon:"🚂" },
  "overige":              { label:"Overig",                 kleur:"#9ca3af", hex:"#9ca3af", risico:"?",      icoon:"❓" },
};
const RISICO_KLEUR = { Hoog:"bg-red-100 text-red-600", Middel:"bg-orange-100 text-orange-600", Laag:"bg-green-100 text-green-600", "?":"bg-gray-100 text-gray-500" };

// ─── Haversine afstand ───────────────────────────────────────────
function afstandM([lat1,lng1],[lat2,lng2]) {
  const R=6371000,dLat=(lat2-lat1)*Math.PI/180,dLng=(lng2-lng1)*Math.PI/180;
  const a=Math.sin(dLat/2)**2+Math.cos(lat1*Math.PI/180)*Math.cos(lat2*Math.PI/180)*Math.sin(dLng/2)**2;
  return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
}

// ─── BGT query ───────────────────────────────────────────────────
async function haalOppervlakOp(lat, lng) {
  try {
    const d=0.00004;
    const bbox=`${lng-d},${lat-d},${lng+d},${lat+d}`;
    const url=`https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature&typeName=bgt:wegdeel,bgt:onbegroeidterreindeel,bgt:begroeidterreindeel,bgt:waterdeel,bgt:spoor&outputFormat=application/json&bbox=${bbox},EPSG:4326&srsName=EPSG:4326&count=1`;
    const res=await fetch(url,{signal:AbortSignal.timeout(8000)});
    if(!res.ok)return null;
    const data=await res.json();
    const feat=data.features?.[0];
    if(!feat)return null;
    const raw=(feat.properties?.fysiekVoorkomen||feat.properties?.bgt_fysiekvoorkomen||feat.properties?.bgt_type||"overige").toLowerCase();
    const match=Object.entries(BGT_TYPEN).find(([k])=>raw.includes(k));
    return match?{...match[1],rawType:raw}:{...BGT_TYPEN["overige"],rawType:raw};
  }catch{return null;}
}

// ─── Genereer sample punten langs boorlijn ───────────────────────
function genereerPunten(coords, stapM=5) {
  if(!coords||coords.length<2)return[];
  const punten=[];
  let cumulatief=0;
  // Startpunt
  punten.push({lat:coords[0][0],lng:coords[0][1],positieM:0});
  for(let i=0;i<coords.length-1;i++){
    const segLen=afstandM(coords[i],coords[i+1]);
    if(segLen<0.01)continue;
    let offset=stapM-(cumulatief%stapM||stapM);
    if(offset>=stapM)offset=stapM;
    while(offset<segLen){
      const t=offset/segLen;
      punten.push({
        lat:coords[i][0]+t*(coords[i+1][0]-coords[i][0]),
        lng:coords[i][1]+t*(coords[i+1][1]-coords[i][1]),
        positieM:cumulatief+offset,
      });
      offset+=stapM;
    }
    cumulatief+=segLen;
  }
  // Eindpunt
  const last=coords[coords.length-1];
  if(punten[punten.length-1].positieM<cumulatief-0.5)
    punten.push({lat:last[0],lng:last[1],positieM:cumulatief});
  return punten;
}

// ─── RD New CRS helpers ──────────────────────────────────────────
function maakRdCrs(L) {
  return new L.Proj.CRS("EPSG:28992",
    "+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 +x_0=155000 +y_0=463000 +ellps=bessel +towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 +units=m +no_defs",
    {resolutions:[3440.640,1720.320,860.160,430.080,215.040,107.520,53.760,26.880,13.440,6.720,3.360,1.680,0.840,0.420,0.210,0.105,0.0525,0.02625,0.013125,0.00656,0.00328,0.00164,0.00082],
     origin:[-285401.920,903401.920],bounds:L.bounds([-285401.920,22598.080],[595401.920,903401.920])}
  );
}
function rdNaarLatLng(L,x,y){
  if(window.proj4){try{const w=proj4("EPSG:28992","EPSG:4326",[x,y]);return L.latLng(w[1],w[0]);}catch{}}
  const dX=(x-155000)/100000,dY=(y-463000)/100000;
  const N=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
  const E=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
  return L.latLng(52.15517440+N/3600,5.38720621+E/3600);
}

// ════════════════════════════════════════════════════════════════
export default function OppervlakteAnalyse({ project, onAnalyseOpgeslagen }) {
  const mapRef   = useRef(null);
  const kaartRef = useRef(null);
  const klicRef  = useRef([]);

  const [analysePunten, setAnalysePunten] = useState(() => {
    try { const s=project?.analyse_punten; if(s)return typeof s==="string"?JSON.parse(s):s; } catch {}
    return [];
  });
  const [bezig,      setBezig]      = useState(false);
  const [voortgang,  setVoortgang]  = useState(0);
  const [totaalPunten, setTotaalPunten] = useState(0);
  const [opgeslagen, setOpgeslagen] = useState(false);
  const [stapGrootte,setStagGrootte] = useState(5); // meters per sample

  const s3 = (() => { try { return JSON.parse(project?.laag_instellingen||"{}"); } catch { return {}; } })();
  const box = s3.__kaartBox ?? null;

  const boorCoords = (() => {
    try {
      const g=project?.boortrace_geojson;
      if(!g)return[];
      const p=typeof g==="string"?JSON.parse(g):g;
      return p.coordinates?.map(([lng,lat])=>[lat,lng])??[];
    } catch { return []; }
  })();
  const totaalM = boorCoords.length>=2
    ? boorCoords.reduce((s,_,i)=>i===0?0:s+afstandM(boorCoords[i-1],boorCoords[i]),0)
    : 0;

  // ── Kaart initialiseren ────────────────────────────────────────
  useEffect(() => {
    if(typeof window==="undefined"||kaartRef.current)return;
    let actief=true;
    (async()=>{
      if(!document.querySelector('link[href*="leaflet"]')){
        const css=document.createElement("link");css.rel="stylesheet";
        css.href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";document.head.appendChild(css);
      }
      const laadS=src=>new Promise((ok,err)=>{
        if(document.querySelector(`script[src="${src}"]`))return ok();
        const s=document.createElement("script");s.src=src;s.onload=ok;s.onerror=err;document.head.appendChild(s);
      });
      await laadS("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js");
      await laadS("https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js");
      await laadS("https://cdnjs.cloudflare.com/ajax/libs/proj4leaflet/1.0.2/proj4leaflet.js");
      if(!actief||!mapRef.current)return;
      const L=window.L;
      let rdCrs; try{rdCrs=maakRdCrs(L);}catch{}
      const pos=s3.__kaartPositie;
      const center=pos?[pos.lat,pos.lng]:(boorCoords[0]??[52.15,5.39]);
      const kaart=L.map(mapRef.current,{...(rdCrs?{crs:rdCrs}:{}),center,zoom:pos?.zoom??14,maxZoom:22,zoomControl:true});
      kaartRef.current=kaart;

      // Achtergrond
      const ach=s3.__achtergrond??"brt_standaard";
      const brtUrls={brt_standaard:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png",brt_grijs:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:28992/{z}/{x}/{y}.png",brt_pastel:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/pastel/EPSG:28992/{z}/{x}/{y}.png"};
      if(ach==="luchtfoto")L.tileLayer.wms("https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",{layers:"Actueel_ortho25",format:"image/jpeg",transparent:false,maxZoom:22,attribution:"© PDOK"}).addTo(kaart);
      else L.tileLayer(brtUrls[ach]??brtUrls.brt_standaard,{maxZoom:22,maxNativeZoom:13,tileSize:256,attribution:"© PDOK BRT, © Kadaster"}).addTo(kaart);

      // Filterbox
      if(box)L.rectangle([[box.lat1,box.lng1],[box.lat2,box.lng2]],{color:"#6b7280",weight:2,fillOpacity:0,interactive:false}).addTo(kaart);

      // Boorlijn (vast, niet bewerkbaar)
      if(boorCoords.length>=2){
        const lijn=L.polyline(boorCoords,{color:"#2563eb",weight:5,opacity:1,interactive:false}).addTo(kaart);
        // Start/eindpunt markeren
        const mkIcoon=(nr,kleur)=>L.divIcon({className:"",html:`<div style="width:20px;height:20px;background:${kleur};border:2px solid white;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:9px;font-weight:700;color:white;box-shadow:0 1px 4px rgba(0,0,0,.4)">${nr}</div>`,iconSize:[20,20],iconAnchor:[10,10]});
        L.marker(boorCoords[0],{icon:mkIcoon("S","#15803d"),interactive:false}).addTo(kaart);
        L.marker(boorCoords[boorCoords.length-1],{icon:mkIcoon("E","#dc2626"),interactive:false}).addTo(kaart);
        try{kaart.fitBounds(lijn.getBounds().pad(0.15));}catch{}
      }

      // KLIC achtergrond laden vanuit cache
      await laadKlicAchtergrond(L,kaart,project,s3,klicRef);
    })();
    return()=>{actief=false;if(kaartRef.current){kaartRef.current.remove();kaartRef.current=null;}};
  },[]);

  async function laadKlicAchtergrond(L,kaart,project,s3,lagenRef){
    const bestanden=(()=>{try{return JSON.parse(project?.bestanden_meta||"[]");}catch{return[];}})();
    for(const b of bestanden){
      if(!b.url)continue;
      const ext=b.naam.split(".").pop().toLowerCase();
      try{
        if(ext==="zip"){
          const cache=sessionStorage.getItem(`klic_parsed_${b.id}`);
          if(!cache)continue;
          const{lagen}=JSON.parse(cache);
          for(const[thema,geoJson]of Object.entries(lagen??{})){
            const lagId=`klic_${thema}`;const li=s3[lagId]??{};
            if(li.zichtbaar===false)continue;
            const kleur=li.kleur??{laagspanning:"#7B00AA",middenspanning:"#00CCFF",water:"#000080",datatransport:"#00CC00",gasLageDruk:"#FFFF00",rioolVrijverval:"#AA00CC",overig:"#888"}[thema]??"#888";
            for(const feat of(geoJson?.features??[])){
              const coords=(feat.geometry?.coordinates??[]).map(([x,y])=>{const ll=rdNaarLatLng(L,x,y);return[ll.lat,ll.lng];});
              if(coords.length<2)continue;
              const laag=L.polyline(coords,{color:kleur,weight:li.dikte??2,opacity:(li.helderheid??0.75)*0.8,interactive:false,zIndexOffset:-500});
              laag.addTo(kaart);lagenRef.current.push(laag);
            }
          }
        }
      }catch(err){console.warn("KLIC:",err.message);}
    }
  }

  // ── Voer analyse uit ────────────────────────────────────────────
  async function voerAnalyseUit() {
    if(boorCoords.length<2){alert("Geen boorlijn gevonden. Teken eerst een boorlijn in stap 4.");return;}
    setBezig(true);setVoortgang(0);
    const punten=genereerPunten(boorCoords,stapGrootte);
    setTotaalPunten(punten.length);
    const resultaten=[];
    // Begin- en eindpunt altijd mee
    const queue=[{lat:boorCoords[0][0],lng:boorCoords[0][1],positieM:0,type:"start"},...punten.slice(1,-1),{lat:boorCoords[boorCoords.length-1][0],lng:boorCoords[boorCoords.length-1][1],positieM:totaalM,type:"eind"}];
    for(let i=0;i<queue.length;i++){
      const p=queue[i];
      const result=await haalOppervlakOp(p.lat,p.lng);
      resultaten.push({...p,oppervlak:result??BGT_TYPEN["overige"],id:`ap_${i}`});
      setVoortgang(Math.round((i+1)/queue.length*100));
      // Kort wachten om API niet te overbelasten
      if(i%5===4)await new Promise(r=>setTimeout(r,200));
    }
    setAnalysePunten(resultaten);
    setBezig(false);
    // Automatisch opslaan
    try{
      await onAnalyseOpgeslagen?.(resultaten);
      setOpgeslagen(true);setTimeout(()=>setOpgeslagen(false),3000);
    }catch(err){console.error("Opslaan:",err);}
  }

  // ── Statistieken ────────────────────────────────────────────────
  const stats = analysePunten.length>=2 ? (() => {
    const totM=analysePunten[analysePunten.length-1]?.positieM??0;
    const groepenRaw={};
    for(let i=0;i<analysePunten.length-1;i++){
      const seg=analysePunten[i+1].positieM-analysePunten[i].positieM;
      const key=analysePunten[i].oppervlak?.label??"Overig";
      groepenRaw[key]=(groepenRaw[key]??0)+seg;
    }
    return Object.entries(groepenRaw).sort((a,b)=>b[1]-a[1]).map(([label,m])=>({label,m:Math.round(m),pct:Math.round(m/totM*100),kleur:Object.values(BGT_TYPEN).find(t=>t.label===label)?.kleur??"#9ca3af"}));
  })() : [];

  // ── Profiel SVG ─────────────────────────────────────────────────
  function AnalyseProfiel() {
    if(analysePunten.length<2)return null;
    const W=900,H=120,PAD={top:20,right:10,bottom:35,left:50};
    const plotW=W-PAD.left-PAD.right;
    const totM=analysePunten[analysePunten.length-1]?.positieM??1;
    const xPos=m=>PAD.left+(m/totM)*plotW;
    const segmenten=[];
    for(let i=0;i<analysePunten.length-1;i++){
      const p=analysePunten[i],n=analysePunten[i+1];
      segmenten.push({x1:xPos(p.positieM),x2:xPos(n.positieM),kleur:p.oppervlak?.kleur??"#9ca3af",label:p.oppervlak?.label??"Overig"});
    }
    const labels=[0,0.25,0.5,0.75,1].map(f=>({x:xPos(f*totM),m:Math.round(f*totM)}));
    return(
      <svg viewBox={`0 0 ${W} ${H}`} className="w-full rounded-lg border border-gray-100 bg-white">
        {/* Verharding band */}
        {segmenten.map((s,i)=>(
          <rect key={i} x={s.x1} y={PAD.top} width={Math.max(1,s.x2-s.x1)} height={H-PAD.top-PAD.bottom} fill={s.kleur} opacity={0.85}/>
        ))}
        {/* Boorlijn lijn */}
        <line x1={PAD.left} y1={(H-PAD.bottom+PAD.top)/2} x2={W-PAD.right} y2={(H-PAD.bottom+PAD.top)/2} stroke="#1d4ed8" strokeWidth="2.5" strokeDasharray="6 3" opacity="0.7"/>
        <text x={PAD.left-4} y={(H-PAD.bottom+PAD.top)/2+4} textAnchor="end" fontSize="10" fill="#6b7280">tracé</text>
        {/* X-as labels */}
        {labels.map(({x,m})=>(
          <g key={m}>
            <line x1={x} y1={H-PAD.bottom} x2={x} y2={H-PAD.bottom+4} stroke="#9ca3af" strokeWidth="1"/>
            <text x={x} y={H-PAD.bottom+14} textAnchor="middle" fontSize="10" fill="#9ca3af">{m}m</text>
          </g>
        ))}
        <text x={W/2} y={H-1} textAnchor="middle" fontSize="11" fill="#6b7280">Afstand langs boorlijn (m)</text>
        {/* Legenda kleuren aan zijkant */}
        <text x={PAD.left} y={12} fontSize="9" fill="#9ca3af" textAnchor="middle">BGT</text>
      </svg>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex gap-4" style={{height:"calc(100vh - 220px)",minHeight:480}}>

        {/* ── Linkerpaneel ─────────────────────────────────────── */}
        <div className="w-72 flex-shrink-0 bg-white border border-gray-200 rounded-xl flex flex-col overflow-hidden">
          <div className="px-4 py-3 border-b border-gray-100">
            <h3 className="text-sm font-semibold text-gray-800">Oppervlakteanalyse</h3>
            <p className="text-xs text-gray-400 mt-0.5">BGT-data langs de boorlijn</p>
          </div>

          <div className="flex-1 overflow-y-auto">
            {/* Boorlijn info */}
            {boorCoords.length>=2?(
              <div className="px-4 py-3 border-b border-gray-100">
                <div className="bg-blue-50 rounded-lg px-3 py-2 space-y-1">
                  <div className="flex justify-between text-xs">
                    <span className="text-blue-600 font-medium">✓ Boorlijn</span>
                    <span className="text-blue-500 font-mono">{Math.round(totaalM)} m</span>
                  </div>
                  <div className="text-xs text-blue-400">{boorCoords.length} punten · start → eind</div>
                </div>
              </div>
            ):(
              <div className="px-4 py-4 text-center">
                <p className="text-sm text-gray-400">Geen boorlijn — teken eerst in stap 4.</p>
              </div>
            )}

            {/* Instellingen */}
            <div className="px-4 py-3 border-b border-gray-100">
              <label className="text-xs font-semibold text-gray-500 uppercase tracking-wide block mb-2">Steekproef interval</label>
              <div className="flex items-center gap-2">
                <input type="range" min="2" max="20" step="1" value={stapGrootte} onChange={e=>setStagGrootte(Number(e.target.value))} className="flex-1 accent-orange-500 h-1"/>
                <span className="text-xs font-mono text-gray-600 w-14 text-right">elke {stapGrootte}m</span>
              </div>
              <p className="text-xs text-gray-400 mt-1">
                ~{boorCoords.length>=2?Math.round(totaalM/stapGrootte)+2:"-"} BGT-queries
              </p>
            </div>

            {/* Analyse knop */}
            <div className="px-4 py-3 border-b border-gray-100">
              <button onClick={voerAnalyseUit} disabled={bezig||boorCoords.length<2}
                className="w-full flex items-center justify-center gap-2 px-3 py-2.5 text-sm font-semibold rounded-lg transition-colors disabled:opacity-50 bg-orange-500 text-white hover:bg-orange-600">
                {bezig?(
                  <><div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"/>Analyse bezig…</>
                ):(
                  <><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/></svg>
                  {analysePunten.length>0?"Heranalyseren":"Analyse uitvoeren"}</>
                )}
              </button>
              {bezig&&(
                <div className="mt-2 space-y-1">
                  <div className="w-full bg-gray-100 rounded-full h-1.5 overflow-hidden">
                    <div className="h-1.5 bg-orange-500 rounded-full transition-all" style={{width:`${voortgang}%`}}/>
                  </div>
                  <p className="text-xs text-gray-400 text-center">{voortgang}% · {Math.round(voortgang/100*totaalPunten)}/{totaalPunten} punten</p>
                </div>
              )}
              {opgeslagen&&<p className="text-xs text-green-600 text-center mt-1">✓ Resultaten opgeslagen</p>}
            </div>

            {/* Statistieken */}
            {stats.length>0&&(
              <div className="px-4 py-3">
                <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Samenvatting</div>
                <div className="space-y-2">
                  {stats.map(({label,m,pct,kleur})=>(
                    <div key={label}>
                      <div className="flex items-center justify-between text-xs mb-0.5">
                        <div className="flex items-center gap-1.5">
                          <div className="w-3 h-3 rounded-sm flex-shrink-0" style={{background:kleur}}/>
                          <span className="text-gray-700 font-medium">{label}</span>
                        </div>
                        <span className="text-gray-400 font-mono">{m}m <span className="text-gray-300">({pct}%)</span></span>
                      </div>
                      <div className="w-full bg-gray-100 rounded-full h-1 overflow-hidden">
                        <div className="h-1 rounded-full" style={{width:`${pct}%`,background:kleur}}/>
                      </div>
                    </div>
                  ))}
                </div>
                <div className="mt-3 pt-2 border-t border-gray-100 space-y-1">
                  {stats.filter(s=>{const t=Object.values(BGT_TYPEN).find(b=>b.label===s.label);return t?.risico==="Hoog";}).map(s=>(
                    <div key={s.label} className="flex items-center gap-1.5 text-xs text-red-600 bg-red-50 rounded px-2 py-1">
                      <span>⚠</span><span className="font-medium">{s.label}</span><span className="text-red-400">— vergunning/aandacht vereist</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        </div>

        {/* ── Kaart ────────────────────────────────────────────── */}
        <div className="flex-1 min-w-0 rounded-xl border border-gray-200 overflow-hidden shadow-sm">
          <div ref={mapRef} className="w-full h-full"/>
        </div>
      </div>

      {/* ── Profiel ────────────────────────────────────────────── */}
      {analysePunten.length>=2&&(
        <div className="bg-white border border-gray-200 rounded-xl p-4">
          <div className="flex items-center justify-between mb-3">
            <h3 className="text-sm font-semibold text-gray-900">BGT Verhardingsprofiel langs boorlijn</h3>
            <div className="flex items-center gap-4">
              {Object.values(BGT_TYPEN).filter(t=>stats.find(s=>s.label===t.label)).map(t=>(
                <div key={t.label} className="flex items-center gap-1.5 text-xs text-gray-600">
                  <div className="w-3 h-3 rounded-sm" style={{background:t.kleur}}/>
                  <span>{t.label}</span>
                </div>
              ))}
            </div>
          </div>
          <AnalyseProfiel/>
          <p className="text-xs text-gray-400 mt-2 text-center">
            {analysePunten.length} meetpunten · interval {stapGrootte}m · klik Volgende om diepteligging in te stellen
          </p>
        </div>
      )}
    </div>
  );
}
