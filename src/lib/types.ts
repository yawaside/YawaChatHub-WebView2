/** Общие типы интерфейса приложения */

export type PlatformId =
  | "twitch"
  | "youtube"
  | "vkplay"
  | "kick"
  | "tiktok"
  | "donationalerts";

/**
 * connecting — идёт подключение
 * connected  — канал подключён (чат читается), но эфир не подтверждён
 * online     — подтверждена активная трансляция (есть данные о стриме/зрителях)
 * offline    — канал доступен, но трансляции нет
 * error      — не удалось подключиться
 */
export type ChannelStatus = "online" | "connected" | "offline" | "connecting" | "error";

export interface Channel {
  id: string;
  platform: PlatformId;
  username: string;
  status: ChannelStatus;
  viewers: number;
  messages: number;
  /** DonationAlerts: секретный токен + валюта */
  token?: string;
  currency?: string;
}

export type MsgKind =
  | "chat"
  | "system"
  | "reward"
  | "donate"
  | "sub"
  | "raid"
  | "gift"
  | "bits"
  | "reward"
  | "command"
  | "mod"
  | "status";

export interface ChatMsg {
  id: number;
  ts: number;
  platform: PlatformId;
  channel: string;
  author: string;
  color: string;
  text: string;
  kind: MsgKind;
  badges?: string[];
  amount?: number;
  currency?: string;
  deleted?: boolean;
}

/** ------- озвучка ------- */
export interface TtsConfig {
  enabled: boolean;
  voice: string;
  rate: number; // 0.5 – 2
  volume: number; // 0 – 1
  speakAuthor: boolean;
  speakPlatform: boolean;
  donatesOnly: boolean;
  skipLinks: boolean;
  skipCommands: boolean;
  maxLen: number;
  capsLimit: number; // % заглавных
  stripEmoji: boolean;
  squashRepeats: boolean;
  linkPhrase: string;
  /** читать названия смайлов/стикеров (по умолчанию выключено) */
  speakEmotes: boolean;
  /** движок озвучки: системный SAPI или нейросетевой Edge TTS */
  engine: "sapi" | "edge";
}

export const DEFAULT_TTS: TtsConfig = {
  // по умолчанию озвучка ВЫКЛЮЧЕНА — включается тумблером в шапке или F9
  enabled: false,
  voice: "",
  rate: 1,
  volume: 0.9,
  speakAuthor: true,
  speakPlatform: false,
  donatesOnly: false,
  skipLinks: true,
  skipCommands: true,
  maxLen: 220,
  capsLimit: 80,
  stripEmoji: true,
  squashRepeats: true,
  linkPhrase: "ссылка",
  speakEmotes: false,   // смайлы не озвучиваются
  engine: "edge",       // нейросетевые русские голоса Edge TTS
};

/** ------- вид ленты ------- */
export type FeedDensity = "cozy" | "standard" | "compact" | "tight";

/** идентификатор шрифтового набора (см. FONT_FAMILIES в lib/fonts.ts) */
export type FontId = "inter" | "mono" | "rounded" | "condensed" | "serif" | "system";

/** написание ников: как есть или капсом */
export type NickCase = "normal" | "upper";

export interface ChatViewConfig {
  /** выбранный стиль оформления (см. lib/presets.ts) */
  styleId: string;
  density: FeedDensity;
  fontSize: number;
  fontFamily: FontId;
  spacing: number;
  /** скругление плашки сообщения */
  radius: number;
  /** цветная полоса площадки слева */
  stripe: boolean;
  /** тонкая линия-разделитель между сообщениями */
  separators: boolean;
  nickCase: NickCase;
  backdrop: boolean;
  animations: boolean;
  showTime: boolean;
  showBadges: boolean;
  showPlatform: boolean;
  accentNick: boolean;
}

export const DEFAULT_CHAT_VIEW: ChatViewConfig = {
  styleId: "classic",
  density: "standard",
  fontSize: 13,
  fontFamily: "inter",
  spacing: 6,
  radius: 10,
  stripe: false,
  separators: false,
  nickCase: "normal",
  backdrop: true,
  animations: true,
  showTime: true,
  showBadges: true,
  showPlatform: true,
  accentNick: true,
};

/** ------- виджет OBS / оверлей ------- */
export interface WidgetConfig {
  styleId: string;
  max: number;
  fontSize: number;
  fontFamily: FontId;
  spacing: number;
  radius: number;
  stripe: boolean;
  nickCase: NickCase;
  /** показывать донаты, сабы и рейды (по умолчанию выключено) */
  showEvents: boolean;
  transparent: boolean;
  outlines: boolean;
  theme: "dark" | "light";
}

