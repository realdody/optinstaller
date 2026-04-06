@echo off

REM Update this to your local path for cl.exe if needed.
set PATH=%PATH%;c:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.50.35717\bin\Hostx64\x64\

cl.exe /O2 /Wall /FoRTSSHooksCompatibility.obj -nologo /c RTSSHooksCompatibility.c 
if %errorlevel% neq 0 exit /b %errorlevel%
dumpbin.exe /EXPORTS RTSSHooksCompatibility.obj
