"use client";

import { useState } from "react";
import { supabase } from "@/lib/supabase-queries";
import { useRouter } from "next/navigation";

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

    const { error } = await supabase.auth.signInWithPassword({
      email,
      password: wachtwoord,
    });

    if (error) {
      setFout("E-mailadres of wachtwoord onjuist.");
      setLaden(false);
      return;
    }

    router.push("/projecten");
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-[#0f1117]">
      <div className="w-full max-w-sm">
        {/* Logo */}
        <div className="text-center mb-8">
          <div className="inline-flex items-center gap-2 mb-2">
            <span className="text-2xl font-bold text-white">Prescan</span>
            <span className="text-2xl font-bold text-[#1B6EF3]">AI</span>
          </div>
          <p className="text-sm text-[#5a6278]">Gestuurde boringen · HDD</p>
        </div>

        {/* Form */}
        <div className="bg-[#141824] border border-[#1e2433] rounded-xl p-6">
          <h1 className="text-base font-semibold text-white mb-5">Inloggen</h1>

          <form onSubmit={handleLogin} className="flex flex-col gap-4">
            <div>
              <label className="block text-xs text-[#5a6278] mb-1.5 font-medium">
                E-mailadres
              </label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                placeholder="naam@bedrijf.nl"
                className="w-full bg-[#0f1117] border border-[#1e2433] rounded-lg px-3 py-2.5 text-sm text-white placeholder-[#3d4558] outline-none focus:border-[#1B6EF3] transition-colors"
              />
            </div>

            <div>
              <label className="block text-xs text-[#5a6278] mb-1.5 font-medium">
                Wachtwoord
              </label>
              <input
                type="password"
                value={wachtwoord}
                onChange={(e) => setWachtwoord(e.target.value)}
                required
                placeholder="••••••••"
                className="w-full bg-[#0f1117] border border-[#1e2433] rounded-lg px-3 py-2.5 text-sm text-white placeholder-[#3d4558] outline-none focus:border-[#1B6EF3] transition-colors"
              />
            </div>

            {fout && (
              <p className="text-xs text-red-400 bg-red-400/10 border border-red-400/20 rounded-lg px-3 py-2">
                {fout}
              </p>
            )}

            <button
              type="submit"
              disabled={laden}
              className="w-full bg-[#1B6EF3] hover:bg-[#1558d4] disabled:bg-[#1e2433] disabled:text-[#3d4558] text-white text-sm font-semibold py-2.5 rounded-lg transition-colors mt-1"
            >
              {laden ? "Bezig met inloggen..." : "Inloggen"}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
