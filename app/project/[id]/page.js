"use client";

import { useState, useEffect } from "react";
import { useParams, useRouter } from "next/navigation";
import { getProjectMetContext, updateProject } from "@/lib/supabase-queries";
import PrescanChat from "@/components/PrescanChat";
import Sidebar from "@/components/Sidebar";
import dynamic from "next/dynamic";

const MapTrace = dynamic(() => import("@/components/MapTrace"), { ssr: false });

export default function ProjectDetailPagina() {
  const { id } = useParams();
  const router = useRouter();
  const [project, setProject] = useState(null);
  const [laden, setLaden] = useState(true);
  const [actieveTab, setActieveTab] = useState("details");
  const [bewerkModaal, setBewerkModaal] = useState(false);
  const [bewerkData, setBewerkData] = useState({});
  const [opslaan, setOpslaan] = useState(false);

  useEffect(() => { laadProject(); }, [id]);

  async function laadProject() {
    try {
      const data = await getProjectMetContext(id);
      setProject(data);
      setBewerkData({
        naam: data.naam ?? "",
        opdrachtgever: data.opdrachtgever ?? "",
        locatie: data.locatie ?? "",
        boorlengte_m: data.boorlengte_m ?? "",
        diameter_mm: data.diameter_mm ?? "",
        materiaal: data.materiaal ?? "PE100",
        bodemtype: data.bodemtype ?? "",
        bijzonderheden: data.bijzonderheden ?? "",
        status: data.status ?? "actief",
      });
    } catch (err) {
      console.error(err);
    } finally {
      setLaden(false);
    }
  }

  async function handleOpslaan(e) {
    e.preventDefault();
    setOpslaan(true);
    try {
      await updateProject(id, {
        ...bewerkData,
        boorlengte_m: bewerkData.boorlengte_m ? Number(bewerkData.boorlengte_m) : null,
        diameter_mm: bewerkData.diameter_mm ? Number(bewerkData.diameter_mm) : null,
      });
      await laadProject();
      setBewerkModaal(false);
    } catch (err) {
      console.error(err);
    } finally {
      setOpslaan(false);
    }
  }

  async function handleTraceOpgeslagen(geojson) {
    await updateProject(id, { boortrace_geojson: geojson });
    await laadProject();
  }

  const risicoBadge = (risico) => {
    const stijlen = {
      rood: "bg-red-50 text-red-600 border-red-200",
      oranje: "bg-orange-50 text-orange-600 border-orange-200",
      groen: "bg-green-50 text-green-600 border-green-200",
    };
    return (
      <span className={`text-xs border px-2 py-0.5 rounded-full font-medium ${stijlen[risico] || ""}`}>
        ● {risico}
      </span>
    );
  };

  if (laden) {
    return (
      <div className="flex min-h-screen bg-gray-50">
        <Sidebar actiefProjectId={id} />
        <div className="flex-1 flex items-center justify-center">
          <p className="text-sm text-gray-400">Laden...</p>
        </div>
      </div>
    );
  }

  if (!project) {
    return (
      <div className="flex min-h-screen bg-gray-50">
        <Sidebar />
        <div className="flex-1 flex items-center justify-center">
          <p className="text-sm text-red-500">Project niet gevonden.</p>
        </div>
      </div>
    );
  }

  const tabs = [
    { id: "details", label: "Projectinformatie", icon: "📋" },
    { id: "trace", label: "Tracé", icon: "📍" },
    { id: "kruisingen", label: "Kruisingen", icon: "⚡", count: project.kruisingen?.length ?? 0 },
    { id: "chat", label: "AI Assistent", icon: "🛠" },
  ];

  const velden = [
    { label: "Projectnaam", key: "naam" },
    { label: "Opdrachtgever", key: "opdrachtgever" },
    { label: "Locatie", key: "locatie" },
    { label: "Boorlengte", key: "boorlengte_m", format: (v) => v ? `${v} m` : "—" },
    { label: "Diameter", key: "diameter_mm", format: (v) => v ? `Ø${v} mm` : "—" },
    { label: "Materiaal", key: "materiaal" },
    { label: "Bodemtype", key: "bodemtype" },
    { label: "Status", key: "status" },
    { label: "Bijzonderheden", key: "bijzonderheden" },
  ];

  return (
    <div className="flex min-h-screen bg-gray-50">
      <Sidebar actiefProjectId={id} />

      {/* Project tabs sidebar */}
      <aside className="w-44 flex-shrink-0 bg-white border-r border-gray-200 flex flex-col pt-4">
        <div className="px-4 mb-2">
          <div className="text-xs text-gray-400 font-medium mb-1 truncate">{project.naam}</div>
        </div>
        <nav className="flex flex-col gap-0.5 px-3">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActieveTab(tab.id)}
              className={`flex items-center gap-2.5 px-3 py-2.5 rounded-lg text-sm transition-all duration-150 w-full text-left ${
                actieveTab === tab.id
                  ? "bg-blue-50 text-blue-700 font-medium"
                  : "text-gray-500 hover:text-gray-900 hover:bg-gray-100"
              }`}
            >
              <span>{tab.icon}</span>
              <span className="flex-1 truncate">{tab.label}</span>
              {tab.count !== undefined && tab.count > 0 && (
                <span className="text-xs bg-gray-100 text-gray-500 px-1.5 py-0.5 rounded-full">{tab.count}</span>
              )}
            </button>
          ))}
        </nav>
      </aside>

      {/* Hoofdinhoud */}
      <main className="flex-1 flex flex-col min-w-0">
        <header className="bg-white border-b border-gray-200 px-6 py-3 flex items-center justify-between flex-shrink-0">
          <h1 className="text-sm font-semibold text-gray-900">
            {tabs.find(t => t.id === actieveTab)?.icon} {tabs.find(t => t.id === actieveTab)?.label}
          </h1>
          <div className="flex items-center gap-3 text-xs text-gray-400">
            {project.locatie && <span>📍 {project.locatie}</span>}
            {project.boorlengte_m && <span>{project.boorlengte_m} m</span>}
            {project.diameter_mm && <span>Ø{project.diameter_mm} mm</span>}
          </div>
        </header>

        <div className="flex-1 overflow-auto">

          {/* Projectinformatie */}
          {actieveTab === "details" && (
            <div className="p-6 max-w-lg">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-base font-semibold text-gray-900">Projectinformatie</h2>
                <button
                  onClick={() => setBewerkModaal(true)}
                  className="flex items-center gap-1.5 text-xs text-blue-600 font-medium bg-blue-50 hover:bg-blue-100 px-3 py-1.5 rounded-lg transition-colors"
                >
                  ✏️ Bewerken
                </button>
              </div>
              <div className="bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
                {velden.map(({ label, key, format }, i) => (
                  <div key={key} className={`flex items-start justify-between gap-4 px-4 py-3 ${i !== 0 ? "border-t border-gray-100" : ""}`}>
                    <span className="text-xs text-gray-400 font-medium flex-shrink-0">{label}</span>
                    <span className="text-xs text-gray-900 text-right font-medium">
                      {format ? format(project[key]) : project[key] || <span className="text-gray-300">—</span>}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Tracé — GIS kaart */}
          {actieveTab === "trace" && (
            <div className="p-4 h-full flex flex-col" style={{ minHeight: "calc(100vh - 100px)" }}>
              <MapTrace project={project} onTraceOpgeslagen={handleTraceOpgeslagen} />
            </div>
          )}

          {/* Kruisingen */}
          {actieveTab === "kruisingen" && (
            <div className="p-6 max-w-4xl mx-auto">
              <h2 className="text-base font-semibold text-gray-900 mb-4">Gedetecteerde kruisingen</h2>
              {project.kruisingen?.length === 0 ? (
                <div className="text-center py-16 border-2 border-dashed border-gray-200 rounded-xl bg-white">
                  <div className="text-3xl mb-3">⚡</div>
                  <p className="text-gray-500 text-sm">Nog geen kruisingen geregistreerd.</p>
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  {project.kruisingen.map((k) => (
                    <div key={k.id} className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
                      <div className="flex items-start justify-between gap-4">
                        <div>
                          <div className="font-medium text-gray-900 text-sm">{k.leidingtype}</div>
                          <div className="text-xs text-gray-400 mt-0.5">{k.netbeheerder}</div>
                          {k.aanbeveling && <div className="text-xs text-gray-500 mt-2 max-w-lg">{k.aanbeveling}</div>}
                        </div>
                        <div className="flex flex-col items-end gap-1.5 flex-shrink-0">
                          {risicoBadge(k.risico)}
                          <span className="text-xs text-gray-400">{k.afstand_cm} cm</span>
                          {k.diepte_m && <span className="text-xs text-gray-400">{k.diepte_m} m diep</span>}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* AI Assistent */}
          {actieveTab === "chat" && (
            <div className="h-full p-4">
              <div className="h-full max-w-4xl mx-auto">
                <PrescanChat projectId={id} projectNaam={project.naam} />
              </div>
            </div>
          )}
        </div>
      </main>

      {/* Bewerk modaal */}
      {bewerkModaal && (
        <div className="fixed inset-0 bg-black/30 flex items-center justify-center z-50 p-4">
          <div className="bg-white rounded-xl w-full max-w-lg max-h-[90vh] overflow-y-auto shadow-xl" style={{ animation: "fadeUp 0.2s ease" }}>
            <div className="p-5 border-b border-gray-100 flex items-center justify-between">
              <h2 className="font-semibold text-gray-900 text-sm">Project bewerken</h2>
              <button onClick={() => setBewerkModaal(false)} className="text-gray-400 hover:text-gray-700 text-lg leading-none">✕</button>
            </div>
            <form onSubmit={handleOpslaan} className="p-5 flex flex-col gap-4">
              {[
                { label: "Projectnaam *", key: "naam", required: true },
                { label: "Opdrachtgever", key: "opdrachtgever" },
                { label: "Locatie", key: "locatie" },
                { label: "Boorlengte (m)", key: "boorlengte_m", type: "number" },
                { label: "Diameter (mm)", key: "diameter_mm", type: "number" },
                { label: "Bodemtype", key: "bodemtype" },
              ].map(({ label, key, required, type }) => (
                <div key={key}>
                  <label className="block text-xs text-gray-500 mb-1.5 font-medium">{label}</label>
                  <input
                    type={type || "text"}
                    value={bewerkData[key]}
                    onChange={(e) => setBewerkData(prev => ({ ...prev, [key]: e.target.value }))}
                    required={required}
                    className="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm text-gray-900 outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-100 transition-colors"
                  />
                </div>
              ))}
              <div>
                <label className="block text-xs text-gray-500 mb-1.5 font-medium">Buismateriaal</label>
                <select value={bewerkData.materiaal} onChange={(e) => setBewerkData(prev => ({ ...prev, materiaal: e.target.value }))} className="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm outline-none focus:border-blue-500 transition-colors">
                  {["PE100", "PE80", "Staal", "GVK", "PVC", "Anders"].map(m => <option key={m} value={m}>{m}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-xs text-gray-500 mb-1.5 font-medium">Status</label>
                <select value={bewerkData.status} onChange={(e) => setBewerkData(prev => ({ ...prev, status: e.target.value }))} className="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm outline-none focus:border-blue-500 transition-colors">
                  {["actief", "afgerond", "on-hold"].map(s => <option key={s} value={s}>{s}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-xs text-gray-500 mb-1.5 font-medium">Bijzonderheden</label>
                <textarea value={bewerkData.bijzonderheden} onChange={(e) => setBewerkData(prev => ({ ...prev, bijzonderheden: e.target.value }))} rows={3} className="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm outline-none focus:border-blue-500 transition-colors resize-none" />
              </div>
              <div className="flex gap-3 pt-1">
                <button type="button" onClick={() => setBewerkModaal(false)} className="flex-1 border border-gray-200 text-gray-500 text-sm py-2.5 rounded-lg hover:bg-gray-50 transition-colors">Annuleren</button>
                <button type="submit" disabled={opslaan} className="flex-1 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-200 text-white text-sm font-semibold py-2.5 rounded-lg transition-colors">{opslaan ? "Opslaan..." : "Opslaan"}</button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{`
        @keyframes fadeUp {
          from { opacity: 0; transform: translateY(12px); }
          to { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  );
}
