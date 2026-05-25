"use client";
import { useMemo, useRef, useState } from "react";
import BoringSVG, { computeBoring } from "@/components/BoringSVG";

// ─── Helpers ──────────────────────────────────────────────────────────────────
const pJ = (v) => { try { return v ? (typeof v==="string" ? JSON.parse(v) : v) : null; } catch { return null; } };
function afstandM(p1,p2){const R=6371000,dLa=(p2[0]-p1[0])*Math.PI/180,dLn=(p2[1]-p1[1])*Math.PI/180,a=Math.sin(dLa/2)**2+Math.cos(p1[0]*Math.PI/180)*Math.cos(p2[0]*Math.PI/180)*Math.sin(dLn/2)**2;return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));}
function cumulatief(c){const r=[0];for(let i=1;i<c.length;i++)r.push(r[i-1]+afstandM(c[i-1],c[i]));return r;}
function latLngNaarRD(la,ln){const dLa=0.36*(la-52.15517440),dLn=0.36*(ln-5.38720621);return{x:155000+190094.945*dLn-11832.228*dLn*dLa-114.221*dLn*dLa*dLa-32.391*dLn*dLn*dLn,y:463000+309056.544*dLa+60940.388*dLn*dLn*dLa-9.941*dLn*dLn-2.340*dLa*dLa*dLa};}
function segSnijpunt(p1,p2,p3,p4){const dx1=p2.x-p1.x,dy1=p2.y-p1.y,dx2=p4.x-p3.x,dy2=p4.y-p3.y,cross=dx1*dy2-dy1*dx2;if(Math.abs(cross)<1e-8)return null;const t=((p3.x-p1.x)*dy2-(p3.y-p1.y)*dx2)/cross,u=((p3.x-p1.x)*dy1-(p3.y-p1.y)*dx1)/cross;if(t>=0&&t<=1&&u>=0&&u<=1)return{t};return null;}
const KLIC_T={ls:{label:"LS",kleur:"#ef4444",diepte:0.60},ms:{label:"MS",kleur:"#f97316",diepte:0.80},gas:{label:"Gas",kleur:"#eab308",diepte:0.80},water:{label:"Water",kleur:"#3b82f6",diepte:1.00},tele:{label:"Tele",kleur:"#8b5cf6",diepte:0.45},riool:{label:"Riool",kleur:"#6b7280",diepte:1.20}};
const NEN={LS:"#7B00AA",MS:"#00CCFF",Gas:"#FFFF00",Water:"#000080",Data:"#00CC00",KLIC:"#FF0000"};
function detectKlic(f){const n=(f.properties?.naam||f.properties?.thema||"").toLowerCase();if(n.includes("laagspan")||n.includes(" ls"))return"ls";if(n.includes("middensp")||n.includes(" ms"))return"ms";if(n.includes("gas"))return"gas";if(n.includes("water"))return"water";if(n.includes("tele")||n.includes("data")||n.includes("glas"))return"tele";if(n.includes("riool"))return"riool";return"tele";}
function traceLen(c){if(!c?.length||c.length<2)return null;let d=0;for(let i=1;i<c.length;i++){const[ln1,la1]=c[i-1],[ln2,la2]=c[i],R=6371000,f=Math.PI/180,a=Math.sin((la2-la1)*f/2)**2+Math.cos(la1*f)*Math.cos(la2*f)*Math.sin((ln2-ln1)*f/2)**2;d+=R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));}return Math.round(d);}
function bearingDeg(c){if(!c?.length||c.length<2)return null;const[ln1,la1]=c[0],[ln2,la2]=c[c.length-1],f=Math.PI/180,y=Math.sin((ln2-ln1)*f)*Math.cos(la2*f),x=Math.cos(la1*f)*Math.sin(la2*f)-Math.sin(la1*f)*Math.cos(la2*f)*Math.cos((ln2-ln1)*f);return Math.round((Math.atan2(y,x)*180/Math.PI+360)%360);}
function brg(d){if(d===null)return"—";const k=d<23||d>=338?"N":d<68?"NO":d<113?"O":d<158?"ZO":d<203?"Z":d<248?"ZW":d<293?"W":"NW";return`${d}° ${k}`;}
function nap(v){return v!=null?`${v>=0?"+":""}${v.toFixed(2)} m NAP`:"—";}
function maaiveldOp(a,pts){const g=pts.filter(p=>p.hoogte!==null);if(!g.length)return null;for(let i=0;i<g.length-1;i++){if(a>=g[i].afstand&&a<=g[i+1].afstand){const t=(a-g[i].afstand)/(g[i+1].afstand-g[i].afstand);return g[i].hoogte+t*(g[i+1].hoogte-g[i].hoogte);}}return null;}

