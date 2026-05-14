"use client";

import { useState, useEffect } from "react";
import { useRouter, usePathname } from "next/navigation";
import { getProjecten, logout } from "@/lib/supabase-queries";

const STAPPEN = [
  { nr: 1,  label: "Projectinformatie",  sub: "Basisgegevens",            leeg: false },
  { nr: 2,  label: "Ontwerp inladen",    sub: "LS · MS · Gas · Water",    leeg: false },
  { nr: 3,  label: "Ontwerp bekijken",   sub: "Lagen & instellingen",     leeg: false },
  { nr: 4,  label: "Boorlijn tekenen",   sub: "Tracé op de kaart",        leeg: false },
  { nr: 5,  label: "Oppervlakteanalyse", sub: "BGT oppervlakken",         leeg: false },
  { nr: 6,  label: "Diepteligging",      sub: "Dwarsprofiel & bodem",     leeg: false },
  { nr: 7,  label: "Machine locatie",    sub: "Boormachine & bentoniet",  leeg: false },
  { nr: 8,  label: "Eindontwerp",        sub: "Overzicht read-only",      leeg: false },
  { nr: 9,  label: "AI optimalisatie",   sub: "Kruisingen & advies",      leeg: true  },
  { nr: 10, label: "Pre-scan",           sub: "Tekeningen",               leeg: true  },
  { nr: 11, label: "Contactpersonen",    sub: "KLIC contacten",           leeg: true  },
  { nr: 12, label: "Exporteren",         sub: "Rapport & coördinaten",    leeg: true  },
];

