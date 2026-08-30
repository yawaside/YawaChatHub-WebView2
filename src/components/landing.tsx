"use client";

// ─── Сайт-витрина YawaChatHub (ТЗ §10) ──────────────────────────────────────
import dynamic from "next/dynamic";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { motion } from "framer-motion";
import {
  ArrowDown, AudioLines, Download, Filter, Gamepad2, GripHorizontal,
  Lock, MousePointerClick, Package, Radio, Sparkles,
  MonitorPlay, Zap, ShieldOff, Move,
} from "lucide-react";

function Github({ size = 16, className }: { size?: number; className?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden>
      <path d="M12 .5A11.5 11.5 0 0 0 .5 12c0 5.08 3.29 9.39 7.86 10.91.58.11.79-.25.79-.55v-2.15c-3.2.69-3.87-1.36-3.87-1.36-.52-1.33-1.28-1.69-1.28-1.69-1.04-.71.08-.7.08-.7 1.15.08 1.76 1.19 1.76 1.19 1.03 1.75 2.69 1.25 3.34.95.1-.74.4-1.25.72-1.53-2.55-.29-5.23-1.28-5.23-5.68 0-1.26.45-2.28 1.19-3.09-.12-.29-.52-1.46.11-3.05 0 0 .97-.31 3.18 1.18a11 11 0 0 1 5.8 0c2.2-1.49 3.17-1.18 3.17-1.18.63 1.59.23 2.76.12 3.05.74.81 1.18 1.83 1.18 3.09 0 4.41-2.69 5.38-5.25 5.67.41.35.77 1.05.77 2.12v3.15c0 .3.21.67.8.55A11.5 11.5 0 0 0 23.5 12 11.5 11.5 0 0 0 12 .5Z" />
    </svg>
  );
}
import { APP_VERSION } from "@/lib/version";
import { PLATFORMS, DEFAULT_HOTKEYS, HOTKEY_ACTIONS, WIDGET_PRESETS, type ChatMsg, type WidgetConfig, DEFAULT_SETTINGS } from "@/lib/types";
import { makeMsg } from "@/lib/chat-sim";
import { lookVars, widgetLike } from "@/lib/look";
import { ChatMessageView, lookFromChatView } from "./chat";
import { WidgetMsgRow } from "./widget-row";
import { cn } from "@/lib/utils";

const DesktopDemo = dynamic(() => import("@/components/desktop-app"), {
  ssr: false,
  loading: () => (
    <div className="grid h-full place-items-center">
      <div className="flex items-center gap-2 text-sm" style={{ color: "var(--muted)" }}>
        <span className="size-3 animate-spin rounded-full border-2 border-current border-t-transparent" />
        Загрузка демо…
      </div>
    </div>
  ),
});

const GITHUB = "https://github.com/yawaside/YawaChatHub-WebView2";
const RELEASES = `${GITHUB}/releases`;

const Reveal = ({ children, delay = 0, className }: { children: React.ReactNode; delay?: number; className?: string }) => (
  <motion.div
    className={className}
    initial={{ opacity: 0, y: 26 }}
    whileInView={{ opacity: 1, y: 0 }}
    viewport={{ once: true, margin: "-70px" }}
    transition={{ duration: 0.65, delay, ease: [0.22, 1, 0.36, 1] }}
  >
    {children}
  </motion.div>
);

function Logo({ size = 28 }: { size?: number }) {
  return (
    <span className="grid place-items-center rounded-xl" style={{ width: size, height: size, background: "linear-gradient(135deg, var(--accent), var(--accent-2))", boxShadow: "0 6px 20px var(--glow)" }}>
      <svg width={size * 0.55} height={size * 0.55} viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        <path d="M8 9h8M8 12h5" />
      </svg>
    </span>
  );
}

