Unicode true
!include "MUI2.nsh"
!define APPNAME "Another DLSS Tool"
!define APPEXE "AnotherDLSSTool.exe"
!define VERSION "2.6.0"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\AnotherDLSSTool"
!define OLDKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DLSS5FeederSetup"
!define OLDNAME "DLSS 5 Feeder Setup"

Name "${APPNAME}"
OutFile "out\Another-DLSS-Tool-Installer.exe"
InstallDir "$LOCALAPPDATA\Programs\Another DLSS Tool"
InstallDirRegKey HKCU "${UNINSTKEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Front end for the open-source DLSS5-Feeder project"

!define MUI_ICON "stage\app.ico"
!define MUI_UNICON "stage\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP "stage\welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "stage\welcome.bmp"
!define MUI_WELCOMEPAGE_TEXT "This installs ${APPNAME}, an app that adds DLSS 5 neural rendering to Direct3D 9, 11 and 12 games using the official DLSS5-Feeder project.$\r$\n$\r$\nIt includes nvngx_dlss.dll (NVIDIA-signed) and the community RTX 40 build of nvngx_dlssnr.dll, which is not signed by NVIDIA.$\r$\n$\r$\nNothing in any game changes until you press Install inside the app."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPEXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Open ${APPNAME}"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\README.txt"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  IntCmp $0 378389 net_ok net_missing net_ok
  net_missing:
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "${APPNAME} needs .NET Framework 4.5 or newer, which this PC doesn't seem to have. Continue anyway?" IDYES net_ok
    Abort
  net_ok:
FunctionEnd

Section "${APPNAME}" SecApp
  SectionIn RO
  ; Replace an earlier install made under the old name, DLSS 5 Feeder Setup.
  ReadRegStr $1 HKCU "${OLDKEY}" "InstallLocation"
  StrCmp $1 "" old_done
    Delete "$1\DLSS5Setup.exe"
    Delete "$1\README.txt"
    Delete "$1\app.ico"
    Delete "$1\files\nvngx_dlss.dll"
    Delete "$1\files\nvngx_dlssnr.dll"
    Delete "$1\files\SHA256.txt"
    RMDir "$1\files"
    Delete "$1\Uninstall.exe"
    RMDir "$1"
    Delete "$SMPROGRAMS\${OLDNAME}.lnk"
    Delete "$DESKTOP\${OLDNAME}.lnk"
    DeleteRegKey HKCU "${OLDKEY}"
  old_done:
  SetOutPath "$INSTDIR"
  File "stage\${APPEXE}"
  File "stage\README.txt"
  File "stage\app.ico"
  SetOutPath "$INSTDIR\files"
  File "stage\files\nvngx_dlss.dll"
  File "stage\files\nvngx_dlssnr.dll"
  File "stage\files\SHA256.txt"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\app.ico"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\app.ico"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" 335000
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\app.ico"
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} "The app and the bundled NVIDIA files."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Put a shortcut on the desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function un.onInit
  MessageBox MB_OKCANCEL|MB_ICONINFORMATION "This removes the app only.$\r$\n$\r$\nGames you installed the feeder into keep it. To undo those, cancel now, open the app, and press Uninstall on each game first." IDOK +2
  Abort
FunctionEnd

Section "Uninstall"
  Delete "$INSTDIR\${APPEXE}"
  Delete "$INSTDIR\README.txt"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\files\nvngx_dlss.dll"
  Delete "$INSTDIR\files\nvngx_dlssnr.dll"
  Delete "$INSTDIR\files\SHA256.txt"
  RMDir "$INSTDIR\files"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"
  DeleteRegKey HKCU "${UNINSTKEY}"
SectionEnd
