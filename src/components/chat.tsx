"use client";

// ─── Рендер сообщений: смайлы, бейджи, события, лента с автопрокруткой ─────
import { useEffect, useRef, useState } from "react";
import {
  ArrowDown, BadgeCheck, Coins, Crown, Gift, Heart, MessageSquareDashed,
  Rocket, Shield, Star, Zap,
} from "lucide-react";
import type { ChatMsg, ChatViewConfig, MsgPart } from "@/lib/types";
import { platformById } from "@/lib/types";
import { cn, formatTime } from "@/lib/utils";

/* Эмоут с фолбэком на текстовый токен, если CDN недоступен. */
export function Emote({ url, alt }: { url: string; alt: string }) {
  const [broken, setBroken] = useState(false);
  if (broken) return <span className="font-semibold" style={{ color: "var(--accent-2)" }}>{alt}</span>;
  // eslint-disable-next-line @next/next/no-img-element
  return <img src={url} alt={alt} title={alt} className="emote" loading="lazy" onError={() => setBroken(true)} />;
}

export function MsgParts({ parts }: { parts: MsgPart[] }) {
  return (
    <>
      {parts.map((p, i) =>
        p.t === "emote" ? (
          <Emote key={i} url={p.url} alt={p.v} />
        ) : p.t === "link" ? (
          <span key={i} className="msg-link">{p.v}</span>
        ) : (
          <span key={i} className="msg-text">{p.v}</span>
        ),
      )}
    </>
  );
}

export function PlatformBadge({ platform, withName = false, size = 15 }: { platform: ChatMsg["platform"]; withName?: boolean; size?: number }) {
  const p = platformById(platform);
  return (
    <span
      className="inline-flex flex-none items-center gap-1 rounded-md px-1.5 py-0.5 font-bold uppercase"
      style={{ background: p.soft, color: p.color, fontSize: size - 6, letterSpacing: "0.04em" }}
      title={p.name}
    >
      <span className="size-1.5 rounded-full" style={{ background: p.color }} />
      {withName ? p.name : p.short}
    </span>
  );
}

export function BadgeIcon({ badge }: { badge: string }) {
  const map: Record<string, { icon: typeof Shield; color: string; title: string }> = {
    mod: { icon: Shield, color: "#34d399", title: "Модератор" },
    sub: { icon: Star, color: "#8b7bff", title: "Подписчик" },
    vip: { icon: Crown, color: "#f472b6", title: "VIP" },
    member: { icon: BadgeCheck, color: "#38bdf8", title: "Спонсор" },
    fan: { icon: Heart, color: "#fb7185", title: "Фан-клуб" },
  };
  const b = map[badge];
  if (!b) return null;
  return <b.icon size={13} style={{ color: b.color }} aria-label={b.title} />;
}

const EVENT_STYLE: Record<string, { icon: typeof Heart; color: string; label: string }> = {
  follow: { icon: Heart, color: "#fb7185", label: "Новый фолловер" },
  sub: { icon: Gift, color: "#8b7bff", label: "Подписка" },
  raid: { icon: Rocket, color: "#38e8ff", label: "Рейд" },
  donate: { icon: Coins, color: "#fbbf24", label: "Донат" },
  info: { icon: Zap, color: "#34d399", label: "Событие" },
};

export interface MsgLook {
  style: ChatViewConfig["style"];
  fontSize: number;
  radius: number;
  showPlatform: boolean;
  showTime: boolean;
  showBadges: boolean;
}

