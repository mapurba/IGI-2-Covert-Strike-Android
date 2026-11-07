#include <jni.h>
#include <android/native_window.h>
#include <android/native_window_jni.h>
#include <android/log.h>
#include <dlfcn.h>
#include <dirent.h>
#include <string>
#include <vector>
#include <thread>
#include <atomic>
#include <mutex>

#define LOG_TAG "ProjectIGI-NDK"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN, LOG_TAG, __VA_ARGS__)



// CoreCLR hosting function signatures
typedef int (*coreclr_initialize_fn)(
    const char* exePath,
    const char* appDomainFriendlyName,
    int propertyCount,
    const char** propertyKeys,
    const char** propertyValues,
    void** hostHandle,
    unsigned int* domainId);

typedef int (*coreclr_shutdown_fn)(
    void* hostHandle,
    unsigned int domainId);

typedef int (*coreclr_create_delegate_fn)(
    void* hostHandle,
    unsigned int domainId,
    const char* entryPointAssemblyName,
    const char* entryPointTypeName,
    const char* entryPointMethodName,
    void** delegate);

// Managed delegates
typedef int (*RunFn)(intptr_t nativeWindow, int width, int height, intptr_t dataDirPtr, intptr_t optionsPtr);
typedef void (*TouchFn)(int action, int pointerId, float x, float y);
typedef void (*KeyFn)(int keyCode, int down);
typedef void (*ResizeFn)(int width, int height);
typedef void (*StopFn)();
typedef void (*GyroFn)(float yawRadians, float pitchRadians);
typedef void (*TextFn)(const char* text);
typedef int (*TextFocusFn)();
typedef int (*KeyboardRequestedFn)();
typedef int (*IsMainMenuFn)();
typedef void (*SetKeyboardRequestedFn)(int value);
typedef void (*SurfaceLostFn)();
typedef void (*SurfaceRecreatedFn)(intptr_t nativeWindow, int width, int height);

static void* g_coreclr_lib = nullptr;
static void* g_host_handle = nullptr;
static unsigned int g_domain_id = 0;

static RunFn g_run_fn = nullptr;
static TouchFn g_touch_fn = nullptr;
static KeyFn g_key_fn = nullptr;
static ResizeFn g_resize_fn = nullptr;
static StopFn g_stop_fn = nullptr;
static GyroFn g_gyro_fn = nullptr;
static TextFn g_text_fn = nullptr;
static TextFocusFn g_text_focus_fn = nullptr;
static KeyboardRequestedFn g_keyboard_requested_fn = nullptr;
static IsMainMenuFn g_main_menu_fn = nullptr;
static SetKeyboardRequestedFn g_set_keyboard_requested_fn = nullptr;
static SurfaceLostFn g_surface_lost_fn = nullptr;
static SurfaceRecreatedFn g_surface_recreated_fn = nullptr;

static std::thread g_game_thread;
static std::atomic<bool> g_game_running{false};
static ANativeWindow* g_native_window = nullptr;

static JavaVM* g_jvm = nullptr;
static std::mutex g_surface_mutex;
static std::condition_variable g_surface_cv;
static bool g_surface_updated = false;

static std::string build_tpa_list(const std::string& directory) {
    std::string tpa;
    DIR* dir = opendir(directory.c_str());
    if (!dir) {
        LOGE("Failed to open managed directory: %s", directory.c_str());
        return tpa;
    }
    struct dirent* entry;
    while ((entry = readdir(dir)) != nullptr) {
        std::string name = entry->d_name;
        if (name.length() > 4 && name.substr(name.length() - 4) == ".dll") {
            if (!tpa.empty()) {
                tpa += ":";
            }
            tpa += directory + "/" + name;
        }
    }
    closedir(dir);
    return tpa;
}

