"use client";

import { useState, useEffect } from "react";
import { useRouter, usePathname } from "next/navigation";
import { getProjecten, logout } from "@/lib/supabase-queries";

const STAPPEN = [
  { nr: 1,  label: "Projectinformatie",    sub: "Basisgegevens"           },
  { nr: 2,  label: "Ontwerp inladen",      sub: "LS · MS · Gas · Water"   },
  { nr: 3,  label: "Ontwerp bekijken",     sub: "Lagen & instellingen"    },
  { nr: 4,  label: "Boorlijn tekenen",     sub: "Tracé op de kaart"       },
  { nr: 5,  label: "Oppervlakteanalyse",   sub: "BGT verharding"          },
  { nr: 6,  label: "Ondergrondanalyse",    sub: "DINO Loket · BRO"        },
  { nr: 7,  label: "Dwarsprofiel",         sub: "Dwarsprofiel & bodem"    },
  { nr: 8,  label: "Machine locatie",      sub: "Boormachine & bentoniet" },
  { nr: 9,  label: "3D ontwerp",           sub: "CesiumJS visualisatie"   },
  { nr: 10, label: "Eindrapport & Export", sub: "Overzicht & exports"     },
];

const SUB_STAPPEN_6 = [
  { id: "5.2", label: "BRO DGM",           subtitel: "3D Bodemopbouw"  },
  { id: "5.3", label: "REGIS II",          subtitel: "Hydrogeologie"   },
  { id: "5.8", label: "Geomorfologie",     subtitel: "BRO GMM kaart"   },
  { id: "5.4", label: "Bodemkaart",        subtitel: "1:50.000"        },
  { id: "5.5", label: "Grondwaterspiegel", subtitel: "BRO Peilbuizen"  },
  { id: "5.6", label: "AHN",               subtitel: "Hoogtemodel"     },
];

function BorevexaIcon({ size = 26 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 44 44" fill="none" style={{ flexShrink: 0 }}>
      <rect width="44" height="44" rx="9" fill="#0D1520"/>
      <line x1="4" y1="17" x2="40" y2="17" stroke="white" strokeWidth="1" opacity=".35"/>
      <line x1="9" y1="17" x2="5" y2="21" stroke="white" strokeWidth=".8" opacity=".2"/>
      <line x1="16" y1="17" x2="12" y2="21" stroke="white" strokeWidth=".8" opacity=".2"/>
      <line x1="23" y1="17" x2="19" y2="21" stroke="white" strokeWidth=".8" opacity=".2"/>
      <line x1="30" y1="17" x2="26" y2="21" stroke="white" strokeWidth=".8" opacity=".2"/>
      <line x1="37" y1="17" x2="33" y2="21" stroke="white" strokeWidth=".8" opacity=".2"/>
      <line x1="9" y1="9" x2="9" y2="17" stroke="white" strokeWidth="1" strokeDasharray="2 1.5" opacity=".25"/>
      <circle cx="9" cy="17" r="2.2" fill="#00F5B4"/>
      <path d="M9 17 C18 34 28 36 31 36" stroke="white" strokeWidth="2.6" fill="none" strokeDasharray="8 3.5" pathLength="100" strokeLinecap="round"/>
      <rect x="29.5" y="33.8" width="5" height="4.4" rx="1.2" fill="#7FFBDB"/>
      <polygon points="34.5,33.8 43,36 34.5,36" fill="#7FFBDB"/>
      <polygon points="34.5,38.2 43,36 34.5,36" fill="#7FFBDB"/>
      <polygon points="38,34.8 43,36 38,37.2" fill="#00F5B4"/>
    </svg>
  );
}

