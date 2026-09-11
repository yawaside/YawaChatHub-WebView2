using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

namespace YawaChatHub;

/// <summary>
/// Чтение чата TikTok Live через скрытое окно WebView2.
///
/// ПОЧЕМУ ТАК. Публичный webcast-API (`webcast/im/fetch`) отдаёт чат только
/// «подписанному» запросу: нужны cookies (ttwid, msToken) и подпись X-Bogus,
/// которую генерирует JS самого TikTok. Без них ответ приходит ПУСТЫМ —
/// именно поэтому канал подключался, а сообщения не шли. Просить у
/// пользователя cookie вручную — плохой путь.
///
/// Решение: открываем страницу эфира в невидимом окне WebView2. Браузер сам
/// получает cookies, сам подписывает запросы и сам поднимает WebSocket чата,
/// а мы читаем уже готовые сообщения из DOM и отдаём их в ленту. Пользователь
/// вводит только имя канала.
/// </summary>
public static class TikTokLive
{
    /// <summary>
    /// Открыть скрытое окно и читать чат канала, пока не отменят.
    /// Вызывается из коннектора; всю работу с UI выполняет владелец окна.
    /// </summary>
    public static async Task RunAsync(
        Form owner,
        string channel,
        Action<string, string> onMessage,
        Action<bool, int> onStatus,
        CancellationToken ct)
    {
        LiveReaderWindow? window = null;
        try
        {
            // окно создаётся строго в UI-потоке приложения
            window = owner.Invoke(() => new LiveReaderWindow(channel, onMessage, onStatus));
            await owner.Invoke(() => window!.StartAsync());
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) { /* обычная остановка коннектора */ }
        finally
        {
            var w = window;
            if (w != null)
            {
                try { owner.BeginInvoke(() => { try { w.ForceClose(); } catch { } }); } catch { }
            }
        }
    }

    /// <summary>
    /// Невидимое окно со страницей эфира. Opacity = 0 — страница живёт и
    /// рендерится (WebSocket чата работает), но пользователю не видна.
    /// </summary>
    private sealed class LiveReaderWindow : Form
    {
        private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
        private readonly string _channel;
        private readonly Action<string, string> _onMessage;
        private readonly Action<bool, int> _onStatus;

        public LiveReaderWindow(string channel, Action<string, string> onMessage, Action<bool, int> onStatus)
        {
            _channel = channel;
            _onMessage = onMessage;
            _onStatus = onStatus;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Opacity = 0;
            // окно внутри экрана: Chromium не «замораживает» отрисовку
            var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Bounds = new Rectangle(scr.Right - 460, scr.Bottom - 360, 450, 350);
            Controls.Add(_web);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // служебное окно: без панели задач, не активируется, не ловит мышь
                cp.ExStyle |= 0x80 | 0x8000000 | 0x20;
                return cp;
            }
        }

        public async Task StartAsync()
        {
            Show();
            await _web.EnsureCoreWebView2Async(await Program.WebViewEnvAsync());

            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            // звук страницы эфира не нужен
            try { core.IsMuted = true; } catch { }

            core.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("t", out var t) ? t.GetString() : "";

                    if (type == "msg")
                    {
                        var author = root.TryGetProperty("a", out var a) ? a.GetString() ?? "" : "";
                        var text = root.TryGetProperty("m", out var m) ? m.GetString() ?? "" : "";
                        if (text.Length > 0) _onMessage(author, text);
                    }
                    else if (type == "state")
                    {
                        var live = root.TryGetProperty("live", out var l) && l.ValueKind == JsonValueKind.True;
                        var viewers = root.TryGetProperty("v", out var v) && v.TryGetInt32(out var vi) ? vi : 0;
                        _onStatus(live, viewers);
                    }
                }
                catch { /* мусорные сообщения игнорируем */ }
            };

            await core.AddScriptToExecuteOnDocumentCreatedAsync(ReaderScript);
            core.Navigate($"https://www.tiktok.com/@{_channel}/live");
        }

        public void ForceClose()
        {
            try { _web.Dispose(); } catch { }
            try { Close(); } catch { }
            try { Dispose(); } catch { }
        }

        /// <summary>
        /// Скрипт-читатель. Работает с готовым DOM чата, поэтому не зависит от
        /// внутреннего формата webcast и его подписей. Новые сообщения ловит
        /// MutationObserver, дубликаты отсекаются по ключу «автор+текст».
        /// </summary>
        private const string ReaderScript = """
            (() => {
              if (window.__yawaTikTok) return;
              window.__yawaTikTok = true;

              const send = (o) => { try { chrome.webview.postMessage(o); } catch (e) {} };
              const seen = new Set();

              const CHAT_SELECTORS = [
                '[data-e2e="chat-message"]',
                '[class*="DivChatMessageContainer"]',
                '[class*="DivChatItemContainer"]',
                '[class*="DivCommentItemContainer"]',
              ];

              // текст сообщения = весь текст узла без ника
              const extract = (node) => {
                const nickEl =
                  node.querySelector('[data-e2e="message-owner-name"]') ||
                  node.querySelector('[class*="SpanNickname"]') ||
                  node.querySelector('span');
                const nick = (nickEl?.textContent || '').trim().replace(/[:：]\s*$/, '');
                let text = (node.textContent || '').trim();
                if (nick && text.startsWith(nick)) text = text.slice(nick.length);
                text = text.replace(/^[:：]\s*/, '').trim();
                return { nick, text };
              };

              const push = (node) => {
                if (!node || node.nodeType !== 1) return;
                const { nick, text } = extract(node);
                if (!text) return;
                const key = nick + '|#|' + text;
                if (seen.has(key)) return;
                seen.add(key);
                if (seen.size > 400) seen.clear();
                send({ t: 'msg', a: nick, m: text });
              };

              const findList = () => {
                for (const sel of CHAT_SELECTORS) {
                  const items = document.querySelectorAll(sel);
                  if (items.length) return items[0].parentElement;
                }
                return null;
              };

              let observed = null;
              const attach = () => {
                const list = findList();
                if (!list || list === observed) return;
                observed = list;
                // существующие строки помечаем как прочитанные: история не нужна
                list.querySelectorAll(':scope > *').forEach((n) => {
                  const { nick, text } = extract(n);
                  if (text) seen.add(nick + '|#|' + text);
                });
                new MutationObserver((records) => {
                  for (const r of records) r.addedNodes.forEach(push);
                }).observe(list, { childList: true });
              };

              // статус эфира и зрители — из заголовка страницы
              const state = () => {
                const live = !!findList() || !!document.querySelector('[data-e2e="live-people-count"]');
                const raw = document.querySelector('[data-e2e="live-people-count"]')?.textContent || '';
                const digits = raw.replace(/[^0-9]/g, '');
                send({ t: 'state', live, v: digits ? parseInt(digits, 10) : 0 });
              };

              const tick = () => { attach(); state(); };
              document.addEventListener('DOMContentLoaded', tick);
              setInterval(tick, 3000);
              setTimeout(tick, 1500);
            })();
            """;
    }
}
