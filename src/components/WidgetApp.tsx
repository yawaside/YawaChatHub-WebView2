import { useEffect, useRef, useState } from "react";
import type { ChatMsg, WidgetConfig } from "../lib/types";
import { DEFAULT_WIDGET } from "../lib/types";
import { isDesktop, onHostEvent } from "../lib/bridge";
import { usePersisted } from "../lib/persist";
import { PlatformIcon, nickColor, platformMeta } from "../lib/platforms";
import { fontStack } from "../lib/fonts";
import { parseMessage } from "../lib/emotes";
import { effectClass, effectVars } from "../lib/effects";

/**
 * Страница виджета для OBS Browser Source (#/widget).
 *
 * ССЫЛКА В OBS ПОСТОЯННАЯ: http://127.0.0.1:8085/widget
 * Оформление НЕ зашито в адрес, а приходит живым событием "widget.config"
 * по SSE — поэтому изменения настроек применяются в OBS сразу и Browser
 * Source не нужно пересоздавать.
 */
export default function WidgetApp() {
  const [stored] = usePersisted<WidgetConfig>("widget", DEFAULT_WIDGET);
  const [live, setLive] = useState<WidgetConfig | null>(null);
  const [items, setItems] = useState<(ChatMsg & { born: number })[]>([]);
  const [now, setNow] = useState(Date.now());
  const seq = useRef(1);
  const realData = useRef(false);

  const c: WidgetConfig = { ...DEFAULT_WIDGET, ...stored, ...(live ?? {}) };
  const cfgRef = useRef(c);
  cfgRef.current = c;

  const dark = c.theme !== "light";

  /* Холст Browser Source всегда прозрачный: фон рисуют только карточки
     сообщений, иначе OBS показывает сплошной прямоугольник. */
  useEffect(() => {
    document.documentElement.style.setProperty("background", "transparent", "important");
    document.body.style.setProperty("background", "transparent", "important");
  }, []);

  /* тик только когда есть что скрывать — иначе лишние перерисовки */
  useEffect(() => {
    if (!c.autoFade || items.length === 0) return;
    const t = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, [c.autoFade, items.length]);

  useEffect(() => {
    const push = (raw: Partial<ChatMsg>) => {
      const cur = cfgRef.current;
      const kind = raw.kind ?? "chat";
      if (kind !== "chat" && kind !== "system" && !cur.showEvents) return;
      const platform = raw.platform ?? "twitch";
      const author = String(raw.author ?? "");
      setItems((prev) =>
        [
          ...prev,
          {
            id: seq.current++,
            ts: typeof raw.ts === "number" && raw.ts > 0 ? raw.ts : Date.now(),
            platform,
            channel: String(raw.channel ?? ""),
            author,
            color: String(raw.color || nickColor(author, platformMeta(platform).color)),
            text: String(raw.text ?? ""),
            kind,
            amount: typeof raw.amount === "number" ? raw.amount : undefined,
            currency: raw.currency,
            born: Date.now(),
          } as ChatMsg & { born: number },
        ].slice(-cur.max)
      );
    };

    // мост приложения (когда страница открыта внутри WebView2)
    const unsub = onHostEvent((type, payload) => {
      if (type === "chat.message") push(payload as ChatMsg);
      else if (type === "widget.config") setLive(payload as WidgetConfig);
    });

    // SSE от сервера приложения — основной канал для OBS
    let es: EventSource | null = null;
    try {
      es = new EventSource("widget/events");
      es.onmessage = (ev) => {
        try {
          const d = JSON.parse(ev.data) as { event?: string; payload?: unknown };
          if (d.event === "chat.message" && d.payload) {
            realData.current = true;
            push(d.payload as ChatMsg);
          } else if (d.event === "widget.config" && d.payload) {
            realData.current = true;
            setLive(d.payload as WidgetConfig);
          }
        } catch {
          /* битый фрейм */
        }
      };
    } catch {
      /* открыто вне приложения */
    }

    // демо только в браузерном предпросмотре
    let alive = true;
    const demo = () => {
      if (!alive) return;
      // приложение подключено — демо больше не нужно, цикл останавливаем
      if (realData.current || isDesktop()) return;
      const names = ["nekto_san", "wesna_life", "shadow_walker", "no_scope_anna"];
      const texts = ["привет из чата!", "красиво затащил", "клипую", "Ф в чат", "лучший контент"];
      const plats: ChatMsg["platform"][] = ["twitch", "youtube", "kick", "vkplay"];
      push({
        platform: plats[Math.floor(Math.random() * plats.length)],
        author: names[Math.floor(Math.random() * names.length)],
        text: texts[Math.floor(Math.random() * texts.length)],
        kind: "chat",
      });
      window.setTimeout(demo, 900 + Math.random() * 2000);
    };
    demo();

    return () => {
      alive = false;
      unsub();
      es?.close();
    };
  }, []);

  // сообщения исчезают по таймеру, как в оверлее
  const visible = c.autoFade
    ? items.filter((m) => now - m.born < Math.max(1, c.fadeAfterSec ?? 30) * 1000)
    : items;

  return (
    <div
      className="fixed right-0 bottom-0 left-0 flex flex-col justify-end p-2"
      style={{
        fontSize: c.fontSize,
        fontFamily: fontStack(c.fontFamily),
        gap: c.spacing,
        background: "transparent",
      }}
    >
      {visible.map((m) => (
        <div
          key={m.id}
          className={`flex items-baseline gap-1.5 px-2.5 py-1 leading-snug ${effectClass(c.effect)}`}
          style={{
            ...effectVars(c.effectSpeed),
            borderRadius: c.radius,
            background: c.transparent ? "transparent" : dark ? "rgba(10,12,20,0.78)" : "rgba(255,255,255,0.92)",
            borderLeft: c.stripe ? `3px solid ${platformMeta(m.platform).color}` : "none",
            color: dark || c.transparent ? "#eef0f8" : "#121524",
            textShadow: c.outlines ? "0 1px 3px rgba(0,0,0,0.95), 0 0 2px rgba(0,0,0,0.9)" : "none",
          }}
        >
          <span className="flex shrink-0 translate-y-[1px]">
            <PlatformIcon id={m.platform} size={Math.round(c.fontSize * 0.85)} />
          </span>
          <span className="shrink-0 font-bold" style={{ color: m.color }}>
            {c.nickCase === "upper" ? m.author.toUpperCase() : m.author}
          </span>
          {m.amount !== undefined && (
            <span className="shrink-0 rounded px-1 font-bold" style={{ background: "rgba(245,158,11,0.28)", color: "#fde68a" }}>
              {m.amount.toLocaleString("ru-RU")} {m.currency}
            </span>
          )}
          <span className="min-w-0 break-words">
            {/* картиночные смайлы: раньше в OBS выводился сырой текст */}
            {parseMessage(String(m.text ?? ""), m.platform).map((part, i) =>
              part.kind === "emote" ? (
                <img
                  key={i}
                  src={part.url}
                  alt={`:${part.code}:`}
                  draggable={false}
                  className="mx-px inline-block align-[-0.28em]"
                  style={{ height: "1.4em", width: "auto" }}
                  onError={(e) => {
                    e.currentTarget.style.display = "none";
                  }}
                />
              ) : (
                <span key={i}>{part.text}</span>
              )
            )}
          </span>
        </div>
      ))}
    </div>
  );
}
