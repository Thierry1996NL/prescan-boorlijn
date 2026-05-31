"use client";

import { useState } from "react";
import { supabase } from "@/lib/supabase-queries";
import { useRouter } from "next/navigation";

function BorevexaIcon({ size = 36 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 44 44" fill="none">
      <rect width="44" height="44" rx="9" fill="#0D1520"/>
      <line x1="4" y1="17" x2="40" y2="17" stroke="white" strokeWidth="1" opacity=".35"/>
      <line x1="9" y1="17" x2="5" y2="21" stroke="white" strokeWidth=".8" opacity=".22"/>
      <line x1="16" y1="17" x2="12" y2="21" stroke="white" strokeWidth=".8" opacity=".22"/>
      <line x1="23" y1="17" x2="19" y2="21" stroke="white" strokeWidth=".8" opacity=".22"/>
      <line x1="30" y1="17" x2="26" y2="21" stroke="white" strokeWidth=".8" opacity=".22"/>
      <line x1="37" y1="17" x2="33" y2="21" stroke="white" strokeWidth=".8" opacity=".22"/>
      <line x1="9" y1="9" x2="9" y2="17" stroke="white" strokeWidth="1" strokeDasharray="2 1.5" opacity=".28"/>
      <circle cx="9" cy="17" r="2.2" fill="#00F5B4"/>
      <path d="M9 17 C18 34 28 36 31 36" stroke="white" strokeWidth="2.6" fill="none" strokeDasharray="8 3.5" pathLength="100" strokeLinecap="round"/>
      <rect x="29.5" y="33.8" width="5" height="4.4" rx="1.2" fill="#7FFBDB"/>
      <polygon points="34.5,33.8 43,36 34.5,36" fill="#7FFBDB"/>
      <polygon points="34.5,38.2 43,36 34.5,36" fill="#7FFBDB"/>
      <polygon points="38,34.8 43,36 38,37.2" fill="#00F5B4"/>
    </svg>
  );
}

export default function LoginPagina() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [wachtwoord, setWachtwoord] = useState("");
  const [laden, setLaden] = useState(false);
  const [fout, setFout] = useState(null);

  async function handleLogin(e) {
    e.preventDefault();
    setFout(null);
    setLaden(true);
    const { error } = await supabase.auth.signInWithPassword({ email, password: wachtwoord });
    if (error) {
      setFout("E-mailadres of wachtwoord onjuist.");
      setLaden(false);
      return;
    }
    router.push("/projecten");
  }

  return (
    <div className="min-h-screen bg-[#F5F7F9] flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        {/* Logo */}
        <div className="flex flex-col items-center mb-8 gap-3">
          <BorevexaIcon size={48} />
          <div className="text-center">
            <div className="text-[22px] font-bold tracking-tight text-[#0D1520] leading-none">
              Bore<span className="text-[#007A5A]">vexa</span>
            </div>
            <p className="text-xs text-[#8FA6B2] mt-1">Prescan, Ontwerp &amp; Calculatie</p>
          </div>
        </div>

        {/* Card */}
        <div className="bg-white border border-[#DEE6EA] rounded-xl p-6 shadow-sm">
          <h1 className="text-sm font-semibold text-[#1B2B35] mb-5">Inloggen</h1>

          <form onSubmit={handleLogin} className="flex flex-col gap-4">
            <div>
              <label className="block text-xs text-[#587080] mb-1.5 font-medium">E-mailadres</label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                placeholder="naam@bedrijf.nl"
                className="w-full border border-[#DEE6EA] rounded-lg px-3 py-2.5 text-sm text-[#1B2B35] placeholder-[#B0C4CE] outline-none focus:border-[#007A5A] focus:ring-1 focus:ring-[#007A5A]/10 transition-colors"
              />
            </div>

            <div>
              <label className="block text-xs text-[#587080] mb-1.5 font-medium">Wachtwoord</label>
              <input
                type="password"
                value={wachtwoord}
                onChange={(e) => setWachtwoord(e.target.value)}
                required
                placeholder="••••••••"
                className="w-full border border-[#DEE6EA] rounded-lg px-3 py-2.5 text-sm text-[#1B2B35] placeholder-[#B0C4CE] outline-none focus:border-[#007A5A] focus:ring-1 focus:ring-[#007A5A]/10 transition-colors"
              />
            </div>

            {fout && (
              <p className="text-xs text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">
                {fout}
              </p>
            )}

            <button
              type="submit"
              disabled={laden}
              className="w-full bg-[#007A5A] hover:bg-[#00915F] disabled:bg-[#DEE6EA] disabled:text-[#8FA6B2] text-white text-sm font-semibold py-2.5 rounded-lg transition-colors mt-1"
            >
              {laden ? "Bezig..." : "Inloggen"}
            </button>
          </form>
        </div>

        <p className="text-center text-xs text-[#B0C4CE] mt-6">
          © 2026 Borevexa · Made in the Netherlands 🇳🇱
        </p>
      </div>
    </div>
  );
}
