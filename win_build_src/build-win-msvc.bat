@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Failed to enter script directory: %SCRIPT_DIR%
  exit /b 1
)

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=x64"

if /I not "%TARGET%"=="x64" if /I not "%TARGET%"=="x86" (
  echo Usage: %~nx0 [x64^|x86]
  echo Example: %~nx0 x64
  goto :error
)

where cl >nul 2>nul
if errorlevel 1 (
  echo [ERROR] cl.exe not found.
  echo Open "x64 Native Tools Command Prompt for VS 2022" ^(or x86^) first.
  goto :error
)

if /I "%TARGET%"=="x64" (
  set "OUT_SUBDIR=x86_64"
  set "MACHINE=X64"
) else (
  set "OUT_SUBDIR=x86"
  set "MACHINE=X86"
)

if defined VSCMD_ARG_TGT_ARCH (
  if /I not "%VSCMD_ARG_TGT_ARCH%"=="%TARGET%" (
    echo [WARN] Requested target is "%TARGET%", but active MSVC env is "%VSCMD_ARG_TGT_ARCH%".
    echo [WARN] Continue anyway.
  )
)

set "API_COMPAT_DEF=/Dlua_tolstring=lua_tolstring_internal /Dlua_pcall=lua_pcall_internal"
set "COMMON_CFLAGS=/nologo /c /O2 /W3 /MT /D_CRT_SECURE_NO_WARNINGS %API_COMPAT_DEF%"
set "COMMON_INCLUDES=/I. /Iluajit\src /Ipbc /Ipbc\src /Icjson /Iluasocket\src"

set "BUILD_DIR=build\msvc\%TARGET%"
set "OBJ_PBC=%BUILD_DIR%\obj\pbc"
set "OBJ_CJSON=%BUILD_DIR%\obj\cjson"
set "OBJ_PLUGIN=%BUILD_DIR%\obj\plugin"
set "LUAJIT_LIB=luajit\src\lua51.lib"
set "PBC_LIB=window\%OUT_SUBDIR%\libpbc.lib"
set "CJSON_LIB=window\%OUT_SUBDIR%\libcjson.lib"
set "OUT_DLL=Plugins\%OUT_SUBDIR%\ulua.dll"

if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
mkdir "%OBJ_PBC%" "%OBJ_CJSON%" "%OBJ_PLUGIN%" >nul 2>nul
mkdir "window\%OUT_SUBDIR%" >nul 2>nul
mkdir "Plugins\%OUT_SUBDIR%" >nul 2>nul

echo [1/4] Building LuaJIT static library...
pushd "luajit\src" >nul
set "OLD_CL=%CL%"
set "CL=%API_COMPAT_DEF% %CL%"
call msvcbuild.bat static
if errorlevel 1 (
  set "CL=%OLD_CL%"
  popd >nul
  goto :error
)
set "CL=%OLD_CL%"
popd >nul

if not exist "%LUAJIT_LIB%" (
  echo [ERROR] Missing LuaJIT static library: %LUAJIT_LIB%
  goto :error
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
  cl %COMMON_CFLAGS% /FI"msvc_stdbool.h" %COMMON_INCLUDES% /Fo"%OBJ_PLUGIN%\%%~nF.obj" "%%F"
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

:error
echo [FAILED] Build aborted.
popd >nul
exit /b 1
