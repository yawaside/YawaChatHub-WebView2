import { useCallback, useEffect, useRef, useState } from "react";
import type { ChatMsg, WidgetConfig } from "../lib/types";
import { DEFAULT_WIDGET } from "../lib/types";
import { isDesktop, onHostEvent } from "../lib/bridge";
import { usePersisted } from "../lib/persist";
import { PlatformIcon, nickColor, platformMeta } from "../lib/platforms";
import { fontStack } from "../lib/fonts";
import { parseMessage } from "../lib/emotes";
import { effectClass, effectVars, textOutline } from "../lib/effects";

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

  /* Звук прямо в источнике OBS. Браузерный источник считается «медиа», поэтому
     при включённой опции «Управлять аудио через OBS» этот звук попадает и в
     микшер OBS, и на локальные колонки — раньше виджет вообще ничего не
     воспроизводил, и захватывать было нечего. */
  const audioRef = useRef<AudioContext | null>(null);
  const playPing = useCallback(
    (isEvent: boolean) => {
      const cur = cfgRef.current;
      if (!cur.sound) return;
      if (cur.soundEventsOnly && !isEvent) return;
      try {
        audioRef.current ??= new AudioContext();
        const ctx = audioRef.current;
        // автозапуск аудио в браузере разрешён после жеста; в OBS контекст
        // стартует сразу, но на всякий случай пробуем возобновить
        if (ctx.state === "suspended") void ctx.resume();

        const t = ctx.currentTime;
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        const vol = Math.min(1, Math.max(0, cur.soundVolume ?? 0.5));
        osc.type = "sine";
        osc.frequency.setValueAtTime(isEvent ? 880 : 660, t);
        osc.frequency.setValueAtTime(isEvent ? 1320 : 880, t + 0.1);
        gain.gain.setValueAtTime(0.0001, t);
        gain.gain.exponentialRampToValueAtTime(Math.max(0.0002, vol * 0.3), t + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.35);
        osc.connect(gain).connect(ctx.destination);
        osc.start(t);
        osc.stop(t + 0.4);
      } catch {
        /* аудио недоступно — виджет продолжает работать */
      }
    },
    []
  );

  /* Воспроизведение озвучки в OBS. Один общий AudioContext и кэш
     раскодированных звуков — создаются один раз, а не на каждое сообщение. */
  const ttsCtxRef = useRef<AudioContext | null>(null);
  const ttsCache = useRef<Map<string, AudioBuffer>>(new Map());

  const playTts = useCallback((b64: string, volume: number) => {
    try {
      const AC = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
      if (!AC) return;
      ttsCtxRef.current ??= new AC();
      const ctx = ttsCtxRef.current;
      if (ctx.state === "suspended") void ctx.resume();

      const cached = ttsCache.current.get(b64);
      const play = (buf: AudioBuffer) => {
        const src = ctx.createBufferSource();
        src.buffer = buf;
        const gain = ctx.createGain();
        gain.gain.value = Math.min(1, Math.max(0, volume));
        src.connect(gain).connect(ctx.destination);
        src.start();
      };

      if (cached) { play(cached); return; }

      // base64 → ArrayBuffer
      const bin = atob(b64);
      const bytes = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
      void ctx.decodeAudioData(bytes.buffer).then((buf) => {
        if (ttsCache.current.size > 30) ttsCache.current.clear();
        ttsCache.current.set(b64, buf);
        play(buf);
      }).catch(() => { /* битый кадр — пропускаем */ });
    } catch { /* аудио недоступно */ }
  }, []);

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
    /* Пакетная обработка: и одиночные сообщения, и chat.batch попадают
       в состояние ОДНИМ обновлением — при плотном чате это радикально
       снижает число ре-рендеров в OBS Browser Source. */
    const pushMany = (list: unknown[]) => {
      const cur = cfgRef.current;
      const fresh: (ChatMsg & { born: number })[] = [];
      let hasEvent = false;

      for (const item of list) {
        const raw = (item ?? {}) as Partial<ChatMsg>;
        const kind = raw.kind ?? "chat";
        if (kind !== "chat" && kind !== "system" && !cur.showEvents) continue;
        const platform = raw.platform ?? "twitch";
        const author = String(raw.author ?? "");
        if (kind !== "chat") hasEvent = true;
        fresh.push({
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
        } as ChatMsg & { born: number });
      }

      if (fresh.length) {
        if (hasEvent) playPing(true);   // один сигнал на пакет, не на каждое сообщение
        setItems((prev) => [...prev, ...fresh].slice(-cur.max));
      }
    };

    // мост приложения (когда страница открыта внутри WebView2)
    const unsub = onHostEvent((type, payload) => {
      if (type === "chat.batch" && Array.isArray(payload)) pushMany(payload);
      else if (type === "chat.message") pushMany([payload]);
      else if (type === "widget.config") setLive(payload as WidgetConfig);
    });

    // SSE от сервера приложения — основной канал для OBS
    let es: EventSource | null = null;
    try {
      es = new EventSource("widget/events");
      es.onmessage = (ev) => {
        try {
          const d = JSON.parse(ev.data) as { event?: string; payload?: unknown };
          if (d.event === "chat.batch" && Array.isArray(d.payload)) {
            realData.current = true;
            pushMany(d.payload);
          } else if (d.event === "chat.message" && d.payload) {
            realData.current = true;
            pushMany([d.payload]);
          } else if (d.event === "widget.config" && d.payload) {
            realData.current = true;
            setLive(d.payload as WidgetConfig);
          } else if (d.event === "tts.audio" && d.payload) {
            // Озвучка текста от приложения. Web Audio API не требует жеста
            // пользователя и надёжно работает в CEF OBS: звук попадает
            // в аудиомикшер OBS (при включённой опции управления аудио).
            const p = d.payload as { audio?: string; volume?: number };
            if (p?.audio) playTts(p.audio, p.volume ?? 1);
          }
        } catch {
          /* битый фрейм */
        }
      };
    } catch {
      /* открыто вне приложения */
    }

    // Демо-строки нужны ТОЛЬКО для предпросмотра в обычном браузере.
    // В OBS страницу отдаёт сервер приложения (порт 8085) и SSE ещё может
    // не успеть подключиться — раньше из-за этого в источник попадало
    // случайное демо-сообщение. Определяем среду по адресу страницы.
    // Страницу виджета отдаёт локальный сервер приложения (любой порт),
    // поэтому демо в OBS не нужно ни при каком стечении обстоятельств.
    const servedByApp =
      typeof location !== "undefined" &&
      /^(127\.0\.0\.1|localhost|\[::1\])$/.test(location.hostname);

    let alive = true;
    const demo = () => {
      if (!alive) return;
      // приложение подключено — демо больше не нужно, цикл останавливаем
      if (realData.current || isDesktop() || servedByApp) return;
      const names = ["nekto_san", "wesna_life", "shadow_walker", "no_scope_anna"];
      const texts = ["привет из чата!", "красиво затащил", "клипую", "Ф в чат", "лучший контент"];
      const plats: ChatMsg["platform"][] = ["twitch", "youtube", "kick", "vkplay"];
      pushMany([{
        platform: plats[Math.floor(Math.random() * plats.length)],
        author: names[Math.floor(Math.random() * names.length)],
        text: texts[Math.floor(Math.random() * texts.length)],
        kind: "chat",
      }]);
      window.setTimeout(demo, 900 + Math.random() * 2000);
    };
    const demoStart = window.setTimeout(demo, 2500);

    return () => {
      alive = false;
      window.clearTimeout(demoStart);
      try { void audioRef.current?.close(); } catch { /* уже закрыт */ }
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
      className={`fixed right-0 bottom-0 left-0 flex flex-col justify-end p-2 style-${c.styleId ?? "rust"}`}
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
            textShadow: textOutline(c.outlineWidth, c.outlines),
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
