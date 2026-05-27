"use client";

import { useState, useEffect } from "react";
import { useParams, useRouter } from "next/navigation";
import { getProjectMetContext, updateProject, supabase } from "@/lib/supabase-queries";
import Sidebar from "@/components/Sidebar";
import dynamic from "next/dynamic";

const MapTrace            = dynamic(() => import("@/components/MapTrace"),            { ssr: false });
const OppervlakteAnalyse = dynamic(() => import("@/components/OppervlakteAnalyse"), { ssr: false });
const OntwerpKaart  = dynamic(() => import("@/components/OntwerpKaart"),  { ssr: false });
const Diepteligging    = dynamic(() => import("@/components/Diepteligging"),    { ssr: false });
const MachineLocatie   = dynamic(() => import("@/components/MachineLocatie"),   { ssr: false });
const Stap8_3D         = dynamic(() => import("@/components/Stap8_3D"),         { ssr: false });
import BoringConfigurator from "@/components/BoringConfigurator";
import BoringSVG, { computeBoring, CATS as BORING_CATS, TUBE_COLORS as BORING_COLORS } from "@/components/BoringSVG";
import Eindontwerp from "@/components/Eindontwerp";
import PrescanBot from "@/components/PrescanBot";
import PrescanAnalyse from "@/components/PrescanAnalyse";

const STAP_LABELS = {
  1:  "Projectinformatie",
  2:  "Ontwerp inladen",
  3:  "Ontwerp bekijken",
  4:  "Boorlijn tekenen",
  5:  "Oppervlakteanalyse",
  6:  "Diepteligging & dwarsprofiel",
  7:  "Machine- & bentonietlocatie",
  8:  "3D ontwerp",
  9:  "Eindontwerp",
  10: "AI optimalisatie & kruisingen",
  11: "Pre-scan en tekeningen",
  12: "Contactpersonen (KLIC)",
  13: "Exporteren",
};

const STAP_MAX_UITGEWERKT = 9;

