"use client";

// ─── Строка сообщения виджета/оверлея — единый рендер для превью и рантайма ─
import { MsgParts, PlatformBadge, BadgeIcon } from "./chat";
import { platformById, type ChatMsg } from "@/lib/types";
import { cn, formatTime } from "@/lib/utils";

export interface RowStyleOpts {
  style: "clean" | "compact" | "bubbles";
  showPlatform: boolean;
  showTime: boolean;
  showBadges?: boolean;
  dir?: "up" | "down";
}

export function WidgetMsgRow({
  msg,
  fx,
  opts,
}: {
  msg: ChatMsg;
  fx?: string;
  opts: RowStyleOpts;
}) {
  const p = platformById(msg.platform);
  const time = opts.showTime ? (
    <span className="flex-none text-[.68em] tabular-nums opacity-50" style={{ color: "var(--w-text)" }}>
      {formatTime(msg.ts)}
    </span>
  ) : null;

  if (msg.sys) {
    return (
      <div
        className={cn("flex items-center gap-2 px-3 py-1.5", fx)}
        style={{
          borderRadius: "var(--w-radius)",
          background: "color-mix(in srgb, var(--w-name) 12%, var(--w-bg))",
          border: "var(--w-border-w) solid color-mix(in srgb, var(--w-name) 30%, transparent)",
          boxShadow: "var(--w-shadow)",
          fontSize: "calc(var(--w-font) * .9)",
          color: "var(--w-text)",
        }}
      >
        <span className="font-bold" style={{ color: "var(--w-name)" }}>{msg.author}</span>
        <span className="opacity-80">{msg.text}</span>
        {time && <span className="ml-auto">{time}</span>}
      </div>
    );
  }

  const badges = (opts.showBadges ?? true) ? msg.badges.map((b) => <BadgeIcon key={b} badge={b} />) : null;

  if (opts.style === "compact") {
    return (
      <div
        className={cn("flex items-baseline gap-[.5em] px-[.7em] py-[.3em] leading-snug", fx)}
        style={{
          borderRadius: "var(--w-radius)",
          background: "var(--w-bg)",
          backgroundImage: "var(--w-bg-image, none)",
          backgroundSize: "cover",
          border: "var(--w-border-w) solid var(--w-border)",
          fontSize: "var(--w-font)",
        }}
      >
        {opts.showPlatform && <span className="relative top-[2px] flex-none"><PlatformBadge platform={msg.platform} size={11} /></span>}
        <span className="flex-none font-bold" style={{ color: "var(--w-name)" }}>{msg.author}</span>
        <span className="msg-text min-w-0" style={{ color: "var(--w-text)" }}><MsgParts parts={msg.parts} /></span>
        {time && <span className="ml-auto">{time}</span>}
      </div>
    );
  }

  if (opts.style === "bubbles") {
    return (
      <div className={cn("flex flex-col gap-[2px]", fx)} style={{ fontSize: "var(--w-font)" }}>
        <div className="flex items-center gap-[.4em] px-[.35em]">
          {opts.showPlatform && <PlatformBadge platform={msg.platform} size={10} />}
          {badges}
          <span className="text-[.8em] font-bold drop-shadow" style={{ color: "var(--w-name)", textShadow: "0 1px 6px rgba(0,0,0,.5)" }}>{msg.author}</span>
          {time}
        </div>
        <div
          className="w-fit max-w-full px-[.8em] py-[.45em] leading-snug"
          style={{
            borderRadius: "var(--w-radius)",
            borderTopLeftRadius: "calc(var(--w-radius) / 3)",
            background: "var(--w-bg)",
            backgroundImage: "var(--w-bg-image, none)",
            backgroundSize: "cover",
            border: "var(--w-border-w) solid var(--w-border)",
            boxShadow: "var(--w-shadow)",
            color: "var(--w-text)",
          }}
        >
          <MsgParts parts={msg.parts} />
        </div>
      </div>
    );
  }

  // clean
  return (
    <div
      className={cn("px-[.8em] py-[.5em] leading-snug", fx)}
      style={{
        borderRadius: "var(--w-radius)",
        background: "var(--w-bg)",
        backgroundImage: "var(--w-bg-image, none)",
        backgroundSize: "cover",
        border: "var(--w-border-w) solid var(--w-border)",
        boxShadow: "var(--w-shadow)",
        fontSize: "var(--w-font)",
      }}
    >
      <div className="flex items-baseline gap-[.45em]">
        {opts.showPlatform && <span className="relative top-[1px] flex-none"><PlatformBadge platform={msg.platform} size={11} /></span>}
        {badges}
        <span className="font-bold" style={{ color: "var(--w-name)", textShadow: `0 0 14px color-mix(in srgb, ${p.color} 45%, transparent)` }}>{msg.author}</span>
        {time && <span className="ml-auto">{time}</span>}
      </div>
      <div className="mt-[1px]" style={{ color: "var(--w-text)" }}>
        <MsgParts parts={msg.parts} />
      </div>
    </div>
  );
}
