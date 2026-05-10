# Next Session — TODO

## Приоритет 0: Файлы исчезают после установки (НЕ РЕШЕНО)

**Симптом:** После установки SCNativeSetup.exe файлы появляются в папке установки (пользователь выбирает диск D), но через ~2-3 секунды бесследно исчезают.

**Что проверено:**
- Windows Defender — полностью отключён (RealTimeProtectionEnabled=False, AntivirusEnabled=False)
- Касперский — даже полный выход из приложения (правый клик → "Выйти") НЕ помог. Процессы avp.exe всё равно висят в памяти и продолжают откатывать файлы
- Касперский `Scan_Qscan` активно сканирует файлы проекта (подтверждено в report.rpt — видно Newtonsoft.Json.dll из Debug-папки)
- Логи Касперского (Traces/KAV.dumpwriter.log) — доступ запрещён, нужны права админа

**Что попробовать:**
1. **Удалить Касперский полностью** через `appwiz.cpl` или Kaspersky Removal Tool (kavremover.exe с сайта). Это самый надёжный способ — avp.exe работает как служба и не убивается через трей
2. **Остановить службу Касперского** — `sc stop AVP21.24` или `net stop avp` из cmd с правами админа
3. **Добавить исключения** — в настройках Касперского: Защита → Исключения → добавить папку установки на диске D и путь к setup.exe
4. **Отключить "Откат изменений"** — Настройки → Дополнительные → Параметры восстановления → снять "Откат изменений"
5. **Подписать exe** — signtool с self-signed сертификатом (Касперский меньше доверяет unsigned exe)

**Вывод:** Проблема 99% в Касперском. Его "Откат изменений" работает на уровне файловой системы (minifilter driver) и откатывает ВСЕ изменения, сделанные процессом, который Касперский считает подозрительным. Даже "Выход" из трея не останавливает службу avp.exe.

## Приоритет 1: Удалить проект Installer/ из solution
WPF-инсталлер больше не нужен — NSIS полностью заменил его. Нужно:
1. Удалить проект `Installer/` из `.sln` файла
2. Можно оставить папку `Installer/` для Resources (app_icon.ico) и Payload (app.zip при сборке), но исходники (MainWindow.xaml, App.xaml и т.д.) можно удалить

## Приоритет 2: Протестировать деинсталлер (Discord Webhook)
Uninstaller переделан с FormSubmit.co на Discord Webhook. Нужно **протестировать** — запустить деинсталлер, проверить что embed приходит в Discord канал.

## Приоритет 3: Мелкие баги/улучшения
- Groq API Key захардкожен в GroqService.cs (строка 15) — вынести в настройки
- LyricsWindow.xaml/.cs — не используется (тексты встроены в MainWindow), можно удалить
- Discord RPC — отключён/вырезан, но пакет DiscordRichPresence всё ещё в csproj

---

# Полная документация проекта

## Структура решения

```
SoundCloudClient/
├── SoundCloudClient/          ← Основное приложение (WPF, .NET 8)
├── Installer/                 ← Бывший WPF-инсталлер (НЕ ИСПОЛЬЗУЕТСЯ, нужен только для Resources/Payload)
│   ├── Resources/             ← app_icon.ico (иконка для NSIS)
│   └── Payload/               ← app/ (файлы приложения) + MicrosoftEdgeWebview2Setup.exe
├── Uninstaller/               ← Кастомный деинсталлер (WPF, .NET 8)
├── setup.nsi                  ← NSIS-скрипт установщика (с автозагрузкой WebView2)
├── build-release.bat          ← Скрипт сборки релиза
├── ReleaseOutput/             ← Готовый SCNativeSetup.exe (~51 MB)
└── Next_session.md
```

---

## Основное приложение (SoundCloudClient)

### Файлы