function Nav() {
  const [scrolled, setScrolled] = useState(false);
  useEffect(() => {
    const on = () => setScrolled(window.scrollY > 16);
    on();
    window.addEventListener("scroll", on, { passive: true });
    return () => window.removeEventListener("scroll", on);
  }, []);
  const links: [string, string][] = [
    ["Демо", "#demo"], ["Возможности", "#features"], ["Виджет OBS", "#widget"], ["Оверлей", "#overlay"], ["Горячие клавиши", "#hotkeys"], ["Скачать", "#download"],
  ];
  return (
    <header
      className="fixed inset-x-0 top-0 z-50 transition-all duration-300"
      style={scrolled ? { backdropFilter: "blur(16px)", background: "color-mix(in srgb, var(--bg) 72%, transparent)", borderBottom: "1px solid var(--border)" } : undefined}
    >
      <div className="mx-auto flex h-16 max-w-7xl items-center gap-4 px-4">
        <a href="#top" className="flex items-center gap-2.5">
          <Logo />
          <span className="text-[15px] font-bold tracking-tight">YawaChatHub</span>
        </a>
        <nav className="mx-auto hidden items-center gap-1 lg:flex">
          {links.map(([name, href]) => (
            <a key={href} href={href} className="rounded-lg px-3 py-1.5 text-sm font-medium transition-colors hover:text-[var(--text)]" style={{ color: "var(--muted)" }}>
              {name}
            </a>
          ))}
        </nav>
        <span className="hidden rounded-md px-2 py-1 text-[11px] font-bold sm:block" style={{ background: "var(--panel-2)", color: "var(--accent)" }}>v{APP_VERSION}</span>
        <a href={GITHUB} target="_blank" rel="noreferrer" className="btn btn-ghost !p-2.5" title="GitHub">
          <Github size={17} />
        </a>
        <a href="#download" className="btn btn-accent !py-2 text-sm max-sm:hidden">Скачать</a>
      </div>
    </header>
  );
}

function useLiveMessages(pool: ChatMsg[], visible: number, intervalMs: number) {
  const [list, setList] = useState<ChatMsg[]>(() => pool.slice(0, visible));
  useEffect(() => {
    let i = visible;
    const t = setInterval(() => {
      setList((l) => [...l.slice(-(visible - 1)), pool[i % pool.length]]);
      i++;
    }, intervalMs);
    return () => clearInterval(t);
  }, [pool, visible, intervalMs]);
  return list;
}

function HeroPreview() {
  const pool = useMemo(
    () => [
      makeMsg("twitch", "yawa_gg", "neon_dream", "катка просто имба Kappa"),
      makeMsg("youtube", "@yawastream", "ИгроманPRO", "привет из чата ютуба! стрим топ"),
      makeMsg("kick", "yawa", "greenmachine", "го розыгрыш после катки EZ"),
      makeMsg("tiktok", "@yawa.gg", "rek_for_you", "попал сюда из реков, залип monkaS"),
      makeMsg("vk", "yawa_live", "ivan_petrov", "смотрю всю семьёй, привет!"),
      makeMsg("twitch", "yawa_gg", "luna_plays", "обожаю этот момент LUL LUL"),
      makeMsg("youtube", "@yawastream", "dmitriy_v", "а во сколько завтра стрим?"),
      makeMsg("kick", "yawa", "fast_fingers", "красиво сработал 4Head"),
    ],
    [],
  );
  const msgs = useLiveMessages(pool, 5, 2100);
  const look = lookFromChatView({ ...DEFAULT_SETTINGS.chatView, style: "classic", fontSize: 13, radius: 14 });
  return (
    <div className="panel relative flex h-[380px] flex-col overflow-hidden p-3" style={{ boxShadow: "var(--shadow)" }}>
      <div className="mb-2 flex items-center gap-2 border-b pb-2" style={{ borderColor: "var(--border)" }}>
        <span className="size-2 rounded-full live-dot" style={{ background: "var(--ok)" }} />
        <span className="text-xs font-bold uppercase tracking-widest" style={{ color: "var(--muted)" }}>Единая лента · live</span>
        <span className="ml-auto flex items-end gap-[3px]" title="Озвучка активна">
          {[0.9, 0.5, 1.1, 0.7].map((d, i) => (
            <span key={i} className="eq-bar w-[3px] rounded-full" style={{ height: 12, background: "var(--accent-2)", animationDelay: `${i * 0.13}s`, animationDuration: `${d}s` }} />
          ))}
        </span>
      </div>
      <div className="flex min-h-0 flex-1 flex-col justify-end gap-1.5 overflow-hidden">
        {msgs.map((m) => (
          <ChatMessageView key={m.id} msg={m} look={look} fx="fx-slide-up" dur={0.4} />
        ))}
      </div>
      <div className="pointer-events-none absolute inset-x-0 top-10 h-14" style={{ background: "linear-gradient(180deg, var(--bg), transparent)" }} />
    </div>
  );
}

