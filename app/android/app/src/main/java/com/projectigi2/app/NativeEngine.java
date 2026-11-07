package com.projectigi2.app;

import android.util.Log;
import android.view.Surface;

/**
 * Java interface to the native C++ ProjectIGI Engine (libprojectigi.so) hosting CoreCLR and Vulkan.
 */
public final class NativeEngine {
    private static final String TAG = "ProjectIGI";

    static {
        try {
            System.loadLibrary("projectigi");
            Log.i(TAG, "Successfully loaded native NDK library libprojectigi.so");
        } catch (UnsatisfiedLinkError e) {
            Log.e(TAG, "Failed to load native library libprojectigi.so", e);
        }
    }

    public static native boolean nativeStartGame(
        Surface surface,
        int width,
        int height,
        String filesDir,
        String nativeLibDir,
        String options
    );

    public static native void nativeOnSurfaceChanged(int width, int height);
    public static native void nativeOnSurfaceDestroyed();
    public static native void nativeOnSurfaceRecreated(Surface surface, int width, int height);
    public static native boolean nativeIsGameRunning();
    public static native void nativeOnTouch(int action, int pointerId, float x, float y);
    public static native void nativeOnKey(int keyCode, boolean down);
    public static native void nativeOnGyro(float yawRadians, float pitchRadians);
    public static native void nativeOnText(String text);
    public static native boolean nativeIsTextFieldFocused();
    public static native boolean nativeIsKeyboardRequested();
    public static native boolean nativeIsMainMenu();
    public static native void nativeSetKeyboardRequested(boolean requested);
    public static native void nativeShutdown();

    private NativeEngine() {}
}