// ─── WMS + SVG Kaart wrapper ──────────────────────────────────────────────────
function WmsKaart({lats,lngs,W=680,H=280,pad=0.35,children,label}){
  const[err,setErr]=useState(false);
  if(!lats?.length)return null;
  const minLa=Math.min(...lats),maxLa=Math.max(...lats),minLn=Math.min(...lngs),maxLn=Math.max(...lngs);
  const pLa=Math.max((maxLa-minLa)*pad,0.002),pLn=Math.max((maxLn-minLn)*pad,0.003);
  const bb={minLa:minLa-pLa,maxLa:maxLa+pLa,minLn:minLn-pLn,maxLn:maxLn+pLn};
  const laS=bb.maxLa-bb.minLa||0.001,lnS=bb.maxLn-bb.minLn||0.001;
  const toX=ln=>((ln-bb.minLn)/lnS*W).toFixed(1);
  const toY=la=>((1-(la-bb.minLa)/laS)*H).toFixed(1);
  const wms=`https://service.pdok.nl/brt/achtergrondkaart/wms/v2_0?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=grijs&CRS=EPSG:4326&BBOX=${bb.minLa},${bb.minLn},${bb.maxLa},${bb.maxLn}&WIDTH=${W}&HEIGHT=${H}&FORMAT=image/png`;
  return(
    <div style={{position:"relative",width:"100%",maxWidth:W,height:H,border:"1px solid #E5E7EB",borderRadius:8,overflow:"hidden",background:"#eef2f7"}}>
      {!err&&<img src={wms} alt="kaart" onError={()=>setErr(true)} style={{position:"absolute",width:"100%",height:"100%",objectFit:"fill"}}/>}
      {err&&<div style={{position:"absolute",inset:0,display:"flex",alignItems:"center",justifyContent:"center",flexDirection:"column",gap:4,fontSize:10,color:"#9CA3AF"}}>
        <span>📡 Kaart niet beschikbaar (offline)</span></div>}
      <svg viewBox={`0 0 ${W} ${H}`} style={{position:"absolute",top:0,left:0,width:"100%",height:"100%",overflow:"visible"}}>
        {typeof children==="function"?children({toX,toY}):children}
        <g transform={`translate(${W-28},28)`}><circle r={17} fill="white" fillOpacity={0.85} stroke="#E5E7EB"/><text x={0} y={-5} textAnchor="middle" fontSize={7} fontWeight="700" fill="#1F2937">N</text><polygon points="0,-12 -3.5,3 0,0 3.5,3" fill="#1F2937"/></g>
      </svg>
      {label&&<div style={{position:"absolute",bottom:6,left:8,background:"rgba(255,255,255,0.85)",borderRadius:4,padding:"2px 7px",fontSize:10,color:"#374151",border:"1px solid #E5E7EB"}}>{label}</div>}
    </div>
  );
}

// ─── Boorlijn op kaart ────────────────────────────────────────────────────────
function BoorlijnKaart({traceCoords,W=680,H=260}){
  const lats=traceCoords.map(c=>c[1]),lngs=traceCoords.map(c=>c[0]);
  return(
    <WmsKaart lats={lats} lngs={lngs} W={W} H={H} label={`Boorlijn · ${traceLen(traceCoords)??0} m`}>
      {({toX,toY})=><>
        <polyline points={traceCoords.map(([ln,la])=>`${toX(ln)},${toY(la)}`).join(" ")} fill="none" stroke="white" strokeWidth={5} strokeLinecap="round" opacity={0.6}/>
        <polyline points={traceCoords.map(([ln,la])=>`${toX(ln)},${toY(la)}`).join(" ")} fill="none" stroke="#1D4ED8" strokeWidth={3.5} strokeLinecap="round" strokeLinejoin="round"/>
        {traceCoords.length>0&&<><circle cx={toX(traceCoords[0][0])} cy={toY(traceCoords[0][1])} r={7} fill="#16A34A" stroke="white" strokeWidth={2}/><text x={+toX(traceCoords[0][0])+10} y={+toY(traceCoords[0][1])+4} fontSize={10} fontWeight="700" fill="#166534">Start</text></>}
        {traceCoords.length>1&&<><circle cx={toX(traceCoords[traceCoords.length-1][0])} cy={toY(traceCoords[traceCoords.length-1][1])} r={7} fill="#DC2626" stroke="white" strokeWidth={2}/><text x={+toX(traceCoords[traceCoords.length-1][0])+10} y={+toY(traceCoords[traceCoords.length-1][1])+4} fontSize={10} fontWeight="700" fill="#991B1B">Einde</text></>}
      </>}
    </WmsKaart>
  );
}

// ─── KLIC kaart (stap 3) ─────────────────────────────────────────────────────
const KLIC_VELDEN=[{key:"klic_ls",kleur:"#7B00AA",label:"LS"},{key:"klic_ms",kleur:"#00AACC",label:"MS"},{key:"klic_gas",kleur:"#D4A800",label:"Gas"},{key:"klic_water",kleur:"#000080",label:"Water"},{key:"klic_tele",kleur:"#16a34a",label:"Tele"},{key:"klic_riool",kleur:"#6b7280",label:"Riool"}];
function KlicKaart({project,traceCoords}){
  const sets=useMemo(()=>{
    const all=[];
    KLIC_VELDEN.forEach(({key,kleur,label})=>{const raw=pJ(project?.[key]);if(!raw)return;(raw.features??[raw]).forEach(f=>{if(f?.geometry?.coordinates){const coords=f.geometry.type==="LineString"?[f.geometry.coordinates]:f.geometry.type==="MultiLineString"?f.geometry.coordinates:[];coords.forEach(ls=>all.push({pts:ls,kleur,label}));}});});
    return all;
  },[project]);
  const allCoords=[...traceCoords,...sets.flatMap(s=>s.pts)];
  const lats=allCoords.map(c=>Array.isArray(c[0])?c[0][1]:c[1]??c[0]);
  const lngs=allCoords.map(c=>Array.isArray(c[0])?c[0][0]:c[0]??c[1]);
  if(!lats.length)return<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen KLIC-data geladen.</div>;
  const geladen=KLIC_VELDEN.filter(({key})=>project?.[key]);
  return(<>
    <WmsKaart lats={lats} lngs={lngs} W={680} H={280} pad={0.2} label="KLIC leidingen + boorlijn">
      {({toX,toY})=><>
        {/* KLIC lijnen */}
        {sets.map((s,i)=><polyline key={i} points={s.pts.map(p=>`${toX(p[0])},${toY(p[1])}`).join(" ")} fill="none" stroke={s.kleur} strokeWidth={2} opacity={0.85}/>)}
        {/* Boorlijn */}
        {traceCoords.length>1&&<><polyline points={traceCoords.map(([ln,la])=>`${toX(ln)},${toY(la)}`).join(" ")} fill="none" stroke="white" strokeWidth={5} strokeLinecap="round" opacity={0.5}/>
        <polyline points={traceCoords.map(([ln,la])=>`${toX(ln)},${toY(la)}`).join(" ")} fill="none" stroke="#1D4ED8" strokeWidth={3} strokeLinecap="round" strokeLinejoin="round"/></>}
        {/* Legenda */}
        <g>
          <rect x={8} y={8} width={130} height={geladen.length*13+10} fill="white" fillOpacity={0.9} rx={4} stroke="#E5E7EB"/>
          {geladen.map(({kleur,label},i)=><g key={label} transform={`translate(14,${18+i*13})`}><line x1={0} y1={0} x2={16} y2={0} stroke={kleur} strokeWidth={2.5}/><text x={20} y={4} fontSize={8.5} fill="#374151">{label}</text></g>)}
          <g transform={`translate(14,${18+geladen.length*13})`}><line x1={0} y1={0} x2={16} y2={0} stroke="#1D4ED8" strokeWidth={2.5}/><text x={20} y={4} fontSize={8.5} fill="#374151">Boorlijn</text></g>
        </g>
      </>}
    </WmsKaart>
  </>);
}

