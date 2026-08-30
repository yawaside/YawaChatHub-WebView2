; NSIS-установщик YawaChatHub (ТЗ §18.5)
; Ярлыки в «Пуск» и на рабочий стол, выбор папки, русский язык (1049),
; удаление через «Программы и компоненты».
;
; Сборка:
;   makensis installer.nsi "/DVERSION=4.0.0" "/DPORTABLE=publish/portable/YawaChatHub.exe"

Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel admin

!define VERSION "4.0.0"
!define PORTABLE_EXE "publish/portable/YawaChatHub.exe"

Name "YawaChatHub"
Caption "Установка YawaChatHub ${VERSION}"
OutFile "installer/YawaChatHub-Setup.exe"

InstallDir "$PROGRAMFILES64\YawaChatHub"
InstallDirRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" "InstallLocation"

!include "MUI2.nsh"

!define MUI_ICON "build\icon.ico"
!define MUI_UNICON "build\icon.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Russian"

Section "Установка" SecMain
  SetOutPath "$INSTDIR"

  ; portable exe + статичный фронтенд (dist) + виджет OBS
  File "/oname=YawaChatHub.exe" "${PORTABLE_EXE}"
  File /r "/nonfatal" "publish\portable\dist"
  File /r "/nonfatal" "publish\portable\widget"

  ; ярлыки
  CreateDirectory "$SMPROGRAMS\YawaChatHub"
  CreateShortCut "$SMPROGRAMS\YawaChatHub\YawaChatHub.lnk" "$INSTDIR\YawaChatHub.exe"
  CreateShortCut "$DESKTOP\YawaChatHub.lnk" "$INSTDIR\YawaChatHub.exe"

  ; удаление через «Программы и компоненты»
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" \
    "DisplayName" "YawaChatHub"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" \
    "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" \
    "DisplayIcon" "$INSTDIR\YawaChatHub.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" \
    "Publisher" "YawaChatHub"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" \
    "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" \
    "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" \
    "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub" \
    "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\YawaChatHub.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir /r "$INSTDIR\dist"
  RMDir /r "$INSTDIR\widget"
  RMDir "$INSTDIR"

  Delete "$DESKTOP\YawaChatHub.lnk"
  Delete "$SMPROGRAMS\YawaChatHub\YawaChatHub.lnk"
  RMDir "$SMPROGRAMS\YawaChatHub"

  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YawaChatHub"
SectionEnd
