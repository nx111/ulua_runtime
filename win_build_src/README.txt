uLua source code comes with no support guarantees and is intended for advanced users.

The uLua source code and build scripts are proprietary and confidential.
Unauthorized copying, via any medium, is strictly prohibited.

LuaJIT
- uLua in this repository is integrated against LuaJIT 2.1.0-beta3.
- Upstream LuaJIT can be obtained from: http://luajit.org/
- Win32/Win64 now share the same `luajit` source tree.
- `build-win64.sh -fr2` / `build-win64-wsl.sh -fr2` / `build-win-msvc.bat x64 -fr2` enable `LUAJIT_ENABLE_GC64`.

Build Overview
- Place LuaJIT source in `<source root>/luajit` (or the path expected by scripts).
- Run platform-specific build scripts.
- Copy generated libraries to the Unity plugin folders.

Windows Tooling (Recommended)
- Shell: Git Bash (`C:\Program Files\Git\bin\bash.exe`)
- Make: `make` (or `mingw32-make`)
- Compiler toolchain for Windows native builds: MinGW
- Android NDK for legacy GCC-based Android build: `android-ndk-r10e` (or compatible legacy NDK)

Important: Do Not Mix Shell Environments
- Do not mix `PowerShell + WSL bash + random make` for the same build.
- Use a single toolchain/shell consistently (Git Bash is recommended in this repo).

Android Build (Windows, ARMv7)
From PowerShell:
1. `cd D:\Games\work\ulua_runtime`
2. `$env:ANDROID_NDK_ROOT='D:/mobile/sdk/win/ndk/android-ndk-r10e'`
3. `$env:MAKE='make'`
4. `Remove-Item Env:CFLAGS,Env:CPPFLAGS,Env:LDFLAGS,Env:MAKEFLAGS,Env:TARGET_FLAGS -ErrorAction SilentlyContinue`
5. `& 'C:\Program Files\Git\bin\bash.exe' win_build_src/build-android-arm.sh`

Expected output contains:
- `LuaJIT SYSROOT: ...`
- `OK        Successfully built LuaJIT`
- `Build done: .../libs/armeabi-v7a/libulua.so`

Android Build (Windows, x86)
From PowerShell:
1. `cd D:\Games\work\ulua_runtime`
2. `$env:ANDROID_NDK_ROOT='D:/mobile/sdk/win/ndk/android-ndk-r10e'`
3. `$env:MAKE='make'`
4. `& 'C:\Program Files\Git\bin\bash.exe' win_build_src/build-android-x86.sh`

Common Error and Fix
Error:
- `...include-fixed/limits.h: error: no include path in which to search for limits.h`

Cause:
- `--sysroot` was not effectively passed to GCC (usually due to mixed shell/tool environment).

Fix:
- Run build from Git Bash entry command shown above.
- Ensure scripts are checked out with LF line endings (this repository includes `.gitattributes` for shell/Makefile LF policy).
- Avoid running legacy/backup script copies.

Tracked Build Scripts
- `win_build_src/build-android-arm.sh`
- `win_build_src/build-android-arm64.sh`
- `win_build_src/build-android-x86.sh`
