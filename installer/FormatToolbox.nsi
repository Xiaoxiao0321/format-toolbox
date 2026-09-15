Unicode True
RequestExecutionLevel user

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

!define APP_NAME "格式转换工具箱"
!ifndef APP_VERSION
!define APP_VERSION "1.0.8"
!endif
!define APP_PUBLISH "..\artifacts\win-x64"
!define APP_EXE "格式转换工具箱.exe"
!define APP_REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\FormatToolbox"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "..\artifacts\installer\格式转换工具箱-Setup-${APP_VERSION}-win-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\FormatToolbox"
InstallDirRegKey HKCU "Software\FormatToolbox" "InstallDir"
BrandingText "${APP_NAME} · 本地离线转换"
SetCompressor /SOLID lzma
Icon "..\src\FormatToolbox.App\app-icon.ico"
UninstallIcon "..\src\FormatToolbox.App\app-icon.ico"

!define MUI_ABORTWARNING
!define MUI_ICON "..\src\FormatToolbox.App\app-icon.ico"
!define MUI_UNICON "..\src\FormatToolbox.App\app-icon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "启动格式转换工具箱"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "本程序仅支持 64 位 Windows 10/11。"
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "本程序需要 Windows 10 或更高版本。"
    Abort
  ${EndIf}
  SetRegView 64
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" "Installed"
  ${If} $0 != 1
    MessageBox MB_ICONEXCLAMATION|MB_OKCANCEL "未检测到 Microsoft Visual C++ 2015–2022 x64 Runtime。$\r$\n$\r$\n图片与 PDF 功能仍可安装，但离线 OCR 可能无法启动。请从 Microsoft 官方安装 VC++ x64 运行库后使用 OCR。$\r$\n$\r$\n是否继续安装？" /SD IDOK IDOK continue
    Abort
    continue:
  ${EndIf}
FunctionEnd

Section "主程序（必选）" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /r "${APP_PUBLISH}\*.*"
  WriteUninstaller "$INSTDIR\卸载格式转换工具箱.exe"
  WriteRegStr HKCU "Software\FormatToolbox" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${APP_REGKEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${APP_REGKEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${APP_REGKEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${APP_REGKEY}" "Publisher" "FormatToolbox Contributors"
  WriteRegStr HKCU "${APP_REGKEY}" "UninstallString" '"$INSTDIR\卸载格式转换工具箱.exe"'
  WriteRegDWORD HKCU "${APP_REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${APP_REGKEY}" "NoRepair" 1
  CreateDirectory "$SMPROGRAMS\格式转换工具箱"
  CreateShortcut "$SMPROGRAMS\格式转换工具箱\格式转换工具箱.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\格式转换工具箱\卸载.lnk" "$INSTDIR\卸载格式转换工具箱.exe"
SectionEnd

Section /o "创建桌面快捷方式" SecDesktop
  CreateShortcut "$DESKTOP\格式转换工具箱.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\格式转换工具箱.lnk"
  RMDir /r "$SMPROGRAMS\格式转换工具箱"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${APP_REGKEY}"
  DeleteRegKey HKCU "Software\FormatToolbox"
  MessageBox MB_ICONQUESTION|MB_YESNO "是否同时删除本机转换历史和诊断日志？$\r$\n$\r$\n选择“否”会保留：$LOCALAPPDATA\FormatToolbox" /SD IDNO IDNO keepdata
  RMDir /r "$LOCALAPPDATA\FormatToolbox"
  keepdata:
SectionEnd
