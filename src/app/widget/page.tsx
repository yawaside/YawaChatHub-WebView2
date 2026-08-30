"use client";

// ─── Виджет для OBS (ТЗ §12; статичный URL /widget?token=…) ─────────────────
// Параметры оформления приходят по потоку (SSE) — URL никогда не меняется.
import { useEffect, useRef, useState } from "react";
import { DEFAULT_SETTINGS, type ChatMsg, type WidgetConfig } from "@/lib/types";
import { lookVars, widgetLike } from "@/lib/look";
import { WidgetMsgRow } from "@/components/widget-row";

interface TimedMsg {
  msg: ChatMsg;
  until: number;
}

export default function WidgetPage() {
  const [cfg, setCfg] = useState<WidgetConfig>(DEFAULT_SETTINGS.widget);
  const [items, setItems] = useState<TimedMsg[]>([]);
  const [status, setStatus] = useState<"connect" | "ok" | "denied">("connect");
  const cfgRef = useRef(cfg);
  cfgRef.current = cfg;

  useEffect(() => {
    document.documentElement.classList.add("overlay-mode");
    document.body.style.background = "transparent";
    document.body.style.margin = "0";
    let es: EventSource | null = null;
    const params = new URLSearchParams(window.location.search);
    const token = params.get("token") ?? "";
    es = new EventSource(`/api/stream?kind=widget&token=${encodeURIComponent(token)}`);
    es.onopen = () => setStatus("ok");
    es.onerror = () => setStatus((s) => (s === "ok" ? s : "denied"));
    es.onmessage = (e) => {
      try {
        const ev = JSON.parse(e.data) as { type: string; msg?: ChatMsg; cfg?: WidgetConfig };
        if (ev.type === "widget:config" && ev.cfg) setCfg((c) => ({ ...c, ...ev.cfg }));
        if (ev.type === "chat" && ev.msg) {
          const until = Date.now() + cfgRef.current.duration * 1000;
          setItems((list) => {
            const next = list.slice(-(cfgRef.current.maxMessages - 1));
            next.push({ msg: ev.msg as ChatMsg, until });
            return next;
          });
        }
      } catch {}
    };
    const purge = setInterval(() => {
      const now = Date.now();
      setItems((list) => (list.some((i) => i.until < now) ? list.filter((i) => i.until >= now) : list));
    }, 800);
    return () => {
      es?.close();
      clearInterval(purge);
      document.documentElement.classList.remove("overlay-mode");
    };
  }, []);

  const shown = items.slice(-cfg.maxMessages);
  const ordered = cfg.dir === "down" ? [...shown].reverse() : shown;

  return (
    <main className="h-screen w-screen overflow-hidden" style={{ background: "transparent" }}>
      <div
        className="flex h-full flex-col justify-end gap-2 p-1"
        style={lookVars(widgetLike(cfg), cfg.effectDuration)}
      >
        {ordered.map(({ msg }) => (
          <WidgetMsgRow
            key={msg.id}
            msg={msg}
            fx={cfg.effect}
            opts={{ style: cfg.style, showPlatform: cfg.showPlatform, showTime: cfg.showTime }}
          />
        ))}
      </div>
      {status === "denied" && (
        <div style={{ position: "fixed", inset: 0, display: "grid", placeItems: "center", color: "#fff", font: "600 13px system-ui", background: "rgba(0,0,0,.6)" }}>
          Неверный токен — возьмите актуальную ссылку на вкладке «Виджет OBS»
        </div>
      )}
    </main>
  );
}
