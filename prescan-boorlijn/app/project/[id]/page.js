"use client";

import { useState, useEffect } from "react";
import { useParams, useRouter } from "next/navigation";
import { getProjectMetContext } from "@/lib/supabase-queries";
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
    const iconen = { rood: "●", oranje: "●", groen: "●" };
    return (
      <span className={`text-xs border px-2 py-0.5 rounded-full ${stijlen[risico] || ""}`}>
        {iconen[risico]} {risico}
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
    { id: "chat", label: "AI Assistent" },
    { id: "kruisingen", label: `Kruisingen (${project.kruisingen?.length ?? 0})` },
    { id: "details", label: "Projectdetails" },
  ];

  return (
    <div className="min-h-screen bg-[#0f1117] flex flex-col">
      {/* Nav */}
      <nav className="border-b border-[#1e2433] bg-[#141824] px-6 py-3 flex items-center gap-4">
        <button
          onClick={() => router.push("/projecten")}
          className="text-[#5a6278] hover:text-white text-sm transition-colors"
        >
          ← Projecten
        </button>
        <span className="text-[#1e2433]">/</span>
        <span className="text-white text-sm font-semibold">{project.naam}</span>
        <span className="text-xs text-[#5a6278] ml-auto">
          {project.opdrachtgever} · {project.locatie}
        </span>
      </nav>

      {/* Tabs */}
      <div className="border-b border-[#1e2433] bg-[#141824] px-6 flex gap-1">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActieveTab(tab.id)}
            className={`text-sm px-4 py-3 border-b-2 transition-colors ${
              actieveTab === tab.id
                ? "border-[#1B6EF3] text-white font-medium"
                : "border-transparent text-[#5a6278] hover:text-white"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Inhoud */}
      <div className="flex-1 p-6 max-w-5xl mx-auto w-full">

        {/* Chat tab */}
        {actieveTab === "chat" && (
          <div className="h-[calc(100vh-160px)]">
            <PrescanChat projectId={id} projectNaam={project.naam} />
          </div>
        )}

        {/* Kruisingen tab */}
        {actieveTab === "kruisingen" && (
          <div>
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-base font-semibold text-white">Gedetecteerde kruisingen</h2>
            </div>

            {project.kruisingen?.length === 0 ? (
              <div className="text-center py-12 border border-dashed border-[#1e2433] rounded-xl">
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
                        <span className="text-xs text-[#5a6278]">
                          {k.afstand_cm} cm afstand
                        </span>
                        {k.diepte_m && (
                          <span className="text-xs text-[#5a6278]">{k.diepte_m} m diep</span>
                        )}
                        {k.kruising_positie_m && (
                          <span className="text-xs text-[#5a6278]">pos. {k.kruising_positie_m} m</span>
                        )}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {/* Details tab */}
        {actieveTab === "details" && (
          <div className="max-w-lg">
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
                  <div
                    key={i}
                    className={`flex items-start justify-between gap-4 px-4 py-3 ${
                      i !== 0 ? "border-t border-[#1e2433]" : ""
                    }`}
                  >
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
    </div>
  );
}
