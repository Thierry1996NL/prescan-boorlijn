"use client";
import { useEffect, useRef, useState, useCallback, useMemo } from "react";

// ─── Geometry helpers ─────────────────────────────────────────────
function afstandM(p1,p2){const R=6371000,dLat=(p2[0]-p1[0])*Math.PI/180,dLng=(p2[1]-p1[1])*Math.PI/180,a=Math.sin(dLat/2)**2+Math.cos(p1[0]*Math.PI/180)*Math.cos(p2[0]*Math.PI/180)*Math.sin(dLng/2)**2;return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));}
function cumulatiefAfstanden(coords){const r=[0];for(let i=1;i<coords.length;i++)r.push(r[i-1]+afstandM(coords[i-1],coords[i]));return r;}
function totaalLengte(coords){return coords.length<2?0:coords.reduce((s,_,i)=>i===0?0:s+afstandM(coords[i-1],coords[i]),0);}
function interpoleerLijn(coords,stap=5){
  if(coords.length<2)return[];
  const cumul=cumulatiefAfstanden(coords),tot=cumul[cumul.length-1],punten=[];
  for(let d=0;d<=tot+0.1;d+=stap){const dd=Math.min(d,tot);let seg=cumul.findIndex((c,i)=>i>0&&cumul[i]>=dd)-1;if(seg<0)seg=coords.length-2;const segLen=cumul[seg+1]-cumul[seg],t=segLen<0.001?0:(dd-cumul[seg])/segLen;punten.push({lat:coords[seg][0]+t*(coords[seg+1][0]-coords[seg][0]),lng:coords[seg][1]+t*(coords[seg+1][1]-coords[seg][1]),afstand:dd});}
  return punten;
}
function positieOpAfstand(coords,afstand){
  const cumul=cumulatiefAfstanden(coords),tot=cumul[cumul.length-1],dd=Math.max(0,Math.min(afstand,tot));
  let seg=cumul.findIndex((c,i)=>i>0&&cumul[i]>=dd)-1;if(seg<0)seg=coords.length-2;
  const segLen=cumul[seg+1]-cumul[seg],t=segLen<0.001?0:(dd-cumul[seg])/segLen;
  return{lat:coords[seg][0]+t*(coords[seg+1][0]-coords[seg][0]),lng:coords[seg][1]+t*(coords[seg+1][1]-coords[seg][1])};
}
function interpoleerDiepte(afstand,dieptePunten){
  if(!dieptePunten.length)return 0;
  const s=[...dieptePunten].sort((a,b)=>a.afstand-b.afstand);
  if(afstand<=s[0].afstand)return s[0].diepte;
  if(afstand>=s[s.length-1].afstand)return s[s.length-1].diepte;
  let i=0;while(i<s.length-1&&s[i+1].afstand<afstand)i++;
  const a=s[i],b=s[i+1],t=(afstand-a.afstand)/(b.afstand-a.afstand);
  return a.diepte+t*(b.diepte-a.diepte);
}
function maaiveldOpAfstand(afstand,profielPunten){
  if(!profielPunten.length)return null;
  const geldig=profielPunten.filter(p=>p.hoogte!==null);
  if(!geldig.length)return null;
  return geldig.reduce((a,b)=>Math.abs(a.afstand-afstand)<Math.abs(b.afstand-afstand)?a:b).hoogte;
}
function latLngNaarRD(lat,lng){const dLat=0.36*(lat-52.15517440),dLon=0.36*(lng-5.38720621);return{x:155000+190094.945*dLon-11832.228*dLon*dLat-114.221*dLon*dLat*dLat-32.391*dLon*dLon*dLon,y:463000+309056.544*dLat+60940.388*dLon*dLon*dLat-9.941*dLon*dLon-2.340*dLat*dLat*dLat};}
function segSnijpunt(p1,p2,p3,p4){const dx1=p2.x-p1.x,dy1=p2.y-p1.y,dx2=p4.x-p3.x,dy2=p4.y-p3.y,cross=dx1*dy2-dy1*dx2;if(Math.abs(cross)<1e-8)return null;const t=((p3.x-p1.x)*dy2-(p3.y-p1.y)*dx2)/cross,u=((p3.x-p1.x)*dy1-(p3.y-p1.y)*dx1)/cross;if(t>=0&&t<=1&&u>=0&&u<=1)return{t,x:p1.x+t*dx1,y:p1.y+t*dy1};return null;}

// ─── KLIC ─────────────────────────────────────────────────────────
const KLIC_TYPES={ls:{label:"LS",kleur:"#ef4444",diepte:0.60},ms:{label:"MS",kleur:"#f97316",diepte:0.80},gas:{label:"Gas",kleur:"#eab308",diepte:0.80},water:{label:"Water",kleur:"#3b82f6",diepte:1.00},tele:{label:"Tele",kleur:"#8b5cf6",diepte:0.45},riool:{label:"Riool",kleur:"#6b7280",diepte:1.20}};
function detecteerKlicType(f){const n=(f.properties?.naam||f.properties?.thema||"").toLowerCase();if(n.includes("laagspan")||n.includes("ls "))return"ls";if(n.includes("middensp")||n.includes("ms "))return"ms";if(n.includes("gas"))return"gas";if(n.includes("water"))return"water";if(n.includes("tele")||n.includes("data")||n.includes("glas"))return"tele";if(n.includes("riool"))return"riool";return"tele";}

function berekenBearing(start,end){
  // [lat,lng] → kompasrichting in graden (0=N,90=E,180=S,270=W)
  const lat1=start[0]*Math.PI/180,lat2=end[0]*Math.PI/180;
  const dLon=(end[1]-start[1])*Math.PI/180;
  const x=Math.sin(dLon)*Math.cos(lat2);
  const y=Math.cos(lat1)*Math.sin(lat2)-Math.sin(lat1)*Math.cos(lat2)*Math.cos(dLon);
  return((Math.atan2(x,y)*180/Math.PI)+360)%360;
}


