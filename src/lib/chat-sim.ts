// ─── Демо-коннекторы: эмуляция живого чата площадок ─────────────────────────
// В desktop-сборке здесь работают настоящие коннекторы (Twitch IRC, YouTube
// polling без API-ключа, Kick, TikTok Live, VK Video Live). В браузере —
// реалистичная генерация трафика, чтобы весь UI был живым.
import type { Channel, ChatMsg, PlatformId } from "./types";
import { EMOTE_CODES, parseParts } from "./emotes";
import { nameColor, uid } from "./utils";

const NICKS: Record<PlatformId, string[]> = {
  twitch: ["midnight_fox", "streamfan_ru", "kotopes_tv", "luna_plays", "xXshadowXx", "twitch_batya", "neon_dream", "sashka_one", "pepega_clap", "mira_tv", "oldfag_2013", "quiet_owl"],
  youtube: ["ИгроманPRO", "Аня смотрит", "BARS_official", "dmitriy_v", "Kotletka", "ЮтубЗритель", "max-play", "NataliStar", "random_guy", "СергейП", "Vladosik", "OpaGangnam"],
  kick: ["kickflip_", "greenmachine", "wagerboss", "s0ulja", "kick_zritel", "fast_fingers", "k1ck3r", "moonshine_x", "rezy_Boy", "toxic_free"],
  vk: ["ivan_petrov", "VKzritel", "marina_vl", "chitay_knigi", "Борис_Бритва", "prostotak", "lesnoy_ded", "anna_k", "studentika", "muzhik_43"],
  tiktok: ["tik_toker", "@danya_cringe", "zoomer_ok", "rek_for_you", "grannny", "dva_stvola", "kris_tinki", "@not_borat", "fyp_king", "dushevnaya"],
};

const LINES: string[] = [
  "привет всем! как настроение?",
  "стрим просто огонь, смотрю третий час",
  "Kappa Kappa Kappa",
  "что за игра? подскажите плиз",
  "LUL это было эпично",
  "когда следующий розыгрыш?",
  "!команды",
  "РЕБЯТ СМОТРИТЕ ЧТО Я НАШЁЛ https://example.com/clip",
  "катка супер, го ещё одну",
  "привет из Питера!",
  "а звук не потрескивает? у меня вроде норм",
  "monkaS сейчас что-то будет",
  "го в дискорд после стрима обсудим",
  "этот момент надо в нарезку 4Head",
  "задонатил бы, но я студент FeelsBadMan",
  "АААААА ЧТО ТВОРИТ",
  "смотрю с работы, начальник не видит Kappa",
  "моды вы лучшие",
  "можно музыку потише? а то не слышно тебя",
  "УЖЕ 3 ЧАСА СТРИМИТ БАТЯ",
  "спасибо за контент, лучший канал",
  "BibleThump почему так грустно",
  "какой у тебя пк? фпс норм держит?",
  "привеет, первый раз на стриме",
  "хахахаха Jebaited",
  "поставь лайк кто с 2020 смотрит",
  "ну и позиция у тебя конечно... DansGame",
  "сделай ставку на красное!",
  "кто в чате шарит за настройки, помогите",
  "промокод когда? PJSalt",
  "обожаю эту песню, как называется?",
  "FeelsBadMan у меня такой же скин слетел",
  "EZ EZ EZ катка",
  "передавай привет Мурманску",
  "щас бы чайку и смотреть до утра",
  "а можно без спойлеров плз",
  "NotLikeThis не делай этого",
  "чат, а вы откуда смотрите?",
  "микро чек 1 2 3",
  "погнали на следующую карту",
  "лучший стример ютуба и твича одновременно Kreygasm",
];

const SHORT = ["gg", "+", "жесть", "орууу", "ждём катку", "топ", "имба", "ф", "F", "красавчик", "лол", "ахах", "+1", "го", "огонь"];

const EVENTS: { kind: ChatMsg["kind"]; text: string }[] = [
  { kind: "follow", text: "подписался на канал" },
  { kind: "sub", text: "оформил подписку (3-й месяц)" },
  { kind: "raid", text: "привёл рейд: 42 зрителя" },
  { kind: "donate", text: "поддержал стрим: 150 ₽" },
];

const BADGES: Record<PlatformId, string[][]> = {
  twitch: [[], [], [], ["sub"], ["sub", "vip"], ["mod"], ["sub"]],
  youtube: [[], [], ["member"], [], ["mod"]],
  kick: [[], [], ["sub"], [], ["mod"]],
  vk: [[], [], [], ["mod"]],
  tiktok: [[], [], [], ["fan"], []],
};

export class ChatSimulator {
  private timers: ReturnType<typeof setTimeout>[] = [];
  private running = false;

  constructor(
    private channels: () => Channel[],
    private push: (msg: ChatMsg) => void,
  ) {}

  start() {
    if (this.running) return;
    this.running = true;
    this.loop();
  }

  stop() {
    this.running = false;
    this.timers.forEach(clearTimeout);
    this.timers = [];
  }

  private loop() {
    if (!this.running) return;
    const chs = this.channels();
    const delay = 700 + Math.random() * (chs.length > 3 ? 1600 : 2600);
    this.timers.push(
      setTimeout(() => {
        if (chs.length) this.emit(chs[Math.floor(Math.random() * chs.length)]);
        this.loop();
      }, delay),
    );
  }

  burst(n = 6) {
    const chs = this.channels();
    for (let i = 0; i < n && chs.length; i++) {
      const ch = chs[Math.floor(Math.random() * chs.length)];
      this.timers.push(setTimeout(() => this.emit(ch), i * 260));
    }
  }

  private emit(ch: Channel) {
    const roll = Math.random();
    if (roll < 0.06) {
      const ev = EVENTS[Math.floor(Math.random() * EVENTS.length)];
      const author = pick(NICKS[ch.platform]);
      this.push({
        id: uid(),
        platform: ch.platform,
        channelId: ch.channelId,
        author,
        color: nameColor(author),
        badges: [],
        text: ev.text,
        parts: [{ t: "text", v: ev.text }],
        ts: Date.now(),
        sys: true,
        kind: ev.kind,
      });
      return;
    }
    const author = pick(NICKS[ch.platform]);
    let text: string;
    const r = Math.random();
    if (r < 0.12) text = pick(SHORT);
    else if (r < 0.2) text = `${pick(LINES)} ${pick(EMOTE_CODES)}`;
    else text = pick(LINES);
    this.push(makeMsg(ch.platform, ch.channelId, author, text));
  }
}

export function makeMsg(platform: PlatformId, channelId: string, author: string, text: string): ChatMsg {
  const badges = pick(BADGES[platform]);
  return {
    id: uid(),
    platform,
    channelId,
    author,
    color: nameColor(author),
    badges: [...badges],
    text,
    parts: parseParts(text),
    ts: Date.now(),
  };
}

const pick = <T,>(arr: T[]): T => arr[Math.floor(Math.random() * arr.length)];
