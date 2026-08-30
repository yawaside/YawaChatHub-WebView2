"use client";

// ─── Главное окно приложения (ТЗ §9; адрес #/app в desktop) ────────────────
import { useMemo, useState } from "react";
import {
  Bell, BellOff, CheckCheck, ChevronsLeft, ChevronsRight, CloudCheck, Eraser,
  Gamepad2, Info, Keyboard, MessagesSquare, Mic, MicOff, Minus, MonitorPlay,
  Palette, Plus, Search, Square, Trash2, Volume2, X,
} from "lucide-react";
import { AppProvider, useApp } from "@/lib/store";
import { PLATFORMS, platformById, type HotkeyAction, type PlatformId } from "@/lib/types";
import { cn } from "@/lib/utils";
import { APP_VERSION } from "@/lib/version";
import { nativeWindow } from "@/lib/native";
import { ChatFeed, PlatformBadge, lookFromChatView } from "./chat";
import { ChatViewPanel, HotkeysPanel, InterfacePanel, AboutPanel } from "./panels/misc";
import { VoicePanel } from "./panels/voice";
import { WidgetPanel } from "./panels/widget";
import { OverlayPanel } from "./panels/overlay";
import { IconBtn, NumBadge } from "./ui";

type TabId = "chat" | "voice" | "widget" | "overlay" | "hotkeys" | "ui" | "about";

const TABS: { id: TabId; name: string; icon: typeof MessagesSquare }[] = [
  { id: "chat", name: "Чат", icon: MessagesSquare },
  { id: "voice", name: "Озвучка", icon: Volume2 },
  { id: "widget", name: "Виджет OBS", icon: MonitorPlay },
  { id: "overlay", name: "Оверлей", icon: Gamepad2 },
  { id: "hotkeys", name: "Горячие клавиши", icon: Keyboard },
  { id: "ui", name: "Интерфейс", icon: Palette },
  { id: "about", name: "О программе", icon: Info },
];

function TitleBar() {
  const { saving, settings, ttsToggle, toast } = useApp();
  const ttsOn = settings.tts.enabled;
  return (
    <header
      className="app-drag relative z-20 flex h-11 flex-none select-none items-center gap-3 border-b px-3"
      style={{ borderColor: "var(--border)", background: "color-mix(in srgb, var(--bg-2) 72%, transparent)", backdropFilter: "blur(14px)" }}
      onMouseDown={(e) => {
        if (e.button !== 0) return;
        const target = e.target as HTMLElement;
        if (target.closest("button, a, input, select, textarea")) return;
        nativeWindow("drag");
      }}
    >
      <div className="flex items-center gap-2.5">
        <span
          className="grid size-6 place-items-center rounded-lg"
          style={{ background: "linear-gradient(135deg, var(--accent), var(--accent-2))", boxShadow: "0 4px 14px var(--glow)" }}
        >
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
          </svg>
        </span>
        <span className="text-sm font-bold tracking-tight">YawaChatHub</span>
        <span className="rounded-md px-1.5 py-0.5 text-[10px] font-bold" style={{ background: "var(--panel-2)", color: "var(--muted)" }}>v{APP_VERSION}</span>
      </div>

      <div className="mx-auto flex items-center gap-2 text-xs" style={{ color: "var(--muted)" }}>
        {saving ? (
          <span className="save-pulse flex items-center gap-1.5 rounded-full px-3 py-1 font-semibold" style={{ background: "color-mix(in srgb, var(--ok) 14%, transparent)", color: "var(--ok)" }}>
            <CloudCheck size={13} /> Сохранено
          </span>
        ) : (
          <span className="hidden items-center gap-1.5 opacity-50 md:flex">Настройки сохраняются автоматически</span>
        )}
      </div>

      <div className="app-no-drag flex items-center gap-1">
        <IconBtn icon={ttsOn ? Mic : MicOff} title={ttsOn ? "Озвучка включена — выключить" : "Озвучка выключена — включить"} onClick={ttsToggle} active={ttsOn} />
        <span className="mx-1 h-4 w-px" style={{ background: "var(--border-2)" }} />
        <IconBtn
          icon={Minus}
          title="Свернуть"
          onClick={() => {
            if (!nativeWindow("minimize")) toast("Сворачивание доступно в desktop-сборке");
          }}
        />
        <IconBtn
          icon={Square}
          title="Развернуть"
          onClick={() => {
            if (!nativeWindow("maximize")) toast("В браузере окно всегда развёрнуто");
          }}
        />
        <IconBtn
          icon={X}
          title="Закрыть"
          onClick={() => {
            if (!nativeWindow("close")) toast("Закрытие нативного окна доступно в desktop-сборке");
          }}
          danger
        />
      </div>
    </header>
  );
}

