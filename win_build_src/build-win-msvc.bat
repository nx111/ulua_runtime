@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Failed to enter script directory: %SCRIPT_DIR%
  exit /b 1
)

set "TARGET="
set "ENABLE_FR2="

:parse_args
if "%~1"=="" goto :args_done
if /I "%~1"=="x64" (
  set "TARGET=x64"
  shift
  goto :parse_args
)
if /I "%~1"=="x86" (
  set "TARGET=x86"
  shift
  goto :parse_args
)
if /I "%~1"=="-fr2" (
  set "ENABLE_FR2=1"
  shift
  goto :parse_args
)
if /I "%~1"=="-h" goto :usage_ok
if /I "%~1"=="--help" goto :usage_ok
echo [ERROR] Unknown argument: %~1
goto :usage_error

:args_done
if not defined TARGET set "TARGET=x64"

if /I "%TARGET%"=="x64" (
  set "OUT_SUBDIR=x86_64"
  set "MACHINE=X64"
  set "TARGET_LUAJIT_DEFS=%WIN64_LUAJIT_DEFS%"
  if defined ENABLE_FR2 set "TARGET_LUAJIT_DEFS=%TARGET_LUAJIT_DEFS% /DLUAJIT_ENABLE_GC64"
) else (
  set "OUT_SUBDIR=x86"
  set "MACHINE=X86"
  set "TARGET_LUAJIT_DEFS="
  if defined ENABLE_FR2 (
    echo [ERROR] -fr2 is only supported for x64.
    goto :error
  )
)

call :ensure_msvc_env "%TARGET%"
if errorlevel 1 goto :error

set "API_COMPAT_DEF=/Dlua_tolstring=lua_tolstring_internal /Dlua_pcall=lua_pcall_internal"
set "PLUGIN_COMPAT_DEF=%API_COMPAT_DEF% /DluaL_reg=luaL_Reg"
set "LUAJIT_DIR=luajit"
set "COMMON_CFLAGS=/nologo /c /O2 /W3 /MT /D_CRT_SECURE_NO_WARNINGS %API_COMPAT_DEF% %TARGET_LUAJIT_DEFS%"
set "PLUGIN_CFLAGS=/nologo /c /O2 /W3 /MT /D_CRT_SECURE_NO_WARNINGS %PLUGIN_COMPAT_DEF% %TARGET_LUAJIT_DEFS%"
set "COMMON_INCLUDES=/I. /I%LUAJIT_DIR%\src /Ipbc /Ipbc\src /Icjson /Iluasocket\src"

set "BUILD_DIR=build\msvc\%TARGET%"
set "OBJ_PBC=%BUILD_DIR%\obj\pbc"
set "OBJ_CJSON=%BUILD_DIR%\obj\cjson"
set "OBJ_PLUGIN=%BUILD_DIR%\obj\plugin"
set "LUAJIT_LIB=%LUAJIT_DIR%\src\lua51.lib"
set "PBC_LIB=window\%OUT_SUBDIR%\libpbc.lib"
set "CJSON_LIB=window\%OUT_SUBDIR%\libcjson.lib"
set "OUT_DLL=Plugins\%OUT_SUBDIR%\ulua.dll"

if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
mkdir "%OBJ_PBC%" "%OBJ_CJSON%" "%OBJ_PLUGIN%" >nul 2>nul
mkdir "window\%OUT_SUBDIR%" >nul 2>nul
mkdir "Plugins\%OUT_SUBDIR%" >nul 2>nul

echo [1/4] Building LuaJIT static library...
pushd "%LUAJIT_DIR%\src" >nul
set "OLD_CL=%CL%"
set "CL=%API_COMPAT_DEF% %TARGET_LUAJIT_DEFS% %CL%"
if exist "lua51.lib" del /q "lua51.lib" >nul 2>nul
call msvcbuild.bat static
set "LUAJIT_RC=%ERRORLEVEL%"
set "CL=%OLD_CL%"
popd >nul

if not exist "%LUAJIT_LIB%" (
  echo [ERROR] Missing LuaJIT static library: %LUAJIT_LIB%
  goto :error
)
if not "%LUAJIT_RC%"=="0" (
  echo [WARN] LuaJIT static library was built, but luajit.exe link failed. Continuing with %LUAJIT_LIB%.
)

echo [2/4] Building pbc static library...
set "PBC_SRCS=context.c varint.c array.c pattern.c register.c proto.c map.c alloc.c rmessage.c wmessage.c bootstrap.c stringpool.c decode.c"
for %%F in (%PBC_SRCS%) do (
  cl %COMMON_CFLAGS% /FI"msvc_stdbool.h" /I"pbc" /I"pbc\src" /Fo"%OBJ_PBC%\%%~nF.obj" "pbc\src\%%F"
  if errorlevel 1 goto :error
)
lib /nologo /OUT:"%PBC_LIB%" "%OBJ_PBC%\*.obj"
if errorlevel 1 goto :error

