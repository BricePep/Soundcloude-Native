@echo off
chcp 65001 >nul
echo.
echo  ══════════════════════════════════════════
echo    SC Native — Сборка установщика (NSIS)
echo  ══════════════════════════════════════════
echo.

set ROOT=%~dp0
set PAYLOAD=%ROOT%Installer\Payload
set NSIS="C:\Program Files (x86)\NSIS\makensis.exe"

:: ═══ Этап 1: Публикация приложения (self-contained, много файлов) ═══
echo  [1/4] Публикация приложения (self-contained)...
echo         Это может занять пару минут...
echo.

rd /s /q "%PAYLOAD%\app" 2>nul
mkdir "%PAYLOAD%\app"

dotnet publish "%ROOT%SoundCloudClient\SoundCloudClient.csproj" ^
    -c Release -r win-x64 --self-contained true ^
    -o "%PAYLOAD%\app" ^
    -p:PublishSingleFile=false ^
    -p:PublishTrimmed=false ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    -p:XmlDoc=false ^
    -p:PublishDocumentationFiles=false

if %ERRORLEVEL% neq 0 (
    echo.
    echo  ОШИБКА: публикация приложения не удалась
    pause
    exit /b 1
)

:: Удаляем мусор
del /q "%PAYLOAD%\app\*.xml" 2>nul
del /q "%PAYLOAD%\app\*.pdb" 2>nul

echo  ✓ Приложение опубликовано

:: ═══ Этап 2: Публикация деинсталлятора ═══
echo.
echo  [2/4] Публикация деинсталлятора...

rd /s /q "%PAYLOAD%\uninstaller" 2>nul

dotnet publish "%ROOT%Uninstaller\Uninstaller.csproj" ^
    -c Release -r win-x64 --self-contained true ^
    -o "%PAYLOAD%\uninstaller" ^
    -p:PublishSingleFile=false ^
    -p:PublishTrimmed=false ^
    -p:DebugType=none ^
    -p:DebugSymbols=false

if %ERRORLEVEL% neq 0 (
    echo.
    echo  ОШИБКА: публикация деинсталлятора не удалась
    pause
    exit /b 1
)

:: Копируем деинсталлеру только СВОИ dll (которых нет в папке приложения)
:: Runtime dll уже есть от приложения — не дублируем
for %%F in ("%PAYLOAD%\uninstaller\*") do (
    if not exist "%PAYLOAD%\app\%%~nxF" (
        copy /y "%%F" "%PAYLOAD%\app\%%~nxF" >nul
    )
)

:: Копируем подпапки локалей если их нет
for /d %%D in ("%PAYLOAD%\uninstaller\*") do (
    if not exist "%PAYLOAD%\app\%%~nxD" (
        xcopy "%%D" "%PAYLOAD%\app\%%~nxD\" /E /I /Q /Y >nul
    )
)

rd /s /q "%PAYLOAD%\uninstaller" 2>nul

echo  ✓ Деинсталлятор опубликован

:: ═══ Этап 3: Копируем иконку в Payload\app ═══
echo.
echo  [3/4] Подготовка файлов...

:: Копируем иконку приложения (нужна для ярлыков)
copy /y "%ROOT%Installer\Resources\app_icon.ico" "%PAYLOAD%\app\app_icon.ico" >nul

echo  ✓ Файлы подготовлены

:: ═══ Этап 4: Сборка установщика через NSIS ═══
echo.
echo  [4/4] Сборка установщика (NSIS)...

:: Очищаем ReleaseOutput
rd /s /q "%ROOT%ReleaseOutput" 2>nul
mkdir "%ROOT%ReleaseOutput"

:: NSIS /D не поддерживает пробелы в значении — создаём junction на путь без пробелов
set NSIS_JUNCTION=C:\SCBuild
if not exist "%NSIS_JUNCTION%" (
    mklink /J "%NSIS_JUNCTION%" "%ROOT%" >nul 2>&1
)

%NSIS% /DROOT=%NSIS_JUNCTION% -V2 "%NSIS_JUNCTION%\setup.nsi" 2>&1

if %ERRORLEVEL% neq 0 (
    echo.
    echo  ОШИБКА: NSIS сборка не удалась
    pause
    exit /b 1
)

:: Удаляем мусор из ReleaseOutput
del /q "%ROOT%ReleaseOutput\*.pdb" 2>nul
del /q "%ROOT%ReleaseOutput\*.deps.json" 2>nul

:: ═══ Готово ═══
echo.
echo  ══════════════════════════════════════════
echo    Готово!
echo.
echo    Установщик: ReleaseOutput\SCNativeSetup.exe
echo.
echo    После установки в папке будет:
echo    - SoundCloudClient.exe  (маленький)
echo    - SCNativeUninstall.exe (кастомный деинсталлер)
echo    - .NET Runtime DLL      (общие)
echo    - Зависимости           (NAudio, и т.д.)
echo.
echo    Просто отправь этот файл другу.
echo  ══════════════════════════════════════════
echo.

:: Показываем размер
for %%A in ("%ROOT%ReleaseOutput\SCNativeSetup.exe") do (
    set /a SIZE_MB=%%~zA / 1048576
    echo  Размер: ~!SIZE_MB! MB
)
echo.
pause