function Hero() {
  return (
    <section id="top" className="relative overflow-hidden pb-14 pt-32">
      <div className="mx-auto grid max-w-7xl items-center gap-12 px-4 lg:grid-cols-[1.1fr_1fr]">
        <div>
          <Reveal>
            <div className="mb-5 inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-semibold" style={{ background: "var(--panel)", border: "1px solid var(--border)", color: "var(--muted)" }}>
              <Sparkles size={13} style={{ color: "var(--accent)" }} />
              Версия {APP_VERSION} · Windows 10 / 11 · portable + установщик
            </div>
          </Reveal>
          <Reveal delay={0.05}>
            <h1 className="font-display text-[34px] font-extrabold leading-[1.12] tracking-tight sm:text-[52px]">
              Все чаты стрима —{" "}
              <span className="glow-text" style={{ background: "linear-gradient(120deg, var(--accent), var(--accent-2))", WebkitBackgroundClip: "text", backgroundClip: "text", color: "transparent" }}>
                в одной ленте
              </span>{" "}
              с озвучкой
            </h1>
          </Reveal>
          <Reveal delay={0.12}>
            <p className="mt-5 max-w-xl text-base leading-relaxed sm:text-lg" style={{ color: "var(--muted)" }}>
              YawaChatHub читает чаты пяти площадок одновременно, озвучивает сообщения стандартными
              голосами Windows (SAPI5), отдаёт картинку в OBS и показывает чат поверх игры.
            </p>
          </Reveal>
          <Reveal delay={0.18}>
            <div className="mt-7 flex flex-wrap items-center gap-3">
              <a href={RELEASES} target="_blank" rel="noreferrer" className="btn btn-accent !px-5 !py-3">
                <Package size={17} /> Portable exe
              </a>
              <a href={RELEASES} target="_blank" rel="noreferrer" className="btn !px-5 !py-3">
                <Download size={17} /> Установщик
              </a>
              <a href="#demo" className="btn btn-ghost !px-5 !py-3" style={{ color: "var(--accent-2)" }}>
                Живое демо <ArrowDown size={15} />
              </a>
            </div>
          </Reveal>
          <Reveal delay={0.24}>
            <div className="mt-8 flex flex-wrap items-center gap-x-5 gap-y-2">
              {PLATFORMS.map((p) => (
                <span key={p.id} className="flex items-center gap-2 text-sm font-medium" style={{ color: "var(--muted)" }}>
                  <span className="size-2.5 rounded-full" style={{ background: p.color, boxShadow: `0 0 12px ${p.color}` }} />
                  {p.name}
                </span>
              ))}
            </div>
          </Reveal>
        </div>
        <Reveal delay={0.15} className="max-lg:hidden">
          <HeroPreview />
        </Reveal>
      </div>
    </section>
  );
}

function DemoSection() {
  return (
    <section id="demo" className="py-16">
      <div className="mx-auto max-w-7xl px-4">
        <Reveal>
          <h2 className="font-display text-2xl font-bold tracking-tight sm:text-4xl">Живое демо приложения</h2>
          <p className="mt-3 max-w-2xl text-[15px] leading-relaxed" style={{ color: "var(--muted)" }}>
            Это настоящий интерфейс YawaChatHub, работающий прямо в браузере: лента, озвучка,
            настройки виджета и оверлея. Всё кликабельно — попробуйте.
          </p>
        </Reveal>
        <Reveal delay={0.1}>
          <div className="mt-8 overflow-hidden rounded-2xl" style={{ border: "1px solid var(--border-2)", boxShadow: "var(--shadow)" }}>
            <div className="flex items-center gap-2 px-4 py-2.5" style={{ background: "var(--bg-2)", borderBottom: "1px solid var(--border)" }}>
              <span className="size-3 rounded-full" style={{ background: "#fb7185" }} />
              <span className="size-3 rounded-full" style={{ background: "#fbbf24" }} />
              <span className="size-3 rounded-full" style={{ background: "#34d399" }} />
              <span className="mx-auto flex w-full max-w-sm items-center gap-2 rounded-lg px-3 py-1 text-xs" style={{ background: "var(--panel-2)", color: "var(--muted)" }}>
                <Lock size={11} /> yawachat/app
              </span>
            </div>
            <div className="h-[700px]" style={{ background: "var(--bg)" }}>
              <DesktopDemo demo />
            </div>
          </div>
          <p className="mt-3 text-center text-xs" style={{ color: "var(--muted)" }}>
            В desktop-сборке озвучка идёт через голоса Windows SAPI5 с принудительным ru-RU; в браузере — через Web Speech API.
          </p>
        </Reveal>
      </div>
    </section>
  );
}

