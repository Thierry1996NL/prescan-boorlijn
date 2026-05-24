"use client";
import { useMemo, useRef, useState } from "react";
import BoringSVG, { computeBoring } from "@/components/BoringSVG";

// ─── Geometrie helpers (uit Diepteligging) ────────────────────────────────────
function afstandM(p1,p2){const R=6371000,dLat=(p2[0]-p1[0])*Math.PI/180,dLng=(p2[1]-p1[1])*Math.PI/180,a=Math.sin(dLat/2)**2+Math.cos(p1[0]*Math.PI/180)*Math.cos(p2[0]*Math.PI/180)*Math.sin(dLng/2)**2;return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));}
function cumulatiefAfstanden(c){const r=[0];for(let i=1;i<c.length;i++)r.push(r[i-1]+afstandM(c[i-1],c[i]));return r;}
function latLngNaarRD(lat,lng){const dLat=0.36*(lat-52.15517440),dLon=0.36*(lng-5.38720621);return{x:155000+190094.945*dLon-11832.228*dLon*dLat-114.221*dLon*dLat*dLat-32.391*dLon*dLon*dLon,y:463000+309056.544*dLat+60940.388*dLon*dLon*dLat-9.941*dLon*dLon-2.340*dLat*dLat*dLat};}
function segSnijpunt(p1,p2,p3,p4){const dx1=p2.x-p1.x,dy1=p2.y-p1.y,dx2=p4.x-p3.x,dy2=p4.y-p3.y,cross=dx1*dy2-dy1*dx2;if(Math.abs(cross)<1e-8)return null;const t=((p3.x-p1.x)*dy2-(p3.y-p1.y)*dx2)/cross,u=((p3.x-p1.x)*dy1-(p3.y-p1.y)*dx1)/cross;if(t>=0&&t<=1&&u>=0&&u<=1)return{t,x:p1.x+t*dx1,y:p1.y+t*dy1};return null;}
const KLIC_TYPES={ls:{label:"LS",kleur:"#ef4444",diepte:0.60},ms:{label:"MS",kleur:"#f97316",diepte:0.80},gas:{label:"Gas",kleur:"#eab308",diepte:0.80},water:{label:"Water",kleur:"#3b82f6",diepte:1.00},tele:{label:"Tele",kleur:"#8b5cf6",diepte:0.45},riool:{label:"Riool",kleur:"#6b7280",diepte:1.20}};
function detecteerKlicType(f){const n=(f.properties?.naam||f.properties?.thema||"").toLowerCase();if(n.includes("laagspan")||n.includes("ls "))return"ls";if(n.includes("middensp")||n.includes("ms "))return"ms";if(n.includes("gas"))return"gas";if(n.includes("water"))return"water";if(n.includes("tele")||n.includes("data")||n.includes("glas"))return"tele";if(n.includes("riool"))return"riool";return"tele";}

function parseJSON(val){try{return val?(typeof val==="string"?JSON.parse(val):val):null;}catch{return null;}}
function traceLength(c){if(!c?.length||c.length<2)return null;let d=0;for(let i=1;i<c.length;i++){const[ln1,la1]=c[i-1],[ln2,la2]=c[i],R=6371000,f=Math.PI/180,a=Math.sin((la2-la1)*f/2)**2+Math.cos(la1*f)*Math.cos(la2*f)*Math.sin((ln2-ln1)*f/2)**2;d+=R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));}return Math.round(d);}
function traceBearing(c){if(!c?.length||c.length<2)return null;const[ln1,la1]=c[0],[ln2,la2]=c[c.length-1],f=Math.PI/180,y=Math.sin((ln2-ln1)*f)*Math.cos(la2*f),x=Math.cos(la1*f)*Math.sin(la2*f)-Math.sin(la1*f)*Math.cos(la2*f)*Math.cos((ln2-ln1)*f);return Math.round((Math.atan2(y,x)*180/Math.PI+360)%360);}
function bearingLabel(d){if(d===null)return"—";const k=d<23||d>=338?"N":d<68?"NO":d<113?"O":d<158?"ZO":d<203?"Z":d<248?"ZW":d<293?"W":"NW";return`${d}° ${k}`;}
function nap(v){return v!=null?`${v>=0?"+":""}${v.toFixed(2)} m NAP`:"—";}

