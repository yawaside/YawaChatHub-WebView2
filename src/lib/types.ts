// ─── Модели данных YawaChatHub (ТЗ §4, §11) ────────────────────────────────

export type PlatformId = "twitch" | "youtube" | "vk" | "kick" | "tiktok";

export interface PlatformMeta {
  id: PlatformId;
  name: string;
  short: string;
  color: string;
  soft: string;
  hint: string;
}

export const PLATFORMS: PlatformMeta[] = [
  { id: "twitch", name: "Twitch", short: "TW", color: "#9146FF", soft: "rgba(145,70,255,.14)", hint: "username" },
  { id: "youtube", name: "YouTube Live", short: "YT", color: "#FF0033", soft: "rgba(255,0,51,.12)", hint: "@handle" },
  { id: "vk", name: "VK Video Live", short: "VK", color: "#0077FF", soft: "rgba(0,119,255,.12)", hint: "channel_name" },
  { id: "kick", name: "Kick", short: "KK", color: "#53FC18", soft: "rgba(83,252,24,.10)", hint: "username" },
  { id: "tiktok", name: "TikTok Live", short: "TT", color: "#FE2C55", soft: "rgba(254,44,85,.12)", hint: "@username" },
];

export const platformById = (id: PlatformId): PlatformMeta =>
  PLATFORMS.find((p) => p.id === id) ?? PLATFORMS[0];

export interface Channel {
  platform: PlatformId;
  channelId: string;
}

/** Статус канала/стримера, присваивается реальными коннекторами. */
export type ChannelState = "connecting" | "online" | "offline" | "unsupported";

export const CHANNEL_STATE_META: Record<ChannelState, { color: string; label: string; pulse: boolean }> = {
  online: { color: "var(--ok)", label: "онлайн", pulse: true },
  offline: { color: "var(--danger)", label: "офлайн", pulse: false },
  connecting: { color: "var(--warn)", label: "подключение…", pulse: true },
  unsupported: { color: "var(--muted)", label: "нет коннектора", pulse: false },
};

export type MsgPart =
  | { t: "text"; v: string }
  | { t: "emote"; v: string; url: string }
  | { t: "link"; v: string };

export type EventKind = "follow" | "sub" | "raid" | "donate" | "info";

export interface ChatMsg {
  id: string;
  platform: PlatformId;
  channelId: string;
  author: string;
  color: string;
  badges: string[];
  text: string;
  parts: MsgPart[];
  ts: number;
  sys?: boolean;
  kind?: EventKind;
}

// ─── Настройки (settings.json, ТЗ §11) ─────────────────────────────────────

export interface TtsTemplate {
  author: boolean;
  platform: boolean;
  text: boolean;
}

export interface TtsFilters {
  links: boolean;
  commands: boolean;
  emoji: boolean;
  dedupe: boolean;
  maxLen: number;
  perMin: number;
  maxCapsRatio: number;
  squashRepeats: boolean;
  stripSymbols: boolean;
  minLen: number;
  banWords: string[];
  maskWords: string[];
  banAuthors: string[];
  allowAuthors: string[];
}

export interface TtsConfig {
  enabled: boolean;
  rate: number;
  volume: number;
  voiceURI: string;
  obsTts: boolean;
  template: TtsTemplate;
  filters: TtsFilters;
}

export interface ChatViewConfig {
  style: "classic" | "compact" | "cards";
  fontSize: number;
  rowGap: number;
  radius: number;
  showPlatform: boolean;
  showTime: boolean;
  showBadges: boolean;
  messageEffect: FxId;
  effectDuration: number;
}

export type FxId =
  | "fx-slide-up"
  | "fx-fade"
  | "fx-slide-left"
  | "fx-slide-down"
  | "fx-scale"
  | "fx-blur"
  | "fx-bounce"
  | "fx-typewriter";

export const FX_LIST: { id: FxId; name: string }[] = [
  { id: "fx-slide-up", name: "Снизу вверх" },
  { id: "fx-fade", name: "Проявление" },
  { id: "fx-slide-left", name: "Слева" },
  { id: "fx-slide-down", name: "Сверху вниз" },
  { id: "fx-scale", name: "Масштаб" },
  { id: "fx-blur", name: "Расфокус" },
  { id: "fx-bounce", name: "Прыжок" },
  { id: "fx-typewriter", name: "Пишущая машинка" },
];

export interface WidgetConfig {
  style: "clean" | "compact" | "bubbles";
  theme: string;
  fontSize: number;
  bgOpacity: number;
  radius: number;
  duration: number;
  dir: "up" | "down";
  shadow: boolean;
  showPlatform: boolean;
  showTime: boolean;
  maxMessages: number;
  effect: FxId;
  effectDuration: number;
  textColor: string;
  nameColor: string;
  bgColor: string;
  border: boolean;
  bgImage: string;
}

