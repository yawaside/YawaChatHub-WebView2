import { useEffect, useMemo, useState } from "react";
import { AudioWaveform, Eraser, ListOrdered, Pause, Play, SkipForward, Volume2 } from "lucide-react";
import type { TtsConfig } from "../../lib/types";
import { isDesktop, ttsBridge } from "../../lib/bridge";
import type { HostVoice } from "../../lib/bridge";
import { Box, Chip, IconBtn, Slider, ToggleRow } from "./ui";

export interface QueueItem {
  id: number;
  author: string;
  text: string;
  status: "speaking" | "waiting";
}

interface Props {
  tts: TtsConfig;
  patch: (p: Partial<TtsConfig>) => void;
  queue: QueueItem[];
  paused: boolean;
  onPauseToggle: () => void;
  onSkip: () => void;
  onClearQueue: () => void;
  onTest: (text: string) => void;
}

export default function VoicePanel({ tts, patch, queue, paused, onPauseToggle, onSkip, onClearQueue, onTest }: Props) {
  const [hostVoices, setHostVoices] = useState<{ edge: HostVoice[]; sapi: HostVoice[] }>({ edge: [], sapi: [] });
  const [webVoices, setWebVoices] = useState<SpeechSynthesisVoice[]>([]);
  const desktop = isDesktop();

  // голоса хоста (Edge TTS + системные SAPI, только русские)
  useEffect(() => {
    if (desktop) void ttsBridge.voices().then(setHostVoices);
  }, [desktop]);

  // браузерный резерв
  useEffect(() => {
    if (desktop || typeof window.speechSynthesis === "undefined") return;
    const load = () => setWebVoices(window.speechSynthesis.getVoices());
    load();
    window.speechSynthesis.onvoiceschanged = load;
    return () => {
      window.speechSynthesis.onvoiceschanged = null;
    };
  }, [desktop]);

  const engine = tts.engine ?? "edge";

  /** список голосов текущего движка — только русские */
  const ruVoices = useMemo<HostVoice[]>(() => {
    if (desktop) return engine === "edge" ? hostVoices.edge : hostVoices.sapi;
    const ru = webVoices.filter((v) => v.lang.toLowerCase().startsWith("ru"));
    return (ru.length ? ru : webVoices).map((v) => ({ id: v.name, title: `${v.name} (${v.lang})` }));
  }, [desktop, engine, hostVoices, webVoices]);

  const selected = tts.voice || ruVoices[0]?.id || "";

  // при смене движка подставляем первый доступный голос
  useEffect(() => {
    if (ruVoices.length && !ruVoices.some((v) => v.id === tts.voice)) patch({ voice: ruVoices[0].id });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [engine, ruVoices.length]);

  return (
    <div className="grid grid-cols-1 gap-3 overflow-y-auto xl:grid-cols-2">
      {/* голос и параметры */}
      <Box
        title="Голос и параметры"
        icon={<Volume2 size={14} style={{ color: "var(--dw-dim)" }} />}
        defaultOpen={false}
        hint={tts.enabled ? "озвучка включена" : "озвучка выключена"}
      >
        <div className="flex flex-col gap-3">
          <ToggleRow
            label="Озвучивать входящие сообщения"
            hint="новые сообщения ставятся в очередь"
            checked={tts.enabled}
            onChange={(v) => patch({ enabled: v })}
          />

          {desktop && (
            <div>
              <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
                Движок озвучки
              </label>
              <div className="flex gap-1.5">
                <Chip active={engine === "edge"} onClick={() => patch({ engine: "edge" })}>
                  Edge TTS · нейро
                </Chip>
                <Chip active={engine === "sapi"} onClick={() => patch({ engine: "sapi" })}>
                  Системный SAPI
                </Chip>
              </div>
              <p className="pt-1 text-[10.5px] leading-snug" style={{ color: "var(--dw-dim)" }}>
                {engine === "edge"
                  ? "Нейросетевые голоса Microsoft Edge — естественное звучание, нужен интернет."
                  : "Голоса, установленные в Windows. Работает офлайн."}
              </p>
            </div>
          )}

          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Голос (только русские)
            </label>
            <select
              value={selected}
              onChange={(e) => patch({ voice: e.target.value })}
              className="w-full cursor-pointer rounded-lg border border-transparent px-2.5 py-1.5 text-[12.5px] outline-none"
              style={{ background: "var(--dw-input)", color: "var(--dw-text)" }}
            >
              {ruVoices.length === 0 && <option value="">Русский голос не найден</option>}
              {ruVoices.map((v) => (
                <option key={v.id} value={v.id} style={{ color: "#000" }}>
                  {v.title}
                </option>
              ))}
            </select>
            {desktop && engine === "sapi" && ruVoices.length === 0 && (
              <p className="pt-1 text-[10.5px]" style={{ color: "#fbbf24" }}>
                В Windows нет русского голоса — переключитесь на Edge TTS.
              </p>
            )}
          </div>

          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Скорость речи
            </label>
            <Slider value={tts.rate} min={0.5} max={2} step={0.05} onChange={(v) => patch({ rate: v })} format={(v) => `×${v.toFixed(2)}`} />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Громкость
            </label>
            <Slider value={tts.volume} min={0} max={1} onChange={(v) => patch({ volume: v })} format={(v) => `${Math.round(v * 100)}%`} />
          </div>

          <button
            onClick={() => onTest("Привет! Это проверка озвучки YawaChatHub.")}
            className="mt-1 flex cursor-pointer items-center justify-center gap-2 rounded-lg py-2 text-[12.5px] font-semibold text-white transition-all hover:brightness-110"
            style={{ background: "var(--dw-accent)" }}
          >
            <Play size={14} /> Проверить голос
          </button>
        </div>
      </Box>

      {/* состав фразы */}
      <Box title="Состав фразы" icon={<AudioWaveform size={14} style={{ color: "var(--dw-dim)" }} />}>
        <div className="flex flex-col">
          <ToggleRow label="Называть автора" hint="«nekto_san: привет всем»" checked={tts.speakAuthor} onChange={(v) => patch({ speakAuthor: v })} />
          <ToggleRow label="Называть площадку" hint="«с Твича nekto_san: …»" checked={tts.speakPlatform} onChange={(v) => patch({ speakPlatform: v })} />
          <ToggleRow label="Только донаты" hint="обычный чат пропускается" checked={tts.donatesOnly} onChange={(v) => patch({ donatesOnly: v })} />
          <ToggleRow
            label="Озвучивать смайлы и стикеры"
            hint="по умолчанию выключено — названия смайлов не читаются"
            checked={tts.speakEmotes ?? false}
            onChange={(v) => patch({ speakEmotes: v })}
          />
          <ToggleRow label="Пропускать ссылки" checked={tts.skipLinks} onChange={(v) => patch({ skipLinks: v })} />
          <ToggleRow label="Пропускать команды" hint="сообщения, начинающиеся с !" checked={tts.skipCommands} onChange={(v) => patch({ skipCommands: v })} />
          <div className="border-t pt-2" style={{ borderColor: "var(--dw-line)" }}>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Максимальная длина сообщения
            </label>
            <Slider value={tts.maxLen} min={50} max={500} step={10} onChange={(v) => patch({ maxLen: v })} format={(v) => `${v} симв.`} />
          </div>
        </div>
      </Box>

      {/* очередь */}
      <Box
        title="Очередь озвучки"
        icon={<ListOrdered size={14} style={{ color: "var(--dw-dim)" }} />}
        className="xl:col-span-2"
        actions={
          <>
            <IconBtn icon={paused ? <Play size={13} /> : <Pause size={13} />} title={paused ? "Продолжить" : "Пауза (F7)"} onClick={onPauseToggle} active={paused} />
            <IconBtn icon={<SkipForward size={13} />} title="Пропустить (F8)" onClick={onSkip} />
            <IconBtn icon={<Eraser size={13} />} title="Очистить очередь" onClick={onClearQueue} danger />
          </>
        }
      >
        {queue.length === 0 ? (
          <p className="py-2 text-center text-[12px]" style={{ color: "var(--dw-dim)" }}>
            Очередь пуста — новые сообщения появятся здесь
          </p>
        ) : (
          <div className="flex flex-col gap-1">
            {queue.slice(0, 8).map((q) => (
              <div
                key={q.id}
                className="flex items-center gap-2 rounded-lg px-2.5 py-1.5 text-[12px]"
                style={{
                  background: q.status === "speaking" ? "color-mix(in srgb, var(--dw-accent) 14%, transparent)" : "var(--dw-input)",
                  color: "var(--dw-text)",
                }}
              >
                <span
                  className="h-1.5 w-1.5 shrink-0 rounded-full"
                  style={{ background: q.status === "speaking" ? "var(--dw-accent-2)" : "var(--dw-dim)" }}
                />
                <span className="shrink-0 font-semibold" style={{ color: "var(--dw-accent-2)" }}>
                  {q.author}
                </span>
                <span className="min-w-0 truncate">{q.text}</span>
                <span className="ml-auto shrink-0 text-[10px] uppercase" style={{ color: "var(--dw-dim)" }}>
                  {q.status === "speaking" ? "звучит" : "ждёт"}
                </span>
              </div>
            ))}
            {queue.length > 8 && (
              <p className="pt-1 text-center text-[10.5px]" style={{ color: "var(--dw-dim)" }}>
                и ещё {queue.length - 8}…
              </p>
            )}
          </div>
        )}
      </Box>
    </div>
  );
}
