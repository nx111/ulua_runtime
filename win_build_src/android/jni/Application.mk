APP_ABI := arm64-v8a
APP_PLATFORM := android-21
APP_LDFLAGS += -Wl,-z,max-page-size=16384 -Wl,-z,common-page-size=16384