// ─── PDOK WMS Kaartje met boorlijn ───────────────────────────────────────────
function TracéKaart({traceCoords}){
  const [imgFout,setImgFout]=useState(false);
  if(!traceCoords?.length)return<div style={{padding:16,color:"#9CA3AF",fontSize:11,fontStyle:"italic"}}>Geen boorlijn beschikbaar</div>;
  const lats=traceCoords.map(c=>c[1]),lngs=traceCoords.map(c=>c[0]);
  const minLat=Math.min(...lats),maxLat=Math.max(...lats),minLng=Math.min(...lngs),maxLng=Math.max(...lngs);
  const padLat=Math.max((maxLat-minLat)*0.35,0.002),padLng=Math.max((maxLng-minLng)*0.35,0.003);
  const bbox={minLat:minLat-padLat,maxLat:maxLat+padLat,minLng:minLng-padLng,maxLng:maxLng+padLng};
  const W=700,H=300;
  const wmsUrl=`https://service.pdok.nl/brt/achtergrondkaart/wms/v2_0?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=grijs&CRS=EPSG:4326&BBOX=${bbox.minLat},${bbox.minLng},${bbox.maxLat},${bbox.maxLng}&WIDTH=${W}&HEIGHT=${H}&FORMAT=image/png&TRANSPARENT=false`;
  const latSpan=bbox.maxLat-bbox.minLat||0.001,lngSpan=bbox.maxLng-bbox.minLng||0.001;
  const toX=lng=>(lng-bbox.minLng)/lngSpan*W;
  const toY=lat=>(1-(lat-bbox.minLat)/latSpan)*H;
  const points=traceCoords.map(([lng,lat])=>`${toX(lng).toFixed(1)},${toY(lat).toFixed(1)}`).join(" ");
  return(
    <div style={{position:"relative",width:"100%",maxWidth:W,height:H,border:"1px solid #E5E7EB",borderRadius:8,overflow:"hidden",background:"#f0f4f8"}}>
      {!imgFout&&<img src={wmsUrl} alt="PDOK Kaart" onError={()=>setImgFout(true)}
        style={{position:"absolute",width:"100%",height:"100%",objectFit:"fill"}}/>}
      {imgFout&&<div style={{position:"absolute",inset:0,display:"flex",alignItems:"center",justifyContent:"center",fontSize:11,color:"#9CA3AF",flexDirection:"column",gap:4}}>
        <div>📡 Kaart niet beschikbaar (offline of CORS)</div>
        <div style={{fontSize:10}}>De boorlijn is hieronder als coordinaten weergegeven</div>
      </div>}
      <svg style={{position:"absolute",top:0,left:0,width:"100%",height:"100%",overflow:"visible"}} viewBox={`0 0 ${W} ${H}`}>
        {/* Schaduw van lijn */}
        <polyline points={points} fill="none" stroke="white" strokeWidth={6} strokeLinecap="round" strokeLinejoin="round" opacity={0.7}/>
        {/* Boorlijn */}
        <polyline points={points} fill="none" stroke="#1D4ED8" strokeWidth={3.5} strokeLinecap="round" strokeLinejoin="round"/>
        {/* Start (groen) */}
        {traceCoords.length>0&&<>
          <circle cx={toX(traceCoords[0][0])} cy={toY(traceCoords[0][1])} r={8} fill="#16A34A" stroke="white" strokeWidth={2}/>
          <text x={toX(traceCoords[0][0])+11} y={toY(traceCoords[0][1])+4} fontSize={10} fontWeight="700" fill="#166534">Start</text>
        </>}
        {/* Einde (rood) */}
        {traceCoords.length>1&&<>
          <circle cx={toX(traceCoords[traceCoords.length-1][0])} cy={toY(traceCoords[traceCoords.length-1][1])} r={8} fill="#DC2626" stroke="white" strokeWidth={2}/>
          <text x={toX(traceCoords[traceCoords.length-1][0])+11} y={toY(traceCoords[traceCoords.length-1][1])+4} fontSize={10} fontWeight="700" fill="#991B1B">Einde</text>
        </>}
        {/* Noordindicator */}
        <g transform={`translate(${W-28},28)`}>
          <circle r={18} fill="white" fillOpacity={0.85} stroke="#E5E7EB"/>
          <text x={0} y={-6} textAnchor="middle" fontSize={8} fontWeight="700" fill="#1F2937">N</text>
          <polygon points="0,-13 -4,3 0,0 4,3" fill="#1F2937"/>
        </g>
        {/* Schaalbalkie */}
        <g transform={`translate(12,${H-16})`}>
          <rect x={0} y={-8} width={60} height={10} fill="white" fillOpacity={0.8} rx={3}/>
          <line x1={0} y1={0} x2={60} y2={0} stroke="#374151" strokeWidth={2}/>
          <line x1={0} y1={-3} x2={0} y2={3} stroke="#374151" strokeWidth={1.5}/>
          <line x1={60} y1={-3} x2={60} y2={3} stroke="#374151" strokeWidth={1.5}/>
          {(()=>{const scaleM=Math.round(lngSpan/W*60*111320);return<text x={30} y={-2} textAnchor="middle" fontSize={8} fill="#374151">~{scaleM}m</text>})()}
        </g>
      </svg>
    </div>
  );
}

