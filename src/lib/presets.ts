import type { ChatViewConfig, OverlayConfig, WidgetConfig } from "./types";

/**
 * СТИЛИ ОФОРМЛЕНИЯ.
 *
 * Стиль — это законченный внешний вид: шрифт, размер, интервалы, плотность,
 * скругления, подложка, полосы, регистр ников и состав строки. Пользователь
 * выбирает стиль целиком и не собирает его из десятка отдельных настроек.
 */

export interface Style<T> {
  id: string;
  name: string;
  desc: string;
  /** акцент и фон миниатюры */
  swatch: [string, string];
  /** шрифт миниатюры */
  font: string;
  patch: Partial<T>;
}

const INTER = '"Inter", sans-serif';
const MONO = '"JetBrains Mono", monospace';
const NUNITO = '"Nunito", sans-serif';
const OSWALD = '"Oswald", sans-serif';
const SERIF = "Georgia, serif";
const SYSTEM = "system-ui, sans-serif";

/* ========================= ЛЕНТА ========================= */

export const FEED_STYLES: Style<ChatViewConfig>[] = [
  {
    id: "classic",
    name: "Классика",
    desc: "тёмные плашки, всё видно",
    swatch: ["#8b5cf6", "#1c1f2e"],
    font: INTER,
    patch: {
      styleId: "classic", density: "standard", fontSize: 13, fontFamily: "inter", spacing: 6,
      radius: 10, stripe: false, separators: false, nickCase: "normal",
      backdrop: true, animations: true, showTime: true, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "cozy",
    name: "Комфорт",
    desc: "округлый шрифт, много воздуха",
    swatch: ["#f472b6", "#241c26"],
    font: NUNITO,
    patch: {
      styleId: "cozy", density: "cozy", fontSize: 15, fontFamily: "rounded", spacing: 10,
      radius: 14, stripe: false, separators: false, nickCase: "normal",
      backdrop: true, animations: true, showTime: true, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "terminal",
    name: "Терминал",
    desc: "моноширинный, без подложки",
    swatch: ["#22d3ee", "#0d1519"],
    font: MONO,
    patch: {
      styleId: "terminal", density: "compact", fontSize: 13, fontFamily: "mono", spacing: 2,
      radius: 0, stripe: false, separators: false, nickCase: "normal",
      backdrop: false, animations: false, showTime: true, showBadges: false, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "cards",
    name: "Карточки",
    desc: "крупные плашки с полосой площадки",
    swatch: ["#38bdf8", "#111a24"],
    font: INTER,
    patch: {
      styleId: "cards", density: "cozy", fontSize: 14, fontFamily: "inter", spacing: 8,
      radius: 12, stripe: true, separators: false, nickCase: "normal",
      backdrop: true, animations: true, showTime: true, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "lines",
    name: "Строки",
    desc: "линии-разделители, без плашек",
    swatch: ["#94a3b8", "#12141c"],
    font: SYSTEM,
    patch: {
      styleId: "lines", density: "compact", fontSize: 13, fontFamily: "system", spacing: 5,
      radius: 0, stripe: false, separators: true, nickCase: "normal",
      backdrop: false, animations: true, showTime: true, showBadges: false, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "dense",
    name: "Плотный",
    desc: "узкий шрифт, максимум строк",
    swatch: ["#fbbf24", "#1a1710"],
    font: OSWALD,
    patch: {
      styleId: "dense", density: "tight", fontSize: 13, fontFamily: "condensed", spacing: 1,
      radius: 0, stripe: false, separators: false, nickCase: "normal",
      backdrop: false, animations: false, showTime: false, showBadges: false, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "journal",
    name: "Журнал",
    desc: "шрифт с засечками, для чтения",
    swatch: ["#4ade80", "#101a14"],
    font: SERIF,
    patch: {
      styleId: "journal", density: "standard", fontSize: 16, fontFamily: "serif", spacing: 9,
      radius: 8, stripe: false, separators: true, nickCase: "normal",
      backdrop: false, animations: true, showTime: false, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "neon",
    name: "Неон",
    desc: "капсовые ники, кибер-подача",
    swatch: ["#e879f9", "#150d1c"],
    font: MONO,
    patch: {
      styleId: "neon", density: "standard", fontSize: 13, fontFamily: "mono", spacing: 6,
      radius: 4, stripe: true, separators: false, nickCase: "upper",
      backdrop: true, animations: true, showTime: false, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "minimal",
    name: "Минимал",
    desc: "только ник и текст",
    swatch: ["#cbd5e1", "#101218"],
    font: INTER,
    patch: {
      styleId: "minimal", density: "compact", fontSize: 13, fontFamily: "inter", spacing: 4,
      radius: 0, stripe: false, separators: false, nickCase: "normal",
      backdrop: false, animations: false, showTime: false, showBadges: false, showPlatform: false, accentNick: true,
    },
  },
  {
    id: "broadcast",
    name: "Эфир",
    desc: "крупно, для второго монитора",
    swatch: ["#fb923c", "#1c1410"],
    font: NUNITO,
    patch: {
      styleId: "broadcast", density: "standard", fontSize: 18, fontFamily: "rounded", spacing: 10,
      radius: 12, stripe: true, separators: false, nickCase: "normal",
      backdrop: true, animations: true, showTime: false, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "console",
    name: "Консоль",
    desc: "лог со временем, монохром",
    swatch: ["#a3e635", "#101408"],
    font: MONO,
    patch: {
      styleId: "console", density: "tight", fontSize: 12, fontFamily: "mono", spacing: 0,
      radius: 0, stripe: false, separators: false, nickCase: "normal",
      backdrop: false, animations: false, showTime: true, showBadges: false, showPlatform: false, accentNick: false,
    },
  },
  {
    id: "focus",
    name: "Фокус",
    desc: "спокойный, без лишних деталей",
    swatch: ["#67e8f9", "#0f1a1d"],
    font: SYSTEM,
    patch: {
      styleId: "focus", density: "cozy", fontSize: 15, fontFamily: "system", spacing: 12,
      radius: 16, stripe: false, separators: false, nickCase: "normal",
      backdrop: true, animations: true, showTime: false, showBadges: false, showPlatform: true, accentNick: true,
    },
  },
];

/* ========================= ВИДЖЕТ OBS ========================= */

export const WIDGET_STYLES: Style<WidgetConfig>[] = [
  {
    id: "transparent",
    name: "Прозрачный",
    desc: "чистый текст с обводкой",
    swatch: ["#ffffff", "transparent"],
    font: INTER,
    patch: {
      styleId: "transparent", transparent: true, outlines: true, theme: "dark",
      fontSize: 18, max: 8, fontFamily: "inter", spacing: 4, radius: 0, stripe: false, nickCase: "normal",
    },
  },
  {
    id: "dark-card",
    name: "Тёмные карточки",
    desc: "подложка под каждой строкой",
    swatch: ["#e8eaf3", "#0b0d14"],
    font: INTER,
    patch: {
      styleId: "dark-card", transparent: false, outlines: false, theme: "dark",
      fontSize: 17, max: 8, fontFamily: "inter", spacing: 6, radius: 10, stripe: false, nickCase: "normal",
    },
  },
  {
    id: "light-card",
    name: "Светлые карточки",
    desc: "для светлых сцен и IRL",
    swatch: ["#121524", "#f4f6fb"],
    font: NUNITO,
    patch: {
      styleId: "light-card", transparent: false, outlines: false, theme: "light",
      fontSize: 17, max: 8, fontFamily: "rounded", spacing: 6, radius: 12, stripe: false, nickCase: "normal",
    },
  },
  {
    id: "big",
    name: "Крупный",
    desc: "узкий шрифт, большой текст",
    swatch: ["#fbbf24", "#0b0d14"],
    font: OSWALD,
    patch: {
      styleId: "big", transparent: true, outlines: true, theme: "dark",
      fontSize: 26, max: 5, fontFamily: "condensed", spacing: 8, radius: 0, stripe: false, nickCase: "upper",
    },
  },
  {
    id: "ticker",
    name: "Лента",
    desc: "моно, много сообщений",
    swatch: ["#22d3ee", "#0b0d14"],
    font: MONO,
    patch: {
      styleId: "ticker", transparent: true, outlines: true, theme: "dark",
      fontSize: 14, max: 14, fontFamily: "mono", spacing: 2, radius: 0, stripe: false, nickCase: "normal",
    },
  },
  {
    id: "stripe",
    name: "Полосы",
    desc: "цветная полоса площадки",
    swatch: ["#a78bfa", "#0c0e18"],
    font: INTER,
    patch: {
      styleId: "stripe", transparent: false, outlines: false, theme: "dark",
      fontSize: 18, max: 7, fontFamily: "inter", spacing: 6, radius: 6, stripe: true, nickCase: "normal",
    },
  },
  {
    id: "bubbles",
    name: "Пузыри",
    desc: "округлые плашки, мягкий шрифт",
    swatch: ["#f472b6", "#150f18"],
    font: NUNITO,
    patch: {
      styleId: "bubbles", transparent: false, outlines: false, theme: "dark",
      fontSize: 18, max: 6, fontFamily: "rounded", spacing: 8, radius: 18, stripe: false, nickCase: "normal",
    },
  },
  {
    id: "book",
    name: "Книга",
    desc: "с засечками, спокойно",
    swatch: ["#fde68a", "#12100a"],
    font: SERIF,
    patch: {
      styleId: "book", transparent: true, outlines: true, theme: "dark",
      fontSize: 20, max: 6, fontFamily: "serif", spacing: 8, radius: 0, stripe: false, nickCase: "normal",
    },
  },
];

/* ========================= ОВЕРЛЕЙ ========================= */

export const OVERLAY_STYLES: Style<OverlayConfig>[] = [
  {
    id: "glass",
    name: "Стекло",
    desc: "полупрозрачная подложка",
    swatch: ["#ffffff", "rgba(8,10,16,0.55)"],
    font: INTER,
    patch: {
      styleId: "glass", backdropOpacity: 0.55, textOpacity: 1, radius: 10, accentBar: true,
      shadowText: true, fontFamily: "inter", fontSize: 14, spacing: 4, nickCase: "normal",
    },
  },
  {
    id: "ghost",
    name: "Призрак",
    desc: "без подложки, только текст",
    swatch: ["#ffffff", "transparent"],
    font: INTER,
    patch: {
      styleId: "ghost", backdropOpacity: 0, textOpacity: 1, radius: 0, accentBar: false,
      shadowText: true, fontFamily: "inter", fontSize: 15, spacing: 6, nickCase: "normal",
    },
  },
  {
    id: "hud",
    name: "HUD",
    desc: "моно, как игровой интерфейс",
    swatch: ["#22d3ee", "rgba(8,10,16,0.75)"],
    font: MONO,
    patch: {
      styleId: "hud", backdropOpacity: 0.75, textOpacity: 1, radius: 4, accentBar: true,
      shadowText: false, fontFamily: "mono", fontSize: 13, spacing: 2, nickCase: "upper",
    },
  },
  {
    id: "solid",
    name: "Плотный",
    desc: "непрозрачная панель",
    swatch: ["#ffffff", "#0a0c14"],
    font: INTER,
    patch: {
      styleId: "solid", backdropOpacity: 0.92, textOpacity: 1, radius: 12, accentBar: true,
      shadowText: false, fontFamily: "inter", fontSize: 14, spacing: 5, nickCase: "normal",
    },
  },
  {
    id: "compact",
    name: "Компакт",
    desc: "узкая полоса у края",
    swatch: ["#a78bfa", "rgba(8,10,16,0.7)"],
    font: OSWALD,
    patch: {
      styleId: "compact", backdropOpacity: 0.7, fontSize: 13, maxMessages: 4, width: 320,
      radius: 8, statsCompact: true, fontFamily: "condensed", spacing: 2, nickCase: "normal",
    },
  },
  {
    id: "events",
    name: "События",
    desc: "донаты, сабы и рейды",
    swatch: ["#f59e0b", "rgba(8,10,16,0.65)"],
    font: NUNITO,
    patch: {
      styleId: "events", backdropOpacity: 0.65, maxMessages: 5, accentBar: true,
      showDonations: true, fontFamily: "rounded", fontSize: 15, spacing: 8, nickCase: "normal",
    },
  },
  {
    id: "cinema",
    name: "Кино",
    desc: "крупно, строки исчезают",
    swatch: ["#fde68a", "rgba(8,10,16,0.45)"],
    font: SERIF,
    patch: {
      styleId: "cinema", backdropOpacity: 0.45, fontSize: 20, maxMessages: 4, autoFade: true,
      fadeAfterSec: 20, shadowText: true, fontFamily: "serif", spacing: 10, nickCase: "normal",
    },
  },
  {
    id: "bubbles",
    name: "Пузыри",
    desc: "округлые плашки",
    swatch: ["#f472b6", "rgba(8,10,16,0.6)"],
    font: NUNITO,
    patch: {
      styleId: "bubbles", backdropOpacity: 0.6, radius: 18, accentBar: false, shadowText: true,
      fontFamily: "rounded", fontSize: 15, spacing: 7, nickCase: "normal",
    },
  },
  {
    id: "stealth",
    name: "Скрытный",
    desc: "приглушённый, не мешает игре",
    swatch: ["#94a3b8", "rgba(8,10,16,0.3)"],
    font: SYSTEM,
    patch: {
      styleId: "stealth", backdropOpacity: 0.3, textOpacity: 0.75, radius: 6, accentBar: false,
      shadowText: true, fontFamily: "system", fontSize: 12, spacing: 3, nickCase: "normal",
    },
  },
  {
    id: "spotlight",
    name: "Акцент",
    desc: "яркие полосы площадок",
    swatch: ["#38bdf8", "rgba(8,10,16,0.8)"],
    font: INTER,
    patch: {
      styleId: "spotlight", backdropOpacity: 0.8, radius: 6, accentBar: true, shadowText: false,
      fontFamily: "inter", fontSize: 15, spacing: 6, nickCase: "upper",
    },
  },
];

/** активен ли стиль */
export function isStyleActive<T extends { styleId?: string }>(style: Style<T>, cfg: T): boolean {
  return cfg.styleId === style.id;
}

/** название выбранного стиля — для заголовка свёрнутого блока */
export function styleName<T>(styles: Style<T>[], id: string | undefined): string {
  return styles.find((s) => s.id === id)?.name ?? "свой";
}

/* совместимость со старыми импортами */
export const FEED_PRESETS = FEED_STYLES;
export const WIDGET_PRESETS = WIDGET_STYLES;
export const OVERLAY_PRESETS = OVERLAY_STYLES;
