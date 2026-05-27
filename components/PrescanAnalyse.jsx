"use client";
import { useState, useEffect, useRef } from "react";

// ─── Stap configuratie ────────────────────────────────────────────────────────
const STAP_CFG = {
  1:{kleur:"#F97316",bg:"#FFF7ED",rand:"#FED7AA",icon:"⚙️",naam:"Boring configuratie"},
  2:{kleur:"#7C3AED",bg:"#F5F3FF",rand:"#DDD6FE",icon:"📂",naam:"Ontwerp inladen"},
  3:{kleur:"#0891B2",bg:"#ECFEFF",rand:"#A5F3FC",icon:"🗺️",naam:"KLIC overzicht"},
  4:{kleur:"#1D4ED8",bg:"#EFF6FF",rand:"#BFDBFE",icon:"✏️",naam:"Boorlijn"},
  5:{kleur:"#059669",bg:"#ECFDF5",rand:"#A7F3D0",icon:"🌿",naam:"Oppervlakteanalyse"},
  6:{kleur:"#7C3AED",bg:"#F5F3FF",rand:"#DDD6FE",icon:"📊",naam:"Diepteligging"},
  7:{kleur:"#0891B2",bg:"#ECFEFF",rand:"#A5F3FC",icon:"🚜",naam:"Machine locatie"},
  8:{kleur:"#374151",bg:"#F9FAFB",rand:"#E5E7EB",icon:"🌐",naam:"3D ontwerp"},
  9:{kleur:"#F97316",bg:"#FFF7ED",rand:"#FED7AA",icon:"📋",naam:"Eindontwerp check"},
};

// ─── Markdown renderer ────────────────────────────────────────────────────────
function Md({text}){
  const lines = text.split("\n");
  return (
    <div style={{fontSize:12,lineHeight:1.6,color:"#1F2937"}}>
      {lines.map((line, i) => {
        // Headers
        if (line.startsWith("### ")) return <div key={i} style={{fontWeight:700,marginTop:8,marginBottom:2,color:"#111827"}}>{line.slice(4)}</div>;
        if (line.startsWith("## "))  return <div key={i} style={{fontWeight:700,marginTop:10,marginBottom:3,color:"#111827",fontSize:13}}>{line.slice(3)}</div>;
        if (line.startsWith("# "))   return <div key={i} style={{fontWeight:800,marginTop:10,marginBottom:3,color:"#111827",fontSize:14}}>{line.slice(2)}</div>;
        // Lege regel
        if (!line.trim()) return <div key={i} style={{height:6}}/>;
        // Inline formatting
        const html = line
          .replace(/\*\*(.+?)\*\*/g,"<strong>$1</strong>")
          .replace(/\*(.+?)\*/g,"<em>$1</em>")
          .replace(/`(.+?)`/g,'<code style="background:#F3F4F6;padding:1px 4px;border-radius:3px;font-family:monospace;font-size:0.9em">$1</code>');
        // Bullet
        if (line.startsWith("- ") || line.startsWith("• ")) {
          return <div key={i} style={{paddingLeft:12,marginBottom:1}} dangerouslySetInnerHTML={{__html:"• "+html.replace(/^[-•]\s+/,"")}}/>;
        }
        return <div key={i} dangerouslySetInnerHTML={{__html:html}}/>;
      })}
    </div>
  );
}