// ─── Volledig Dwarsprofiel SVG (read-only) ────────────────────────────────────
function VolDwarsprofiel({profielPunten,dieptePunten,klicKruisingen,totM}){
  const W=720,H=200,M={l:52,r:20,t:20,b:36};
  const geldig=(profielPunten||[]).filter(p=>p.hoogte!==null);
  if(geldig.length<2)return<div style={{padding:16,color:"#9CA3AF",fontSize:11,fontStyle:"italic"}}>Geen AHN4 hoogteprofiel opgeslagen. Voer diepteligging analyse uit in stap 6.</div>;
  const hMax=Math.max(...geldig.map(p=>p.hoogte))+0.6;
  const maxDiepte=Math.max(...(dieptePunten||[]).map(p=>p.diepte),0)+1.2;
  const hMin=Math.min(...geldig.map(p=>p.hoogte))-maxDiepte;
  const hSpan=hMax-hMin||1;
  const plotW=W-M.l-M.r,plotH=H-M.t-M.b;
  const totaal=totM||geldig[geldig.length-1]?.afstand||1;
  const xP=d=>M.l+d/totaal*plotW;
  const yP=h=>M.t+(hMax-h)/hSpan*plotH;
  const maaiveldPts=geldig.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");
  const vlakPts=`${xP(geldig[0].afstand)},${H-M.b} ${maaiveldPts} ${xP(geldig[geldig.length-1].afstand)},${H-M.b}`;
  const maaiveldOpAfstand=(a)=>{const g=geldig;for(let i=0;i<g.length-1;i++){if(a>=g[i].afstand&&a<=g[i+1].afstand){const t=(a-g[i].afstand)/(g[i+1].afstand-g[i].afstand);return g[i].hoogte+t*(g[i+1].hoogte-g[i].hoogte);}}return null;};
  const sortedWP=[...(dieptePunten||[])].sort((a,b)=>a.afstand-b.afstand);
  const boorWP=sortedWP.map(dp=>({afstand:dp.afstand,hoogte:(maaiveldOpAfstand(dp.afstand)??0)-dp.diepte,diepte:dp.diepte}));
  const boorPoly=boorWP.length>=2?boorWP.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" "):"";
  const yTicks=[];const tickStep=hSpan>10?2:hSpan>5?1:0.5;
  for(let h=Math.ceil(hMin/tickStep)*tickStep;h<=hMax;h+=tickStep)yTicks.push(h);
  // Segment labels
  const segLabels=boorWP.slice(0,-1).map((wp,i)=>{
    const nxt=boorWP[i+1];
    const mx=(xP(wp.afstand)+xP(nxt.afstand))/2,my=(yP(wp.hoogte)+yP(nxt.hoogte))/2;
    const dH=nxt.afstand-wp.afstand,dV=nxt.hoogte-wp.hoogte;
    const hoek=Math.atan2(dV,dH)*180/Math.PI;
    const pijl=hoek>0.5?"↗":hoek<-0.5?"↘":"→";
    const kleur=Math.abs(hoek)>15?"#dc2626":Math.abs(hoek)>8?"#f97316":"#16a34a";
    return{mx,my,hoek:hoek.toFixed(1),pijl,kleur,len:Math.sqrt(dH*dH+dV*dV).toFixed(1)};
  });
  return(
    <svg width="100%" viewBox={`0 0 ${W} ${H}`} style={{display:"block",maxWidth:W}}>
      <defs><marker id="rpArR" markerWidth="5" markerHeight="5" refX="4.5" refY="2.5" orient="auto"><polygon points="0,0 5,2.5 0,5" fill="#9CA3AF"/></marker><marker id="rpArL" markerWidth="5" markerHeight="5" refX="0.5" refY="2.5" orient="auto-start-reverse"><polygon points="0,0 5,2.5 0,5" fill="#9CA3AF"/></marker></defs>
      {/* Achtergrond */}
      <rect x={M.l} y={M.t} width={plotW} height={plotH} fill="#F9FAFB"/>
      {/* Maaiveld */}
      <polygon points={vlakPts} fill="#86efac" opacity={0.35}/>
      <polyline points={maaiveldPts} fill="none" stroke="#16a34a" strokeWidth={2}/>
      {/* NAP=0 lijn */}
      {hMin<0&&hMax>0&&<line x1={M.l} y1={yP(0)} x2={W-M.r} y2={yP(0)} stroke="#3b82f6" strokeWidth={0.8} strokeDasharray="5,4" opacity={0.7}/>}
      {/* KLIC kruisingen */}
      {(klicKruisingen||[]).map((k,i)=>{const kt=KLIC_TYPES[k.type]??KLIC_TYPES.tele;const x=xP(k.afstand);const yTop=M.t;const yBot=yP(k.hoogte-k.diepte);return(<g key={i}><line x1={x} y1={yTop} x2={x} y2={yBot} stroke={kt.kleur} strokeWidth={1.5} strokeDasharray="4,3" opacity={0.8}/><circle cx={x} cy={yBot} r={4} fill={kt.kleur} stroke="white" strokeWidth={1}/><rect x={x-12} y={yTop} width={24} height={12} rx={3} fill={kt.kleur} opacity={0.9}/><text x={x} y={yTop+9} textAnchor="middle" fontSize={7.5} fontWeight="700" fill="white">{kt.label}</text></g>);})}
      {/* Boring pad */}
      {boorPoly&&<><polyline points={boorPoly} fill="none" stroke="#f97316" strokeWidth={3} strokeLinecap="round" strokeLinejoin="round" opacity={0.9}/>{boorWP.map((wp,i)=>{const isStart=i===0,isEinde=i===boorWP.length-1;const kleur=isStart?"#16a34a":isEinde?"#dc2626":"#f97316";return(<g key={i}><circle cx={xP(wp.afstand)} cy={yP(wp.hoogte)} r={isStart||isEinde?6:4} fill={kleur} stroke="white" strokeWidth={1.5}/></g>);})}
        {/* Diepteaanduiding per punt */}
        {sortedWP.map((dp,i)=>{const mv=maaiveldOpAfstand(dp.afstand);const napH=mv!=null?mv-dp.diepte:null;const x=xP(dp.afstand);const y=yP(napH??hMin+0.5);return(<g key={i}><line x1={x} y1={yP(mv??hMax)} x2={x} y2={y+6} stroke="#f97316" strokeWidth={1} strokeDasharray="3,2" opacity={0.5}/><rect x={x-22} y={y-18} width={44} height={20} rx={3} fill="white" stroke="#f97316" strokeWidth={0.8} opacity={0.95}/><text x={x} y={y-9} textAnchor="middle" fontSize={7} fill="#ea580c" fontWeight="700">{dp.diepte.toFixed(2)}m</text><text x={x} y={y-2} textAnchor="middle" fontSize={6.5} fill="#6b7280">{napH!=null?nap(napH):"—"}</text></g>);})}</>}
      {/* Segment labels */}
      {segLabels.map((sl,i)=>(
        <g key={i}><rect x={sl.mx-26} y={sl.my-16} width={52} height={18} rx={3} fill="white" fillOpacity={0.9}/><text x={sl.mx} y={sl.my-6} textAnchor="middle" fontSize={8} fill={sl.kleur} fontWeight="600">{sl.pijl} {sl.hoek}°</text></g>
      ))}
      {/* Y-as ticks */}
      {yTicks.map(h=>(<g key={h}><line x1={M.l-4} y1={yP(h)} x2={M.l} y2={yP(h)} stroke="#9ca3af"/><text x={M.l-6} y={yP(h)+3.5} textAnchor="end" fontSize={8} fill="#6b7280">{h>=0?"+":""}{h.toFixed(1)}</text></g>))}
      {/* X-as ticks */}
      {[0,0.25,0.5,0.75,1].map(f=>{const d=Math.round(f*totaal);return(<g key={f}><line x1={xP(d)} y1={H-M.b} x2={xP(d)} y2={H-M.b+4} stroke="#9ca3af"/><text x={xP(d)} y={H-M.b+13} textAnchor="middle" fontSize={8} fill="#6b7280">{d}m</text></g>);})}
      {/* As-labels */}
      <text x={10} y={H/2} fontSize={8} fill="#6b7280" transform={`rotate(-90,10,${H/2})`} textAnchor="middle">Hoogte (m NAP)</text>
      <text x={W/2} y={H-2} textAnchor="middle" fontSize={8} fill="#6b7280">Afstand langs boorlijn (m)</text>
      {/* NAP=0 label */}
      {hMin<0&&hMax>0&&<text x={M.l+4} y={yP(0)-2} fontSize={7} fill="#3b82f6">± 0.00 NAP</text>}
      {/* Frame */}
      <rect x={M.l} y={M.t} width={plotW} height={plotH} fill="none" stroke="#e5e7eb" strokeWidth={1}/>
      {/* Legenda */}
      <g transform={`translate(${M.l+8},${M.t+4})`}>
        <rect x={0} y={0} width={160} height={14} fill="white" fillOpacity={0.85} rx={3}/>
        <circle cx={10} cy={7} r={3} fill="#16a34a"/><text x={16} y={10} fontSize={7.5} fill="#374151">Maaiveld (AHN4)</text>
        <line x1={80} y1={7} x2={92} y2={7} stroke="#f97316" strokeWidth={2}/><circle cx={88} cy={7} r={2.5} fill="#f97316"/><text x={95} y={10} fontSize={7.5} fill="#374151">Boorpad</text>
        {(klicKruisingen?.length>0)&&<><circle cx={138} cy={7} r={3} fill="#ef4444"/><text x={144} y={10} fontSize={7.5} fill="#374151">KLIC</text></>}
      </g>
    </svg>
  );
}

