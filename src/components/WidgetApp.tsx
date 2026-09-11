import { useEffect, useRef, useState } from "react";
import type { ChatMsg } from "../lib/types";
import { isDesktop, onHostEvent } from "../lib/bridge";
import { PlatformIcon, nickColor, platformMeta } from "../lib/platforms";
import { fontStack } from "../lib/fonts";
import type { FontId } from "../lib/types";

/**
 * Страница виджета для OBS Browser Source (#/widget).
 * В WebView2 при любом запуске приложения её отдаёт встроенный HTTP-сервер
 * (http://127.0.0.1:8085/widget) — в браузере служит предпросмотром.
 */
export default function WidgetApp() {
  const params = new URLSearchParams(location.hash.split("?")[1] ?? location.search);
  const max = Number(params.get("max") ?? 8);
  const fs = Number(params.get("fs") ?? 16);
  const theme = params.get("theme") ?? "dark";
  const transparent = params.get("transparent") === "1";
  const font = fontStack((params.get("font") as FontId | null) ?? "inter");
  const gap = Number(params.get("gap") ?? 4);
  const dark = theme !== "light";

  const [items, setItems] = useState<ChatMsg[]>([]);
  const seq = useRef(1);
  const realData = useRef(false); // true, когда идут события от приложения (SSE/мост)

  useEffect(() => {
    if (transparent || dark) {
      document.body.style.background = transparent ? "transparent" : "#0b0d14";
    }
    const pushItem = (m: ChatMsg) =>
      setItems((prev) =>
        [
          ...prev,
          {
            ...m,
            id: seq.current++,
            ts: m.ts || Date.now(),
            color: m.color || nickColor(m.author, platformMeta(m.platform).color),
          },
        ].slice(-max)
      );

    const unsub = onHostEvent((type, payload) => {
      if (type === "chat.message") pushItem(payload as ChatMsg);
    });

    /* Реальные данные в OBS: страницу отдаёт сервер приложения (127.0.0.1:8085),
       события приходят по SSE /widget/events. Пока SSE жив — демо отключено. */
    let es: EventSource | null = null;
    try {
      es = new EventSource("widget/events");
      es.onopen = () => {
        realData.current = true;
      };
      es.onmessage = (ev) => {
        try {
          const d = JSON.parse(ev.data) as { event?: string; payload?: ChatMsg };
          if (d.event === "chat.message" && d.payload) {
            realData.current = true;
            pushItem(d.payload);
          }
        } catch {
          /* битый фрейм */
        }
      };
    } catch {
      /* не desktop-хост */
    }

    let alive = true;
    const demo = () => {
      if (!alive) return;
      if (realData.current || isDesktop()) {
        // приложение подключено — ждём реальные сообщения, демо не показываем
        window.setTimeout(demo, 1500);
        return;
      }
      const names = ["nekto_san", "wesna_life", "shadow_walker", "no_scope_anna", "zloy_bananchik"];
      const texts = ["привет из чата!", "главное — участие", "красиво затащил", "клипую клипую", "а дальше что по плану?", "Ф в чат", "лучший контент"];
      const plats: ChatMsg["platform"][] = ["twitch", "youtube", "kick", "vkplay", "tiktok"];
      const p = plats[Math.floor(Math.random() * plats.length)];
      const a = names[Math.floor(Math.random() * names.length)];
      setItems((prev) =>
        [
          ...prev,
          {
            id: seq.current++,
            ts: Date.now(),
            platform: p,
            channel: "",
            author: a,
            color: nickColor(a, platformMeta(p).color),
            text: texts[Math.floor(Math.random() * texts.length)],
            kind: "chat" as const,
          },
        ].slice(-max)
      );
      window.setTimeout(demo, 800 + Math.random() * 2000);
    };
    demo();
    return () => {
      alive = false;
      unsub();
      es?.close();
    };
  }, [max, transparent, dark]);

  return (
    <div
      className="fixed right-0 bottom-0 left-0 flex flex-col justify-end p-2"
      style={{
        fontSize: fs,
        fontFamily: font,
        gap,
        background: transparent ? "transparent" : dark ? "#0b0d14" : "#f4f6fb",
      }}
    >
      {items.map((m) => (
        <div
          key={m.id}
          className="msg-anim flex items-baseline gap-1.5 rounded-lg px-2.5 py-1.5 leading-snug"
          style={{
            background: dark ? "rgba(10,12,20,0.78)" : "rgba(255,255,255,0.92)",
            color: dark ? "#eef0f8" : "#121524",
            textShadow: dark ? "0 1px 3px rgba(0,0,0,0.9)" : "none",
            boxShadow: dark ? "none" : "0 1px 4px rgba(15,23,42,0.12)",
          }}
        >
          <span className="flex shrink-0 translate-y-[1px]">
            <PlatformIcon id={m.platform} size={13} />
          </span>
          <span className="shrink-0 font-bold" style={{ color: m.color }}>
            {m.author}
          </span>
          <span className="min-w-0 break-words">{m.text}</span>
        </div>
      ))}
    </div>
  );
}
