"use client";
import { useMemo, useRef } from "react";
import BoringSVG, { computeBoring } from "@/components/BoringSVG";

// ─── Helpers ──────────────────────────────────────────────────────────────────
function parseJSON(val) { try { return val ? (typeof val === "string" ? JSON.parse(val) : val) : null; } catch { return null; } }

function traceLength(coords) {
  if (!coords?.length || coords.length < 2) return null;
  let d = 0;
  for (let i = 1; i < coords.length; i++) {
    const [ln1,la1] = coords[i-1], [ln2,la2] = coords[i];
    const R=6371000,f=Math.PI/180;
    const a=Math.sin((la2-la1)*f/2)**2+Math.cos(la1*f)*Math.cos(la2*f)*Math.sin((ln2-ln1)*f/2)**2;
    d += R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
  }
  return Math.round(d);
}
function traceBearing(coords) {
  if (!coords?.length || coords.length < 2) return null;
  const [ln1,la1]=coords[0],[ln2,la2]=coords[coords.length-1],f=Math.PI/180;
  const y=Math.sin((ln2-ln1)*f)*Math.cos(la2*f);
  const x=Math.cos(la1*f)*Math.sin(la2*f)-Math.sin(la1*f)*Math.cos(la2*f)*Math.cos((ln2-ln1)*f);
  return Math.round((Math.atan2(y,x)*180/Math.PI+360)%360);
}
function bearingLabel(deg) {
  if(deg===null)return"—";
  const d=Math.round(deg);
  const k=d<23||d>=338?"N":d<68?"NO":d<113?"O":d<158?"ZO":d<203?"Z":d<248?"ZW":d<293?"W":"NW";
  return `${d}° ${k}`;
}
function nap(val) { return val!=null ? `${val>=0?"+":""}${val.toFixed(2)} m NAP` : "—"; }
function rdCoord(lngLat) {
  // simplified: just show decimal degrees
  if(!lngLat) return "—";
  return `${lngLat[1].toFixed(5)}°N  ${lngLat[0].toFixed(5)}°E`;
}

