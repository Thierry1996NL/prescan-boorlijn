// ============================================================
// Prescan Boorlijn AI — Chat Component
// components/PrescanChat.jsx
// ============================================================

"use client";

import { useState, useEffect, useRef } from "react";
import { slaBerichtOp, verwijderChatGeschiedenis } from "@/lib/supabase-queries";

// Markdown-achtige opmaak voor AI-antwoorden (minimale parser)
function FormateerTekst({ tekst }) {
  const regels = tekst.split("\n");
  return (
    <div className="formatted-text">
      {regels.map((regel, i) => {
        if (regel.startsWith("### ")) return <h3 key={i}>{regel.slice(4)}</h3>;
        if (regel.startsWith("## ")) return <h2 key={i}>{regel.slice(3)}</h2>;
        if (regel.startsWith("# ")) return <h1 key={i}>{regel.slice(2)}</h1>;
        if (regel.startsWith("- ") || regel.startsWith("* ")) {
          return <li key={i}>{regel.slice(2)}</li>;
        }
        if (regel.startsWith("🔴") || regel.startsWith("🟠") || regel.startsWith("🟢")) {
          return (
            <p key={i} className="risico-regel">
              {regel}
            </p>
          );
        }
        if (regel.trim() === "") return <br key={i} />;
        return <p key={i}>{regel}</p>;
      })}
    </div>
  );
}

// Risico-badge component
function RisicoBadge({ tekst }) {
  if (tekst.includes("🔴") || tekst.toLowerCase().includes("rood")) {
    return <span className="badge badge-rood">● Rood</span>;
  }
  if (tekst.includes("🟠") || tekst.toLowerCase().includes("oranje")) {
    return <span className="badge badge-oranje">● Oranje</span>;
  }
  if (tekst.includes("🟢") || tekst.toLowerCase().includes("groen")) {
    return <span className="badge badge-groen">● Groen</span>;
  }
  return null;
}