| Файл | Строк | Назначение |
|------|-------|-----------|
| App.xaml / App.xaml.cs | 9 / 47 | Запуск: splash → MainWindow. Глобальный обработчик непойманных исключений |
| MainWindow.xaml | ~1450 | Основной UI: сайдбар, плеер, список треков, панель текстов, эквалайзер, настройки |
| MainWindow.xaml.cs | ~2340 | Вся логика: плеер, поиск, плейлисты, тексты, эквалайзер, рекомендации, playback queue |
| SoundCloudService.cs | 665 | SoundCloud API v2: авторизация, поиск, лайки, плейлисты, рекомендации |
| LocalLibrary.cs | 114 | Локальное хранилище: лайки + плейлисты → library.json |
| EqSampleProvider.cs | 101 | 10-полосный эквалайзер (NAudio BiQuadFilter, 32Hz–16kHz) |
| LyricsService.cs | 140 | LRCLIB API: загрузка текстов, 4-ступенчатый fallback поиска |
| LyricsCacheService.cs | 141 | Кеш текстов на диске (%LocalAppData%/MusicBox/lyrics_cache/) |
| GroqService.cs | 339 | Groq AI (llama-3.3-70b): анализ жанров, профиля, рекомендации |
| RecommendationService.cs | 403 | 3-уровневый fallback рекомендаций: Groq → SoundCloud → жанровый поиск |
| DiscordRpcService.cs | 181 | Discord Rich Presence (отключён, не используется) |
| SettingsWindow.xaml/.cs | 172 | Настройки: цвета темы, фон (НЕ ИСПОЛЬЗУЕТСЯ — настройки встроены в MainWindow) |
| LoginWindow.xaml/.cs | 36 | Ручной ввод OAuth-токена |
| WebView2LoginWindow.xaml/.cs | ~95 | Браузерный логин через WebView2, извлечение OAuth-куки + обработка ошибок |
| LyricsWindow.xaml/.cs | 315 | Отдельное окно текстов (НЕ ИСПОЛЬЗУЕТСЯ, встроено в MainWindow) |
| CsvImportWindow.xaml/.cs | 101 | Импорт треков из CSV |
| PlaylistPickerDialog.xaml/.cs | ~50 | Диалог выбора плейлиста |
| CreatePlaylistDialog.xaml/.cs | ~50 | Диалог создания плейлиста |
| SplashScreen.xaml | 97 | Экран загрузки: анимация коробки + диска |
| SplashScreen.xaml.cs | 190 | Анимация splash screen по фазам |
| Resources/app_icon.ico | — | Иконка приложения |
| Resources/app_icon.png | — | Иконка для UI |

### Ключевые фичи

#### Плеер
- **Движок:** NAudio (MediaFoundationReader → EqSampleProvider → WaveOutEvent)
- **Форматы:** MP3, WAV, FLAC (через MediaFoundation)
- **Управление:** Play/Pause, Prev/Next, громкость, прогресс-бар
- **Сохранение:** Последний трек и громкость → settings.json
- **Playback Queue:** _playbackQueue + _playbackQueueIndex — очередь запоминается при старте трека, автопереход работает даже если пользователь ушёл в другое меню
- **Защита от двойного воспроизведения:** _playTrackId — если PlayTrack вызван повторно пока первый ещё грузится, старый вызов отменяется

#### Список треков (как в Spotify)
- **Сердечко ♡/♥** — рядом с названием трека. ♡ = не в лайках, ♥ оранжевый = в лайках. Клик переключает. Статус IsLiked подтягивается при загрузке списка
- **Кнопка "+"** — добавить в плейлист, рядом с длительностью. При наведении подсвечивается оранжевым
- **Кнопка "⋯"** — контекстное меню (лайк, плейлист, убрать из плейлиста)
- **Индикатор играющего трека** — оранжевый оверлей с ▶ на обложке трека через IsCurrentlyPlaying

#### 10-полосный эквалайзер
- **Полосы:** 32, 64, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz
- **Диапазон:** ±12 dB (BiQuadFilter peaking EQ)
- **Пресеты:** Flat, Bass, Treble, Vocal, Loud
- **UI:** Вертикальные слайдеры в popup-панели
- **Сохранение:** Настройки → settings.json

#### Тексты + Караоке
- **Источник:** LRCLIB API (бесплатно, без ключа)
- **Форматы:** Обычный текст + LRC (синхронизированные с таймстампами)
- **Караоке:** Построчная подсветка, активная строка — оранжевая 16pt, клик по строке → перемотка
- **Анимации:** ColorAnimation 350ms, FontSize 300ms, AutoScroll 500ms
- **Кеш:** %LocalAppData%/MusicBox/lyrics_cache/
- **Поиск:** 4-ступенчатый fallback (точный → artist+title → title → грязные данные)

#### Локальная библиотека
- **Хранилище:** %LocalAppData%/MusicBox/library.json
- **Лайки:** List<Track> с дедупликацией через HashSet<VideoId>
- **Плейлисты:** Создание, удаление, добавление треков, CSV-импорт