export default function Sidebar({
  actiefProjectId = null,
  actieveStap     = 1,
  onStapWijzigen  = null,
  project         = null,
  gebruiker       = null,
}) {
  const router   = useRouter();
  const pathname = usePathname();
  const [ingeklapt, setIngeklapt] = useState(false);
  const [projecten, setProjecten] = useState([]);

  const inProject = !!actiefProjectId && !!onStapWijzigen;

  useEffect(() => {
    if (!inProject) laadProjecten();
  }, [inProject]);

  async function laadProjecten() {
    try {
      const data = await getProjecten();
      setProjecten(data || []);
    } catch (err) {
      console.error("Sidebar:", err);
    }
  }

  async function handleLogout() {
    await logout();
    router.push("/login");
  }

  const initialen = (() => {
    if (gebruiker?.naam)
      return gebruiker.naam.split(" ").map(w => w[0]).join("").slice(0, 2).toUpperCase();
    return (gebruiker?.email?.[0] ?? "U").toUpperCase();
  })();

  return (
    <aside
      className={`${ingeklapt ? "w-16" : "w-64"
        } flex-shrink-0 h-screen bg-white border-r border-gray-100 flex flex-col transition-all duration-300 sticky top-0 overflow-y-auto z-30`}
    >
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-4 border-b border-gray-100 flex-shrink-0">
        {!ingeklapt && (
          <span className="font-semibold text-sm text-gray-900 select-none">
            Prescan<span className="text-orange-500">AI</span>
          </span>
        )}
        <div className={`flex items-center gap-1 ${ingeklapt ? "w-full justify-center" : "ml-auto"}`}>
          {!ingeklapt && (
            <button
              onClick={() => router.push("/instellingen")}
              title="Instellingen"
              className="w-8 h-8 flex items-center justify-center rounded-lg hover:bg-gray-100 text-gray-400 hover:text-gray-600 transition-colors"
            >
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="3"/>
                <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>
              </svg>
            </button>
          )}
          <button
            onClick={() => setIngeklapt(v => !v)}
            className="w-8 h-8 flex items-center justify-center rounded-lg hover:bg-gray-100 text-gray-400 text-xs transition-colors"
          >
            {ingeklapt ? "▶" : "◀"}
          </button>
        </div>
      </div>

      {/* Gebruiker – gele cirkel */}
      <div className={`flex items-center gap-3 px-4 py-3 border-b border-gray-100 flex-shrink-0 ${ingeklapt ? "justify-center" : ""}`}>
        <div className="w-8 h-8 rounded-full bg-yellow-400 flex items-center justify-center text-xs font-bold text-yellow-900 flex-shrink-0 shadow-sm select-none">
          {initialen}
        </div>
        {!ingeklapt && (
          <div className="min-w-0">
            <div className="text-xs font-medium text-gray-800 truncate">
              {gebruiker?.naam || gebruiker?.email || "Gebruiker"}
            </div>
            {gebruiker?.naam && (
              <div className="text-xs text-gray-400 truncate">{gebruiker.email}</div>
            )}
          </div>
        )}
      </div>

      {/* Navigatie */}
      <div className="flex-1 py-3 overflow-y-auto">
        {inProject ? (
          <>
            <button
              onClick={() => router.push("/projecten")}
              className={`flex items-center gap-2 w-full px-4 py-2 text-xs text-gray-400 hover:text-gray-600 hover:bg-gray-50 transition-colors mb-2 ${ingeklapt ? "justify-center" : ""}`}
            >
              <span>←</span>
              {!ingeklapt && <span>Alle projecten</span>}
            </button>

            {!ingeklapt && project && (
              <div className="mx-3 mb-3 px-3 py-2 bg-orange-50 rounded-lg border border-orange-100">
                <div className="text-xs font-semibold text-orange-800 truncate">{project.naam}</div>
                {project.locatie && (
                  <div className="text-xs text-orange-400 truncate mt-0.5">{project.locatie}</div>
                )}
              </div>
            )}

            <div className="px-2 space-y-0.5">
              {STAPPEN.map(stap => {
                const actief   = actieveStap === stap.nr;
                const voltooid = !stap.leeg && actieveStap > stap.nr;
                return (
                  <button
                    key={stap.nr}
                    onClick={() => !stap.leeg && onStapWijzigen(stap.nr)}
                    disabled={stap.leeg}
                    title={ingeklapt ? `${stap.nr}. ${stap.label}` : undefined}
                    className={`flex items-center gap-3 w-full rounded-lg text-left transition-all duration-150 ${
                      ingeklapt ? "px-0 py-2 justify-center" : "px-2 py-2"
                    } ${actief ? "bg-orange-50" : stap.leeg ? "opacity-40 cursor-not-allowed" : "hover:bg-gray-50"}`}
                  >
                    <div className={`w-6 h-6 rounded-full flex-shrink-0 flex items-center justify-center text-xs font-bold transition-colors ${
                      actief ? "bg-orange-500 text-white" : voltooid ? "bg-green-100 text-green-600" : "bg-gray-100 text-gray-500"
                    }`}>
                      {voltooid ? "✓" : stap.nr}
                    </div>
                    {!ingeklapt && (
                      <div className="min-w-0 flex-1">
                        <div className={`text-xs font-medium truncate ${actief ? "text-orange-700" : "text-gray-700"}`}>
                          {stap.label}
                        </div>
                        <div className="text-xs text-gray-400 truncate">{stap.sub}</div>
                      </div>
                    )}
                    {!ingeklapt && stap.leeg && (
                      <span className="text-xs text-gray-300 italic flex-shrink-0">binnenkort</span>
                    )}
                  </button>
                );
              })}
            </div>
          </>
        ) : (
          <>
            {!ingeklapt && (
              <div className="px-4 mb-2">
                <span className="text-xs font-semibold text-gray-400 uppercase tracking-wide">Projecten</span>
              </div>
            )}
            <div className="px-2 space-y-0.5">
              {projecten.map(p => {
                const actief = pathname === `/project/${p.id}`;
                return (
                  <button
                    key={p.id}
                    onClick={() => router.push(`/project/${p.id}`)}
                    title={ingeklapt ? p.naam : undefined}
                    className={`flex items-center gap-2 w-full px-3 py-2 rounded-lg text-left text-sm transition-colors ${
                      actief ? "bg-orange-50 text-orange-700 font-medium" : "text-gray-600 hover:bg-gray-50"
                    } ${ingeklapt ? "justify-center" : ""}`}
                  >
                    <span className="w-1.5 h-1.5 rounded-full bg-current flex-shrink-0 opacity-60" />
                    {!ingeklapt && <span className="truncate">{p.naam}</span>}
                  </button>
                );
              })}
              {projecten.length === 0 && !ingeklapt && (
                <div className="px-3 py-6 text-xs text-gray-400 text-center">Nog geen projecten</div>
              )}
            </div>
          </>
        )}
      </div>

      {/* Uitloggen */}
      <div className="px-3 py-3 border-t border-gray-100 flex-shrink-0">
        <button
          onClick={handleLogout}
          title={ingeklapt ? "Uitloggen" : undefined}
          className={`flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-gray-400 hover:text-gray-700 hover:bg-gray-100 transition-colors w-full ${ingeklapt ? "justify-center" : "text-left"}`}
        >
          <span>↩</span>
          {!ingeklapt && <span>Uitloggen</span>}
        </button>
      </div>
    </aside>
  );
}