// ─── Analyse context samenstellen per stap ────────────────────────────────────
function maakAnalyseContext(stap, project, boringConfig) {
  const bc = boringConfig;
  const pJ = (v) => { try { return v?(typeof v==="string"?JSON.parse(v):v):null; } catch { return null; } };
  const ahnData   = pJ(project?.ahn_profiel);
  const machData  = pJ(project?.machine_locaties);
  const analyse   = pJ(project?.analyse_punten) ?? [];
  const traceGeo  = pJ(project?.boortrace_geojson) ?? project?.boortrace_geojson;
  const traceC    = traceGeo?.coordinates ?? [];

  // Tracélengte berekenen
  let traceLengte = null;
  if (traceC.length >= 2) {
    let d = 0;
    for (let i = 1; i < traceC.length; i++) {
      const [ln1,la1]=traceC[i-1],[ln2,la2]=traceC[i],R=6371000,f=Math.PI/180;
      const a=Math.sin((la2-la1)*f/2)**2+Math.cos(la1*f)*Math.cos(la2*f)*Math.sin((ln2-ln1)*f/2)**2;
      d += R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
    }
    traceLengte = `${Math.round(d)} m`;
  }

  // Richting berekenen
  let richting = null;
  if (traceC.length >= 2) {
    const [ln1,la1]=traceC[0],[ln2,la2]=traceC[traceC.length-1],f=Math.PI/180;
    const y=Math.sin((ln2-ln1)*f)*Math.cos(la2*f),x=Math.cos(la1*f)*Math.sin(la2*f)-Math.sin(la1*f)*Math.cos(la2*f)*Math.cos((ln2-ln1)*f);
    const d=Math.round((Math.atan2(y,x)*180/Math.PI+360)%360);
    const k=d<23||d>=338?"N":d<68?"NO":d<113?"O":d<158?"ZO":d<203?"Z":d<248?"ZW":d<293?"W":"NW";
    richting = `${d}° ${k}`;
  }

  // KLIC beschikbaar
  const klicVelden=["klic_ls","klic_ms","klic_gas","klic_water","klic_tele","klic_riool"];
  const klicLabels=["LS","MS","Gas","Water","Tele","Riool"];
  const klicBeschikbaar = klicVelden.map((k,i)=>project?.[k]?`✅ ${klicLabels[i]}`:`❌ ${klicLabels[i]}`).join(", ");

  // BGT samenvatting
  let bgtSamenvatting = "niet beschikbaar";
  if (analyse.length > 0) {
    const typen = {};
    for (let i = 0; i < analyse.length-1; i++) {
      const seg=(analyse[i+1].positieM-analyse[i].positieM)||0;
      const k=analyse[i].oppervlak?.label??"Overig";
      typen[k]=(typen[k]||0)+seg;
    }
    const tot=Object.values(typen).reduce((s,v)=>s+v,0)||1;
    bgtSamenvatting = Object.entries(typen).sort((a,b)=>b[1]-a[1]).map(([k,v])=>`${k}: ${Math.round(v)}m (${Math.round(v/tot*100)}%)`).join(", ");
  }

  // Diepte stats
  const dieptePunten = ahnData?.dieptePunten ?? [];
  const maxDiepte = dieptePunten.length ? `${Math.max(...dieptePunten.map(d=>d.diepte)).toFixed(2)} m` : null;
  const geldig = (ahnData?.profielPunten??[]).filter(p=>p.hoogte!==null);
  const napMin = geldig.length ? `${Math.min(...geldig.map(p=>p.hoogte)).toFixed(2)}` : null;
  const napMax = geldig.length ? `${Math.max(...geldig.map(p=>p.hoogte)).toFixed(2)}` : null;

  // Machine info
  const MACHINES={d10x15:"Vermeer D10x15 S3 (Ø180mm max)",d20x22:"Vermeer D20x22 S3 (Ø250mm max)",d23x30:"Vermeer D23x30 S3 (Ø300mm max)",d36x50:"Vermeer D36x50 S3 (Ø400mm max)"};

  return {
    boringD: bc?.boringD,
    machine: bc?.machine ? (MACHINES[bc.machine] ?? bc.machine) : null,
    items: bc?.items?.map(i=>i.type==="mb"?`PE${i.dn} mantelbuis${i.contents?.length?` met ${i.contents.map(c=>c.label).join(", ")}`:""}`  :i.label).join(" | ") ?? null,
    bodemtype: project?.bodemtype,
    locatie: project?.locatie,
    traceLengte,
    richting,
    klicBeschikbaar,
    bgtSamenvatting,
    maxDiepte,
    napMin,
    napMax,
    aantalDieptePunten: dieptePunten.length || null,
    klicKruisingen: 0, // berekend elders indien nodig
    machineAfm: machData?.boormachine?.lengte && machData?.boormachine?.breedte ? `${machData.boormachine.lengte}×${machData.boormachine.breedte}m` : null,
    bentonietAfm: machData?.bentoniet?.lengte && machData?.bentoniet?.breedte ? `${machData.bentoniet.lengte}×${machData.bentoniet.breedte}m` : null,
    projectNaam: project?.naam,
    heeftDiepte: dieptePunten.length > 0 ? "ja" : "nee",
    heeftBGT: analyse.length > 0 ? "ja" : "nee",
    heeftMachine: machData ? "ja" : "nee",
  };
}

