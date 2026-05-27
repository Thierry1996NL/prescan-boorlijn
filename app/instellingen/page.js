// app/instellingen/page.js
"use client";
import { useState, useRef, useEffect } from "react";
import { useRouter } from "next/navigation";

const STAP_CFG = [
  {nr:1,kleur:"#F97316",bg:"#FFF7ED",icon:"⚙️",naam:"Boring configuratie",     omschrijving:"Diameter, machine, mantelbuis, vulgraad"},
  {nr:2,kleur:"#7C3AED",bg:"#F5F3FF",icon:"📂",naam:"Ontwerp inladen",          omschrijving:"KLIC bestanden, NEN-1775 kleuren"},
  {nr:3,kleur:"#0891B2",bg:"#ECFEFF",icon:"🗺️",naam:"Ontwerp bekijken",         omschrijving:"KLIC kruisingen, vrijwaringszones"},
  {nr:4,kleur:"#1D4ED8",bg:"#EFF6FF",icon:"✏️",naam:"Boorlijn tekenen",          omschrijving:"Tracé, inslaghoeken, afstanden"},
  {nr:5,kleur:"#059669",bg:"#ECFDF5",icon:"🌿",naam:"Oppervlakteanalyse",        omschrijving:"BGT risiconiveaus, vergunningen"},
  {nr:6,kleur:"#7C3AED",bg:"#F5F3FF",icon:"📊",naam:"Diepteligging",             omschrijving:"AHN4, NAP-waarden, segmenthoeken"},
  {nr:7,kleur:"#0891B2",bg:"#ECFEFF",icon:"🚜",naam:"Machine locatie",           omschrijving:"Ruimtebehoefte, bentoniet"},
  {nr:8,kleur:"#374151",bg:"#F9FAFB",icon:"🌐",naam:"3D ontwerp",                omschrijving:"Ruimtelijke conflicten"},
  {nr:9,kleur:"#F97316",bg:"#FFF7ED",icon:"📋",naam:"Eindontwerp",               omschrijving:"Volledigheidscheck prescan"},
];

const STANDAARD_BASE = [
  "SIKB-norm boordiameter = productbundel × 1.5. Vulgraad mantelbuis max 40%.",
  "NEN-1775 kleuren: LS=paars, MS=cyaan, Gas=geel, Water=donkerblauw, Data=groen.",
  "Vrijwaringszones CROW 500. Risicoklassering Rood/Oranje/Groen.",
  "Inslaghoek min 8°, max 20°. Minimale afstand bebouwing 1.5m.",
  "Gesloten verharding = hoog risico, groenvoorziening = laag risico.",
  "Minimale dekking: Gas≥1.0m, Water≥1.0m, LS≥0.6m, MS≥0.8m.",
  "Bentoniet verbruik 200-400 L/m. Min. afstand bentonietput tot woning 10m.",
  "Ruimtelijke conflicten beoordelen op verticale én horizontale vrije ruimte.",
  "Normencheck: CROW 500, SIKB, NEN-1775, BRL SIKB 7000.",
];

function LS(key, def = "") { try { return localStorage.getItem(key) ?? def; } catch { return def; } }
function LSSet(key, val) { try { localStorage.setItem(key, val); } catch {} }
function LSJ(key, def = []) { try { return JSON.parse(localStorage.getItem(key) || "null") ?? def; } catch { return def; } }
function LSJSet(key, val) { try { localStorage.setItem(key, JSON.stringify(val)); } catch {} }

async function extractTekst(file) {
  const n = file.name.toLowerCase();
  if (n.endsWith(".txt") || n.endsWith(".md") || n.endsWith(".csv")) return await file.text();
  if (n.endsWith(".docx")) return `[DOCX: exporteer naar .txt voor beste resultaat]\n${file.name}`;
  return `[Niet ondersteund: ${file.name}]\nGebruik: TXT, MD, CSV`;
}