// ═══════════════════════════════════════════════════════════════
export default function ProjectDetailPagina() {
  const { id }   = useParams();
  const router   = useRouter();

  const [project,     setProject]     = useState(null);
  const [laden,       setLaden]       = useState(true);
  const [actieveStap, setActieveStap] = useState(1);
  const [gebruiker,   setGebruiker]   = useState(null);

  // stap 1 – bewerken
  const [bewerkModaal, setBewerkModaal] = useState(false);
  const [bewerkData,   setBewerkData]   = useState({});
  const [opslaan,      setOpslaan]      = useState(false);

  // stap 2 – uploads
  const [uploadStatus,  setUploadStatus]  = useState({});
  const [uploadLaden,   setUploadLaden]   = useState({});

  // boring configuratie (stap 1) — gedeeld met alle andere stappen
  const [boringConfig, setBoringConfig] = useState(null);

  useEffect(() => { laadProject(); }, [id]);

  async function laadProject() {
    try {
      const data = await getProjectMetContext(id);
      setProject(data);
      // Herstel boring configuratie uit opgeslagen JSON
      if (data.boring_config) {
        try {
          const bc = typeof data.boring_config === "string" ? JSON.parse(data.boring_config) : data.boring_config;
          setBoringConfig(bc);
        } catch {}
      }
      setBewerkData({
        naam:           data.naam           ?? "",
        opdrachtgever:  data.opdrachtgever  ?? "",
        locatie:        data.locatie         ?? "",
        boorlengte_m:   data.boorlengte_m   ?? "",
        diameter_mm:    data.diameter_mm    ?? "",
        materiaal:      data.materiaal      ?? "PE100",
        bodemtype:      data.bodemtype      ?? "",
        bijzonderheden: data.bijzonderheden ?? "",
        status:         data.status         ?? "actief",
      });
      try {
        const { data: { user } } = await supabase.auth.getUser();
        if (user) setGebruiker({ email: user.email, naam: user.user_metadata?.full_name ?? null });
      } catch {}
    } catch (err) {
      console.error(err);
    } finally {
      setLaden(false);
    }
  }

  // ── Boorlijn opslaan/verwijderen vanuit MapTrace ─────────────
  // Geen JSON.stringify — Supabase accepteert objecten direct voor JSONB kolommen
  async function handleTraceOpgeslagen(data) {
    try {
      if (!data) {
        // Verwijderen
        await updateProject(id, {
          boortrace_geojson: null,
          diepte_punten:     null,
          analyse_punten:    null,
        });
        await laadProject(); // herlaad na verwijderen
      } else if (data._alleenDiepte) {
        // Auto-save dieptepunten — geen laadProject (te frequent, veroorzaakt re-renders)
        await updateProject(id, { diepte_punten: data.diepte_punten });
      } else if (data._alleenAnalyse) {
        // Auto-save analysepunten — geen laadProject
        await updateProject(id, { analyse_punten: data.analyse_punten });
      } else {
        // Boorlijn opslaan — geojson object direct meegeven (geen stringify)
        await updateProject(id, { boortrace_geojson: data });
        await laadProject(); // herlaad zodat bestaandTrace bijgewerkt is
      }
    } catch (err) {
      console.error("handleTraceOpgeslagen fout:", err);
      throw err; // doorgooi zodat de aanroeper de fout ziet
    }
  }

  async function handleOpslaan(e) {
    e.preventDefault();
    setOpslaan(true);
    try {
      await updateProject(id, {
        ...bewerkData,
        boorlengte_m: bewerkData.boorlengte_m ? Number(bewerkData.boorlengte_m) : null,
        diameter_mm:  bewerkData.diameter_mm  ? Number(bewerkData.diameter_mm)  : null,
      });
      await laadProject();
      setBewerkModaal(false);
    } catch (err) { console.error(err); }
    finally { setOpslaan(false); }
  }

  // ── Upload bestand naar Supabase Storage ───────────────────────
  async function uploadBestand(type, bestand) {
    setUploadLaden(s => ({ ...s, [type]: true }));
    setUploadStatus(s => ({ ...s, [type]: "Uploaden…" }));
    try {
      const pad = `${id}/${type}_${Date.now()}_${bestand.name}`;
      const { data: uploadData, error } = await supabase.storage
        .from("project-bestanden")
        .upload(pad, bestand, { upsert: true });

      if (error) throw error;

      const { data: urlData } = supabase.storage
        .from("project-bestanden")
        .getPublicUrl(uploadData.path);

      // Huidige bestandslijst ophalen en uitbreiden
      const huidigeMeta = (() => {
        try { return JSON.parse(project.bestanden_meta || "[]"); }
        catch { return []; }
      })();

      const nieuweEntry = {
        id:       `${type}_${Date.now()}`,
        type,
        naam:     bestand.name,
        url:      urlData.publicUrl,
        pad:      uploadData.path,
        grootte:  bestand.size,
      };

      const nieuweMeta = [...huidigeMeta.filter(b => b.type !== type), nieuweEntry];
      await updateProject(id, { bestanden_meta: JSON.stringify(nieuweMeta) });
      await laadProject();

      setUploadStatus(s => ({ ...s, [type]: `✓ ${bestand.name}` }));
    } catch (err) {
      console.error(err);
      setUploadStatus(s => ({ ...s, [type]: `✗ ${err.message}` }));
    } finally {
      setUploadLaden(s => ({ ...s, [type]: false }));
    }
  }

  // ── Helpers ────────────────────────────────────────────────────
  if (laden) {
    return (
      <div className="flex min-h-screen bg-gray-50">
        <Sidebar actiefProjectId={id} actieveStap={1} onStapWijzigen={() => {}} />
        <div className="flex-1 flex items-center justify-center">
          <p className="text-sm text-gray-400">Laden…</p>
        </div>
      </div>
    );
  }

  if (!project) {
    return (
      <div className="flex min-h-screen bg-gray-50">
        <Sidebar />
        <div className="flex-1 flex flex-col items-center justify-center gap-3">
          <p className="text-sm text-gray-500">Project niet gevonden.</p>
          <button onClick={() => router.push("/projecten")} className="text-xs text-orange-600 hover:underline">← Terug</button>
        </div>
      </div>
    );
  }

  // Bestaande uploads voor stap 2
  const bestaandeBestanden = (() => {
    try { return JSON.parse(project.bestanden_meta || "[]"); }
    catch { return []; }
  })();

  // ── Boring info banner — getoond in stap 2 t/m 9 ─────────────
  const BORING_MACHINES = [
    {id:"d10x15",model:"D10x15 S3"},{id:"d20x22",model:"D20x22 S3"},
    {id:"d23x30",model:"D23x30 S3"},{id:"d36x50",model:"D36x50 S3"},
  ];

  function BoringBanner({ showSVG = false }) {
    if (!boringConfig?.boringD) return null;
    const bc      = boringConfig;
    const res     = bc.items?.length ? computeBoring(bc.items) : null;
    const machine = BORING_MACHINES.find(m => m.id === bc.machine);

    return (
      <div className="bg-white border border-orange-200 rounded-xl mb-4 overflow-hidden">
        <div className="flex items-center gap-3 px-4 py-3 bg-orange-50 border-b border-orange-100">
          <div className="flex items-center gap-2">
            <div className="w-2 h-2 rounded-full bg-orange-500"/>
            <span className="text-xs font-semibold text-orange-700">Boring configuratie</span>
          </div>
          <div className="flex items-center gap-2 ml-2 flex-wrap">
            <span className="text-xs font-bold text-orange-600 bg-orange-100 px-2 py-0.5 rounded-full">
              Ø{bc.boringD} mm
            </span>
            {bc.items?.length > 0 && (
              <span className="text-xs text-orange-600">{bc.items.length} item{bc.items.length !== 1 ? "s" : ""}</span>
            )}
            {machine && (
              <span className="text-xs text-orange-600">· {machine.model}</span>
            )}
          </div>
          <button onClick={() => setActieveStap(1)}
                  className="ml-auto text-xs text-orange-500 hover:text-orange-700 underline">
            Aanpassen →
          </button>
        </div>

        {/* Items kleur-chips */}
        {bc.items?.length > 0 && (
          <div className="flex flex-wrap gap-2 px-4 py-2.5">
            {bc.items.map((item, idx) => {
              if (item.type === "mb") {
                const color = BORING_COLORS[idx % BORING_COLORS.length];
                return (
                  <div key={item.id} className="flex items-center gap-1.5 bg-gray-50 border border-gray-100 rounded-full px-2.5 py-1">
                    <div className="w-2 h-2 rounded-full flex-shrink-0" style={{background: color}}/>
                    <span className="text-xs text-gray-700 font-medium">PE{item.dn}</span>
                    {item.contents.length > 0 && (
                      <div className="flex gap-0.5 ml-0.5">
                        {item.contents.map(c => {
                          const cat = BORING_CATS.find(cc => cc.items.some(i => i.label === c.label));
                          return <div key={c.id} className="w-1.5 h-1.5 rounded-full" style={{background: cat?.color || "#6B7280"}}/>;
                        })}
                      </div>
                    )}
                  </div>
                );
              }
              const cat = BORING_CATS.find(c => c.items.some(i => i.label === item.label));
              return (
                <div key={item.id} className="flex items-center gap-1.5 bg-gray-50 border border-gray-100 rounded-full px-2.5 py-1">
                  <div className="w-2 h-2 rounded-full flex-shrink-0" style={{background: cat?.color || "#6B7280"}}/>
                  <span className="text-xs text-gray-700">{item.label}</span>
                </div>
              );
            })}
          </div>
        )}

        {/* Dwarsdoorsnede (alleen als showSVG=true) */}
        {showSVG && res && (
          <div className="border-t border-orange-100 py-3 px-4">
            <p className="text-xs text-gray-500 font-medium mb-2">Dwarsdoorsnede boring</p>
            <div className="flex justify-center">
              <BoringSVG res={res} customPos={bc.customPos ?? {}} size={180} showLabel={true}/>
            </div>
          </div>
        )}
      </div>
    );
  }

  // ═══════════════════════════════════════════════════════════════
  //  Stap content
  // ═══════════════════════════════════════════════════════════════
  function renderStap() {
    switch (actieveStap) {

      // ── Stap 1: Projectinformatie ──────────────────────────────
      case 1:
        return (
          <div className="max-w-2xl space-y-0">
            <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
              <div className="flex items-center justify-between px-5 py-4 border-b border-gray-100">
                <h2 className="text-sm font-semibold text-gray-800">Projectgegevens</h2>
                <button onClick={() => setBewerkModaal(true)} className="px-3 py-1.5 text-xs bg-orange-500 text-white rounded-lg hover:bg-orange-600 transition-colors">
                  Bewerken
                </button>
              </div>
              <div className="divide-y divide-gray-100">
                {[
                  { label: "Projectnaam",   waarde: project.naam },
                  { label: "Opdrachtgever", waarde: project.opdrachtgever },
                  { label: "Locatie",       waarde: project.locatie },
                  { label: "Boorlengte",    waarde: project.boorlengte_m  ? `${project.boorlengte_m} m`     : null },
                  { label: "Status",        waarde: project.status },
                ].map(({ label, waarde }, i) =>
                  waarde ? (
                    <div key={i} className="flex items-start justify-between gap-6 px-5 py-3">
                      <span className="text-xs text-gray-500 font-medium flex-shrink-0">{label}</span>
                      <span className="text-xs text-gray-900 text-right">{waarde}</span>
                    </div>
                  ) : null
                )}
                {project.bijzonderheden && (
                  <div className="px-5 py-3">
                    <span className="text-xs text-gray-500 font-medium block mb-1">Bijzonderheden</span>
                    <span className="text-xs text-gray-800">{project.bijzonderheden}</span>
                  </div>
                )}
              </div>
            </div>

            {bewerkModaal && (
              <div className="fixed inset-0 bg-black/30 flex items-center justify-center z-50 p-4">
                <div className="bg-white rounded-xl shadow-xl w-full max-w-lg max-h-[90vh] overflow-y-auto">
                  <div className="flex items-center justify-between px-5 py-4 border-b border-gray-100">
                    <h3 className="text-sm font-semibold text-gray-800">Project bewerken</h3>
                    <button onClick={() => setBewerkModaal(false)} className="text-gray-400 hover:text-gray-600 text-xl">×</button>
                  </div>
                  <form onSubmit={handleOpslaan} className="p-5 space-y-3">
                    {[
                      { label: "Projectnaam",      key: "naam",          type: "text"   },
                      { label: "Opdrachtgever",     key: "opdrachtgever", type: "text"   },
                      { label: "Locatie",           key: "locatie",       type: "text"   },
                      { label: "Boorlengte (m)",    key: "boorlengte_m",  type: "number" },
                      { label: "Diameter (mm)",     key: "diameter_mm",   type: "number" },
                      { label: "Bodemtype",         key: "bodemtype",     type: "text"   },
                    ].map(({ label, key, type }) => (
                      <div key={key}>
                        <label className="text-xs text-gray-500 font-medium block mb-1">{label}</label>
                        <input
                          type={type}
                          value={bewerkData[key] ?? ""}
                          onChange={e => setBewerkData(d => ({ ...d, [key]: e.target.value }))}
                          className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:border-orange-400 outline-none"
                        />
                      </div>
                    ))}
                    <div>
                      <label className="text-xs text-gray-500 font-medium block mb-1">Materiaal</label>
                      <select value={bewerkData.materiaal ?? "PE100"} onChange={e => setBewerkData(d => ({ ...d, materiaal: e.target.value }))} className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm outline-none">
                        {["PE100","PE80","PVC","Staal","GVK","Anders"].map(m => <option key={m}>{m}</option>)}
                      </select>
                    </div>
                    <div>
                      <label className="text-xs text-gray-500 font-medium block mb-1">Status</label>
                      <select value={bewerkData.status ?? "actief"} onChange={e => setBewerkData(d => ({ ...d, status: e.target.value }))} className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm outline-none">
                        {["actief","in uitvoering","afgerond","on hold"].map(s => <option key={s}>{s}</option>)}
                      </select>
                    </div>
                    <div>
                      <label className="text-xs text-gray-500 font-medium block mb-1">Bijzonderheden</label>
                      <textarea rows={3} value={bewerkData.bijzonderheden ?? ""} onChange={e => setBewerkData(d => ({ ...d, bijzonderheden: e.target.value }))} className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm outline-none resize-none" />
                    </div>
                    <div className="flex gap-3 pt-2">
                      <button type="button" onClick={() => setBewerkModaal(false)} className="flex-1 px-4 py-2 text-sm border border-gray-200 rounded-lg hover:bg-gray-50">Annuleren</button>
                      <button type="submit" disabled={opslaan} className="flex-1 px-4 py-2 text-sm bg-orange-500 text-white rounded-lg hover:bg-orange-600 disabled:opacity-50">
                        {opslaan ? "Opslaan…" : "Opslaan"}
                      </button>
                    </div>
                  </form>
                </div>
              </div>
            )}

            {/* ── Boring configurator ── */}
            <BoringConfigurator projectId={id} initialConfig={project.boring_config} onConfigChange={setBoringConfig} />
          </div>
        );

      // ── Stap 2: Ontwerp inladen ────────────────────────────────
      case 2: {
        // NEN-1775 standaardkleuren (zelfde als stap 3)
        const TYPEN = [
          { type: "LS",    label: "Laagspanning (LS)",        kleur: "#7B00AA", accept: ".dxf,.gml,.kml,.geojson,.zip" },
          { type: "MS",    label: "Middenspanning (MS)",       kleur: "#00CCFF", accept: ".dxf,.gml,.kml,.geojson,.zip" },
          { type: "Gas",   label: "Gas (lage druk)",           kleur: "#FFFF00", accept: ".dxf,.gml,.kml,.geojson,.zip" },
          { type: "Water", label: "Water",                     kleur: "#000080", accept: ".dxf,.gml,.kml,.geojson,.zip" },
          { type: "Data",  label: "Data / Telecom",            kleur: "#00CC00", accept: ".dxf,.gml,.kml,.geojson,.zip" },
          { type: "KLIC",  label: "KLIC-melding (ZIP / GML)", kleur: "#FF0000", accept: ".zip,.gml" },
        ];

        // 3 aanpasbare extra velden — namen opgeslagen in project.custom_veld_namen (JSON)
        const savedCustomNamen = (() => {
          try { return JSON.parse(project.custom_veld_namen || "{}"); } catch { return {}; }
        })();
        const CUSTOM = [
          { type: "custom1", defaultNaam: "Aangepast 1", kleur: "#888888" },
          { type: "custom2", defaultNaam: "Aangepast 2", kleur: "#555555" },
          { type: "custom3", defaultNaam: "Aangepast 3", kleur: "#333333" },
        ];

        const alleTypen = [...TYPEN, ...CUSTOM];
        const aantalGeladen = alleTypen.filter(t => bestaandeBestanden.find(b => b.type === t.type)).length;

        async function verwijderBestand(type) {
          const meta = bestaandeBestanden.find(b => b.type === type);
          if (!meta) return;
          try { await supabase.storage.from("project-bestanden").remove([meta.pad]); } catch {}
          const nieuw = bestaandeBestanden.filter(b => b.type !== type);
          await updateProject(id, { bestanden_meta: JSON.stringify(nieuw) });
          await laadProject();
        }

        async function slaCustomNaamOp(type, naam) {
          const huidig = (() => {
            try { return JSON.parse(project.custom_veld_namen || "{}"); } catch { return {}; }
          })();
          await updateProject(id, { custom_veld_namen: JSON.stringify({ ...huidig, [type]: naam }) });
          await laadProject();
        }

        function LaagRij({ type, label, kleur, accept, isCustom }) {
          const bestaand   = bestaandeBestanden.find(b => b.type === type);
          const isLaden    = uploadLaden[type];
          const uploadFout = uploadStatus[type]?.startsWith("✗") ? uploadStatus[type] : null;
          const naam       = isCustom ? (savedCustomNamen[type] || label) : label;

          return (
            <div className="flex items-center gap-3 px-5 py-3.5">
              {/* Kleurbol */}
              <div className="w-3 h-3 rounded-full flex-shrink-0 border border-white shadow-sm"
                style={{ background: kleur }} />

              {/* Naam (custom = bewerkbaar) */}
              <div className="flex-1 min-w-0">
                {isCustom ? (
                  <input
                    type="text"
                    defaultValue={naam}
                    onBlur={e => { if (e.target.value !== naam) slaCustomNaamOp(type, e.target.value); }}
                    placeholder={label}
                    className="text-xs font-medium text-gray-800 bg-transparent border-b border-dashed border-gray-200 focus:border-orange-400 outline-none w-full pb-0.5"
                  />
                ) : (
                  <div className="text-xs font-medium text-gray-800">{naam}</div>
                )}
                {bestaand && !uploadFout ? (
                  <div className="flex items-center gap-1.5 mt-0.5">
                    <span className="text-xs text-green-600 font-medium">✓ Opgeslagen</span>
                    <span className="text-xs text-gray-400 truncate">{bestaand.naam}</span>
                    {bestaand.grootte && (
                      <span className="text-xs text-gray-300">· {(bestaand.grootte / 1024).toFixed(0)} KB</span>
                    )}
                  </div>
                ) : uploadFout ? (
                  <div className="text-xs text-red-500 mt-0.5">{uploadFout}</div>
                ) : (
                  <div className="text-xs text-gray-400 mt-0.5">{accept ?? ".dxf,.gml,.kml,.geojson,.zip"}</div>
                )}
              </div>

              {/* Acties */}
              <div className="flex items-center gap-2 flex-shrink-0">
                {bestaand && (
                  <button onClick={() => verwijderBestand(type)}
                    className="w-6 h-6 flex items-center justify-center rounded text-gray-300 hover:text-red-400 hover:bg-red-50 transition-colors text-sm"
                    title="Verwijderen">×</button>
                )}
                <label className={`px-3 py-1.5 text-xs border border-gray-200 rounded-lg cursor-pointer text-gray-600 transition-colors ${isLaden ? "opacity-50 cursor-wait" : "hover:bg-gray-50"}`}>
                  {isLaden ? "Uploaden…" : bestaand ? "Vervangen" : "Kiezen"}
                  <input type="file" accept={accept ?? ".dxf,.gml,.kml,.geojson,.zip"} className="hidden"
                    disabled={isLaden}
                    onChange={e => { if (e.target.files?.[0]) uploadBestand(type, e.target.files[0]); }} />
                </label>
              </div>
            </div>
          );
        }

        return (
          <div className="max-w-2xl space-y-4">
            <div className="flex items-center gap-3">
              <p className="text-sm text-gray-500 flex-1">
                Upload leidingenontwerpen per type. Bestanden worden opgeslagen en blijven bewaard.
              </p>
              {aantalGeladen > 0 && (
                <span className="text-xs bg-green-100 text-green-700 font-medium px-2 py-1 rounded-full flex-shrink-0">
                  {aantalGeladen} / {alleTypen.length} opgeslagen
                </span>
              )}
            </div>

            {/* Standaard KLIC-lagen */}
            <div className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
              {TYPEN.map(t => <LaagRij key={t.type} {...t} isCustom={false} />)}
            </div>

            {/* Aangepaste lagen */}
            <div>
              <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2 px-1">
                Eigen lagen (klik op de naam om te hernoemen)
              </div>
              <div className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
                {CUSTOM.map(t => <LaagRij key={t.type} {...t} isCustom={true} />)}
              </div>
            </div>

            <div className="flex items-center justify-between pt-1">
              <p className="text-xs text-gray-400">
                Bucket: <code className="bg-gray-100 px-1 rounded">project-bestanden</code>
              </p>
              <button onClick={() => setActieveStap(3)} disabled={aantalGeladen === 0}
                className="px-4 py-2 text-sm bg-orange-500 text-white rounded-lg hover:bg-orange-600 disabled:opacity-40 transition-colors font-medium">
                Opgeslagen — naar stap 3 →
              </button>
            </div>
          </div>
        );
      }

      // ── Stap 3: Ontwerp bekijken ───────────────────────────────
      case 3:
        return (
          <OntwerpKaart
            project={project}
            projectId={id}
            onOpgeslagen={laadProject}
          />
        );

      // ── Stap 4: Boorlijn tekenen ───────────────────────────────
      case 4:
        return (
          <div>
            <BoringBanner />
            <MapTrace
              projectId={id}
              project={project}
              boringConfig={boringConfig}
              onTraceOpgeslagen={handleTraceOpgeslagen}
            />
          </div>
        );

      // ── Stap 5: Oppervlakteanalyse ─────────────────────────────
      case 5:
        return (
          <OppervlakteAnalyse
            project={project}
            boringConfig={boringConfig}
            onAnalyseOpgeslagen={async (resultaten) => {
              await handleTraceOpgeslagen({ _alleenAnalyse: true, analyse_punten: resultaten });
            }}
          />
        );

      // ── Stap 6: Diepteligging ──────────────────────────────────
      case 6:
        return (
          <div>
            <BoringBanner showSVG={true} />
            <Diepteligging
              project={project}
              boringConfig={boringConfig}
              onSave={async (updates) => {
                await updateProject(id, updates);
              }}
            />
          </div>
        );

      // ── Stap 7: Machine & bentonietlocatie ─────────────────────
      case 7:
        return (
          <div>
            <BoringBanner />
            <MachineLocatie
              project={project}
              boringConfig={boringConfig}
              onSave={async (updates) => { await updateProject(id, updates); }}
            />
          </div>
        );

      // ── Stap 8: 3D Ontwerp ─────────────────────────────────────
      case 8:
        return (
          <div>
            <BoringBanner />
            <Stap8_3D project={project} boringConfig={boringConfig} />
          </div>
        );

      // ── Stap 9: Eindontwerp ────────────────────────────────────
      case 9:
        return <Eindontwerp project={project} boringConfig={boringConfig} />;

      // ── Stap 10–13: Nog niet uitgewerkt ──────────────────────
      default:
        return (
          <div className="flex flex-col items-center justify-center h-64 text-center">
            <div className="text-4xl mb-4">🚧</div>
            <h2 className="text-base font-semibold text-gray-700 mb-1">{STAP_LABELS[actieveStap]}</h2>
            <p className="text-sm text-gray-400">Wordt uitgewerkt zodra stap 1 t/m 9 stabiel zijn.</p>
          </div>
        );
    }
  }

  // ═══════════════════════════════════════════════════════════════
  return (
    <div className="flex min-h-screen bg-gray-50">
      <Sidebar
        actiefProjectId={id}
        actieveStap={actieveStap}
        onStapWijzigen={setActieveStap}
        project={project}
        gebruiker={gebruiker}
      />

      <main className="flex-1 min-w-0 flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-100 bg-white sticky top-0 z-20 flex-shrink-0">
          <div>
            <div className="text-xs text-gray-400">Stap {actieveStap} van 13</div>
            <h1 className="text-base font-semibold text-gray-900 mt-0.5">{STAP_LABELS[actieveStap]}</h1>
          </div>
          <div className="flex items-center gap-2">
            {actieveStap > 1 && (
              <button onClick={() => setActieveStap(s => s - 1)} className="px-3 py-1.5 text-xs border border-gray-200 rounded-lg hover:bg-gray-50 text-gray-600 transition-colors">
                ← Vorige
              </button>
            )}
            {actieveStap < STAP_MAX_UITGEWERKT && (
              <button onClick={() => setActieveStap(s => s + 1)} className="px-3 py-1.5 text-xs bg-orange-500 text-white rounded-lg hover:bg-orange-600 transition-colors">
                Volgende →
              </button>
            )}
          </div>
        </div>

        {/* Content */}
        <div className="flex-1 p-6 overflow-auto">
          {renderStap()}
          {/* AI Analyse box — per stap */}
          {actieveStap >= 1 && actieveStap <= 9 && (
            <PrescanAnalyse
              key={actieveStap}
              stap={actieveStap}
              project={project}
              boringConfig={boringConfig}
            />
          )}
        </div>

        {/* AI-assistent — altijd beschikbaar per stap */}
        {actieveStap >= 1 && actieveStap <= 9 && (
          <PrescanBot
            stap={actieveStap}
            project={project}
            boringConfig={boringConfig}
          />
        )}
      </main>
    </div>
  );
}