function maakRdCrs(L){return new L.Proj.CRS("EPSG:28992","+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 +x_0=155000 +y_0=463000 +ellps=bessel +towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 +units=m +no_defs",{resolutions:[3440.640,1720.320,860.160,430.080,215.040,107.520,53.760,26.880,13.440,6.720,3.360,1.680,0.840,0.420,0.210,0.105,0.0525,0.02625,0.013125,0.00656,0.00328,0.00164,0.00082],origin:[-285401.920,903401.920],bounds:L.bounds([-285401.920,22598.080],[595401.920,903401.920])});}

// ─── Bereken segmenten voor tabel ────────────────────────────────
function berekenSegmenten(dieptePunten,profielPunten){
  const sorted=[...dieptePunten].sort((a,b)=>a.afstand-b.afstand);
  return sorted.map((dp,i)=>{
    const mv=maaiveldOpAfstand(dp.afstand,profielPunten);
    const nap=mv!==null?mv-dp.diepte:null;
    let seg=null;
    if(i<sorted.length-1){
      const next=sorted[i+1];
      const mvNext=maaiveldOpAfstand(next.afstand,profielPunten);
      const napNext=mvNext!==null?mvNext-next.diepte:null;
      const dH=next.afstand-dp.afstand;
      const dV=nap!==null&&napNext!==null?napNext-nap:null;
      const hoek=dH>0&&dV!==null?Math.atan2(dV,dH)*180/Math.PI:null;
      seg={dH,dV,hoek};
    }
    return{...dp,mv,nap,isStart:i===0,isEinde:i===sorted.length-1,seg};
  });
}