// ─── Mini Diepteprofiel SVG ───────────────────────────────────────────────────
function MiniProfiel({ profielPunten, dieptePunten, totM }) {
  const W=600, H=140, M={l:48,r:16,t:16,b:32};
  const geldig = (profielPunten||[]).filter(p=>p.hoogte!==null);
  if (geldig.length < 2) return <div className="text-xs text-gray-400 italic">Geen AHN4 hoogteprofiel beschikbaar</div>;

  const hMax=Math.max(...geldig.map(p=>p.hoogte))+0.5;
  const maxDiepte=Math.max(...(dieptePunten||[]).map(p=>p.diepte),0)+1;
  const hMin=Math.min(...geldig.map(p=>p.hoogte))-maxDiepte;
  const hSpan=hMax-hMin||1;
  const plotW=W-M.l-M.r, plotH=H-M.t-M.b;
  const totaal=totM||geldig[geldig.length-1]?.afstand||1;
  const xP=d=>M.l+d/totaal*plotW;
  const yP=h=>M.t+(hMax-h)/hSpan*plotH;

  const maaiveld=geldig.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");
  const vlak=`${xP(geldig[0].afstand)},${H-M.b} ${maaiveld} ${xP(geldig[geldig.length-1].afstand)},${H-M.b}`;

  const sortedWP=[...(dieptePunten||[])].sort((a,b)=>a.afstand-b.afstand);
  const maaiveldOpAfstand=(a,pts)=>{const gelp=pts.filter(p=>p.hoogte!==null);for(let i=0;i<gelp.length-1;i++){if(a>=gelp[i].afstand&&a<=gelp[i+1].afstand){const t=(a-gelp[i].afstand)/(gelp[i+1].afstand-gelp[i].afstand);return gelp[i].hoogte+t*(gelp[i+1].hoogte-gelp[i].hoogte);}}return null;};
  const boorWP=sortedWP.map(dp=>({afstand:dp.afstand,hoogte:(maaiveldOpAfstand(dp.afstand,geldig)??0)-dp.diepte}));
  const boorPoly=boorWP.map(p=>`${xP(p.afstand)},${yP(p.hoogte)}`).join(" ");

  const yTicks=[];
  const tickStep=hSpan>8?2:hSpan>4?1:0.5;
  for(let h=Math.ceil(hMin/tickStep)*tickStep;h<=hMax;h+=tickStep) yTicks.push(h);

  return (
    <svg width="100%" viewBox={`0 0 ${W} ${H}`} style={{display:"block",maxWidth:W}}>
      <polygon points={vlak} fill="#86efac" opacity={0.4}/>
      <polyline points={maaiveld} fill="none" stroke="#16a34a" strokeWidth={1.5}/>
      {boorWP.length>=2&&<polyline points={boorPoly} fill="none" stroke="#f97316" strokeWidth={2} strokeDasharray="8,4"/>}
      <line x1={M.l} y1={yP(0)} x2={W-M.r} y2={yP(0)} stroke="#3b82f6" strokeWidth={0.8} strokeDasharray="4,4" opacity={0.6}/>
      {yTicks.map(h=>(
        <g key={h}>
          <line x1={M.l-4} y1={yP(h)} x2={M.l} y2={yP(h)} stroke="#9ca3af"/>
          <text x={M.l-6} y={yP(h)+3.5} textAnchor="end" fontSize={8} fill="#6b7280">{h>=0?"+":""}{h.toFixed(1)}</text>
        </g>
      ))}
      {[0,0.25,0.5,0.75,1].map(f=>{const d=Math.round(f*totaal);return(<g key={f}><line x1={xP(d)} y1={H-M.b} x2={xP(d)} y2={H-M.b+3} stroke="#9ca3af"/><text x={xP(d)} y={H-M.b+11} textAnchor="middle" fontSize={8} fill="#6b7280">{d}m</text></g>);})}
      <line x1={M.l} y1={M.t} x2={M.l} y2={H-M.b} stroke="#e5e7eb"/>
      <line x1={M.l} y1={H-M.b} x2={W-M.r} y2={H-M.b} stroke="#e5e7eb"/>
      <text x={12} y={H/2} fontSize={8} fill="#6b7280" transform={`rotate(-90,12,${H/2})`} textAnchor="middle">Hoogte (m NAP)</text>
      <rect x={M.l} y={M.t} width={plotW} height={plotH} fill="none" stroke="#e5e7eb"/>
    </svg>
  );
}

// ─── Sectie component ─────────────────────────────────────────────────────────
function Sectie({ titel, children, kleur = "#F97316" }) {
  return (
    <div style={{border:"1px solid #E5E7EB",borderRadius:8,overflow:"hidden",marginBottom:20,breakInside:"avoid"}}>
      <div style={{background:kleur,padding:"8px 16px",display:"flex",alignItems:"center",gap:8}}>
        <span style={{fontSize:11,fontWeight:700,color:"white",textTransform:"uppercase",letterSpacing:"0.05em"}}>{titel}</span>
      </div>
      <div style={{padding:"12px 16px"}}>{children}</div>
    </div>
  );
}
function Rij({ label, waarde, highlight }) {
  return (
    <div style={{display:"flex",justifyContent:"space-between",alignItems:"baseline",padding:"4px 0",borderBottom:"1px solid #F9FAFB"}}>
      <span style={{fontSize:11,color:"#6B7280"}}>{label}</span>
      <span style={{fontSize:12,fontWeight:highlight?700:400,color:highlight?"#1F2937":"#374151",textAlign:"right"}}>{waarde??<em style={{color:"#9CA3AF"}}>—</em>}</span>
    </div>
  );
}

