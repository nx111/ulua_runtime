#!/bin/bash
#
# Linux 32-bit/64-bit

# Copyright (C) polynation games ltd - All Rights Reserved
# Unauthorized copying of this file, via any medium is strictly prohibited
# Proprietary and confidential
# Written by Christopher Redden, December 2013

set -euo pipefail

API_COMPAT_DEF="-Dlua_tolstring=lua_tolstring_internal -Dlua_pcall=lua_pcall_internal"

# 32 Bit Version
mkdir -p linux/x86
mkdir -p Plugins/x86

cd luajit
make clean

make BUILDMODE=static CC="gcc -fPIC -m32" XCFLAGS="$API_COMPAT_DEF"
cp src/libluajit.a ../linux/x86/libluajit.a

cd ../pbc/
make clean
make lib BUILDMODE=static CC="gcc -fPIC -m32" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libpbc.a ../linux/x86/libpbc.a

cd ../cjson/
make clean
make lib BUILDMODE=static CC="gcc -fPIC -m32" CFLAGS="-O2 -fPIC -Wall $API_COMPAT_DEF"
cp build/libcjson.a ../linux/x86/libcjson.a

cd ..
gcc -fPIC \
	lua_wrap.c \
	pb_win.c \
	lpeg.c \
	sproto.c \
	lsproto.c \
	pbc/binding/lua/pbc-lua.c \
	cjson/lua_cjson.c \
	-o Plugins/x86/libulua.so -shared -m32 \
	-I./ \
	-Iluajit/src \
	-Ipbc \
	-Icjson \
	$API_COMPAT_DEF \
	-Wl,--whole-archive \
	linux/x86/libluajit.a \
	linux/x86/libpbc.a \
	linux/x86/libcjson.a \
	-Wl,--no-whole-archive -static-libgcc -static-libstdc++