function Rail({ tab, setTab }: { tab: TabId; setTab: (t: TabId) => void }) {
  const { settings, patch } = useApp();
  const collapsed = settings.menuCollapsed;
  return (
    <nav
      className="flex flex-none flex-col gap-1 border-r p-2 transition-[width] duration-300"
      style={{ width: collapsed ? 60 : 176, borderColor: "var(--border)", background: "color-mix(in srgb, var(--bg-2) 45%, transparent)" }}
    >
      {TABS.map((t) => {
        const active = tab === t.id;
        return (
          <button
            key={t.id}
            type="button"
            onClick={() => setTab(t.id)}
            title={t.name}
            className={cn("flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold transition-all", collapsed && "justify-center px-0")}
            style={
              active
                ? { background: "linear-gradient(135deg, color-mix(in srgb, var(--accent) 22%, transparent), color-mix(in srgb, var(--accent-2) 12%, transparent))", color: "var(--text)", border: "1px solid color-mix(in srgb, var(--accent) 35%, transparent)", boxShadow: "0 6px 20px var(--glow)" }
                : { color: "var(--muted)", border: "1px solid transparent" }
            }
          >
            <t.icon size={17} style={active ? { color: "var(--accent)" } : undefined} className="flex-none" />
            {!collapsed && <span className="truncate">{t.name}</span>}
          </button>
        );
      })}
      <div className="mt-auto">
        <button
          type="button"
          onClick={() => patch({ menuCollapsed: !collapsed })}
          className={cn("flex w-full items-center gap-3 rounded-xl px-3 py-2 text-xs font-semibold", collapsed && "justify-center px-0")}
          style={{ color: "var(--muted)" }}
          title={collapsed ? "Развернуть меню" : "Свернуть меню"}
        >
          {collapsed ? <ChevronsRight size={15} /> : <ChevronsLeft size={15} />}
          {!collapsed && "Свернуть меню"}
        </button>
      </div>
    </nav>
  );
}

