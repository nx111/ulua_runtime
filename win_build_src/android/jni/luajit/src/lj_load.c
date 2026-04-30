/*
** Load and dump code.
** Copyright (C) 2005-2017 Mike Pall. See Copyright Notice in luajit.h
*/

#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#if defined(__ANDROID__)
#include <android/log.h>
#endif

#define lj_load_c
#define LUA_CORE

#include "lua.h"
#include "lauxlib.h"

#include "lj_obj.h"
#include "lj_gc.h"
#include "lj_err.h"
#include "lj_buf.h"
#include "lj_func.h"
#include "lj_frame.h"
#include "lj_vm.h"
#include "lj_lex.h"
#include "lj_bcdump.h"
#include "lj_parse.h"

LUALIB_API char* tolua_convertbytecodeex(const char *buff, int sz, int target_fr2, int *out_sz, int *error_code);
LUALIB_API const char* tolua_getlastbytecodedebug(void);
LUALIB_API const char* tolua_getbytecodeerrorstr(int error_code);

/* -- Load Lua source code and bytecode ----------------------------------- */

static TValue *cpparser(lua_State *L, lua_CFunction dummy, void *ud)
{
  LexState *ls = (LexState *)ud;
  GCproto *pt;
  GCfunc *fn;
  int bc;
  UNUSED(dummy);
  cframe_errfunc(L->cframe) = -1;  /* Inherit error function. */
  bc = lj_lex_setup(L, ls);
  if (ls->mode && !strchr(ls->mode, bc ? 'b' : 't')) {
    setstrV(L, L->top++, lj_err_str(L, LJ_ERR_XMODE));
    lj_err_throw(L, LUA_ERRSYNTAX);
  }
  pt = bc ? lj_bcread(ls) : lj_parse(ls);
  fn = lj_func_newL_empty(L, pt, tabref(L->env));
  /* Don't combine above/below into one statement. */
  setfuncV(L, L->top++, fn);
  return NULL;
}

LUA_API int lua_loadx(lua_State *L, lua_Reader reader, void *data,
		      const char *chunkname, const char *mode)
{
  LexState ls;
  int status;
  ls.rfunc = reader;
  ls.rdata = data;
  ls.chunkarg = chunkname ? chunkname : "?";
  ls.mode = mode;
  lj_buf_init(L, &ls.sb);
  status = lj_vm_cpcall(L, NULL, &ls, cpparser);
  lj_lex_cleanup(L, &ls);
  lj_gc_check(L);
  return status;
}

LUA_API int lua_load(lua_State *L, lua_Reader reader, void *data,
		     const char *chunkname)
{
  return lua_loadx(L, reader, data, chunkname, NULL);
}

typedef struct FileReaderCtx {
  FILE *fp;
  char buf[LUAL_BUFFERSIZE];
} FileReaderCtx;

static const char *reader_file(lua_State *L, void *ud, size_t *size)
{
  FileReaderCtx *ctx = (FileReaderCtx *)ud;
  UNUSED(L);
  if (feof(ctx->fp)) return NULL;
  *size = fread(ctx->buf, 1, sizeof(ctx->buf), ctx->fp);
  return *size > 0 ? ctx->buf : NULL;
}

LUALIB_API int luaL_loadfilex(lua_State *L, const char *filename,
			      const char *mode)
{
  FileReaderCtx ctx;
  int status;
  const char *chunkname;
  if (filename) {
    ctx.fp = fopen(filename, "rb");
    if (ctx.fp == NULL) {
      lua_pushfstring(L, "cannot open %s: %s", filename, strerror(errno));
      return LUA_ERRFILE;
    }
    chunkname = lua_pushfstring(L, "@%s", filename);
  } else {
    ctx.fp = stdin;
    chunkname = "=stdin";
  }
  status = lua_loadx(L, reader_file, &ctx, chunkname, mode);
  if (ferror(ctx.fp)) {
    L->top -= filename ? 2 : 1;
    lua_pushfstring(L, "cannot read %s: %s", chunkname+1, strerror(errno));
    if (filename)
      fclose(ctx.fp);
    return LUA_ERRFILE;
  }
  if (filename) {
    L->top--;
    copyTV(L, L->top-1, L->top);
    fclose(ctx.fp);
  }
  return status;
}

LUALIB_API int luaL_loadfile(lua_State *L, const char *filename)
{
  return luaL_loadfilex(L, filename, NULL);
}

typedef struct StringReaderCtx {
  const char *str;
  size_t size;
} StringReaderCtx;

static const char *reader_string(lua_State *L, void *ud, size_t *size)
{
  StringReaderCtx *ctx = (StringReaderCtx *)ud;
  UNUSED(L);
  if (ctx->size == 0) return NULL;
  *size = ctx->size;
  ctx->size = 0;
  return ctx->str;
}

LUALIB_API int luaL_loadbufferx(lua_State *L, const char *buf, size_t size,
				const char *name, const char *mode)
{
  StringReaderCtx ctx;
  ctx.str = buf;
  ctx.size = size;
  return lua_loadx(L, reader_string, &ctx, name, mode);
}

