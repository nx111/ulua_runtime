#!/bin/bash
#
# Linux 32-bit/64-bit

# Copyright (C) polynation games ltd - All Rights Reserved
# Unauthorized copying of this file, via any medium is strictly prohibited
# Proprietary and confidential
# Written by Christopher Redden, December 2013

set -euo pipefail

API_COMPAT_DEF="-Dlua_tolstring=lua_tolstring_internal -Dlua_pcall=lua_pcall_internal"
LUAJIT_XCFLAGS="$API_COMPAT_DEF"
ULUA_CFLAGS="$API_COMPAT_DEF"

usage() {
  cat <<'EOF'
Usage: build-linux64.sh [-fr2]

Options:
  -fr2    Enable LuaJIT GC64 build flags.
EOF
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

if [ "$ENABLE_FR2" -eq 1 ]; then
  LUAJIT_XCFLAGS="$LUAJIT_XCFLAGS -DLUAJIT_ENABLE_GC64"
  ULUA_CFLAGS="$ULUA_CFLAGS -DLUAJIT_ENABLE_GC64"
  echo "[INFO] LuaJIT GC64: enabled (-fr2)"
fi

# 64 Bit Version
mkdir -p linux/x86_64
mkdir -p Plugins/x86_64

cd luajit
make clean

make BUILDMODE=static CC="gcc -fPIC -m64" XCFLAGS="$LUAJIT_XCFLAGS"
cp src/libluajit.a ../linux/x86_64/libluajit.a

cd ../pbc/
make clean
make lib BUILDMODE=static CC="gcc -fPIC -m64" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libpbc.a ../linux/x86_64/libpbc.a

cd ../cjson/
make clean
make lib BUILDMODE=static CC="gcc -fPIC -m64" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libcjson.a ../linux/x86_64/libcjson.a

cd ..
gcc -fPIC \
	lua_wrap.c \
	pb_win.c \
	lpeg.c \
	sproto.c \
	lsproto.c \
	pbc/binding/lua/pbc-lua.c \
	cjson/lua_cjson.c \
	-o Plugins/x86_64/libulua.so -shared \
	-I./ \
	-Iluajit/src \
	-Ipbc \
	-Icjson \
	$ULUA_CFLAGS \
	-Wl,--whole-archive \
	linux/x86_64/libluajit.a \
	linux/x86_64/libpbc.a \
	linux/x86_64/libcjson.a \
	-Wl,--no-whole-archive -static-libgcc -static-libstdc++
