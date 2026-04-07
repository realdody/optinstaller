@echo off
setlocal

set "ScriptDir=%~dp0"

where cl.exe >nul 2>nul
if errorlevel 1 (
  set "VsWhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
  if not exist "%VsWhere%" (
    echo Could not find cl.exe or vswhere.exe. Install Visual Studio Build Tools with C++ support, or run this from a Developer Command Prompt.
    exit /b 1
  )

  for /f "usebackq delims=" %%i in (`"%VsWhere%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALLDIR=%%i"

  if not defined VSINSTALLDIR (
    echo Could not find a Visual Studio installation with the MSVC x64 toolchain.
    exit /b 1
  )

  call "%VSINSTALLDIR%\Common7\Tools\VsDevCmd.bat" -no_logo -arch=x64 -host_arch=x64
  if errorlevel 1 exit /b 1
)

cl.exe /O2 /Wall /Fo"%ScriptDir%RTSSHooksCompatibility.obj" -nologo /c "%ScriptDir%RTSSHooksCompatibility.c"
if %errorlevel% neq 0 exit /b %errorlevel%
dumpbin.exe /EXPORTS "%ScriptDir%RTSSHooksCompatibility.obj"