function ChannelsPanel() {
  const { settings, patch, addChannel, removeChannel } = useApp();
  const collapsed = settings.channelsCollapsed;
  const [platform, setPlatform] = useState<PlatformId>("twitch");
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = () => {
    const err = addChannel(platform, name);
    setError(err);
    if (!err) setName("");
  };

  if (collapsed) {
    return (
      <aside className="panel flex w-12 flex-none flex-col items-center gap-2 py-3">
        <button type="button" onClick={() => patch({ channelsCollapsed: false })} className="grid size-8 place-items-center rounded-lg" style={{ color: "var(--muted)" }} title="Развернуть каналы">
          <ChevronsRight size={15} />
        </button>
        {PLATFORMS.map((p) => {
          const n = settings.channels.filter((c) => c.platform === p.id).length;
          return (
            <span key={p.id} className="relative grid size-8 place-items-center rounded-lg text-[9px] font-black uppercase" style={{ background: p.soft, color: p.color }} title={`${p.name}: ${n}`}>
              {p.short}
              {n > 0 && <span className="absolute -right-1 -top-1 grid size-4 place-items-center rounded-full text-[9px] font-bold" style={{ background: "var(--accent)", color: "var(--accent-text)" }}>{n}</span>}
            </span>
          );
        })}
      </aside>
    );
  }

  return (
    <aside className="panel flex w-60 flex-none flex-col overflow-hidden">
      <div className="flex items-center gap-2 border-b px-3 py-2.5" style={{ borderColor: "var(--border)" }}>
        <span className="text-sm font-bold">Каналы</span>
        <NumBadge tone="muted">{settings.channels.length}</NumBadge>
        <button type="button" className="ml-auto" onClick={() => patch({ channelsCollapsed: true })} title="Свернуть в мини-бар" style={{ color: "var(--muted)" }}>
          <ChevronsLeft size={15} />
        </button>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto p-2">
        {settings.channels.map((c) => {
          const p = platformById(c.platform);
          return (
            <div key={`${c.platform}:${c.channelId}`} className="group mb-1 flex items-center gap-2 rounded-xl px-2 py-2 transition-colors" style={{ border: "1px solid transparent" }}
              onMouseEnter={(e) => (e.currentTarget.style.background = "var(--panel-2)")}
              onMouseLeave={(e) => (e.currentTarget.style.background = "transparent")}
            >
              <span className="size-2 flex-none rounded-full live-dot" style={{ background: "var(--ok)" }} title="Подключено" />
              <span className="min-w-0 flex-1 truncate text-sm font-medium">{c.channelId}</span>
              <PlatformBadge platform={c.platform} size={12} />
              <button type="button" className="opacity-0 transition-opacity group-hover:opacity-100" style={{ color: "var(--danger)" }} onClick={() => removeChannel(c.platform, c.channelId)} title="Отключить">
                <Trash2 size={13} />
              </button>
            </div>
          );
        })}
        {settings.channels.length === 0 && (
          <div className="px-2 py-6 text-center text-xs" style={{ color: "var(--muted)" }}>Нет каналов — добавьте первый ниже.</div>
        )}
      </div>
      <div className="border-t p-2.5" style={{ borderColor: "var(--border)" }}>
        <div className="flex gap-1.5">
          <select className="input !w-[86px] flex-none !px-2 !text-xs" value={platform} onChange={(e) => setPlatform(e.target.value as PlatformId)}>
            {PLATFORMS.map((p) => (
              <option key={p.id} value={p.id} style={{ background: "var(--bg-2)" }}>{p.short}</option>
            ))}
          </select>
          <input
            className="input !text-xs"
            placeholder={platformById(platform).hint}
            value={name}
            onChange={(e) => { setName(e.target.value); setError(null); }}
            onKeyDown={(e) => e.key === "Enter" && submit()}
          />
          <button type="button" className="btn btn-accent flex-none !px-2.5" onClick={submit} title="Добавить канал">
            <Plus size={15} />
          </button>
        </div>
        {error && <div className="mt-1.5 text-[11px] leading-snug" style={{ color: "var(--danger)" }}>{error}</div>}
        <div className="mt-1.5 text-[10px] leading-snug" style={{ color: "var(--muted)" }}>
          Только ник канала, без ссылки. YouTube — @handle, TikTok — @ник.
        </div>
      </div>
    </aside>
  );
}