// ─── BGT Profiel (stap 5) ────────────────────────────────────────────────────
const BGT_K={"Gesloten verharding":"#6b7280","Open verharding":"#f59e0b","Half verhard":"#fbbf24","Onverhard":"#d97706","Groenvoorziening":"#16a34a","Water":"#2563eb","Spoor":"#dc2626","Overig":"#9ca3af"};
function BgtProfiel({analysePunten,totM}){
  if(!analysePunten?.length)return<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen BGT-analyse beschikbaar.</div>;
  const W=680,H=60,LW=64;
  const maxM=analysePunten[analysePunten.length-1]?.positieM??totM??1;
  const xP=m=>(m/maxM)*(W-LW);
  return(<>
    {/* Verhardingsbalk */}
    <svg viewBox={`0 0 ${W} ${H+20}`} style={{width:"100%",maxWidth:W,display:"block",marginBottom:8}}>
      <text x={0} y={12} fontSize={9} fill="#6B7280">0m</text>
      <text x={W-LW} y={12} textAnchor="end" fontSize={9} fill="#6B7280">{Math.round(maxM)}m</text>
      {analysePunten.slice(0,-1).map((p,i)=>{
        const next=analysePunten[i+1];
        const x1=xP(p.positieM),x2=xP(next.positieM);
        const kleur=p.oppervlak?.kleur??BGT_K[p.oppervlak?.label]??"#9ca3af";
        return(<rect key={i} x={x1} y={16} width={Math.max(x2-x1,0.5)} height={28} fill={kleur}/>);
      })}
      <rect x={0} y={16} width={W-LW} height={28} fill="none" stroke="#E5E7EB" strokeWidth={0.5}/>
      {[0,0.25,0.5,0.75,1].map(f=><line key={f} x1={xP(f*maxM)} y1={44} x2={xP(f*maxM)} y2={48} stroke="#9ca3af"/>)}
      {[0,0.25,0.5,0.75,1].map(f=><text key={f} x={xP(f*maxM)} y={56} textAnchor="middle" fontSize={8} fill="#6B7280">{Math.round(f*maxM)}m</text>)}
    </svg>
    {/* Legenda + tabel */}
    {(()=>{
      const typen={};
      for(let i=0;i<analysePunten.length-1;i++){const seg=(analysePunten[i+1].positieM-analysePunten[i].positieM)||0;const k=analysePunten[i].oppervlak?.label??"Overig";typen[k]={m:(typen[k]?.m||0)+seg,kleur:analysePunten[i].oppervlak?.kleur??BGT_K[k]??"#9ca3af"};}
      const tot=Object.values(typen).reduce((s,v)=>s+v.m,0)||1;
      return(<div style={{display:"flex",flexWrap:"wrap",gap:6}}>
        {Object.entries(typen).sort((a,b)=>b[1].m-a[1].m).map(([label,{m,kleur}])=>(
          <div key={label} style={{display:"flex",alignItems:"center",gap:5,background:"#F9FAFB",border:"1px solid #E5E7EB",borderRadius:5,padding:"3px 8px"}}>
            <div style={{width:10,height:10,borderRadius:2,background:kleur,flexShrink:0}}/>
            <span style={{fontSize:10,fontWeight:600,color:"#374151"}}>{label}</span>
            <span style={{fontSize:10,color:"#6B7280"}}>{Math.round(m)}m · {Math.round(m/tot*100)}%</span>
          </div>
        ))}
      </div>);
    })()}
  </>);
}