// ─── Dwarsprofiel SVG (2D interactief) ───────────────────────────
function Dwarsprofiel({profielPunten,dieptePunten,setDieptePunten,klicKruisingen,totM,onHoverAfstand,onHoverLeave}){
  const svgRef=useRef(null);
  const dragRef=useRef(null);

  if(!profielPunten||profielPunten.length<2)return(
    <div className="flex items-center justify-center h-48 text-sm text-gray-400">
      Klik <strong className="mx-1">⛰ Analyseer hoogte</strong> om het profiel te laden
    </div>
  );
  const geldig=profielPunten.filter(p=>p.hoogte!==null);
  if(!geldig.length)return null;

  const M={l:64,r:24,t:28,b:40};
  const W=900,H=320;
  const plotW=W-M.l-M.r,plotH=H-M.t-M.b;

  const maxDiepte=Math.max(...dieptePunten.map(p=>p.diepte),0)+1.5;
  const hMin=Math.min(...geldig.map(p=>p.hoogte))-maxDiepte;
  const hMax=Math.max(...geldig.map(p=>p.hoogte))+0.6;
  const hSpan=hMax-hMin||1;

  const xP=d=>M.l+d/totM*plotW;
  const yP=h=>M.t+(hMax-h)/hSpan*plotH;

  // Boorpad: rechte lijnen ALLEEN tussen waypoints (geen curve door alle AHN-punten)
  // Waypoints → NAP hoogte = maaiveld - diepte op dat punt
  const sortedWP=[...dieptePunten].sort((a,b)=>a.afstand-b.afstand);
  const boorWaypoints=sortedWP.map(dp=>{
    const mv=maaiveldOpAfstand(dp.afstand,geldig)??0;
    return{afstand:dp.afstand,hoogte:mv-dp.diepte};
  });
  // Segment labels op elke lijn: lengte + hoek
  const segmentLabels=boorWaypoints.slice(0,-1).map((wp,i)=>{
    const nxt=boorWaypoints[i+1];
    const mx=(xP(wp.afstand)+xP(nxt.afstand))/2;
    const my=(yP(wp.hoogte)+yP(nxt.hoogte))/2;
    const dH=nxt.afstand-wp.afstand;
    const dV=nxt.hoogte-wp.hoogte;
    const len=Math.sqrt(dH*dH+dV*dV); // werkelijke boorsegmentlengte
    const hoek=Math.atan2(dV,dH)*180/Math.PI;
    const kleur=Math.abs(hoek)>15?"#dc2626":Math.abs(hoek)>8?"#f97316":"#16a34a";
    const pijl=hoek>0.3?"↗":hoek<-0.3?"↘":"→";
    return{mx,my,len,hoek,kleur,pijl};
  });
  const boorPolyline=boorWaypoints.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");
  const maaiveldPts=geldig.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");
  const vlakPts=`${xP(geldig[0].afstand)},${H-M.b} ${maaiveldPts} ${xP(geldig[geldig.length-1].afstand)},${H-M.b}`;

  const stap=hSpan>8?2:hSpan>4?1:0.5;
  const yGrid=[];for(let h=Math.ceil(hMin/stap)*stap;h<=hMax;h+=stap)yGrid.push(h);

  const tussenPunten=dieptePunten.filter((_,i)=>i>0&&i<dieptePunten.length-1);

  function svgXY(e){
    const r=svgRef.current.getBoundingClientRect();
    return{x:(e.clientX-r.left)/r.width*W,y:(e.clientY-r.top)/r.height*H};
  }
  function xyNaarAfstandDiepte(x,y,afstandHint){
    const afstand=Math.max(0.5,Math.min(totM-0.5,(x-M.l)/plotW*totM));
    const napHoogte=hMax-((y-M.t)/plotH*hSpan);
    const mv=maaiveldOpAfstand(afstandHint??afstand,geldig);
    const diepte=Math.max(0,(mv??0)-napHoogte);
    return{afstand,diepte};
  }

  function handleMouseMove(e){
    const{x,y}=svgXY(e);
    const muisAfstand=Math.max(0,Math.min(totM,(x-M.l)/plotW*totM));
    onHoverAfstand?.(muisAfstand);
    if(dragRef.current!==null){
      const{idx}=dragRef.current;
      // Bereken nieuwe positie
      const nieuweAfstand=Math.max(0.5,Math.min(totM-0.5,(x-M.l)/plotW*totM));
      const napHoogte=hMax-((y-M.t)/plotH*hSpan);
      const mv=maaiveldOpAfstand(nieuweAfstand,geldig);
      const nieuweDiepte=Math.max(0,(mv??0)-napHoogte);
      setDieptePunten(prev=>{
        // Clamp horizontaal tussen buren (zodat volgorde stabiel blijft tijdens drag)
        const sorted=[...prev].sort((a,b)=>a.afstand-b.afstand);
        const sortedIdx=sorted.findIndex(p=>Math.abs(p.afstand-prev[idx].afstand)<0.5&&Math.abs(p.diepte-prev[idx].diepte)<0.5);
        const isStart=sortedIdx===0,isEnd=sortedIdx===sorted.length-1;
        let clampedAfstand=nieuweAfstand;
        if(!isStart&&!isEnd){
          const minA=sorted[sortedIdx-1].afstand+0.5;
          const maxA=sorted[sortedIdx+1].afstand-0.5;
          clampedAfstand=Math.max(minA,Math.min(maxA,nieuweAfstand));
        }
        // GEEN sort tijdens drag — index blijft stabiel
        return prev.map((p,i)=>i===idx?{...p,afstand:isStart?0:isEnd?totM:clampedAfstand,diepte:isStart||isEnd?0:nieuweDiepte}:p);
      });
    }
  }
  function handleMouseUp(){
    if(dragRef.current!==null)
      setDieptePunten(prev=>[...prev].sort((a,b)=>a.afstand-b.afstand));
    dragRef.current=null;
  }
  function handleMouseLeave(){onHoverLeave?.();if(dragRef.current!==null)setDieptePunten(prev=>[...prev].sort((a,b)=>a.afstand-b.afstand));dragRef.current=null;}
  function handlePuntMouseDown(e,afstand){
    e.stopPropagation();
    const idx=dieptePunten.findIndex(p=>Math.abs(p.afstand-afstand)<0.5);
    if(idx>=0)dragRef.current={idx};
  }
  function handleBoorpadKlik(e){
    if(dragRef.current!==null)return;
    const{x,y}=svgXY(e);
    const afstand=Math.max(0.5,Math.min(totM-0.5,(x-M.l)/plotW*totM));
    if(dieptePunten.some(p=>Math.abs(p.afstand-afstand)<totM*0.02))return;
    const mv=maaiveldOpAfstand(afstand,geldig);
    const napHoogte=hMax-((y-M.t)/plotH*hSpan);
    const diepte=Math.max(0,(mv??0)-napHoogte);
    setDieptePunten(prev=>[...prev,{afstand,diepte}].sort((a,b)=>a.afstand-b.afstand));
  }
  function handleDubbelKlik(e,afstand){
    e.stopPropagation();
    setDieptePunten(prev=>prev.filter((p,i)=>i===0||i===prev.length-1||Math.abs(p.afstand-afstand)>0.5));
  }

  return(
    <div className="w-full overflow-x-auto select-none">
      <div className="text-xs text-gray-400 text-right pr-2 mb-1">💡 Klik op lijn = punt toevoegen · sleep punt (2D) · dubbelklik = verwijder</div>
      <svg ref={svgRef} viewBox={`0 0 ${W} ${H}`} className="w-full cursor-crosshair"
        style={{minWidth:600,height:320}}
        onMouseMove={handleMouseMove} onMouseLeave={handleMouseLeave} onMouseUp={handleMouseUp}>
        {yGrid.map(h=>(
          <g key={h}>
            <line x1={M.l} y1={yP(h)} x2={W-M.r} y2={yP(h)} stroke={h===0?"#93c5fd":"#e5e7eb"} strokeWidth={h===0?1.5:0.5}/>
            <text x={M.l-4} y={yP(h)+4} textAnchor="end" fontSize={9} fill="#9ca3af">{h.toFixed(1)}</text>
          </g>
        ))}
        {hMin<0&&hMax>0&&<text x={M.l-4} y={yP(0)+4} textAnchor="end" fontSize={8} fill="#93c5fd">NAP</text>}
        <polygon points={vlakPts} fill="#bbf7d0" fillOpacity={0.45}/>
        <polyline points={maaiveldPts} fill="none" stroke="#16a34a" strokeWidth={2.5}/>
        <text x={M.l+4} y={M.t+11} fontSize={9} fill="#15803d" fontWeight="600">Maaiveld (AHN4)</text>

        {/* KLIC */}
        {klicKruisingen.map((k,i)=>{
          const kt=KLIC_TYPES[k.type]??KLIC_TYPES.tele;
          return(<g key={i}>
            <line x1={xP(k.afstand)} y1={M.t} x2={xP(k.afstand)} y2={H-M.b} stroke={kt.kleur} strokeWidth={1.5} strokeDasharray="5,3" opacity={0.7}/>
            <circle cx={xP(k.afstand)} cy={yP(k.hoogte-k.diepte)} r={5} fill={kt.kleur} fillOpacity={0.9}/>
            <text x={xP(k.afstand)+7} y={yP(k.hoogte-k.diepte)+4} fontSize={9} fill={kt.kleur} fontWeight="600">{kt.label}</text>
          </g>);
        })}

        {/* Boorpad klikzone */}
        <polyline points={boorPolyline} fill="none" stroke="#f97316" strokeWidth={10} opacity={0} style={{cursor:"copy"}} onClick={handleBoorpadKlik}/>
        <polyline points={boorPolyline} fill="none" stroke="#f97316" strokeWidth={3} strokeDasharray="10,5" strokeLinecap="round" onClick={handleBoorpadKlik} style={{cursor:"copy"}}/>

        {/* Segment labels: lengte + hoek op elke lijn */}
        {segmentLabels.map((sl,i)=>(
          <g key={i} style={{pointerEvents:"none"}}>
            <rect x={sl.mx-42} y={sl.my-26} width={84} height={24} rx={4}
              fill="white" fillOpacity={0.93} stroke="#e5e7eb" strokeWidth={0.8}/>
            <text x={sl.mx} y={sl.my-15} textAnchor="middle" fontSize={9} fill="#374151" fontWeight="600">
              {sl.len.toFixed(1)}m
            </text>
            <text x={sl.mx} y={sl.my-5} textAnchor="middle" fontSize={9} fill={sl.kleur} fontWeight="700">
              {sl.pijl}{Math.abs(sl.hoek).toFixed(1)}°
            </text>
          </g>
        ))}

        {/* Start/einde op maaiveld */}
        {(()=>{const s=geldig[0],e=geldig[geldig.length-1];return(<>
          <circle cx={xP(s.afstand)} cy={yP(s.hoogte)} r={7} fill="#16a34a" stroke="white" strokeWidth={2.5}/>
          <text x={xP(s.afstand)+9} y={yP(s.hoogte)-7} fontSize={9} fill="#15803d" fontWeight="700">S</text>
          <circle cx={xP(e.afstand)} cy={yP(e.hoogte)} r={7} fill="#dc2626" stroke="white" strokeWidth={2.5}/>
          <text x={xP(e.afstand)-9} y={yP(e.hoogte)-7} fontSize={9} fill="#dc2626" fontWeight="700" textAnchor="end">E</text>
        </>);})()}

        {/* Tussenpunten — 2D sleepbaar */}
        {tussenPunten.map((dp,relIdx)=>{
          const mv=maaiveldOpAfstand(dp.afstand,geldig)??0;
          const napHoogte=mv-dp.diepte;
          return(<g key={relIdx} style={{cursor:"move"}}
            onMouseDown={e=>handlePuntMouseDown(e,dp.afstand)}
            onDoubleClick={e=>handleDubbelKlik(e,dp.afstand)}>
            <line x1={xP(dp.afstand)} y1={yP(mv)} x2={xP(dp.afstand)} y2={yP(napHoogte)} stroke="#f97316" strokeWidth={1.5} strokeDasharray="3,2" opacity={0.5}/>
            <circle cx={xP(dp.afstand)} cy={yP(napHoogte)} r={8} fill="#f97316" stroke="white" strokeWidth={2.5}/>
            <rect x={xP(dp.afstand)-22} y={yP(napHoogte)+11} width={44} height={14} rx={3} fill="white" fillOpacity={0.9}/>
            <text x={xP(dp.afstand)} y={yP(napHoogte)+22} textAnchor="middle" fontSize={9} fill="#ea580c" fontWeight="700">-{dp.diepte.toFixed(1)}m</text>
          </g>);
        })}

        {/* X-as */}
        {[0,0.25,0.5,0.75,1].map(f=>{const d=f*totM;return(<g key={f}>
          <line x1={xP(d)} y1={H-M.b} x2={xP(d)} y2={H-M.b+4} stroke="#9ca3af"/>
          <text x={xP(d)} y={H-M.b+14} textAnchor="middle" fontSize={9} fill="#9ca3af">{Math.round(d)}m</text>
        </g>);})}
        <text x={M.l-42} y={H/2} fontSize={10} fill="#6b7280" transform={`rotate(-90,${M.l-42},${H/2})`} textAnchor="middle">Hoogte (m NAP)</text>
        <text x={W/2} y={H-2} textAnchor="middle" fontSize={10} fill="#6b7280">Afstand langs boorlijn (m)</text>
        <rect x={M.l} y={M.t} width={plotW} height={plotH} fill="none" stroke="#e5e7eb" strokeWidth={1}/>
      </svg>
    </div>
  );
}