static bool initialize_coreclr(const std::string& filesDir, const std::string& nativeLibDir) {
    if (g_host_handle != nullptr) {
        return true;
    }

    std::string managedDir = filesDir + "/managed";
    LOGI("Scanning managed assemblies in: %s", managedDir.c_str());
    std::string tpaList = build_tpa_list(managedDir);
    if (tpaList.empty()) {
        LOGE("No assemblies found in %s! Did asset extraction fail?", managedDir.c_str());
        return false;
    }

    std::string coreclrPath = nativeLibDir + "/libcoreclr.so";
    LOGI("Loading CoreCLR from %s...", coreclrPath.c_str());
    g_coreclr_lib = dlopen(coreclrPath.c_str(), RTLD_NOW | RTLD_GLOBAL);
    if (!g_coreclr_lib) {
        LOGW("Failed to load %s (%s), trying bare dlopen...", coreclrPath.c_str(), dlerror());
        g_coreclr_lib = dlopen("libcoreclr.so", RTLD_NOW | RTLD_GLOBAL);
    }
    if (!g_coreclr_lib) {
        LOGE("Fatal: could not dlopen libcoreclr.so: %s", dlerror());
        return false;
    }

    std::string openalPath = nativeLibDir + "/libopenal.so";
    void* openalLib = dlopen(openalPath.c_str(), RTLD_NOW | RTLD_GLOBAL);
    if (!openalLib) {
        openalPath = managedDir + "/libopenal.so";
        openalLib = dlopen(openalPath.c_str(), RTLD_NOW | RTLD_GLOBAL);
    }
    if (!openalLib) {
        openalPath = filesDir + "/libopenal.so";
        openalLib = dlopen(openalPath.c_str(), RTLD_NOW | RTLD_GLOBAL);
    }
    if (!openalLib) {
        openalLib = dlopen("libopenal.so", RTLD_NOW | RTLD_GLOBAL);
    }
    if (openalLib) {
        LOGI("Preloaded OpenAL native library successfully: %s", openalPath.c_str());
    } else {
        LOGW("Failed to preload OpenAL: %s", dlerror());
    }

    setenv("PROJECTIGI_NATIVE_LIB_DIR", nativeLibDir.c_str(), 1);

    auto coreclr_init = (coreclr_initialize_fn)dlsym(g_coreclr_lib, "coreclr_initialize");
    auto coreclr_delegate = (coreclr_create_delegate_fn)dlsym(g_coreclr_lib, "coreclr_create_delegate");

    if (!coreclr_init || !coreclr_delegate) {
        LOGE("Fatal: missing coreclr_initialize or coreclr_create_delegate symbols in libcoreclr.so");
        return false;
    }

    const char* propertyKeys[] = {
        "TRUSTED_PLATFORM_ASSEMBLIES",
        "APP_PATHS",
        "NATIVE_DLL_SEARCH_DIRECTORIES"
    };

    std::string nativeDirs = nativeLibDir + ":" + filesDir + ":" + managedDir;
    const char* propertyValues[] = {
        tpaList.c_str(),
        managedDir.c_str(),
        nativeDirs.c_str()
    };

    LOGI("Initializing CoreCLR runtime...");
    int hr = coreclr_init(
        managedDir.c_str(),
        "ProjectIGIDomain",
        3,
        propertyKeys,
        propertyValues,
        &g_host_handle,
        &g_domain_id
    );

    if (hr < 0 || !g_host_handle) {
        LOGE("coreclr_initialize failed with HRESULT 0x%08X", hr);
        return false;
    }

    LOGI("CoreCLR initialized successfully (domain %u). Binding delegates...", g_domain_id);

    int res1 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "Run", (void**)&g_run_fn);
    int res2 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "OnTouch", (void**)&g_touch_fn);
    int res3 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "OnKey", (void**)&g_key_fn);
    int res4 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "OnResize", (void**)&g_resize_fn);
    int res5 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "Stop", (void**)&g_stop_fn);
    int res6 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "OnGyro", (void**)&g_gyro_fn);
    int res7 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "OnText", (void**)&g_text_fn);
    int res8 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "IsTextFieldFocused", (void**)&g_text_focus_fn);
    int res9 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "OnSurfaceLost", (void**)&g_surface_lost_fn);
    int res10 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "OnSurfaceRecreated", (void**)&g_surface_recreated_fn);
    int res11 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "IsKeyboardRequested", (void**)&g_keyboard_requested_fn);
    int res12 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "SetKeyboardRequested", (void**)&g_set_keyboard_requested_fn);
    int res13 = coreclr_delegate(g_host_handle, g_domain_id, "OpenIGI.Android", "OpenIGI.Android.AndroidHost", "IsMainMenu", (void**)&g_main_menu_fn);

    LOGI("Delegate binding results: Run=0x%08X, OnTouch=0x%08X, OnKey=0x%08X, OnResize=0x%08X, Stop=0x%08X, OnGyro=0x%08X, OnText=0x%08X, IsTextFieldFocused=0x%08X, OnSurfaceLost=0x%08X, OnSurfaceRecreated=0x%08X, IsKeyboardRequested=0x%08X, SetKeyboardRequested=0x%08X, IsMainMenu=0x%08X", res1, res2, res3, res4, res5, res6, res7, res8, res9, res10, res11, res12, res13);

    if (res1 < 0 || !g_run_fn) {
        LOGE("Failed to bind AndroidHost.Run: 0x%08X", res1);
        return false;
    }

    LOGI("Successfully bound all AndroidHost managed callbacks!");
    return true;
}

