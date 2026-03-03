#!/bin/bash
#
# Windows 32-bit/64-bit

# Copyright (C) polynation games ltd - All Rights Reserved
# Unauthorized copying of this file, via any medium is strictly prohibited
# Proprietary and confidential
# Written by Christopher Redden, December 2013

API_COMPAT_DEF="-Dlua_tolstring=lua_tolstring_internal -Dlua_pcall=lua_pcall_internal"

# 32 Bit Version
mkdir -p window/x86
mkdir -p Plugins/x86

cd luajit
mingw32-make clean

mingw32-make BUILDMODE=static TARGET_SYS=Windows CC="gcc -m32" XCFLAGS="$API_COMPAT_DEF"
cp src/libluajit.a ../window/x86/libluajit.a

cd ../pbc/
mingw32-make clean
mingw32-make lib BUILDMODE=static CC="gcc -m32" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libpbc.a ../window/x86/libpbc.a

cd ../cjson/
mingw32-make clean
mingw32-make lib BUILDMODE=static CC="gcc -m32" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libcjson.a ../window/x86/libcjson.a

cd ..

gcc lua_wrap.c \
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
	-o Plugins/x86/ulua.dll -m32 -shared \
	-I./ \
	-Iluajit/src \
	-Ipbc \
	-Icjson \
	-Iluasocket/src \
	$API_COMPAT_DEF \
	-Wl,--whole-archive \
	window/x86/libluajit.a \
	window/x86/libpbc.a \
	window/x86/libcjson.a \
	-O3 -Wl,--no-whole-archive -lwsock32 -static-libgcc -static-libstdc++