function FeedArea() {
  const { settings, patch, messages, clearFeed, toast } = useApp();
  const cv = settings.chatView;
  const [query, setQuery] = useState("");
  const [hidden, setHidden] = useState<PlatformId[]>([]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return messages.filter((m) => {
      if (m.sys && !settings.showEvents) return false;
      if (hidden.includes(m.platform)) return false;
      if (q && !m.text.toLowerCase().includes(q) && !m.author.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [messages, query, hidden, settings.showEvents]);

  const togglePlatform = (p: PlatformId) =>
    setHidden((h) => (h.includes(p) ? h.filter((x) => x !== p) : [...h, p]));

  return (
    <div className="panel flex min-w-0 flex-1 flex-col overflow-hidden">
      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-2" style={{ borderColor: "var(--border)" }}>
        <div className="flex flex-wrap items-center gap-1.5">
          {PLATFORMS.map((p) => {
            const on = !hidden.includes(p.id);
            return (
              <button
                key={p.id}
                type="button"
                onClick={() => togglePlatform(p.id)}
                className="rounded-md px-2 py-1 text-[10px] font-black uppercase tracking-wide transition-all"
                style={{
                  background: on ? p.soft : "transparent",
                  color: on ? p.color : "var(--muted)",
                  border: `1px solid ${on ? `color-mix(in srgb, ${p.color} 40%, transparent)` : "var(--border)"}`,
                  opacity: on ? 1 : 0.55,
                }}
                title={`${on ? "Скрыть" : "Показать"} ${p.name}`}
              >
                {p.short}
              </button>
            );
          })}
        </div>
        <div className="relative ml-auto min-w-[140px] flex-1 sm:max-w-[240px]">
          <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2" style={{ color: "var(--muted)" }} />
          <input className="input !py-1.5 !pl-7 !text-xs" placeholder="Поиск по ленте…" value={query} onChange={(e) => setQuery(e.target.value)} />
        </div>
        <IconBtn
          icon={settings.showEvents ? Bell : BellOff}
          title={settings.showEvents ? "Скрыть события (фолловы, подписки)" : "Показать события"}
          active={settings.showEvents}
          onClick={() => patch({ showEvents: !settings.showEvents })}
        />
        <IconBtn icon={Eraser} title="Очистить ленту (Ctrl+Shift+L)" onClick={() => { clearFeed(); toast("Лента очищена"); }} />
      </div>
      <div className="flex min-h-0 flex-1 flex-col p-2">
        <ChatFeed
          messages={filtered}
          look={lookFromChatView(cv)}
          fx={cv.messageEffect}
          dur={cv.effectDuration}
          rowGap={cv.rowGap}
          empty={query ? "Ничего не найдено." : "Пока тихо. Подключите каналы — и здесь оживёт."}
        />
      </div>
    </div>
  );
}

function ChatTab() {
  return (
    <div className="flex min-h-0 flex-1 gap-3">
      <ChannelsPanel />
      <FeedArea />
      <div className="hidden w-[320px] flex-none flex-col gap-3 overflow-y-auto pr-0.5 xl:flex">
        <ChatViewPanel />
        <HotkeysHint />
      </div>
    </div>
  );
}

function HotkeysHint() {
  const { settings } = useApp();
  const hk = settings.hotkeys;
  const top: HotkeyAction[] = ["tts:toggle", "tts:skip", "overlay:toggle", "feed:clear"];
  const names: Record<string, string> = {
    "tts:toggle": "Озвучка вкл/выкл", "tts:skip": "Пропустить", "overlay:toggle": "Оверлей", "feed:clear": "Очистить ленту",
  };
  return (
    <div className="panel px-4 py-3">
      <div className="mb-2 flex items-center gap-2 text-xs font-bold uppercase tracking-wider" style={{ color: "var(--muted)" }}>
        <CheckCheck size={13} /> Быстрые клавиши
      </div>
      <div className="flex flex-col gap-1.5">
        {top.map((a) => (
          <div key={a} className="flex items-center justify-between gap-2 text-xs">
            <span style={{ color: "var(--muted)" }}>{names[a]}</span>
            <span className="kbd !py-0.5 !text-[10px]">{hk[a]}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function Toasts() {
  const { toasts } = useApp();
  return (
    <div className="pointer-events-none fixed bottom-5 left-1/2 z-50 flex -translate-x-1/2 flex-col items-center gap-2">
      {toasts.map((t) => (
        <div key={t.id} className="toast-item px-4 py-2 text-sm font-medium" style={{ color: "var(--text)" }}>
          {t.text}
        </div>
      ))}
    </div>
  );
}

function Shell() {
  const [tab, setTab] = useState<TabId>("chat");
  const { loaded } = useApp();
  return (
    <div className="flex h-full min-h-0 flex-col">
      <TitleBar />
      <div className="flex min-h-0 flex-1">
        <Rail tab={tab} setTab={setTab} />
        <main className="min-w-0 flex-1 overflow-hidden p-3">
          {!loaded ? (
            <div className="grid h-full place-items-center">
              <div className="flex items-center gap-2 text-sm" style={{ color: "var(--muted)" }}>
                <span className="size-3 animate-spin rounded-full border-2 border-current border-t-transparent" />
                Загрузка настроек…
              </div>
            </div>
          ) : tab === "chat" ? (
            <ChatTab />
          ) : (
            <div className="h-full overflow-y-auto pr-0.5">
              <div className="mx-auto max-w-6xl">
                {tab === "voice" && <VoicePanel />}
                {tab === "widget" && <WidgetPanel />}
                {tab === "overlay" && <OverlayPanel />}
                {tab === "hotkeys" && <HotkeysPanel />}
                {tab === "ui" && <InterfacePanel />}
                {tab === "about" && <AboutPanel />}
              </div>
            </div>
          )}
        </main>
      </div>
      <Toasts />
    </div>
  );
}

/** embedded=true — встраиваемое демо (в браузерной рамке на лендинге). */
export default function DesktopApp({ demo = false }: { demo?: boolean }) {
  return (
    <div className="h-full w-full overflow-hidden" style={{ background: "var(--bg)", color: "var(--text)" }}>
      <AppProvider demo={demo}>
        <Shell />
      </AppProvider>
    </div>
  );
}