export interface OverlayConfig {
  enabled: boolean;
  bgOpacity: number;
  clickThrough: boolean;
  mode: "compact" | "cozy";
  fontSize: number;
  maxMessages: number;
  locked: boolean;
  style: "clean" | "compact" | "bubbles";
  showBorder: boolean;
  effect: FxId;
  effectDuration: number;
  textColor: string;
  nameColor: string;
  bgColor: string;
  radius: number;
  bgImage: string;
  showTime: boolean;
  showPlatform: boolean;
}

export type HotkeyAction =
  | "tts:toggle"
  | "tts:pause"
  | "tts:skip"
  | "tts:clear"
  | "overlay:toggle"
  | "overlay:clicks"
  | "window:toggle"
  | "feed:clear";

export const HOTKEY_ACTIONS: { id: HotkeyAction; name: string }[] = [
  { id: "tts:toggle", name: "Озвучка вкл/выкл" },
  { id: "tts:pause", name: "Пауза озвучки" },
  { id: "tts:skip", name: "Пропустить текущее" },
  { id: "tts:clear", name: "Очистить очередь" },
  { id: "overlay:toggle", name: "Оверлей вкл/выкл" },
  { id: "overlay:clicks", name: "Сквозные клики" },
  { id: "window:toggle", name: "Свернуть/показать окно" },
  { id: "feed:clear", name: "Очистить ленту" },
];

export const DEFAULT_HOTKEYS: Record<HotkeyAction, string> = {
  "overlay:toggle": "Control+Shift+G",
  "overlay:clicks": "Control+Shift+C",
  "tts:toggle": "Control+Shift+T",
  "tts:pause": "Control+Shift+P",
  "tts:skip": "Control+Shift+S",
  "tts:clear": "Control+Shift+Q",
  "window:toggle": "Control+Shift+H",
  "feed:clear": "Control+Shift+L",
};

export interface Settings {
  settingsSchemaVersion: number;
  port: number;
  token: string;
  theme: string;
  closeToTray: boolean;
  minimizeToTray: boolean;
  startHidden: boolean;
  youtubeApiKey: string;
  overlayBounds: null | { x: number; y: number; w: number; h: number };
  channelsCollapsed: boolean;
  menuCollapsed: boolean;
  showEvents: boolean;
  channels: Channel[];
  tts: TtsConfig;
  chatView: ChatViewConfig;
  widget: WidgetConfig;
  overlay: OverlayConfig;
  hotkeys: Record<HotkeyAction, string>;
}

export const DEFAULT_SETTINGS: Settings = {
  settingsSchemaVersion: 4,
  port: 47823,
  token: "yawa_demo",
  theme: "midnight",
  closeToTray: false,
  minimizeToTray: false,
  startHidden: false,
  youtubeApiKey: "",
  overlayBounds: null,
  channelsCollapsed: false,
  menuCollapsed: false,
  showEvents: true,
  channels: [
    { platform: "twitch", channelId: "yawa_gg" },
    { platform: "youtube", channelId: "@yawastream" },
    { platform: "kick", channelId: "yawa" },
    { platform: "vk", channelId: "yawa_live" },
    { platform: "tiktok", channelId: "@yawa.gg" },
  ],
  tts: {
    enabled: false,
    rate: 1.0,
    volume: 0.9,
    voiceURI: "",
    obsTts: false,
    template: { author: true, platform: true, text: true },
    filters: {
      links: true,
      commands: true,
      emoji: false,
      dedupe: true,
      maxLen: 220,
      perMin: 24,
      maxCapsRatio: 65,
      squashRepeats: true,
      stripSymbols: true,
      minLen: 2,
      banWords: [],
      maskWords: [],
      banAuthors: [],
      allowAuthors: [],
    },
  },
  chatView: {
    style: "classic",
    fontSize: 15,
    rowGap: 6,
    radius: 16,
    showPlatform: true,
    showTime: true,
    showBadges: true,
    messageEffect: "fx-slide-up",
    effectDuration: 0.34,
  },
  widget: {
    style: "clean",
    theme: "minimal-dark",
    fontSize: 16,
    bgOpacity: 70,
    radius: 12,
    duration: 8,
    dir: "up",
    shadow: true,
    showPlatform: true,
    showTime: true,
    maxMessages: 8,
    effect: "fx-slide-up",
    effectDuration: 0.32,
    textColor: "",
    nameColor: "",
    bgColor: "",
    border: true,
    bgImage: "",
  },
  overlay: {
    enabled: false,
    bgOpacity: 55,
    clickThrough: false,
    mode: "compact",
    fontSize: 12,
    maxMessages: 6,
    locked: false,
    style: "clean",
    showBorder: true,
    effect: "fx-slide-up",
    effectDuration: 0.3,
    textColor: "",
    nameColor: "",
    bgColor: "",
    radius: 14,
    bgImage: "",
    showTime: false,
    showPlatform: true,
  },
  hotkeys: { ...DEFAULT_HOTKEYS },
};

// ─── Валидация каналов (ТЗ §15) ─────────────────────────────────────────────

