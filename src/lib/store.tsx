"use client";

// ─── Глобальное состояние приложения ────────────────────────────────────────
// Автосохранение: любое изменение → немедленный settings.patch (ТЗ §14.2),
// без кнопок «Сохранить»; в шапке — пульсирующий индикатор «сохранено».
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  DEFAULT_SETTINGS,
  platformById,
  validateChannelId,
  type Channel,
  type ChatMsg,
  type PlatformId,
  type Settings,
} from "./types";
import { comboMatches, deepMerge, uid } from "./utils";
import { ChatSimulator, makeMsg } from "./chat-sim";
import { buildPhrase, TtsEngine, TtsFilter } from "./tts";

export interface Toast {
  id: string;
  text: string;
}

interface AppCtxValue {
  settings: Settings;
  loaded: boolean;
  patch: (p: Record<string, unknown>) => void;
  saving: boolean;
  demo: boolean;

  messages: ChatMsg[];
  clearFeed: () => void;

  addChannel: (platform: PlatformId, channelId: string) => string | null;
  removeChannel: (platform: PlatformId, channelId: string) => void;

  toasts: Toast[];
  toast: (text: string) => void;

  widgetClients: number;

  ttsQueue: number;
  ttsPaused: boolean;
  ttsToggle: () => void;
  ttsPauseToggle: () => void;
  ttsSkip: () => void;
  ttsClear: () => void;
  speakTest: (text?: string) => void;

  publishMsg: (msg: ChatMsg) => void;
}

const AppCtx = createContext<AppCtxValue | null>(null);

export function useApp(): AppCtxValue {
  const v = useContext(AppCtx);
  if (!v) throw new Error("useApp вне AppProvider");
  return v;
}