// ─── Dwarsprofiel SVG (stap 6) ───────────────────────────────────────────────
function DwarsprofielSVG({profielPunten,dieptePunten,klicKruisingen,totM}){
  const W=720,H=200,M={l:52,r:20,t:20,b:36};
  const geldig=(profielPunten||[]).filter(p=>p.hoogte!==null);
  if(geldig.length<2)return<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic",padding:"12px 0"}}>Geen AHN4 profiel — voer diepteligging uit in stap 6.</div>;
  const hMax=Math.max(...geldig.map(p=>p.hoogte))+0.6;
  const maxD=Math.max(...(dieptePunten||[]).map(p=>p.diepte),0)+1.2;
  const hMin=Math.min(...geldig.map(p=>p.hoogte))-maxD;
  const hSpan=hMax-hMin||1,plotW=W-M.l-M.r,plotH=H-M.t-M.b;
  const totaal=totM||geldig[geldig.length-1]?.afstand||1;
  const xP=d=>M.l+d/totaal*plotW, yP=h=>M.t+(hMax-h)/hSpan*plotH;
  const mvPts=geldig.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");
  const vlak=`${xP(geldig[0].afstand)},${H-M.b} ${mvPts} ${xP(geldig[geldig.length-1].afstand)},${H-M.b}`;
  const sortedWP=[...(dieptePunten||[])].sort((a,b)=>a.afstand-b.afstand);
  const boorWP=sortedWP.map(dp=>({a:dp.afstand,h:(maaiveldOp(dp.afstand,geldig)??0)-dp.diepte,d:dp.diepte}));
  const boorPoly=boorWP.length>=2?boorWP.map(p=>`${xP(p.a)},${yP(p.h)}`).join(" "):"";
  const ticks=[];const ts=hSpan>10?2:hSpan>5?1:0.5;
  for(let h=Math.ceil(hMin/ts)*ts;h<=hMax;h+=ts)ticks.push(h);
  const segL=boorWP.slice(0,-1).map((wp,i)=>{const nxt=boorWP[i+1];const mx=(xP(wp.a)+xP(nxt.a))/2,my=(yP(wp.h)+yP(nxt.h))/2;const dH=nxt.a-wp.a,dV=nxt.h-wp.h;const hoek=Math.atan2(dV,dH)*180/Math.PI;const pijl=hoek>0.5?"↗":hoek<-0.5?"↘":"→";const kleur=Math.abs(hoek)>15?"#dc2626":Math.abs(hoek)>8?"#f97316":"#16a34a";return{mx,my,hoek:hoek.toFixed(1),pijl,kleur};});
  return(
    <svg width="100%" viewBox={`0 0 ${W} ${H}`} style={{display:"block",maxWidth:W}}>
      <rect x={M.l} y={M.t} width={plotW} height={plotH} fill="#F9FAFB"/>
      <polygon points={vlak} fill="#86efac" opacity={0.35}/>
      <polyline points={mvPts} fill="none" stroke="#16a34a" strokeWidth={2}/>
      {hMin<0&&hMax>0&&<line x1={M.l} y1={yP(0)} x2={W-M.r} y2={yP(0)} stroke="#3b82f6" strokeWidth={0.8} strokeDasharray="5,4" opacity={0.7}/>}
      {/* KLIC */}
      {(klicKruisingen||[]).map((k,i)=>{const kt=KLIC_T[k.type]??KLIC_T.tele;const x=xP(k.afstand),yTop=M.t,yBot=yP(k.hoogte-k.diepte);return(<g key={i}><line x1={x} y1={yTop} x2={x} y2={yBot} stroke={kt.kleur} strokeWidth={1.5} strokeDasharray="4,3" opacity={0.8}/><circle cx={x} cy={yBot} r={4} fill={kt.kleur} stroke="white" strokeWidth={1}/><rect x={x-11} y={yTop} width={22} height={11} rx={2} fill={kt.kleur} opacity={0.9}/><text x={x} y={yTop+8} textAnchor="middle" fontSize={7} fontWeight="700" fill="white">{kt.label}</text></g>);})}
      {/* Boorpad */}
      {boorPoly&&<>
        <polyline points={boorPoly} fill="none" stroke="#f97316" strokeWidth={3} strokeLinecap="round" strokeLinejoin="round"/>
        {sortedWP.map((dp,i)=>{const mv=maaiveldOp(dp.afstand,geldig);const napH=mv!=null?mv-dp.diepte:null;const x=xP(dp.afstand),y=yP(napH??hMin+0.5);const isS=i===0,isE=i===sortedWP.length-1;return(<g key={i}><circle cx={x} cy={y} r={isS||isE?6:4} fill={isS?"#16a34a":isE?"#dc2626":"#f97316"} stroke="white" strokeWidth={1.5}/><rect x={x-22} y={y-20} width={44} height={22} rx={3} fill="white" stroke="#f97316" strokeWidth={0.8} opacity={0.95}/><text x={x} y={y-11} textAnchor="middle" fontSize={7} fill="#ea580c" fontWeight="700">{dp.diepte.toFixed(2)}m</text><text x={x} y={y-4} textAnchor="middle" fontSize={6.5} fill="#6b7280">{napH!=null?nap(napH):"—"}</text></g>);})}
        {segL.map((sl,i)=><g key={i}><rect x={sl.mx-25} y={sl.my-15} width={50} height={16} rx={3} fill="white" fillOpacity={0.88}/><text x={sl.mx} y={sl.my-5} textAnchor="middle" fontSize={7.5} fill={sl.kleur} fontWeight="600">{sl.pijl} {sl.hoek}°</text></g>)}
      </>}
      {ticks.map(h=><g key={h}><line x1={M.l-4} y1={yP(h)} x2={M.l} y2={yP(h)} stroke="#9ca3af"/><text x={M.l-6} y={yP(h)+3.5} textAnchor="end" fontSize={8} fill="#6b7280">{h>=0?"+":""}{h.toFixed(1)}</text></g>)}
      {[0,0.25,0.5,0.75,1].map(f=>{const d=Math.round(f*totaal);return(<g key={f}><line x1={xP(d)} y1={H-M.b} x2={xP(d)} y2={H-M.b+4} stroke="#9ca3af"/><text x={xP(d)} y={H-M.b+13} textAnchor="middle" fontSize={8} fill="#6b7280">{d}m</text></g>);})}
      <text x={10} y={H/2} fontSize={8} fill="#6b7280" transform={`rotate(-90,10,${H/2})`} textAnchor="middle">Hoogte (m NAP)</text>
      {hMin<0&&hMax>0&&<text x={M.l+4} y={yP(0)-3} fontSize={7} fill="#3b82f6">± 0.00</text>}
      <rect x={M.l} y={M.t} width={plotW} height={plotH} fill="none" stroke="#e5e7eb"/>
      <g transform={`translate(${M.l+6},${M.t+4})`}>
        <rect x={0} y={0} width={(klicKruisingen?.length?182:132)} height={12} fill="white" fillOpacity={0.85} rx={3}/>
        <circle cx={8} cy={6} r={3} fill="#16a34a"/><text x={14} y={9} fontSize={7.5} fill="#374151">Maaiveld AHN4</text>
        <line x1={80} y1={6} x2={92} y2={6} stroke="#f97316" strokeWidth={2}/><circle cx={86} cy={6} r={2.5} fill="#f97316"/><text x={95} y={9} fontSize={7.5} fill="#374151">Boorpad</text>
        {klicKruisingen?.length>0&&<><circle cx={140} cy={6} r={3} fill="#ef4444"/><text x={146} y={9} fontSize={7.5} fill="#374151">KLIC</text></>}
      </g>
    </svg>
  );
}