export default function InstellingenPage() {
  const router = useRouter();
  const [tabblad, setTabblad] = useState("bots"); // bots | kennisbank
  const [actiefStap, setActiefStap] = useState(null);
  const [prompts, setPrompts] = useState(() =>
    Object.fromEntries(STAP_CFG.map(s => [s.nr, LS(`prescan_bot_prompt_${s.nr}`)]))
  );
  const [opgeslagen, setOpgeslagen] = useState({});
  const [kennisbank, setKennisbank] = useState(() => LSJ("prescan_globale_kennisbank"));
  const [uploadBezig, setUploadBezig] = useState(false);
  const fileRef = useRef(null);

  function slaPromptOp(nr) {
    LSSet(`prescan_bot_prompt_${nr}`, prompts[nr] ?? "");
    setOpgeslagen(prev => ({ ...prev, [nr]: true }));
    setTimeout(() => setOpgeslagen(prev => ({ ...prev, [nr]: false })), 2000);
  }

  function slaAllePromtsOp() {
    STAP_CFG.forEach(s => LSSet(`prescan_bot_prompt_${s.nr}`, prompts[s.nr] ?? ""));
    setOpgeslagen(Object.fromEntries(STAP_CFG.map(s => [s.nr, true])));
    setTimeout(() => setOpgeslagen({}), 2000);
  }

  async function voegDocToe(files) {
    setUploadBezig(true);
    const nieuw = [...kennisbank];
    for (const file of files) {
      const tekst = await extractTekst(file);
      nieuw.push({ id: Date.now() + Math.random(), naam: file.name, tekst, grootte: file.size > 1024*1024 ? `${(file.size/1024/1024).toFixed(1)} MB` : `${Math.round(file.size/1024)} KB`, datum: new Date().toLocaleDateString("nl-NL") });
    }
    setKennisbank(nieuw);
    LSJSet("prescan_globale_kennisbank", nieuw);
    setUploadBezig(false);
  }

  function verwijderDoc(id) {
    const nieuw = kennisbank.filter(d => d.id !== id);
    setKennisbank(nieuw);
    LSJSet("prescan_globale_kennisbank", nieuw);
  }

  return (
    <div style={{ minHeight: "100vh", background: "#F9FAFB", fontFamily: "system-ui, sans-serif" }}>
      {/* Header */}
      <div style={{ background: "white", borderBottom: "1px solid #E5E7EB", padding: "0 24px" }}>
        <div style={{ maxWidth: 900, margin: "0 auto", display: "flex", alignItems: "center", gap: 16, height: 56 }}>
          <button onClick={() => router.back()} style={{ background: "none", border: "none", cursor: "pointer", color: "#6B7280", fontSize: 13, display: "flex", alignItems: "center", gap: 4 }}>
            ← Terug
          </button>
          <div style={{ fontSize: 16, fontWeight: 700, color: "#1F2937" }}>⚙️ Instellingen</div>
        </div>
      </div>

      <div style={{ maxWidth: 900, margin: "0 auto", padding: "24px 24px" }}>
        {/* Tabs */}
        <div style={{ display: "flex", gap: 0, marginBottom: 24, borderBottom: "1px solid #E5E7EB" }}>
          {[["bots","🤖 AI Bots"],["kennisbank","📚 Globale kennisbank"]].map(([id,label]) => (
            <button key={id} onClick={() => setTabblad(id)} style={{
              padding: "10px 20px", fontSize: 13, fontWeight: tabblad===id ? 700 : 400,
              color: tabblad===id ? "#F97316" : "#6B7280",
              background: "none", border: "none", cursor: "pointer",
              borderBottom: tabblad===id ? "2px solid #F97316" : "2px solid transparent",
              marginBottom: -1,
            }}>{label}</button>
          ))}
        </div>

        {/* ── AI BOTS TAB ─────────────────────────────────────────────────────── */}
        {tabblad === "bots" && (
          <div>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 16 }}>
              <div>
                <div style={{ fontSize: 15, fontWeight: 700, color: "#1F2937" }}>AI Bot instellingen per stap</div>
                <div style={{ fontSize: 12, color: "#6B7280", marginTop: 2 }}>Voeg eigen instructies toe aan elke stap-bot. Ze worden toegevoegd bovenop de standaard prompts.</div>
              </div>
              <button onClick={slaAllePromtsOp} style={{ padding: "8px 16px", background: "#F97316", color: "white", border: "none", borderRadius: 8, cursor: "pointer", fontSize: 12, fontWeight: 600 }}>
                💾 Alles opslaan
              </button>
            </div>

            <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
              {STAP_CFG.map(stap => (
                <div key={stap.nr} style={{ background: "white", border: `1px solid ${actiefStap===stap.nr ? stap.kleur : "#E5E7EB"}`, borderLeft: `4px solid ${stap.kleur}`, borderRadius: 10, overflow: "hidden", transition: "border-color 0.15s" }}>
                  {/* Stap header */}
                  <div
                    onClick={() => setActiefStap(v => v===stap.nr ? null : stap.nr)}
                    style={{ display: "flex", alignItems: "center", gap: 12, padding: "12px 16px", cursor: "pointer", background: actiefStap===stap.nr ? stap.bg : "white" }}
                  >
                    <span style={{ fontSize: 18 }}>{stap.icon}</span>
                    <div style={{ flex: 1 }}>
                      <div style={{ fontSize: 13, fontWeight: 600, color: "#1F2937" }}>Stap {stap.nr} — {stap.naam}</div>
                      <div style={{ fontSize: 11, color: "#6B7280" }}>{stap.omschrijving}</div>
                    </div>
                    <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                      {prompts[stap.nr] && <span style={{ fontSize: 10, background: stap.kleur, color: "white", borderRadius: 10, padding: "1px 7px" }}>Eigen instructies actief</span>}
                      {opgeslagen[stap.nr] && <span style={{ fontSize: 11, color: "#059669" }}>✓ Opgeslagen</span>}
                      <span style={{ fontSize: 12, color: "#9CA3AF" }}>{actiefStap===stap.nr ? "▲" : "▼"}</span>
                    </div>
                  </div>

                  {/* Uitklapbaar */}
                  {actiefStap === stap.nr && (
                    <div style={{ padding: "14px 16px", borderTop: `1px solid ${stap.kleur}20` }}>
                      {/* Standaard base prompt info */}
                      <div style={{ background: "#F9FAFB", border: "1px solid #E5E7EB", borderRadius: 6, padding: "8px 12px", marginBottom: 12 }}>
                        <div style={{ fontSize: 10, fontWeight: 700, color: "#6B7280", marginBottom: 4, textTransform: "uppercase", letterSpacing: "0.04em" }}>Standaard basis (altijd actief)</div>
                        <div style={{ fontSize: 11, color: "#6B7280" }}>{STANDAARD_BASE[stap.nr-1]}</div>
                      </div>

                      {/* Eigen instructies */}
                      <div style={{ fontSize: 11, fontWeight: 600, color: "#374151", marginBottom: 6 }}>
                        ✏️ Eigen instructies toevoegen
                      </div>
                      <textarea
                        value={prompts[stap.nr] ?? ""}
                        onChange={e => setPrompts(prev => ({ ...prev, [stap.nr]: e.target.value }))}
                        placeholder={`Bijv: "Houd altijd rekening met de lage grondwaterstand in dit gebied" of "Wij werken altijd met PE 100 SDR11 materiaal"`}
                        rows={4}
                        style={{ width: "100%", padding: "10px 12px", border: `1.5px solid ${prompts[stap.nr] ? stap.kleur : "#E5E7EB"}`, borderRadius: 8, fontSize: 12, resize: "vertical", outline: "none", fontFamily: "system-ui", boxSizing: "border-box", transition: "border-color 0.15s" }}
                      />
                      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: 8 }}>
                        <div style={{ fontSize: 11, color: "#9CA3AF" }}>{(prompts[stap.nr]?.length ?? 0)} tekens · wordt toegevoegd aan elke bot-sessie in stap {stap.nr}</div>
                        <div style={{ display: "flex", gap: 8 }}>
                          {prompts[stap.nr] && (
                            <button onClick={() => { setPrompts(prev => ({...prev,[stap.nr]:""})); LSSet(`prescan_bot_prompt_${stap.nr}`, ""); }}
                              style={{ padding: "5px 10px", background: "none", border: "1px solid #E5E7EB", borderRadius: 6, cursor: "pointer", fontSize: 11, color: "#6B7280" }}>
                              Wissen
                            </button>
                          )}
                          <button onClick={() => slaPromptOp(stap.nr)} style={{ padding: "5px 14px", background: opgeslagen[stap.nr] ? "#059669" : stap.kleur, color: "white", border: "none", borderRadius: 6, cursor: "pointer", fontSize: 11, fontWeight: 600 }}>
                            {opgeslagen[stap.nr] ? "✓ Opgeslagen" : "Opslaan"}
                          </button>
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* ── KENNISBANK TAB ──────────────────────────────────────────────────── */}
        {tabblad === "kennisbank" && (
          <div>
            <div style={{ marginBottom: 16 }}>
              <div style={{ fontSize: 15, fontWeight: 700, color: "#1F2937" }}>Globale kennisbank</div>
              <div style={{ fontSize: 12, color: "#6B7280", marginTop: 2 }}>
                Documenten hier zijn beschikbaar voor <strong>alle bots in alle stappen</strong>. Gebruik dit voor normen, richtlijnen, machine-specs of bedrijfsdocumentatie.
                <br/>Ondersteund: <strong>TXT, MD, CSV</strong>
              </div>
            </div>

            {/* Upload zone */}
            <div
              onClick={() => fileRef.current?.click()}
              onDragOver={e => { e.preventDefault(); e.currentTarget.style.background = "#FFF7ED"; }}
              onDragLeave={e => { e.currentTarget.style.background = "white"; }}
              onDrop={e => { e.preventDefault(); e.currentTarget.style.background = "white"; voegDocToe([...e.dataTransfer.files]); }}
              style={{ border: "2px dashed #FED7AA", borderRadius: 12, padding: "32px 24px", textAlign: "center", cursor: "pointer", background: "white", marginBottom: 20, transition: "background 0.15s" }}
            >
              <div style={{ fontSize: 32, marginBottom: 8 }}>{uploadBezig ? "⏳" : "📄"}</div>
              <div style={{ fontSize: 13, fontWeight: 600, color: "#374151", marginBottom: 4 }}>
                {uploadBezig ? "Bezig met verwerken..." : "Klik of sleep bestanden hier"}
              </div>
              <div style={{ fontSize: 11, color: "#9CA3AF" }}>TXT, MD, CSV — CROW 500, SIKB richtlijnen, machine specs, normen</div>
              <input ref={fileRef} type="file" multiple accept=".txt,.md,.csv" style={{ display: "none" }} onChange={e => voegDocToe([...e.target.files])} />
            </div>

            {/* Documenten lijst */}
            {kennisbank.length > 0 ? (
              <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                <div style={{ fontSize: 12, fontWeight: 600, color: "#6B7280", marginBottom: 4 }}>{kennisbank.length} document{kennisbank.length > 1 ? "en" : ""} geladen</div>
                {kennisbank.map(doc => (
                  <div key={doc.id} style={{ display: "flex", alignItems: "center", gap: 12, background: "white", border: "1px solid #E5E7EB", borderRadius: 8, padding: "12px 14px" }}>
                    <span style={{ fontSize: 22, flexShrink: 0 }}>📄</span>
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div style={{ fontSize: 13, fontWeight: 600, color: "#1F2937", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{doc.naam}</div>
                      <div style={{ fontSize: 11, color: "#9CA3AF" }}>{doc.grootte} · Toegevoegd {doc.datum} · {Math.round((doc.tekst?.length ?? 0)/1000)}k tekens</div>
                    </div>
                    <button onClick={() => verwijderDoc(doc.id)} style={{ background: "#FEF2F2", border: "1px solid #FCA5A5", borderRadius: 6, padding: "4px 10px", cursor: "pointer", fontSize: 11, color: "#DC2626", flexShrink: 0 }}>
                      Verwijder
                    </button>
                  </div>
                ))}
              </div>
            ) : (
              <div style={{ textAlign: "center", padding: "32px 0", color: "#9CA3AF", fontSize: 13 }}>
                <div style={{ fontSize: 40, marginBottom: 8 }}>📚</div>
                <div>Nog geen documenten in de globale kennisbank</div>
                <div style={{ fontSize: 11, marginTop: 4 }}>Upload bijv. CROW 500 samenvatting, Vermeer specificaties of interne richtlijnen</div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