// ─── Sectie / Rij componenten ─────────────────────────────────────────────────
function Sectie({titel,nr,kleur="#F97316",children}){
  return(
    <div style={{border:"1px solid #E5E7EB",borderRadius:8,overflow:"hidden",marginBottom:18,pageBreakInside:"avoid"}}>
      <div style={{background:kleur,padding:"7px 14px",display:"flex",alignItems:"center",gap:8}}>
        {nr&&<div style={{width:20,height:20,borderRadius:"50%",background:"rgba(255,255,255,0.25)",display:"flex",alignItems:"center",justifyContent:"center",fontSize:10,fontWeight:800,color:"white",flexShrink:0}}>{nr}</div>}
        <span style={{fontSize:11,fontWeight:700,color:"white",textTransform:"uppercase",letterSpacing:"0.05em"}}>{titel}</span>
      </div>
      <div style={{padding:"12px 14px"}}>{children}</div>
    </div>
  );
}
function Rij({label,waarde,highlight,wide}){
  return(
    <div style={{display:"flex",justifyContent:"space-between",alignItems:"baseline",padding:"3px 0",borderBottom:"1px solid #F9FAFB",gap:8}}>
      <span style={{fontSize:11,color:"#6B7280",flexShrink:0,width:wide?"55%":undefined}}>{label}</span>
      <span style={{fontSize:11,fontWeight:highlight?700:400,color:highlight?"#1F2937":"#374151",textAlign:"right"}}>{waarde??<em style={{color:"#9CA3AF"}}>—</em>}</span>
    </div>
  );
}
function Grid({children,cols=2}){return<div style={{display:"grid",gridTemplateColumns:`repeat(${cols},1fr)`,gap:"0 28px"}}>{children}</div>;}