export default function Sidebar({
  actiefProjectId   = null,
  actieveStap       = 1,
  onStapWijzigen    = null,
  actieveSubStap    = "5.2",
  onSubStapWijzigen = null,
  project           = null,
  gebruiker         = null,
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
      className={`${ingeklapt ? "w-[60px]" : "w-[248px]"
        } flex-shrink-0 h-screen bg-white border-r border-[#DEE6EA] flex flex-col transition-all duration-250 sticky top-0 overflow-y-auto z-30`}
    >
      {/* ── Header ── */}
      <div className="flex items-center justify-between px-4 py-3.5 border-b border-[#DEE6EA] flex-shrink-0 h-[54px]">
        {!ingeklapt && (
          <div className="flex items-center gap-2.5 min-w-0">
            <BorevexaIcon size={26} />
            <span className="font-bold text-[15px] tracking-tight text-[#0D1520] select-none">
              Bore<span className="text-[#007A5A]">vexa</span>
            </span>
          </div>
        )}
        {ingeklapt && <BorevexaIcon size={26} />}
        <div className={`flex items-center gap-1 ${ingeklapt ? "hidden" : ""}`}>
          <button
            onClick={() => router.push("/instellingen")}
            title="Instellingen"
            className="w-7 h-7 flex items-center justify-center rounded-lg hover:bg-[#F5F7F9] text-[#8FA6B2] hover:text-[#587080] transition-colors"
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="3"/>
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>
            </svg>
          </button>
          <button
            onClick={() => setIngeklapt(v => !v)}
            className="w-7 h-7 flex items-center justify-center rounded-lg hover:bg-[#F5F7F9] text-[#8FA6B2] hover:text-[#587080] transition-colors text-xs"
          >
            ◀
          </button>
        </div>
        {ingeklapt && (
          <button
            onClick={() => setIngeklapt(false)}
            className="absolute left-full top-3.5 w-5 h-5 flex items-center justify-center bg-white border border-[#DEE6EA] rounded-r-md text-[#8FA6B2] hover:text-[#587080] text-xs shadow-sm"
          >
            ▶
          </button>
        )}
      </div>

      {/* ── Gebruiker ── */}
      <div className={`flex items-center gap-2.5 px-4 py-3 border-b border-[#DEE6EA] flex-shrink-0 ${ingeklapt ? "justify-center" : ""}`}>
        <div className="w-7 h-7 rounded-full bg-[#E5F3EC] flex items-center justify-center text-xs font-bold text-[#007A5A] flex-shrink-0 select-none">
          {initialen}
        </div>
        {!ingeklapt && (
          <div className="min-w-0">
            <div className="text-xs font-medium text-[#1B2B35] truncate">
              {gebruiker?.naam || gebruiker?.email || "Gebruiker"}
            </div>
            {gebruiker?.naam && (
              <div className="text-xs text-[#8FA6B2] truncate">{gebruiker.email}</div>
            )}
          </div>
        )}
      </div>

      {/* ── Navigatie ── */}
      <div className="flex-1 py-2.5 overflow-y-auto">
        {inProject ? (
          <>
            {/* Terug */}
            <button
              onClick={() => router.push("/projecten")}
              className={`flex items-center gap-2 w-full px-4 py-2 text-xs text-[#8FA6B2] hover:text-[#587080] hover:bg-[#F5F7F9] transition-colors mb-2 ${ingeklapt ? "justify-center" : ""}`}
            >
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M19 12H5M12 5l-7 7 7 7"/></svg>
              {!ingeklapt && <span>Alle projecten</span>}
            </button>

            {/* Project badge */}
            {!ingeklapt && project && (
              <div className="mx-3 mb-2.5 px-3 py-2 bg-[#F5F7F9] rounded-lg border border-[#DEE6EA]">
                <div className="text-xs font-semibold text-[#1B2B35] truncate">{project.naam}</div>
                {project.locatie && (
                  <div className="text-xs text-[#8FA6B2] truncate mt-0.5">{project.locatie}</div>
                )}
              </div>
            )}

            {/* Stappen */}
            <div className="px-2 space-y-px">
              {STAPPEN.map(stap => {
                const actief   = actieveStap === stap.nr;
                const voltooid = actieveStap > stap.nr;
                const toontSub = actief && stap.nr === 6 && !ingeklapt;

                return (
                  <div key={stap.nr}>
                    <button
                      onClick={() => onStapWijzigen(stap.nr)}
                      title={ingeklapt ? `${stap.nr}. ${stap.label}` : undefined}
                      className={`flex items-center gap-2.5 w-full rounded-lg text-left transition-colors duration-150 ${
                        ingeklapt ? "px-0 py-2 justify-center" : "px-2.5 py-2"
                      } ${actief
                          ? "bg-[#E5F3EC]"
                          : "hover:bg-[#F5F7F9]"
                      }`}
                    >
                      {/* Stapnummer cirkel */}
                      <div className={`w-5 h-5 rounded-full flex-shrink-0 flex items-center justify-center text-[10px] font-bold transition-colors ${
                        actief
                          ? "bg-[#007A5A] text-white"
                          : voltooid
                          ? "bg-[#E5F3EC] text-[#007A5A]"
                          : "bg-[#EEF2F4] text-[#8FA6B2]"
                      }`}>
                        {voltooid ? "✓" : stap.nr}
                      </div>

                      {!ingeklapt && (
                        <div className="min-w-0 flex-1">
                          <div className={`text-xs font-medium truncate ${
                            actief ? "text-[#007A5A]" : "text-[#1B2B35]"
                          }`}>
                            {stap.label}
                          </div>
                          <div className="text-[11px] text-[#8FA6B2] truncate">{stap.sub}</div>
                        </div>
                      )}
                    </button>

                    {/* Sub-stappen stap 6 */}
                    {toontSub && (
                      <div className="ml-7 mt-0.5 mb-1 space-y-px border-l-2 border-[#E5F3EC] pl-3">
                        {SUB_STAPPEN_6.map(sub => {
                          const subActief = actieveSubStap === sub.id;
                          return (
                            <button
                              key={sub.id}
                              onClick={() => onSubStapWijzigen?.(sub.id)}
                              className={`flex items-center gap-2 w-full text-left rounded-lg px-2 py-1.5 transition-colors ${
                                subActief
                                  ? "bg-[#E5F3EC] text-[#007A5A]"
                                  : "text-[#587080] hover:bg-[#F5F7F9] hover:text-[#1B2B35]"
                              }`}
                            >
                              <div className="min-w-0">
                                <div className={`text-[11px] font-medium ${subActief ? "text-[#007A5A]" : "text-[#587080]"}`}>
                                  {sub.id} {sub.label}
                                </div>
                                <div className="text-[10px] text-[#8FA6B2]">{sub.subtitel}</div>
                              </div>
                              {subActief && (
                                <div className="ml-auto w-1.5 h-1.5 rounded-full bg-[#007A5A] flex-shrink-0"/>
                              )}
                            </button>
                          );
                        })}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </>
        ) : (
          <>
            {!ingeklapt && (
              <div className="px-4 mb-2">
                <span className="text-[10.5px] font-semibold text-[#8FA6B2] uppercase tracking-wide">Projecten</span>
              </div>
            )}
            <div className="px-2 space-y-px">
              {projecten.map(p => {
                const actief = pathname === `/project/${p.id}`;
                return (
                  <button
                    key={p.id}
                    onClick={() => router.push(`/project/${p.id}`)}
                    title={ingeklapt ? p.naam : undefined}
                    className={`flex items-center gap-2 w-full px-3 py-2 rounded-lg text-left text-xs transition-colors ${
                      actief
                        ? "bg-[#E5F3EC] text-[#007A5A] font-semibold"
                        : "text-[#587080] hover:bg-[#F5F7F9] hover:text-[#1B2B35]"
                    } ${ingeklapt ? "justify-center" : ""}`}
                  >
                    <span className={`w-1.5 h-1.5 rounded-full flex-shrink-0 ${actief ? "bg-[#007A5A]" : "bg-[#DEE6EA]"}`} />
                    {!ingeklapt && <span className="truncate">{p.naam}</span>}
                  </button>
                );
              })}
              {projecten.length === 0 && !ingeklapt && (
                <div className="px-3 py-6 text-xs text-[#8FA6B2] text-center">Nog geen projecten</div>
              )}
            </div>
          </>
        )}
      </div>

      {/* ── Uitloggen ── */}
      <div className="px-3 py-3 border-t border-[#DEE6EA] flex-shrink-0">
        <button
          onClick={handleLogout}
          title={ingeklapt ? "Uitloggen" : undefined}
          className={`flex items-center gap-2.5 px-3 py-2 rounded-lg text-xs text-[#8FA6B2] hover:text-[#587080] hover:bg-[#F5F7F9] transition-colors w-full ${ingeklapt ? "justify-center" : "text-left"}`}
        >
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/>
          </svg>
          {!ingeklapt && <span>Uitloggen</span>}
        </button>
      </div>
    </aside>
  );
}
