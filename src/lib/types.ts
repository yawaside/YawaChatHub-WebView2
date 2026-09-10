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

export interface ChatViewConfig {
  density: FeedDensity;
  fontSize: number;
  backdrop: boolean;
  animations: boolean;
  showTime: boolean;
  showBadges: boolean;
  showPlatform: boolean;
  accentNick: boolean;
}

export const DEFAULT_CHAT_VIEW: ChatViewConfig = {
  density: "standard",
  fontSize: 13,
  backdrop: true,
  animations: true,
  showTime: true,
  showBadges: true,
  showPlatform: true,
  accentNick: true,
};

/** ------- виджет OBS / оверлей ------- */
export interface WidgetConfig {
  max: number;
  fontSize: number;
  transparent: boolean;
  outlines: boolean;
  theme: "dark" | "light";
}

export const DEFAULT_WIDGET: WidgetConfig = {
  max: 8,
  fontSize: 16,
  transparent: true,
  outlines: true,
  theme: "dark",
};

export type OverlayCorner = "top-left" | "top-right" | "bottom-left" | "bottom-right" | "free";
export type OverlayGrowth = "down" | "up";
/** где расположена панель статистики относительно ленты */
export type OverlayStatsPos = "bottom" | "top";

export interface OverlayConfig {
  enabled: boolean;
  /** прозрачность ПОДЛОЖКИ (0 = полностью прозрачная, текст остаётся чётким) */
  backdropOpacity: number;
  /** прозрачность самого текста — отдельная, по умолчанию 1 */
  textOpacity: number;
  fontSize: number;
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
  eventsOnly: boolean;
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
  // по умолчанию оверлей ВЫКЛЮЧЕН — включается кнопкой или хоткеем
  enabled: false,
  backdropOpacity: 0.55,
  textOpacity: 1,
  fontSize: 14,
  clickThrough: true,
  maxMessages: 6,
  corner: "top-left",
  width: 400,
  offsetX: 16,
  offsetY: 16,
  growth: "down",
  showTime: false,
  showPlatform: true,
  showBadges: false,
  showEmotes: true,
  eventsOnly: false,
  autoFade: false,
  fadeAfterSec: 30,
  freePosition: false,
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

export const DEFAULT_HOTKEYS: Record<string, string> = {
  toggleSpeech: "F9",
  pauseQueue: "F7",
  skipQueue: "F8",
  toggleOverlay: "Ctrl+Shift+O",
  clearFeed: "Ctrl+Shift+X",
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