LUALIB_API int luaL_loadbuffer(lua_State *L, const char *buf, size_t size,
			       const char *name)
{
  int status;
#if LJ_FR2
  static int ulua_preconv_log_budget = 32;
  int trace_chunk = (ulua_preconv_log_budget > 0);
  if (buf != NULL && size > 4 &&
      (uint8_t)buf[0] == BCDUMP_HEAD1 &&
      (uint8_t)buf[1] == BCDUMP_HEAD2 &&
      (uint8_t)buf[2] == BCDUMP_HEAD3) {
    int source_version = (int)(uint8_t)buf[3];
#if defined(__ANDROID__)
    if (trace_chunk) {
      __android_log_print(ANDROID_LOG_INFO, "ulua-bytecode",
        "preconv_probe_v2 name=%s ver=%d head=%02x %02x %02x %02x flag0=%02x size=%d",
        name ? name : "<null>",
        source_version,
        (unsigned int)(uint8_t)buf[0],
        (unsigned int)(uint8_t)buf[1],
        (unsigned int)(uint8_t)buf[2],
        (unsigned int)(uint8_t)buf[3],
        (unsigned int)(uint8_t)buf[4],
        (int)size);
      ulua_preconv_log_budget--;
    }
#endif
    int patched_sz = 0;
    int conv_status = 0;
    char *patched = tolua_convertbytecodeex(buf, (int)size, 1, &patched_sz, &conv_status);
    if (patched != NULL) {
#if defined(__ANDROID__)
      if (trace_chunk) {
        __android_log_print(ANDROID_LOG_INFO, "ulua-bytecode",
          "preconv_ok_v2 name=%s src=%d out=%d ver=%d out_head=%02x %02x %02x %02x out_flag0=%02x",
          name ? name : "<null>",
          (int)size,
          patched_sz,
          source_version,
          (unsigned int)(patched_sz > 0 ? (uint8_t)patched[0] : 0),
          (unsigned int)(patched_sz > 1 ? (uint8_t)patched[1] : 0),
          (unsigned int)(patched_sz > 2 ? (uint8_t)patched[2] : 0),
          (unsigned int)(patched_sz > 3 ? (uint8_t)patched[3] : 0),
          (unsigned int)(patched_sz > 4 ? (uint8_t)patched[4] : 0));
      }
#endif
      status = luaL_loadbufferx(L, patched, (size_t)patched_sz, name, NULL);
      free(patched);
      return status;
    }
    /* On FR2 runtime, v1 bytecode must never continue with raw load if conversion failed.
       Raw fallback can appear to load but execute with wrong semantics. */
    if (source_version == 1) {
      const char *detail = tolua_getlastbytecodedebug();
      const char *ename = tolua_getbytecodeerrorstr(conv_status);
#if defined(__ANDROID__)
      if (trace_chunk) {
        __android_log_print(ANDROID_LOG_ERROR, "ulua-bytecode",
          "preconv_fail name=%s err=%s detail=%s",
          name,
          ename ? ename : "conv_error",
          (detail && detail[0]) ? detail : "conversion failed");
      }
#endif
      if (detail && detail[0]) {
        lua_pushfstring(L, "[tolua-bytecode] %s (%s)", detail, ename ? ename : "conv_error");
      } else if (ename) {
        lua_pushfstring(L, "[tolua-bytecode] conversion failed (%s)", ename);
      } else {
        lua_pushstring(L, "[tolua-bytecode] conversion failed");
      }
      return LUA_ERRSYNTAX;
    }
  }
#endif
  status = luaL_loadbufferx(L, buf, size, name, NULL);
#if LJ_FR2
  if (status == LUA_ERRSYNTAX && buf != NULL && size > 4 &&
      (uint8_t)buf[0] == BCDUMP_HEAD1 &&
      (uint8_t)buf[1] == BCDUMP_HEAD2 &&
      (uint8_t)buf[2] == BCDUMP_HEAD3) {
    int patched_sz = 0;
    int conv_status = 0;
    char *patched = tolua_convertbytecodeex(buf, (int)size, 1, &patched_sz, &conv_status);
    if (patched != NULL) {
      lua_pop(L, 1);  /* Drop previous incompatible-bytecode error. */
      status = luaL_loadbufferx(L, patched, (size_t)patched_sz, name, NULL);
      free(patched);
    } else if (conv_status != 0) {
      const char *orig = lua_tostring(L, -1);
      const char *detail = tolua_getlastbytecodedebug();
      const char *ename = tolua_getbytecodeerrorstr(conv_status);
      if (detail && detail[0]) {
	lua_pushfstring(L, "%s\n[tolua-bytecode] %s (%s)", orig ? orig : "", detail, ename ? ename : "conv_error");
	lua_replace(L, -2);
      } else if (ename) {
	lua_pushfstring(L, "%s\n[tolua-bytecode] conversion failed (%s)", orig ? orig : "", ename);
	lua_replace(L, -2);
      }
    }
  }
#endif
  return status;
}

LUALIB_API int luaL_loadstring(lua_State *L, const char *s)
{
  return luaL_loadbuffer(L, s, strlen(s), s);
}

/* -- Dump bytecode ------------------------------------------------------- */

LUA_API int lua_dump(lua_State *L, lua_Writer writer, void *data)
{
  cTValue *o = L->top-1;
  api_check(L, L->top > L->base);
  if (tvisfunc(o) && isluafunc(funcV(o)))
    return lj_bcwrite(L, funcproto(funcV(o)), writer, data, 0);
  else
    return 1;
}