// ─── Stap layout helpers ──────────────────────────────────────────────────────
const STAP_KLEUREN=["#F97316","#7C3AED","#0891B2","#1D4ED8","#059669","#7C3AED","#0891B2","#374151"];
function Stap({nr,titel,children}){
  const kleur=STAP_KLEUREN[(nr-1)%STAP_KLEUREN.length];
  return(
    <div style={{border:`1px solid ${kleur}30`,borderRadius:10,overflow:"hidden",marginBottom:20,pageBreakInside:"avoid"}}>
      <div style={{background:kleur,padding:"8px 16px",display:"flex",alignItems:"center",gap:10}}>
        <div style={{width:24,height:24,borderRadius:"50%",background:"rgba(255,255,255,0.25)",display:"flex",alignItems:"center",justifyContent:"center",fontSize:11,fontWeight:800,color:"white",flexShrink:0}}>{nr}</div>
        <span style={{fontSize:12,fontWeight:700,color:"white",textTransform:"uppercase",letterSpacing:"0.05em"}}>{titel}</span>
      </div>
      <div style={{padding:"14px 16px",background:"white"}}>{children}</div>
    </div>
  );
}
function Grid({children,cols=2,gap=24}){return<div style={{display:"grid",gridTemplateColumns:`repeat(${cols},1fr)`,gap:`0 ${gap}px`}}>{children}</div>;}
function Rij({label,waarde,highlight}){return(<div style={{display:"flex",justifyContent:"space-between",alignItems:"baseline",padding:"3px 0",borderBottom:"1px solid #F9FAFB"}}><span style={{fontSize:11,color:"#6B7280"}}>{label}</span><span style={{fontSize:11,fontWeight:highlight?700:400,color:highlight?"#1F2937":"#374151"}}>{waarde??<em style={{color:"#9CA3AF"}}>—</em>}</span></div>);}
function Sub({titel}){return<div style={{fontSize:10,fontWeight:700,color:"#6B7280",marginBottom:5,marginTop:10,textTransform:"uppercase",letterSpacing:"0.04em"}}>{titel}</div>;}

