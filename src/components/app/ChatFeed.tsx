import { useEffect, useMemo, useRef, useState } from "react";
import {
  ArrowDownToLine, Ban, Crown, Eraser, Gavel, Gift, HandCoins, Heart,
  MessageSquareX, Search, Shield, Star, Timer, UserX, Zap,
} from "lucide-react";
import type { ChatMsg, ChatViewConfig, Channel, ModerationRequest, PlatformId } from "../../lib/types";
import { PlatformIcon, platformMeta } from "../../lib/platforms";
import { isStickerOnly, parseMessage } from "../../lib/emotes";
import { IconBtn } from "./ui";

const BADGE_ICONS: Record<string, { icon: React.ReactNode; color: string }> = {
  mod: { icon: <Shield size={10} />, color: "#34d399" },
  sub: { icon: <Star size={10} />, color: "#a78bfa" },
  vip: { icon: <Heart size={10} />, color: "#f472b6" },
  turbo: { icon: <Zap size={10} />, color: "#fbbf24" },
};

const KIND_STYLE: Record<string, { icon: React.ReactNode; tint: string }> = {
  donate: { icon: <HandCoins size={11} />, tint: "#f59e0b" },
  sub: { icon: <Crown size={11} />, tint: "#a78bfa" },
  gift: { icon: <Gift size={11} />, tint: "#f472b6" },
  raid: { icon: <Zap size={11} />, tint: "#38bdf8" },
};

interface Props {
  feed: ChatMsg[];
  channels: Channel[];
  view: ChatViewConfig;
  maxFeed: number;
  hideDeleted?: boolean;
  mentionHL?: boolean;
  onModerate: (req: ModerationRequest) => void;
  onClear: () => void;
}