// ─── Hoofd component ──────────────────────────────────────────────────────────
export default function PrescanAnalyse({ stap, project, boringConfig }) {
  const [status,    setStatus]   = useState("idle"); // idle | laden | klaar | fout
  const [tekst,     setTekst]    = useState("");
  const [streaming, setStreaming] = useState("");
  const [ingeklapt, setIngeklapt] = useState(false);
  const heeftGedraaidRef = useRef(false);

  const cfg = STAP_CFG[stap] ?? STAP_CFG[1];

  // Auto-start wanneer data beschikbaar is
  useEffect(() => {
    if (heeftGedraaidRef.current) return;
    const ctx = maakAnalyseContext(stap, project, boringConfig);
    // Alleen starten als er relevante data is
    const heeftData = ctx.boringD || ctx.traceLengte || ctx.maxDiepte || ctx.heeftBGT==="ja";
    if (heeftData) {
      heeftGedraaidRef.current = true;
      startAnalyse(ctx);
    }
  }, [project, boringConfig]);

  async function startAnalyse(ctx) {
    const analyseContext = ctx ?? maakAnalyseContext(stap, project, boringConfig);
    setStatus("laden");
    setTekst("");
    setStreaming("");
    setIngeklapt(false);

    try {
      const res = await fetch("/api/stap-ai", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ stap, project, boringConfig, analyseer: true, analyseContext }),
      });

      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        setTekst(`❌ ${data.error ?? "Analyse mislukt"}`);
        setStatus("fout");
        return;
      }

      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let antwoord = "";

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        const chunk = decoder.decode(value);
        for (const lijn of chunk.split("\n")) {
          if (!lijn.startsWith("data: ")) continue;
          const data = lijn.slice(6).trim();
          if (data === "[DONE]") continue;
          try {
            const evt = JSON.parse(data);
            const delta = evt.choices?.[0]?.delta?.content ?? "";
            if (delta) { antwoord += delta; setStreaming(antwoord); }
          } catch {}
        }
      }

      setTekst(antwoord || "Geen analyse beschikbaar.");
      setStatus("klaar");
    } catch (e) {
      setTekst("❌ Verbindingsfout. Controleer je internetverbinding.");
      setStatus("fout");
    } finally {
      setStreaming("");
    }
  }

  const tonen = streaming || tekst;

  return (
    <div style={{
      border: `1.5px solid ${cfg.rand}`,
      borderLeft: `4px solid ${cfg.kleur}`,
      borderRadius: 10,
      background: cfg.bg,
      marginTop: 20,
      overflow: "hidden",
    }}>
      {/* Header */}
      <div style={{
        display: "flex", alignItems: "center", gap: 10,
        padding: "10px 14px",
        borderBottom: tonen && !ingeklapt ? `1px solid ${cfg.rand}` : "none",
        cursor: tonen ? "pointer" : "default",
      }}
        onClick={() => tonen && setIngeklapt(v => !v)}
      >
        <span style={{fontSize:16}}>{cfg.icon}</span>
        <div style={{flex:1}}>
          <div style={{fontSize:12,fontWeight:700,color:cfg.kleur}}>🤖 AI Analyse — {cfg.naam}</div>
          <div style={{fontSize:10,color:"#6B7280",marginTop:1}}>
            {status==="idle"   && "Wacht op data..."}
            {status==="laden"  && <span style={{color:cfg.kleur}}>⚡ Groq analyseert...</span>}
            {status==="klaar"  && <span style={{color:"#059669"}}>✓ Analyse gereed · klik om in/uitklappen</span>}
            {status==="fout"   && <span style={{color:"#DC2626"}}>Analyse mislukt</span>}
          </div>
        </div>

        {/* Acties */}
        <div style={{display:"flex",gap:6,flexShrink:0}} onClick={e=>e.stopPropagation()}>
          {(status==="klaar"||status==="fout") && (
            <button onClick={()=>startAnalyse()} style={{
              background:cfg.kleur,color:"white",border:"none",
              borderRadius:6,padding:"4px 10px",fontSize:10,fontWeight:600,cursor:"pointer",
            }}>↻ Heranalyseer</button>
          )}
          {tonen && (
            <button onClick={()=>setIngeklapt(v=>!v)} style={{
              background:"white",color:cfg.kleur,border:`1px solid ${cfg.rand}`,
              borderRadius:6,padding:"4px 8px",fontSize:11,cursor:"pointer",
            }}>
              {ingeklapt?"▼":"▲"}
            </button>
          )}
        </div>
      </div>

      {/* Laad-animatie */}
      {status==="laden"&&!streaming&&(
        <div style={{padding:"12px 16px",display:"flex",gap:5,alignItems:"center"}}>
          {[0,1,2].map(i=>(
            <div key={i} style={{width:7,height:7,borderRadius:"50%",background:cfg.kleur,animation:`bounce 0.9s ${i*0.15}s ease-in-out infinite`}}/>
          ))}
          <span style={{fontSize:11,color:"#6B7280",marginLeft:4}}>Analyseren met Groq...</span>
        </div>
      )}

      {/* Analyse tekst */}
      {tonen && !ingeklapt && (
        <div style={{padding:"12px 16px",borderTop:`1px solid ${cfg.rand}`}}>
          <Md text={streaming||tekst}/>
          {streaming && (
            <span style={{display:"inline-block",width:6,height:13,background:cfg.kleur,borderRadius:2,marginLeft:2,animation:"blink 0.7s step-end infinite",verticalAlign:"middle"}}/>
          )}
        </div>
      )}

      <style>{`
        @keyframes bounce{0%,100%{transform:translateY(0)}50%{transform:translateY(-5px)}}
        @keyframes blink{0%,100%{opacity:1}50%{opacity:0}}
      `}</style>
    </div>
  );
}
