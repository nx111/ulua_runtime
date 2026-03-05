#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ABI="armeabi-v7a"
API_COMPAT_DEF="-Dlua_tolstring=lua_tolstring_internal -Dlua_pcall=lua_pcall_internal"

NDK_ROOT="${ANDROID_NDK_ROOT:-${ANDROID_NDK_HOME:-${NDK:-}}}"
if [ -z "${NDK_ROOT}" ]; then
  if [ -d "D:/Mobile/sdk/ndk/android-ndk-r10e" ]; then
    NDK_ROOT="D:/Mobile/sdk/ndk/android-ndk-r10e"
  elif [ -d "D:/adt-bundle-windows/ndk-r8d" ]; then
    NDK_ROOT="D:/adt-bundle-windows/ndk-r8d"
  else
    echo "ERROR: NDK path not found. Set ANDROID_NDK_ROOT (or ANDROID_NDK_HOME/NDK)." >&2
    exit 1
  fi
fi
NDK_ROOT="${NDK_ROOT%\"}"
NDK_ROOT="${NDK_ROOT#\"}"
NDK_ROOT="${NDK_ROOT%\'}"
NDK_ROOT="${NDK_ROOT#\'}"
if [[ "$NDK_ROOT" == [A-Za-z]:\\* ]]; then
  NDK_ROOT="${NDK_ROOT//\\//}"
fi

MAKE_CMD="${MAKE:-}"
if [ -z "$MAKE_CMD" ]; then
  if command -v make >/dev/null 2>&1; then
    MAKE_CMD="make"
  elif command -v mingw32-make >/dev/null 2>&1; then
    MAKE_CMD="mingw32-make"
  elif command -v gmake >/dev/null 2>&1; then
    MAKE_CMD="gmake"
  else
    echo "ERROR: make not found. Install make (or mingw32-make/gmake), or set MAKE=/path/to/make." >&2
    exit 1
  fi
fi

NDK_BUILD=""
if [ -f "$NDK_ROOT/ndk-build.cmd" ] && (command -v cmd.exe >/dev/null 2>&1 || command -v cmd >/dev/null 2>&1); then
  NDK_BUILD="$NDK_ROOT/ndk-build.cmd"
elif [ -x "$NDK_ROOT/ndk-build" ]; then
  NDK_BUILD="$NDK_ROOT/ndk-build"
elif [ -f "$NDK_ROOT/ndk-build" ]; then
  NDK_BUILD="$NDK_ROOT/ndk-build"
elif [ -f "$NDK_ROOT/ndk-build.cmd" ]; then
  NDK_BUILD="$NDK_ROOT/ndk-build.cmd"
elif command -v ndk-build >/dev/null 2>&1; then
  NDK_BUILD="$(command -v ndk-build)"
else
  echo "ERROR: ndk-build not found in NDK_ROOT or PATH." >&2
  exit 1
fi

run_ndk_build() {
  if [[ "$NDK_BUILD" == *.cmd ]]; then
    local cmd_runner=""
    if command -v cmd.exe >/dev/null 2>&1; then
      cmd_runner="cmd.exe"
    elif command -v cmd >/dev/null 2>&1; then
      cmd_runner="cmd"
    else
      echo "ERROR: ndk-build.cmd found, but cmd/cmd.exe is not available in PATH." >&2
      exit 1
    fi

    local ndk_cmd_path="$NDK_BUILD"
    if [[ "$ndk_cmd_path" =~ ^/([a-zA-Z])/(.*)$ ]]; then
      ndk_cmd_path="${BASH_REMATCH[1]}:/${BASH_REMATCH[2]}"
    fi
    ndk_cmd_path="${ndk_cmd_path//\//\\}"

    if [ "$#" -gt 0 ]; then
      MSYS2_ARG_CONV_EXCL='*' "$cmd_runner" /d /c call "$ndk_cmd_path" "$@"
    else
      MSYS2_ARG_CONV_EXCL='*' "$cmd_runner" /d /c call "$ndk_cmd_path"
    fi
  else
    if [ -x "$NDK_BUILD" ]; then
      "$NDK_BUILD" "$@"
    else
      bash "$NDK_BUILD" "$@"
    fi
  fi
}

ARM_GCC="$(find "$NDK_ROOT/toolchains" -type f \( -name "arm-linux-androideabi-gcc" -o -name "arm-linux-androideabi-gcc.exe" \) 2>/dev/null | head -n 1 || true)"
if [ -z "$ARM_GCC" ]; then
  echo "ERROR: arm-linux-androideabi-gcc(.exe) not found. This script needs legacy NDK GCC toolchain (r8d/r10e)." >&2
  exit 1
fi
CROSS_PREFIX="${ARM_GCC%.exe}"
CROSS_PREFIX="${CROSS_PREFIX%gcc}"

SYSROOT="$NDK_ROOT/platforms/android-14/arch-arm"
if [ ! -d "$SYSROOT" ]; then
  SYSROOT="$(find "$NDK_ROOT/platforms" -maxdepth 2 -type d -path "*/arch-arm" 2>/dev/null | sort | head -n 1 || true)"
fi
if [ -z "$SYSROOT" ] || [ ! -d "$SYSROOT" ]; then
  echo "ERROR: Android ARM sysroot not found under $NDK_ROOT/platforms." >&2
  exit 1
fi

HOST_CC_CMD="${HOST_CC:-gcc -ffast-math -O3}"

rm -f "$SCRIPT_DIR/libluajit.a"
echo "APP_ABI := $ABI" > "$SCRIPT_DIR/Application.mk"

pushd "$SCRIPT_DIR/luajit/src" >/dev/null
"$MAKE_CMD" clean
MAKE_VARS=(
  "HOST_CC=$HOST_CC_CMD"
  "CROSS=$CROSS_PREFIX"
  "TARGET_SYS=Linux"
  "XCFLAGS=$API_COMPAT_DEF"
  "TARGET_FLAGS=--sysroot $SYSROOT -march=armv7-a -mfloat-abi=softfp -Wl,--fix-cortex-a8"
)
echo "LuaJIT SYSROOT: $SYSROOT"
"$MAKE_CMD" "${MAKE_VARS[@]}"
cp -f ./libluajit.a ../../libluajit.a
popd >/dev/null

pushd "$SCRIPT_DIR/.." >/dev/null
run_ndk_build clean
run_ndk_build
popd >/dev/null

SO_PATH_LIBS="$SCRIPT_DIR/../libs/$ABI/libulua.so"
SO_PATH_OBJ="$SCRIPT_DIR/../obj/local/$ABI/libulua.so"
if [ -f "$SO_PATH_LIBS" ]; then
  echo "Build done: $SO_PATH_LIBS"
elif [ -f "$SO_PATH_OBJ" ]; then
  mkdir -p "$SCRIPT_DIR/../libs/$ABI"
  cp -f "$SO_PATH_OBJ" "$SO_PATH_LIBS"
  echo "Build done: $SO_PATH_LIBS"
else
  echo "ERROR: libulua.so not found after ndk-build." >&2
  echo "Checked:" >&2
  echo "  $SO_PATH_LIBS" >&2
  echo "  $SO_PATH_OBJ" >&2
  ls -la "$SCRIPT_DIR/../libs" 2>/dev/null || true
  ls -la "$SCRIPT_DIR/../obj/local" 2>/dev/null || true
  exit 1
fi
