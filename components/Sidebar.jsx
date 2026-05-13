"use client";

import { useState } from "react";
import { useRouter, usePathname } from "next/navigation";
import { logout } from "@/lib/supabase-queries";

export default function Sidebar({ projecten = [], actiefProjectId = null }) {
  const router = useRouter();
  const pathname = usePathname();
  const [ingeklapt, setIngeklapt] = useState(false);

  async function handleLogout() {
    await logout();
    router.push("/login");
  }

  const breedte = ingeklapt ? "w-14" : "w-56";

  return (
    <aside
      className={`${breedte} flex-shrink-0 bg-white border-r border-gray-200 flex flex-col transition-all duration-300 ease-in-out overflow-hidden`}
      style={{ minHeight: "100vh" }}
    >
      {/* Logo + toggle */}
      <div className="flex items-center justify-between px-4 py-4 border-b border-gray-100">
        {!ingeklapt && (
          <div className="flex items-center gap-1 overflow-hidden">
            <span className="font-bold text-gray-900 text-sm whitespace-nowrap">Prescan</span>
            <span className="font-bold text-blue-600 text-sm">AI</span>
          </div>
        )}
        <button
          onClick={() => setIngeklapt(!ingeklapt)}
          className="p-1.5 rounded-lg hover:bg-gray-100 text-gray-400 hover:text-gray-700 transition-colors flex-shrink-0"
          title={ingeklapt ? "Uitklappen" : "Inklappen"}
        >
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            {ingeklapt ? (
              <path d="M6 4l4 4-4 4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
            ) : (
              <path d="M10 4L6 8l4 4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
            )}
          </svg>
        </button>
      </div>

      {/* Projecten sectie */}
      <div className="flex-1 overflow-y-auto py-3">
        {/* Alle projecten link */}
        <div className="px-3 mb-1">
          <button
            onClick={() => router.push("/projecten")}
            className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors w-full text-left ${
              pathname === "/projecten"
                ? "bg-blue-50 text-blue-700 font-medium"
                : "text-gray-500 hover:text-gray-900 hover:bg-gray-100"
            }`}
          >
            <span className="flex-shrink-0 text-base">🏗</span>
            {!ingeklapt && <span className="truncate">Alle projecten</span>}
          </button>
        </div>

        {/* Projectenlijst */}
        {!ingeklapt && projecten.length > 0 && (
          <div className="px-3 mt-3">
            <div className="text-xs text-gray-400 font-medium px-3 mb-1 uppercase tracking-wide">Projecten</div>
            {projecten.map((p) => (
              <button
                key={p.id}
                onClick={() => router.push(`/project/${p.id}`)}
                className={`flex items-center gap-3 px-3 py-2 rounded-lg text-sm transition-colors w-full text-left group ${
                  actiefProjectId === p.id
                    ? "bg-blue-50 text-blue-700 font-medium"
                    : "text-gray-500 hover:text-gray-900 hover:bg-gray-100"
                }`}
              >
                <span className="w-1.5 h-1.5 rounded-full bg-current flex-shrink-0 opacity-50" />
                <span className="truncate">{p.naam}</span>
              </button>
            ))}
          </div>
        )}
      </div>

      {/* Uitloggen */}
      <div className="px-3 py-3 border-t border-gray-100">
        <button
          onClick={handleLogout}
          className="flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm text-gray-400 hover:text-gray-700 hover:bg-gray-100 transition-colors w-full text-left"
        >
          <span className="flex-shrink-0 text-base">↩</span>
          {!ingeklapt && <span>Uitloggen</span>}
        </button>
      </div>
    </aside>
  );
}