export const DEFAULT_WIDGET: WidgetConfig = {
  styleId: "transparent",
  max: 8,
  fontSize: 18,
  fontFamily: "inter",
  spacing: 4,
  radius: 10,
  stripe: false,
  nickCase: "normal",
  showEvents: false,
  transparent: true,
  outlines: true,
  theme: "dark",
};

export type OverlayCorner = "top-left" | "top-right" | "bottom-left" | "bottom-right" | "free";
export type OverlayGrowth = "down" | "up";
/** где расположена панель статистики относительно ленты */
export type OverlayStatsPos = "bottom" | "top";

export interface OverlayConfig {
  /** выбранный стиль оформления */
  styleId: string;
  nickCase: NickCase;
  enabled: boolean;
  /** прозрачность ПОДЛОЖКИ (0 = полностью прозрачная, текст остаётся чётким) */
  backdropOpacity: number;
  /** прозрачность самого текста — отдельная, по умолчанию 1 */
  textOpacity: number;
  fontSize: number;
  fontFamily: FontId;
  /** интервал между сообщениями, px */
  spacing: number;
  clickThrough: boolean;
  maxMessages: number;
  /* размещение */
  corner: OverlayCorner;
  width: number;
  offsetX: number;
  offsetY: number;
  growth: OverlayGrowth;
  /* содержимое строки */
  showTime: boolean;
  showPlatform: boolean;
  showBadges: boolean;
  showEmotes: boolean;
  /** показывать донаты, сабы и рейды (по умолчанию выключено) */
  showEvents: boolean;
  /* исчезновение старых сообщений */
  autoFade: boolean;
  fadeAfterSec: number;
  /* свободное размещение: перетаскивание окна мышью */
  freePosition: boolean;
  posX: number;
  posY: number;
  locked: boolean;
  /* панель статистики площадок */
  showStats: boolean;
  /** снизу (по умолчанию) или сверху ленты */
  statsPos: OverlayStatsPos;
  showDonations: boolean;
  showViewers: boolean;
  statsCompact: boolean;
  /** скрывать площадки без подключённых каналов */
  hideDisconnected: boolean;
  /* оформление */
  radius: number;
  accentBar: boolean;
  shadowText: boolean;
}

export const DEFAULT_OVERLAY: OverlayConfig = {
  styleId: "glass",
  nickCase: "normal",
  // по умолчанию оверлей ВЫКЛЮЧЕН — включается кнопкой или хоткеем
  enabled: false,
  backdropOpacity: 0.55,
  textOpacity: 1,
  fontSize: 14,
  fontFamily: "inter",
  spacing: 4,
  // сквозной клик ВЫКЛЮЧЕН по умолчанию — включается хоткеем toggleClickThrough
  clickThrough: false,
  maxMessages: 6,
  width: 400,
  offsetX: 16,
  offsetY: 16,
  growth: "down",
  showTime: false,
  showPlatform: true,
  showBadges: false,
  showEmotes: true,
  showEvents: false,
  autoFade: false,
  fadeAfterSec: 30,
  // Как в рабочем v1.2.4: по умолчанию привязка к углу. В свободном режиме
  // старый OverlayWindow временно отключает chroma-key, чтобы окно ловило мышь.
  freePosition: false,
  corner: "top-left",
  posX: 24,
  posY: 24,
  locked: false,
  showStats: true,
  statsPos: "bottom",
  showDonations: true,
  showViewers: true,
  statsCompact: false,
  hideDisconnected: true,
  radius: 10,
  accentBar: true,
  shadowText: true,
};

/** статистика площадок для шапки оверлея (донаты идут первыми) */
export interface OverlayStats {
  donations: { total: number; currency: string; count: number };
  platforms: {
    platform: PlatformId;
    /** подтверждённый эфир */
    online: boolean;
    /** канал подключён (чат читается) */
    connected?: boolean;
    viewers: number;
    messages: number;
  }[];
}

/** Клавиши хранятся физическими кодами — не зависят от раскладки. */
export const DEFAULT_HOTKEYS: Record<string, string> = {
  toggleSpeech: "F9",
  pauseQueue: "F7",
  skipQueue: "F8",
  toggleOverlay: "Ctrl+Shift+KeyO",
  // сквозной клик включается ТОЛЬКО хоткеем — комбинацию назначает пользователь
  toggleClickThrough: "Ctrl+Shift+KeyT",
  clearFeed: "Ctrl+Shift+KeyX",
};

export interface BotCommand {
  id: string;
  trigger: string;
  response: string;
  cooldown: number;
  enabled: boolean;
  platform: PlatformId | "all";
}

/** команда модерации из ленты */
export interface ModerationRequest {
  msg: ChatMsg;
  x: number;
  y: number;
}
