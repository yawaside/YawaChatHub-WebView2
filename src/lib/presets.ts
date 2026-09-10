import type { ChatViewConfig, OverlayConfig, WidgetConfig } from "./types";

/**
 * Варианты оформления (как в оригинальном репозитории): готовые пресеты
 * для ленты, OBS-виджета и игрового оверлея. Каждый пресет меняет не только
 * плотность и цвета, но и ШРИФТ с интервалами между сообщениями.
 */

export interface Preset<T> {
  id: string;
  name: string;
  desc: string;
  /** цвета для миниатюры предпросмотра */
  swatch: [string, string];
  /** шрифт миниатюры */
  previewFont?: string;
  patch: Partial<T>;
}

/* ==================== ЛЕНТА ==================== */

export const FEED_PRESETS: Preset<ChatViewConfig>[] = [
  {
    id: "classic",
    name: "Классика",
    desc: "Inter · всё видно",
    swatch: ["#8b5cf6", "#1c1f2e"],
    previewFont: '"Inter", sans-serif',
    patch: {
      density: "standard", fontSize: 13, fontFamily: "inter", spacing: 6,
      backdrop: true, animations: true, showTime: true, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "cozy",
    name: "Комфорт",
    desc: "Nunito · крупно и просторно",
    swatch: ["#f472b6", "#241c26"],
    previewFont: '"Nunito", sans-serif',
    patch: {
      density: "cozy", fontSize: 15, fontFamily: "rounded", spacing: 10,
      backdrop: true, animations: true, showTime: true, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "terminal",
    name: "Терминал",
    desc: "моноширинный, плотный",
    swatch: ["#22d3ee", "#0d1519"],
    previewFont: '"JetBrains Mono", monospace',
    patch: {
      density: "compact", fontSize: 13, fontFamily: "mono", spacing: 3,
      backdrop: false, animations: false, showTime: true, showBadges: false, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "minimal",
    name: "Минимал",
    desc: "системный · только текст",
    swatch: ["#94a3b8", "#12141c"],
    previewFont: "system-ui, sans-serif",
    patch: {
      density: "compact", fontSize: 13, fontFamily: "system", spacing: 4,
      backdrop: false, animations: false, showTime: false, showBadges: false, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "dense",
    name: "Плотный",
    desc: "Oswald · максимум строк",
    swatch: ["#fbbf24", "#1a1710"],
    previewFont: '"Oswald", sans-serif',
    patch: {
      density: "tight", fontSize: 13, fontFamily: "condensed", spacing: 1,
      backdrop: false, animations: false, showTime: false, showBadges: false, showPlatform: true, accentNick: true,
    },
  },
  {
    id: "journal",
    name: "Журнал",
    desc: "с засечками, для чтения",
    swatch: ["#4ade80", "#101a14"],
    previewFont: "Georgia, serif",
    patch: {
      density: "standard", fontSize: 16, fontFamily: "serif", spacing: 8,
      backdrop: true, animations: true, showTime: false, showBadges: true, showPlatform: true, accentNick: true,
    },
  },
];

/* ==================== ВИДЖЕТ OBS ==================== */

export const WIDGET_PRESETS: Preset<WidgetConfig>[] = [
  {
    id: "transparent",
    name: "Прозрачный",
    desc: "Inter · чистый текст",
    swatch: ["#ffffff", "transparent"],
    previewFont: '"Inter", sans-serif',
    patch: { transparent: true, outlines: true, theme: "dark", fontSize: 18, max: 8, fontFamily: "inter", spacing: 4 },
  },
  {
    id: "dark-card",
    name: "Тёмные карточки",
    desc: "Nunito · подложка",
    swatch: ["#e8eaf3", "#0b0d14"],
    previewFont: '"Nunito", sans-serif',
    patch: { transparent: false, outlines: false, theme: "dark", fontSize: 17, max: 8, fontFamily: "rounded", spacing: 6 },
  },
  {
    id: "light-card",
    name: "Светлые карточки",
    desc: "для светлых сцен",
    swatch: ["#121524", "#f4f6fb"],
    previewFont: '"Inter", sans-serif',
    patch: { transparent: false, outlines: false, theme: "light", fontSize: 17, max: 8, fontFamily: "inter", spacing: 6 },
  },
  {
    id: "big",
    name: "Крупный",
    desc: "Oswald · большой текст",
    swatch: ["#fbbf24", "#0b0d14"],
    previewFont: '"Oswald", sans-serif',
    patch: { transparent: true, outlines: true, theme: "dark", fontSize: 26, max: 5, fontFamily: "condensed", spacing: 8 },
  },
  {
    id: "ticker",
    name: "Лента",
    desc: "моно · много сообщений",
    swatch: ["#22d3ee", "#0b0d14"],
    previewFont: '"JetBrains Mono", monospace',
    patch: { transparent: true, outlines: true, theme: "dark", fontSize: 14, max: 14, fontFamily: "mono", spacing: 2 },
  },
];

/* ==================== ОВЕРЛЕЙ ==================== */

export const OVERLAY_PRESETS: Preset<OverlayConfig>[] = [
  {
    id: "glass",
    name: "Стекло",
    desc: "Inter · размытая подложка",
    swatch: ["#ffffff", "rgba(8,10,16,0.55)"],
    previewFont: '"Inter", sans-serif',
    patch: {
      backdropOpacity: 0.55, textOpacity: 1, radius: 10, accentBar: true, shadowText: true,
      showStats: true, fontFamily: "inter", spacing: 4,
    },
  },
  {
    id: "ghost",
    name: "Призрак",
    desc: "без подложки, тень текста",
    swatch: ["#ffffff", "transparent"],
    previewFont: '"Inter", sans-serif',
    patch: {
      backdropOpacity: 0, textOpacity: 1, radius: 0, accentBar: false, shadowText: true,
      showStats: true, fontFamily: "inter", spacing: 6,
    },
  },
  {
    id: "hud",
    name: "HUD",
    desc: "моно · как игровой интерфейс",
    swatch: ["#22d3ee", "rgba(8,10,16,0.75)"],
    previewFont: '"JetBrains Mono", monospace',
    patch: {
      backdropOpacity: 0.75, textOpacity: 1, radius: 4, accentBar: true, shadowText: false,
      showStats: true, fontFamily: "mono", fontSize: 13, spacing: 2,
    },
  },
  {
    id: "compact",
    name: "Компакт",
    desc: "Oswald · узкая полоса",
    swatch: ["#a78bfa", "rgba(8,10,16,0.7)"],
    previewFont: '"Oswald", sans-serif',
    patch: {
      backdropOpacity: 0.7, fontSize: 13, maxMessages: 4, width: 320, radius: 8,
      showStats: true, statsCompact: true, fontFamily: "condensed", spacing: 2,
    },
  },
  {
    id: "events",
    name: "События",
    desc: "только донаты и сабы",
    swatch: ["#f59e0b", "rgba(8,10,16,0.65)"],
    previewFont: '"Nunito", sans-serif',
    patch: {
      backdropOpacity: 0.65, eventsOnly: true, maxMessages: 5, accentBar: true,
      showStats: true, showDonations: true, fontFamily: "rounded", spacing: 8,
    },
  },
  {
    id: "cinema",
    name: "Кино",
    desc: "крупно, строки исчезают",
    swatch: ["#fde68a", "rgba(8,10,16,0.45)"],
    previewFont: "Georgia, serif",
    patch: {
      backdropOpacity: 0.45, fontSize: 20, maxMessages: 4, autoFade: true, fadeAfterSec: 20,
      shadowText: true, fontFamily: "serif", spacing: 10,
    },
  },
];

/** активен ли пресет: все его поля совпадают с текущим конфигом */
export function isPresetActive<T extends object>(preset: Preset<T>, cfg: T): boolean {
  return Object.entries(preset.patch).every(
    ([key, value]) => (cfg as Record<string, unknown>)[key] === value
  );
}