// ─── MAIN ─────────────────────────────────────────────────────────────────────
export default function Eindontwerp({project,boringConfig:bcProp}){
  const reportRef=useRef(null);
  const bc=bcProp??parseJSON(project?.boring_config);
  const ahnData=parseJSON(project?.ahn_profiel);
  const machData=parseJSON(project?.machine_locaties);
  const analyse=parseJSON(project?.analyse_punten)??[];
  const traceGeo=parseJSON(project?.boortrace_geojson)??project?.boortrace_geojson;
  const traceCoords=traceGeo?.coordinates??[];
  const boringRes=useMemo(()=>bc?.items?.length?computeBoring(bc.items):null,[bc]);
  const traceLengte=traceLength(traceCoords);
  const traceBear=traceBearing(traceCoords);
  const profielPunten=ahnData?.profielPunten??[];
  const dieptePunten=ahnData?.dieptePunten??[];
  const totM=profielPunten.length?profielPunten[profielPunten.length-1]?.afstand??0:0;
  const geldig=profielPunten.filter(p=>p.hoogte!==null);

  // KLIC kruisingen berekenen vanuit project data
  const klicKruisingen=useMemo(()=>{
    if(!traceCoords.length||!geldig.length)return[];
    try{
      const boorCoords=traceCoords.map(([lng,lat])=>[lat,lng]);
      const klicSets=[];
      ["klic_ls","klic_ms","klic_gas","klic_water","klic_tele","klic_riool"].forEach(k=>{const raw=project?.[k];if(!raw)return;const gj=parseJSON(raw)??raw;(gj.features??[gj]).forEach(f=>{if(f?.geometry)klicSets.push({...f,_klicKey:k});});});
      try{const ss=typeof sessionStorage!=="undefined"?sessionStorage.getItem("klic_features"):null;if(ss)parseJSON(ss)?.forEach(f=>klicSets.push(f));}catch{}
      const kruisingen=[],cumul=cumulatiefAfstanden(boorCoords);
      klicSets.forEach(feat=>{
        const type=detecteerKlicType(feat),kType=KLIC_TYPES[type]??KLIC_TYPES.tele,geom=feat.geometry;if(!geom?.coordinates)return;
        const klicSegs=[];
        const flatten=cs=>{for(let i=0;i<cs.length-1;i++){const a=cs[i],b=cs[i+1];if(Array.isArray(a[0])){flatten(cs);return;}const rdA=latLngNaarRD(a[1]??a[0],a[0]??a[1]),rdB=latLngNaarRD(b[1]??b[0],b[0]??b[1]);klicSegs.push([rdA,rdB]);}};
        flatten(geom.coordinates);
        for(let bi=0;bi<boorCoords.length-1;bi++){
          const bA=latLngNaarRD(boorCoords[bi][0],boorCoords[bi][1]),bB=latLngNaarRD(boorCoords[bi+1][0],boorCoords[bi+1][1]),segLen=afstandM(boorCoords[bi],boorCoords[bi+1]);
          for(const[kA,kB]of klicSegs){const sn=segSnijpunt(bA,bB,kA,kB);if(sn){const afstand=cumul[bi]+sn.t*segLen,pp=geldig.find(p=>Math.abs(p.afstand-afstand)<2.5),hoogte=pp?.hoogte??0;kruisingen.push({afstand,hoogte,type,diepte:kType.diepte,label:kType.label,kleur:kType.kleur});}}
        }
      });
      return kruisingen.filter((k,i)=>!kruisingen.slice(0,i).some(k2=>Math.abs(k2.afstand-k.afstand)<3&&k2.type===k.type));
    }catch(e){console.warn("KLIC rapport:",e);return[];}
  },[traceCoords,geldig,project]);

  // BGT samenvatting
  const bgtSamenv=useMemo(()=>{
    if(!analyse.length)return[];
    const typen={};
    for(let i=0;i<analyse.length-1;i++){const seg=(analyse[i+1].positieM-analyse[i].positieM)||0;const k=analyse[i].oppervlak?.label??"Overig";typen[k]=(typen[k]||0)+seg;}
    const tot=Object.values(typen).reduce((s,v)=>s+v,0)||1;
    return Object.entries(typen).sort((a,b)=>b[1]-a[1]).map(([k,v])=>({label:k,m:v,pct:Math.round(v/tot*100)}));
  },[analyse]);

  // Diepte stats
  const napMin=geldig.length?Math.min(...geldig.map(p=>p.hoogte)):null;
  const napMax=geldig.length?Math.max(...geldig.map(p=>p.hoogte)):null;
  const maxDiepte=dieptePunten.length?Math.max(...dieptePunten.map(d=>d.diepte)):null;

  // Machine specs
  const MACHINES={d10x15:{label:"Vermeer D10x15 S3",push:"44.5 kN",koppel:"1.085 Nm",maxBoor:"Ø180 mm"},d20x22:{label:"Vermeer D20x22 S3",push:"86.7 kN",koppel:"2.983 Nm",maxBoor:"Ø250 mm"},d23x30:{label:"Vermeer D23x30 S3",push:"102 kN",koppel:"4.067 Nm",maxBoor:"Ø300 mm"},d36x50:{label:"Vermeer D36x50 S3",push:"160 kN",koppel:"6.779 Nm",maxBoor:"Ø400 mm"}};
  const machInfo=bc?.machine?MACHINES[bc.machine]:null;
  const today=new Date().toLocaleDateString("nl-NL",{day:"2-digit",month:"long",year:"numeric"});

  function handlePrint(){
    const html=`<!DOCTYPE html><html><head><meta charset="utf-8"><title>PrescanAI Rapport – ${project?.naam??""}</title>
    <style>body{font-family:system-ui,sans-serif;margin:0;padding:20px;background:white;}@page{size:A4 portrait;margin:15mm;}img{max-width:100%;}.no-print{display:none!important;}</style></head>
    <body>${reportRef.current?.innerHTML??""}</body></html>`;
    const w=window.open("","_blank");
    w.document.write(html);
    w.document.close();
    setTimeout(()=>w.print(),800);
  }

  return(
    <div style={{fontFamily:"system-ui,sans-serif",maxWidth:880}}>
      {/* Export knop */}
      <div className="no-print" style={{display:"flex",gap:12,marginBottom:20,alignItems:"center"}}>
        <button onClick={handlePrint} style={{display:"flex",alignItems:"center",gap:8,padding:"10px 20px",background:"#F97316",color:"white",border:"none",borderRadius:8,cursor:"pointer",fontSize:13,fontWeight:600,boxShadow:"0 2px 8px rgba(249,115,22,0.3)"}}>
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>
          Exporteer naar PDF
        </button>
        <span style={{fontSize:12,color:"#9CA3AF"}}>Opent afdrukdialoog in nieuw venster → Opslaan als PDF</span>
      </div>

      <div ref={reportRef}>
        {/* Kop */}
        <div style={{display:"flex",justifyContent:"space-between",alignItems:"flex-start",marginBottom:20,paddingBottom:14,borderBottom:"3px solid #F97316"}}>
          <div>
            <div style={{fontSize:24,fontWeight:900,color:"#F97316",letterSpacing:"-0.5px"}}>PrescanAI</div>
            <div style={{fontSize:12,color:"#6B7280",marginTop:2}}>HDD Horizontaal Gestuurd Boren — Prescan Rapport</div>
          </div>
          <div style={{textAlign:"right"}}>
            <div style={{fontSize:20,fontWeight:800,color:"#1F2937"}}>{project?.naam??"—"}</div>
            <div style={{fontSize:13,color:"#6B7280"}}>{project?.opdrachtgever??""} · {project?.locatie??""}</div>
            <div style={{fontSize:11,color:"#9CA3AF",marginTop:4}}>Gegenereerd: {today}</div>
          </div>
        </div>

        {/* 1. Projectgegevens */}
        <Sectie nr="1" titel="Projectgegevens">
          <Grid>
            <div>
              <Rij label="Projectnaam"     waarde={project?.naam}/>
              <Rij label="Opdrachtgever"   waarde={project?.opdrachtgever}/>
              <Rij label="Locatie"         waarde={project?.locatie}/>
              <Rij label="Status"          waarde={project?.status}/>
            </div>
            <div>
              <Rij label="Boorlengte (invoer)"  waarde={project?.boorlengte_m?`${project.boorlengte_m} m`:null}/>
              <Rij label="Boorlengte (tracé)"   waarde={traceLengte?`${traceLengte} m`:null} highlight/>
              <Rij label="Bodemtype"       waarde={project?.bodemtype}/>
              <Rij label="Materiaal"       waarde={project?.materiaal}/>
            </div>
          </Grid>
          {project?.bijzonderheden&&<div style={{marginTop:10,padding:"8px 10px",background:"#FFFBEB",borderRadius:6,border:"1px solid #FEF3C7",fontSize:12,color:"#374151"}}><strong style={{fontSize:10,color:"#92400E"}}>Bijzonderheden: </strong>{project.bijzonderheden}</div>}
        </Sectie>

        {/* 2. Boring configuratie + dwarsdoorsnede */}
        <Sectie nr="2" titel="Boring configuratie & dwarsdoorsnede" kleur="#EA580C">
          {(bc||boringRes)?(<>
            <div style={{display:"grid",gridTemplateColumns:"220px 1fr",gap:20,alignItems:"start",marginBottom:12}}>
              {/* Dwarsdoorsnede */}
              <div style={{textAlign:"center"}}>
                {boringRes
                  ?<BoringSVG res={boringRes} customPos={bc?.customPos??{}} size={210} showLabel={true}/>
                  :<div style={{width:210,height:210,background:"#F9FAFB",borderRadius:8,display:"flex",alignItems:"center",justifyContent:"center",fontSize:11,color:"#9CA3AF"}}>Geen cross-section</div>}
                <div style={{fontSize:10,color:"#6B7280",marginTop:4}}>Dwarsdoorsnede boring</div>
              </div>
              {/* Specs */}
              <div>
                <Grid>
                  <div>
                    <Rij label="Vereiste boring Ø" waarde={boringRes?.boringD?`Ø${boringRes.boringD} mm`:bc?.boringD?`Ø${bc.boringD} mm`:null} highlight/>
                    <Rij label="Productbundel Ø"   waarde={boringRes?.bundleD?`Ø${Math.round(boringRes.bundleD)} mm`:null}/>
                    <Rij label="Aantal items"      waarde={bc?.items?.length}/>
                  </div>
                  <div>
                    <Rij label="Machine"           waarde={machInfo?.label??bc?.machine??null} highlight/>
                    {machInfo&&<><Rij label="Max. trekracht" waarde={machInfo.push}/><Rij label="Max. koppel" waarde={machInfo.koppel}/><Rij label="Max. boring Ø" waarde={machInfo.maxBoor}/></>}
                  </div>
                </Grid>
                {/* Inhoud */}
                {bc?.items?.length>0&&(<>
                  <div style={{fontSize:10,fontWeight:700,color:"#6B7280",marginTop:12,marginBottom:6,textTransform:"uppercase",letterSpacing:"0.04em"}}>Inhoud boring</div>
                  <div style={{display:"flex",flexDirection:"column",gap:4}}>
                    {bc.items.map((item,idx)=>(
                      <div key={item.id??idx} style={{padding:"5px 8px",background:"#F9FAFB",borderRadius:5,border:"1px solid #E5E7EB"}}>
                        <div style={{fontSize:11,fontWeight:600,color:"#374151"}}>{item.type==="mb"?`PE${item.dn} mantelbuis (SDR11)`:item.label}</div>
                        {item.type==="mb"&&item.contents?.length>0&&<div style={{marginTop:2,paddingLeft:10}}>{item.contents.map((c,ci)=><div key={c.id??ci} style={{fontSize:10,color:"#6B7280"}}>• {c.label} (Ø{c.od}mm)</div>)}</div>}
                      </div>
                    ))}
                  </div>
                </>)}
              </div>
            </div>
          </>):<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen boring geconfigureerd in stap 1.</div>}
        </Sectie>

        {/* 3. Boorlijn kaart */}
        <Sectie nr="3" titel="Boorlijn — Tracé op de kaart" kleur="#1D4ED8">
          <Grid>
            <div>
              <Rij label="Tracélengte"   waarde={traceLengte?`${traceLengte} m`:null} highlight/>
              <Rij label="Richting"      waarde={bearingLabel(traceBear)}/>
              <Rij label="Aantal punten" waarde={traceCoords.length}/>
            </div>
            <div>
              <Rij label="Startpunt (WGS84)" waarde={traceCoords.length?`${traceCoords[0][1].toFixed(5)}°N  ${traceCoords[0][0].toFixed(5)}°E`:null}/>
              <Rij label="Eindpunt (WGS84)"  waarde={traceCoords.length?`${traceCoords[traceCoords.length-1][1].toFixed(5)}°N  ${traceCoords[traceCoords.length-1][0].toFixed(5)}°E`:null}/>
            </div>
          </Grid>
          <div style={{marginTop:14}}>
            <TracéKaart traceCoords={traceCoords}/>
          </div>
        </Sectie>

        {/* 4. Diepteligging & Dwarsprofiel */}
        <Sectie nr="4" titel="Diepteligging & Dwarsprofiel" kleur="#7C3AED">
          <Grid>
            <div>
              <Rij label="Maaiveld max (AHN4)" waarde={napMax!=null?nap(napMax):null}/>
              <Rij label="Maaiveld min (AHN4)" waarde={napMin!=null?nap(napMin):null}/>
              <Rij label="Max. boringdiepte"   waarde={maxDiepte!=null?`${maxDiepte.toFixed(2)} m`:null} highlight/>
            </div>
            <div>
              <Rij label="AHN4 meetpunten" waarde={geldig.length}/>
              <Rij label="Dieptepunten"    waarde={dieptePunten.length}/>
              <Rij label="KLIC kruisingen" waarde={klicKruisingen.length}/>
            </div>
          </Grid>
          <div style={{marginTop:16}}>
            <VolDwarsprofiel profielPunten={profielPunten} dieptePunten={dieptePunten} klicKruisingen={klicKruisingen} totM={totM}/>
          </div>
          {/* Dieptepunten tabel */}
          {dieptePunten.length>0&&(<>
            <div style={{fontSize:10,fontWeight:700,color:"#6B7280",marginTop:14,marginBottom:6,textTransform:"uppercase",letterSpacing:"0.04em"}}>Dieptepunten boorpad</div>
            <table style={{width:"100%",borderCollapse:"collapse",fontSize:11}}>
              <thead><tr style={{background:"#F5F3FF"}}>
                {["#","Afstand","Diepte","NAP hoogte","Segment hoek"].map(h=><th key={h} style={{padding:"4px 8px",textAlign:"left",fontWeight:600,color:"#6B7280",borderBottom:"1px solid #DDD6FE",fontSize:10}}>{h}</th>)}
              </tr></thead>
              <tbody>
                {[...dieptePunten].sort((a,b)=>a.afstand-b.afstand).map((dp,i,arr)=>{
                  const mv=geldig.find(p=>Math.abs(p.afstand-dp.afstand)<10)?.hoogte;
                  const napH=mv!=null?mv-dp.diepte:null;
                  let hoek="—";
                  if(i<arr.length-1){const next=arr[i+1];const mvN=geldig.find(p=>Math.abs(p.afstand-next.afstand)<10)?.hoogte;const napN=mvN!=null?mvN-next.diepte:null;if(napH!=null&&napN!=null){const dH=next.afstand-dp.afstand,dV=napN-napH;hoek=`${(Math.atan2(dV,dH)*180/Math.PI).toFixed(1)}°`;}}
                  return(<tr key={i} style={{borderBottom:"1px solid #F3F4F6"}}>
                    <td style={{padding:"3px 8px",color:"#9CA3AF"}}>{i+1}</td>
                    <td style={{padding:"3px 8px"}}>{dp.afstand.toFixed(1)} m</td>
                    <td style={{padding:"3px 8px",fontWeight:600,color:"#7C3AED"}}>{dp.diepte.toFixed(2)} m</td>
                    <td style={{padding:"3px 8px"}}>{napH!=null?nap(napH):"—"}</td>
                    <td style={{padding:"3px 8px",color:"#6B7280"}}>{hoek}</td>
                  </tr>);
                })}
              </tbody>
            </table>
          </>)}
          {/* KLIC tabel */}
          {klicKruisingen.length>0&&(<>
            <div style={{fontSize:10,fontWeight:700,color:"#6B7280",marginTop:14,marginBottom:6,textTransform:"uppercase",letterSpacing:"0.04em"}}>KLIC Kruisingen ({klicKruisingen.length})</div>
            <div style={{display:"flex",flexWrap:"wrap",gap:6}}>
              {klicKruisingen.map((k,i)=>{const kt=KLIC_TYPES[k.type]??KLIC_TYPES.tele;return(<div key={i} style={{display:"flex",alignItems:"center",gap:5,background:"#F9FAFB",border:"1px solid #E5E7EB",borderRadius:6,padding:"4px 8px"}}>
                <div style={{width:8,height:8,borderRadius:"50%",background:kt.kleur}}/>
                <span style={{fontSize:10,fontWeight:600,color:"#374151"}}>{kt.label}</span>
                <span style={{fontSize:10,color:"#6B7280"}}>@ {Math.round(k.afstand)}m</span>
                <span style={{fontSize:10,color:"#6B7280"}}>diepte {k.diepte}m</span>
              </div>);})}
            </div>
          </>)}
        </Sectie>

        {/* 5. BGT Oppervlakteanalyse */}
        <Sectie nr="5" titel="BGT Oppervlakteanalyse" kleur="#059669">
          {bgtSamenv.length>0?(<>
            <div style={{display:"flex",gap:6,flexWrap:"wrap",marginBottom:10}}>
              {bgtSamenv.map(({label,m,pct})=>(
                <div key={label} style={{padding:"5px 10px",background:"#ECFDF5",border:"1px solid #A7F3D0",borderRadius:6}}>
                  <div style={{fontSize:11,fontWeight:600,color:"#065F46"}}>{label}</div>
                  <div style={{fontSize:12,fontWeight:700,color:"#047857"}}>{Math.round(m)} m</div>
                  <div style={{fontSize:10,color:"#6B7280"}}>{pct}%</div>
                </div>
              ))}
            </div>
            <div style={{height:16,borderRadius:8,overflow:"hidden",display:"flex",marginBottom:6}}>
              {bgtSamenv.map(({label,pct},i)=>{const c=["#16A34A","#2563EB","#D97706","#7C3AED","#DC2626","#0891B2","#65A30D"];return<div key={label} style={{width:`${pct}%`,background:c[i%c.length]}}/>;  })}
            </div>
            <div style={{fontSize:11,color:"#6B7280"}}>Totaal: {Math.round(bgtSamenv.reduce((s,{m})=>s+m,0))} m · {analyse.length} meetpunten</div>
          </>):<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen BGT-analyse opgeslagen. Voer analyse uit in stap 5.</div>}
        </Sectie>

        {/* 6. Machine & Bentonietlocatie */}
        {machData&&(
          <Sectie nr="6" titel="Machine & Bentonietlocatie" kleur="#0891B2">
            <Grid>
              <div>
                <div style={{fontSize:10,fontWeight:700,color:"#6B7280",marginBottom:6,textTransform:"uppercase"}}>HDD Boormachine</div>
                <Rij label="Lengte"    waarde={machData.boormachine?.lengte?`${machData.boormachine.lengte} m`:null}/>
                <Rij label="Breedte"   waarde={machData.boormachine?.breedte?`${machData.boormachine.breedte} m`:null}/>
                <Rij label="Oppervlak" waarde={machData.boormachine?.lengte&&machData.boormachine?.breedte?`${machData.boormachine.lengte*machData.boormachine.breedte} m²`:null} highlight/>
              </div>
              <div>
                <div style={{fontSize:10,fontWeight:700,color:"#6B7280",marginBottom:6,textTransform:"uppercase"}}>Bentoniet & Opvangput</div>
                <Rij label="Lengte"    waarde={machData.bentoniet?.lengte?`${machData.bentoniet.lengte} m`:null}/>
                <Rij label="Breedte"   waarde={machData.bentoniet?.breedte?`${machData.bentoniet.breedte} m`:null}/>
                <Rij label="Oppervlak" waarde={machData.bentoniet?.lengte&&machData.bentoniet?.breedte?`${machData.bentoniet.lengte*machData.bentoniet.breedte} m²`:null} highlight/>
              </div>
            </Grid>
          </Sectie>
        )}

        {/* Footer */}
        <div style={{marginTop:28,paddingTop:14,borderTop:"1px solid #E5E7EB",display:"flex",justifyContent:"space-between",alignItems:"center"}}>
          <div style={{fontSize:10,color:"#9CA3AF"}}>PrescanAI · HDD Prescan Tool · prescanai.nl</div>
          <div style={{fontSize:10,color:"#9CA3AF"}}>{project?.naam??""} · {today}</div>
        </div>
      </div>
    </div>
  );
}
