"use client";

// ─── Игровой оверлей (ТЗ §13; адрес #/overlay в desktop) ────────────────────
// Прозрачное окно: фон убирается на уровне html, прозрачна только подложка.
import { useEffect, useRef, useState } from "react";
import { MousePointerClick, Pin, X } from "lucide-react";
import { DEFAULT_SETTINGS, type ChatMsg, type OverlayConfig } from "@/lib/types";
import { lookVars } from "@/lib/look";
import { WidgetMsgRow } from "@/components/widget-row";

export default function OverlayPage() {
  const [cfg, setCfg] = useState<OverlayConfig>(DEFAULT_SETTINGS.overlay);
  const [messages, setMessages] = useState<ChatMsg[]>([]);
  const cfgRef = useRef(cfg);
  cfgRef.current = cfg;

  useEffect(() => {
    document.documentElement.classList.add("overlay-mode");
    document.body.style.background = "transparent";
    let es: EventSource | null = null;
    let cancelled = false;

    void fetch("/api/settings", { cache: "no-store" })
      .then((r) => r.json())
      .then((d) => {
        if (cancelled || !d?.settings) return;
        const token = d.settings.token as string;
        if (d.settings.overlay) setCfg((c) => ({ ...c, ...d.settings.overlay }));
        es = new EventSource(`/api/stream?kind=overlay&token=${encodeURIComponent(token)}`);
        es.onmessage = (e) => {
          try {
            const ev = JSON.parse(e.data) as { type: string; msg?: ChatMsg; cfg?: OverlayConfig };
            if (ev.type === "overlay:config" && ev.cfg) setCfg((c) => ({ ...c, ...ev.cfg }));
            if (ev.type === "chat" && ev.msg) {
              const msg = ev.msg;
              setMessages((m) => {
                const next = m.length >= cfgRef.current.maxMessages + 4 ? m.slice(m.length - (cfgRef.current.maxMessages + 3)) : m.slice();
                next.push(msg);
                return next;
              });
            }
          } catch {}
        };
      })
      .catch(() => {});

    return () => {
      cancelled = true;
      es?.close();
      document.documentElement.classList.remove("overlay-mode");
    };
  }, []);

  const shown = messages.filter((m) => !m.sys).slice(-cfg.maxMessages);

  return (
    <main className="h-screen w-screen overflow-hidden p-2" style={{ background: "transparent" }}>
      <div className="flex h-full flex-col justify-end" style={lookVars({ theme: "minimal-dark", fontSize: cfg.fontSize, bgOpacity: cfg.bgOpacity, radius: cfg.radius, shadow: true, border: cfg.showBorder, textColor: cfg.textColor, nameColor: cfg.nameColor, bgColor: cfg.bgColor, bgImage: cfg.bgImage }, cfg.effectDuration)}>
        {cfg.showBorder && (
          <div
            className="app-drag mb-1.5 flex select-none items-center gap-2 px-3 py-1.5"
            style={{
              borderRadius: "var(--w-radius)",
              background: "var(--w-bg)",
              border: "var(--w-border-w) solid var(--w-border)",
              color: "var(--w-text)",
              fontSize: Math.max(10, cfg.fontSize - 1),
              pointerEvents: cfg.clickThrough ? "none" : "auto",
            }}
          >
            <span className="size-1.5 flex-none rounded-full live-dot" style={{ background: "#34d399" }} />
            <span className="font-bold">YawaChatHub</span>
            <span className="ml-2 opacity-55">{cfg.locked ? "закреплён" : cfg.clickThrough ? "сквозные клики" : "тяните за шапку"}</span>
            <span className="app-no-drag ml-auto flex items-center gap-1">
              {cfg.clickThrough && <MousePointerClick size={11} className="opacity-60" />}
              {cfg.locked && <Pin size={11} className="opacity-60" />}
              <button type="button" className="opacity-50 transition-opacity hover:opacity-100" onClick={() => window.close()} aria-label="Скрыть">
                <X size={12} />
              </button>
            </span>
          </div>
        )}
        <div className="app-no-drag flex flex-col justify-end gap-1.5" style={{ pointerEvents: cfg.clickThrough ? "none" : "auto" }}>
          {shown.map((m) => (
            <WidgetMsgRow
              key={m.id}
              msg={m}
              fx={cfg.effect}
              opts={{
                style: cfg.mode === "compact" ? "compact" : cfg.style,
                showPlatform: cfg.showPlatform,
                showTime: cfg.showTime,
              }}
            />
          ))}
        </div>
      </div>
    </main>
  );
}
