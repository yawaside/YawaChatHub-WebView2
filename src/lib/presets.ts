import type { ChatViewConfig, OverlayConfig, WidgetConfig } from "./types";

/**
 * СТИЛИ ОФОРМЛЕНИЯ — внешний вид по мотивам внутриигровых чатов.
 *
 * ВАЖНО: стиль отвечает ТОЛЬКО за внешний вид — шрифт, размер, плотность,
 * скругления, подложку, полосы и регистр ников. Стиль НЕ трогает:
 *   • эффекты появления (effect / effectSpeed),
 *   • содержимое строки (время, значки, иконка площадки, ники),
 *   • исчезновение сообщений (autoFade / fadeAfterSec),
 *   • поведение ленты и панель статистики.
 * Эти группы настраиваются отдельно и не сбрасываются при смене стиля.
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
    id: "rust",
    name: "Rust",
    desc: "как в игре: без плашек, узкий шрифт, цветные ники",
    swatch: ["#cd412b", "transparent"],
    font: OSWALD,
    patch: {
      // В Rust чат — просто строки поверх экрана: подложки нет,
      // шрифт узкий (Roboto Condensed), ники выделены цветом.
      styleId: "rust", density: "tight", fontSize: 14, fontFamily: "condensed", spacing: 1,
      radius: 0, stripe: false, separators: false, nickCase: "normal", backdrop: false,
    },
  },
  {
    id: "wot",
    name: "World of Tanks",
    desc: "техничный: узкий шрифт, полосы команд",
    swatch: ["#f2a900", "#14171c"],
    font: OSWALD,
    patch: {
      styleId: "wot", density: "compact", fontSize: 14, fontFamily: "condensed", spacing: 3,
      radius: 2, stripe: true, separators: false, nickCase: "upper", backdrop: true,
    },
  },
  {
    id: "cs",
    name: "Counter-Strike",
    desc: "консольный: моно, разделители строк",
    swatch: ["#5e98d9", "#1b2838"],
    font: MONO,
    patch: {
      styleId: "cs", density: "tight", fontSize: 13, fontFamily: "mono", spacing: 1,
      radius: 0, stripe: false, separators: true, nickCase: "normal", backdrop: false,
    },
  },
  {
    id: "minecraft",
    name: "Minecraft",
    desc: "пиксельный дух: тёмная плашка, без скруглений",
    swatch: ["#7ee787", "#101410"],
    font: MONO,
    patch: {
      styleId: "minecraft", density: "compact", fontSize: 14, fontFamily: "mono", spacing: 2,
      radius: 0, stripe: false, separators: false, nickCase: "normal", backdrop: true,
    },
  },
  {
    id: "gta",
    name: "GTA Online",
    desc: "крупный курсивный акцент, широкие строки",
    swatch: ["#f5a623", "#161a12"],
    font: OSWALD,
    patch: {
      styleId: "gta", density: "standard", fontSize: 15, fontFamily: "condensed", spacing: 5,
      radius: 4, stripe: true, separators: false, nickCase: "upper", backdrop: true,
    },
  },
  {
    id: "wow",
    name: "World of Warcraft",
    desc: "фэнтези: серифный шрифт, спокойные строки",
    swatch: ["#ffd100", "#12100a"],
    font: SERIF,
    patch: {
      styleId: "wow", density: "standard", fontSize: 14, fontFamily: "serif", spacing: 4,
      radius: 3, stripe: false, separators: true, nickCase: "normal", backdrop: true,
    },
  },
  {
    id: "valorant",
    name: "Valorant",
    desc: "чистый современный HUD, скруглённые плашки",
    swatch: ["#ff4655", "#0f1923"],
    font: INTER,
    patch: {
      styleId: "valorant", density: "standard", fontSize: 14, fontFamily: "inter", spacing: 6,
      radius: 8, stripe: true, separators: false, nickCase: "normal", backdrop: true,
    },
  },
  {
    id: "cozy",
    name: "Мягкий",
    desc: "округлый шрифт, много воздуха",
    swatch: ["#f472b6", "#241c26"],
    font: NUNITO,
    patch: {
      styleId: "cozy", density: "cozy", fontSize: 15, fontFamily: "rounded", spacing: 10,
      radius: 14, stripe: false, separators: false, nickCase: "normal", backdrop: true,
    },
  },
];

/* ========================= ВИДЖЕТ OBS ========================= */