export function AppProvider({ children, demo = false }: { children: ReactNode; demo?: boolean }) {
  const [settings, setSettings] = useState<Settings>(DEFAULT_SETTINGS);
  const [loaded, setLoaded] = useState(false);
  const [messages, setMessages] = useState<ChatMsg[]>([]);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [saving, setSaving] = useState(false);
  const [widgetClients, setWidgetClients] = useState(0);
  const [ttsQueue, setTtsQueue] = useState(0);
  const [ttsPaused, setTtsPaused] = useState(false);

  const settingsRef = useRef(settings);
  settingsRef.current = settings;
  const simRef = useRef<ChatSimulator | null>(null);
  const engineRef = useRef<TtsEngine | null>(null);
  const filterRef = useRef<TtsFilter | null>(null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const demoHydrated = useRef(false);

  // ── тосты (ТЗ §14.3: 2.4 c, fixed bottom center) ──────────────────────
  const toast = useCallback((text: string) => {
    const id = uid();
    setToasts((t) => [...t.slice(-3), { id, text }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 2400);
  }, []);

  // ── загрузка настроек ──────────────────────────────────────────────────
  useEffect(() => {
    let alive = true;
    if (demo) {
      try {
        const raw = localStorage.getItem("yawa-demo-settings");
        if (raw) {
          const parsed = JSON.parse(raw) as Settings;
          if (alive) setSettings(deepMerge(DEFAULT_SETTINGS, parsed));
        }
      } catch {}
      demoHydrated.current = true;
      setLoaded(true);
      return;
    }
    fetch("/api/settings", { cache: "no-store" })
      .then((r) => r.json())
      .then((d) => {
        if (alive && d?.settings) setSettings(deepMerge(DEFAULT_SETTINGS, d.settings));
      })
      .catch(() => {})
      .finally(() => alive && setLoaded(true));
    return () => {
      alive = false;
    };
  }, [demo]);

  // ── тема интерфейса ────────────────────────────────────────────────────
  useEffect(() => {
    document.documentElement.dataset.theme = settings.theme;
  }, [settings.theme]);

  // ── автосохранение ─────────────────────────────────────────────────────
  const pulse = useCallback(() => {
    setSaving(true);
    if (saveTimer.current) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => setSaving(false), 1900);
  }, []);

  const patch = useCallback(
    (p: Record<string, unknown>) => {
      setSettings((s) => deepMerge(s, p));
      pulse();
      if (demo) {
        if (demoHydrated.current) {
          setSettings((s) => {
            try {
              localStorage.setItem("yawa-demo-settings", JSON.stringify(s));
            } catch {}
            return s;
          });
        }
        return;
      }
      void fetch("/api/settings", {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(p),
      }).catch(() => {});
    },
    [demo, pulse],
  );

  // ── TTS движок ─────────────────────────────────────────────────────────
  if (!engineRef.current) {
    engineRef.current = new TtsEngine();
    filterRef.current = new TtsFilter(() => settingsRef.current.tts);
  }
  useEffect(() => {
    const eng = engineRef.current!;
    return eng.on((ev) => {
      if (ev.type === "queue") setTtsQueue(ev.size ?? 0);
    });
  }, []);
  useEffect(() => {
    if (engineRef.current) engineRef.current.paused = ttsPaused;
  }, [ttsPaused]);

  // ── приём сообщений ────────────────────────────────────────────────────
  const publishMsg = useCallback(
    (msg: ChatMsg) => {
      setMessages((m) => {
        const next = m.length >= 200 ? m.slice(m.length - 199) : m.slice();
        next.push(msg);
        return next;
      });
      const s = settingsRef.current;
      if (s.tts.enabled && filterRef.current && engineRef.current) {
        const res = filterRef.current.process(msg);
        if (res.ok) {
          engineRef.current.enqueue({
            text: buildPhrase(msg, res.text, s.tts),
            rate: s.tts.rate,
            volume: s.tts.volume,
            voice: s.tts.voiceURI || undefined,
          });
        }
      }
      if (!demo) {
        void fetch("/api/publish", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ type: "chat", msg }),
        }).catch(() => {});
      }
    },
    [demo],
  );

  // ── демо-коннекторы ────────────────────────────────────────────────────
  useEffect(() => {
    if (!loaded) return;
    const sim = new ChatSimulator(
      () => settingsRef.current.channels,
      (msg) => publishMsg(msg),
    );
    simRef.current = sim;
    sim.start();
    const burstTimer = setInterval(() => {
      if (Math.random() < 0.3) sim.burst(4 + Math.floor(Math.random() * 5));
    }, 14000);
    return () => {
      sim.stop();
      clearInterval(burstTimer);
    };
  }, [loaded, publishMsg]);

  // ── число OBS-клиентов (SSE kind=app) ──────────────────────────────────
  useEffect(() => {
    if (demo) return;
    const es = new EventSource("/api/stream?kind=app");
    es.onmessage = (e) => {
      try {
        const d = JSON.parse(e.data) as { type: string; n?: number };
        if (d.type === "clients") setWidgetClients(d.n ?? 0);
      } catch {}
    };
    return () => es.close();
  }, [demo]);

  // ── каналы ─────────────────────────────────────────────────────────────
  const addChannel = useCallback(
    (platform: PlatformId, raw: string): string | null => {
      const v = validateChannelId(platform, raw);
      if (!v.ok) return v.error ?? "Некорректный ник";
      const exists = settingsRef.current.channels.some(
        (c) => c.platform === platform && c.channelId.toLowerCase() === v.value.toLowerCase(),
      );
      if (exists) return "Такой канал уже добавлен";
      const channels: Channel[] = [...settingsRef.current.channels, { platform, channelId: v.value }];
      patch({ channels });
      toast(`${platformById(platform).name}: канал ${v.value} подключён`);
      simRef.current?.burst(3);
      return null;
    },
    [patch, toast],
  );

  const removeChannel = useCallback(
    (platform: PlatformId, channelId: string) => {
      patch({
        channels: settingsRef.current.channels.filter(
          (c) => !(c.platform === platform && c.channelId === channelId),
        ),
      });
      toast("Канал отключён");
    },
    [patch, toast],
  );

  // ── TTS действия ───────────────────────────────────────────────────────
  const ttsToggle = useCallback(() => {
    const next = !settingsRef.current.tts.enabled;
    patch({ tts: { enabled: next } });
    if (!next) engineRef.current?.stopAll();
    toast(next ? "Озвучка включена" : "Озвучка выключена");
  }, [patch, toast]);

  const ttsPauseToggle = useCallback(() => {
    setTtsPaused((p) => {
      toast(!p ? "Озвучка на паузе" : "Озвучка продолжена");
      return !p;
    });
  }, [toast]);

  const ttsSkip = useCallback(() => {
    engineRef.current?.skip();
    toast("Текущее сообщение пропущено");
  }, [toast]);

  const ttsClear = useCallback(() => {
    engineRef.current?.stopAll();
    toast("Очередь озвучки очищена");
  }, [toast]);

  const clearFeed = useCallback(() => setMessages([]), []);

  const speakTest = useCallback(
    (text?: string) => {
      const s = settingsRef.current;
      const sample =
        text ??
        "Привет! Это тестовое сообщение для проверки озвучки чата.";
      const msg = makeMsg("twitch", "demo", "test_streamer", sample);
      const res = filterRef.current?.process(msg) ?? { ok: true, text: sample };
      engineRef.current?.enqueue({
        text: buildPhrase(msg, res.ok ? res.text : sample, s.tts),
        rate: s.tts.rate,
        volume: s.tts.volume,
        voice: s.tts.voiceURI || undefined,
      });
      toast("Тестовая фраза поставлена в очередь");
    },
    [toast],
  );

  // ── горячие клавиши (ТЗ §17; в браузере — пока окно в фокусе) ─────────
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable)) return;
      const map = settingsRef.current.hotkeys;
      for (const [action, combo] of Object.entries(map)) {
        if (!combo || !comboMatches(e, combo)) continue;
        e.preventDefault();
        switch (action) {
          case "tts:toggle": ttsToggle(); break;
          case "tts:pause": ttsPauseToggle(); break;
          case "tts:skip": ttsSkip(); break;
          case "tts:clear": ttsClear(); break;
          case "feed:clear": clearFeed(); toast("Лента очищена"); break;
          case "overlay:toggle": {
            const next = !settingsRef.current.overlay.enabled;
            patch({ overlay: { enabled: next } });
            toast(next ? "Оверлей включён" : "Оверлей выключен");
            break;
          }
          case "overlay:clicks": {
            const next = !settingsRef.current.overlay.clickThrough;
            patch({ overlay: { clickThrough: next } });
            toast(next ? "Сквозные клики включены" : "Сквозные клики выключены");
            break;
          }
          case "window:toggle":
            toast("В браузере сворачивание недоступно — только в desktop");
            break;
        }
        return;
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [patch, toast, ttsToggle, ttsPauseToggle, ttsSkip, ttsClear, clearFeed]);

  const value = useMemo<AppCtxValue>(
    () => ({
      settings, loaded, patch, saving, demo,
      messages, clearFeed,
      addChannel, removeChannel,
      toasts, toast,
      widgetClients,
      ttsQueue, ttsPaused, ttsToggle, ttsPauseToggle, ttsSkip, ttsClear, speakTest,
      publishMsg,
    }),
    [settings, loaded, patch, saving, demo, messages, clearFeed, addChannel, removeChannel,
     toasts, toast, widgetClients, ttsQueue, ttsPaused, ttsToggle, ttsPauseToggle, ttsSkip, ttsClear, speakTest, publishMsg],
  );

  return <AppCtx.Provider value={value}>{children}</AppCtx.Provider>;
}
