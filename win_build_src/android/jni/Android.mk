LOCAL_PATH := $(call my-dir)
ULUA_ROOT := $(LOCAL_PATH)/../..

include $(CLEAR_VARS)
LOCAL_MODULE := libluajit
LOCAL_SRC_FILES := libluajit.a
include $(PREBUILT_STATIC_LIBRARY)

include $(CLEAR_VARS)
LOCAL_FORCE_STATIC_EXECUTABLE := true
LOCAL_MODULE := ulua
LOCAL_C_INCLUDES := $(ULUA_ROOT)/luajit/src
LOCAL_C_INCLUDES += $(ULUA_ROOT)/pbc
LOCAL_C_INCLUDES += $(ULUA_ROOT)/cjson
LOCAL_C_INCLUDES += $(ULUA_ROOT)/luasocket/src
LOCAL_C_INCLUDES += $(ULUA_ROOT)

LOCAL_CPPFLAGS := -O3 -ffast-math
LOCAL_CFLAGS += -Dlua_tolstring=lua_tolstring_internal -Dlua_pcall=lua_pcall_internal -DluaL_reg=luaL_Reg
LOCAL_LDLIBS += -llog
LOCAL_SRC_FILES := ../../lua_wrap.c \
				   ../../pb.c \
				   ../../lpeg.c \
				   ../../sproto.c \
				   ../../lsproto.c \
				   ../../pbc/src/alloc.c \
				   ../../pbc/src/array.c \
				   ../../pbc/src/bootstrap.c \
				   ../../pbc/src/context.c \
				   ../../pbc/src/decode.c \
				   ../../pbc/src/map.c \
				   ../../pbc/src/pattern.c \
				   ../../pbc/src/proto.c \
				   ../../pbc/src/register.c \
				   ../../pbc/src/rmessage.c \
				   ../../pbc/src/stringpool.c \
				   ../../pbc/src/varint.c \
				   ../../pbc/src/wmessage.c \
				   ../../pbc/binding/lua/pbc-lua.c \
				   ../../cjson/fpconv.c \
				   ../../cjson/strbuf.c \
				   ../../cjson/lua_cjson.c \
				   ../../luasocket/src/auxiliar.c \
				   ../../luasocket/src/buffer.c \
				   ../../luasocket/src/except.c \
				   ../../luasocket/src/inet.c \
				   ../../luasocket/src/io.c \
				   ../../luasocket/src/luasocket.c \
				   ../../luasocket/src/mime.c \
				   ../../luasocket/src/options.c \
				   ../../luasocket/src/select.c \
				   ../../luasocket/src/tcp.c \
				   ../../luasocket/src/timeout.c \
				   ../../luasocket/src/udp.c \
				   ../../luasocket/src/unix.c \
				   ../../luasocket/src/usocket.c

LOCAL_WHOLE_STATIC_LIBRARIES += libluajit
include $(BUILD_SHARED_LIBRARY)
