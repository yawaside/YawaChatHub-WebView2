# Changelog

## YawaChatHub v4.0.0

### Добавлено
- Восстановлен desktop-проект: C# WPF/WebView2 (окно, оверлей, SAPI5, WidgetServer, хоткеи, коннекторы) — **только portable**, без NSIS-установщика
- Единая лента сообщений Twitch, YouTube Live, VK Video Live, Kick, TikTok Live
- Озвучка стандартными голосами Windows (SAPI5), SSML с принудительным ru-RU
- OBS-виджет по статичной ссылке, оформление по соединению
- Игровой оверлей: drag, сквозные клики, фиксация позиции
- Глобальные горячие клавиши с переназначением

### Исправлено
- CI: desktop-шаги (C#/NSIS) выполняются только при наличии desktop/ — сборка не падает без него
- CI: `next build` падал с «DATABASE_URL is required» на Collecting page data — подключение к БД стало ленивым (пул создаётся при первом запросе, а не при импорте модуля)
- CI: релиз выходил без exe — шаги desktop ссылались на несуществующий `desktop/`; workflow теперь авто-детектит desktop (`hashFiles('desktop/**')`) и перед C# дополнительно собирает Vite single-file `dist/index.html`
- CI: `npm ci` → EUSAGE без `package-lock.json` — workflow поддерживает оба варианта
- CI: `CS0102: ChatMsg.Text already defined` — auto-property `Text` конфликтовала со static-фабрикой; переименована в `ChatMsg.Create(...)` (все вызовы обновлены)
- CI: `CS0104: 'Application' is an ambiguous reference` — `UseWindowsForms` (трей) убран, везде явное `System.Windows.Application`
- CI: `makensis: not recognized` — NSIS-установщик полностью убран из репозитория и CI; portable-релиз публикуется как `YawaChatHub.exe` + папки `dist/` и `widget/`
- CI: `icon.ico` отсутствует — `ApplicationIcon` восстановлен; файл `desktop/build/icon.ico` генерируется в CI (`node scripts/make-ico.mjs`) и коммитится в репозиторий
- CI: `CS0103: The name 'ConnectorManager' does not exist` — `BridgeHost.cs` перемещён в namespace `YawaChatHub.Services`
- CI: ParserError на шаге «Синхронизировать версию» — экранирование кавычек вынесено в `scripts/sync-version.ps1`
