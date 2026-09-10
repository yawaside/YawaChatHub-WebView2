import { useEffect, useState } from "react";
import { Check, Copy, ExternalLink, MonitorPlay, Palette, Server } from "lucide-react";
import type { WidgetConfig } from "../../lib/types";
import { copyText, isDesktop, openExternal, widgetInfo } from "../../lib/bridge";
import { WIDGET_PRESETS } from "../../lib/presets";
import { usePersisted } from "../../lib/persist";
import { PresetGrid } from "./Presets";
import { Box, CheckRow, FontPicker, Slider, SpacingSlider } from "./ui";

interface Props {
  cfg: WidgetConfig;
  patch: (c: WidgetConfig) => void;
  toast: (t: string) => void;
}

export default function WidgetPanel({ cfg, patch, toast }: Props) {
  const [copied, setCopied] = useState(false);
  const [port, setPort] = usePersisted("widgetPort", 8085);
  const [localOnly, setLocalOnly] = usePersisted("widgetLocalhost", true);
  const [info, setInfo] = useState<{ url: string; port: number; running: boolean }>({
    url: "http://127.0.0.1:8085/widget",
    port: 8085,
    running: false,
  });

  useEffect(() => {
    void widgetInfo().then(setInfo);
  }, []);

  const url =
    `${info.url}?max=${cfg.max}&fs=${cfg.fontSize}&theme=${cfg.theme}` +
    `&font=${cfg.fontFamily ?? "inter"}&gap=${cfg.spacing ?? 4}` +
    `${cfg.transparent ? "&transparent=1" : ""}`;

  const copy = async () => {
    if (await copyText(url)) {
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1600);
      toast("Ссылка на виджет скопирована");
    }
  };

  return (
    <div className="flex flex-col gap-3">
      {/* предпросмотр показывается в правой колонке настроек — здесь не дублируем */}
      <Box title="Варианты оформления" icon={<Palette size={14} style={{ color: "var(--dw-dim)" }} />}>
        <PresetGrid presets={WIDGET_PRESETS} cfg={cfg} onApply={(p) => patch({ ...cfg, ...p })} />
      </Box>

      <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
      <Box title="Виджет для OBS" icon={<MonitorPlay size={14} style={{ color: "var(--dw-dim)" }} />}>
        <div className="flex flex-col gap-3">
          <p className="text-[11.5px] leading-snug" style={{ color: "var(--dw-dim)" }}>
            Прозрачная страница для Browser Source: лента тех же сообщений поверх трансляции.
            {isDesktop() ? (
              <> Сервер поднят приложением на порту {info.port}.</>
            ) : (
              <> В WebView2-приложении сервер поднимается автоматически на порту 8085.</>
            )}
          </p>
          <div
            className="flex items-center gap-2 rounded-lg border px-3 py-2"
            style={{ background: "var(--dw-input)", borderColor: "var(--dw-line)" }}
          >
            <span className="min-w-0 flex-1 truncate font-mono text-[11px]" style={{ color: "var(--dw-accent-2)" }}>
              {url}
            </span>
            <button onClick={copy} className="shrink-0 cursor-pointer" style={{ color: copied ? "#4ade80" : "var(--dw-dim)" }}>
              {copied ? <Check size={14} /> : <Copy size={14} />}
            </button>
            <button onClick={() => openExternal(url)} className="shrink-0 cursor-pointer" style={{ color: "var(--dw-dim)" }}>
              <ExternalLink size={14} />
            </button>
          </div>

          <div className="grid grid-cols-2 gap-2">
            {(["dark", "light"] as const).map((t) => (
              <button
                key={t}
                onClick={() => patch({ ...cfg, theme: t })}
                className="cursor-pointer rounded-xl border p-3 transition-all"
                style={{
                  background: t === "dark" ? "#0b0d14" : "#f4f6fb",
                  borderColor: cfg.theme === t ? "var(--dw-accent)" : "var(--dw-line)",
                }}
              >
                <div className="mb-1.5 flex items-center gap-1">
                  <span className="h-2 w-2 rounded-full" style={{ background: "var(--dw-accent)" }} />
                  <span className="h-1.5 flex-1 rounded-full" style={{ background: t === "dark" ? "#2a2e40" : "#d4d9e8" }} />
                </div>
                <div className="h-1.5 w-2/3 rounded-full" style={{ background: t === "dark" ? "#232637" : "#dde2ef" }} />
                <p className="pt-2 text-[11px] font-semibold" style={{ color: t === "dark" ? "#e8eaf3" : "#111" }}>
                  {t === "dark" ? "Тёмный" : "Светлый"}
                </p>
              </button>
            ))}
          </div>
        </div>
      </Box>

      <Box title="Параметры вывода">
        <div className="flex flex-col gap-3">
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Сообщений на экране
            </label>
            <Slider value={cfg.max} min={2} max={20} step={1} onChange={(v) => patch({ ...cfg, max: v })} format={(v) => `${v}`} />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Размер текста виджета
            </label>
            <Slider value={cfg.fontSize} min={11} max={32} step={1} onChange={(v) => patch({ ...cfg, fontSize: v })} format={(v) => `${v}px`} />
          </div>
          <SpacingSlider value={cfg.spacing} onChange={(v) => patch({ ...cfg, spacing: v })} />
          <FontPicker value={cfg.fontFamily} onChange={(id) => patch({ ...cfg, fontFamily: id })} />
          <CheckRow label="Прозрачный фон (для Browser Source)" checked={cfg.transparent} onChange={(v) => patch({ ...cfg, transparent: v })} />
          <CheckRow label="Обводка текста (читаемость на игре)" checked={cfg.outlines} onChange={(v) => patch({ ...cfg, outlines: v })} />
        </div>
      </Box>

      {/* сервер виджета — раньше это была отдельная непонятная вкладка «Вывод» */}
      <Box title="Сервер виджета" icon={<Server size={14} style={{ color: "var(--dw-dim)" }} />}>
        <div className="flex flex-col gap-2">
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Порт для OBS Browser Source
            </label>
            <input
              type="number"
              value={port}
              min={1024}
              max={65535}
              onChange={(e) => setPort(Math.max(1024, Math.min(65535, Number(e.target.value))))}
              className="w-28 rounded-lg border border-transparent px-2.5 py-1.5 text-[12.5px] outline-none"
              style={{ background: "var(--dw-input)", color: "var(--dw-text)" }}
            />
            <p className="pt-1 text-[10.5px]" style={{ color: "var(--dw-dim)" }}>
              применится после перезапуска приложения
            </p>
          </div>
          <CheckRow
            label="Принимать только локальные подключения"
            checked={localOnly}
            onChange={setLocalOnly}
          />
        </div>
      </Box>
      </div>
    </div>
  );
}
