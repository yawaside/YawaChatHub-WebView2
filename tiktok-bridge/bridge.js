/**
 * Проверенный коннектор TikTok LIVE для YawaChatHub.
 *
 * Использует модуль `tiktok-live-connector` — реально протестирован на живых
 * эфирах: чат, подарки, подписки, лайки, входы и зрители приходят стабильно.
 * Не требует cookies, логина и браузера.
 *
 * Протокол (stdin/stdout, одна JSON-строка на событие):
 *   → {"type":"connect","user":"nickname"}
 *   ← {"type":"ready","user":..,"roomId":..}
 *   ← {"type":"status","status":"online|connected|offline|error","viewers":N}
 *   ← {"type":"chat","author":"ник","text":"..."}
 *   ← {"type":"event","kind":"gift|sub|share|join|like","author":..,"text":..,"amount":N}
 *   ← {"type":"log","message":"..."}
 */
const { TikTokLiveConnection, WebcastEvent } = require("tiktok-live-connector");

let conn = null;

function send(obj) {
  try { process.stdout.write(JSON.stringify(obj) + "\n"); }
  catch { /* пайп закрыт */ }
}

function log(message) {
  send({ type: "log", message: String(message).slice(0, 400) });
}

function disconnect() {
  if (!conn) return;
  const c = conn;
  conn = null;
  try { if (typeof c.disconnect === "function") c.disconnect().catch(() => undefined); }
  catch { }
}

function connect(user) {
  disconnect();
  if (!user || !/^[A-Za-z0-9._]+$/.test(user)) {
    send({ type: "status", status: "error", viewers: 0 });
    return;
  }

  const c = new TikTokLiveConnection(user, {
    processInitialData: false,
    fetchRoomInfoOnConnect: true,
    enableExtendedGiftInfo: false,
  });
  conn = c;

  /* ---------- чат ---------- */
  c.on(WebcastEvent.CHAT, (d) => {
    // Ник берём как есть: эмодзи внутри ника (✊, 🇺🇬 …) — обычный Unicode,
    // рендерится приложением без конвертации.
    const author = d.user?.nickname || d.user?.uniqueId || "";
    const raw = String(d.content || d.comment || "");
    if (!raw) return;

    // Кастомные эмодзи TikTok приходят отдельным массивом с URL картинок.
    // Вставляем их в текст меткой [[e|URL|имя]] — рендерер показывает картинку.
    let text = raw;
    const emotes = Array.isArray(d.emotes) ? d.emotes : [];
    for (const em of emotes) {
      const url = (em.image?.urlList || [])[0];
      if (!url || !em.emoji) continue;
      text = text.split(em.emoji).join("[[e|" + url + "|" + em.emoji + "]]");
    }

    send({ type: "chat", author, text: text.slice(0, 500) });
  });

  /* ---------- подарки ---------- */
  c.on(WebcastEvent.GIFT, (d) => {
    const author = d.user?.nickname || d.user?.uniqueId || "";
    const g = d.giftDetails || {};
    const name = g.giftName || "подарок";
    const diamonds = Number(g.diamondCount || 0);
    const repeat = Number(d.repeatCount || 1);
    send({
      type: "event",
      kind: "gift",
      author,
      text: repeat > 1 ? `отправил подарок «${name}» ×${repeat}` : `отправил подарок «${name}»`,
      amount: diamonds > 0 ? diamonds : undefined,
    });
  });

  /* ---------- подписка / репост ---------- */
  c.on(WebcastEvent.SOCIAL, (d) => {
    const author = d.user?.nickname || d.user?.uniqueId || "";
    const t = String(d.displayType || "");
    if (t.includes("follow")) send({ type: "event", kind: "sub", author, text: "подписался на канал" });
    else if (t.includes("share")) send({ type: "event", kind: "share", author, text: "поделился трансляцией" });
    else if (t.includes("join")) send({ type: "event", kind: "join", author, text: "присоединился к эфиру" });
  });

  /* ---------- вход зрителя ---------- */
  c.on(WebcastEvent.MEMBER, (d) => {
    const author = d.user?.nickname || d.user?.uniqueId || "";
    send({ type: "event", kind: "join", author, text: "зашёл в эфир" });
  });

  /* ---------- лайки ---------- */
  c.on(WebcastEvent.LIKE, (d) => {
    const author = d.user?.nickname || d.user?.uniqueId || "";
    const total = Number(d.totalLikeCount || 0);
    send({ type: "event", kind: "like", author, text: `лайков на стриме: ${total}` });
  });

  /* ---------- зрители ---------- */
  c.on(WebcastEvent.ROOM_USER, (d) => {
    send({ type: "status", status: "online", viewers: Number(d.total || d.viewerCount || 0) });
  });

  /* ---------- конец эфира ---------- */
  c.on(WebcastEvent.STREAM_END, () => {
    send({ type: "status", status: "offline", viewers: 0 });
  });

  c.on(WebcastEvent.DISCONNECT, () => {
    send({ type: "status", status: "connected", viewers: 0 });
  });

  c.on(WebcastEvent.ERROR, (err) => log(err?.message || String(err)));

  /* ---------- подключение ---------- */
  c.connect()
    .then((state) => {
      const viewers = Number(state?.roomInfo?.userCount || 0);
      send({ type: "ready", user, roomId: state?.roomId || "" });
      send({ type: "status", status: "online", viewers });
    })
    .catch((err) => {
      const msg = String(err?.message || err);
      log("connect failed: " + msg);
      const offline = /offline|isn.t online|not live/i.test(msg);
      send({ type: "status", status: offline ? "offline" : "error", viewers: 0 });
      // транзиентная ошибка — пробуем снова через паузу
      if (!offline) {
        setTimeout(() => {
          if (!conn) return;
          log("повторное подключение");
          connect(user);
        }, 15000);
      }
    });
}

/* ---------- команды от хоста ---------- */

let buffer = "";
process.stdin.setEncoding("utf8");

process.stdin.on("data", (chunk) => {
  buffer += chunk;
  let idx;
  while ((idx = buffer.indexOf("\n")) >= 0) {
    const line = buffer.slice(0, idx).trim();
    buffer = buffer.slice(idx + 1);
    if (!line) continue;

    let cmd;
    try { cmd = JSON.parse(line); } catch { continue; }

    if (cmd.type === "connect") {
      log("connect: " + cmd.user);
      connect(String(cmd.user || "").trim());
    } else if (cmd.type === "disconnect") {
      disconnect();
      send({ type: "status", status: "connected", viewers: 0 });
    }
  }
});

process.stdin.on("end", () => { disconnect(); process.exit(0); });

process.on("uncaughtException", (err) => log("uncaught: " + (err?.message || err)));
process.on("unhandledRejection", (err) => log("unhandled: " + String(err)));

send({ type: "log", message: "tiktok bridge запущен (tiktok-live-connector)" });
