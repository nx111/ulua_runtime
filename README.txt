ulua编译工程PC\Andriod基于luajit2.0.4，MAC\iOS\WinPhone基于luavm 

LuaJIT http://luajit.org/

BUILDING
- Place LuaJIT distribution in <source root>/luajit
- Run platform specific shell script to build uLua library
- Copy uLua library into relevant Unity plugins folder

WINDOWS平台使用Mingw32/64
下载地址：https://github.com/niXman/mingw-builds-binaries/releases
    注意最好不要选择mcf的版本，那将产生依赖。
	
WINDOWS平台使用MSBuild
安装工具
   Visual Studio Build Tools 2026，勾选：
      MSVC v143 x64/x86 build tools
      Windows 10/11 SDK
   打开 MSVC 环境（x64 示例）
      用 x64 Native Tools Command Prompt for VS 2022，或执行：
        call "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64
        cd /d D:\Games\work\ulua_runtime\win_build_src
	  执行 build-win-msvc.bat x64 或 build-win-msvc.bat x86，默认x64。
	

APPLE MAC OSX
基于XCode 6.0以上的版本。

ANDROID编译工具NDK(adt-bundle-windows)
可选：android-ndk-r10e或相应的版本。

//-------------2026-03-04-------------
(1)兼容hanjiasongshu的金庸群侠传X。

//-------------2015-10-17-------------
(1)编译底层库使其支持在安卓、iOS进行Lua真机调试。
PS:现在可调试平台包括WIN/MAC/LINUX/iOS/Android全平台。

