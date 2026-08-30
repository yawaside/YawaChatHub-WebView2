// ─── Смайлы площадок: нативные Twitch + BTTV/7TV (ТЗ §7) ────────────────────
import type { MsgPart } from "./types";

interface EmoteDef {
  code: string;
  url: string;
  src: "twitch" | "bttv" | "7tv";
}

const tw = (id: string) => `https://static-cdn.jtvnw.net/emoticons/v2/${id}/default/dark/2.0`;
const bttv = (id: string) => `https://cdn.betterttv.net/emote/${id}/2x`;

export const EMOTES: EmoteDef[] = [
  { code: "Kappa", url: tw("25"), src: "twitch" },
  { code: "4Head", url: tw("354"), src: "twitch" },
  { code: "Kreygasm", url: tw("41"), src: "twitch" },
  { code: "BibleThump", url: tw("86"), src: "twitch" },
  { code: "DansGame", url: tw("33"), src: "twitch" },
  { code: "SwiftRage", url: tw("34"), src: "twitch" },
  { code: "FailFish", url: tw("360"), src: "twitch" },
  { code: "NotLikeThis", url: tw("58765"), src: "twitch" },
  { code: "LUL", url: tw("425618"), src: "twitch" },
  { code: "Jebaited", url: tw("114836"), src: "twitch" },
  { code: "PJSalt", url: tw("36"), src: "twitch" },
  { code: "monkaS", url: bttv("56e9f494fff3cc5c35e5287e"), src: "bttv" },
  { code: "FeelsBadMan", url: bttv("566c9fde65dbbdab32ec053e"), src: "bttv" },
  { code: "PepeHands", url: bttv("5a3d2f0aa8c3f026fd24e5e6"), src: "bttv" },
  { code: "EZ", url: bttv("5590b223b344e2c42a9e28e3"), src: "bttv" },
];

const EMOTE_MAP = new Map(EMOTES.map((e) => [e.code, e]));

const LINK_RE = /(https?:\/\/[^\s]+|www\.[^\s]+)/gi;

/** Разбивает текст на части: текст / ссылки / смайлы (точное совпадение токена). */
export function parseParts(text: string): MsgPart[] {
  const tokens = text.split(/(\s+)/).filter((t) => t.length > 0);
  const parts: MsgPart[] = [];
  const pushText = (v: string) => {
    const last = parts[parts.length - 1];
    if (last && last.t === "text") last.v += v;
    else parts.push({ t: "text", v });
  };
  for (const tok of tokens) {
    const em = EMOTE_MAP.get(tok);
    if (em) {
      parts.push({ t: "emote", v: em.code, url: em.url });
      continue;
    }
    LINK_RE.lastIndex = 0;
    if (LINK_RE.test(tok)) {
      parts.push({ t: "link", v: tok });
      continue;
    }
    pushText(tok);
  }
  return parts;
}

/** Убирает платформенные смайлы из текста (для озвучки, ТЗ §16 шаг 6). */
export function stripEmotes(text: string): string {
  return text
    .split(/\s+/)
    .filter((t) => !EMOTE_MAP.has(t))
    .join(" ");
}

export const EMOTE_CODES = EMOTES.map((e) => e.code);