const FEATURES = [
  { icon: Radio, title: "Пять площадок", text: "Twitch, YouTube Live, VK Video Live, Kick и TikTok Live — в одной ленте с фильтрами, поиском и автопрокруткой." },
  { icon: AudioLines, title: "Озвучка голосом", text: "Стандартные голоса Windows (SAPI5), SSML с принудительным ru-RU, очередь FIFO и шаблон фразы." },
  { icon: Filter, title: "Фильтры и банворды", text: "Ссылки → «ссылка», маскировка слов на «пип», игнор авторов, анти-повтор, лимиты в минуту и CAPS-контроль." },
  { icon: MonitorPlay, title: "OBS-виджет", text: "Статичная локальная ссылка — оформление доставляется по соединению и применяется мгновенно." },
  { icon: Gamepad2, title: "Игровой оверлей", text: "Прозрачное окно поверх игры: перетаскивается, сквозные клики, фиксация позиции, запоминает размер." },
  { icon: ShieldOff, title: "Без телеметрии", text: "Никакой аналитики, нейросетей и автоскачивания моделей. Только системные компоненты Windows." },
];

function Features() {
  return (
    <section id="features" className="py-16">
      <div className="mx-auto max-w-7xl px-4">
        <Reveal>
          <h2 className="font-display text-2xl font-bold tracking-tight sm:text-4xl">Возможности</h2>
        </Reveal>
        <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {FEATURES.map((f, i) => (
            <Reveal key={f.title} delay={i * 0.05}>
              <div className="panel group h-full p-5 transition-transform duration-300 hover:-translate-y-1">
                <span className="mb-4 grid size-11 place-items-center rounded-xl transition-transform duration-300 group-hover:scale-110" style={{ background: "linear-gradient(135deg, color-mix(in srgb, var(--accent) 24%, transparent), color-mix(in srgb, var(--accent-2) 14%, transparent))", border: "1px solid color-mix(in srgb, var(--accent) 30%, transparent)", color: "var(--accent)" }}>
                  <f.icon size={19} />
                </span>
                <div className="text-base font-bold">{f.title}</div>
                <p className="mt-1.5 text-sm leading-relaxed" style={{ color: "var(--muted)" }}>{f.text}</p>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}

function WidgetSection() {
  const [preset, setPreset] = useState("minimal-dark");
  const [opacity, setOpacity] = useState(70);
  const cfg: WidgetConfig = useMemo(() => {
    const p = WIDGET_PRESETS.find((x) => x.id === preset) ?? WIDGET_PRESETS[0];
    return { ...DEFAULT_SETTINGS.widget, ...p.patch, bgOpacity: opacity };
  }, [preset, opacity]);
  const samples = useMemo(
    () => [
      makeMsg("twitch", "demo", "neon_dream", "Виджет обновляется мгновенно Kappa"),
      makeMsg("youtube", "demo", "ИгроманPRO", "ссылка в OBS всегда одна и та же"),
      makeMsg("kick", "demo", "greenmachine", "а выглядит fresh! EZ"),
    ],
    [],
  );
  return (
    <section id="widget" className="py-16">
      <div className="mx-auto grid max-w-7xl items-center gap-10 px-4 lg:grid-cols-2">
        <Reveal>
          <h2 className="font-display text-2xl font-bold tracking-tight sm:text-4xl">Виджет для OBS</h2>
          <p className="mt-4 text-[15px] leading-relaxed" style={{ color: "var(--muted)" }}>
            Один HTML-источник без внешних зависимостей. Ссылка статична: стиль, тема и размеры
            приезжают по соединению — OBS обновляется мгновенно, без пересборки URL.
            Попробуйте прямо здесь:
          </p>
          <div className="mt-6 flex flex-wrap gap-2">
            {WIDGET_PRESETS.map((p) => (
              <button
                key={p.id}
                type="button"
                onClick={() => setPreset(p.id)}
                className={cn("rounded-full px-3.5 py-1.5 text-xs font-semibold transition-all")}
                style={{
                  background: preset === p.id ? "linear-gradient(135deg, var(--accent), color-mix(in srgb, var(--accent-2) 60%, var(--accent)))" : "var(--panel-2)",
                  color: preset === p.id ? "var(--accent-text)" : "var(--muted)",
                  border: `1px solid ${preset === p.id ? "transparent" : "var(--border)"}`,
                }}
              >
                {p.name}
              </button>
            ))}
          </div>
          <div className="mt-6 max-w-xs">
            <div className="mb-1 flex justify-between text-xs font-semibold">
              <span style={{ color: "var(--muted)" }}>Прозрачность подложки</span>
              <span style={{ color: "var(--accent)" }}>{opacity}%</span>
            </div>
            <input type="range" className="slider" min={0} max={100} value={opacity} style={{ "--val": `${opacity}%` } as React.CSSProperties} onChange={(e) => setOpacity(Number(e.target.value))} />
          </div>
        </Reveal>
        <Reveal delay={0.1}>
          <div className="overflow-hidden rounded-2xl p-5" style={{ border: "1px solid var(--border-2)", background: "repeating-conic-gradient(rgba(128,128,128,.14) 0% 25%, transparent 0% 50%) 0 0 / 24px 24px, linear-gradient(150deg, #20293c, #10141f)", boxShadow: "var(--shadow)" }}>
            <div className="flex flex-col justify-end gap-2.5" style={{ minHeight: 300, ...lookVars(widgetLike(cfg), 0.35) }}>
              {samples.map((m) => (
                <WidgetMsgRow key={m.id + preset} msg={m} fx={cfg.effect} opts={{ style: cfg.style, showPlatform: cfg.showPlatform, showTime: cfg.showTime }} />
              ))}
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}

function GameModeSection() {
  const o = DEFAULT_SETTINGS.overlay;
  const samples = useMemo(
    () => [
      makeMsg("twitch", "demo", "quiet_owl", "слева обходят, аккуратно!"),
      makeMsg("vk", "demo", "prostotak", "красивый фраг monkaS"),
      makeMsg("youtube", "demo", "NataliStar", "го ещё раунд после этой катки"),
      makeMsg("tiktok", "demo", "zoomer_ok", "этот момент в тикток Jebaited"),
    ],
    [],
  );
  return (
    <section id="overlay" className="py-16">
      <div className="mx-auto max-w-7xl px-4">
        <Reveal>
          <h2 className="font-display text-2xl font-bold tracking-tight sm:text-4xl">Игровой режим</h2>
          <p className="mt-3 max-w-2xl text-[15px] leading-relaxed" style={{ color: "var(--muted)" }}>
            Чат всегда перед глазами — поверх игры. Окно прозрачное, не крадёт клики и не отвлекает.
          </p>
        </Reveal>
        <Reveal delay={0.1}>
          <div className="relative mt-8 overflow-hidden rounded-2xl" style={{ minHeight: 420, border: "1px solid var(--border-2)", boxShadow: "var(--shadow)", background: "radial-gradient(600px 260px at 75% 8%, rgba(255,190,90,.4), transparent 55%), linear-gradient(180deg, #2a4066 0%, #47664d 52%, #2c4429 100%)" }}>
            <div className="absolute left-10 top-10 size-24 rounded-full opacity-90" style={{ background: "radial-gradient(circle, #ffedb8, #ffb84d)", boxShadow: "0 0 80px rgba(255,200,100,.6)" }} />
            <div className="absolute bottom-0 left-[8%] h-28 w-[38%] rounded-t-[100%] opacity-70" style={{ background: "#1d3320" }} />
            <div className="absolute bottom-0 right-[30%] h-36 w-[30%] rounded-t-[100%] opacity-50" style={{ background: "#182b18" }} />
            <div className="absolute inset-x-0 bottom-0 h-24" style={{ background: "linear-gradient(180deg, transparent, rgba(0,0,0,.4))" }} />
            <div className="float-y absolute right-5 top-5 w-[320px] max-w-[82%]" style={lookVars({ theme: "minimal-dark", fontSize: 12, bgOpacity: o.bgOpacity, radius: o.radius, shadow: true, border: true, textColor: "", nameColor: "", bgColor: "", bgImage: "" }, 0.3)}>
              <div className="mb-1.5 flex items-center gap-2 px-3 py-1.5 text-[11px] font-bold" style={{ borderRadius: "var(--w-radius)", background: "var(--w-bg)", border: "var(--w-border-w) solid var(--w-border)", color: "var(--w-text)" }}>
                <span className="size-1.5 rounded-full live-dot" style={{ background: "#34d399" }} /> YawaChatHub
                <span className="ml-auto font-medium opacity-55">тяните за шапку</span>
              </div>
              <div className="flex flex-col gap-1.5">
                {samples.map((m) => (
                  <WidgetMsgRow key={m.id} msg={m} fx="fx-slide-up" opts={{ style: "compact", showPlatform: true, showTime: false }} />
                ))}
              </div>
            </div>
          </div>
        </Reveal>
        <div className="mt-4 grid gap-4 sm:grid-cols-3">
          {[
            { icon: Move, t: "Перетаскивается", d: "Тяните за шапку окна — позиция и размер сохраняются в настройки." },
            { icon: MousePointerClick, t: "Сквозные клики", d: "Одно сочетание — и окно перестаёт перехватывать мышь, клики уходят в игру." },
            { icon: GripHorizontal, t: "Фиксация позиции", d: "Закрепите окно, чтобы случайно не сдвинуть его в разгар катки." },
          ].map((c, i) => (
            <Reveal key={c.t} delay={i * 0.06}>
              <div className="panel h-full p-5">
                <c.icon size={18} style={{ color: "var(--accent-2)" }} />
                <div className="mt-3 text-sm font-bold">{c.t}</div>
                <p className="mt-1 text-[13px] leading-relaxed" style={{ color: "var(--muted)" }}>{c.d}</p>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}

function HotkeysSection() {
  return (
    <section id="hotkeys" className="py-16">
      <div className="mx-auto max-w-4xl px-4">
        <Reveal>
          <h2 className="font-display text-2xl font-bold tracking-tight sm:text-4xl">Горячие клавиши</h2>
          <p className="mt-3 text-[15px]" style={{ color: "var(--muted)" }}>
            Работают глобально через WinAPI RegisterHotKey — даже когда окно свёрнуто в трей. Все сочетания переназначаются.
          </p>
        </Reveal>
        <Reveal delay={0.08}>
          <div className="panel mt-8 overflow-hidden">
            {HOTKEY_ACTIONS.map((a, i) => (
              <div key={a.id} className="flex items-center justify-between gap-4 px-5 py-3" style={{ borderTop: i ? "1px solid var(--border)" : "none" }}>
                <span className="text-sm font-medium">{a.name}</span>
                <span className="kbd">{DEFAULT_HOTKEYS[a.id].replaceAll("+", " + ")}</span>
              </div>
            ))}
          </div>
        </Reveal>
      </div>
    </section>
  );
}

function DownloadSection() {
  return (
    <section id="download" className="py-16">
      <div className="mx-auto max-w-5xl px-4">
        <Reveal>
          <h2 className="font-display text-center text-2xl font-bold tracking-tight sm:text-4xl">Скачать YawaChatHub</h2>
          <p className="mx-auto mt-3 max-w-xl text-center text-[15px]" style={{ color: "var(--muted)" }}>
            Windows 10 / 11 (x64). Сборка, тег и релиз создаются автоматически при каждом обновлении.
          </p>
        </Reveal>
        <div className="mt-10 grid gap-4 sm:grid-cols-2">
          <Reveal>
            <a href={RELEASES} target="_blank" rel="noreferrer" className="panel group block h-full p-6 transition-transform duration-300 hover:-translate-y-1">
              <span className="grid size-12 place-items-center rounded-xl" style={{ background: "linear-gradient(135deg, var(--accent), var(--accent-2))", color: "#fff", boxShadow: "0 10px 26px var(--glow)" }}>
                <Package size={20} />
              </span>
              <div className="mt-4 text-lg font-bold">Portable</div>
              <p className="mt-1 text-sm leading-relaxed" style={{ color: "var(--muted)" }}>
                Один файл YawaChatHub.exe — без установки. Распаковал, запустил, работает.
              </p>
              <span className="mt-4 inline-flex items-center gap-1.5 text-sm font-semibold" style={{ color: "var(--accent)" }}>
                YawaChatHub.exe <ArrowDown size={14} className="transition-transform group-hover:translate-y-0.5" />
              </span>
            </a>
          </Reveal>
          <Reveal delay={0.08}>
            <a href={RELEASES} target="_blank" rel="noreferrer" className="panel group block h-full p-6 transition-transform duration-300 hover:-translate-y-1">
              <span className="grid size-12 place-items-center rounded-xl" style={{ background: "var(--panel-2)", border: "1px solid var(--border-2)", color: "var(--accent-2)" }}>
                <Download size={20} />
              </span>
              <div className="mt-4 text-lg font-bold">Установщик (NSIS)</div>
              <p className="mt-1 text-sm leading-relaxed" style={{ color: "var(--muted)" }}>
                Ярлыки в меню «Пуск» и на рабочем столе, выбор папки, удаление через «Программы и компоненты».
              </p>
              <span className="mt-4 inline-flex items-center gap-1.5 text-sm font-semibold" style={{ color: "var(--accent-2)" }}>
                YawaChatHub-Setup.exe <ArrowDown size={14} className="transition-transform group-hover:translate-y-0.5" />
              </span>
            </a>
          </Reveal>
        </div>
        <Reveal delay={0.12}>
          <div className="mt-6 text-center">
            <Link href="/app" className="btn btn-accent !px-6 !py-3">
              <Zap size={16} /> Открыть веб-версию приложения
            </Link>
            <div className="mt-3 text-xs" style={{ color: "var(--muted)" }}>
              Тот же интерфейс, что и в desktop: лента, настройки, виджет и оверлей.
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}

function Footer() {
  return (
    <footer className="mt-10 border-t py-10" style={{ borderColor: "var(--border)" }}>
      <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-4 px-4">
        <Logo size={24} />
        <span className="text-sm font-bold">YawaChatHub</span>
        <span className="rounded-md px-2 py-0.5 text-[11px] font-bold" style={{ background: "var(--panel-2)", color: "var(--accent)" }}>v{APP_VERSION}</span>
        <span className="text-xs" style={{ color: "var(--muted)" }}>MIT</span>
        <div className="mx-auto hidden flex-wrap items-center gap-x-4 gap-y-1 md:flex">
          {PLATFORMS.map((p) => (
            <span key={p.id} className="flex items-center gap-1.5 text-xs" style={{ color: "var(--muted)" }}>
              <span className="size-1.5 rounded-full" style={{ background: p.color }} />
              {p.name}
            </span>
          ))}
        </div>
        <div className="ml-auto flex items-center gap-2">
          <Link href="/app" className="btn btn-ghost !px-3 !py-1.5 text-xs">Приложение</Link>
          <Link href="/overlay" className="btn btn-ghost !px-3 !py-1.5 text-xs">Оверлей</Link>
          <a href={GITHUB} target="_blank" rel="noreferrer" className="btn btn-ghost !p-2" title="GitHub"><Github size={15} /></a>
        </div>
      </div>
    </footer>
  );
}

export default function Landing() {
  return (
    <div className="min-h-screen" style={{ background: "var(--bg)", color: "var(--text)" }}>
      <Nav />
      <Hero />
      <DemoSection />
      <Features />
      <WidgetSection />
      <GameModeSection />
      <HotkeysSection />
      <DownloadSection />
      <Footer />
    </div>
  );
}
