#!/bin/bash
# Windows 64-bit cross compile from WSL

set -euo pipefail

API_COMPAT_DEF="-Dlua_tolstring=lua_tolstring_internal"

mkdir -p window/x86_64
mkdir -p Plugins/x86_64

cd luajit
make clean
make HOST_CC="gcc" CROSS=x86_64-w64-mingw32- TARGET_SYS=Windows BUILDMODE=static XCFLAGS="$API_COMPAT_DEF"
cp src/libluajit.a ../window/x86_64/libluajit.a

cd ../pbc/
make clean
make BUILDMODE=static CC="x86_64-w64-mingw32-gcc" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libpbc.a ../window/x86_64/libpbc.a

cd ../cjson/
make clean
make BUILDMODE=static CC="x86_64-w64-mingw32-gcc" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libcjson.a ../window/x86_64/libcjson.a

cd ..

x86_64-w64-mingw32-gcc lua_wrap.c \
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
	$API_COMPAT_DEF \
	-Wl,--whole-archive \
	window/x86_64/libluajit.a \
	window/x86_64/libpbc.a \
	window/x86_64/libcjson.a \
	-O3 -DluaL_reg=luaL_Reg -Wl,--no-whole-archive -lwsock32 -static-libgcc -static-libstdc++
