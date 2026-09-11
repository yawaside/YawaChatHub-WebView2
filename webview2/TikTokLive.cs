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
        private bool _diagWritten;
        private bool _hasChat;
        private int _msgs;

        /// Диагностика: видно, читается ли чат вообще (%LOCALAPPDATA%\YawaChatHub\tiktok.log).
        private static string LogPath => Path.Combine(Program.AppDir, "tiktok.log");
        private void Log(string line)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\n"); }
            catch { }
        }

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
                        if (text.Length > 0)
                        {
                            _msgs++;
                            if (_msgs == 1) Log($"[msg] {_channel}: первое сообщение получено");
                            _onMessage(author, text);
                        }
                    }
                    else if (type == "state")
                    {
                        var live = root.TryGetProperty("live", out var l) && l.ValueKind == JsonValueKind.True;
                        var viewers = root.TryGetProperty("v", out var v) && v.TryGetInt32(out var vi) ? vi : 0;
                        _hasChat |= root.TryGetProperty("chat", out var ch) && ch.ValueKind == JsonValueKind.True;
                        _onStatus(live, viewers);
                    }
                    else if (type == "diag" && !_diagWritten)
                    {
                        // одна запись на канал: показывает, загрузилась ли страница
                        _diagWritten = true;
                        var title = root.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
                        var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                        var items = root.TryGetProperty("items", out var it) && it.TryGetInt32(out var ii) ? ii : 0;
                        Log($"[diag] канал {_channel}: «{title}» ({url}), узлов чата: {items}");
                    }
                }
                catch { /* мусорные сообщения игнорируем */ }
            };

            await core.AddScriptToExecuteOnDocumentCreatedAsync(ReaderScript);
            core.Navigate($"https://www.tiktok.com/@{_channel}/live");
            Log($"[nav] {_channel}: открыта страница эфира");

            // если через 20 секунд чат так и не нашёлся — пробуем страницу профиля:
            // live-рендер может быть недоступен регионом, а профиль его рисует
            _ = Task.Run(async () =>
            {
                await Task.Delay(20000);
                if (_hasChat || _msgs > 0) return;
                try
                {
                    Log($"[nav] {_channel}: чат не найден за 20 сек, открываю профиль");
                    core.Navigate($"https://www.tiktok.com/@{_channel}");
                }
                catch { }
            });
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

              // TikTok постоянно меняет вёрстку, поэтому не привязываемся к
              // контейнеру чата: периодически сканируем ВЕСЬ документ и берём
              // узлы, похожие на сообщения чата. Таким образом чат читается
              // при любой структуре страницы.
              const CHAT_SELECTORS = [
                '[data-e2e="chat-message"]',
                '[data-e2e="comment-item"]',
                '[class*="DivChatMessageContainer"]',
                '[class*="DivChatItemContainer"]',
                '[class*="DivCommentItemContainer"]',
                '[class*="ChatMessage"]',
                '[class*="CommentItem"]',
                'div[data-tid*="chat"] > div',
              ];

              const MESSAGE_NODES =
                '[data-e2e="chat-message"], [data-e2e="comment-item"], ' +
                '[class*="DivChatMessageContainer"], [class*="DivChatItemContainer"], ' +
                '[class*="DivCommentItemContainer"], [class*="ChatMessage"], ' +
                '[class*="CommentItem"]';

              const VIEWER_SELECTORS = [
                '[data-e2e="live-people-count"]',
                '[data-e2e="live-viewer-count"]',
                '[class*="PeopleCount"]',
                '[class*="ViewerCount"]',
              ];

              const parseCount = (raw) => {
                const s = (raw || '').trim().toLowerCase().replace(/[^\d.,kкmм]/gi, '');
                const m = s.match(/([\d.,]+)\s*([kкmм])?/);
                if (!m) return 0;
                const num = parseFloat(m[1].replace(',', '.'));
                if (isNaN(num)) return 0;
                const suf = m[2];
                return suf === 'k' || suf === 'к' ? Math.round(num * 1000)
                     : suf === 'm' || suf === 'м' ? Math.round(num * 1000000)
                     : Math.round(num);
              };

              const extract = (node) => {
                // автор: элемент с ником или первый span
                const nickEl =
                  node.querySelector('[data-e2e="message-owner-name"]') ||
                  node.querySelector('[class*="Nickname"], [class*="nickname"], [class*="UserName"], [class*="user-name"]') ||
                  node.querySelector('span');
                const nick = (nickEl?.textContent || '').trim().replace(/[:：]\s*$/, '');
                // текст без ника и без служебных хвостов
                let lines = (node.innerText || node.textContent || '')
                  .split('\n')
                  .map((l) => l.trim())
                  .filter((l) => l.length > 0);
                if (nick && lines.length && lines[0].startsWith(nick.slice(0, 18))) lines.shift();
                return { nick, text: lines.join(' ').trim() };
              };

              let pushed = 0;
              const push = (node) => {
                if (!node || node.nodeType !== 1) return;
                const { nick, text } = extract(node);
                if (!text || text.length > 500) return;
                const key = nick + '|#|' + text;
                if (seen.has(key)) return;
                seen.add(key);
                if (seen.size > 600) seen.clear();
                send({ t: 'msg', a: nick, m: text });
                pushed++;
              };

              // Полное сканирование: новые сообщения находятся в любом случае,
              // даже если наблюдатель пропустил перестройку контейнера.
              const scan = () => {
                try {
                  const nodes = Array.from(document.querySelectorAll(MESSAGE_NODES));
                  // начинаем только с новых сообщений: историю пропускаем первым проходом
                  for (const n of nodes.slice(-40)) {
                    const { text } = extract(n);
                    if (!text) continue;
                    const { nick } = extract(n);
                    const key = nick + '|#|' + text;
                    if (seen.has(key)) continue;
                    seen.add(key);
                    if (seen.size > 600) seen.clear();
                    if (started) { send({ t: 'msg', a: nick, m: text }); pushed++; }
                  }
                } catch (e) {}
              };

              const getJsonViewers = () => {
                try {
                  // SIGI_STATE или __UNIVERSAL_DATA_FOR_REHYDRATION__ содержат
                  // точное число зрителей (userCount) и статус эфира напрямую от TikTok.
                  const script = document.getElementById('SIGI_STATE') ||
                                 document.getElementById('__UNIVERSAL_DATA_FOR_REHYDRATION__');
                  if (script && script.textContent) {
                    const data = JSON.parse(script.textContent);
                    const find = (o, k) => {
                      if (!o || typeof o !== 'object') return null;
                      if (k in o) return o[k];
                      for (const v of Object.values(o)) {
                        const r = find(v, k);
                        if (r != null) return r;
                      }
                      return null;
                    };
                    const uc = find(data, 'userCount') || find(data, 'viewerCount');
                    if (typeof uc === 'number') return uc;
                  }
                } catch (e) {}
                return 0;
              };

              const viewerNode = () => {
                for (const sel of VIEWER_SELECTORS) {
                  const el = document.querySelector(sel);
                  if (el && el.textContent) return el;
                }
                return null;
              };

              const state = () => {
                let viewers = getJsonViewers();
                const el = viewerNode();
                if (!viewers && el) viewers = parseCount(el.textContent || '');
                const hasChat = document.querySelector(MESSAGE_NODES) != null;
                const hasVideo = !!document.querySelector('video');
                const live = hasChat || hasVideo || !!el || viewers > 0;
                send({ t: 'state', live, v: viewers, chat: hasChat });
              };

              // диагностика для лога: видно, загрузилась ли страница вообще
              let reported = 0;
              let started = false;
              const report = () => {
                if (reported++ === 0) {
                  send({
                    t: 'diag',
                    title: document.title || '',
                    url: location.href,
                    items: document.querySelectorAll(MESSAGE_NODES).length,
                  });
                  started = true;   // с момента старта показываем только новое
                }
              };

              // MutationObserver как мгновенный канал, сканирование — как страховка
              const observer = new MutationObserver((records) => {
                for (const r of records) {
                  r.addedNodes.forEach((n) => {
                    try {
                      if (n.matches && n.matches(MESSAGE_NODES)) { if (started) push(n); }
                      else if (n.querySelector) n.querySelectorAll(MESSAGE_NODES).forEach((x) => { if (started) push(x); });
                    } catch (e) {}
                  });
                }
              });
              const boot = () => {
                try { observer.observe(document.body, { childList: true, subtree: true }); } catch (e) {}
              };
              document.addEventListener('DOMContentLoaded', boot);
              boot();

              // Основной приём идёт через лёгкий MutationObserver (0% CPU).
              // Тяжёлый scan всего DOM запускаем только на старте для инициализации,
              // а не каждые 1.5 секунды non-stop.
              setInterval(state, 3000);
              setTimeout(report, 5000);
              [500, 1500, 3000, 6000].forEach((d) => setTimeout(scan, d));
            })();
            """;
    }
}