echo [3/4] Building cjson static library...
set "CJSON_SRCS=fpconv.c strbuf.c"
for %%F in (%CJSON_SRCS%) do (
  cl %COMMON_CFLAGS% /Fo"%OBJ_CJSON%\%%~nF.obj" "cjson\%%F"
  if errorlevel 1 goto :error
)
lib /nologo /OUT:"%CJSON_LIB%" "%OBJ_CJSON%\*.obj"
if errorlevel 1 goto :error

echo [4/4] Building ulua.dll...
set "PLUGIN_SRCS=lua_wrap.c pb_win.c lpeg.c sproto.c lsproto.c luasocket/src/luasocket.c luasocket/src/timeout.c luasocket/src/buffer.c luasocket/src/io.c luasocket/src/auxiliar.c luasocket/src/options.c luasocket/src/inet.c luasocket/src/tcp.c luasocket/src/udp.c luasocket/src/except.c luasocket/src/select.c luasocket/src/wsocket.c pbc/binding/lua/pbc-lua.c cjson/lua_cjson.c"
for %%F in (%PLUGIN_SRCS%) do (
  cl %PLUGIN_CFLAGS% /FI"msvc_stdbool.h" %COMMON_INCLUDES% /Fo"%OBJ_PLUGIN%\%%~nF.obj" "%%F"
  if errorlevel 1 goto :error
)

link /nologo /DLL /OUT:"%OUT_DLL%" /MACHINE:%MACHINE% /INCREMENTAL:NO /OPT:REF /OPT:ICF ^
  /DEF:"ulua_exports.def" ^
  "%OBJ_PLUGIN%\*.obj" ^
  "%LUAJIT_LIB%" ^
  "%PBC_LIB%" ^
  "%CJSON_LIB%" ^
  wsock32.lib
if errorlevel 1 goto :error

echo [OK] Built %OUT_DLL%
popd >nul
exit /b 0

:usage_ok
echo Usage: %~nx0 [x64^|x86] [-fr2]
echo Example: %~nx0 x64 -fr2
popd >nul
exit /b 0

:usage_error
echo Usage: %~nx0 [x64^|x86] [-fr2]
echo Example: %~nx0 x64 -fr2
goto :error

:ensure_msvc_env
set "REQ_TARGET=%~1"
set "NEED_INIT="

where cl >nul 2>nul
if errorlevel 1 set "NEED_INIT=1"
if defined VSCMD_ARG_TGT_ARCH (
  if /I not "%VSCMD_ARG_TGT_ARCH%"=="%REQ_TARGET%" set "NEED_INIT=1"
)

if not defined NEED_INIT exit /b 0

call :find_vsdevcmd
if not defined VSDEVCMD (
  echo [ERROR] cl.exe not found, and VsDevCmd.bat was not located.
  echo Install Visual Studio Build Tools with "MSVC v143 x64/x86 build tools".
  exit /b 1
)

echo [INFO] Initializing MSVC environment for %REQ_TARGET%...
call "!VSDEVCMD!" -arch=%REQ_TARGET% >nul
if errorlevel 1 (
  echo [ERROR] Failed to initialize MSVC environment via:
  echo         !VSDEVCMD!
  exit /b 1
)

where cl >nul 2>nul
if errorlevel 1 (
  echo [ERROR] cl.exe is still unavailable after initializing MSVC environment.
  exit /b 1
)

if defined VSCMD_ARG_TGT_ARCH (
  if /I not "%VSCMD_ARG_TGT_ARCH%"=="%REQ_TARGET%" (
    echo [WARN] Active MSVC target arch is "%VSCMD_ARG_TGT_ARCH%", expected "%REQ_TARGET%".
  )
)

exit /b 0

:find_vsdevcmd
set "VSDEVCMD="
set "VSROOT64=%ProgramFiles%\Microsoft Visual Studio"
set "VSROOT32=%ProgramFiles(x86)%\Microsoft Visual Studio"

if defined VSDEVCMD_PATH if exist "!VSDEVCMD_PATH!" (
  set "VSDEVCMD=!VSDEVCMD_PATH!"
  exit /b 0
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"

if exist "!VSWHERE!" (
  for /f "usebackq delims=" %%I in (`"!VSWHERE!" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
    if exist "%%~I\Common7\Tools\VsDevCmd.bat" (
      set "VSDEVCMD=%%~I\Common7\Tools\VsDevCmd.bat"
      exit /b 0
    )
  )
)

for %%V in (2022 2019) do (
  for %%S in (BuildTools Community Professional Enterprise) do (
    if exist "!VSROOT64!\%%V\%%S\Common7\Tools\VsDevCmd.bat" (
      set "VSDEVCMD=!VSROOT64!\%%V\%%S\Common7\Tools\VsDevCmd.bat"
      exit /b 0
    )
    if exist "!VSROOT32!\%%V\%%S\Common7\Tools\VsDevCmd.bat" (
      set "VSDEVCMD=!VSROOT32!\%%V\%%S\Common7\Tools\VsDevCmd.bat"
      exit /b 0
    )
  )
)

exit /b 1

:error
echo [FAILED] Build aborted.
popd >nul
exit /b 1