// ─── Punten tabel met pijltjes + segmenten ────────────────────────
function DieptePuntenTabel({dieptePunten,setDieptePunten,profielPunten,totM}){
  const segmenten=useMemo(()=>berekenSegmenten(dieptePunten,profielPunten),[dieptePunten,profielPunten]);

  function wijzigAfstand(idx,delta){
    setDieptePunten(prev=>{
      const pts=[...prev];
      const nieuw=Math.max(0.5,Math.min(totM-0.5,pts[idx].afstand+delta));
      pts[idx]={...pts[idx],afstand:nieuw};
      return pts.sort((a,b)=>a.afstand-b.afstand);
    });
  }
  function wijzigDiepte(idx,delta){
    setDieptePunten(prev=>{
      const pts=[...prev];
      const idx2=prev.findIndex(p=>Math.abs(p.afstand-dieptePunten[idx].afstand)<0.5);
      if(idx2<0)return prev;
      pts[idx2]={...pts[idx2],diepte:Math.max(0,pts[idx2].diepte+delta)};
      return pts;
    });
  }
  function verwijder(idx){
    setDieptePunten(prev=>prev.filter((_,i)=>i!==prev.findIndex(p=>Math.abs(p.afstand-dieptePunten[idx].afstand)<0.5)));
  }

  const hoekKleur=(hoek)=>{
    if(hoek===null)return"text-gray-400";
    const abs=Math.abs(hoek);
    if(abs>15)return"text-red-600 font-bold";
    if(abs>8)return"text-orange-500 font-semibold";
    return"text-green-600";
  };

  return(
    <div className="bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
      <div className="px-4 py-3 border-b border-gray-100 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-gray-900">📍 Boorpunten &amp; segmenten</h3>
        <div className="text-xs text-gray-400">↑↓ = diepte ±0.1m &nbsp; ←→ = positie ±1m</div>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-gray-50 border-b border-gray-100">
              <th className="px-3 py-2 text-left text-gray-500 font-medium w-8">#</th>
              <th className="px-3 py-2 text-left text-gray-500 font-medium">Positie</th>
              <th className="px-3 py-2 text-left text-gray-500 font-medium">Diepte</th>
              <th className="px-3 py-2 text-left text-gray-500 font-medium">NAP</th>
              <th className="px-3 py-2 text-left text-gray-500 font-medium">Maaiveld</th>
              <th className="px-3 py-2 text-center text-gray-500 font-medium border-l border-gray-100" colSpan={3}>Segment naar volgend punt</th>
              <th className="px-3 py-2 w-20 text-center text-gray-500 font-medium border-l border-gray-100">Acties</th>
            </tr>
            <tr className="bg-gray-50 border-b border-gray-100 text-gray-400">
              <th/><th className="px-3 pb-1 text-left font-normal">(m)</th><th className="px-3 pb-1 text-left font-normal">(m↓)</th>
              <th className="px-3 pb-1 text-left font-normal">(m NAP)</th><th className="px-3 pb-1 text-left font-normal">(m NAP)</th>
              <th className="px-3 pb-1 text-center font-normal border-l border-gray-100">Δ afstand</th>
              <th className="px-3 pb-1 text-center font-normal">Δ hoogte</th>
              <th className="px-3 pb-1 text-center font-normal">Hoek</th>
              <th className="border-l border-gray-100"/>
            </tr>
          </thead>
          <tbody>
            {segmenten.map((s,i)=>{
              const isStart=i===0,isEinde=i===segmenten.length-1;
              const labelKleur=isStart?"text-green-700 bg-green-50":isEinde?"text-red-700 bg-red-50":"text-orange-700 bg-orange-50";
              return(<tr key={i} className={`border-b border-gray-50 hover:bg-gray-50 ${isStart?"bg-green-50/30":isEinde?"bg-red-50/30":""}`}>
                <td className="px-3 py-2">
                  <span className={`inline-block w-6 h-6 rounded-full text-center leading-6 text-xs font-bold ${labelKleur}`}>
                    {isStart?"S":isEinde?"E":i}
                  </span>
                </td>
                <td className="px-3 py-2 font-mono font-semibold text-gray-800">{s.afstand.toFixed(1)}m</td>
                <td className="px-3 py-2 font-mono text-orange-600">{s.diepte.toFixed(2)}m</td>
                <td className="px-3 py-2 font-mono text-blue-700">{s.nap!==null?s.nap.toFixed(2)+"m":"—"}</td>
                <td className="px-3 py-2 font-mono text-green-700">{s.mv!==null?s.mv.toFixed(2)+"m":"—"}</td>
                {/* Segment naar volgende */}
                {s.seg?(
                  <>
                    <td className="px-3 py-2 text-center font-mono text-gray-600 border-l border-gray-100">{s.seg.dH.toFixed(1)}m</td>
                    <td className="px-3 py-2 text-center font-mono">
                      <span className={s.seg.dV>0?"text-green-600":"text-red-600"}>
                        {s.seg.dV!==null?(s.seg.dV>=0?"+":"")+s.seg.dV.toFixed(2)+"m":"—"}
                      </span>
                    </td>
                    <td className={`px-3 py-2 text-center font-mono ${hoekKleur(s.seg.hoek)}`}>
                      {s.seg.hoek!==null?(
                        <span title={Math.abs(s.seg.hoek)>15?"Steile helling!":Math.abs(s.seg.hoek)>8?"Let op helling":""}>
                          {s.seg.hoek>=0?"↗":"↘"}{Math.abs(s.seg.hoek).toFixed(1)}°
                        </span>
                      ):"—"}
                    </td>
                  </>
                ):(
                  <td className="px-3 py-2 text-center text-gray-300 border-l border-gray-100" colSpan={3}>einde</td>
                )}
                {/* Actieknopjes */}
                <td className="px-2 py-1 border-l border-gray-100">
                  <div className="flex items-center gap-0.5">
                    {!isStart&&!isEinde&&(
                      <>
                        <button onClick={()=>wijzigDiepte(i,-0.1)} title="Minder diep" className="w-6 h-6 rounded bg-gray-100 hover:bg-orange-100 text-gray-600 hover:text-orange-700 text-xs">↑</button>
                        <button onClick={()=>wijzigDiepte(i,+0.1)} title="Meer diep"   className="w-6 h-6 rounded bg-gray-100 hover:bg-orange-100 text-gray-600 hover:text-orange-700 text-xs">↓</button>
                        <button onClick={()=>wijzigAfstand(i,-1)}  title="1m naar voren" className="w-6 h-6 rounded bg-gray-100 hover:bg-blue-100 text-gray-600 hover:text-blue-700 text-xs">←</button>
                        <button onClick={()=>wijzigAfstand(i,+1)}  title="1m naar achter" className="w-6 h-6 rounded bg-gray-100 hover:bg-blue-100 text-gray-600 hover:text-blue-700 text-xs">→</button>
                        <button onClick={()=>verwijder(i)} title="Verwijder" className="w-6 h-6 rounded bg-gray-100 hover:bg-red-100 text-gray-400 hover:text-red-600 text-xs ml-0.5">×</button>
                      </>
                    )}
                  </div>
                </td>
              </tr>);
            })}
          </tbody>
        </table>
      </div>
      <div className="px-4 py-2 bg-gray-50 border-t border-gray-100 flex gap-4 text-xs text-gray-500">
        <span className="text-green-600">↗ &lt;8° prima</span>
        <span className="text-orange-500">↗ 8–15° let op</span>
        <span className="text-red-600 font-semibold">↗ &gt;15° te steil!</span>
        <span className="ml-auto text-gray-400">Positieve hoek = stijgend · negatief = dalend</span>
      </div>
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

  // ── Bearing + kaartrotatie (altijd actief: bore horizontaal) ──
  const bearing=useMemo(()=>boorCoords.length>=2?berekenBearing(boorCoords[0],boorCoords[boorCoords.length-1]):0,[boorCoords]);
  const [geroteerd,setGeroteerd]=useState(true); // standaard AAN
  const rotatieDeg=useMemo(()=>{
    let r=(90-bearing+360)%360;
    if(r>180)r-=360;
    return r;
  },[bearing]);

  const [dieptePunten,setDieptePunten]=useState(()=>{
    // Laad uit diepte_profiel (aparte kolom) of ahn_profiel (gecombineerd oud formaat)
    try{
      const dp=project?.diepte_profiel;
      if(dp){const p=typeof dp==="string"?JSON.parse(dp):dp;if(Array.isArray(p)&&p.length>=2)return p;}
    }catch{}
    try{
      const raw=project?.ahn_profiel;
      if(raw){const p=typeof raw==="string"?JSON.parse(raw):raw;if(!Array.isArray(p)&&p?.dieptePunten?.length>=2)return p.dieptePunten;}
    }catch{}
    return [{afstand:0,diepte:0},{afstand:0,diepte:0}];
  });

  useEffect(()=>{
    if(totM>0){setDieptePunten(prev=>{const pts=[...prev];pts[0]={afstand:0,diepte:0};pts[pts.length-1]={afstand:totM,diepte:0};return pts;});}
  },[totM]);

  const [profielPunten,setProfielPunten]=useState(()=>{
    try{
      const raw=project?.ahn_profiel;if(!raw)return[];
      const p=typeof raw==="string"?JSON.parse(raw):raw;
      if(Array.isArray(p))return p; // oud formaat
      return p.profielPunten??[];   // nieuw formaat
    }catch{return[];}
  });
  const [hoogteBezig,setHoogteBezig]=useState(false);
  const [hoogteInfo,setHoogteInfo]=useState(()=>{
    try{
      const raw=project?.ahn_profiel;if(!raw)return null;
      const p=typeof raw==="string"?JSON.parse(raw):raw;
      const pp=Array.isArray(p)?p:(p.profielPunten??[]);
      const g=pp.filter(x=>x.hoogte!==null);
      return g.length?`${g.length}/${pp.length} punten (opgeslagen) · ${Math.min(...g.map(x=>x.hoogte)).toFixed(2)}–${Math.max(...g.map(x=>x.hoogte)).toFixed(2)}m NAP`:null;
    }catch{return null;}
  });
  const [klicKruisingen,setKlicKruisingen]=useState([]);
  boorCoordRef.current=boorCoords;

  useEffect(()=>{
    if(boorCoords.length<2)return;
    try{
      const klicSets=[];
      ["klic_ls","klic_ms","klic_gas","klic_water","klic_tele","klic_riool"].forEach(k=>{const raw=project?.[k];if(!raw)return;const gj=typeof raw==="string"?JSON.parse(raw):raw;(gj.features??[gj]).forEach(f=>{if(f?.geometry)klicSets.push({...f,_klicKey:k});});});
      try{const ss=sessionStorage.getItem("klic_features");if(ss)JSON.parse(ss).forEach(f=>klicSets.push(f));}catch{}
      const kruisingen=[],cumul=cumulatiefAfstanden(boorCoords);
      klicSets.forEach(feat=>{
        const type=detecteerKlicType(feat),kType=KLIC_TYPES[type]??KLIC_TYPES.tele,geom=feat.geometry;if(!geom?.coordinates)return;
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
      const L=window.L,crs=maakRdCrs(L),center=boorCoordRef.current[0]??[52.15,5.39];
      const kaart=L.map(mapRef.current,{crs,center,zoom:14,maxZoom:22,zoomControl:true});
      kaartRef.current=kaart;
      basisLaagRef.current=L.tileLayer("https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png",{maxZoom:22,maxNativeZoom:13,tileSize:256,attribution:"© PDOK BRT",zIndex:1}).addTo(kaart);
      L.tileLayer.wms("https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0",{layers:"buisleiding",format:"image/png",transparent:true,opacity:0.8,zIndex:10,attribution:"© Kadaster KLIC"}).addTo(kaart);
      let hoverMk=null;
      kaart._zetHoverMarker=(lat,lng)=>{if(hoverMk)hoverMk.setLatLng([lat,lng]);else{hoverMk=L.circleMarker([lat,lng],{radius:9,fillColor:"#f97316",fillOpacity:0.9,color:"white",weight:2.5,interactive:false,zIndexOffset:999}).addTo(kaart);}};
      kaart._verwijderHoverMarker=()=>{if(hoverMk){kaart.removeLayer(hoverMk);hoverMk=null;}};
      let boorPoly=null,editMks=[],tussenMks=[],isDrag=false;
      function markerIcon(kleur,groot){const sz=groot?20:14;return L.divIcon({html:`<div style="width:${sz}px;height:${sz}px;border-radius:50%;background:${kleur};border:2.5px solid white;box-shadow:0 1px 4px rgba(0,0,0,.35);cursor:grab"></div>`,className:"",iconSize:[sz,sz],iconAnchor:[sz/2,sz/2]});}
      function tussenIcon(){return L.divIcon({html:`<div style="width:10px;height:10px;border-radius:50%;background:#f97316;border:2px solid white;opacity:0.55"></div>`,className:"",iconSize:[10,10],iconAnchor:[5,5]});}
      function updateKaartLaag(){
        editMks.forEach(m=>kaart.removeLayer(m));editMks=[];tussenMks.forEach(m=>kaart.removeLayer(m));tussenMks=[];if(boorPoly){kaart.removeLayer(boorPoly);boorPoly=null;}
        const coords=boorCoordRef.current;if(!coords.length)return;
        if(coords.length>=2)boorPoly=L.polyline(coords,{color:"#f97316",weight:4,opacity:0.9,lineCap:"round"}).addTo(kaart);
        coords.forEach((coord,idx)=>{const isS=idx===0,isE=idx===coords.length-1;const mk=L.marker(coord,{draggable:true,icon:markerIcon(isS?"#16a34a":isE?"#dc2626":"#f97316",isS||isE),zIndexOffset:isS||isE?200:100}).addTo(kaart);mk.on("drag",e=>{isDrag=true;boorCoordRef.current[idx]=[e.latlng.lat,e.latlng.lng];if(boorPoly)boorPoly.setLatLngs(boorCoordRef.current);});mk.on("dragend",()=>{setBoorCoords([...boorCoordRef.current]);setTimeout(()=>{isDrag=false;},100);});mk.on("dblclick",e=>{L.DomEvent.stop(e);if(isS||isE||boorCoordRef.current.length<=2)return;boorCoordRef.current.splice(idx,1);setBoorCoords([...boorCoordRef.current]);});editMks.push(mk);});
        if(coords.length>=2){for(let i=0;i<coords.length-1;i++){const mid=[(coords[i][0]+coords[i+1][0])/2,(coords[i][1]+coords[i+1][1])/2];const tm=L.marker(mid,{icon:tussenIcon(),zIndexOffset:50}).addTo(kaart);const ci=i;tm.on("click",e=>{if(isDrag)return;L.DomEvent.stop(e);boorCoordRef.current.splice(ci+1,0,mid);setBoorCoords([...boorCoordRef.current]);});tussenMks.push(tm);}}
      }
      kaart._updateBoorLaag=(coords)=>{boorCoordRef.current=coords;updateKaartLaag();};
      updateKaartLaag();
      if(boorCoordRef.current.length>=2)try{kaart.fitBounds(L.latLngBounds(boorCoordRef.current).pad(0.15),{maxZoom:16});}catch{}
    })();
    return()=>{actief=false;if(kaartRef.current){try{kaartRef.current.remove();}catch{}kaartRef.current=null;}};
  },[]);

  useEffect(()=>{kaartRef.current?._updateBoorLaag?.(boorCoords);},[boorCoords]);

  const handleHoverAfstand=useCallback((afstand)=>{
    if(boorCoords.length<2)return;
    const pos=positieOpAfstand(boorCoords,afstand);
    kaartRef.current?._zetHoverMarker?.(pos.lat,pos.lng);
  },[boorCoords]);

  const [opslaanBezig,  setOpslaanBezig]  = useState(false);
  const [opslaanStatus, setOpslaanStatus] = useState(null); // "ok" | "fout" | null

  const handleOpslaan = useCallback(async()=>{
    if(!onSave) return;
    setOpslaanBezig(true); setOpslaanStatus(null);
    try{
      // diepte_profiel = aparte jsonb kolom (voer SQL uit: ALTER TABLE projecten ADD COLUMN IF NOT EXISTS diepte_profiel jsonb)
      await onSave({ diepte_profiel: dieptePunten });
      setOpslaanStatus("ok");
      setTimeout(()=>setOpslaanStatus(null), 3000);
    }catch(e){ setOpslaanStatus("fout"); console.error("Opslaan:",e); }
    setOpslaanBezig(false);
  },[dieptePunten, onSave]);

  const haalHoogteOp=useCallback(async()=>{
    if(boorCoords.length<2)return;
    setHoogteBezig(true);setHoogteInfo("Bezig met ophalen…");
    try{
      const punten=interpoleerLijn(boorCoords,5),rdPunten=punten.map(p=>{const rd=latLngNaarRD(p.lat,p.lng);return{x:rd.x,y:rd.y};});
      const res=await fetch("/api/ahn-hoogte",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({punten:rdPunten})});
      if(!res.ok){const t=await res.text().catch(()=>"");throw new Error(`HTTP ${res.status}${t?" — "+t.slice(0,80):""}`);}
      const data=await res.json();
      const metHoogte=punten.map((p,i)=>({...p,hoogte:data.hoogtes?.[i]??null}));
      setProfielPunten(metHoogte);
      const geldig=metHoogte.filter(p=>p.hoogte!==null);
      setHoogteInfo(geldig.length?`${geldig.length}/${punten.length} punten · ${Math.min(...geldig.map(p=>p.hoogte)).toFixed(2)}–${Math.max(...geldig.map(p=>p.hoogte)).toFixed(2)}m NAP`:"❌ Geen data — controleer /api/ahn-hoogte");
      if(geldig.length&&onSave)try{await onSave({ahn_profiel:metHoogte});}catch(e){console.warn("AHN opslaan:",e);}
    }catch(e){setHoogteInfo(`❌ ${e.message}`);}
    setHoogteBezig(false);
  },[boorCoords,onSave]);

  return(
    <div className="space-y-4">
      <div className="flex gap-4" style={{height:"calc(100vh - 260px)",minHeight:420}}>
        {/* Sidebar */}
        <div className="w-72 flex-shrink-0 bg-white border border-gray-200 rounded-xl overflow-y-auto flex flex-col">
          <div className="flex items-center px-4 py-2.5 border-b border-gray-100">
            <div><span className="text-sm font-semibold text-gray-900">6. Diepteligging</span><div className="text-xs text-gray-400">Dwarsprofiel & bodem</div></div>
          </div>
          <div className="flex-1 overflow-y-auto px-4 py-3 space-y-4">
            <div className="bg-orange-50 rounded-lg px-3 py-2">
              <div className="text-xs font-semibold text-orange-700 mb-1">Boorlijn</div>
              <div className="text-xs text-orange-600">{boorCoords.length>=2?`${Math.round(totM)}m · ${boorCoords.length} punten · ${bearing.toFixed(0)}° ${bearing<22.5||bearing>=337.5?"N":bearing<67.5?"NO":bearing<112.5?"O":bearing<157.5?"ZO":bearing<202.5?"Z":bearing<247.5?"ZW":bearing<292.5?"W":"NW"}`:"Geen boorlijn"}</div>
            </div>

            {/* Kaartrotatie-knop */}
            <button onClick={()=>setGeroteerd(v=>!v)}
              className={`w-full py-2 rounded-xl text-xs font-semibold transition-all border ${geroteerd?"bg-indigo-600 text-white border-indigo-600":"bg-white text-indigo-600 border-indigo-300 hover:bg-indigo-50"}`}>
              {geroteerd?"↑ Terug naar Noord-omhoog (interactief)":"↺ Bore-richting (horizontaal)"}
            </button>
            <div className="text-xs text-gray-400 text-center -mt-2">
              {geroteerd?"Kaart + profiel horizontaal uitgelijnd":"Klik om bore horizontaal te zetten"}
            </div>
            <button onClick={haalHoogteOp} disabled={hoogteBezig||boorCoords.length<2}
              className={`w-full py-2.5 rounded-xl text-sm font-semibold transition-all ${hoogteBezig||boorCoords.length<2?"bg-gray-100 text-gray-400 cursor-not-allowed":"bg-orange-500 hover:bg-orange-600 text-white shadow-sm"}`}>
              {hoogteBezig?<span className="flex items-center justify-center gap-2"><span className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"/>Bezig…</span>:"⛰ Analyseer hoogte (AHN4)"}
            </button>
            {hoogteInfo&&<div className={`text-xs rounded-lg px-3 py-2 leading-snug ${hoogteInfo.startsWith("❌")?"bg-red-50 text-red-600":"bg-green-50 text-green-700"}`}>{hoogteInfo}</div>}

            {/* Opslaan */}
            <button onClick={handleOpslaan} disabled={opslaanBezig||!onSave}
              className={`w-full py-2.5 rounded-xl text-sm font-semibold transition-all ${
                opslaanBezig||!onSave?"bg-gray-100 text-gray-400 cursor-not-allowed"
                :opslaanStatus==="ok"?"bg-green-500 text-white"
                :opslaanStatus==="fout"?"bg-red-500 text-white"
                :"bg-blue-600 hover:bg-blue-700 text-white shadow-sm"
              }`}>
              {opslaanBezig ? <span className="flex items-center justify-center gap-2"><span className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"/>Opslaan…</span>
               : opslaanStatus==="ok" ? "✓ Opgeslagen!"
               : opslaanStatus==="fout" ? "✗ Fout bij opslaan"
               : "💾 Diepteprofiel opslaan"}
            </button>
            {klicKruisingen.length>0&&<div className="border-t border-gray-100 pt-2"><div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1">KLIC ({klicKruisingen.length})</div><div className="space-y-0.5">{klicKruisingen.map((k,i)=>{const kt=KLIC_TYPES[k.type]??KLIC_TYPES.tele;return(<div key={i} className="flex items-center gap-2 text-xs"><div className="w-2 h-2 rounded-full" style={{background:kt.kleur}}/><span className="font-medium text-gray-700">{kt.label}</span><span className="text-gray-400">@{Math.round(k.afstand)}m</span><span className="text-gray-500 ml-auto">-{k.diepte.toFixed(1)}m</span></div>);})}</div></div>}
            <button onClick={()=>{if(boorCoords.length<2)return;const gj={type:"Feature",geometry:{type:"LineString",coordinates:boorCoords.map(([lat,lng])=>[lng,lat])},properties:{dieptePunten}};const blob=new Blob([JSON.stringify(gj,null,2)],{type:"application/json"});const a=document.createElement("a");a.href=URL.createObjectURL(blob);a.download="boorlijn_diepte.geojson";a.click();}}>⬇ Download GeoJSON</button>
          </div>
        </div>
        {/* Kaart + rotatie-wrapper */}
        <div className="flex-1 min-w-0 rounded-xl border border-gray-200 overflow-hidden shadow-sm relative bg-gray-100">
          {/* Rotatie-container — CSS-rotatatie zodat boorlijn horizontaal loopt */}
          <div style={{
            width:"100%",height:"100%",
            transform: geroteerd ? `rotate(${rotatieDeg}deg)` : "none",
            transition:"transform 0.5s ease",
            transformOrigin:"center center",
          }}>
            <div ref={mapRef} className="w-full h-full"/>
          </div>

          {/* Noord-pijl overlay — buiten de geroteerde div zodat hij correct wijst */}
          <div className="absolute top-3 right-3 z-[500] pointer-events-none">
            <div className="bg-white/90 backdrop-blur-sm rounded-full w-14 h-14 flex items-center justify-center shadow border border-gray-200"
              style={{transform: geroteerd ? `rotate(${-rotatieDeg}deg)` : "none", transition:"transform 0.5s ease"}}>
              <svg viewBox="0 0 40 40" width="40" height="40">
                {/* Noord-pijl */}
                <polygon points="20,4 24,20 20,17 16,20" fill="#dc2626"/>
                <polygon points="20,36 24,20 20,23 16,20" fill="#374151"/>
                <circle cx="20" cy="20" r="3" fill="white" stroke="#9ca3af" strokeWidth="1"/>
                <text x="20" y="3" textAnchor="middle" fontSize="7" fill="#dc2626" fontWeight="700">N</text>
              </svg>
            </div>
          </div>

          {/* Info overlay: bearing + rotatiestatus */}
          <div className="absolute bottom-3 left-3 z-[500] pointer-events-none">
            <div className="bg-white/90 backdrop-blur-sm rounded-lg px-3 py-1.5 shadow border border-gray-100 text-xs text-gray-600">
              {geroteerd
                ? <span>↺ <strong>{rotatieDeg>0?"+":""}{rotatieDeg.toFixed(0)}°</strong> → bore loopt horizontaal · Noord op {(-rotatieDeg+360)%360|0}°</span>
                : <span>Boorlijn: <strong>{bearing.toFixed(0)}°</strong> ({
                    bearing<22.5||bearing>=337.5?"N":bearing<67.5?"NO":bearing<112.5?"O":bearing<157.5?"ZO":bearing<202.5?"Z":bearing<247.5?"ZW":bearing<292.5?"W":"NW"
                  })</span>
              }
            </div>
          </div>
        </div>
      </div>

      {/* Dwarsprofiel */}
      <div className="bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100">
          <h3 className="text-sm font-semibold text-gray-900">⛰ Dwarsprofiel langs boorlijn</h3>
          <span className="text-xs text-gray-400">{profielPunten.filter(p=>p.hoogte!==null).length} meetpunten · AHN4 · hover = positie op kaart</span>
        </div>
        <div className="p-3">
          <Dwarsprofiel profielPunten={profielPunten} dieptePunten={dieptePunten} setDieptePunten={setDieptePunten} klicKruisingen={klicKruisingen} totM={totM} onHoverAfstand={handleHoverAfstand} onHoverLeave={()=>kaartRef.current?._verwijderHoverMarker?.()}/>
        </div>
      </div>

      {/* Punten tabel */}
      {profielPunten.filter(p=>p.hoogte!==null).length>0&&(
        <DieptePuntenTabel dieptePunten={dieptePunten} setDieptePunten={setDieptePunten} profielPunten={profielPunten} totM={totM}/>
      )}
    </div>
  );
}
