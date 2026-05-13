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
    const { error } = await supabase.auth.signInWithPassword({ email, password: wachtwoord });
    if (error) {
      setFout("E-mailadres of wachtwoord onjuist.");
      setLaden(false);
      return;
    }
    router.push("/projecten");
  }

  return (
    <div className="min-h-screen bg-gray-50 flex items-center justify-center">
      <div className="w-full max-w-sm">
        {/* Logo */}
        <div className="text-center mb-8">
          <div className="inline-flex items-center gap-2 mb-2">
            <span className="text-2xl font-bold text-gray-900">Prescan</span>
            <span className="text-2xl font-bold text-blue-600">AI</span>
          </div>
          <p className="text-sm text-gray-400">Gestuurde boringen · HDD</p>
        </div>

        {/* Card */}
        <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm">
          <h1 className="text-base font-semibold text-gray-900 mb-5">Inloggen</h1>

          <form onSubmit={handleLogin} className="flex flex-col gap-4">
            <div>
              <label className="block text-xs text-gray-500 mb-1.5 font-medium">E-mailadres</label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                placeholder="naam@bedrijf.nl"
                className="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm text-gray-900 placeholder-gray-300 outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-100 transition-colors"
              />
            </div>

            <div>
              <label className="block text-xs text-gray-500 mb-1.5 font-medium">Wachtwoord</label>
              <input
                type="password"
                value={wachtwoord}
                onChange={(e) => setWachtwoord(e.target.value)}
                required
                placeholder="••••••••"
                className="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm text-gray-900 placeholder-gray-300 outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-100 transition-colors"
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
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:bg-gray-200 disabled:text-gray-400 text-white text-sm font-semibold py-2.5 rounded-lg transition-colors mt-1"
            >
              {laden ? "Bezig..." : "Inloggen"}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
