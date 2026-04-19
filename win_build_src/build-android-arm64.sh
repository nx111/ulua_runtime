#!/bin/bash
#
# Android ARM64 (arm64-v8a) + 16KB page alignment

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ANDROID_DIR="$ROOT_DIR/android"
JNI_DIR="$ANDROID_DIR/jni"
LUAJIT_SRC_DIR="$JNI_DIR/luajit/src"
ABI="arm64-v8a"
API_LEVEL="${ANDROID_API_LEVEL:-21}"
API_COMPAT_DEF="-Dlua_tolstring=lua_tolstring_internal -Dlua_pcall=lua_pcall_internal -DluaL_reg=luaL_Reg"
PAGE_ALIGN_LDFLAGS="-Wl,-z,max-page-size=16384 -Wl,-z,common-page-size=16384"

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
    if [[ "$ndk_cmd_path" =~ ^/mnt/([a-zA-Z])/(.*)$ ]]; then
      ndk_cmd_path="${BASH_REMATCH[1]}:/${BASH_REMATCH[2]}"
    elif [[ "$ndk_cmd_path" =~ ^/([a-zA-Z])/(.*)$ ]]; then
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

to_win_path_if_mnt() {
  local p="$1"
  if [[ "$p" =~ ^/mnt/([a-zA-Z])/(.*)$ ]]; then
    echo "${BASH_REMATCH[1]}:/${BASH_REMATCH[2]}"
  else
    echo "$p"
  fi
}

path_exists_cross_env() {
  local p="$1"
  if [ -e "$p" ]; then
    return 0
  fi
  if [[ "$p" =~ ^([a-zA-Z]):/(.*)$ ]]; then
    local drive="${BASH_REMATCH[1],,}"
    local rest="${BASH_REMATCH[2]}"
    if [ -e "/mnt/$drive/$rest" ]; then
      return 0
    fi
  fi
  return 1
}

if ! grep -R -E -n "LJ_TARGET_ARM64|arm64|aarch64|vm_arm64\\.dasc" "$LUAJIT_SRC_DIR" >/dev/null 2>&1; then
  echo "ERROR: Current LuaJIT source does not include ARM64 support." >&2
  echo "Root cause: this repo is LuaJIT 2.0.4, which cannot build arm64-v8a." >&2
  echo "Please upgrade jni/luajit to LuaJIT 2.1 branch (or another arm64-capable fork), then rerun." >&2
  exit 1
fi

LLVM_PREBUILT_DIR="$(find "$NDK_ROOT/toolchains/llvm/prebuilt" -maxdepth 1 -mindepth 1 -type d 2>/dev/null | head -n 1 || true)"
if [ -z "$LLVM_PREBUILT_DIR" ]; then
  echo "ERROR: LLVM prebuilt toolchain not found under $NDK_ROOT/toolchains/llvm/prebuilt." >&2
  echo "Root cause: this arm64 script requires modern NDK (clang toolchain)." >&2
  exit 1
fi

TOOLCHAIN_BIN="$LLVM_PREBUILT_DIR/bin"
TARGET_TRIPLE="aarch64-linux-android${API_LEVEL}"
TARGET_CC="$TOOLCHAIN_BIN/${TARGET_TRIPLE}-clang"
TARGET_AR="$TOOLCHAIN_BIN/llvm-ar"
TARGET_STRIP="$TOOLCHAIN_BIN/llvm-strip"

if [[ "$MAKE_CMD" == *.exe ]]; then
  TARGET_CC="${TARGET_CC}.cmd"
fi

if [ ! -x "$TARGET_AR" ] && [ -x "${TARGET_AR}.exe" ]; then
  TARGET_AR="${TARGET_AR}.exe"
fi
if [ ! -x "$TARGET_STRIP" ] && [ -x "${TARGET_STRIP}.exe" ]; then
  TARGET_STRIP="${TARGET_STRIP}.exe"
fi

if [[ "$MAKE_CMD" == *.exe ]]; then
  TARGET_CC="$(to_win_path_if_mnt "$TARGET_CC")"
  TARGET_AR="$(to_win_path_if_mnt "$TARGET_AR")"
  TARGET_STRIP="$(to_win_path_if_mnt "$TARGET_STRIP")"
fi

if ! path_exists_cross_env "$TARGET_CC"; then
  echo "ERROR: $TARGET_CC not found." >&2
  exit 1
fi
if ! path_exists_cross_env "$TARGET_AR"; then
  echo "ERROR: $TARGET_AR not found." >&2
  exit 1
fi
if ! path_exists_cross_env "$TARGET_STRIP"; then
  echo "ERROR: $TARGET_STRIP not found." >&2
  exit 1
fi

cat > "$JNI_DIR/Application.mk" <<EOF
APP_ABI := $ABI
APP_PLATFORM := android-$API_LEVEL
APP_LDFLAGS += $PAGE_ALIGN_LDFLAGS
EOF
rm -f "$JNI_DIR/libluajit.a"

if [ -n "${HOST_CC:-}" ]; then
  HOST_CC_CMD="${HOST_CC}"
elif [[ "$MAKE_CMD" == *.exe ]]; then
  HOST_CC_CMD="$(to_win_path_if_mnt "$TOOLCHAIN_BIN/clang.exe") -O2"
else
  HOST_CC_CMD="gcc -ffast-math -O3"
fi
pushd "$LUAJIT_SRC_DIR" >/dev/null
MAKE_VARS=(
  "HOST_CC=$HOST_CC_CMD"
  "CC=$TARGET_CC"
  "TARGET_SYS=Linux"
  "TARGET_CC=$TARGET_CC"
  "TARGET_LD=$TARGET_CC"
  "TARGET_AR=$TARGET_AR rcus"
  "TARGET_STRIP=$TARGET_STRIP"
  "XCFLAGS=$API_COMPAT_DEF"
)
echo "LuaJIT TARGET_CC: $TARGET_CC"
"$MAKE_CMD" "${MAKE_VARS[@]}" libluajit.a
cp -f libluajit.a ../../libluajit.a
popd >/dev/null

pushd "$ANDROID_DIR" >/dev/null
run_ndk_build clean
run_ndk_build
popd >/dev/null

SO_PATH_LIBS="$ANDROID_DIR/libs/$ABI/libulua.so"
SO_PATH_OBJ="$ANDROID_DIR/obj/local/$ABI/libulua.so"
if [ ! -f "$SO_PATH_LIBS" ] && [ -f "$SO_PATH_OBJ" ]; then
  mkdir -p "$ANDROID_DIR/libs/$ABI"
  cp -f "$SO_PATH_OBJ" "$SO_PATH_LIBS"
fi
if [ ! -f "$SO_PATH_LIBS" ]; then
  echo "ERROR: libulua.so not found after ndk-build." >&2
  echo "Checked:" >&2
  echo "  $SO_PATH_LIBS" >&2
  echo "  $SO_PATH_OBJ" >&2
  ls -la "$ANDROID_DIR/libs" 2>/dev/null || true
  ls -la "$ANDROID_DIR/obj/local" 2>/dev/null || true
  exit 1
fi

mkdir -p "$ROOT_DIR/Plugins/Android/libs/$ABI"
cp -f "$SO_PATH_LIBS" "$ROOT_DIR/Plugins/Android/libs/$ABI/libulua.so"

echo "Build done: $ROOT_DIR/Plugins/Android/libs/$ABI/libulua.so"
