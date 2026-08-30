// ─── Движок озвучки: очередь FIFO ≤12 + фильтры (ТЗ §16) ────────────────────
// В desktop-сборке синтез идёт через SAPI5 (C# TtsService, SSML ru-RU).
// В браузерной демо-версии — через Web Speech API с теми же фильтрами.
import type { ChatMsg, PlatformId, TtsConfig } from "./types";
import { stripEmotes } from "./emotes";
import { platformById } from "./types";
import { uid } from "./utils";

const EMOJI_RE =
  /[\u{1F000}-\u{1FAFF}\u{2600}-\u{27BF}\u{2190}-\u{21FF}\u{2B00}-\u{2BFF}\u{FE0F}\u{1F1E6}-\u{1F1FF}]/gu;

const SYMBOLS_RE = /[~^`|•●◦★☆♡♥✧✦♪♫█▓▒░▄▀■□▪▫►◄☼]+/g;

const COMMON_EMOTE_WORDS = /^(kappa|pog|pogchamp|lulw?|kekw|omegalul|pepega|monkas|sadge|copium|hopium|pepelaugh|ez|ggwp)$/i;

export interface FilterResult {
  ok: boolean;
  text: string;
  reason?: string;
}

/** Полный конвейер фильтрации (ТЗ §16.1, шаги 1–18). */
export class TtsFilter {
  private lastByAuthor = new Map<string, { text: string; ts: number }>();
  private window: number[] = [];

  constructor(private cfg: () => TtsConfig) {}

  process(msg: ChatMsg): FilterResult {
    const c = this.cfg();
    const f = c.filters;
    const no = (reason: string): FilterResult => ({ ok: false, text: "", reason });

    // 1. системные сообщения пропускаем
    if (msg.sys) return no("sys");
    const author = msg.author.toLowerCase();
    // 2. автор в бан-листе
    if (f.banAuthors.some((a) => a.toLowerCase() === author)) return no("banAuthor");
    // 3. белый список
    if (f.allowAuthors.length > 0 && !f.allowAuthors.some((a) => a.toLowerCase() === author))
      return no("notAllowed");
    let text = msg.text;
    // 4. слишком длинное
    if (text.length > f.maxLen) return no("maxLen");
    // 5. команды
    if (f.commands && /^[!\/]/.test(text.trim())) return no("command");
    // 6. убрать платформенные смайлы (если включён фильтр эмоутов)
    if (f.emoji) text = stripEmotes(text);
    // 7. банворды
    if (hasWord(text, f.banWords)) return no("banWord");
    // 8. ссылки → «ссылка»
    if (f.links) text = text.replace(/(https?:\/\/[^\s]+|www\.[^\s]+)/gi, "ссылка");
    // 9. эмодзи / :name: / популярные названия
    if (f.emoji) {
      text = text.replace(EMOJI_RE, " ");
      text = text.replace(/\[emote:[^\]]+\]/gi, " ");
      text = text.replace(/:([a-z0-9_]{2,24}):/gi, (m, n) => (COMMON_EMOTE_WORDS.test(n) ? " " : m));
      text = text
        .split(/\s+/)
        .filter((t) => !COMMON_EMOTE_WORDS.test(t))
        .join(" ");
    }
    // 10. мусорные символы
    if (f.stripSymbols) text = text.replace(SYMBOLS_RE, " ");
    // 11. маскировка слов на «пип»
    for (const w of f.maskWords) {
      if (!w) continue;
      text = text.replace(new RegExp(`(^|[^\\p{L}\\p{N}_])${escapeRe(w)}(?=[^\\p{L}\\p{N}_]|$)`, "giu"), "$1пип");
    }
    // 12. сжатие повторов: ааааа → аа
    if (f.squashRepeats) text = text.replace(/(.)\1{2,}/g, "$1$1");
    // 13. CAPS выше порога → lower
    const letters = text.replace(/[^\p{L}]/gu, "");
    if (letters.length >= 4) {
      const caps = (text.match(/[A-ZА-ЯЁ]/g) ?? []).length;
      if ((caps / letters.length) * 100 > f.maxCapsRatio) text = text.toLowerCase();
    }
    // 14. trim + схлопнуть пробелы
    text = text.trim().replace(/\s+/g, " ");
    // 15. повторная проверка длины
    if (text.length > f.maxLen) return no("maxLen2");
    // 16. слишком короткое
    if (text.length < f.minLen) return no("minLen");
    // 17. анти-повтор того же текста автора < 45 с
    const prev = this.lastByAuthor.get(author);
    if (f.dedupe && prev && prev.text === text && Date.now() - prev.ts < 45_000) return no("dedupe");
    // 18. лимит сообщений в минуту
    const now = Date.now();
    this.window = this.window.filter((t) => now - t < 60_000);
    if (this.window.length >= f.perMin) return no("perMin");

    this.lastByAuthor.set(author, { text, ts: now });
    this.window.push(now);
    return { ok: true, text };
  }
}

function hasWord(text: string, words: string[]): boolean {
  const low = text.toLowerCase();
  return words.some((w) => {
    if (!w) return false;
    const re = new RegExp(`(^|[^\\p{L}\\p{N}_])${escapeRe(w.toLowerCase())}(?=[^\\p{L}\\p{N}_]|$)`, "u");
    return re.test(low);
  });
}

const escapeRe = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** Собирает фразу по шаблону (автор / площадка / текст). */
export function buildPhrase(msg: ChatMsg, text: string, cfg: TtsConfig): string {
  const parts: string[] = [];
  if (cfg.template.author) parts.push(msg.author.replace(/^@/, ""));
  if (cfg.template.platform) {
    const p = platformById(msg.platform as PlatformId).name;
    parts.push(parts.length ? `с ${p}` : `Сообщение с ${p}`);
  }
  let phrase = parts.length ? parts.join(" ") : "";
  if (cfg.template.text) phrase = phrase ? `${phrase}: ${text}` : text;
  return phrase || text;
}

// ─── Очередь синтеза (FIFO, max 12, один постоянный «процесс») ─────────────

export interface SpeakItem {
  id: string;
  text: string;
  rate: number;
  volume: number;
  voice?: string;
  msg?: ChatMsg;
}

type Listener = (ev: { type: "end" | "queue"; id?: string; size?: number }) => void;

export class TtsEngine {
  private queue: SpeakItem[] = [];
  private busy = false;
  private listeners = new Set<Listener>();
  paused = false;

  on(fn: Listener): () => void {
    this.listeners.add(fn);
    return () => {
      this.listeners.delete(fn);
    };
  }
  private emit(ev: { type: "end" | "queue"; id?: string; size?: number }) {
    this.listeners.forEach((f) => f(ev));
  }

  get size() {
    return this.queue.length + (this.busy ? 1 : 0);
  }

  enqueue(item: Omit<SpeakItem, "id"> & { id?: string }) {
    const it: SpeakItem = { ...item, id: item.id ?? uid() };
    this.queue.push(it);
    // при превышении 12 — выбрасывается самый старый (ТЗ §16.2)
    while (this.queue.length > 12) this.queue.shift();
    this.emit({ type: "queue", size: this.size });
    void this.pump();
    return it.id;
  }

  skip() {
    try {
      speechSynthesis.cancel();
    } catch {}
  }

  stopAll() {
    this.queue = [];
    try {
      speechSynthesis.cancel();
    } catch {}
    this.busy = false;
    this.emit({ type: "queue", size: 0 });
  }

  private async pump() {
    if (this.busy) return;
    const item = this.queue.shift();
    if (!item) return;
    this.busy = true;
    this.emit({ type: "queue", size: this.size });
    await this.speak(item);
    this.busy = false;
    this.emit({ type: "end", id: item.id });
    this.emit({ type: "queue", size: this.size });
    void this.pump();
  }

  private speak(item: SpeakItem): Promise<void> {
    return new Promise((resolve) => {
      if (typeof window === "undefined" || !("speechSynthesis" in window)) return resolve();
      if (this.paused) {
        const t = setInterval(() => {
          if (!this.paused) {
            clearInterval(t);
            this.speak(item).then(resolve);
          }
        }, 250);
        return;
      }
      const u = new SpeechSynthesisUtterance(item.text);
      u.lang = "ru-RU"; // принудительно ru-RU, как SSML xml:lang в SAPI5
      u.rate = item.rate;
      u.volume = item.volume;
      const voices = speechSynthesis.getVoices();
      const v = voices.find((x) => x.voiceURI === item.voice || x.name === item.voice);
      if (v) u.voice = v;
      u.onend = () => resolve();
      u.onerror = () => resolve();
      speechSynthesis.speak(u);
      // страховка от зависшего onend
      setTimeout(() => resolve(), Math.max(4000, item.text.length * 220));
    });
  }
}

/** Список голосов: сначала русские (аналог RuVoiceComparer из C#). */
export function listVoices(): { name: string; voiceURI: string; lang: string }[] {
  if (typeof window === "undefined" || !("speechSynthesis" in window)) return [];
  const vs = speechSynthesis.getVoices();
  const ru = vs.filter((v) => v.lang.toLowerCase().startsWith("ru"));
  const rest = vs.filter((v) => !v.lang.toLowerCase().startsWith("ru"));
  const sort = (a: SpeechSynthesisVoice, b: SpeechSynthesisVoice) => a.name.localeCompare(b.name, "ru");
  return [...ru.sort(sort), ...rest.sort(sort)].map((v) => ({
    name: v.name,
    voiceURI: v.voiceURI,
    lang: v.lang,
  }));
}
