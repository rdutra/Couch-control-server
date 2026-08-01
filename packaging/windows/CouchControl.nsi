Unicode true
RequestExecutionLevel user

!define APP_NAME "CouchCTRL Windows Companion"
!define APP_PUBLISHER "CouchCTRL"
!define APP_VERSION "1.2.2"
!define PACKAGE_ROOT "..\..\artifacts\win-x64\CouchControl"
!define SETUP_OUTPUT "..\..\artifacts\win-x64\CouchControlSetup-win-x64.exe"

Name "${APP_NAME}"
OutFile "${SETUP_OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\CouchControl"
InstallDirRegKey HKCU "Software\CouchControl" "InstallDir"
BrandingText "${APP_NAME}"

!include "MUI2.nsh"

!define MUI_ABORTWARNING
!define MUI_ICON "..\..\src\CouchControl.Agent\Assets\couchcontrol.ico"
!define MUI_UNICON "..\..\src\CouchControl.Agent\Assets\couchcontrol.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\agent\CouchControl.Agent.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch CouchControl Agent"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "CouchControl Agent and CLI" SecMain
  SectionIn RO

  RMDir /r "$SMPROGRAMS\CouchControl"

  SetOutPath "$INSTDIR\agent"
  File /r "${PACKAGE_ROOT}\agent\*.*"

  SetOutPath "$INSTDIR\cli"
  File /r "${PACKAGE_ROOT}\cli\*.*"

  SetOutPath "$INSTDIR"
  File "${PACKAGE_ROOT}\README-INSTALL.md"
  File "${PACKAGE_ROOT}\PRIVACY.md"
  File "${PACKAGE_ROOT}\SUPPORT.md"
  File "${PACKAGE_ROOT}\VERSION"
  File "${PACKAGE_ROOT}\uninstall.ps1"

  WriteRegStr HKCU "Software\CouchControl" "InstallDir" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\CouchCTRL"
  CreateShortcut "$SMPROGRAMS\CouchCTRL\CouchCTRL Agent.lnk" "$INSTDIR\agent\CouchControl.Agent.exe" "" "$INSTDIR\agent\CouchControl.Agent.exe" 0
  CreateShortcut "$SMPROGRAMS\CouchCTRL\CouchCTRL CLI.lnk" "$SYSDIR\cmd.exe" '/k "$INSTDIR\cli\CouchControl.Cli.exe"' "$INSTDIR\cli\CouchControl.Cli.exe" 0
  CreateShortcut "$SMPROGRAMS\CouchCTRL\Uninstall CouchCTRL.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Start CouchControl Agent when I sign in" SecStartup
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CouchControl.Agent" '"$INSTDIR\agent\CouchControl.Agent.exe"'
SectionEnd

Section "Uninstall"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CouchControl.Agent"
  DeleteRegKey HKCU "Software\CouchControl"

  Delete "$SMPROGRAMS\CouchCTRL\CouchCTRL Agent.lnk"
  Delete "$SMPROGRAMS\CouchCTRL\CouchCTRL CLI.lnk"
  Delete "$SMPROGRAMS\CouchCTRL\Uninstall CouchCTRL.lnk"
  RMDir "$SMPROGRAMS\CouchCTRL"
  RMDir /r "$SMPROGRAMS\CouchControl"

  RMDir /r "$INSTDIR\agent"
  RMDir /r "$INSTDIR\cli"
  Delete "$INSTDIR\README-INSTALL.md"
  Delete "$INSTDIR\PRIVACY.md"
  Delete "$INSTDIR\SUPPORT.md"
  Delete "$INSTDIR\VERSION"
  Delete "$INSTDIR\uninstall.ps1"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
