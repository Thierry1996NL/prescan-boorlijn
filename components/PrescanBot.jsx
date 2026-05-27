"use client";
import { useState, useRef, useEffect, useCallback } from "react";
import { updateProject } from "@/lib/supabase-queries";

// ─── Stap config ──────────────────────────────────────────────────────────────
const STAP_CFG = {
  1:{kleur:"#F97316",icon:"⚙️",naam:"Boring configuratie"},
  2:{kleur:"#7C3AED",icon:"📂",naam:"Ontwerp inladen"},
  3:{kleur:"#0891B2",icon:"🗺️",naam:"Ontwerp bekijken"},
  4:{kleur:"#1D4ED8",icon:"✏️",naam:"Boorlijn tekenen"},
  5:{kleur:"#059669",icon:"🌿",naam:"Oppervlakteanalyse"},
  6:{kleur:"#7C3AED",icon:"📊",naam:"Diepteligging"},
  7:{kleur:"#0891B2",icon:"🚜",naam:"Machine locatie"},
  8:{kleur:"#374151",icon:"🌐",naam:"3D ontwerp"},
  9:{kleur:"#F97316",icon:"📋",naam:"Eindontwerp"},
};
const SUGGESTIES = {
  1:["Is Ø375mm voldoende voor dit ontwerp?","Welke machine past bij dit tracé?","Wat is de maximale vulgraad van een mantelbuis?"],
  2:["Welke bestanden heb ik nodig?","Wat betekent de KLIC-kleur paars?","Hoe controleer ik of KLIC compleet is?"],
  3:["Wat zijn de vrijwaringszones per leidingtype?","Bij welke hoek is een kruising risicovol?","Hoe lees ik KLIC-diepte af?"],
  4:["Wat is de minimale inslaghoek?","Hoe ver van bebouwing moet ik blijven?","Is dit tracé haalbaar met de machine?"],
  5:["Risico's bij boren onder gesloten verharding?","Welke vergunning voor een watergang?","Herstelkosten asfalt vs klinkers?"],
  6:["Is een segment van 16° acceptabel?","Minimale dekking boven gasleiding?","Kloppen mijn NAP-waarden?"],
  7:["Hoeveel ruimte voor de boormachine?","Waar plaats ik de bentonietopvangput?","Hoeveel bentoniet voor dit tracé?"],
  8:["Zijn er ruimtelijke conflicten?","Hoe exporteer ik data voor de aannemer?","Wat moet ik documenteren?"],
  9:["Is mijn prescan compleet?","Welke documenten meesturen?","Welke normen moet ik nog checken?"],
};