export default function ChatFeed({ feed, channels, view, maxFeed, hideDeleted = false, mentionHL = true, onModerate, onClear }: Props) {
  const [query, setQuery] = useState("");
  const [platforms, setPlatforms] = useState<Set<PlatformId>>(new Set());
  const [pinned, setPinned] = useState(true);
  const [atBottom, setAtBottom] = useState(true);
  const scrollRef = useRef<HTMLDivElement>(null);

  const visible = useMemo(() => {
    let out = feed;
    if (hideDeleted) out = out.filter((m) => !m.deleted);
    if (platforms.size) out = out.filter((m) => m.kind === "system" || platforms.has(m.platform));
    const q = query.trim().toLowerCase();
    if (q)
      out = out.filter(
        (m) =>
          String(m.text ?? "").toLowerCase().includes(q) ||
          String(m.author ?? "").toLowerCase().includes(q)
      );
    return out.slice(-maxFeed);
  }, [feed, query, platforms, maxFeed, hideDeleted]);

  /* автопрокрутка: зависим от id последнего сообщения — при лимите ленты
     длина массива не меняется и скролл должен срабатывать всё равно */
  const lastId = visible.length ? visible[visible.length - 1].id : 0;
  useEffect(() => {
    const el = scrollRef.current;
    if (!el || !pinned || !atBottom) return;
    requestAnimationFrame(() => {
      el.scrollTop = el.scrollHeight;
    });
  }, [lastId, pinned, atBottom]);

  const onScroll = () => {
    const el = scrollRef.current;
    if (!el) return;
    setAtBottom(el.scrollHeight - el.scrollTop - el.clientHeight < 60);
  };

  const onlineSet = useMemo(
    () => new Set(channels.filter((c) => c.status === "online").map((c) => c.platform)),
    [channels]
  );

  const togglePlatform = (id: PlatformId) => {
    setPlatforms((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const fmtTime = (ts: number) =>
    new Date(ts).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit", second: "2-digit" });

  return (
    <div className="flex h-full min-h-0 flex-col gap-2">
      {/* панель инструментов ленты */}
      <div
        className="flex items-center gap-1.5 rounded-xl border px-2 py-1.5"
        style={{ background: "var(--dw-panel)", borderColor: "var(--dw-line)" }}
      >
        <div className="relative min-w-32 flex-1">
          <Search
            size={13}
            className="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2"
            style={{ color: "var(--dw-dim)" }}
          />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Поиск по ленте…"
            className="w-full rounded-lg border border-transparent py-1.5 pr-2 pl-7.5 text-[12px] outline-none focus:border-[var(--dw-accent)]"
            style={{ background: "var(--dw-input)", color: "var(--dw-text)" }}
          />
        </div>
        <div className="mx-1 hidden items-center gap-1 md:flex">
          {(["twitch", "youtube", "vkplay", "kick", "tiktok"] as PlatformId[]).map((id) => {
            const on = platforms.size === 0 || platforms.has(id);
            const live = onlineSet.has(id);
            return (
              <button
                key={id}
                onClick={() => togglePlatform(id)}
                title={platformMeta(id).name}
                className="flex h-7 w-7 cursor-pointer items-center justify-center rounded-lg transition-all"
                style={{
                  background: on ? "var(--dw-input)" : "transparent",
                  opacity: on ? 1 : 0.35,
                  outline: on && platforms.size ? `1px solid ${platformMeta(id).color}55` : "none",
                }}
              >
                <span className="relative">
                  <PlatformIcon id={id} size={14} />
                  {live && (
                    <span className="absolute -top-1 -right-1 h-1.5 w-1.5 rounded-full bg-emerald-400" />
                  )}
                </span>
              </button>
            );
          })}
        </div>
        <div className="ml-auto flex items-center gap-1">
          <IconBtn
            icon={<ArrowDownToLine size={14} />}
            title={pinned ? "Автопрокрутка включена" : "Автопрокрутка выключена"}
            active={pinned}
            onClick={() => setPinned(!pinned)}
          />
          <IconBtn icon={<Eraser size={14} />} title="Очистить ленту" onClick={onClear} />
        </div>
      </div>

      {/* лента */}
      <div
        ref={scrollRef}
        onScroll={onScroll}
        className={`min-h-0 flex-1 overflow-y-auto px-1 py-1 ${
          view.density === "cozy"
            ? "feed-cozy"
            : view.density === "compact"
              ? "feed-compact"
              : view.density === "tight"
                ? "feed-tight"
                : "feed-standard"
        }`}
        style={{ fontSize: view.fontSize }}
      >
        {visible.length === 0 && (
          <div className="flex h-full flex-col items-center justify-center gap-2 opacity-50">
            <MessageSquareX size={28} style={{ color: "var(--dw-dim)" }} />
            <p className="text-[12px]" style={{ color: "var(--dw-dim)" }}>
              Лента пуста — подключите канал слева
            </p>
          </div>
        )}
        {visible.map((m, i) => {
          const anim = view.animations && i >= visible.length - 12;
          if (m.kind === "system" || m.kind === "status") {
            return (
              <div
                key={m.id}
                className={`dw-msg flex items-center gap-2 justify-center text-center ${anim ? "msg-anim" : ""}`}
                style={{ color: "var(--dw-dim)", fontSize: "0.85em" }}
              >
                <span className="h-px w-8" style={{ background: "var(--dw-line)" }} />
                {m.text}
                <span className="h-px w-8" style={{ background: "var(--dw-line)" }} />
              </div>
            );
          }
          const ks = KIND_STYLE[m.kind];
          const isEvent = !!ks || m.kind === "command";
          const mentioned = mentionHL && !m.deleted && m.kind === "chat" && /@/.test(m.text);
          const sticker = !m.deleted && m.kind === "chat" && isStickerOnly(m.text, m.platform);
          return (
            <div
              key={m.id}
              className={`dw-msg group selectable leading-snug ${anim ? "msg-anim" : ""}`}
              style={{
                background: m.deleted
                  ? "transparent"
                  : mentioned
                    ? "color-mix(in srgb, var(--dw-accent) 10%, transparent)"
                    : view.backdrop
                      ? isEvent
                        ? `color-mix(in srgb, ${ks?.tint ?? "#f59e0b"} ${m.kind === "command" ? 6 : 9}%, transparent)`
                        : "var(--dw-panel)"
                      : "transparent",
                borderLeft:
                  isEvent && !m.deleted
                    ? `2px solid ${ks?.tint ?? "var(--dw-accent)"}`
                    : mentioned
                      ? "2px solid var(--dw-accent)"
                      : "2px solid transparent",
                opacity: m.deleted ? 0.45 : 1,
              }}
            >
              <div className="flex items-baseline gap-1.5" style={{ textDecoration: m.deleted ? "line-through" : "none" }}>
                {view.showTime && (
                  <span className="shrink-0 text-[0.78em] tabular-nums" style={{ color: "var(--dw-dim)" }}>
                    {fmtTime(m.ts)}
                  </span>
                )}
                {ks && <span className="flex shrink-0 translate-y-[1.5px]" style={{ color: ks.tint }}>{ks.icon}</span>}
                {view.showPlatform && (
                  <span className="flex shrink-0 translate-y-[1.5px]">
                    <PlatformIcon id={m.platform} size={12} />
                  </span>
                )}
                {view.showBadges &&
                  !m.deleted &&
                  m.badges?.map((b, j) => {
                    const bi = BADGE_ICONS[b];
                    if (!bi) return null;
                    return (
                      <span
                        key={j}
                        className="flex h-[14px] w-[14px] shrink-0 translate-y-[2px] items-center justify-center rounded"
                        style={{ background: `${bi.color}26`, color: bi.color }}
                      >
                        {bi.icon}
                      </span>
                    );
                  })}
                <button
                  className="shrink-0 cursor-pointer font-semibold hover:underline"
                  style={{ color: view.accentNick ? m.color : "var(--dw-text)" }}
                  onClick={(e) => {
                    const r = (e.target as HTMLElement).getBoundingClientRect();
                    onModerate({ msg: m, x: r.left, y: r.bottom + 4 });
                  }}
                  title="Меню модерации"
                >
                  {m.author}
                </button>
                {m.amount !== undefined && (
                  <span className="shrink-0 rounded px-1 py-px text-[0.85em] font-bold" style={{ background: "#f59e0b22", color: "#fbbf24" }}>
                    {m.amount.toLocaleString("ru-RU")} {m.currency}
                  </span>
                )}
                <span className="min-w-0 break-words" style={{ color: "var(--dw-text)" }}>
                  {m.deleted ? "сообщение удалено" : <MsgText text={m.text} platform={m.platform} sticker={sticker} />}
                </span>
              </div>
            </div>
          );
        })}
      </div>

      {!atBottom && pinned && (
        <button
          onClick={() => {
            const el = scrollRef.current;
            if (el) el.scrollTop = el.scrollHeight;
            setAtBottom(true);
          }}
          className="pop-anim absolute bottom-4 left-1/2 -translate-x-1/2 cursor-pointer rounded-full px-3 py-1 text-[11px] font-medium shadow-lg"
          style={{ background: "var(--dw-accent)", color: "#fff" }}
        >
          ↓ к новым сообщениям
        </button>
      )}
    </div>
  );
}

/* ---------- текст сообщения с эмоутами/стикерами площадки ---------- */
function MsgText({ text, platform, sticker }: { text: string; platform: PlatformId; sticker?: boolean }) {
  // если картинка эмоута не загрузилась — показываем его код текстом.
  // ВАЖНО: только через состояние React. Прямая мутация DOM (replaceWith)
  // ломала дерево и роняла всё окно в белый экран.
  const [broken, setBroken] = useState<Record<string, boolean>>({});
  const parts = parseMessage(String(text ?? ""), platform);

  return (
    <>
      {parts.map((p, i) =>
        p.kind === "emote" ? (
          broken[p.url] ? (
            <span key={i}>:{p.code}:</span>
          ) : (
            <img
              key={i}
              src={p.url}
              alt={`:${p.code}:`}
              title={`:${p.code}:`}
              loading="lazy"
              draggable={false}
              className="mx-px inline-block align-[-0.28em] select-none"
              style={{ height: sticker ? "2.3em" : "1.45em", width: "auto" }}
              onError={() => setBroken((prev) => (prev[p.url] ? prev : { ...prev, [p.url]: true }))}
            />
          )
        ) : (
          <span key={i}>{p.text}</span>
        )
      )}
    </>
  );
}

/* ---------- всплывающее меню модерации ---------- */
export function ModerationMenu({
  req,
  onAction,
  onClose,
}: {
  req: ModerationRequest;
  onAction: (action: string, msg: ChatMsg, arg?: number) => void;
  onClose: () => void;
}) {
  const { msg } = req;
  const canModerate = msg.platform === "twitch" || msg.platform === "vkplay";
  const canModes = msg.platform === "twitch";

  useEffect(() => {
    const close = () => onClose();
    window.addEventListener("mousedown", close);
    window.addEventListener("blur", close);
    return () => {
      window.removeEventListener("mousedown", close);
      window.removeEventListener("blur", close);
    };
  }, [onClose]);

  const w = 218;
  const x = Math.min(req.x, window.innerWidth - w - 10);
  const y = Math.min(req.y, window.innerHeight - 320);

  const Item = ({
    icon,
    label,
    danger,
    onClick,
  }: {
    icon: React.ReactNode;
    label: string;
    danger?: boolean;
    onClick?: () => void;
  }) => (
    <button
      onMouseDown={(e) => e.stopPropagation()}
      onClick={onClick}
      className="flex w-full cursor-pointer items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-[12px] transition-colors hover:bg-[var(--dw-hover)]"
      style={{ color: danger ? "#f87171" : "var(--dw-text)" }}
    >
      <span style={{ color: danger ? "#f87171" : "var(--dw-dim)" }}>{icon}</span>
      {label}
    </button>
  );

  return (
    <div
      className="pop-anim fixed z-[90] rounded-xl border p-1.5 shadow-2xl"
      style={{ background: "var(--dw-panel2)", borderColor: "var(--dw-line)", width: w, left: x, top: y }}
      onMouseDown={(e) => e.stopPropagation()}
    >
      <div className="flex items-center gap-2 px-2.5 py-1.5">
        <span className="h-2 w-2 rounded-full" style={{ background: msg.color }} />
        <span className="truncate text-[12px] font-semibold" style={{ color: "var(--dw-text)" }}>
          {msg.author}
        </span>
        <span className="ml-auto opacity-70"><PlatformIcon id={msg.platform} size={11} /></span>
      </div>
      {canModerate ? (
        <>
          <div className="px-2.5 pt-1 pb-0.5 text-[10px] font-semibold tracking-wider uppercase" style={{ color: "var(--dw-dim)" }}>
            Таймаут
          </div>
          <div className="flex gap-1 px-1 pb-1">
            {[
              ["60с", 60],
              ["10м", 600],
              ["1ч", 3600],
            ].map(([l, s]) => (
              <button
                key={String(l)}
                onMouseDown={(e) => e.stopPropagation()}
                onClick={() => onAction("timeout", msg, s as number)}
                className="flex-1 cursor-pointer rounded-lg py-1 text-[11px] font-medium transition-colors hover:bg-[var(--dw-hover)]"
                style={{ background: "var(--dw-input)", color: "var(--dw-text)" }}
              >
                <Timer size={10} className="mr-0.5 inline" /> {l}
              </button>
            ))}
          </div>
          <Item icon={<Ban size={13} />} label="Забанить" danger onClick={() => onAction("ban", msg)} />
          <Item icon={<UserX size={13} />} label="Разбанить" onClick={() => onAction("unban", msg)} />
          <Item icon={<Gavel size={13} />} label="Удалить сообщение" danger onClick={() => onAction("delete", msg)} />
          {canModes && (
            <>
              <div className="mt-1 border-t px-2.5 pt-1.5 pb-0.5 text-[10px] font-semibold tracking-wider uppercase" style={{ borderColor: "var(--dw-line)", color: "var(--dw-dim)" }}>
                Режимы чата
              </div>
              <Item icon={<Timer size={13} />} label="Медленный режим" onClick={() => onAction("mode-slow", msg)} />
              <Item icon={<Zap size={13} />} label="Только смайлы" onClick={() => onAction("mode-emote", msg)} />
              <Item icon={<Heart size={13} />} label="Только подписчики" onClick={() => onAction("mode-followers", msg)} />
              <Item icon={<Crown size={13} />} label="Только сабы" onClick={() => onAction("mode-subs", msg)} />
              <Item icon={<Eraser size={13} />} label="Очистить чат" danger onClick={() => onAction("mode-clear", msg)} />
            </>
          )}
        </>
      ) : (
        <p className="px-2.5 py-2 text-[11.5px] leading-snug" style={{ color: "var(--dw-dim)" }}>
          Меню модерации доступно только для сообщений Twitch и VK Видео Live
        </p>
      )}
    </div>
  );
}