export const WIDGET_STYLES: Style<WidgetConfig>[] = [
  {
    id: "rust",
    name: "Rust",
    desc: "как в игре: без фона, узкий шрифт с обводкой",
    swatch: ["#cd412b", "transparent"],
    font: OSWALD,
    patch: {
      styleId: "rust", transparent: true, outlines: true, theme: "dark",
      fontSize: 19, fontFamily: "condensed", spacing: 1, radius: 0, stripe: false, nickCase: "normal",
    },
  },
  {
    id: "wot",
    name: "World of Tanks",
    desc: "узкий шрифт, цветная полоса",
    swatch: ["#f2a900", "#14171c"],
    font: OSWALD,
    patch: {
      styleId: "wot", transparent: false, outlines: true, theme: "dark",
      fontSize: 19, fontFamily: "condensed", spacing: 3, radius: 2, stripe: true, nickCase: "upper",
    },
  },
  {
    id: "cs",
    name: "Counter-Strike",
    desc: "консольный вид, плотные строки",
    swatch: ["#5e98d9", "transparent"],
    font: MONO,
    patch: {
      styleId: "cs", transparent: true, outlines: true, theme: "dark",
      fontSize: 17, fontFamily: "mono", spacing: 1, radius: 0, stripe: false, nickCase: "normal",
    },
  },
  {
    id: "minecraft",
    name: "Minecraft",
    desc: "тёмная плашка без скруглений",
    swatch: ["#7ee787", "#101410"],
    font: MONO,
    patch: {
      styleId: "minecraft", transparent: false, outlines: false, theme: "dark",
      fontSize: 18, fontFamily: "mono", spacing: 2, radius: 0, stripe: false, nickCase: "normal",
    },
  },
  {
    id: "valorant",
    name: "Valorant",
    desc: "современный HUD, мягкие углы",
    swatch: ["#ff4655", "#0f1923"],
    font: INTER,
    patch: {
      styleId: "valorant", transparent: false, outlines: false, theme: "dark",
      fontSize: 18, fontFamily: "inter", spacing: 6, radius: 10, stripe: true, nickCase: "normal",
    },
  },
  {
    id: "big",
    name: "Крупный",
    desc: "большой текст для дальнего экрана",
    swatch: ["#fbbf24", "transparent"],
    font: OSWALD,
    patch: {
      styleId: "big", transparent: true, outlines: true, theme: "dark",
      fontSize: 26, fontFamily: "condensed", spacing: 8, radius: 0, stripe: false, nickCase: "upper",
    },
  },
  {
    id: "light",
    name: "Светлый",
    desc: "для светлых сцен OBS",
    swatch: ["#121524", "#f4f6fb"],
    font: INTER,
    patch: {
      styleId: "light", transparent: false, outlines: false, theme: "light",
      fontSize: 17, fontFamily: "inter", spacing: 6, radius: 12, stripe: false, nickCase: "normal",
    },
  },
];

/* ========================= ОВЕРЛЕЙ ========================= */