// ─── Markdown renderer ────────────────────────────────────────────────────────
function Md({text}){
  const html=text
    .replace(/\*\*(.+?)\*\*/g,"<strong>$1</strong>")
    .replace(/\*(.+?)\*/g,"<em>$1</em>")
    .replace(/`(.+?)`/g,'<code style="background:#F3F4F6;padding:1px 4px;border-radius:3px;font-family:monospace;font-size:0.88em">$1</code>')
    .replace(/^#{1,3}\s+(.+)$/gm,'<div style="font-weight:700;margin:6px 0 2px;color:#1F2937">$1</div>')
    .replace(/^[-•]\s+(.+)$/gm,'<div style="padding-left:10px;margin:1px 0">• $1</div>')
    .replace(/\n\n/g,'<div style="margin-top:6px"/>')
    .replace(/\n/g,"<br/>");
  return <div dangerouslySetInnerHTML={{__html:html}}/>;
}

// ─── Bestand tekst extractie ──────────────────────────────────────────────────
async function extractTekst(file) {
  const naam = file.name.toLowerCase();
  if (naam.endsWith(".txt") || naam.endsWith(".md") || naam.endsWith(".csv")) {
    return await file.text();
  }
  if (naam.endsWith(".docx")) {
    // DOCX is een ZIP met XML — probeer tekst te extraheren zonder library
    try {
      const { unzipSync, strFromU8 } = await import("fflate");
      const buf = await file.arrayBuffer();
      const files = unzipSync(new Uint8Array(buf));
      const wordDoc = files["word/document.xml"];
      if (wordDoc) {
        const xml = strFromU8(wordDoc);
        // Verwijder XML tags, houd tekst over
        const tekst = xml
          .replace(/<w:p[ >]/g, "\n")
          .replace(/<[^>]+>/g, "")
          .replace(/&amp;/g,"&").replace(/&lt;/g,"<").replace(/&gt;/g,">")
          .replace(/\n{3,}/g, "\n\n").trim();
        return tekst || `[DOCX leeg of niet leesbaar: ${file.name}]`;
      }
    } catch {
      // fflate ook niet beschikbaar — geef instructie
    }
    return `[DOCX niet ondersteund zonder installatie]\nTip: sla het document op als .txt en upload opnieuw.\nBestand: ${file.name}`;
  }
  if (naam.endsWith(".pdf")) {
    return `[PDF niet ondersteund — exporteer naar .txt]\nBestand: ${file.name}`;
  }
  return `[Niet-ondersteund formaat: ${file.name}]\nOndersteuning: TXT, MD, CSV`;
}

// ─── Kennisbank paneel ────────────────────────────────────────────────────────
function KennisbankPaneel({kennisbank, onAdd, onVerwijder, onOpslaan, kleur, opslaat}) {
  const fileRef = useRef(null);
  const [bezig, setBezig] = useState(false);

  async function handleFile(files) {
    setBezig(true);
    for (const file of files) {
      const tekst = await extractTekst(file);
      const grootte = file.size > 1024*1024
        ? `${(file.size/1024/1024).toFixed(1)} MB`
        : `${Math.round(file.size/1024)} KB`;
      onAdd({ id: Date.now()+Math.random(), naam: file.name, tekst, grootte, toegevoegd: new Date().toLocaleDateString("nl-NL") });
    }
    setBezig(false);
  }

  return (
    <div style={{padding:"12px 14px"}}>
      <div style={{fontSize:12,fontWeight:700,color:"#374151",marginBottom:8}}>📚 Kennisbank</div>
      <p style={{fontSize:11,color:"#6B7280",margin:"0 0 10px"}}>
        Upload documenten die de assistent gebruikt als context: CROW 500, machine specs, normen, projectdocumentatie.
        <br/>Ondersteund: <strong>TXT, MD, CSV</strong> · DOCX via fflate indien beschikbaar
      </p>

      {/* Upload zone */}
      <div
        onClick={() => fileRef.current?.click()}
        onDragOver={e => { e.preventDefault(); e.currentTarget.style.background = `${kleur}15`; }}
        onDragLeave={e => { e.currentTarget.style.background = "#F9FAFB"; }}
        onDrop={e => { e.preventDefault(); e.currentTarget.style.background="#F9FAFB"; handleFile([...e.dataTransfer.files]); }}
        style={{
          border: `2px dashed ${kleur}60`,
          borderRadius: 8, padding: "14px 12px",
          textAlign: "center", cursor: "pointer",
          background: "#F9FAFB", marginBottom: 10,
          transition: "background 0.15s",
        }}
      >
        <div style={{fontSize: 22, marginBottom: 4}}>{bezig ? "⏳" : "📄"}</div>
        <div style={{fontSize: 11, color: "#6B7280"}}>
          {bezig ? "Bezig met verwerken..." : "Klik of sleep bestanden hier"}
        </div>
        <input ref={fileRef} type="file" multiple accept=".txt,.md,.csv,.docx"
          style={{display:"none"}} onChange={e => handleFile([...e.target.files])}/>
      </div>

      {/* Geladen documenten */}
      {kennisbank.length > 0 && (
        <div style={{display:"flex",flexDirection:"column",gap:4,marginBottom:10}}>
          {kennisbank.map(doc => (
            <div key={doc.id} style={{
              display:"flex",alignItems:"center",gap:8,
              background:"white",border:`1px solid ${kleur}30`,
              borderRadius:6,padding:"5px 8px",
            }}>
              <span style={{fontSize:14}}>📄</span>
              <div style={{flex:1,minWidth:0}}>
                <div style={{fontSize:11,fontWeight:600,color:"#374151",overflow:"hidden",textOverflow:"ellipsis",whiteSpace:"nowrap"}}>{doc.naam}</div>
                <div style={{fontSize:10,color:"#9CA3AF"}}>{doc.grootte} · {doc.toegevoegd} · {Math.round(doc.tekst?.length/1000)}k tekens</div>
              </div>
              <button onClick={() => onVerwijder(doc.id)} style={{background:"none",border:"none",cursor:"pointer",color:"#9CA3AF",fontSize:14,padding:2,flexShrink:0}}>✕</button>
            </div>
          ))}
        </div>
      )}

      {/* Opslaan bij project */}
      {kennisbank.length > 0 && (
        <button onClick={onOpslaan} disabled={opslaat} style={{
          width:"100%",padding:"7px 0",fontSize:11,fontWeight:600,
          background: opslaat ? "#E5E7EB" : kleur,
          color: opslaat ? "#9CA3AF" : "white",
          border:"none",borderRadius:6,cursor:opslaat?"default":"pointer",
          transition:"background 0.15s",
        }}>
          {opslaat ? "⏳ Opslaan..." : "💾 Sla kennisbank op bij project"}
        </button>
      )}
      {kennisbank.length === 0 && <div style={{fontSize:11,color:"#9CA3AF",textAlign:"center",padding:"8px 0"}}>Nog geen documenten geladen</div>}
    </div>
  );
}

// ─── Hoofd component ──────────────────────────────────────────────────────────
export default function PrescanBot({stap, project, boringConfig}) {
  const [open,       setOpen]       = useState(false);
  const [tabblad,    setTabblad]    = useState("chat"); // "chat" | "kennisbank"
  const [berichten,  setBerichten]  = useState([]);
  const [invoer,     setInvoer]     = useState("");
  const [bezig,      setBezig]      = useState(false);
  const [streaming,  setStreaming]  = useState("");
  const [kennisbank, setKennisbank] = useState(() => {
    try { return JSON.parse(project?.kennisbank || "[]"); } catch { return []; }
  });
  const [opslaat,    setOpslaat]    = useState(false);
  const [fout,       setFout]       = useState(null);
  const [rateLimitT, setRateLimitT] = useState(0); // countdown timer
  const bottomRef  = useRef(null);
  const inputRef   = useRef(null);
  const timerRef   = useRef(null);

  const info = STAP_CFG[stap] ?? STAP_CFG[1];
  const suggesties = SUGGESTIES[stap] ?? [];

  useEffect(() => {
    if (open && tabblad === "chat") setTimeout(() => inputRef.current?.focus(), 100);
  }, [open, tabblad]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [berichten, streaming]);

  // Rate limit countdown
  useEffect(() => {
    if (rateLimitT > 0) {
      timerRef.current = setTimeout(() => setRateLimitT(t => t - 1), 1000);
    }
    return () => clearTimeout(timerRef.current);
  }, [rateLimitT]);

  const stuur = useCallback(async (tekst) => {
    const bericht = (tekst ?? invoer).trim();
    if (!bericht || bezig || rateLimitT > 0) return;
    setInvoer("");
    setFout(null);
    const nieuw = [...berichten, { role: "user", content: bericht }];
    setBerichten(nieuw);
    setBezig(true);
    setStreaming("");

    try {
      const res = await fetch("/api/stap-ai", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          berichten: nieuw,
          stap,
          project,
          boringConfig,
          kennisbank: kennisbank.map(d => ({ naam: d.naam, tekst: d.tekst?.slice(0, 3000) })),
        }),
      });

      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        if (res.status === 429 && data.retryAfter) {
          setRateLimitT(data.retryAfter);
        }
        setFout(data.error ?? "Fout bij AI-aanroep");
        setBerichten(prev => prev.slice(0, -1)); // haal user bericht weg
        return;
      }

      // Stream OpenAI/Groq SSE formaat verwerken
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

      if (antwoord) {
        setBerichten(prev => [...prev, { role: "assistant", content: antwoord }]);
      }
    } catch (e) {
      setFout("Verbindingsfout. Controleer je internetverbinding.");
      setBerichten(prev => prev.slice(0, -1));
    } finally {
      setBezig(false);
      setStreaming("");
    }
  }, [invoer, bezig, berichten, stap, project, boringConfig, kennisbank, rateLimitT]);

  async function slaKennisbankOp() {
    if (!project?.id) return;
    setOpslaat(true);
    try {
      await updateProject(project.id, { kennisbank: JSON.stringify(kennisbank) });
    } catch (e) {
      console.error("Kennisbank opslaan:", e);
    } finally {
      setOpslaat(false);
    }
  }

  const aantalAntwoorden = berichten.filter(b => b.role === "assistant").length;

  return (
    <>
      {/* Floating knop */}
      <button
        onClick={() => setOpen(o => !o)}
        title={`AI-assistent — ${info.naam}`}
        style={{
          position:"fixed", bottom:24, right:24,
          width:54, height:54, borderRadius:"50%",
          background: open ? "#374151" : info.kleur,
          border:"none", cursor:"pointer",
          boxShadow:"0 4px 18px rgba(0,0,0,0.2)",
          display:"flex", alignItems:"center", justifyContent:"center",
          fontSize:22, zIndex:9000,
          transition:"transform 0.15s, background 0.2s",
        }}
        onMouseEnter={e => { if(!open) e.currentTarget.style.transform="scale(1.08)"; }}
        onMouseLeave={e => e.currentTarget.style.transform="scale(1)"}
      >
        <span style={{transition:"transform 0.2s",display:"inline-block",transform:open?"rotate(45deg)":"none"}}>
          {open ? "✕" : "🤖"}
        </span>
        {aantalAntwoorden > 0 && !open && (
          <div style={{position:"absolute",top:-2,right:-2,width:17,height:17,borderRadius:"50%",background:"#EF4444",border:"2px solid white",fontSize:9,color:"white",display:"flex",alignItems:"center",justifyContent:"center",fontWeight:700}}>
            {aantalAntwoorden}
          </div>
        )}
      </button>

      {/* Chat venster */}
      {open && (
        <div style={{
          position:"fixed", bottom:90, right:24,
          width:390, maxHeight:"72vh",
          background:"white", borderRadius:14,
          boxShadow:"0 8px 48px rgba(0,0,0,0.18)",
          display:"flex", flexDirection:"column",
          zIndex:8999,
          border:`1.5px solid ${info.kleur}30`,
          overflow:"hidden",
        }}>

          {/* Header */}
          <div style={{background:info.kleur, padding:"11px 14px", display:"flex", alignItems:"center", gap:10, flexShrink:0}}>
            <div style={{fontSize:18}}>{info.icon}</div>
            <div style={{flex:1}}>
              <div style={{fontSize:12,fontWeight:700,color:"white"}}>AI-assistent · Stap {stap}</div>
              <div style={{fontSize:10,color:"rgba(255,255,255,0.8)"}}>{info.naam} · Groq {kennisbank.length > 0 ? `· 📚 ${kennisbank.length} doc${kennisbank.length>1?"s":""}` : ""}</div>
            </div>
            <button onClick={() => { setBerichten([]); setFout(null); }} style={{background:"rgba(255,255,255,0.2)",border:"none",borderRadius:5,padding:"2px 7px",color:"white",fontSize:10,cursor:"pointer"}}>Reset</button>
          </div>

          {/* Tabbladen */}
          <div style={{display:"flex",borderBottom:"1px solid #F3F4F6",flexShrink:0}}>
            {[["chat","💬 Chat"],["kennisbank","📚 Kennisbank"]].map(([id,label]) => (
              <button key={id} onClick={() => setTabblad(id)} style={{
                flex:1, padding:"8px 4px", fontSize:11, fontWeight:tabblad===id?700:400,
                color: tabblad===id ? info.kleur : "#6B7280",
                background:"white", border:"none", cursor:"pointer",
                borderBottom: tabblad===id ? `2px solid ${info.kleur}` : "2px solid transparent",
                transition:"color 0.1s",
              }}>
                {label}
                {id === "kennisbank" && kennisbank.length > 0 && (
                  <span style={{marginLeft:4,background:info.kleur,color:"white",borderRadius:10,padding:"0 5px",fontSize:9}}>{kennisbank.length}</span>
                )}
              </button>
            ))}
          </div>

          {/* Kennisbank tab */}
          {tabblad === "kennisbank" && (
            <div style={{flex:1,overflowY:"auto"}}>
              <KennisbankPaneel
                kennisbank={kennisbank}
                onAdd={doc => setKennisbank(prev => [...prev, doc])}
                onVerwijder={id => setKennisbank(prev => prev.filter(d => d.id !== id))}
                onOpslaan={slaKennisbankOp}
                kleur={info.kleur}
                opslaat={opslaat}
              />
            </div>
          )}

          {/* Chat tab */}
          {tabblad === "chat" && (<>
            <div style={{flex:1,overflowY:"auto",padding:"12px 14px",minHeight:160}}>

              {/* Welkom + suggesties */}
              {berichten.length === 0 && (
                <div>
                  <div style={{background:"#F9FAFB",borderRadius:10,padding:"10px 12px",fontSize:11,color:"#374151",marginBottom:10,borderLeft:`3px solid ${info.kleur}`}}>
                    Hoi! Ik ben je AI-assistent voor <strong>{info.naam}</strong>.
                    {project?.naam && <span style={{color:"#6B7280"}}> Project: {project.naam}.</span>}
                    {kennisbank.length > 0 && <span style={{color:info.kleur}}> 📚 {kennisbank.length} kennisbank-doc{kennisbank.length>1?"s":""} geladen.</span>}
                  </div>
                  <div style={{display:"flex",flexDirection:"column",gap:5}}>
                    {suggesties.map((s,i) => (
                      <button key={i} onClick={() => stuur(s)} style={{
                        background:"white",border:`1px solid ${info.kleur}40`,
                        borderRadius:7,padding:"7px 10px",fontSize:11,
                        color:"#374151",cursor:"pointer",textAlign:"left",
                        transition:"all 0.1s",
                      }}
                      onMouseEnter={e=>{e.currentTarget.style.background=`${info.kleur}10`;e.currentTarget.style.borderColor=info.kleur;}}
                      onMouseLeave={e=>{e.currentTarget.style.background="white";e.currentTarget.style.borderColor=`${info.kleur}40`;}}>
                        {s}
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {/* Berichten */}
              {berichten.map((b,i) => (
                <div key={i} style={{display:"flex",justifyContent:b.role==="user"?"flex-end":"flex-start",marginBottom:10,gap:6,alignItems:"flex-end"}}>
                  {b.role==="assistant" && <div style={{fontSize:18,flexShrink:0,marginBottom:2}}>🤖</div>}
                  <div style={{
                    maxWidth:"85%",
                    background: b.role==="user" ? info.kleur : "#F3F4F6",
                    color: b.role==="user" ? "white" : "#1F2937",
                    borderRadius: b.role==="user" ? "12px 12px 2px 12px" : "12px 12px 12px 2px",
                    padding:"8px 11px", fontSize:12, lineHeight:1.55,
                  }}>
                    {b.role==="assistant" ? <Md text={b.content}/> : b.content}
                  </div>
                </div>
              ))}

              {/* Streaming */}
              {streaming && (
                <div style={{display:"flex",marginBottom:10,gap:6,alignItems:"flex-end"}}>
                  <div style={{fontSize:18,flexShrink:0}}>🤖</div>
                  <div style={{maxWidth:"85%",background:"#F3F4F6",color:"#1F2937",borderRadius:"12px 12px 12px 2px",padding:"8px 11px",fontSize:12,lineHeight:1.55}}>
                    <Md text={streaming}/>
                    <span style={{display:"inline-block",width:6,height:13,background:info.kleur,borderRadius:2,marginLeft:2,animation:"blink 0.7s step-end infinite"}}/>
                  </div>
                </div>
              )}

              {/* Laden */}
              {bezig && !streaming && (
                <div style={{display:"flex",gap:5,padding:"6px 2px"}}>
                  {[0,1,2].map(i=><div key={i} style={{width:7,height:7,borderRadius:"50%",background:info.kleur,animation:`bounce 0.9s ${i*0.15}s ease-in-out infinite`}}/>)}
                </div>
              )}

              {/* Fout */}
              {fout && (
                <div style={{background:"#FEF2F2",border:"1px solid #FCA5A5",borderRadius:8,padding:"8px 11px",fontSize:11,color:"#DC2626",marginTop:8}}>
                  ❌ {fout}
                  {rateLimitT > 0 && <div style={{marginTop:4,fontWeight:600}}>⏳ Probeer opnieuw over {rateLimitT}s</div>}
                </div>
              )}

              <div ref={bottomRef}/>
            </div>

            {/* Input */}
            <div style={{padding:"10px 12px",borderTop:"1px solid #F3F4F6",display:"flex",gap:8,flexShrink:0}}>
              <textarea
                ref={inputRef}
                value={invoer}
                onChange={e => setInvoer(e.target.value)}
                onKeyDown={e => { if(e.key==="Enter"&&!e.shiftKey){e.preventDefault();stuur();} }}
                placeholder={rateLimitT>0?`Wacht ${rateLimitT}s...`:"Stel een vraag... (Enter = stuur)"}
                rows={2}
                disabled={bezig||rateLimitT>0}
                style={{
                  flex:1,resize:"none",
                  border:`1.5px solid ${invoer&&!rateLimitT?info.kleur:"#E5E7EB"}`,
                  borderRadius:8,padding:"7px 10px",
                  fontSize:12,outline:"none",
                  fontFamily:"system-ui,sans-serif",
                  transition:"border-color 0.15s",
                  opacity:rateLimitT>0?0.6:1,
                }}
              />
              <button
                onClick={()=>stuur()}
                disabled={bezig||!invoer.trim()||rateLimitT>0}
                style={{
                  width:40,height:40,alignSelf:"flex-end",
                  background:invoer.trim()&&!rateLimitT?info.kleur:"#E5E7EB",
                  border:"none",borderRadius:8,
                  cursor:invoer.trim()&&!rateLimitT?"pointer":"default",
                  display:"flex",alignItems:"center",justifyContent:"center",
                  fontSize:16,transition:"background 0.15s",flexShrink:0,
                }}
              >
                {rateLimitT>0?rateLimitT:"➤"}
              </button>
            </div>
          </>)}

          <style>{`
            @keyframes blink{0%,100%{opacity:1}50%{opacity:0}}
            @keyframes bounce{0%,100%{transform:translateY(0)}50%{transform:translateY(-5px)}}
          `}</style>
        </div>
      )}
    </>
  );
}
