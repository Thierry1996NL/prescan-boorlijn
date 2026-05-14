"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import { getProjecten, maakProject } from "@/lib/supabase-queries";
import Sidebar from "@/components/Sidebar";

export default function ProjectenPagina() {
  const router = useRouter();

  const [projecten,   setProjecten]   = useState([]);
  const [laden,       setLaden]       = useState(true);
  const [gebruiker,   setGebruiker]   = useState(null);
  const [modaalOpen,  setModaalOpen]  = useState(false);
  const [opslaan,     setOpslaan]     = useState(false);
  const [nieuw, setNieuw] = useState({
    naam: "", opdrachtgever: "", locatie: "",
    boorlengte_m: "", diameter_mm: "",
    materiaal: "PE100", bodemtype: "", bijzonderheden: "",
  });

  useEffect(() => { laadProjecten(); laadGebruiker(); }, []);

  async function laadProjecten() {
    try {
      const data = await getProjecten();
      setProjecten(data || []);
    } catch (err) {
      console.error(err);
    } finally {
      setLaden(false);
    }
  }

  async function laadGebruiker() {
    try {
      const { supabase } = await import("@/lib/supabase-queries");
      const { data: { user } } = await supabase.auth.getUser();
      if (user) {
        setGebruiker({ email: user.email, naam: user.user_metadata?.full_name ?? null });
      }
    } catch { /* optioneel */ }
  }

  async function handleMaakProject(e) {
    e.preventDefault();
    setOpslaan(true);
    try {
      const proj = await maakProject({
        ...nieuw,
        boorlengte_m: nieuw.boorlengte_m ? Number(nieuw.boorlengte_m) : null,
        diameter_mm:  nieuw.diameter_mm  ? Number(nieuw.diameter_mm)  : null,
      });
      setModaalOpen(false);
      setNieuw({ naam: "", opdrachtgever: "", locatie: "", boorlengte_m: "", diameter_mm: "", materiaal: "PE100", bodemtype: "", bijzonderheden: "" });
      router.push(`/project/${proj.id}`);
    } catch (err) {
      console.error(err);
    } finally {
      setOpslaan(false);
    }
  }

  // Status kleur
  const statusKleur = (status) => {
    if (!status) return "bg-gray-100 text-gray-500";
    const s = status.toLowerCase();
    if (s.includes("actief"))       return "bg-green-100 text-green-700";
    if (s.includes("uitvoering"))   return "bg-blue-100 text-blue-700";
    if (s.includes("afgerond"))     return "bg-gray-100 text-gray-600";
    if (s.includes("hold"))         return "bg-yellow-100 text-yellow-700";
    return "bg-gray-100 text-gray-500";
  };

  return (
    <div className="flex min-h-screen bg-gray-50">
      <Sidebar gebruiker={gebruiker} />

      <main className="flex-1 min-w-0 p-6 overflow-auto">
        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-xl font-semibold text-gray-900">Projecten</h1>
            <p className="text-sm text-gray-400 mt-0.5">
              {laden ? "Laden…" : `${projecten.length} project${projecten.length !== 1 ? "en" : ""}`}
            </p>
          </div>
          <button
            onClick={() => setModaalOpen(true)}
            className="px-4 py-2 text-sm bg-orange-500 text-white rounded-lg hover:bg-orange-600 transition-colors font-medium"
          >
            + Nieuw project
          </button>
        </div>

        {/* Projectenlijst */}
        {laden ? (
          <div className="flex items-center justify-center h-48">
            <p className="text-sm text-gray-400">Laden…</p>
          </div>
        ) : projecten.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-64 bg-white border border-gray-200 rounded-xl text-center gap-3">
            <div className="text-3xl">📁</div>
            <div>
              <p className="text-sm font-medium text-gray-700">Nog geen projecten</p>
              <p className="text-xs text-gray-400 mt-1">Maak je eerste project aan om te beginnen.</p>
            </div>
            <button
              onClick={() => setModaalOpen(true)}
              className="px-4 py-2 text-sm bg-orange-500 text-white rounded-lg hover:bg-orange-600 transition-colors"
            >
              + Nieuw project
            </button>
          </div>
        ) : (
          <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
            {/* Tabelheader */}
            <div className="grid grid-cols-[2fr_1.5fr_1fr_1fr_auto] gap-4 px-5 py-3 border-b border-gray-100 bg-gray-50">
              <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Project</span>
              <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Opdrachtgever</span>
              <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Locatie</span>
              <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Status</span>
              <span />
            </div>

            {/* Rijen */}
            {projecten.map((proj, i) => (
              <button
                key={proj.id}
                onClick={() => router.push(`/project/${proj.id}`)}
                className={`grid grid-cols-[2fr_1.5fr_1fr_1fr_auto] gap-4 items-center w-full px-5 py-3.5 text-left hover:bg-orange-50 transition-colors ${
                  i !== 0 ? "border-t border-gray-100" : ""
                }`}
              >
                <div>
                  <div className="text-sm font-medium text-gray-900 truncate">{proj.naam}</div>
                  {proj.boorlengte_m && (
                    <div className="text-xs text-gray-400 mt-0.5">{proj.boorlengte_m} m · {proj.diameter_mm ? `Ø${proj.diameter_mm} mm` : ""}</div>
                  )}
                </div>
                <div className="text-sm text-gray-600 truncate">{proj.opdrachtgever || "—"}</div>
                <div className="text-sm text-gray-600 truncate">{proj.locatie || "—"}</div>
                <div>
                  <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${statusKleur(proj.status)}`}>
                    {proj.status || "—"}
                  </span>
                </div>
                <div className="text-gray-400 text-sm">→</div>
              </button>
            ))}
          </div>
        )}

        {/* ── Nieuw project modaal ────────────────────────── */}
        {modaalOpen && (
          <div className="fixed inset-0 bg-black/30 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-xl shadow-xl w-full max-w-lg max-h-[90vh] overflow-y-auto">
              <div className="flex items-center justify-between px-5 py-4 border-b border-gray-100">
                <h2 className="text-sm font-semibold text-gray-800">Nieuw project aanmaken</h2>
                <button onClick={() => setModaalOpen(false)} className="text-gray-400 hover:text-gray-600 text-xl leading-none">×</button>
              </div>
              <form onSubmit={handleMaakProject} className="p-5 space-y-3">
                {[
                  { label: "Projectnaam *",     key: "naam",          type: "text",   required: true  },
                  { label: "Opdrachtgever",      key: "opdrachtgever", type: "text",   required: false },
                  { label: "Locatie / adres",    key: "locatie",       type: "text",   required: false },
                  { label: "Boorlengte (m)",     key: "boorlengte_m",  type: "number", required: false },
                  { label: "Diameter (mm)",      key: "diameter_mm",   type: "number", required: false },
                  { label: "Bodemtype",          key: "bodemtype",     type: "text",   required: false },
                ].map(({ label, key, type, required }) => (
                  <div key={key}>
                    <label className="text-xs text-gray-500 font-medium block mb-1">{label}</label>
                    <input
                      type={type}
                      required={required}
                      value={nieuw[key] ?? ""}
                      onChange={e => setNieuw(n => ({ ...n, [key]: e.target.value }))}
                      className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:border-orange-400 outline-none"
                    />
                  </div>
                ))}

                <div>
                  <label className="text-xs text-gray-500 font-medium block mb-1">Materiaal</label>
                  <select
                    value={nieuw.materiaal}
                    onChange={e => setNieuw(n => ({ ...n, materiaal: e.target.value }))}
                    className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:border-orange-400 outline-none"
                  >
                    {["PE100", "PE80", "PVC", "Staal", "GVK", "Anders"].map(m => (
                      <option key={m}>{m}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="text-xs text-gray-500 font-medium block mb-1">Bijzonderheden</label>
                  <textarea
                    value={nieuw.bijzonderheden}
                    onChange={e => setNieuw(n => ({ ...n, bijzonderheden: e.target.value }))}
                    rows={3}
                    className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:border-orange-400 outline-none resize-none"
                  />
                </div>

                <div className="flex gap-3 pt-2">
                  <button
                    type="button"
                    onClick={() => setModaalOpen(false)}
                    className="flex-1 px-4 py-2 text-sm border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
                  >
                    Annuleren
                  </button>
                  <button
                    type="submit"
                    disabled={opslaan || !nieuw.naam}
                    className="flex-1 px-4 py-2 text-sm bg-orange-500 text-white rounded-lg hover:bg-orange-600 disabled:opacity-50 transition-colors"
                  >
                    {opslaan ? "Aanmaken…" : "Project aanmaken"}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