export function validateChannelId(platform: PlatformId, raw: string): { ok: boolean; value: string; error?: string } {
  let v = raw.trim();
  if (!v) return { ok: false, value: v, error: "Пустое значение" };
  if (/https?:\/\/|\/|\?|&|#/i.test(v))
    return { ok: false, value: v, error: "Укажите только ник канала, не ссылку" };
  if (platform === "tiktok" && !v.startsWith("@")) v = "@" + v;
  if (platform === "youtube") {
    if (/^UC[\w-]{20,}$/.test(v))
      return { ok: false, value: v, error: "Нужен @handle или username, не UC-ID" };
    if (/^[\w-]{11}$/.test(v) && !v.startsWith("@"))
      return { ok: false, value: v, error: "Похоже на ID видео — укажите @handle канала" };
  }
  const bare = v.replace(/^@/, "");
  if (!/^[a-zA-Z0-9_.-]{2,40}$/.test(bare))
    return { ok: false, value: v, error: "Допустимы 2–40 символов: a-z 0-9 _ . -" };
  return { ok: true, value: v };
}

// ─── Пресеты оформления виджета (ТЗ §9, виджет) ────────────────────────────

export interface WidgetPreset {
  id: string;
  name: string;
  patch: Partial<WidgetConfig>;
  swatch: { bg: string; text: string; accent: string };
}

export const WIDGET_PRESETS: WidgetPreset[] = [
  {
    id: "minimal-dark",
    name: "Минимал · тёмный",
    swatch: { bg: "rgba(12,14,20,.85)", text: "#f2f4f8", accent: "#8b7bff" },
    patch: { theme: "minimal-dark", style: "clean", radius: 12, bgOpacity: 70, border: true, shadow: true, textColor: "", nameColor: "", bgColor: "" },
  },
  {
    id: "minimal-light",
    name: "Минимал · светлый",
    swatch: { bg: "rgba(255,255,255,.92)", text: "#181a20", accent: "#5b54e6" },
    patch: { theme: "minimal-light", style: "clean", radius: 12, bgOpacity: 85, border: false, shadow: true, textColor: "#181a20", nameColor: "", bgColor: "rgba(255,255,255,1)" },
  },
  {
    id: "neon",
    name: "Неон",
    swatch: { bg: "rgba(8,10,18,.9)", text: "#e8fbff", accent: "#38e8ff" },
    patch: { theme: "neon", style: "clean", radius: 10, bgOpacity: 75, border: true, shadow: true, textColor: "#e8fbff", nameColor: "#38e8ff", bgColor: "" },
  },
  {
    id: "glass",
    name: "Стекло",
    swatch: { bg: "rgba(255,255,255,.10)", text: "#ffffff", accent: "#b7a6ff" },
    patch: { theme: "glass", style: "bubbles", radius: 18, bgOpacity: 25, border: true, shadow: false, textColor: "#ffffff", nameColor: "", bgColor: "" },
  },
  {
    id: "cyber",
    name: "Кибер",
    swatch: { bg: "rgba(16,7,28,.92)", text: "#ffe95e", accent: "#ff2ea6" },
    patch: { theme: "cyber", style: "clean", radius: 4, bgOpacity: 80, border: true, shadow: true, textColor: "#ffe95e", nameColor: "#ff2ea6", bgColor: "" },
  },
  {
    id: "pastel",
    name: "Пастель",
    swatch: { bg: "rgba(250,240,255,.92)", text: "#4a3f55", accent: "#c084fc" },
    patch: { theme: "pastel", style: "bubbles", radius: 20, bgOpacity: 80, border: false, shadow: true, textColor: "#4a3f55", nameColor: "#a855f7", bgColor: "rgba(250,240,255,1)" },
  },
  {
    id: "console",
    name: "Консоль",
    swatch: { bg: "rgba(0,0,0,.92)", text: "#9dff9d", accent: "#4ade80" },
    patch: { theme: "console", style: "compact", radius: 2, bgOpacity: 85, border: true, shadow: false, textColor: "#9dff9d", nameColor: "#4ade80", bgColor: "rgba(0,0,0,1)" },
  },
  {
    id: "streamer",
    name: "Стриммер",
    swatch: { bg: "rgba(20,16,32,.88)", text: "#ffffff", accent: "#ff8a3d" },
    patch: { theme: "streamer", style: "bubbles", radius: 16, bgOpacity: 70, border: false, shadow: true, textColor: "", nameColor: "#ff8a3d", bgColor: "" },
  },
];

export const THEMES: { id: string; name: string; swatch: [string, string, string] }[] = [
  { id: "midnight", name: "Полночь", swatch: ["#0b0e17", "#171c2c", "#8b7bff"] },
  { id: "onyx", name: "Оникс", swatch: ["#0a0a0c", "#17181d", "#e2e4f0"] },
  { id: "aurora", name: "Аврора", swatch: ["#071410", "#0d2520", "#34d399"] },
  { id: "sakura", name: "Сакура", swatch: ["#170b12", "#25121d", "#fb7185"] },
  { id: "sunset", name: "Закат", swatch: ["#160c08", "#281408", "#fb923c"] },
  { id: "daylight", name: "День", swatch: ["#eef1f7", "#ffffff", "#5b54e6"] },
];