export default function PrescanChat({ projectId, projectNaam }) {
  const [berichten, setBerichten] = useState([]);
  const [invoer, setInvoer] = useState("");
  const [laden, setLaden] = useState(false);
  const [fout, setFout] = useState(null);
  const onderRef = useRef(null);
  const invoerRef = useRef(null);

  // Scroll naar beneden bij nieuw bericht
  useEffect(() => {
    onderRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [berichten]);

  // Snel-vragen suggesties
  const suggesties = [
    "Wat zijn de risico's bij de gedetecteerde kruisingen?",
    "Genereer een prescan samenvatting voor dit project",
    "Welke boorspoeling adviseer je bij dit bodemtype?",
    "Wat zijn de vrijwaringszones voor dit tracé?",
  ];

  async function verstuurBericht(tekst) {
    const berichtTekst = tekst || invoer.trim();
    if (!berichtTekst || laden) return;

    setInvoer("");
    setFout(null);
    setLaden(true);

    const nieuwGebruikersBericht = {
      rol: "user",
      inhoud: berichtTekst,
      tijdstip: new Date(),
    };

    setBerichten((prev) => [...prev, nieuwGebruikersBericht]);

    try {
      // Sla gebruikersbericht op in Supabase
      if (projectId) {
        await slaBerichtOp(projectId, "user", berichtTekst);
      }

      // Stuur naar API
      const response = await fetch("/api/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          bericht: berichtTekst,
          projectId: projectId ?? null,
        }),
      });

      const data = await response.json();

      if (!response.ok) throw new Error(data.error || "Onbekende fout");

      const aiAntwoord = {
        rol: "assistant",
        inhoud: data.antwoord,
        tijdstip: new Date(),
      };

      setBerichten((prev) => [...prev, aiAntwoord]);

      // Sla AI-antwoord op in Supabase
      if (projectId) {
        await slaBerichtOp(projectId, "assistant", data.antwoord);
      }
    } catch (err) {
      setFout(err.message);
    } finally {
      setLaden(false);
      invoerRef.current?.focus();
    }
  }

  async function verwijderGeschiedenis() {
    if (!projectId) return;
    await verwijderChatGeschiedenis(projectId);
    setBerichten([]);
  }

  function handleKeyDown(e) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      verstuurBericht();
    }
  }

  return (
    <>
      <style>{`
        .chat-container {
          display: flex;
          flex-direction: column;
          height: 100%;
          min-height: 600px;
          background: #0f1117;
          border: 1px solid #1e2433;
          border-radius: 12px;
          overflow: hidden;
          font-family: 'DM Sans', 'Helvetica Neue', sans-serif;
        }

        /* HEADER */
        .chat-header {
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 16px 20px;
          background: #141824;
          border-bottom: 1px solid #1e2433;
        }
        .chat-header-left {
          display: flex;
          align-items: center;
          gap: 10px;
        }
        .chat-icon {
          width: 32px;
          height: 32px;
          background: linear-gradient(135deg, #1B6EF3, #0a4fc4);
          border-radius: 8px;
          display: flex;
          align-items: center;
          justify-content: center;
          font-size: 16px;
        }
        .chat-title {
          font-size: 14px;
          font-weight: 600;
          color: #e8eaf0;
          letter-spacing: -0.01em;
        }
        .chat-project {
          font-size: 11px;
          color: #5a6278;
          margin-top: 1px;
        }
        .chat-clear-btn {
          background: transparent;
          border: 1px solid #1e2433;
          color: #5a6278;
          font-size: 11px;
          padding: 4px 10px;
          border-radius: 6px;
          cursor: pointer;
          transition: all 0.15s;
        }
        .chat-clear-btn:hover {
          border-color: #2a3247;
          color: #8892a4;
        }

        /* BERICHTEN LIJST */
        .chat-berichten {
          flex: 1;
          overflow-y: auto;
          padding: 20px;
          display: flex;
          flex-direction: column;
          gap: 16px;
          scrollbar-width: thin;
          scrollbar-color: #1e2433 transparent;
        }
        .chat-berichten::-webkit-scrollbar { width: 4px; }
        .chat-berichten::-webkit-scrollbar-track { background: transparent; }
        .chat-berichten::-webkit-scrollbar-thumb { background: #1e2433; border-radius: 4px; }

        /* LEEG SCHERM */
        .chat-leeg {
          flex: 1;
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          gap: 24px;
          padding: 40px 20px;
        }
        .chat-leeg-titel {
          font-size: 16px;
          font-weight: 600;
          color: #8892a4;
          text-align: center;
        }
        .chat-leeg-sub {
          font-size: 12px;
          color: #3d4558;
          text-align: center;
          max-width: 280px;
          line-height: 1.6;
        }
        .suggesties {
          display: flex;
          flex-direction: column;
          gap: 8px;
          width: 100%;
          max-width: 400px;
        }
        .suggestie-btn {
          background: #141824;
          border: 1px solid #1e2433;
          color: #8892a4;
          font-size: 12px;
          padding: 10px 14px;
          border-radius: 8px;
          cursor: pointer;
          text-align: left;
          transition: all 0.15s;
          line-height: 1.4;
        }
        .suggestie-btn:hover {
          border-color: #1B6EF3;
          color: #c8d0e0;
          background: #16213a;
        }

        /* BERICHTEN */
        .bericht {
          display: flex;
          gap: 12px;
          animation: fadeInUp 0.2s ease;
        }
        @keyframes fadeInUp {
          from { opacity: 0; transform: translateY(8px); }
          to { opacity: 1; transform: translateY(0); }
        }
        .bericht-avatar {
          width: 28px;
          height: 28px;
          border-radius: 6px;
          display: flex;
          align-items: center;
          justify-content: center;
          font-size: 12px;
          flex-shrink: 0;
          margin-top: 2px;
        }
        .bericht-avatar.user {
          background: #1e2433;
          color: #8892a4;
        }
        .bericht-avatar.assistant {
          background: linear-gradient(135deg, #1B6EF3, #0a4fc4);
          color: white;
        }
        .bericht-inhoud {
          flex: 1;
        }
        .bericht-rol {
          font-size: 11px;
          font-weight: 600;
          color: #3d4558;
          margin-bottom: 4px;
          text-transform: uppercase;
          letter-spacing: 0.05em;
        }
        .bericht-tekst {
          font-size: 13px;
          line-height: 1.65;
          color: #c8d0e0;
        }
        .bericht-tekst.user {
          color: #8892a4;
        }

        /* FORMATTED TEXT */
        .formatted-text h1, .formatted-text h2, .formatted-text h3 {
          color: #e8eaf0;
          font-weight: 600;
          margin: 12px 0 6px;
        }
        .formatted-text h3 { font-size: 13px; }
        .formatted-text h2 { font-size: 14px; }
        .formatted-text p { margin: 4px 0; }
        .formatted-text li {
          margin: 3px 0 3px 16px;
          list-style: disc;
        }
        .formatted-text br { display: block; height: 4px; }
        .risico-regel {
          font-weight: 500;
          padding: 4px 0;
        }

        /* RISICO BADGES */
        .badge {
          display: inline-flex;
          align-items: center;
          gap: 4px;
          font-size: 10px;
          font-weight: 600;
          padding: 2px 8px;
          border-radius: 4px;
        }
        .badge-rood { background: rgba(239,68,68,0.15); color: #ef4444; }
        .badge-oranje { background: rgba(249,115,22,0.15); color: #f97316; }
        .badge-groen { background: rgba(34,197,94,0.15); color: #22c55e; }

        /* LADEN INDICATOR */
        .laden-indicator {
          display: flex;
          gap: 4px;
          align-items: center;
          padding: 4px 0;
        }
        .laden-indicator span {
          width: 6px;
          height: 6px;
          background: #1B6EF3;
          border-radius: 50%;
          animation: pulse 1.2s ease-in-out infinite;
        }
        .laden-indicator span:nth-child(2) { animation-delay: 0.2s; }
        .laden-indicator span:nth-child(3) { animation-delay: 0.4s; }
        @keyframes pulse {
          0%, 80%, 100% { opacity: 0.3; transform: scale(0.8); }
          40% { opacity: 1; transform: scale(1); }
        }

        /* FOUT */
        .fout-bericht {
          background: rgba(239,68,68,0.08);
          border: 1px solid rgba(239,68,68,0.2);
          border-radius: 8px;
          padding: 10px 14px;
          font-size: 12px;
          color: #ef4444;
        }

        /* INVOER */
        .chat-invoer {
          padding: 16px 20px;
          background: #141824;
          border-top: 1px solid #1e2433;
        }
        .invoer-wrapper {
          display: flex;
          gap: 10px;
          align-items: flex-end;
          background: #0f1117;
          border: 1px solid #1e2433;
          border-radius: 10px;
          padding: 10px 12px;
          transition: border-color 0.15s;
        }
        .invoer-wrapper:focus-within {
          border-color: #1B6EF3;
        }
        textarea {
          flex: 1;
          background: transparent;
          border: none;
          outline: none;
          color: #c8d0e0;
          font-size: 13px;
          font-family: inherit;
          resize: none;
          min-height: 20px;
          max-height: 120px;
          line-height: 1.5;
        }
        textarea::placeholder { color: #3d4558; }
        .verstuur-btn {
          width: 32px;
          height: 32px;
          background: #1B6EF3;
          border: none;
          border-radius: 7px;
          cursor: pointer;
          display: flex;
          align-items: center;
          justify-content: center;
          flex-shrink: 0;
          transition: all 0.15s;
          color: white;
        }
        .verstuur-btn:hover { background: #1558d4; }
        .verstuur-btn:disabled {
          background: #1e2433;
          cursor: not-allowed;
          color: #3d4558;
        }
        .invoer-hint {
          font-size: 10px;
          color: #3d4558;
          margin-top: 6px;
          text-align: right;
        }
      `}</style>

      <div className="chat-container">
        {/* Header */}
        <div className="chat-header">
          <div className="chat-header-left">
            <div className="chat-icon">🛠</div>
            <div>
              <div className="chat-title">Prescan Boorlijn AI</div>
              <div className="chat-project">
                {projectNaam ?? "Geen project geselecteerd"}
              </div>
            </div>
          </div>
          {berichten.length > 0 && (
            <button className="chat-clear-btn" onClick={verwijderGeschiedenis}>
              Gesprek wissen
            </button>
          )}
        </div>

        {/* Berichten */}
        {berichten.length === 0 ? (
          <div className="chat-leeg">
            <div>
              <div className="chat-leeg-titel">Hoe kan ik helpen?</div>
              <div className="chat-leeg-sub">
                Stel een vraag over het tracé, de KLIC-melding of het booradvies.
              </div>
            </div>
            <div className="suggesties">
              {suggesties.map((s, i) => (
                <button
                  key={i}
                  className="suggestie-btn"
                  onClick={() => verstuurBericht(s)}
                >
                  {s}
                </button>
              ))}
            </div>
          </div>
        ) : (
          <div className="chat-berichten">
            {berichten.map((b, i) => (
              <div key={i} className="bericht">
                <div className={`bericht-avatar ${b.rol}`}>
                  {b.rol === "user" ? "👤" : "🛠"}
                </div>
                <div className="bericht-inhoud">
                  <div className="bericht-rol">
                    {b.rol === "user" ? "Jij" : "Prescan AI"}
                  </div>
                  <div className={`bericht-tekst ${b.rol}`}>
                    {b.rol === "assistant" ? (
                      <FormateerTekst tekst={b.inhoud} />
                    ) : (
                      b.inhoud
                    )}
                  </div>
                </div>
              </div>
            ))}

            {laden && (
              <div className="bericht">
                <div className="bericht-avatar assistant">🛠</div>
                <div className="bericht-inhoud">
                  <div className="bericht-rol">Prescan AI</div>
                  <div className="laden-indicator">
                    <span /><span /><span />
                  </div>
                </div>
              </div>
            )}

            {fout && (
              <div className="fout-bericht">
                ⚠ Fout: {fout}. Probeer het opnieuw.
              </div>
            )}

            <div ref={onderRef} />
          </div>
        )}

        {/* Invoer */}
        <div className="chat-invoer">
          <div className="invoer-wrapper">
            <textarea
              ref={invoerRef}
              value={invoer}
              onChange={(e) => setInvoer(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Stel een vraag over het project..."
              rows={1}
              onInput={(e) => {
                e.target.style.height = "auto";
                e.target.style.height = e.target.scrollHeight + "px";
              }}
            />
            <button
              className="verstuur-btn"
              onClick={() => verstuurBericht()}
              disabled={!invoer.trim() || laden}
              title="Verstuur (Enter)"
            >
              ↑
            </button>
          </div>
          <div className="invoer-hint">Enter om te versturen · Shift+Enter voor nieuwe regel</div>
        </div>
      </div>
    </>
  );
}
