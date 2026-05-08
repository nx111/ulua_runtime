#!/bin/bash
#
# Windows 64-bit build for MinGW or Linux/WSL cross toolchains.

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: build-win64.sh [-fr2]

Options:
  -fr2    Enable LuaJIT GC64 build flags.
EOF
}

pick_make_cmd() {
  local build_mode="$1"
  if [ -n "${MAKE:-}" ]; then
    echo "$MAKE"
  elif [ "$build_mode" = "mingw" ] && command -v mingw32-make >/dev/null 2>&1; then
    echo "mingw32-make"
  elif command -v make >/dev/null 2>&1; then
    echo "make"
  elif command -v gmake >/dev/null 2>&1; then
    echo "gmake"
  else
    echo ""
  fi
}

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "ERROR: required command not found: $1" >&2
    exit 1
  fi
}

ENABLE_FR2=0
for arg in "$@"; do
  case "$arg" in
    -fr2)
      ENABLE_FR2=1
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: unknown argument: $arg" >&2
      usage >&2
      exit 1
      ;;
  esac
done

API_COMPAT_DEF="-Dlua_tolstring=lua_tolstring_internal -Dlua_pcall=lua_pcall_internal"
WIN64_LUAJIT_XCFLAGS="${WIN64_LUAJIT_XCFLAGS:-}"
if [ "$ENABLE_FR2" -eq 1 ]; then
  WIN64_LUAJIT_XCFLAGS="${WIN64_LUAJIT_XCFLAGS:+$WIN64_LUAJIT_XCFLAGS }-DLUAJIT_ENABLE_GC64"
fi
WIN64_LUAJIT_MAKE_XCFLAGS="$API_COMPAT_DEF${WIN64_LUAJIT_XCFLAGS:+ $WIN64_LUAJIT_XCFLAGS}"
WIN64_PLUGIN_CFLAGS="$API_COMPAT_DEF -DluaL_reg=luaL_Reg${WIN64_LUAJIT_XCFLAGS:+ $WIN64_LUAJIT_XCFLAGS}"

UNAME_S="$(uname -s)"

case "$UNAME_S" in
  MINGW*|MSYS*|CYGWIN*)
    BUILD_MODE="mingw"
    TARGET_CC="${CC:-gcc}"
    LUAJIT_MAKE_ARGS=(
      "BUILDMODE=static"
      "CC=$TARGET_CC -m64"
      "XCFLAGS=$WIN64_LUAJIT_MAKE_XCFLAGS"
    )
    PBC_CC="$TARGET_CC -m64"
    CJSON_CC="$TARGET_CC -m64"
    ;;
  *)
    BUILD_MODE="cross"
    TARGET_PREFIX="${TARGET_PREFIX:-x86_64-w64-mingw32-}"
    TARGET_CC="${TARGET_CC:-${TARGET_PREFIX}gcc}"
    HOST_CC_CMD="${HOST_CC:-gcc}"
    HOST_CC_BIN="${HOST_CC_CMD%% *}"
    require_cmd "$TARGET_CC"
    require_cmd "$HOST_CC_BIN"
    LUAJIT_MAKE_ARGS=(
      "HOST_CC=$HOST_CC_CMD"
      "CROSS=$TARGET_PREFIX"
      "TARGET_SYS=Windows"
      "BUILDMODE=static"
      "XCFLAGS=$WIN64_LUAJIT_MAKE_XCFLAGS"
    )
    PBC_CC="$TARGET_CC"
    CJSON_CC="$TARGET_CC"
    ;;
esac

MAKE_CMD="$(pick_make_cmd "$BUILD_MODE")"
if [ -z "$MAKE_CMD" ]; then
  echo "ERROR: make command not found. Install make or set MAKE=/path/to/make." >&2
  exit 1
fi

mkdir -p window/x86_64
mkdir -p Plugins/x86_64

echo "[INFO] Build mode: $BUILD_MODE"
if [ "$ENABLE_FR2" -eq 1 ]; then
  echo "[INFO] LuaJIT GC64: enabled (-fr2)"
fi

pushd luajit/src >/dev/null
"$MAKE_CMD" clean
"$MAKE_CMD" "${LUAJIT_MAKE_ARGS[@]}" libluajit.a
cp libluajit.a ../../window/x86_64/libluajit.a
popd >/dev/null

pushd pbc >/dev/null
"$MAKE_CMD" clean
"$MAKE_CMD" lib BUILDMODE=static CC="$PBC_CC" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libpbc.a ../window/x86_64/libpbc.a
popd >/dev/null

pushd cjson >/dev/null
"$MAKE_CMD" clean
"$MAKE_CMD" lib BUILDMODE=static CC="$CJSON_CC" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libcjson.a ../window/x86_64/libcjson.a
popd >/dev/null

# shellcheck disable=SC2086
"$TARGET_CC" lua_wrap.c \
  pb_win.c \
  lpeg.c \
  sproto.c \
  lsproto.c \
  luasocket/src/luasocket.c \
  luasocket/src/timeout.c \
  luasocket/src/buffer.c \
  luasocket/src/io.c \
  luasocket/src/auxiliar.c \
  luasocket/src/options.c \
  luasocket/src/inet.c \
  luasocket/src/tcp.c \
  luasocket/src/udp.c \
  luasocket/src/except.c \
  luasocket/src/select.c \
  luasocket/src/wsocket.c \
  pbc/binding/lua/pbc-lua.c \
  cjson/lua_cjson.c \
  -o Plugins/x86_64/ulua.dll -m64 -shared \
  -I./ \
  -Iluajit/src \
  -Ipbc \
  -Icjson \
  -Iluasocket/src \
  $WIN64_PLUGIN_CFLAGS \
  -Wl,--whole-archive \
  window/x86_64/libluajit.a \
  window/x86_64/libpbc.a \
  window/x86_64/libcjson.a \
  -O3 -Wl,--no-whole-archive -lwsock32 -static-libgcc -static-libstdc++
