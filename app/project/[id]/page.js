"use client";

import { useState, useEffect } from "react";
import { useParams, useRouter } from "next/navigation";
import { getProjectMetContext, logout } from "@/lib/supabase-queries";
import PrescanChat from "@/components/PrescanChat";

export default function ProjectDetailPagina() {
  const { id } = useParams();
  const router = useRouter();
  const [project, setProject] = useState(null);
  const [laden, setLaden] = useState(true);
  const [actieveTab, setActieveTab] = useState("chat");

  useEffect(() => {
    laadProject();
  }, [id]);

  async function laadProject() {
    try {
      const data = await getProjectMetContext(id);
      setProject(data);
    } catch (err) {
      console.error(err);
    } finally {
      setLaden(false);
    }
  }

  const risicoBadge = (risico) => {
    const stijlen = {
      rood: "bg-red-500/10 text-red-400 border-red-500/20",
      oranje: "bg-orange-500/10 text-orange-400 border-orange-500/20",
      groen: "bg-green-500/10 text-green-400 border-green-500/20",
    };
    return (
      <span className={`text-xs border px-2 py-0.5 rounded-full ${stijlen[risico] || ""}`}>
        ● {risico}
      </span>
    );
  };

  if (laden) {
    return (
      <div className="min-h-screen bg-[#0f1117] flex items-center justify-center">
        <p className="text-sm text-[#5a6278]">Laden...</p>
      </div>
    );
  }

  if (!project) {
    return (
      <div className="min-h-screen bg-[#0f1117] flex items-center justify-center">
        <p className="text-sm text-red-400">Project niet gevonden.</p>
      </div>
    );
  }

  const tabs = [
    { id: "chat", label: "AI Assistent", icon: "🛠" },
    { id: "trace", label: "Tracé", icon: "📍" },
    { id: "kruisingen", label: "Kruisingen", icon: "⚡", count: project.kruisingen?.length ?? 0 },
    { id: "details", label: "Projectdetails", icon: "📋" },
  ];

  return (
    <div className="min-h-screen bg-[#0f1117] flex">

      {/* SIDEBAR */}
      <aside className="w-56 flex-shrink-0 bg-[#141824] border-r border-[#1e2433] flex flex-col">

        {/* Logo */}
        <div className="px-4 py-4 border-b border-[#1e2433]">
          <div className="flex items-center gap-2">
            <span className="font-bold text-white text-sm">Prescan</span>
            <span className="font-bold text-[#1B6EF3] text-sm">AI</span>
          </div>
        </div>

        {/* Terug naar projecten */}
        <div className="px-3 py-3 border-b border-[#1e2433]">
          <button
            onClick={() => router.push("/projecten")}
            className="flex items-center gap-2 text-xs text-[#5a6278] hover:text-white transition-colors w-full px-2 py-1.5 rounded-lg hover:bg-[#1e2433]"
          >
            <span>←</span>
            <span>Alle projecten</span>
          </button>
        </div>

        {/* Project naam */}
        <div className="px-4 py-3 border-b border-[#1e2433]">
          <div className="text-xs text-[#5a6278] mb-0.5">Project</div>
          <div className="text-sm font-semibold text-white truncate">{project.naam}</div>
          {project.opdrachtgever && (
            <div className="text-xs text-[#5a6278] mt-0.5 truncate">{project.opdrachtgever}</div>
          )}
        </div>

        {/* Navigatie tabs */}
        <nav className="flex-1 px-3 py-3 flex flex-col gap-1">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActieveTab(tab.id)}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors w-full text-left ${
                actieveTab === tab.id
                  ? "bg-[#1B6EF3]/10 text-white border border-[#1B6EF3]/20"
                  : "text-[#5a6278] hover:text-white hover:bg-[#1e2433]"
              }`}
            >
              <span className="text-base">{tab.icon}</span>
              <span className="flex-1">{tab.label}</span>
              {tab.count !== undefined && tab.count > 0 && (
                <span className="text-xs bg-[#1e2433] text-[#5a6278] px-1.5 py-0.5 rounded-full">
                  {tab.count}
                </span>
              )}
            </button>
          ))}
        </nav>

        {/* Uitloggen */}
        <div className="px-3 py-3 border-t border-[#1e2433]">
          <button
            onClick={async () => { await logout(); router.push("/login"); }}
            className="flex items-center gap-2 text-xs text-[#5a6278] hover:text-white transition-colors w-full px-2 py-1.5 rounded-lg hover:bg-[#1e2433]"
          >
            <span>↩</span>
            <span>Uitloggen</span>
          </button>
        </div>
      </aside>

      {/* HOOFDINHOUD */}
      <main className="flex-1 flex flex-col min-w-0">

        {/* Header */}
        <header className="bg-[#141824] border-b border-[#1e2433] px-6 py-3 flex items-center justify-between flex-shrink-0">
          <h1 className="text-sm font-semibold text-white">
            {tabs.find(t => t.id === actieveTab)?.icon} {tabs.find(t => t.id === actieveTab)?.label}
          </h1>
          <div className="flex items-center gap-3 text-xs text-[#5a6278]">
            {project.locatie && <span>📍 {project.locatie}</span>}
            {project.boorlengte_m && <span>{project.boorlengte_m} m</span>}
            {project.diameter_mm && <span>Ø{project.diameter_mm} mm</span>}
          </div>
        </header>

        {/* Inhoud */}
        <div className="flex-1 overflow-auto">

          {/* AI Assistent */}
          {actieveTab === "chat" && (
            <div className="h-full p-4">
              <div className="h-full max-w-4xl mx-auto">
                <PrescanChat projectId={id} projectNaam={project.naam} />
              </div>
            </div>
          )}

          {/* Tracé */}
          {actieveTab === "trace" && (
            <div className="p-6 max-w-4xl mx-auto">
              <h2 className="text-base font-semibold text-white mb-4">Boortracé</h2>

              {project.boortrace_geojson ? (
                <div className="bg-[#141824] border border-[#1e2433] rounded-xl p-4">
                  <div className="text-xs text-[#5a6278] mb-2">GeoJSON</div>
                  <pre className="text-xs text-[#8892a4] overflow-auto">
                    {JSON.stringify(project.boortrace_geojson, null, 2)}
                  </pre>
                </div>
              ) : (
                <div className="text-center py-16 border border-dashed border-[#1e2433] rounded-xl">
                  <div className="text-3xl mb-3">📍</div>
                  <p className="text-[#5a6278] text-sm font-medium">Nog geen tracé toegevoegd</p>
                  <p className="text-[#3d4558] text-xs mt-1 max-w-xs mx-auto">
                    Upload een GeoJSON of voer coördinaten in om kruisingen te kunnen analyseren.
                  </p>
                  <div className="mt-4 flex gap-2 justify-center">
                    <button className="bg-[#1B6EF3] hover:bg-[#1558d4] text-white text-xs font-semibold px-4 py-2 rounded-lg transition-colors">
                      GeoJSON uploaden
                    </button>
                    <button className="bg-[#1e2433] hover:bg-[#2a3247] text-[#8892a4] text-xs font-semibold px-4 py-2 rounded-lg transition-colors">
                      Coördinaten invoeren
                    </button>
                  </div>
                </div>
              )}

              {/* Tracé statistieken */}
              <div className="mt-4 grid grid-cols-3 gap-3">
                {[
                  { label: "Boorlengte", waarde: project.boorlengte_m ? `${project.boorlengte_m} m` : "—" },
                  { label: "Diameter", waarde: project.diameter_mm ? `Ø${project.diameter_mm} mm` : "—" },
                  { label: "Materiaal", waarde: project.materiaal ?? "—" },
                  { label: "Bodemtype", waarde: project.bodemtype ?? "—" },
                  { label: "Locatie", waarde: project.locatie ?? "—" },
                  { label: "Kruisingen", waarde: project.kruisingen?.length ?? 0 },
                ].map(({ label, waarde }, i) => (
                  <div key={i} className="bg-[#141824] border border-[#1e2433] rounded-xl p-3">
                    <div className="text-xs text-[#5a6278] mb-1">{label}</div>
                    <div className="text-sm font-medium text-white">{waarde}</div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Kruisingen */}
          {actieveTab === "kruisingen" && (
            <div className="p-6 max-w-4xl mx-auto">
              <h2 className="text-base font-semibold text-white mb-4">Gedetecteerde kruisingen</h2>
              {project.kruisingen?.length === 0 ? (
                <div className="text-center py-16 border border-dashed border-[#1e2433] rounded-xl">
                  <div className="text-3xl mb-3">⚡</div>
                  <p className="text-[#5a6278] text-sm">Nog geen kruisingen geregistreerd.</p>
                  <p className="text-[#3d4558] text-xs mt-1">Upload een KLIC-melding om kruisingen te detecteren.</p>
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  {project.kruisingen.map((k) => (
                    <div key={k.id} className="bg-[#141824] border border-[#1e2433] rounded-xl p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div>
                          <div className="font-medium text-white text-sm">{k.leidingtype}</div>
                          <div className="text-xs text-[#5a6278] mt-0.5">{k.netbeheerder}</div>
                          {k.aanbeveling && (
                            <div className="text-xs text-[#8892a4] mt-2 max-w-lg">{k.aanbeveling}</div>
                          )}
                        </div>
                        <div className="flex flex-col items-end gap-1.5 flex-shrink-0">
                          {risicoBadge(k.risico)}
                          <span className="text-xs text-[#5a6278]">{k.afstand_cm} cm afstand</span>
                          {k.diepte_m && <span className="text-xs text-[#5a6278]">{k.diepte_m} m diep</span>}
                          {k.kruising_positie_m && <span className="text-xs text-[#5a6278]">pos. {k.kruising_positie_m} m</span>}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* Details */}
          {actieveTab === "details" && (
            <div className="p-6 max-w-lg">
              <h2 className="text-base font-semibold text-white mb-4">Projectdetails</h2>
              <div className="bg-[#141824] border border-[#1e2433] rounded-xl overflow-hidden">
                {[
                  { label: "Projectnaam", waarde: project.naam },
                  { label: "Opdrachtgever", waarde: project.opdrachtgever },
                  { label: "Locatie", waarde: project.locatie },
                  { label: "Boorlengte", waarde: project.boorlengte_m ? `${project.boorlengte_m} m` : null },
                  { label: "Diameter", waarde: project.diameter_mm ? `Ø${project.diameter_mm} mm` : null },
                  { label: "Materiaal", waarde: project.materiaal },
                  { label: "Bodemtype", waarde: project.bodemtype },
                  { label: "Status", waarde: project.status },
                ].map(({ label, waarde }, i) => (
                  waarde && (
                    <div key={i} className={`flex items-start justify-between gap-4 px-4 py-3 ${i !== 0 ? "border-t border-[#1e2433]" : ""}`}>
                      <span className="text-xs text-[#5a6278] font-medium">{label}</span>
                      <span className="text-xs text-white text-right">{waarde}</span>
                    </div>
                  )
                ))}
                {project.bijzonderheden && (
                  <div className="border-t border-[#1e2433] px-4 py-3">
                    <span className="text-xs text-[#5a6278] font-medium block mb-1">Bijzonderheden</span>
                    <span className="text-xs text-white">{project.bijzonderheden}</span>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