export function ChatMessageView({ msg, look, fx, dur }: { msg: ChatMsg; look: MsgLook; fx?: string; dur?: number }) {
  const p = platformById(msg.platform);
  if (msg.sys) {
    const ev = EVENT_STYLE[msg.kind ?? "info"];
    return (
      <div
        className={cn("flex items-center gap-2.5 px-3 py-2", fx)}
        style={{
          "--dur": `${dur ?? 0.34}s`,
          borderRadius: Math.min(look.radius, 14),
          background: `color-mix(in srgb, ${ev.color} 10%, transparent)`,
          border: `1px solid color-mix(in srgb, ${ev.color} 28%, transparent)`,
          fontSize: look.fontSize - 1,
        } as React.CSSProperties}
      >
        <ev.icon size={look.fontSize} style={{ color: ev.color }} className="flex-none" />
        <span style={{ color: p.color }} className="flex-none"><PlatformBadge platform={msg.platform} size={look.fontSize - 1} /></span>
        <span className="font-semibold" style={{ color: ev.color }}>{msg.author}</span>
        <span style={{ color: "var(--muted)" }}>{msg.text}</span>
        {look.showTime && <span className="ml-auto flex-none text-[11px] tabular-nums" style={{ color: "var(--muted)" }}>{formatTime(msg.ts)}</span>}
      </div>
    );
  }

  const nameEl = (
    <span className="font-bold" style={{ color: msg.color }}>
      {msg.author}
    </span>
  );
  const bodyEl = (
    <span style={{ color: "var(--text)" }}>
      <MsgParts parts={msg.parts} />
    </span>
  );

  if (look.style === "compact") {
    return (
      <div className={cn("flex items-baseline gap-2 px-2 py-1 leading-snug", fx)} style={{ "--dur": `${dur ?? 0.34}s`, fontSize: look.fontSize } as React.CSSProperties}>
        {look.showTime && <span className="flex-none text-[11px] tabular-nums" style={{ color: "var(--muted)" }}>{formatTime(msg.ts)}</span>}
        {look.showPlatform && <span className="relative top-[1px] flex-none"><PlatformBadge platform={msg.platform} size={look.fontSize - 1} /></span>}
        {look.showBadges && msg.badges.map((b) => <BadgeIcon key={b} badge={b} />)}
        {nameEl}
        <span style={{ color: "var(--muted)" }} className="flex-none">·</span>
        {bodyEl}
      </div>
    );
  }

  const isCards = look.style === "cards";
  return (
    <div
      className={cn("px-3 py-2 leading-snug", fx)}
      style={{
        "--dur": `${dur ?? 0.34}s`,
        fontSize: look.fontSize,
        borderRadius: look.radius,
        background: isCards ? "var(--panel-2)" : "var(--panel)",
        border: `1px solid ${isCards ? "var(--border-2)" : "var(--border)"}`,
      } as React.CSSProperties}
    >
      <div className="flex items-center gap-2">
        {look.showPlatform && <PlatformBadge platform={msg.platform} size={look.fontSize - 1} />}
        {look.showBadges && msg.badges.map((b) => <BadgeIcon key={b} badge={b} />)}
        {nameEl}
        {look.showTime && <span className="ml-auto flex-none text-[11px] tabular-nums" style={{ color: "var(--muted)" }}>{formatTime(msg.ts)}</span>}
      </div>
      <div className="mt-0.5">{bodyEl}</div>
    </div>
  );
}

export function lookFromChatView(cv: ChatViewConfig): MsgLook {
  return {
    style: cv.style,
    fontSize: cv.fontSize,
    radius: cv.radius,
    showPlatform: cv.showPlatform,
    showTime: cv.showTime,
    showBadges: cv.showBadges,
  };
}

/** Лента сообщений с индикатором автопрокрутки (ТЗ §14.4). */
export function ChatFeed({
  messages,
  look,
  fx,
  dur,
  rowGap = 6,
  height,
  empty,
}: {
  messages: ChatMsg[];
  look: MsgLook;
  fx?: string;
  dur?: number;
  rowGap?: number;
  height?: number | string;
  empty?: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [stuck, setStuck] = useState(true);

  useEffect(() => {
    const el = ref.current;
    if (el && stuck) el.scrollTop = el.scrollHeight;
  }, [messages, stuck]);

  const onScroll = () => {
    const el = ref.current;
    if (!el) return;
    const dist = el.scrollHeight - el.scrollTop - el.clientHeight;
    if (dist <= 48 && !stuck) setStuck(true);
    else if (dist > 48 && stuck) setStuck(false);
  };

  return (
    <div className="relative min-h-0 flex-1" style={height ? { height, flex: "none" } : undefined}>
      <div
        ref={ref}
        onScroll={onScroll}
        className="flex h-full flex-col overflow-y-auto overscroll-contain px-1"
        style={{ gap: rowGap, scrollbarGutter: "stable" }}
      >
        {messages.length === 0 && empty !== "" && (
          <div className="grid h-full place-items-center py-10 text-center">
            <div>
              <MessageSquareDashed size={30} className="mx-auto mb-2 opacity-40" />
              <div className="text-sm" style={{ color: "var(--muted)" }}>{empty ?? "Пока тихо. Сообщения появятся здесь."}</div>
            </div>
          </div>
        )}
        {messages.map((m) => (
          <ChatMessageView key={m.id} msg={m} look={look} fx={fx} dur={dur} />
        ))}
      </div>
      {!stuck && (
        <button
          type="button"
          onClick={() => {
            setStuck(true);
            const el = ref.current;
            if (el) el.scrollTo({ top: el.scrollHeight, behavior: "smooth" });
          }}
          className="toast-item absolute bottom-3 left-1/2 z-10 flex -translate-x-1/2 items-center gap-2 px-4 py-2 text-xs font-semibold"
          style={{ color: "var(--text)" }}
        >
          <ArrowDown size={13} style={{ color: "var(--accent)" }} />
          Новые сообщения
        </button>
      )}
    </div>
  );
}