// ─── MAIN ─────────────────────────────────────────────────────────────────────
export default function Eindontwerp({project,boringConfig:bcProp}){
  const reportRef=useRef(null);
  const bc=bcProp??pJ(project?.boring_config);
  const ahnData=pJ(project?.ahn_profiel);
  const machData=pJ(project?.machine_locaties);
  const analyse=pJ(project?.analyse_punten)??[];
  const traceGeo=pJ(project?.boortrace_geojson)??project?.boortrace_geojson;
  const traceCoords=traceGeo?.coordinates??[];
  const bestanden=pJ(project?.bestanden_meta)??[];
  const boringRes=useMemo(()=>bc?.items?.length?computeBoring(bc.items):null,[bc]);
  const traceLengte=traceLen(traceCoords);
  const traceBear=bearingDeg(traceCoords);
  const profielPunten=ahnData?.profielPunten??[];
  const dieptePunten=ahnData?.dieptePunten??[];
  const geldig=profielPunten.filter(p=>p.hoogte!==null);
  const totM=geldig.length?geldig[geldig.length-1]?.afstand??0:0;
  const napMin=geldig.length?Math.min(...geldig.map(p=>p.hoogte)):null;
  const napMax=geldig.length?Math.max(...geldig.map(p=>p.hoogte)):null;
  const maxDiepte=dieptePunten.length?Math.max(...dieptePunten.map(d=>d.diepte)):null;

  const MACHINES={d10x15:{label:"Vermeer D10x15 S3",push:"44.5 kN",koppel:"1.085 Nm",maxBoor:"Ø180 mm"},d20x22:{label:"Vermeer D20x22 S3",push:"86.7 kN",koppel:"2.983 Nm",maxBoor:"Ø250 mm"},d23x30:{label:"Vermeer D23x30 S3",push:"102 kN",koppel:"4.067 Nm",maxBoor:"Ø300 mm"},d36x50:{label:"Vermeer D36x50 S3",push:"160 kN",koppel:"6.779 Nm",maxBoor:"Ø400 mm"}};
  const machInfo=bc?.machine?MACHINES[bc.machine]:null;

  // KLIC kruisingen voor dwarsprofiel
  const klicKruisingen=useMemo(()=>{
    if(!traceCoords.length||!geldig.length)return[];
    try{
      const boorC=traceCoords.map(([ln,la])=>[la,ln]);
      const sets=[];
      ["klic_ls","klic_ms","klic_gas","klic_water","klic_tele","klic_riool"].forEach(k=>{const raw=pJ(project?.[k])??project?.[k];if(!raw)return;(raw.features??[raw]).forEach(f=>{if(f?.geometry)sets.push({...f,_k:k});});});
      const cumul=cumulatief(boorC),kruisingen=[];
      sets.forEach(feat=>{
        const type=detectKlic(feat),kType=KLIC_T[type]??KLIC_T.tele,geom=feat.geometry;if(!geom?.coordinates)return;
        const segs=[];
        const flat=cs=>{for(let i=0;i<cs.length-1;i++){const a=cs[i],b=cs[i+1];if(Array.isArray(a[0])){flat(cs);return;}segs.push([latLngNaarRD(a[1]??a[0],a[0]??a[1]),latLngNaarRD(b[1]??b[0],b[0]??b[1])]);}};
        flat(geom.coordinates);
        for(let bi=0;bi<boorC.length-1;bi++){
          const bA=latLngNaarRD(boorC[bi][0],boorC[bi][1]),bB=latLngNaarRD(boorC[bi+1][0],boorC[bi+1][1]),segL=afstandM(boorC[bi],boorC[bi+1]);
          for(const[kA,kB]of segs){const sn=segSnijpunt(bA,bB,kA,kB);if(sn){const afs=cumul[bi]+sn.t*segL,pp=geldig.find(p=>Math.abs(p.afstand-afs)<2.5),h=pp?.hoogte??0;kruisingen.push({afstand:afs,hoogte:h,type,diepte:kType.diepte,label:kType.label,kleur:kType.kleur});}}
        }
      });
      return kruisingen.filter((k,i)=>!kruisingen.slice(0,i).some(k2=>Math.abs(k2.afstand-k.afstand)<3&&k2.type===k.type));
    }catch(e){return[];}
  },[traceCoords,geldig,project]);

  const KLIC_TYPEN_GELADEN=KLIC_VELDEN.filter(({key})=>project?.[key]);
  const today=new Date().toLocaleDateString("nl-NL",{day:"2-digit",month:"long",year:"numeric"});

  function handlePrint(){
    const html=`<!DOCTYPE html><html><head><meta charset="utf-8"><title>PrescanAI Rapport – ${project?.naam??""}</title><style>body{font-family:system-ui,sans-serif;margin:0;padding:20px;background:white}@page{size:A4 portrait;margin:14mm}img{max-width:100%}.no-print{display:none!important}</style></head><body>${reportRef.current?.innerHTML??""}</body></html>`;
    const w=window.open("","_blank");if(!w)return alert("Pop-up geblokkeerd.");
    w.document.write(html);w.document.close();setTimeout(()=>w.print(),900);
  }

  return(
    <div style={{fontFamily:"system-ui,sans-serif",maxWidth:900}}>
      {/* Export knop */}
      <div className="no-print" style={{display:"flex",gap:12,marginBottom:20,alignItems:"center"}}>
        <button onClick={handlePrint} style={{display:"flex",alignItems:"center",gap:8,padding:"10px 20px",background:"#F97316",color:"white",border:"none",borderRadius:8,cursor:"pointer",fontSize:13,fontWeight:600,boxShadow:"0 2px 8px rgba(249,115,22,0.3)"}}>
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>
          Exporteer naar PDF
        </button>
        <span style={{fontSize:12,color:"#9CA3AF"}}>Opent nieuw venster → Afdrukken → Opslaan als PDF</span>
      </div>

      <div ref={reportRef}>
        {/* Koptekst */}
        <div style={{display:"flex",justifyContent:"space-between",alignItems:"flex-start",marginBottom:20,paddingBottom:14,borderBottom:"3px solid #F97316"}}>
          <div><div style={{fontSize:22,fontWeight:900,color:"#F97316",letterSpacing:"-0.5px"}}>PrescanAI</div><div style={{fontSize:11,color:"#6B7280",marginTop:2}}>HDD Horizontaal Gestuurd Boren — Prescan Rapportage</div></div>
          <div style={{textAlign:"right"}}><div style={{fontSize:20,fontWeight:800,color:"#1F2937"}}>{project?.naam??"—"}</div><div style={{fontSize:12,color:"#6B7280"}}>{project?.opdrachtgever??""}{project?.locatie?` · ${project.locatie}`:""}</div><div style={{fontSize:11,color:"#9CA3AF",marginTop:4}}>Gegenereerd: {today}</div></div>
        </div>

        {/* STAP 1 — Projectinformatie + Boring configuratie */}
        <Stap nr={1} titel="Projectinformatie & Boring configuratie">
          <Grid>
            <div>
              <Sub titel="Projectgegevens"/>
              <Rij label="Projectnaam"     waarde={project?.naam}/>
              <Rij label="Opdrachtgever"   waarde={project?.opdrachtgever}/>
              <Rij label="Locatie"         waarde={project?.locatie}/>
              <Rij label="Status"          waarde={project?.status}/>
              <Rij label="Bodemtype"       waarde={project?.bodemtype}/>
              <Rij label="Materiaal"       waarde={project?.materiaal}/>
              <Rij label="Boorlengte (invoer)" waarde={project?.boorlengte_m?`${project.boorlengte_m} m`:null}/>
              <Rij label="Boorlengte (tracé)"  waarde={traceLengte?`${traceLengte} m`:null} highlight/>
              {project?.bijzonderheden&&<div style={{marginTop:8,padding:"6px 9px",background:"#FFFBEB",borderRadius:5,border:"1px solid #FEF3C7",fontSize:11,color:"#374151"}}><strong style={{color:"#92400E"}}>Bijzonderheden: </strong>{project.bijzonderheden}</div>}
            </div>
            <div>
              <Sub titel="Boring configuratie"/>
              {boringRes?<>
                <div style={{textAlign:"center",marginBottom:8}}>
                  <BoringSVG res={boringRes} customPos={bc?.customPos??{}} size={200} showLabel={true}/>
                  <div style={{fontSize:10,color:"#6B7280",marginTop:3}}>Dwarsdoorsnede boring</div>
                </div>
                <Rij label="Vereiste boring Ø" waarde={`Ø${boringRes.boringD} mm`} highlight/>
                <Rij label="Productbundel Ø"   waarde={`Ø${Math.round(boringRes.bundleD)} mm`}/>
                <Rij label="Machine"           waarde={machInfo?.label??bc?.machine??null} highlight/>
                {machInfo&&<><Rij label="Max. trekracht" waarde={machInfo.push}/><Rij label="Max. koppel" waarde={machInfo.koppel}/></>}
                <Rij label="Aantal items" waarde={bc?.items?.length}/>
              </>:<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen boring geconfigureerd.</div>}
            </div>
          </Grid>
          {/* Inhoud items */}
          {bc?.items?.length>0&&<>
            <Sub titel="Inhoud boring"/>
            <div style={{display:"flex",flexDirection:"column",gap:3}}>
              {bc.items.map((item,idx)=>(
                <div key={item.id??idx} style={{padding:"4px 8px",background:"#F9FAFB",borderRadius:5,border:"1px solid #E5E7EB",fontSize:11}}>
                  <strong style={{color:"#374151"}}>{item.type==="mb"?`PE${item.dn} mantelbuis (SDR11)`:item.label}</strong>
                  {item.type==="mb"&&item.contents?.length>0&&<span style={{color:"#6B7280"}}>{" — "}{item.contents.map(c=>`${c.label} (Ø${c.od}mm)`).join(", ")}</span>}
                </div>
              ))}
            </div>
          </>}
        </Stap>

        {/* STAP 2 — Ontwerp inladen */}
        <Stap nr={2} titel="Ontwerp inladen — KLIC leidingen">
          {bestanden.length>0||KLIC_TYPEN_GELADEN.length>0?(
            <Grid>
              <div>
                <Sub titel="Geladen bestanden"/>
                {bestanden.length>0?bestanden.map((b,i)=>(
                  <div key={i} style={{display:"flex",alignItems:"center",gap:8,padding:"3px 0",borderBottom:"1px solid #F9FAFB"}}>
                    <div style={{width:8,height:8,borderRadius:"50%",background:NEN[b.type]??"#9CA3AF",flexShrink:0}}/>
                    <span style={{fontSize:11,fontWeight:600,color:"#374151"}}>{b.type}</span>
                    <span style={{fontSize:11,color:"#6B7280"}}>{b.naam??b.bestandsnaam??""}</span>
                  </div>
                )):<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen bestanden gevonden in metadata.</div>}
              </div>
              <div>
                <Sub titel="KLIC data beschikbaar"/>
                {KLIC_TYPEN_GELADEN.length>0?KLIC_TYPEN_GELADEN.map(({key,kleur,label})=>(
                  <div key={key} style={{display:"flex",alignItems:"center",gap:8,padding:"3px 0",borderBottom:"1px solid #F9FAFB"}}>
                    <div style={{width:8,height:8,borderRadius:"50%",background:kleur,flexShrink:0}}/>
                    <span style={{fontSize:11,fontWeight:600,color:"#374151"}}>{label}</span>
                    <span style={{fontSize:11,color:"#16A34A"}}>✓ geladen</span>
                  </div>
                )):<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen KLIC-data.</div>}
              </div>
            </Grid>
          ):<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen bestanden geladen in stap 2.</div>}
        </Stap>

        {/* STAP 3 — Ontwerp bekijken */}
        <Stap nr={3} titel="Ontwerp bekijken — KLIC leidingen op kaart">
          <KlicKaart project={project} traceCoords={traceCoords}/>
          {klicKruisingen.length>0&&<>
            <Sub titel={`${klicKruisingen.length} KLIC kruisingen met boorlijn`}/>
            <div style={{display:"flex",flexWrap:"wrap",gap:5}}>
              {klicKruisingen.map((k,i)=>{const kt=KLIC_T[k.type]??KLIC_T.tele;return(<div key={i} style={{display:"flex",alignItems:"center",gap:4,background:"#F9FAFB",border:"1px solid #E5E7EB",borderRadius:5,padding:"3px 7px"}}>
                <div style={{width:7,height:7,borderRadius:"50%",background:kt.kleur}}/><span style={{fontSize:10,fontWeight:600,color:"#374151"}}>{kt.label}</span><span style={{fontSize:10,color:"#6B7280"}}>@ {Math.round(k.afstand)}m · {k.diepte}m diep</span>
              </div>);})}
            </div>
          </>}
        </Stap>

        {/* STAP 4 — Boorlijn tekenen */}
        <Stap nr={4} titel="Boorlijn tekenen — Tracé op de kaart">
          <Grid cols={3} gap={16}>
            <div><Rij label="Tracélengte"   waarde={traceLengte?`${traceLengte} m`:null} highlight/><Rij label="Richting"      waarde={brg(traceBear)}/><Rij label="Punten"        waarde={traceCoords.length}/></div>
            <div><Rij label="Startpunt"     waarde={traceCoords.length?`${traceCoords[0][1].toFixed(5)}°N`:null}/><Rij label="" waarde={traceCoords.length?`${traceCoords[0][0].toFixed(5)}°E`:null}/></div>
            <div><Rij label="Eindpunt"      waarde={traceCoords.length?`${traceCoords[traceCoords.length-1][1].toFixed(5)}°N`:null}/><Rij label="" waarde={traceCoords.length?`${traceCoords[traceCoords.length-1][0].toFixed(5)}°E`:null}/></div>
          </Grid>
          <div style={{marginTop:12}}>
            {traceCoords.length>=2?<BoorlijnKaart traceCoords={traceCoords}/>:<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen boorlijn getekend in stap 4.</div>}
          </div>
        </Stap>

        {/* STAP 5 — Oppervlakteanalyse */}
        <Stap nr={5} titel="Oppervlakteanalyse — BGT verhardingsprofiel">
          <BgtProfiel analysePunten={analyse} totM={traceLengte??totM}/>
          {analyse.length>0&&<div style={{marginTop:10,fontSize:11,color:"#6B7280"}}>{analyse.length} meetpunten langs {Math.round(analyse[analyse.length-1]?.positieM??0)} m tracé</div>}
        </Stap>

        {/* STAP 6 — Diepteligging */}
        <Stap nr={6} titel="Diepteligging & Dwarsprofiel">
          <Grid>
            <div>
              <Rij label="Maaiveld max (AHN4)" waarde={napMax!=null?nap(napMax):null}/>
              <Rij label="Maaiveld min (AHN4)" waarde={napMin!=null?nap(napMin):null}/>
              <Rij label="Max. boringdiepte"   waarde={maxDiepte!=null?`${maxDiepte.toFixed(2)} m`:null} highlight/>
            </div>
            <div>
              <Rij label="AHN4 meetpunten" waarde={geldig.length||null}/>
              <Rij label="Dieptepunten"    waarde={dieptePunten.length||null}/>
              <Rij label="KLIC kruisingen" waarde={klicKruisingen.length||null}/>
            </div>
          </Grid>
          <div style={{marginTop:14}}>
            <DwarsprofielSVG profielPunten={profielPunten} dieptePunten={dieptePunten} klicKruisingen={klicKruisingen} totM={totM}/>
          </div>
          {/* Dieptepunten tabel */}
          {dieptePunten.length>0&&<>
            <Sub titel="Dieptepunten boorpad"/>
            <table style={{width:"100%",borderCollapse:"collapse",fontSize:11}}>
              <thead><tr style={{background:"#F5F3FF"}}>{["#","Afstand","Diepte","NAP hoogte","Segment hoek"].map(h=><th key={h} style={{padding:"3px 8px",textAlign:"left",fontWeight:600,color:"#6B7280",borderBottom:"1px solid #DDD6FE",fontSize:10}}>{h}</th>)}</tr></thead>
              <tbody>{[...dieptePunten].sort((a,b)=>a.afstand-b.afstand).map((dp,i,arr)=>{
                const mv=maaiveldOp(dp.afstand,geldig),napH=mv!=null?mv-dp.diepte:null;
                let hoek="—";if(i<arr.length-1){const nx=arr[i+1];const mvN=maaiveldOp(nx.afstand,geldig);const napN=mvN!=null?mvN-nx.diepte:null;if(napH!=null&&napN!=null){const dH=nx.afstand-dp.afstand,dV=napN-napH;hoek=`${(Math.atan2(dV,dH)*180/Math.PI).toFixed(1)}°`;}}
                return(<tr key={i} style={{borderBottom:"1px solid #F3F4F6"}}><td style={{padding:"2px 8px",color:"#9CA3AF"}}>{i+1}</td><td style={{padding:"2px 8px"}}>{dp.afstand.toFixed(1)} m</td><td style={{padding:"2px 8px",fontWeight:600,color:"#7C3AED"}}>{dp.diepte.toFixed(2)} m</td><td style={{padding:"2px 8px"}}>{napH!=null?nap(napH):"—"}</td><td style={{padding:"2px 8px",color:"#6B7280"}}>{hoek}</td></tr>);
              })}</tbody>
            </table>
          </>}
        </Stap>

        {/* STAP 7 — Machine locatie */}
        <Stap nr={7} titel="Machine & Bentonietlocatie">
          {machData?<>
            <Grid>
              <div>
                <Sub titel="HDD Boormachine"/>
                <Rij label="Machine type" waarde={machInfo?.label??bc?.machine??null} highlight/>
                <Rij label="Lengte"       waarde={machData.boormachine?.lengte?`${machData.boormachine.lengte} m`:null}/>
                <Rij label="Breedte"      waarde={machData.boormachine?.breedte?`${machData.boormachine.breedte} m`:null}/>
                <Rij label="Oppervlak"    waarde={machData.boormachine?.lengte&&machData.boormachine?.breedte?`${machData.boormachine.lengte*machData.boormachine.breedte} m²`:null} highlight/>
                {machInfo&&<><Rij label="Max. trekracht" waarde={machInfo.push}/><Rij label="Max. koppel" waarde={machInfo.koppel}/><Rij label="Max. boring Ø" waarde={machInfo.maxBoor}/></>}
              </div>
              <div>
                <Sub titel="Bentoniet & Opvangput"/>
                <Rij label="Lengte"    waarde={machData.bentoniet?.lengte?`${machData.bentoniet.lengte} m`:null}/>
                <Rij label="Breedte"   waarde={machData.bentoniet?.breedte?`${machData.bentoniet.breedte} m`:null}/>
                <Rij label="Oppervlak" waarde={machData.bentoniet?.lengte&&machData.bentoniet?.breedte?`${machData.bentoniet.lengte*machData.bentoniet.breedte} m²`:null} highlight/>
              </div>
            </Grid>
            {traceCoords.length>=2&&<div style={{marginTop:12}}><BoorlijnKaart traceCoords={traceCoords} W={680} H={220}/></div>}
          </>:<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic"}}>Geen machinelocaties opgeslagen in stap 7.</div>}
        </Stap>

        {/* STAP 8 — 3D ontwerp */}
        <Stap nr={8} titel="3D Ontwerp — CesiumJS visualisatie">
          <div style={{display:"flex",gap:16,alignItems:"flex-start"}}>
            <div style={{flex:1}}>
              <p style={{fontSize:11,color:"#6B7280",margin:0,marginBottom:10}}>Het 3D-ontwerp is interactief en kan niet als statisch beeld worden opgenomen. Hieronder de specificaties van het ontwerp.</p>
              {boringRes&&<><Rij label="Boring Ø"      waarde={`Ø${boringRes.boringD} mm`}/><Rij label="Boorlengte"   waarde={traceLengte?`${traceLengte} m`:null}/><Rij label="Machine"      waarde={machInfo?.label??null}/><Rij label="Richting"     waarde={brg(traceBear)}/></>}
              {dieptePunten.length>0&&<Rij label="Max. diepte" waarde={maxDiepte?`${maxDiepte.toFixed(2)} m`:null} highlight/>}
            </div>
            <div style={{flexShrink:0,textAlign:"center"}}>
              {boringRes&&<><BoringSVG res={boringRes} customPos={bc?.customPos??{}} size={160} showLabel={true}/><div style={{fontSize:10,color:"#6B7280",marginTop:3}}>Dwarsdoorsnede</div></>}
            </div>
          </div>
        </Stap>

        {/* Footer */}
        <div style={{marginTop:24,paddingTop:12,borderTop:"1px solid #E5E7EB",display:"flex",justifyContent:"space-between"}}>
          <span style={{fontSize:10,color:"#9CA3AF"}}>PrescanAI · HDD Prescan Tool</span>
          <span style={{fontSize:10,color:"#9CA3AF"}}>{project?.naam??""} · {today}</span>
        </div>
      </div>
    </div>
  );
}
