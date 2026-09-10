import type { ChatViewConfig } from "../../lib/types";
import { FEED_STYLES } from "../../lib/presets";
import { StyleGallery } from "./Presets";
import { CheckRow, Collapsible, Slider, ToggleRow } from "./ui";

interface Props {
  view: ChatViewConfig;
  patch: (c: ChatViewConfig) => void;
  /* поведение ленты — раньше жило отдельной вкладкой */
  maxFeed?: number;
  onMaxFeed?: (v: number) => void;
  hideDeleted?: boolean;
  setHideDeleted?: (v: boolean) => void;
  mentionHL?: boolean;
  setMentionHL?: (v: boolean) => void;
}

/**
 * Оформление ленты: сначала галерея готовых стилей, всё остальное —
 * в свёрнутых блоках, чтобы экран не был перегружен.
 */
export default function ChatViewPanel(p: Props) {
  const { view, patch } = p;

  return (
    <div className="flex flex-col gap-2">
      <StyleGallery styles={FEED_STYLES} cfg={view} onApply={(s) => patch({ ...view, ...s })} />

      <Collapsible title="Подстроить размер" hint={`${view.fontSize}px · интервал ${view.spacing}px`}>
        <div className="flex flex-col gap-2">
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Размер текста
            </label>
            <Slider value={view.fontSize} min={10} max={24} step={1} onChange={(v) => patch({ ...view, fontSize: v })} format={(v) => `${v}px`} />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Интервал между сообщениями
            </label>
            <Slider
              value={view.spacing}
              min={0}
              max={16}
              step={1}
              onChange={(v) => patch({ ...view, spacing: v })}
              format={(v) => (v === 0 ? "вплотную" : `${v}px`)}
            />
          </div>
        </div>
      </Collapsible>

      <Collapsible title="Что показывать в строке" hint="время, значки, площадка">
        <CheckRow label="Время сообщения" checked={view.showTime} onChange={(v) => patch({ ...view, showTime: v })} />
        <CheckRow label="Значки (мод, саб, vip)" checked={view.showBadges} onChange={(v) => patch({ ...view, showBadges: v })} />
        <CheckRow label="Иконка площадки" checked={view.showPlatform} onChange={(v) => patch({ ...view, showPlatform: v })} />
        <CheckRow label="Цветные никнеймы" checked={view.accentNick} onChange={(v) => patch({ ...view, accentNick: v })} />
        <CheckRow label="Анимация появления" checked={view.animations} onChange={(v) => patch({ ...view, animations: v })} />
      </Collapsible>

      {p.onMaxFeed && (
        <Collapsible title="Поведение ленты" hint={`история ${p.maxFeed} сообщений`}>
          <div className="flex flex-col gap-1">
            <div className="pb-1">
              <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
                Хранить сообщений в истории
              </label>
              <Slider value={p.maxFeed ?? 300} min={50} max={1000} step={50} onChange={p.onMaxFeed} format={(v) => `${v}`} />
            </div>
            <ToggleRow
              label="Подсвечивать упоминания"
              hint="строки с «@ник»"
              checked={p.mentionHL ?? true}
              onChange={(v) => p.setMentionHL?.(v)}
            />
            <ToggleRow
              label="Скрывать удалённые сообщения"
              checked={p.hideDeleted ?? false}
              onChange={(v) => p.setHideDeleted?.(v)}
            />
          </div>
        </Collapsible>
      )}
    </div>
  );
}
