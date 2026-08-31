## YawaChatHub v4.0.13 (2026-08-31)

## YawaChatHub v4.0.12 (2026-08-31)

# Changelog

## YawaChatHub v4.0.0

### Добавлено
- Восстановлен desktop-проект: C# WPF/WebView2 (окно, оверлей, SAPI5, WidgetServer, хоткеи, трей, коннекторы), NSIS-установщик и статичный OBS-виджет
- Единая лента сообщений Twitch, YouTube Live, VK Video Live, Kick, TikTok Live
- Озвучка стандартными голосами Windows (SAPI5), SSML с принудительным ru-RU
- OBS-виджет по статичной ссылке, оформление по соединению
- Игровой оверлей: drag, сквозные клики, фиксация позиции
- Глобальные горячие клавиши с переназначением

### Исправлено
- Portable EXE открывал пустое окно: Vite HTML и OBS-виджет теперь встроены в assembly и извлекаются в LocalAppData при запуске
- WebView2 virtual-host URL исправлен с неверного `app://index.html` на `https://yawachat.invalid/index.html`
- Кнопки закрытия/сворачивания/разворачивания и drag подключены к WPF через `chrome.webview.postMessage`; добавлен нативный экран диагностики и гарантированный выход
- Desktop EXE больше не запускает ChatSimulator и не добавляет статусные тексты заглушек в чат: лента принимает только реальные `sp:chat` от C#-коннекторов
- Убран белый флэш/полоса WebView2: до успешной навигации виден тёмный нативный экран, WebView2 получает `DefaultBackgroundColor=#0B0E17`
- Исправлены: изменение размера окна (WebView2 больше не закрывает WPF-рамку), перетаскивание оверлея, доставка сообщений в оверлей (рассылка по всем мостам), остановка коннекторов при удалении канала
- Коннекторы переписаны по рабочим протоколам: Twitch — анонимный IRC поверх WebSocket (`wss://irc-ws.chat.twitch.tv:443`, `PASS SCHMOOPIIFS`), Kick — Pusher WebSocket + `api/v2/channels/{slug}`, YouTube — innertube `live_chat` без Data API с CONSENT-куками
- CI: ParserError на шаге «Синхронизировать версию» — экранирование кавычек вынесено в scripts/sync-version.ps1
- CI: `npm ci` падал с EUSAGE из-за отсутствия package-lock.json — установка зависимостей теперь устойчива (npm ci → иначе npm install)
- CI: сборка приведена к Next.js (проверка `.next` вместо `dist/index.html`), релизный архив из standalone-вывода
- CI: desktop-шаги (C#/NSIS) выполняются только при наличии desktop/ — сборка не падает без него
- CI: `next build` падал с «DATABASE_URL is required» на Collecting page data — подключение к БД стало ленивым (пул создаётся при первом запросе, а не при импорте модуля)
- CI: релиз выходил без exe — шаги desktop/NSIS ссылались на несуществующий `desktop/`; workflow теперь авто-детектит desktop (`hashFiles('desktop/**')`) и перед C#/NSIS дополнительно собирает Vite single-file `dist/index.html`