#### SoundCloud интеграция
- **API:** SoundCloud API v2 (api-v2.soundcloud.com)
- **Авторизация:** OAuth-токен (WebView2 браузер или ручной ввод)
- **Возможности:** Поиск, профиль, лайки, плейлисты, рекомендации, извлечение Client ID

#### AI-рекомендации (Groq)
- **Модель:** llama-3.3-70b-versatile
- **Анализ:** Нормализация жанров (~50 маппингов), анализ текстов (настроение, язык, энергия, темы)
- **Fallback-цепочка:** Groq AI → SoundCloud /me/recommendations → жанровый поиск
- **Профиль:** Кешируется в settings.json

#### Панель настроек (встроена в MainWindow)
- **3 карточки** с полупрозрачными фонами и скруглёнными углами:
  - **Внешний вид** — цветовые темы в виде квадратных кнопок-плашек с emoji + превью
  - **Фотообои** — большой превью с оверлеем, кнопки "Выбрать фото" / "Убрать фон"
  - **Интеграции** — Discord Rich Presence с переключателем
- **Groq API key убран из UI** — ключ по-прежнему сохраняется/загружается в settings.json, но поле ввода удалено

#### UI/UX
- **Тема:** Тёмная (#0D0D0D фон, #CC7B3A акцент)
- **Window Chrome:** Кастомный (WindowStyle=None, CornerRadius=12)
- **Сайдбар:** Сворачиваемый (50px иконки ↔ полная ширина), анимация
- **Навигация:** Home, Search, Likes, Library, Settings
- **Фон:** Кастомная картинка + градиент
- **6 пресетов цветов** (настраиваемых в Settings)
- **Иконки:** Material Design Icons (PackIcon)

#### Splash Screen
- **Коробка** (software box) с логотипом "SC NATIVE", 3D-грани
- **Виниловый диск** с лейблом, концентрическими кольцами, иконкой в центре
- **Анимация по фазам:**
  1. Появление коробки (0–0.5с)
  2. Крышка открывается (0.5–1.1с)
  3. Диск поднимается из коробки (0.9–1.5с)
  4. Текст + прогресс-бар (1.2–1.8с)
  5. Вращающийся блик на диске (бесконечно)
- **Прогресс-бар:** 0% → 100% за ~3.2с
- **Статусы:** "Загрузка компонентов..." → "Инициализация плеера..." → "Подключение к SoundCloud..." → "Готово"
- **Закрытие:** Плавный fade-out 0.4с

### Модель Track (обновлена)
- `IsLiked` (bool, JsonIgnore) — статус лайка, обновляется при загрузке списка и клике на сердечко
- `IsCurrentlyPlaying` (bool, JsonIgnore) — подсветка играющего трека в списке
- `Artwork` (BitmapImage, JsonIgnore) — обложка с INotifyPropertyChanged

### Защита от крашей
- **Глобальный DispatcherUnhandledException** — непойманные исключения не крашат приложение
- **AppDomain.CurrentDomain.UnhandledException** — ловит исключения из фоновых потоков
- **_discord.Connect()** обёрнут в try/catch — если Discord не установлен, просто отключает RPC
- **RestoreNavContextAsync** обёрнут в try/catch — сетевые ошибки при восстановлении сессии не крашат
- **_playTrackId** — защита от двойного воспроизведения при быстрых кликах

### NuGet-зависимости
- DiscordRichPresence 1.6.1.70 — *не используется, можно удалить*
- MaterialDesignThemes 5.3.1 — UI-темы
- Microsoft.Web.WebView2 1.0.2792.45 — браузерный логин
- NAudio 2.3.0 — аудио-плеер
- Newtonsoft.Json 13.0.4 — JSON

### Хранилище данных (%LocalAppData%/MusicBox/)
- `settings.json` — OAuth-токен, цвета, громкость, EQ, Groq API key, профиль, repeat, shuffle, animations_enabled
- `library.json` — лайки и плейлисты
- `background.jpg` — кастомный фон
- `lyrics_cache/` — кешированные тексты
- `webview2_data/` — каталог данных WebView2 (НЕ рядом с exe!)

---

## Установщик (NSIS)

### Файлы
- `setup.nsi` — NSIS-скрипт (~115 строк)
- `Installer/Resources/app_icon.ico` — иконка установщика
- `Installer/Payload/MicrosoftEdgeWebview2Setup.exe` — WebView2 Bootstrapper (~1.6 MB)

### Конфигурация
- **Имя:** SC Native
- **Версия:** 1.0.0
- **Путь установки:** C:\Program Files\SC Native
- **Сжатие:** LZMA solid, 64MB dictionary
- **Права:** Admin
- **UI:** Modern UI 2, русский язык
- **Страницы:** Welcome → Выбор папки → Установка → Готово (с чекбоксом "Запустить")
- **Реестр:** HKCU\Software\SCNative + Uninstall-ключи
- **Ярлыки:** Desktop, Start Menu (SC Native + Uninstall)
- **Деинсталлер:** Кастомный SCNativeUninstall.exe (не NSIS)

### WebView2
- WebView2 НЕ устанавливается автоматически через NSIS (убрано из-за ошибки 0x80040c01)
- Приложение само предлагает скачать WebView2 при нажатии "Войти" если Runtime не установлен

### Важно!
- Файл `setup.nsi` должен быть в **UTF-8 без BOM** (NSIS 3.12 с `Unicode true` читает UTF-8)
- В комментариях нельзя заканчивать строку на `\` — NSIS воспримет как line-continuation
- Пути в скрипте используют `${ROOT}\...` — ROOT передаётся через `/DROOT="путь"` при вызове makensis
- `File /r` нужен `*.*` на конце, иначе создаёт подпапку: `File /r "${ROOT}\Installer\Payload\app\*.*"`
- **Пробелы в пути ROOT** — build-release.bat не работает с путями типа "D:\My soundcloud\...", нужно запускать makensis вручную:
  ```
  "C:\Program Files (x86)\NSIS\makensis.exe" /DROOT="D:\My soundcloud\SoundCloudClient" -V2 "D:\My soundcloud\SoundCloudClient\setup.nsi"
  ```

---

## Деинсталлер (Uninstaller)

### Файлы
| Файл | Назначение |
|------|-----------|
| MainWindow.xaml/.cs | UI + логика: опрос причины, комментарий, удаление файлов |
| App.xaml/.cs | Запуск |
| Resources/app_icon.ico, .png | Иконка |

### Фичи
- Опрос причины удаления (баги, нет фич, нашёл альтернативу и т.д.)
- Поле для комментария
- Discord Webhook: отправляет embed с причиной, датой, комментарием
- Удаляет все файлы из папки установки
- Чистит реестр (Uninstall-ключи + Software\SCNative)
- Удаляет ярлыки (Desktop, Start Menu)

### NuGet
- Newtonsoft.Json 13.0.3 — Discord webhook payload

---

## Система сборки

### ⚠️ КАК СОБРАТЬ SETUP (читай внимательно!)

**build-release.bat НЕ РАБОТАЕТ** из-за пробела в пути "D:\My soundcloud\..." — makensis рвёт путь на части и падает.
Нужно запускать этапы вручную через PowerShell:

```powershell
# Этап 1: Публикация приложения
rd -Recurse -Force "D:\My soundcloud\SoundCloudClient\Installer\Payload\app" -ErrorAction SilentlyContinue
dotnet publish "D:\My soundcloud\SoundCloudClient\SoundCloudClient\SoundCloudClient.csproj" -c Release -r win-x64 --self-contained true -o "D:\My soundcloud\SoundCloudClient\Installer\Payload\app" -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -p:XmlDoc=false -p:PublishDocumentationFiles=false

# Этап 2: Публикация деинсталлера
rd -Recurse -Force "D:\My soundcloud\SoundCloudClient\Installer\Payload\uninstaller" -ErrorAction SilentlyContinue
dotnet publish "D:\My soundcloud\SoundCloudClient\Uninstaller\Uninstaller.csproj" -c Release -r win-x64 --self-contained true -o "D:\My soundcloud\SoundCloudClient\Installer\Payload\uninstaller" -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false

# Копируем уникальные DLL деинсталлера в app/ (те, которых нет в app/)
$uninst = "D:\My soundcloud\SoundCloudClient\Installer\Payload\uninstaller"
$app = "D:\My soundcloud\SoundCloudClient\Installer\Payload\app"
Get-ChildItem $uninst -File | Where-Object { -not (Test-Path (Join-Path $app $_.Name)) } | Copy-Item -Destination $app
Get-ChildItem $uninst -Directory | Where-Object { -not (Test-Path (Join-Path $app $_.Name)) } | Copy-Item -Destination $app -Recurse
rd -Recurse -Force $uninstaller

# Этап 3: Копируем иконку
copy "D:\My soundcloud\SoundCloudClient\Installer\Resources\app_icon.ico" "D:\My soundcloud\SoundCloudClient\Installer\Payload\app\app_icon.ico"

# Этап 4: NSIS (ВРУЧНУЮ, не через батник!)
rd -Recurse -Force "D:\My soundcloud\SoundCloudClient\ReleaseOutput" -ErrorAction SilentlyContinue
mkdir "D:\My soundcloud\SoundCloudClient\ReleaseOutput"
& "C:\Program Files (x86)\NSIS\makensis.exe" /DROOT="D:\My soundcloud\SoundCloudClient" -V2 "D:\My soundcloud\SoundCloudClient\setup.nsi"
```

**Результат:** `ReleaseOutput\SCNativeSetup.exe` (~51 MB)

**Важно:**
- Удалять .xml и .pdb из Payload/app/ не обязательно — NSIS всё сожмёт, а при установке они не мешают
- Если makensis не найден — установлен в `C:\Program Files (x86)\NSIS\` (NSIS 3.12 через winget)
- setup.nsi должен быть UTF-8 без BOM
- build-release.bat можно запускать только если путь без пробелов (перенеси проект в `D:\SoundCloudClient\`)

---

## Внешние сервисы

| Сервис | Назначение | Статус |
|--------|-----------|--------|
| SoundCloud API v2 | Поиск, юзер, лайки, рекомендации | Активен |
| LRCLIB API | Тексты треков | Активен |
| Groq API | AI-анализ профиля, рекомендации | Активен (key в коде, не в UI) |
| Discord Webhook | Отзывы деинсталлера | Активен |
| Discord RPC | Rich Presence | Отключён |
| WebView2 | Браузерный логин | Активен (автоустановка через NSIS) |

---

## История сессий

### Сессия 1
- Анализ текстов для рекомендаций (LyricsCacheService, GroqService.AnalyzeLyricsAsync)
- Uninstaller — Discord Webhook (заменён FormSubmit.co)
- Инсталлер — не работал (EmbeddedResource не извлекался в single-file)

### Сессия 2
- **Splash Screen** — создан SplashScreen.xaml/.cs с анимацией коробки + диска
- **NSIS-установщик** — заменён WPF-инсталлер на NSIS (setup.nsi)
  - Установлен NSIS 3.12 через winget
  - Создан setup.nsi с Modern UI 2, русский язык, LZMA-сжатие
  - Обновлён build-release.bat (makensis вместо dotnet publish Installer)
  - Исправлены баги: файлы не распаковывались (File /r путь), кракозябры (Unicode true + UTF-8)
- Сборка релиза: ReleaseOutput/SCNativeSetup.exe (~49 MB)

### Сессия 4
- **WebView2 баг "already initialized"** — пофиксен. Причина: `Source="https://soundcloud.com"` в XAML вызывал авто-инициализацию WebView2 с дефолтным окружением, а код пытался `EnsureCoreWebView2Async(env)` с кастомным dataDir. Фикс: убран Source из XAML, навигация через `CoreWebView2.Navigate()` после инициализации
- **Файлы исчезают после установки** — причина: Касперский "Откат изменений" (молча откатывает файлы от неизвестных exe без подписи). Нужно приостановить защиту Касперского перед установкой
- **NSIS скрипт почищен:**
  - Убран мёртвый `Section "Uninstall"` с `RMDir /r "$INSTDIR"` (опасный мусор)
  - Убраны MUI_UNPAGE_CONFIRM / MUI_UNPAGE_INSTFILES (не используются)
  - WebView2 bootstrapper: `/silent` → `/passive` (показывает прогресс)
  - Убран UninstallIcon (ссылался на несуществующий NSIS uninstaller)
- **Инструкция по сборке** добавлена в next_session.md (build-release.bat не работает из-за пробела в пути)
- Сборка релиза: ReleaseOutput/SCNativeSetup.exe (~51 MB)
- **Редизайн панели настроек** — 3 карточки (Внешний вид, Фотообои, Интеграции) вместо плоского списка
- **Groq API key убран из UI** — поле ввода удалено, ключ остаётся в settings.json
- **Playback Queue** — _playbackQueue + _playbackQueueIndex, автопереход к следующему треку работает даже если пользователь ушёл в другое меню
- **_isAutoNext флаг** — при автопереходе очередь не перезаписывается текущим _tracks
- **Защита от двойного воспроизведения** — StopPlayback() перенесён в начало PlayTrack, _playTrackId отменяет устаревшие вызовы
- **WebView2 каталог данных** — %LocalAppData%/MusicBox/webview2_data вместо папки рядом с exe (фикс "Не удалось создать каталог данных")
- **WebView2 обработка ошибок** — если Runtime не установлен, предложение скачать (кнопка "Да" открывает ссылку)
- **WebView2 автозагрузка в NSIS** — Bootstrapper встроен в установщик, проверяет реестр и ставит /silent если не найден
- **Глобальный обработчик исключений** — DispatcherUnhandledException + AppDomain.UnhandledException
- **Discord RPC try/catch** — если Discord не установлен, просто отключает RPC без краша
- **RestoreNavContextAsync try/catch** — сетевые ошибки при восстановлении сессии не крашат
- **Сердечко ♡/♥** — рядом с названием трека, клик переключает лайк, IsLiked подтягивается при загрузке
- **Кнопка "+"** — добавить в плейлист, рядом с длительностью
- **Индикатор играющего трека** — оранжевый ▶ на обложке через IsCurrentlyPlaying
- Сборка релиза: ReleaseOutput/SCNativeSetup.exe (~51 MB, с WebView2 Bootstrapper)

### Сессия 5 (текущая)
- **WebView2 bootstrapper убран из NSIS** — WebView2 Setup вызывал ошибку 0x80040c01 у пользователей. Теперь приложение само предлагает скачать WebView2 при нажатии "Войти"
- **Баг: чёрный экран при Alt+Tab** — пофиксен. Причина: анимация Opacity→0 при минимизации блокировала свойство. При восстановлении окно оставалось прозрачным. Фикс: BeginAnimation(OpacityProperty, null) после минимизации, Opacity=1 при восстановлении
- **Баг: максимизация за панелью задач** — пофиксен. При WindowStyle=None WPF максимизирует на весь экран включая taskbar. Фикс: вместо WindowState.Maximized используем SystemParameters.WorkArea для позиционирования. Флаг _isMaximized вместо WindowState
- **Сердечки переработаны:**
  - ♡/♥ текст → Material Design PackIcon HeartOutline/Heart (векторные иконки)
  - Сердце теперь **справа от названия** трека (Grid с ColumnDefinition Auto)
  - Обе иконки — кнопки с hover + тултипами
  - **Анимация пульса** при лайке: ScaleTransform 1→1.35→1 (120мс ease-out + 180мс ease-in)
  - Кнопки действий: "+" → PackIcon PlaylistPlus, "⋯" → PackIcon DotsVertical, "▶" → PackIcon Play
- **Repeat (повтор трека)** — кнопка PackIcon Repeat в плеере (справа от Next). При включении — иконка оранжевая, трек повторяется бесконечно. Сохраняется в settings.json
- **Shuffle (перемешивание)** — кнопка PackIcon Shuffle в плеере (слева от Prev). При включении — очередь перемешивается (Fisher-Yates), текущий трек остаётся первым. Сохраняется в settings.json
- **Анимации — настройка вкл/выкл:**
  - При первом запуске (нет settings.json) — MessageBox "Включить анимации интерфейса?"
  - Переключатель "Анимации интерфейса" в карточке "Воспроизведение" в настройках (switch-стиль)
  - Флаг _animationsEnabled сохраняется/загружается из settings.json
- **Красивая кнопка сайдбара:**
  - Вместо текста "◂"/"▸" → PackIcon ChevronLeft/ChevronRight
  - Круглая 24x24 с hover-подсветкой и белой иконкой при наведении
- **Баг: автопереход не работает при первом прослушивании** — пофиксен. Две причины:
  1. Рекурсия: StopPlayback() в PlayTrack вызывал _wavePlayer.Stop() → PlaybackStopped → ещё один автопереход. Фикс: _isPlaying=false ДО StopPlayback
  2. TracksList.SelectedIndex не вызывал SelectionChanged если индекс не изменился. Фикс: автопереход теперь всегда вызывает PlayTrack() напрямую
- **Баг: таймлайн не двигается, время неправильное** — пофиксен. Причины:
  1. ProgressTimer_Tick крашился молча когда _audioReader disposed между проверкой и доступом. Фикс: try/catch + проверка _wavePlayer!=null && _isPlaying
  2. TotalTime=0 для потокового контента → ProgressSlider.Maximum=0 → слайдер не работал. Фикс: если TotalTime==0 показываем "--:--", обновляем Maximum когда становится известен
  3. HLS fallback в GetAudioStreamUrlAsync — если progressive недоступен, пробуем HLS (m3u8)
- Сборка релиза: ReleaseOutput/SCNativeSetup.exe (~50 MB, без WebView2 Bootstrapper)

### Сессия 6 (UI-полировка + новые фичи)

#### UI-полировка (сессия 6a)
- **Title bar** — PackIcons (WindowMinimize/Maximize/Restore/Close), maximize/restore swap через Visibility toggle
- **Custom ScrollBar** — 8px тонкие, без стрелок, hover #22→#44→#66FFFFFF
- **Player polish** — Gradient #141414→#0A0A0A, DropShadow на border, Play button RadialGradient (#FF9E2E→AccentColor) + orange glow DropShadow 40px circle, album art drop shadow
- **Sidebar nav** — Active state с orange accent bar (3px, left-aligned) + Tag="Active" trigger. UpdateNavHighlight() упрощён
- **Track list** — Hover/selected #10/#1AFFFFFF, CornerRadius=8. Playing indicator: 3 animated orange equalizer bars (ScaleY). Section headers: orange mini-bar + brighter text
- **Settings cards** — BorderBrush="#14FFFFFF", BorderThickness=1, CornerRadius=14
- **Greeting sub** — Дата ниже приветствия (GreetingSub TextBlock)
- **Lyrics panel** — Large artwork с DropShadowEffect (BlurRadius=36), OpacityMask для CornerRadius=14
- **Settings redesign** — Hero header с diagonal gradient (#33FF7A00→#0E0E0E), 48px icon с radial gradient + glow. Theme picker: 12 gradient cards (6x2 grid) с names, hover border + scale animation. Apply button с diagonal gradient + blur glow
- **"Create Playlist" button** — Moved from sidebar to Library section header
- **"CSV Import" button** — Moved from sidebar to Settings panel ("Данные" card с DatabaseImportOutline icon)
- **"ДЕЙСТВИЯ" section header** — Removed from sidebar
- **Dynamic accent color system** — AccentColor/AccentLightColor resources (Color), AccentBrush/AccentLightBrush (SolidColorBrush DynamicResource). Все hardcoded #FF7A00 заменены на DynamicResource AccentBrush
- **Theme accent colors** — Dark=#FF7A00, Blue=#4A8FFF, Purple=#9B5BFF, Burgundy=#E0466F, Green=#4ECCA3, Red=#FF5252, Rose=#F06292, Cyan=#26C6DA, Amber=#FFB300, Mint=#69F0AE, Lavender=#B388FF, Ocean=#448AFF (с light variants)
- **_pendingAccent/_pendingAccentLight** — Set on theme click, applied on SettingsApply via ApplyAccentColors()
- **Background blur/darken** — BgBlurSlider (0-30), BgDarkenSlider (0-100). BackgroundDarken Border + BlurEffect. Saved/loaded as bg_blur/bg_darken
- **Bug report feature** — BugReportDialog.xaml/.cs, "Нашли баг?" section in sidebar с BugOutline icon + "Сообщить" button. Discord webhook
- **Collapsed sidebar fix** — Avatar stays visible when collapsed, ToggleSidebarButton accessible. Collapsed width 76px
- **Progress slider alignment** — ModernSlider template: both decrease/increase borders Height=3, VerticalAlignment=Center, SnapsToDevicePixels. SliderProgressWidthConverter for progress bar width
- **EQ redesign** — EqVerticalSlider style: thin 3px track, zero-line, accent thumb (10px white circle + 22px glow halo). EQ Popup: hero header с Equalizer icon, 5 preset buttons, dB scale (+12/0/-12), sliders 110px height, "Сбросить" button с Refresh icon
- **EQ button** — Equalizer PackIcon вместо текста "EQ", круглая 32x32
- **Create playlist button** — Glass style (#0EFFFFFF + #22FFFFFF border), PlaylistPlus icon в accent кружке с glow, hover accent border
- **App border** — RootBorder BorderBrush="#1AFFFFFF" BorderThickness=1
- **Player bar** — Artwork 44x44 (was 56x56), CornerRadius=8, smaller placeholder ♪
- **Volume icon animation** — ScaleTransform pulse 1→1.25→1 при смене иконки (VolumeOff/Low/Medium/High)
- **12 theme cards** — UniformGrid 6x2, 56px height, hover scale 1.06x
- **Custom theme dialog** — "Своя тема" button с PaletteOutline icon, диалог с 2 HEX-полями (accent + light accent), валидация

#### Новые фичи (сессия 6b)
- **Heart position fix** — Переписано с DockPanel на Grid (Column=0 title, Column=1 heart). Больше не съезжает вверх
- **Track list left margin** — Margin="8,1,0,1" на TrackRow
- **Context menu redesigned** — Убраны "Добавить в лайки" и "Добавить в плейлист". Вместо них: "Скачать трек" (SaveFileDialog, MP3). "Убрать из плейлиста" только внутри плейлиста
- **Download track** — DownloadTrack_Click: GetAudioStreamUrlAsync → HttpClient.GetByteArrayAsync → File.WriteAllBytesAsync
- **"In playlist" button** — PlaylistMinus icon, показывается через DataTrigger IsInPlaylist=True. IsInPlaylist обновляется в LoadPlaylistTracks
- **Track model updates** — IsInPlaylist (bool, INotifyPropertyChanged), IsPaused (bool, INotifyPropertyChanged), LocalFilePath (string?)
- **Paused indicator in track list** — PausedIndicator Border с PackIcon Pause, MultiDataTrigger (IsCurrentlyPlaying=True + IsPaused=True) скрывает эквалайзер, показывает паузу. UpdatePauseState() вызывается из PlayPauseButton_Click
- **Custom theme** — CustomTheme_Click: программный диалог с 2 TextBox'ами для HEX accent/light accent
- **Add local music** — AddLocalMusicBtn в Library header, OpenFileDialog multiselect (mp3/wav/flac/ogg/m4a/aac/wma). MediaFoundationReader для длительности. LocalFilePath для воспроизведения. PlayTrack проверяет VideoId.StartsWith("local_")
- **Shuffle fix** — UpdateShuffleIcon() использует AccentBrush вместо хардкода. Вызывается при загрузке настроек (Dispatcher.InvokeAsync Loaded priority)
- **Repeat icon fix** — Тоже AccentBrush вместо хардкода
- **SoundCloud mixes on home** — Секция "SoundCloud миксы для вас" через _soundcloud.GetRecommendationsAsync(), 20 треков
- **StringExtensions** — ReplaceInvalidFileNameChars() для безопасных имён файлов при скачивании

#### Ключевые файлы (обновлённые размеры)
- MainWindow.xaml: ~1450 строк (было ~1197)
- MainWindow.xaml.cs: ~3230 строк (было ~2844)
- BugReportDialog.xaml: 158 строк
- BugReportDialog.xaml.cs: 105 строк
- SoundCloudService.cs: 683 строки (GetRecommendationsAsync уже был)

#### GradientStop dynamic update
GradientStop.Color не поддерживает DynamicResource. Решение: именованные кисти (SettingsHeroBrush и т.д.) + программное обновление в ApplyAccentColors() через GradientStops[i].Color. Кнопки в ControlTemplate обновляются через UpdateTemplateGradient() (FindName("Bd") → Background as LinearGradientBrush)

#### Известные баги / TODO на следующую сессию
- **Heart animation** — AnimateHeartPulse ищет ScaleTransform по имени HeartScaleFull/HeartScaleEmpty, но они внутри ControlTemplate — FindName может не работать. Нужно тестировать
- **Local file playback** — PlayTrack проверяет VideoId.StartsWith("local_") и использует LocalFilePath как streamUrl. MediaFoundationReader должен открывать локальные файлы напрямую. НО: если LocalFilePath не сохраняется в library.json (JsonIgnore нет, но Track сериализуется через Newtonsoft), нужно проверить
- **Download track** — Сохраняет как MP3, но SoundCloud отдаёт HLS/прогрессивный поток. Файл может быть не валидным MP3. Нужен тест
- **BugReportDialog** — Webhook URL захардкожен. Работает, но стоит вынести
- **SettingsWindow.xaml/.cs** — Всё ещё существует, но не используется (настройки встроены в MainWindow). Можно удалить
- **LyricsWindow.xaml/.cs** — Не используется, можно удалить
- **DiscordRichPresence** — Пакет в csproj, но RPC отключён. Можно удалить пакет