export const OVERLAY_STYLES: Style<OverlayConfig>[] = [
  {
    id: "rust",
    name: "Rust",
    desc: "как в игре: без подложки, только текст с тенью",
    swatch: ["#cd412b", "transparent"],
    font: OSWALD,
    patch: {
      styleId: "rust", backdropOpacity: 0, textOpacity: 1, radius: 0, accentBar: false,
      shadowText: true, fontFamily: "condensed", fontSize: 15, spacing: 1, nickCase: "normal",
    },
  },
  {
    id: "wot",
    name: "World of Tanks",
    desc: "узкий шрифт, полоса площадки",
    swatch: ["#f2a900", "rgba(20,23,28,0.7)"],
    font: OSWALD,
    patch: {
      styleId: "wot", backdropOpacity: 0.7, textOpacity: 1, radius: 2, accentBar: true,
      shadowText: false, fontFamily: "condensed", fontSize: 14, spacing: 3, nickCase: "upper",
    },
  },
  {
    id: "cs",
    name: "Counter-Strike",
    desc: "почти без подложки, плотные строки",
    swatch: ["#5e98d9", "rgba(27,40,56,0.35)"],
    font: MONO,
    patch: {
      styleId: "cs", backdropOpacity: 0.35, textOpacity: 1, radius: 0, accentBar: false,
      shadowText: true, fontFamily: "mono", fontSize: 13, spacing: 1, nickCase: "normal",
    },
  },
  {
    id: "minecraft",
    name: "Minecraft",
    desc: "плотный тёмный блок без скруглений",
    swatch: ["#7ee787", "rgba(16,20,16,0.75)"],
    font: MONO,
    patch: {
      styleId: "minecraft", backdropOpacity: 0.75, textOpacity: 1, radius: 0, accentBar: false,
      shadowText: false, fontFamily: "mono", fontSize: 14, spacing: 2, nickCase: "normal",
    },
  },
  {
    id: "valorant",
    name: "Valorant",
    desc: "чистый HUD со скруглениями",
    swatch: ["#ff4655", "rgba(15,25,35,0.55)"],
    font: INTER,
    patch: {
      styleId: "valorant", backdropOpacity: 0.55, textOpacity: 1, radius: 10, accentBar: true,
      shadowText: true, fontFamily: "inter", fontSize: 14, spacing: 4, nickCase: "normal",
    },
  },
  {
    id: "ghost",
    name: "Прозрачный",
    desc: "без подложки, только текст с тенью",
    swatch: ["#ffffff", "transparent"],
    font: INTER,
    patch: {
      styleId: "ghost", backdropOpacity: 0, textOpacity: 1, radius: 0, accentBar: false,
      shadowText: true, fontFamily: "inter", fontSize: 15, spacing: 4, nickCase: "normal",
    },
  },
  {
    id: "cinema",
    name: "Крупный",
    desc: "большой текст для дальнего экрана",
    swatch: ["#fde68a", "rgba(8,10,16,0.45)"],
    font: SERIF,
    patch: {
      styleId: "cinema", backdropOpacity: 0.45, textOpacity: 1, radius: 6, accentBar: false,
      shadowText: true, fontFamily: "serif", fontSize: 20, spacing: 6, nickCase: "normal",
    },
  },
];

/** Переезд удалённых стилей на новые игровые темы (старые сохранения). */
const STYLE_ALIAS: Record<string, string> = {
  classic: "valorant", cards: "valorant", focus: "valorant", broadcast: "valorant",
  terminal: "cs", console: "cs", lines: "cs", dense: "rust", minimal: "rust",
  neon: "wot", journal: "wow", book: "wow",
  transparent: "rust", ticker: "cs", "dark-card": "minecraft", "light-card": "light",
  bubbles: "cozy", stripe: "wot", glass: "valorant", hud: "wot", solid: "minecraft",
  compact: "cs", events: "valorant", stealth: "ghost", spotlight: "wot",
};

export function resolveStyleId(id: string | undefined): string | undefined {
  if (!id) return id;
  return STYLE_ALIAS[id] ?? id;
}

export function isStyleActive<T extends { styleId?: string }>(style: Style<T>, cfg: T): boolean {
  return resolveStyleId(cfg.styleId) === style.id;
}

/** название выбранного стиля — для заголовка свёрнутого блока */
export function styleName<T>(styles: Style<T>[], id: string | undefined): string {
  const resolved = resolveStyleId(id);
  return styles.find((s) => s.id === resolved)?.name ?? "свой";
}

/* совместимость со старыми импортами */
export const FEED_PRESETS = FEED_STYLES;
export const WIDGET_PRESETS = WIDGET_STYLES;
export const OVERLAY_PRESETS = OVERLAY_STYLES;

/** запасной шрифт миниатюр — используется, если стиль его не задал */
export const PREVIEW_FONTS = { INTER, MONO, NUNITO, OSWALD, SERIF, SYSTEM };
