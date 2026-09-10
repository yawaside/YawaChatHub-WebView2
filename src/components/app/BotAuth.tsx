import { LogIn, LogOut } from "lucide-react";
import type { PlatformId } from "../../lib/types";
import { PlatformIcon, platformMeta } from "../../lib/platforms";
import { openExternal } from "../../lib/bridge";
import { usePersisted } from "../../lib/persist";

/** общее состояние авторизации бота (одинаково в «Каналах» и в разделе «Чат-бот») */
export function useBotAuth(): [
  Record<string, string>,
  (v: Record<string, string> | ((p: Record<string, string>) => Record<string, string>)) => void,
] {
  const [value, set] = usePersisted<Record<string, string>>("botAuth", {});
  return [value, set];
}

const AUTH_PLATFORMS: { id: PlatformId; name: string; url: string }[] = [
  { id: "twitch", name: "Twitch", url: "https://id.twitch.tv/oauth2/authorize?client_id=yawachathub&response_type=token&scope=chat:read+chat:edit+moderator:manage:chat_messages" },
  { id: "vkplay", name: "VK Видео Live", url: "https://id.vk.com/authorize?client_id=yawachathub&response_type=token" },
];

function useLogin(setAuthorized: (fn: (p: Record<string, string>) => Record<string, string>) => void, toast: (t: string) => void) {
  return (id: PlatformId, name: string, url: string) => {
    openExternal(url);
    toast(`Открыт браузер — вход через ${name}…`);
    // демо: имитируем возврат с токеном
    window.setTimeout(() => {
      const acc = id === "twitch" ? "yawa_bot" : "yawa_vk_bot";
      setAuthorized((p) => ({ ...p, [id]: acc }));
      toast(`Чат-бот ${name} авторизован как ${acc}`);
    }, 1500);
  };
}

/** крупные карточки — раздел «Чат-бот» */
export function BotAuthCards({ toast }: { toast: (t: string) => void }) {
  const [authorized, setAuthorized] = useBotAuth();
  const login = useLogin(setAuthorized, toast);

  return (
    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
      {AUTH_PLATFORMS.map((p) => {
        const acc = authorized[p.id];
        const color = platformMeta(p.id).color;
        return (
          <div
            key={p.id}
            className="flex items-center gap-3 rounded-xl border p-3"
            style={{
              background: "var(--dw-panel2)",
              borderColor: acc ? color : "var(--dw-line)",
              boxShadow: acc ? `0 0 18px ${color}22` : "none",
            }}
          >
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg" style={{ background: `${color}1f` }}>
              <PlatformIcon id={p.id} size={18} />
            </span>
            <div className="min-w-0 flex-1">
              <p className="text-[12.5px] font-semibold" style={{ color: "var(--dw-text)" }}>
                {p.name}
              </p>
              {acc ? (
                <p className="truncate text-[11px] text-emerald-400">✓ бот онлайн · {acc}</p>
              ) : (
                <p className="text-[11px]" style={{ color: "var(--dw-dim)" }}>
                  бот не авторизован
                </p>
              )}
            </div>
            {acc ? (
              <button
                onClick={() =>
                  setAuthorized((prev) => {
                    const n = { ...prev };
                    delete n[p.id];
                    return n;
                  })
                }
                className="flex shrink-0 cursor-pointer items-center gap-1 rounded-lg px-2.5 py-1.5 text-[11px] font-medium"
                style={{ background: "var(--dw-input)", color: "var(--dw-dim)" }}
              >
                <LogOut size={11} /> Выйти
              </button>
            ) : (
              <button
                onClick={() => login(p.id, p.name, p.url)}
                className="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-lg px-3 py-1.5 text-[11.5px] font-bold text-white transition-all hover:brightness-110"
                style={{ background: color }}
              >
                <LogIn size={12} /> Войти
              </button>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** компактный вид — низ панели «Каналы» */
export function BotAuthCompact({ toast }: { toast: (t: string) => void }) {
  const [authorized, setAuthorized] = useBotAuth();
  const login = useLogin(setAuthorized, toast);

  return (
    <div className="flex flex-col gap-1 px-0.5">
      {AUTH_PLATFORMS.map((p) => {
        const acc = authorized[p.id];
        return (
          <div
            key={p.id}
            className="flex items-center gap-2 rounded-lg border px-2 py-1.5"
            style={{ background: "var(--dw-panel2)", borderColor: "var(--dw-line)" }}
          >
            <PlatformIcon id={p.id} size={13} />
            {acc ? (
              <>
                <span className="min-w-0 flex-1 truncate text-[11.5px] font-medium" style={{ color: "var(--dw-text)" }}>
                  {acc}
                </span>
                <span className="rounded-full bg-emerald-400/15 px-1.5 py-px text-[9px] font-bold text-emerald-400 uppercase">
                  онлайн
                </span>
                <button
                  className="cursor-pointer text-[10px] hover:underline"
                  style={{ color: "var(--dw-dim)" }}
                  onClick={() =>
                    setAuthorized((prev) => {
                      const n = { ...prev };
                      delete n[p.id];
                      return n;
                    })
                  }
                >
                  выйти
                </button>
              </>
            ) : (
              <>
                <span className="min-w-0 flex-1 truncate text-[10.5px]" style={{ color: "var(--dw-dim)" }}>
                  не авторизован
                </span>
                <button
                  className="flex shrink-0 cursor-pointer items-center gap-1 rounded-lg px-2 py-1 text-[10.5px] font-semibold"
                  style={{ background: `${platformMeta(p.id).color}22`, color: platformMeta(p.id).color }}
                  onClick={() => login(p.id, p.name, p.url)}
                >
                  <LogIn size={10} /> Войти
                </button>
              </>
            )}
          </div>
        );
      })}
    </div>
  );
}
