; ══════════════════════════════════════════════════════════
;   SC Native - NSIS Setup Script
; ══════════════════════════════════════════════════════════

Unicode true

!define APPNAME "SC Native"
!define APPNAME_INTERNAL "SCNative"
!define APPVERSION "1.0.0"
!define APPEXE "SoundCloudClient.exe"
!define UNINSTEXE "SCNativeUninstall.exe"

; ROOT передается через /DROOT=... при вызове makensis
!ifndef ROOT
  !define ROOT "."
!endif

; ── Modern UI ──
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

; ── Настройки ──
Name "${APPNAME}"
OutFile "${ROOT}\ReleaseOutput\SCNativeSetup.exe"
InstallDir "$PROGRAMFILES64\${APPNAME}"
InstallDirRegKey HKCU "Software\${APPNAME_INTERNAL}" "InstallPath"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 64

; ── Интерфейс ──
Icon "${ROOT}\Installer\Resources\app_icon.ico"
!define MUI_ICON "${ROOT}\Installer\Resources\app_icon.ico"
!define MUI_UNICON "${ROOT}\Installer\Resources\app_icon.ico"

!define MUI_ABORTWARNING

; ── Страницы ──
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

; Финишная страница - чекбокс Запустить
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPEXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить ${APPNAME}"
!insertmacro MUI_PAGE_FINISH

; ── Страницы деинсталлера не нужны — используем кастомный SCNativeUninstall.exe ──

; ── Язык ──
!insertmacro MUI_LANGUAGE "Russian"

; ══════════════════════════════════════════════════════════
;   Секция установки
; ══════════════════════════════════════════════════════════
Section "Install" SecInstall
    SetOutPath "$INSTDIR"

    ; Распаковка файлов приложения (содержимое app/ прямо в INSTDIR)
    File /r "${ROOT}\Installer\Payload\app\*.*"

    ; Копируем деинсталлер в корень установки (он уже в app/ после merge DLL)
    ; SCNativeUninstall.exe уже среди файлов app/ — NSIS распакует его в $INSTDIR

    ; WebView2 НЕ устанавливается автоматически — приложение само предложит скачать при входе

    ; Запись пути установки в реестр
    WriteRegStr HKCU "Software\${APPNAME_INTERNAL}" "InstallPath" "$INSTDIR"

    ; Регистрируем кастомный деинсталлер
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "DisplayName" "${APPNAME}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "DisplayVersion" "${APPVERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "Publisher" "SC Native"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "DisplayIcon" "$INSTDIR\${APPEXE},0"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "UninstallString" '"$INSTDIR\${UNINSTEXE}"'
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "NoRepair" 1

    ; Размер установки
    ${getSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME_INTERNAL}" "EstimatedSize" "$0"

    ; Ярлыки
    CreateDirectory "$SMPROGRAMS\${APPNAME}"
    CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
    CreateShortCut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\${UNINSTEXE}" "" "$INSTDIR\${UNINSTEXE}" 0
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
SectionEnd

; ══════════════════════════════════════════════════════════
;   Деинсталляция — через кастомный SCNativeUninstall.exe
;   (NSIS uninstaller не используется, секция удалена)
; ══════════════════════════════════════════════════════════
