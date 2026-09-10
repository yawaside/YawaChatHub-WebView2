import { useEffect, useMemo, useRef, useState } from "react";
import type { ChatMsg, OverlayConfig, OverlayStats, PlatformId } from "../lib/types";
import { DEFAULT_OVERLAY } from "../lib/types";
import { isDesktop, onHostEvent, overlayBridge } from "../lib/bridge";
import { usePersisted } from "../lib/persist";
import { DonationAlertsIcon, PlatformIcon, nickColor, platformMeta } from "../lib/platforms";
import { parseMessage } from "../lib/emotes";

/**
 * Оверлей поверх игры (#/overlay).
 * • Прозрачность применяется ТОЛЬКО к подложке — текст всегда чёткий.
 * • Панель площадок по умолчанию СНИЗУ ленты; донаты — первый элемент.
 * • Показываются только подключённые площадки.
 * • Свободное размещение: окно можно перетащить мышью в любую точку экрана.
 */
export default function OverlayApp() {
  const [stored] = usePersisted<OverlayConfig>("overlay", DEFAULT_OVERLAY);
  const [live, setLive] = useState<OverlayConfig | null>(null);
  const [items, setItems] = useState<(ChatMsg & { born: number })[]>([]);
  const [stats, setStats] = useState<OverlayStats | null>(null);
  const [now, setNow] = useState(Date.now());
  const [dragging, setDragging] = useState(false);
  const seq = useRef(1);

  const c: OverlayConfig = { ...DEFAULT_OVERLAY, ...stored, ...(live ?? {}) };
  const cfgRef = useRef(c);
  cfgRef.current = c;

  useEffect(() => {
    document.documentElement.style.setProperty("--dw-bg", "transparent");
    document.body.style.background = "transparent";
  }, []);

  /* ---------- события приложения ---------- */
  useEffect(() => {
    const unsub = onHostEvent((type, payload) => {
      if (type === "chat.message") {
        const cur = cfgRef.current;
        const raw = (payload ?? {}) as Partial<ChatMsg>;
        const validPlatforms: PlatformId[] = ["twitch", "youtube", "vkplay", "kick", "tiktok", "donationalerts"];
        const platform = validPlatforms.includes(raw.platform as PlatformId)
          ? (raw.platform as PlatformId)
          : "twitch";
        const author = String(raw.author ?? "");
        const m: ChatMsg = {
          id: typeof raw.id === "number" ? raw.id : Date.now(),
          ts: typeof raw.ts === "number" && raw.ts > 0 ? raw.ts : Date.now(),
          platform,
          channel: String(raw.channel ?? ""),
          author,
          color: String(raw.color || nickColor(author, platformMeta(platform).color)),
          text: String(raw.text ?? ""),
          kind: raw.kind ?? "chat",
          badges: Array.isArray(raw.badges) ? raw.badges.map(String) : undefined,
          amount: typeof raw.amount === "number" ? raw.amount : undefined,
          currency: raw.currency ? String(raw.currency) : undefined,
        };
        if (!m.text && m.kind === "chat") return;
        if (cur.eventsOnly && (m.kind === "chat" || m.kind === "system")) return;
        setItems((p) =>
          [
            ...p,
            {
              ...m,
              id: seq.current++,
              born: Date.now(),
              ts: m.ts || Date.now(),
              color: m.color || nickColor(m.author, platformMeta(m.platform).color),
            },
          ].slice(-cur.maxMessages)
        );
      } else if (type === "overlay.stats") {
        setStats(payload as OverlayStats);
      } else if (type === "overlay.config") {
        setLive(payload as OverlayConfig);
      }
    });

    /* демо только вне приложения (предпросмотр в браузере) */
    let alive = !isDesktop();
    const demo = () => {
      if (!alive) return;
      const names = ["nekto_san", "kot1k_tv", "lazy_panda", "ultra_nagibator", "chill_guy"];
      const texts = ["красиво!", "ГГ :fire:", "изи катка", "клипую", "а это как?", "лучший стрим :100:", "Ф", "погнали"];
      const plats: PlatformId[] = ["twitch", "youtube", "kick"];
      const p = plats[Math.floor(Math.random() * plats.length)];
      const a = names[Math.floor(Math.random() * names.length)];
      setItems((prev) =>
        [
          ...prev,
          {
            id: seq.current++,
            born: Date.now(),
            ts: Date.now(),
            platform: p,
            channel: "",
            author: a,
            color: nickColor(a, platformMeta(p).color),
            text: texts[Math.floor(Math.random() * texts.length)],
            kind: "chat" as const,
          },
        ].slice(-cfgRef.current.maxMessages)
      );
      setStats({
        donations: { total: 3250, currency: "₽", count: 7 },
        platforms: [
          { platform: "twitch", online: true, viewers: 2213, messages: 148 },
          { platform: "youtube", online: true, viewers: 1042, messages: 61 },
          { platform: "kick", online: false, viewers: 0, messages: 0 },
        ],
      });
      window.setTimeout(demo, 900 + Math.random() * 2200);
    };
    demo();

    return () => {
      alive = false;
      unsub();
    };
  }, []);

  /* ---------- перетаскивание окна мышью ---------- */
  useEffect(() => {
    if (!c.freePosition || c.locked) return;
    const onDown = (e: MouseEvent) => {
      if (e.button !== 0) return;
      setDragging(true);
      overlayBridge.beginDrag();
    };
    const onUp = () => setDragging(false);
    window.addEventListener("mousedown", onDown);
    window.addEventListener("mouseup", onUp);
    return () => {
      window.removeEventListener("mousedown", onDown);
      window.removeEventListener("mouseup", onUp);
    };
  }, [c.freePosition, c.locked]);

  /* тик для авто-затухания */
  useEffect(() => {
    if (!c.autoFade) return;
    const t = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, [c.autoFade]);

  const visible = useMemo(() => {
    if (!c.autoFade) return items;
    return items.filter((m) => now - m.born < c.fadeAfterSec * 1000);
  }, [items, now, c.autoFade, c.fadeAfterSec]);

  /* только подключённые площадки (по желанию — все) */
  const shownPlatforms = useMemo(() => {
    const list = stats?.platforms ?? [];
    return c.hideDisconnected
      ? list.filter((p) => p.online || p.connected || p.messages > 0)
      : list;
  }, [stats, c.hideDisconnected]);

  const hasDonations = c.showDonations && (stats?.donations.count ?? 0) >= 0;
  const statsVisible = c.showStats && (shownPlatforms.length > 0 || hasDonations);

  const backdrop = (extra = 0) => ({
    background: `rgba(8, 10, 16, ${Math.min(1, Math.max(0, c.backdropOpacity + extra))})`,
    backdropFilter: c.backdropOpacity > 0.05 ? "blur(6px)" : "none",
    WebkitBackdropFilter: c.backdropOpacity > 0.05 ? "blur(6px)" : "none",
    borderRadius: c.radius,
  });

  const textShadow = c.shadowText ? "0 1px 3px rgba(0,0,0,0.95), 0 0 2px rgba(0,0,0,0.9)" : "none";

  /* Позицию и размер задаёт нативное окно. Внутри WebView страница всегда
     занимает 100% его площади, иначе экранные отступы применялись дважды,
     а flex-область сообщений схлопывалась до нулевой высоты. */
  const placement: React.CSSProperties = { inset: 0, width: "100%", height: "100%" };

  /* ---------- панель статистики ---------- */
  const statsBar = statsVisible ? (
    <div
      className="flex flex-wrap items-center gap-x-2.5 gap-y-1 px-2.5 py-1.5"
      style={{ ...backdrop(0.06), color: "#fff", textShadow }}
    >
      {/* донаты — всегда первым элементом, оригинальная иконка DonationAlerts */}
      {c.showDonations && (
        <span className="flex items-center gap-1.5 font-bold" style={{ color: "#FFB454" }}>
          <DonationAlertsIcon size={Math.round(c.fontSize * 1.1)} />
          {(stats?.donations.total ?? 0).toLocaleString("ru-RU")} {stats?.donations.currency ?? "₽"}
          {!c.statsCompact && (stats?.donations.count ?? 0) > 0 && (
            <span className="font-medium opacity-75">· {stats!.donations.count}</span>
          )}
        </span>
      )}

      {c.showDonations && shownPlatforms.length > 0 && (
        <span className="h-3 w-px" style={{ background: "rgba(255,255,255,0.25)" }} />
      )}

      {shownPlatforms.map((p) => (
        <span
          key={p.platform}
          className="flex items-center gap-1"
          style={{ opacity: p.online ? 1 : 0.72 }}
          title={`${platformMeta(p.platform).name}: ${p.online ? "в эфире" : "подключено"}`}
        >
          <PlatformIcon id={p.platform} size={Math.round(c.fontSize * 0.95)} />
          {/* зелёный — подтверждённый эфир, синий — просто подключено */}
          <span
            className="h-1.5 w-1.5 rounded-full"
            style={{
              background: p.online ? "#4ade80" : "#60a5fa",
              boxShadow: p.online ? "0 0 6px rgba(74,222,128,0.9)" : "none",
            }}
          />
          {c.showViewers && p.online && p.viewers > 0 && (
            <span className="font-semibold tabular-nums" style={{ fontSize: "0.86em" }}>
              {p.viewers.toLocaleString("ru-RU")}
            </span>
          )}
        </span>
      ))}
    </div>
  ) : null;

  /* ---------- лента ---------- */
  const feedList = (
    <div
      className="flex min-h-0 flex-1 flex-col gap-1"
      style={{
        justifyContent: c.growth === "up" ? "flex-start" : "flex-end",
        flexDirection: c.growth === "up" ? "column-reverse" : "column",
      }}
    >
      {visible.map((m) => {
        const tint =
          m.kind === "donate" ? "#f59e0b" : m.kind === "sub" ? "#a78bfa" : m.kind === "raid" ? "#38bdf8" : m.kind === "gift" ? "#f472b6" : null;
        return (
          <div
            key={m.id}
            className="msg-anim flex items-baseline gap-1.5 px-2.5 py-1.5 leading-snug"
            style={{
              ...backdrop(tint ? 0.05 : 0),
              color: "#fff",
              textShadow,
              borderLeft: c.accentBar ? `3px solid ${tint ?? platformMeta(m.platform).color}` : "none",
            }}
          >
            {c.showTime && (
              <span className="shrink-0 tabular-nums" style={{ fontSize: "0.78em", opacity: 0.7 }}>
                {new Date(m.ts).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })}
              </span>
            )}
            {c.showPlatform && (
              <span className="flex shrink-0 translate-y-[1px]">
                <PlatformIcon id={m.platform} size={Math.round(c.fontSize * 0.85)} />
              </span>
            )}
            {c.showBadges &&
              m.badges?.slice(0, 3).map((b, i) => (
                <span key={i} className="shrink-0 rounded px-1 font-bold uppercase" style={{ fontSize: "0.62em", background: "rgba(255,255,255,0.18)" }}>
                  {b}
                </span>
              ))}
            <span className="shrink-0 font-bold" style={{ color: m.color }}>
              {m.author}
            </span>
            {m.amount !== undefined && (
              <span className="shrink-0 rounded px-1 font-bold" style={{ background: "rgba(245,158,11,0.28)", color: "#fde68a" }}>
                {m.amount.toLocaleString("ru-RU")} {m.currency}
              </span>
            )}
            <span className="min-w-0 break-words">
              {c.showEmotes
                ? parseMessage(String(m.text ?? ""), m.platform).map((part, i) =>
                    part.kind === "emote" ? (
                      <img
                        key={i}
                        src={part.url}
                        alt={`:${part.code}:`}
                        draggable={false}
                        className="mx-px inline-block align-[-0.28em]"
                        style={{ height: "1.45em", width: "auto" }}
                        onError={(e) => {
                          e.currentTarget.style.display = "none";
                        }}
                      />
                    ) : (
                      <span key={i}>{part.text}</span>
                    )
                  )
                : String(m.text ?? "")}
            </span>
          </div>
        );
      })}
    </div>
  );

  const movable = c.freePosition && !c.locked;

  return (
    <div
      className="fixed flex min-h-0 flex-col overflow-hidden p-1"
      style={{
        ...placement,
        gap: 4,
        fontSize: c.fontSize,
        opacity: c.textOpacity,
        cursor: movable ? (dragging ? "grabbing" : "grab") : "default",
        outline: movable ? "1px dashed rgba(255,255,255,0.35)" : "none",
        outlineOffset: 2,
        borderRadius: c.radius,
      }}
    >
      {/* панель статистики сверху — если выбрано */}
      {c.statsPos === "top" && statsBar}

      {feedList}

      {/* панель статистики снизу — вариант по умолчанию */}
      {c.statsPos === "bottom" && statsBar}

      {visible.length === 0 && !statsVisible && (
        <div className="px-2.5 py-1.5" style={{ ...backdrop(), color: "rgba(255,255,255,0.65)", textShadow, fontSize: "0.85em" }}>
          оверлей активен — сообщения появятся здесь
        </div>
      )}

      {movable && (
        <div
          className="pointer-events-none absolute -top-6 left-0 rounded px-2 py-0.5 text-[10px] font-semibold whitespace-nowrap"
          style={{ background: "rgba(8,10,16,0.8)", color: "#fff" }}
        >
          режим перемещения — тяните мышью, «Зафиксировать» в настройках
        </div>
      )}
    </div>
  );
}