// ─── MAIN COMPONENT ───────────────────────────────────────────────────────────
export default function Eindontwerp({ project, boringConfig: bcProp }) {
  const reportRef = useRef(null);

  // ── Data parsing ──
  const bc        = bcProp ?? parseJSON(project?.boring_config);
  const ahnData   = parseJSON(project?.ahn_profiel);
  const machData  = parseJSON(project?.machine_locaties);
  const analyse   = parseJSON(project?.analyse_punten) ?? [];
  const traceGeo  = parseJSON(project?.boortrace_geojson) ?? project?.boortrace_geojson;
  const traceCoords = traceGeo?.coordinates ?? [];

  const boringRes = useMemo(() => bc?.items?.length ? computeBoring(bc.items) : null, [bc]);

  const traceLengte  = traceLength(traceCoords);
  const traceBear    = traceBearing(traceCoords);
  const profielPunten = ahnData?.profielPunten ?? [];
  const dieptePunten  = ahnData?.dieptePunten ?? [];
  const totM = profielPunten.length ? profielPunten[profielPunten.length-1]?.afstand ?? 0 : 0;

  const napMin = profielPunten.filter(p=>p.hoogte!==null).length
    ? Math.min(...profielPunten.filter(p=>p.hoogte!==null).map(p=>p.hoogte))
    : null;
  const napMax = profielPunten.filter(p=>p.hoogte!==null).length
    ? Math.max(...profielPunten.filter(p=>p.hoogte!==null).map(p=>p.hoogte))
    : null;
  const maxDiepte = dieptePunten.length ? Math.max(...dieptePunten.map(d=>d.diepte)) : null;
  const minNapBoor = dieptePunten.length && profielPunten.length
    ? (() => {
        const sorted = [...dieptePunten].sort((a,b)=>a.afstand-b.afstand);
        const geldig = profielPunten.filter(p=>p.hoogte!==null);
        let minH = Infinity;
        sorted.forEach(dp => {
          const mv = geldig.find(p=>Math.abs(p.afstand-dp.afstand)<5)?.hoogte??0;
          minH = Math.min(minH, mv - dp.diepte);
        });
        return minH === Infinity ? null : minH;
      })()
    : null;

  // BGT samenvatting
  const bgtSamenv = useMemo(() => {
    if (!analyse.length) return [];
    const typen = {};
    for (let i = 0; i < analyse.length - 1; i++) {
      const seg = (analyse[i+1].positieM - analyse[i].positieM) || 0;
      const k = analyse[i].oppervlak?.label ?? "Overig";
      typen[k] = (typen[k] || 0) + seg;
    }
    const tot = Object.values(typen).reduce((s,v)=>s+v,0)||1;
    return Object.entries(typen).sort((a,b)=>b[1]-a[1]).map(([k,v])=>({label:k,m:v,pct:Math.round(v/tot*100)}));
  }, [analyse]);

  // Machine info
  const mach = bc?.machine;
  const MACHINES = {
    d10x15:{label:"Vermeer D10x15 S3",push:"44.5 kN",koppel:"1.085 Nm",max:"Ø180 mm"},
    d20x22:{label:"Vermeer D20x22 S3",push:"86.7 kN",koppel:"2.983 Nm",max:"Ø250 mm"},
    d23x30:{label:"Vermeer D23x30 S3",push:"102 kN",koppel:"4.067 Nm",max:"Ø300 mm"},
    d36x50:{label:"Vermeer D36x50 S3",push:"160 kN",koppel:"6.779 Nm",max:"Ø400 mm"},
  };
  const machInfo = mach ? MACHINES[mach] : null;

  // ── Print ──
  function handlePrint() {
    const style = document.createElement("style");
    style.id = "__prescan_print";
    style.innerHTML = `
      @media print {
        body > *:not(#prescan-report-root) { display: none !important; }
        #prescan-report-root { display: block !important; margin: 0; padding: 0; }
        .no-print { display: none !important; }
        @page { margin: 15mm; size: A4 portrait; }
      }
    `;
    document.head.appendChild(style);
    const orig = document.body.innerHTML;
    const content = reportRef.current?.innerHTML ?? "";
    document.body.innerHTML = `<div id="prescan-report-root">${content}</div>`;
    window.print();
    document.body.innerHTML = orig;
    document.getElementById("__prescan_print")?.remove();
    window.location.reload();
  }

  const today = new Date().toLocaleDateString("nl-NL", {day:"2-digit",month:"2-digit",year:"numeric"});

  return (
    <div style={{fontFamily:"system-ui,sans-serif",maxWidth:860}}>

      {/* Export knop */}
      <div className="no-print flex gap-3 mb-5">
        <button onClick={handlePrint}
          style={{display:"flex",alignItems:"center",gap:8,padding:"10px 20px",
            background:"#F97316",color:"white",border:"none",borderRadius:8,
            cursor:"pointer",fontSize:13,fontWeight:600,boxShadow:"0 2px 8px rgba(249,115,22,0.3)"}}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
            <polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/>
            <line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/>
          </svg>
          Exporteer rapport (PDF)
        </button>
        <span style={{fontSize:12,color:"#9CA3AF",alignSelf:"center"}}>Opent afdrukdialoog → sla op als PDF</span>
      </div>

      {/* RAPPORT */}
      <div ref={reportRef}>

        {/* Rapportkop */}
        <div style={{display:"flex",justifyContent:"space-between",alignItems:"flex-start",marginBottom:24,paddingBottom:16,borderBottom:"2px solid #F97316"}}>
          <div>
            <div style={{fontSize:22,fontWeight:800,color:"#1F2937"}}>PrescanAI</div>
            <div style={{fontSize:13,color:"#6B7280",marginTop:2}}>HDD Prescan Rapportage</div>
          </div>
          <div style={{textAlign:"right"}}>
            <div style={{fontSize:18,fontWeight:700,color:"#1F2937"}}>{project?.naam ?? "—"}</div>
            <div style={{fontSize:12,color:"#6B7280"}}>{project?.locatie ?? ""}</div>
            <div style={{fontSize:11,color:"#9CA3AF",marginTop:4}}>Gegenereerd op {today}</div>
          </div>
        </div>

        {/* 1. Projectgegevens */}
        <Sectie titel="1. Projectgegevens">
          <div style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:"0 32px"}}>
            <div>
              <Rij label="Projectnaam"    waarde={project?.naam}/>
              <Rij label="Opdrachtgever"  waarde={project?.opdrachtgever}/>
              <Rij label="Locatie"        waarde={project?.locatie}/>
              <Rij label="Status"         waarde={project?.status}/>
            </div>
            <div>
              <Rij label="Boorlengte (invoer)" waarde={project?.boorlengte_m ? `${project.boorlengte_m} m` : null}/>
              <Rij label="Boorlengte (tracé)"  waarde={traceLengte ? `${traceLengte} m` : null} highlight/>
              <Rij label="Bodemtype"      waarde={project?.bodemtype}/>
              <Rij label="Materiaal"      waarde={project?.materiaal}/>
            </div>
          </div>
          {project?.bijzonderheden && (
            <div style={{marginTop:10,padding:"8px 10px",background:"#FFFBEB",borderRadius:6,border:"1px solid #FEF3C7"}}>
              <div style={{fontSize:10,color:"#92400E",fontWeight:600,marginBottom:3}}>Bijzonderheden</div>
              <div style={{fontSize:12,color:"#374151"}}>{project.bijzonderheden}</div>
            </div>
          )}
        </Sectie>

        {/* 2. Boring configuratie */}
        {(bc || boringRes) && (
          <Sectie titel="2. Boring configuratie">
            <div style={{display:"grid",gridTemplateColumns:"auto 1fr",gap:24,alignItems:"start"}}>
              {/* Cross-section */}
              <div style={{flexShrink:0}}>
                {boringRes
                  ? <BoringSVG res={boringRes} customPos={bc?.customPos ?? {}} size={200} showLabel={true}/>
                  : <div style={{width:200,height:200,background:"#F9FAFB",borderRadius:8,display:"flex",alignItems:"center",justifyContent:"center",fontSize:11,color:"#9CA3AF"}}>Geen configuratie</div>
                }
              </div>
              <div>
                <Rij label="Vereiste boring Ø" waarde={boringRes?.boringD ? `Ø${boringRes.boringD} mm` : bc?.boringD ? `Ø${bc.boringD} mm` : null} highlight/>
                <Rij label="Productbundel Ø"   waarde={boringRes?.bundleD ? `Ø${Math.round(boringRes.bundleD)} mm` : null}/>
                <Rij label="Machine"           waarde={machInfo?.label ?? mach ?? null}/>
                {machInfo && <>
                  <Rij label="Max. trekracht"  waarde={machInfo.push}/>
                  <Rij label="Max. koppel"     waarde={machInfo.koppel}/>
                  <Rij label="Max. boring Ø"   waarde={machInfo.max}/>
                </>}
                <Rij label="Aantal items"      waarde={bc?.items?.length}/>
                {/* Inhoud */}
                {bc?.items?.length > 0 && (
                  <div style={{marginTop:10}}>
                    <div style={{fontSize:10,fontWeight:600,color:"#6B7280",marginBottom:6,textTransform:"uppercase",letterSpacing:"0.04em"}}>Inhoud</div>
                    {bc.items.map((item, idx) => (
                      <div key={item.id??idx} style={{marginBottom:4,padding:"5px 8px",background:"#F9FAFB",borderRadius:5,border:"1px solid #E5E7EB"}}>
                        <div style={{fontSize:11,fontWeight:600,color:"#374151"}}>
                          {item.type==="mb" ? `PE${item.dn} mantelbuis` : item.label}
                        </div>
                        {item.type==="mb" && item.contents?.length > 0 && (
                          <div style={{marginTop:3,paddingLeft:10}}>
                            {item.contents.map((c,ci) => (
                              <div key={c.id??ci} style={{fontSize:10,color:"#6B7280"}}>• {c.label} (Ø{c.od}mm)</div>
                            ))}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </Sectie>
        )}

        {/* 3. Tracé & Geometrie */}
        <Sectie titel="3. Tracé & Geometrie" kleur="#2563EB">
          <div style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:"0 32px"}}>
            <div>
              <Rij label="Tracélengte"    waarde={traceLengte ? `${traceLengte} m` : null} highlight/>
              <Rij label="Richting"       waarde={bearingLabel(traceBear)}/>
              <Rij label="Aantal punten"  waarde={traceCoords.length}/>
            </div>
            <div>
              <Rij label="Startpunt"      waarde={traceCoords.length ? rdCoord(traceCoords[0]) : null}/>
              <Rij label="Eindpunt"       waarde={traceCoords.length ? rdCoord(traceCoords[traceCoords.length-1]) : null}/>
            </div>
          </div>
          {traceCoords.length === 0 && <p style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic",margin:0}}>Geen boorlijn getekend in stap 4.</p>}
        </Sectie>

        {/* 4. Diepteligging */}
        <Sectie titel="4. Diepteligging & AHN4 profiel" kleur="#7C3AED">
          {profielPunten.filter(p=>p.hoogte!==null).length > 0 ? (
            <>
              <div style={{display:"grid",gridTemplateColumns:"1fr 1fr 1fr",gap:"0 24px",marginBottom:14}}>
                <Rij label="AHN4 hoogte maaiveld (max)" waarde={napMax!=null ? nap(napMax) : null}/>
                <Rij label="AHN4 hoogte maaiveld (min)" waarde={napMin!=null ? nap(napMin) : null}/>
                <Rij label="Max. diepte boring"          waarde={maxDiepte!=null ? `${maxDiepte.toFixed(2)} m` : null} highlight/>
                <Rij label="Min. boorhoogte (NAP)"       waarde={minNapBoor!=null ? nap(minNapBoor) : null} highlight/>
                <Rij label="Aantal AHN4 meetpunten"      waarde={profielPunten.filter(p=>p.hoogte!==null).length}/>
                <Rij label="Aantal dieptepunten"         waarde={dieptePunten.length}/>
              </div>
              <MiniProfiel profielPunten={profielPunten} dieptePunten={dieptePunten} totM={totM}/>
              {dieptePunten.length > 0 && (
                <div style={{marginTop:14}}>
                  <div style={{fontSize:10,fontWeight:600,color:"#6B7280",marginBottom:6,textTransform:"uppercase",letterSpacing:"0.04em"}}>Dieptepunten boorpad</div>
                  <table style={{width:"100%",borderCollapse:"collapse",fontSize:11}}>
                    <thead>
                      <tr style={{background:"#F9FAFB"}}>
                        {["#","Afstand","Diepte","Hoogte NAP"].map(h=>(
                          <th key={h} style={{padding:"4px 8px",textAlign:"left",fontWeight:600,color:"#6B7280",borderBottom:"1px solid #E5E7EB"}}>{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {[...dieptePunten].sort((a,b)=>a.afstand-b.afstand).map((dp,i)=>{
                        const mv = profielPunten.filter(p=>p.hoogte!==null).find(p=>Math.abs(p.afstand-dp.afstand)<10)?.hoogte;
                        return (
                          <tr key={i} style={{borderBottom:"1px solid #F3F4F6"}}>
                            <td style={{padding:"3px 8px",color:"#9CA3AF"}}>{i+1}</td>
                            <td style={{padding:"3px 8px"}}>{dp.afstand.toFixed(1)} m</td>
                            <td style={{padding:"3px 8px",fontWeight:600}}>{dp.diepte.toFixed(2)} m</td>
                            <td style={{padding:"3px 8px",color:"#7C3AED"}}>{mv!=null ? nap(mv-dp.diepte) : "—"}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          ) : (
            <p style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic",margin:0}}>Geen diepteprofiel opgeslagen. Voer AHN4 analyse uit in stap 6.</p>
          )}
        </Sectie>

        {/* 5. Oppervlakteanalyse */}
        <Sectie titel="5. BGT Oppervlakteanalyse" kleur="#059669">
          {bgtSamenv.length > 0 ? (
            <>
              <div style={{display:"flex",gap:6,flexWrap:"wrap",marginBottom:12}}>
                {bgtSamenv.map(({label,m,pct})=>(
                  <div key={label} style={{padding:"5px 10px",background:"#ECFDF5",border:"1px solid #A7F3D0",borderRadius:6}}>
                    <div style={{fontSize:11,fontWeight:600,color:"#065F46"}}>{label}</div>
                    <div style={{fontSize:12,fontWeight:700,color:"#047857"}}>{Math.round(m)} m</div>
                    <div style={{fontSize:10,color:"#6B7280"}}>{pct}%</div>
                  </div>
                ))}
              </div>
              {/* Balk diagram */}
              <div style={{height:16,borderRadius:8,overflow:"hidden",display:"flex",marginBottom:8}}>
                {bgtSamenv.map(({label,pct},i)=>{
                  const colors=["#16A34A","#2563EB","#D97706","#7C3AED","#DC2626","#0891B2","#65A30D"];
                  return <div key={label} style={{width:`${pct}%`,background:colors[i%colors.length],minWidth:pct>3?undefined:0}}/>;
                })}
              </div>
              <div style={{fontSize:11,color:"#6B7280"}}>Totaal tracé: {Math.round(bgtSamenv.reduce((s,{m})=>s+m,0))} m · {analyse.length} meetpunten</div>
            </>
          ) : (
            <p style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic",margin:0}}>Geen BGT-analyse opgeslagen. Voer analyse uit in stap 5.</p>
          )}
        </Sectie>

        {/* 6. Machine locatie */}
        {machData && (
          <Sectie titel="6. Machine & Bentonietlocatie" kleur="#0891B2">
            <div style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:"0 32px"}}>
              <div>
                <div style={{fontSize:10,fontWeight:600,color:"#6B7280",marginBottom:6,textTransform:"uppercase"}}>HDD Boormachine</div>
                <Rij label="Lengte"  waarde={machData.boormachine?.lengte ? `${machData.boormachine.lengte} m` : null}/>
                <Rij label="Breedte" waarde={machData.boormachine?.breedte ? `${machData.boormachine.breedte} m` : null}/>
                <Rij label="Oppervlak" waarde={machData.boormachine?.lengte && machData.boormachine?.breedte ? `${machData.boormachine.lengte*machData.boormachine.breedte} m²` : null}/>
              </div>
              <div>
                <div style={{fontSize:10,fontWeight:600,color:"#6B7280",marginBottom:6,textTransform:"uppercase"}}>Bentoniet & Opvangput</div>
                <Rij label="Lengte"  waarde={machData.bentoniet?.lengte ? `${machData.bentoniet.lengte} m` : null}/>
                <Rij label="Breedte" waarde={machData.bentoniet?.breedte ? `${machData.bentoniet.breedte} m` : null}/>
                <Rij label="Oppervlak" waarde={machData.bentoniet?.lengte && machData.bentoniet?.breedte ? `${machData.bentoniet.lengte*machData.bentoniet.breedte} m²` : null}/>
              </div>
            </div>
          </Sectie>
        )}

        {/* Footer */}
        <div style={{marginTop:32,paddingTop:16,borderTop:"1px solid #E5E7EB",display:"flex",justifyContent:"space-between",alignItems:"center"}}>
          <div style={{fontSize:10,color:"#9CA3AF"}}>PrescanAI — HDD Boring Prescan Tool</div>
          <div style={{fontSize:10,color:"#9CA3AF"}}>{project?.naam} · {today}</div>
        </div>
      </div>
    </div>
  );
}
