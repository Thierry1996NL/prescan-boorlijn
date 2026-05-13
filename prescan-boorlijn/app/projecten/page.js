"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import { getProjecten, maakProject, logout } from "@/lib/supabase-queries";

export default function ProjectenPagina() {
  const router = useRouter();
  const [projecten, setProjecten] = useState([]);
  const [laden, setLaden] = useState(true);
  const [modaalOpen, setModaalOpen] = useState(false);
  const [nieuw, setNieuw] = useState({
    naam: "",
    opdrachtgever: "",
    locatie: "",
    boorlengte_m: "",
    diameter_mm: "",
    materiaal: "PE100",
    bodemtype: "",
    bijzonderheden: "",
  });
  const [opslaan, setOpslaan] = useState(false);

  useEffect(() => {
    laadProjecten();
  }, []);

  async function laadProjecten() {
    try {
      const data = await getProjecten();
      setProjecten(data);
    } catch (err) {
      console.error(err);
    } finally {
      setLaden(false);
    }
  }

  async function handleMaakProject(e) {
    e.preventDefault();
    setOpslaan(true);
    try {
      const project = await maakProject({
        ...nieuw,
        boorlengte_m: nieuw.boorlengte_m ? Number(nieuw.boorlengte_m) : null,
        diameter_mm: nieuw.diameter_mm ? Number(nieuw.diameter_mm) : null,
      });
      setModaalOpen(false);
      router.push(`/project/${project.id}`);
    } catch (err) {
      console.error(err);
    } finally {
      setOpslaan(false);
    }
  }

  const statusKleur = {
    actief: "bg-green-500/10 text-green-400 border-green-500/20",
    afgerond: "bg-blue-500/10 text-blue-400 border-blue-500/20",
    "on-hold": "bg-yellow-500/10 text-yellow-400 border-yellow-500/20",
  };

  return (
    <div className="min-h-screen bg-[#0f1117]">
      {/* Nav */}
      <nav className="border-b border-[#1e2433] bg-[#141824] px-6 py-3 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="font-bold text-white">Prescan</span>
          <span className="font-bold text-[#1B6EF3]">AI</span>
        </div>
        <button
          onClick={async () => { await logout(); router.push("/login"); }}
          className="text-xs text-[#5a6278] hover:text-white transition-colors"
        >
          Uitloggen
        </button>
      </nav>

      <div className="max-w-4xl mx-auto px-6 py-8">
        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-xl font-bold text-white">Projecten</h1>
            <p className="text-sm text-[#5a6278] mt-0.5">
              {projecten.length} project{projecten.length !== 1 ? "en" : ""}
            </p>
          </div>
          <button
            onClick={() => setModaalOpen(true)}
            className="bg-[#1B6EF3] hover:bg-[#1558d4] text-white text-sm font-semibold px-4 py-2 rounded-lg transition-colors"
          >
            + Nieuw project
          </button>
        </div>

        {/* Projectenlijst */}
        {laden ? (
          <div className="text-sm text-[#5a6278]">Laden...</div>
        ) : projecten.length === 0 ? (
          <div className="text-center py-16 border border-dashed border-[#1e2433] rounded-xl">
            <p className="text-[#5a6278] text-sm">Nog geen projecten.</p>
            <button
              onClick={() => setModaalOpen(true)}
              className="mt-3 text-[#1B6EF3] text-sm hover:underline"
            >
              Maak je eerste project aan
            </button>
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            {projecten.map((p) => (
              <div
                key={p.id}
                onClick={() => router.push(`/project/${p.id}`)}
                className="bg-[#141824] border border-[#1e2433] rounded-xl p-4 cursor-pointer hover:border-[#1B6EF3]/50 transition-colors"
              >
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <div className="font-semibold text-white text-sm">{p.naam}</div>
                    <div className="text-xs text-[#5a6278] mt-0.5">
                      {p.opdrachtgever && <span>{p.opdrachtgever} · </span>}
                      {p.locatie && <span>{p.locatie}</span>}
                    </div>
                  </div>
                  <div className="flex items-center gap-2 flex-shrink-0">
                    {p.boorlengte_m && (
                      <span className="text-xs text-[#5a6278]">{p.boorlengte_m} m</span>
                    )}
                    {p.diameter_mm && (
                      <span className="text-xs text-[#5a6278]">Ø{p.diameter_mm} mm</span>
                    )}
                    <span className={`text-xs border px-2 py-0.5 rounded-full ${statusKleur[p.status]}`}>
                      {p.status}
                    </span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Modaal nieuw project */}
      {modaalOpen && (
        <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
          <div className="bg-[#141824] border border-[#1e2433] rounded-xl w-full max-w-lg max-h-[90vh] overflow-y-auto">
            <div className="p-5 border-b border-[#1e2433] flex items-center justify-between">
              <h2 className="font-semibold text-white text-sm">Nieuw project</h2>
              <button
                onClick={() => setModaalOpen(false)}
                className="text-[#5a6278] hover:text-white text-lg"
              >
                ✕
              </button>
            </div>

            <form onSubmit={handleMaakProject} className="p-5 flex flex-col gap-4">
              {[
                { label: "Projectnaam *", key: "naam", required: true, placeholder: "Boring Parallelweg Noord" },
                { label: "Opdrachtgever", key: "opdrachtgever", placeholder: "Stedin" },
                { label: "Locatie", key: "locatie", placeholder: "Alphen aan den Rijn" },
                { label: "Boorlengte (m)", key: "boorlengte_m", placeholder: "120", type: "number" },
                { label: "Diameter (mm)", key: "diameter_mm", placeholder: "315", type: "number" },
              ].map(({ label, key, required, placeholder, type }) => (
                <div key={key}>
                  <label className="block text-xs text-[#5a6278] mb-1.5 font-medium">{label}</label>
                  <input
                    type={type || "text"}
                    value={nieuw[key]}
                    onChange={(e) => setNieuw((prev) => ({ ...prev, [key]: e.target.value }))}
                    required={required}
                    placeholder={placeholder}
                    className="w-full bg-[#0f1117] border border-[#1e2433] rounded-lg px-3 py-2.5 text-sm text-white placeholder-[#3d4558] outline-none focus:border-[#1B6EF3] transition-colors"
                  />
                </div>
              ))}

              <div>
                <label className="block text-xs text-[#5a6278] mb-1.5 font-medium">Buismateriaal</label>
                <select
                  value={nieuw.materiaal}
                  onChange={(e) => setNieuw((prev) => ({ ...prev, materiaal: e.target.value }))}
                  className="w-full bg-[#0f1117] border border-[#1e2433] rounded-lg px-3 py-2.5 text-sm text-white outline-none focus:border-[#1B6EF3] transition-colors"
                >
                  {["PE100", "PE80", "Staal", "GVK", "PVC", "Anders"].map((m) => (
                    <option key={m} value={m}>{m}</option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-xs text-[#5a6278] mb-1.5 font-medium">Bodemtype</label>
                <input
                  type="text"
                  value={nieuw.bodemtype}
                  onChange={(e) => setNieuw((prev) => ({ ...prev, bodemtype: e.target.value }))}
                  placeholder="klei, zand, veen, grind, gemengd"
                  className="w-full bg-[#0f1117] border border-[#1e2433] rounded-lg px-3 py-2.5 text-sm text-white placeholder-[#3d4558] outline-none focus:border-[#1B6EF3] transition-colors"
                />
              </div>

              <div>
                <label className="block text-xs text-[#5a6278] mb-1.5 font-medium">Bijzonderheden</label>
                <textarea
                  value={nieuw.bijzonderheden}
                  onChange={(e) => setNieuw((prev) => ({ ...prev, bijzonderheden: e.target.value }))}
                  placeholder="Kruising met watergang, nabij bebouwing, etc."
                  rows={3}
                  className="w-full bg-[#0f1117] border border-[#1e2433] rounded-lg px-3 py-2.5 text-sm text-white placeholder-[#3d4558] outline-none focus:border-[#1B6EF3] transition-colors resize-none"
                />
              </div>

              <div className="flex gap-3 pt-1">
                <button
                  type="button"
                  onClick={() => setModaalOpen(false)}
                  className="flex-1 bg-[#0f1117] border border-[#1e2433] text-[#5a6278] text-sm py-2.5 rounded-lg hover:text-white transition-colors"
                >
                  Annuleren
                </button>
                <button
                  type="submit"
                  disabled={opslaan}
                  className="flex-1 bg-[#1B6EF3] hover:bg-[#1558d4] disabled:bg-[#1e2433] disabled:text-[#3d4558] text-white text-sm font-semibold py-2.5 rounded-lg transition-colors"
                >
                  {opslaan ? "Aanmaken..." : "Project aanmaken"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
