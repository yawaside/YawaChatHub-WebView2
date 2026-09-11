import { useEffect, useState } from "react";
import { Check, Copy, ExternalLink } from "lucide-react";
import type { WidgetConfig } from "../../lib/types";
import { copyText, isDesktop, openExternal, widgetConfig, widgetInfo } from "../../lib/bridge";
import { WIDGET_STYLES, styleName } from "../../lib/presets";
import { effectName } from "../../lib/effects";
import { usePersisted } from "../../lib/persist";
import { StyleGallery } from "./Presets";
import { CheckRow, Chip, Collapsible, EffectPicker, Slider } from "./ui";

interface Props {
  cfg: WidgetConfig;
  patch: (c: WidgetConfig) => void;
  toast: (t: string) => void;
}

export default function WidgetPanel({ cfg, patch, toast }: Props) {
  const [copied, setCopied] = useState(false);
  const [port, setPort] = usePersisted("widgetPort", 8085);
  const [localOnly, setLocalOnly] = usePersisted("widgetLocalhost", true);
  const [info, setInfo] = useState({ url: "http://127.0.0.1:8085/widget", port: 8085, running: false });

  useEffect(() => {
    void widgetInfo().then(setInfo);
  }, []);

  // ССЫЛКА ПОСТОЯННАЯ: оформление уходит в OBS живым событием по SSE,
  // поэтому Browser Source добавляется один раз и не требует замены.
  const url = info.url;

  useEffect(() => {
    widgetConfig(cfg);
  }, [cfg]);

  const copy = async () => {
    if (await copyText(url)) {
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1600);
      toast("Ссылка на виджет скопирована");
    }
  };

  return (
    <div className="flex flex-col gap-2">
      <Collapsible title="Стиль оформления" hint={styleName(WIDGET_STYLES, cfg.styleId)} defaultOpen>
        <StyleGallery styles={WIDGET_STYLES} cfg={cfg} onApply={(s) => patch({ ...cfg, ...s })} />
      </Collapsible>

      {/* ссылка для OBS — главное действие раздела */}
      <div
        className="flex items-center gap-2 rounded-xl border px-3 py-2"
        style={{ background: "var(--dw-panel)", borderColor: "var(--dw-line)" }}
      >
        <div className="min-w-0 flex-1">
          <span className="block truncate font-mono text-[11px]" style={{ color: "var(--dw-accent-2)" }}>
            {url}
          </span>
          <span className="block text-[10px]" style={{ color: "var(--dw-dim)" }}>
постоянная ссылка — добавьте в OBS один раз, оформление меняется на лету
          </span>
        </div>
        <button onClick={copy} className="shrink-0 cursor-pointer" title="Скопировать" style={{ color: copied ? "#4ade80" : "var(--dw-dim)" }}>
          {copied ? <Check size={14} /> : <Copy size={14} />}
        </button>
        <button onClick={() => openExternal(url)} className="shrink-0 cursor-pointer" title="Открыть" style={{ color: "var(--dw-dim)" }}>
          <ExternalLink size={14} />
        </button>
      </div>

      <Collapsible title="Размер и плотность" hint={`${cfg.fontSize}px · ${cfg.max} строк`}>
        <div className="flex flex-col gap-2">
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Размер текста
            </label>
            <Slider value={cfg.fontSize} min={11} max={34} step={1} onChange={(v) => patch({ ...cfg, fontSize: v })} format={(v) => `${v}px`} />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Сообщений на экране
            </label>
            <Slider value={cfg.max} min={2} max={20} step={1} onChange={(v) => patch({ ...cfg, max: v })} format={(v) => `${v}`} />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Интервал между сообщениями
            </label>
            <Slider value={cfg.spacing} min={0} max={16} step={1} onChange={(v) => patch({ ...cfg, spacing: v })} format={(v) => (v === 0 ? "вплотную" : `${v}px`)} />
          </div>
        </div>
      </Collapsible>

      <Collapsible
        title="Фон и читаемость"
        hint={cfg.transparent ? "прозрачный фон" : cfg.theme === "light" ? "светлые плашки" : "тёмные плашки"}
      >
        <div className="flex flex-col gap-2">
          <CheckRow
            label="Прозрачный фон (для Browser Source)"
            checked={cfg.transparent}
            onChange={(v) => patch({ ...cfg, transparent: v })}
          />
          <CheckRow
            label="Обводка текста (читаемость поверх игры)"
            checked={cfg.outlines}
            onChange={(v) => patch({ ...cfg, outlines: v })}
          />
          <CheckRow
            label="Цветная полоса площадки"
            checked={cfg.stripe}
            onChange={(v) => patch({ ...cfg, stripe: v })}
          />
          {!cfg.transparent && (
            <div className="flex items-center gap-1.5 pt-0.5">
              <span className="text-[11px]" style={{ color: "var(--dw-dim)" }}>Тема плашек:</span>
              <Chip active={cfg.theme === "dark"} onClick={() => patch({ ...cfg, theme: "dark" })}>тёмная</Chip>
              <Chip active={cfg.theme === "light"} onClick={() => patch({ ...cfg, theme: "light" })}>светлая</Chip>
            </div>
          )}
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Скругление плашки
            </label>
            <Slider
              value={cfg.radius}
              min={0}
              max={20}
              step={1}
              onChange={(v) => patch({ ...cfg, radius: v })}
              format={(v) => `${v}px`}
            />
          </div>
        </div>
      </Collapsible>

      <Collapsible title="Исчезновение сообщений" hint={cfg.autoFade ? `через ${cfg.fadeAfterSec ?? 30} сек` : "выключено"}>
        <div className="flex flex-col gap-2">
          <CheckRow
            label="Скрывать старые сообщения"
            checked={cfg.autoFade ?? false}
            onChange={(v) => patch({ ...cfg, autoFade: v })}
          />
          {cfg.autoFade && (
            <div>
              <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
                Время жизни сообщения
              </label>
              <Slider
                value={cfg.fadeAfterSec ?? 30}
                min={5}
                max={180}
                step={5}
                onChange={(v) => patch({ ...cfg, fadeAfterSec: v })}
                format={(v) => `${v} сек`}
              />
            </div>
          )}
        </div>
      </Collapsible>

      <Collapsible title="Эффект появления" hint={effectName(cfg.effect)}>
        <EffectPicker
          effect={cfg.effect}
          speed={cfg.effectSpeed}
          onChange={(v) => patch({ ...cfg, ...v })}
        />
      </Collapsible>

      <Collapsible title="Подключение к OBS" hint={`порт ${port}`}>
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
          <CheckRow label="Принимать только локальные подключения" checked={localOnly} onChange={setLocalOnly} />
          {!isDesktop() && (
            <p className="text-[10.5px]" style={{ color: "var(--dw-dim)" }}>
              Сервер работает внутри приложения — в браузере это предпросмотр.
            </p>
          )}
        </div>
      </Collapsible>
    </div>
  );
}