extern "C" {

JNIEXPORT jboolean JNICALL Java_com_projectigi2_app_NativeEngine_nativeStartGame
  (JNIEnv *env, jclass clazz, jobject surface, jint width, jint height, jstring javaFilesDir, jstring javaNativeLibDir, jstring javaOptions) {
    LOGI("Java_com_projectigi2_app_NativeEngine_nativeStartGame: %dx%d", width, height);

    if (g_game_running.load()) {
        LOGW("Game is already running.");
        return JNI_TRUE;
    }

    const char* filesDirC = env->GetStringUTFChars(javaFilesDir, nullptr);
    const char* nativeLibDirC = env->GetStringUTFChars(javaNativeLibDir, nullptr);
    const char* optionsC = env->GetStringUTFChars(javaOptions, nullptr);

    std::string filesDir(filesDirC ? filesDirC : "");
    std::string nativeLibDir(nativeLibDirC ? nativeLibDirC : "");
    std::string options(optionsC ? optionsC : "");

    if (filesDirC) env->ReleaseStringUTFChars(javaFilesDir, filesDirC);
    if (nativeLibDirC) env->ReleaseStringUTFChars(javaNativeLibDir, nativeLibDirC);
    if (optionsC) env->ReleaseStringUTFChars(javaOptions, optionsC);

    if (!initialize_coreclr(filesDir, nativeLibDir)) {
        LOGE("Failed to initialize CoreCLR.");
        return JNI_FALSE;
    }

    int landscapeWidth = width > height ? width : height;
    int landscapeHeight = width > height ? height : width;

    if (g_native_window) {
        ANativeWindow_release(g_native_window);
        g_native_window = nullptr;
    }


    g_native_window = ANativeWindow_fromSurface(env, surface);
    if (!g_native_window) {
        LOGE("ANativeWindow_fromSurface failed.");
        return JNI_FALSE;
    }

    int32_t setGeoRes = ANativeWindow_setBuffersGeometry(g_native_window, landscapeWidth, landscapeHeight, 0);
    LOGI("ANativeWindow_setBuffersGeometry(%dx%d) returned %d", landscapeWidth, landscapeHeight, setGeoRes);

    std::string dataDir = filesDir + "/Project IGI 2";


    g_game_running.store(true);
    g_game_thread = std::thread([landscapeWidth, landscapeHeight, dataDir, options]() {
        LOGI("Game thread started. Attaching thread to Java VM...");
        JNIEnv* env = nullptr;
        if (g_jvm) {
            jint attachResult = g_jvm->AttachCurrentThread(&env, nullptr);
            LOGI("JVM AttachCurrentThread returned %d, JNIEnv=%p", attachResult, env);
        }

        LOGI("Vulkan backend selected: vkCreateAndroidSurfaceKHR has exclusive access (%dx%d)...", landscapeWidth, landscapeHeight);

        int exitCode = g_run_fn((intptr_t)g_native_window, landscapeWidth, landscapeHeight, (intptr_t)dataDir.c_str(), (intptr_t)options.c_str());
        LOGI("ProjectIGI managed game loop ended with exit code: %d", exitCode);


        if (g_jvm) {
            g_jvm->DetachCurrentThread();
        }
        g_game_running.store(false);
    });

    return JNI_TRUE;
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeOnSurfaceChanged
  (JNIEnv *env, jclass clazz, jint width, jint height) {
    int landscapeWidth = width > height ? width : height;
    int landscapeHeight = width > height ? height : width;
    if (g_native_window) {
        ANativeWindow_setBuffersGeometry(g_native_window, landscapeWidth, landscapeHeight, 0);
    }
    if (g_resize_fn) {
        g_resize_fn(landscapeWidth, landscapeHeight);
    }
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeOnSurfaceDestroyed
  (JNIEnv *env, jclass clazz) {
    LOGI("nativeOnSurfaceDestroyed");
    // The surface is gone (app backgrounded) but the engine is NOT stopped: it idles with
    // no surface until a new one arrives, so the player returns to the pause menu in the
    // mission instead of the main menu. Genuine shutdown still goes through nativeShutdown.
    if (g_surface_lost_fn) {
        g_surface_lost_fn();
    }
    if (g_native_window) {
        ANativeWindow_release(g_native_window);
        g_native_window = nullptr;
    }
}

JNIEXPORT jboolean JNICALL Java_com_projectigi2_app_NativeEngine_nativeIsGameRunning
  (JNIEnv *env, jclass clazz) {
    return g_game_running.load() ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeOnSurfaceRecreated
  (JNIEnv *env, jclass clazz, jobject surface, jint width, jint height) {
    LOGI("Java_com_projectigi2_app_NativeEngine_nativeOnSurfaceRecreated: %dx%d", width, height);

    if (g_native_window) {
        ANativeWindow_release(g_native_window);
        g_native_window = nullptr;
    }


    if (surface) {
        g_native_window = ANativeWindow_fromSurface(env, surface);
        if (g_native_window) {
            int landscapeWidth = width > height ? width : height;
            int landscapeHeight = width > height ? height : width;
            ANativeWindow_setBuffersGeometry(g_native_window, landscapeWidth, landscapeHeight, 0);

            // Hand the new window to managed code so it can rebind the presentation surface
            // to it and resume in place (instead of restarting the engine to the main menu).
            if (g_surface_recreated_fn) {
                g_surface_recreated_fn(
                    reinterpret_cast<intptr_t>(g_native_window),
                    landscapeWidth,
                    landscapeHeight);
            }
        }
    }

    {
        std::lock_guard<std::mutex> lock(g_surface_mutex);
        g_surface_updated = true;
    }
    g_surface_cv.notify_all();
}

extern "C" JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM* vm, void* reserved) {
    g_jvm = vm;
    return JNI_VERSION_1_6;
}

extern "C" JNIEXPORT intptr_t JNICALL get_current_jni_env() {
    if (!g_jvm) {
        LOGE("get_current_jni_env: g_jvm is null!");
        return 0;
    }
    JNIEnv* env = nullptr;
    jint res = g_jvm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);
    if (res == JNI_EDETACHED) {
        if (g_jvm->AttachCurrentThread(&env, nullptr) != JNI_OK) {
            LOGE("get_current_jni_env: AttachCurrentThread failed!");
            return 0;
        }
        LOGI("get_current_jni_env: Attached thread to JVM, env=%p", env);
    } else if (res != JNI_OK) {
        LOGE("get_current_jni_env: GetEnv failed with status %d", res);
        return 0;
    }
    return reinterpret_cast<intptr_t>(env);
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeOnTouch
  (JNIEnv *env, jclass clazz, jint action, jint pointerId, jfloat x, jfloat y) {
    if (g_touch_fn) {
        g_touch_fn(action, pointerId, x, y);
    }
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeOnKey
  (JNIEnv *env, jclass clazz, jint keyCode, jboolean down) {
    if (g_key_fn) {
        g_key_fn(keyCode, down ? 1 : 0);
    }
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeOnGyro
  (JNIEnv *env, jclass clazz, jfloat yawRadians, jfloat pitchRadians) {
    if (g_gyro_fn) {
        g_gyro_fn(yawRadians, pitchRadians);
    }
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeOnText
  (JNIEnv *env, jclass clazz, jstring text) {
    if (g_text_fn && text != nullptr) {
        const char* utf8 = env->GetStringUTFChars(text, nullptr);
        if (utf8 != nullptr) {
            g_text_fn(utf8);
            env->ReleaseStringUTFChars(text, utf8);
        }
    }
}

JNIEXPORT jboolean JNICALL Java_com_projectigi2_app_NativeEngine_nativeIsTextFieldFocused
  (JNIEnv *env, jclass clazz) {
    if (g_text_focus_fn) {
        return g_text_focus_fn() != 0 ? JNI_TRUE : JNI_FALSE;
    }
    return JNI_FALSE;
}

JNIEXPORT jboolean JNICALL Java_com_projectigi2_app_NativeEngine_nativeIsKeyboardRequested
  (JNIEnv *env, jclass clazz) {
    if (g_keyboard_requested_fn) {
        return g_keyboard_requested_fn() != 0 ? JNI_TRUE : JNI_FALSE;
    }
    return JNI_FALSE;
}

JNIEXPORT jboolean JNICALL Java_com_projectigi2_app_NativeEngine_nativeIsMainMenu
  (JNIEnv *env, jclass clazz) {
    if (g_main_menu_fn) {
        return g_main_menu_fn() != 0 ? JNI_TRUE : JNI_FALSE;
    }
    return JNI_FALSE;
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeSetKeyboardRequested
  (JNIEnv *env, jclass clazz, jboolean requested) {
    if (g_set_keyboard_requested_fn) {
        g_set_keyboard_requested_fn(requested ? 1 : 0);
    }
}

JNIEXPORT void JNICALL Java_com_projectigi2_app_NativeEngine_nativeShutdown
  (JNIEnv *env, jclass clazz) {
    LOGI("nativeShutdown");
    if (g_stop_fn) {
        g_stop_fn();
    }
    if (g_game_thread.joinable()) {
        g_game_thread.join();
    }
    if (g_native_window) {
        ANativeWindow_release(g_native_window);
        g_native_window = nullptr;
    }
}



} // extern "C"
