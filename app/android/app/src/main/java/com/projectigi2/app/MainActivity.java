package com.projectigi2.app;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.ContentValues;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ActivityInfo;
import android.content.pm.PackageManager;
import android.content.res.AssetManager;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.graphics.Bitmap;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.inputmethod.InputMethodManager;
import android.widget.EditText;
import android.graphics.BitmapFactory;
import android.graphics.BlendMode;
import android.graphics.BlendModeColorFilter;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.SystemClock;
import android.provider.MediaStore;
import android.util.Log;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import androidx.documentfile.provider.DocumentFile;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.Locale;

public class MainActivity extends Activity implements SurfaceHolder.Callback {
    private static final String TAG = "ProjectIGI2";
    private static final String ASSET_VERSION = "v22_perm_on_start";
    private static final int REQUEST_SELECT_GAME_FOLDER = 2001;
    private static final int REQUEST_STORAGE_PERMISSIONS = 1002;
    private static final String PREFS_NAME = "projectigi_prefs";
    private static final String PREF_RENDERER_BACKEND = "renderer_backend";

    private SurfaceView surfaceView;
    private FrameLayout rootLayout;
    private FrameLayout splashLayout;
    private TextView statusText;
    private ProgressBar progressBar;
    private Button selectFolderBtn;
    private Button playBtn;
    private Button vulkanBtn;
    private Button webGpuBtn;
    private String rendererBackend = "vulkan";

    private boolean isExtracted = false;
    private boolean isGameFolderVerified = false;
    private boolean isGameStarted = false;
    private SurfaceHolder pendingHolder = null;

    // Total / copied bytes for the game-folder install, driving the determinate progress bar.
    private long copyTotalBytes = 0;
    private long copyDoneBytes = 0;
    // Last time the progress bar was redrawn (uptime ms), to throttle UI updates.
    private long lastProgressUiMs = 0;
    // Gyroscope aim: the sensor and its listener, and the last event timestamp (ns)
    // so rates (rad/s) integrate into per-event angle deltas.
    private SensorManager sensorManager;
    private Sensor gyroSensor;
    private SensorEventListener gyroListener;
    private long lastGyroTimestampNs = 0;
    // Soft keyboard: a hidden, zero-size EditText the IME types into, and a poller
    // that shows/hides the keyboard when the game focuses a text field.
    private EditText hiddenInput;
    private Handler imeHandler;
    private Runnable imePoller;
    private boolean imeShown = false;
    // Socials: a small button in the top-left corner of the file-picker screen and
    // the main menu, opening a popup with Discord + Reddit links. A native View, not
    // a managed overlay: the retail menu is drawn from menu data, and there is no
    // managed-to-Java channel to fire an ACTION_VIEW intent from C#.
    private ImageButton socialsButton;
    private AlertDialog socialsDialog;
    private boolean socialsButtonShown = false;
    private static final String DISCORD_URL = "https://discord.gg/hSyz7KWZrg";
    private static final String REDDIT_URL = "https://www.reddit.com/user/project_igi_porter";
    // True while this Activity is in the foreground. The quit watcher uses it to tell a Quit
    // in the main menu (foreground, game stops) from tabbing out (backgrounded, game stops too).
    private boolean isResumed = false;
    // Watches for the game thread ending while we are in the foreground (a Quit), so Quit in
    // the main menu closes the app instead of leaving a black screen behind.
    private volatile boolean quitWatcherStopped = false;
    private Thread quitWatcherThread = null;
    // Set only by the quit watcher when it has seen a genuine Quit (game thread ended
    // while in the foreground). onDestroy kills the process when this is set, so the
    // next PLAY starts a fresh process instead of re-entering the never-shut-down
    // runtime in this one (WebGPU Crash A: second-launch SIGSEGV). It is never set on
    // backgrounding, tab-out or surface loss, where the game must survive to resume.
    private volatile boolean gameQuitRequested = false;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Force landscape orientation strictly (supports both landscape directions)
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);

        // Keep screen on during gameplay
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        // Sustained performance mode (API 24+)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            getWindow().setSustainedPerformanceMode(true);
        }

        // Handle offline intent extras/saved state strictly null-safe to prevent crash
        String fallbackGameDir = getFilesDir().getAbsolutePath();
        String gameDir = fallbackGameDir;
        String engineMode = "normal";

        if (savedInstanceState != null) {
            try {
                String safeExtra = savedInstanceState.getString("engine_mode");
                if (safeExtra != null) {
                    engineMode = safeExtra;
                }
            } catch (Throwable e) {
                Log.w(TAG, "Safe savedInstanceState extra read bypassed.");
            }
        }

        Intent intent = getIntent();
        if (intent != null) {
            try {
                Bundle extras = intent.getExtras();
                if (extras != null) {
                    String targetMap = extras.getString("map_name");
                    if (targetMap == null) {
                        targetMap = "default";
                        Log.d(TAG, "map_name intent string is null, using fallback game directory: " + fallbackGameDir);
                    }
                    String customDir = extras.getString("game_dir");
                    if (customDir != null) {
                        gameDir = customDir;
                    }
                } else {
                    Log.w(TAG, "Intent bundle is null, explicitly falling back to default string.");
                    gameDir = "/data/user/0/com.projectigi2.app/files";
                }
            } catch (Throwable e) {
                Log.e(TAG, "Failed to parse intent bundle safely.", e);
                gameDir = "/data/user/0/com.projectigi2.app/files";
            }
        } else {
            gameDir = "/data/user/0/com.projectigi2.app/files";
        }

        // Request storage permissions on app startup
        requestStoragePermissionsOnStart();

        // Restore the saved renderer backend (default: Vulkan)
        rendererBackend = getSharedPreferences(PREFS_NAME, MODE_PRIVATE)
            .getString(PREF_RENDERER_BACKEND, "vulkan");
        if (!"webgpu".equals(rendererBackend)) rendererBackend = "vulkan";

        rootLayout = new FrameLayout(this);
        rootLayout.setBackgroundColor(Color.BLACK);

        surfaceView = new SurfaceView(this);
        surfaceView.getHolder().setFormat(android.graphics.PixelFormat.RGBA_8888);
        surfaceView.getHolder().addCallback(this);
        surfaceView.setOnTouchListener((v, event) -> onTouchEvent(event));
        rootLayout.addView(surfaceView, new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));

        setupGyroAndKeyboard();

        // Authentic I.G.I. 2 Launcher Splash
        splashLayout = new FrameLayout(this);
        splashLayout.setBackgroundColor(Color.rgb(8, 10, 18)); // #0a0c08

        // 1. Background image (igi2-loading.png — Ray's I.G.I. 2 art; the old IGI 1 img.png splash is gone)
        Bitmap bmp = null;
        for (String bgPath : new String[]{"public/Assets/menu_images/igi2-loading.png"}) {
            try (InputStream is = getAssets().open(bgPath)) {
                bmp = BitmapFactory.decodeStream(is);
                if (bmp != null) {
                    Log.i(TAG, "Loaded launcher background from: " + bgPath);
                    break;
                }
            } catch (Exception ignored) {}
        }
        if (bmp != null) {
            ImageView bgImage = new ImageView(this);
            bgImage.setImageBitmap(bmp);
            bgImage.setScaleType(ImageView.ScaleType.FIT_XY);
            splashLayout.addView(bgImage, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
        }

        // Center Tactical Card Container
        float density = getResources().getDisplayMetrics().density;
        LinearLayout cardLayout = new LinearLayout(this);
        cardLayout.setOrientation(LinearLayout.VERTICAL);
        cardLayout.setGravity(Gravity.CENTER_HORIZONTAL);
        cardLayout.setPadding((int)(32 * density), (int)(24 * density), (int)(32 * density), (int)(24 * density));

        // Dark tactical translucent background with green border
        GradientDrawable cardBg = new GradientDrawable();
        cardBg.setColor(Color.argb(225, 10, 16, 30)); // Translucent dark green
        cardBg.setStroke((int)(2 * density), Color.rgb(80, 160, 255)); // Phosphor green
        cardBg.setCornerRadius(10 * density);
        cardLayout.setBackground(cardBg);

        FrameLayout.LayoutParams cardParams = new FrameLayout.LayoutParams(
            (int)(540 * density), FrameLayout.LayoutParams.WRAP_CONTENT);
        cardParams.gravity = Gravity.CENTER;

        // Title
        TextView titleText = new TextView(this);
        titleText.setTextColor(Color.rgb(80, 160, 255));
        titleText.setTextSize(18);
        titleText.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        titleText.setLetterSpacing(0.12f);
        titleText.setGravity(Gravity.CENTER);
        titleText.setText("I.G.I. 2: COVERT STRIKE");
        cardLayout.addView(titleText);

        // Subtitle
        TextView subtitleText = new TextView(this);
        subtitleText.setTextColor(Color.rgb(170, 195, 225));
        subtitleText.setTextSize(11);
        subtitleText.setTypeface(Typeface.MONOSPACE);
        subtitleText.setGravity(Gravity.CENTER);
        subtitleText.setPadding(0, (int)(4 * density), 0, (int)(16 * density));
        subtitleText.setText("AUTHENTIC GAME FILES REQUIRED");
        cardLayout.addView(subtitleText);

        // Glowing progress bar
        progressBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        progressBar.setIndeterminate(true);
        progressBar.setProgressTintList(ColorStateList.valueOf(Color.rgb(80, 160, 255)));
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            progressBar.getIndeterminateDrawable().setColorFilter(
                new BlendModeColorFilter(Color.rgb(80, 160, 255), BlendMode.SRC_IN));
        }
        LinearLayout.LayoutParams pbParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT, (int)(5 * density));
        pbParams.bottomMargin = (int)(12 * density);
        cardLayout.addView(progressBar, pbParams);

        // Status Message Text
        statusText = new TextView(this);
        statusText.setTextColor(Color.rgb(110, 160, 210));
        statusText.setTextSize(12);
        statusText.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        statusText.setGravity(Gravity.CENTER);
        statusText.setPadding(0, 0, 0, (int)(18 * density));
        statusText.setText("INITIALIZING SYSTEM ASSEMBLIES...");
        cardLayout.addView(statusText);

        // Renderer selector: the backend the engine starts on (Vulkan or WebGPU)
        TextView rendererLabel = new TextView(this);
        rendererLabel.setTextColor(Color.rgb(170, 195, 225));
        rendererLabel.setTextSize(10);
        rendererLabel.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        rendererLabel.setGravity(Gravity.CENTER);
        rendererLabel.setLetterSpacing(0.12f);
        rendererLabel.setPadding(0, 0, 0, (int)(6 * density));
        rendererLabel.setText("RENDERER");
        cardLayout.addView(rendererLabel);

        LinearLayout rendererRow = new LinearLayout(this);
        rendererRow.setOrientation(LinearLayout.HORIZONTAL);
        rendererRow.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams rendererRowParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        rendererRowParams.bottomMargin = (int)(14 * density);
        cardLayout.addView(rendererRow, rendererRowParams);

        vulkanBtn = new Button(this);
        vulkanBtn.setText("VULKAN");
        vulkanBtn.setTextSize(11);
        vulkanBtn.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        LinearLayout.LayoutParams vulkanLp = new LinearLayout.LayoutParams(
            (int)(160 * density), (int)(40 * density));
        vulkanLp.rightMargin = (int)(12 * density);
        vulkanBtn.setOnClickListener(v -> setRendererBackend("vulkan"));
        rendererRow.addView(vulkanBtn, vulkanLp);

        webGpuBtn = new Button(this);
        webGpuBtn.setText("WEBGPU");
        webGpuBtn.setTextSize(11);
        webGpuBtn.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        LinearLayout.LayoutParams webGpuLp = new LinearLayout.LayoutParams(
            (int)(160 * density), (int)(40 * density));
        webGpuBtn.setOnClickListener(v -> setRendererBackend("webgpu"));
        rendererRow.addView(webGpuBtn, webGpuLp);

        updateRendererButtons();

        // Buttons Layout Row
        LinearLayout btnRow = new LinearLayout(this);
        btnRow.setOrientation(LinearLayout.HORIZONTAL);
        btnRow.setGravity(Gravity.CENTER);

        // 1. Select Game Folder Button
        selectFolderBtn = new Button(this);
        selectFolderBtn.setText("SELECT GAME FOLDER");
        selectFolderBtn.setTextSize(11);
        selectFolderBtn.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        selectFolderBtn.setTextColor(Color.WHITE);

        GradientDrawable selectBg = new GradientDrawable();
        selectBg.setColor(Color.rgb(20, 35, 60));
        selectBg.setStroke((int)(1.5f * density), Color.rgb(80, 160, 255));
        selectBg.setCornerRadius(6 * density);
        selectFolderBtn.setBackground(selectBg);

        LinearLayout.LayoutParams selectLp = new LinearLayout.LayoutParams(
            (int)(220 * density), (int)(44 * density));
        selectLp.rightMargin = (int)(12 * density);
        selectFolderBtn.setOnClickListener(v -> openGameFolderPicker());
        btnRow.addView(selectFolderBtn, selectLp);

        // 2. Play Button
        playBtn = new Button(this);
        playBtn.setText("PLAY");
        playBtn.setTextSize(13);
        playBtn.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);

        LinearLayout.LayoutParams playLp = new LinearLayout.LayoutParams(
            (int)(160 * density), (int)(44 * density));
        playBtn.setOnClickListener(v -> onPlayButtonClicked());
        btnRow.addView(playBtn, playLp);

        cardLayout.addView(btnRow);
        splashLayout.addView(cardLayout, cardParams);

        rootLayout.addView(splashLayout, new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));

        // Socials button: top-left, above the picker card and the game surface alike,
        // so one button serves both screens. Its visibility is polled below.
        setupSocialsButton();

        setContentView(rootLayout);

        // Disable Play button by default
        updatePlayButtonState(false);

        // Apply immersive mode after view is attached
        applyImmersiveMode();

        // Start background asset preparation
        new Thread(this::prepareAssetsAndStart).start();
    }

    private void requestStoragePermissionsOnStart() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            if (checkSelfPermission(android.Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED
                || checkSelfPermission(android.Manifest.permission.READ_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(new String[]{
                    android.Manifest.permission.WRITE_EXTERNAL_STORAGE,
                    android.Manifest.permission.READ_EXTERNAL_STORAGE
                }, REQUEST_STORAGE_PERMISSIONS);
            }
        }
    }

    private void updatePlayButtonState(boolean enabled) {
        isGameFolderVerified = enabled;
        float density = getResources().getDisplayMetrics().density;
        runOnUiThread(() -> {
            playBtn.setEnabled(enabled);
            GradientDrawable playBg = new GradientDrawable();
            playBg.setCornerRadius(6 * density);

            if (enabled) {
                playBg.setColor(Color.rgb(80, 160, 255)); // Phosphor green
                playBg.setStroke((int)(2 * density), Color.rgb(190, 215, 255));
                playBtn.setTextColor(Color.BLACK);
            } else {
                playBg.setColor(Color.rgb(38, 44, 56)); // Disabled dark grey
                playBg.setStroke((int)(1 * density), Color.rgb(65, 75, 95));
                playBtn.setTextColor(Color.rgb(100, 115, 140));
            }
            playBtn.setBackground(playBg);
        });
    }

    private void setRendererBackend(String backend) {
        rendererBackend = "webgpu".equals(backend) ? "webgpu" : "vulkan";
        getSharedPreferences(PREFS_NAME, MODE_PRIVATE).edit()
            .putString(PREF_RENDERER_BACKEND, rendererBackend).apply();
        updateRendererButtons();
    }

    private void updateRendererButtons() {
        if (vulkanBtn == null || webGpuBtn == null) return;
        float density = getResources().getDisplayMetrics().density;
        styleRendererButton(vulkanBtn, "vulkan".equals(rendererBackend), density);
        styleRendererButton(webGpuBtn, "webgpu".equals(rendererBackend), density);
    }

    private void styleRendererButton(Button btn, boolean selected, float density) {
        GradientDrawable bg = new GradientDrawable();
        bg.setCornerRadius(6 * density);
        if (selected) {
            bg.setColor(Color.rgb(80, 160, 255)); // Phosphor green
            bg.setStroke((int)(2 * density), Color.rgb(190, 215, 255));
            btn.setTextColor(Color.BLACK);
        } else {
            bg.setColor(Color.rgb(20, 35, 60)); // Dark green
            bg.setStroke((int)(1 * density), Color.rgb(65, 80, 68));
            btn.setTextColor(Color.rgb(100, 120, 105));
        }
        btn.setBackground(bg);
    }

    private void prepareAssetsAndStart() {
        File filesDir = getFilesDir();
        File marker = new File(filesDir, ".installed_" + ASSET_VERSION);

        if (!marker.exists()) {
            runOnUiThread(() -> statusText.setText("Extracting system assemblies..."));
            Log.i(TAG, "Extracting managed assets to " + filesDir.getAbsolutePath());

            try {
                // Delete managed assemblies
                deleteRecursively(new File(filesDir, "managed"));
                deleteRecursively(new File(filesDir, "Assets"));

                // Extract managed DLLs
                File managedDir = new File(filesDir, "managed");
                copyAssetFolder(getAssets(), "managed", managedDir);

                // Extract engine Assets (fonts, weather, UI images, materials)
                File assetsDir = new File(filesDir, "Assets");
                copyAssetFolder(getAssets(), "public/Assets", assetsDir);
                File managedAssetsDir = new File(managedDir, "Assets");
                copyAssetFolder(getAssets(), "public/Assets", managedAssetsDir);

                // Copy libopenal.so (the old bundled img.png splash is gone; the loading art now
                // ships under public/Assets/menu_images and is copied fresh on every launch below)
                File[] libDirs = new File[]{filesDir, assetsDir, managedDir, managedAssetsDir};
                for (File d : libDirs) {
                    copySingleAssetSafe("managed/libopenal.so", new File(d, "libopenal.so"));
                }

                marker.createNewFile();
                Log.i(TAG, "Managed assemblies extraction completed.");
            } catch (Exception e) {
                Log.e(TAG, "Managed extraction error", e);
                runOnUiThread(() -> statusText.setText("Extraction error: " + e.getMessage()));
                return;
            }
        } else {
            try {
                File managedDir = new File(filesDir, "managed");
                copyAssetFolder(getAssets(), "managed", managedDir);
                File assetsDir = new File(filesDir, "Assets");
                File managedAssetsDir = new File(managedDir, "Assets");

                if (!assetsDir.exists()) {
                    copyAssetFolder(getAssets(), "public/Assets", assetsDir);
                }
                if (!managedAssetsDir.exists()) {
                    copyAssetFolder(getAssets(), "public/Assets", managedAssetsDir);
                }
            } catch (Exception e) {
                Log.w(TAG, "Managed sync error", e);
            }
        }

        // The loading art ships in the APK and is read out of the files directory by the
        // managed host; copy it on every launch so an update over an existing install
        // refreshes it too — the Assets folder itself is only extracted on first install.
        // JPEG on purpose: the managed sprite loader decodes JPEG, not PNG.
        copySingleAssetSafe(
            "public/Assets/menu_images/igi2-loading-title.jpg",
            new File(new File(filesDir, "Assets"), "menu_images/igi2-loading-title.jpg"));

        isExtracted = true;

        // Check if game folder already exists and is verified
        boolean hasExistingGame = checkExistingGameFolder();
        runOnUiThread(() -> {
            if (hasExistingGame) {
                statusText.setText("GAME FILES VERIFIED & READY!");
                updatePlayButtonState(true);
            } else {
                statusText.setText("SELECT GAME FOLDER TO CONTINUE");
                updatePlayButtonState(false);
            }
        });
    }

    private boolean checkExistingGameFolder() {
        File dataDir = new File(getFilesDir(), "Project IGI 2");
        if (!dataDir.exists() || !dataDir.isDirectory()) {
            return false;
        }

        // Verify presence of signature I.G.I. 2 folders/files
        int matches = 0;
        String[] signatureItems = new String[]{
            "common", "missions", "menusystem", "language", "igi2.exe"
        };

        File[] files = dataDir.listFiles();
        if (files == null) return false;

        for (File f : files) {
            String name = f.getName().toLowerCase();
            for (String sig : signatureItems) {
                if (name.equals(sig)) {
                    matches++;
                    break;
                }
            }
        }

        return matches >= 3;
    }

    private void openGameFolderPicker() {
        try {
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
            startActivityForResult(intent, REQUEST_SELECT_GAME_FOLDER);
        } catch (Exception e) {
            Log.e(TAG, "Failed to open folder picker", e);
            Toast.makeText(this, "Failed to open storage folder picker", Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQUEST_SELECT_GAME_FOLDER && resultCode == RESULT_OK && data != null) {
            Uri treeUri = data.getData();
            if (treeUri != null) {
                try {
                    getContentResolver().takePersistableUriPermission(
                        treeUri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
                } catch (Exception ignored) {}

                statusText.setText("VERIFYING GAME FOLDER STRUCTURE...");
                progressBar.setVisibility(View.VISIBLE);
                new Thread(() -> processAndCopyGameFolder(treeUri)).start();
            }
        }
    }

    private void processAndCopyGameFolder(Uri treeUri) {
        DocumentFile rootDoc = DocumentFile.fromTreeUri(this, treeUri);
        if (rootDoc == null || !rootDoc.exists() || !rootDoc.isDirectory()) {
            runOnUiThread(() -> {
                statusText.setText("INVALID FOLDER SELECTED");
                Toast.makeText(this, "Could not access selected folder", Toast.LENGTH_SHORT).show();
            });
            return;
        }

        // 1. Verify I.G.I. 2 folder signatures (data root is the pc folder)
        DocumentFile[] children = rootDoc.listFiles();
        int matches = 0;
        String[] signatureItems = new String[]{
            "common", "missions", "menusystem", "language", "igi2.exe"
        };

        for (DocumentFile child : children) {
            if (child.getName() != null) {
                String name = child.getName().toLowerCase();
                for (String sig : signatureItems) {
                    if (name.equals(sig)) {
                        matches++;
                        break;
                    }
                }
            }
        }

        if (matches < 2) {
            runOnUiThread(() -> {
                statusText.setText("INVALID GAME FOLDER");
                Toast.makeText(this, "Missing authentic I.G.I. 2 game files in selected folder", Toast.LENGTH_LONG).show();
            });
            return;
        }

        // 2. Copy files from SAF tree to internal getFilesDir()/Project IGI 2 (the pc folder)
        // First count the total bytes (a metadata-only traversal, no byte transfer),
        // so the progress bar can show real percent instead of spinning.
        runOnUiThread(() -> statusText.setText("COUNTING GAME FILES..."));
        copyTotalBytes = countTreeBytes(rootDoc);
        copyDoneBytes = 0;
        lastProgressUiMs = 0;
        runOnUiThread(() -> {
            progressBar.setIndeterminate(false);
            progressBar.setMax(100);
            progressBar.setProgress(0);
            statusText.setText("INSTALLING GAME FILES... 0%");
        });

        File targetDataDir = new File(getFilesDir(), "Project IGI 2");
        try {
            if (!targetDataDir.exists()) {
                targetDataDir.mkdirs();
            }
            copyDocumentTree(rootDoc, targetDataDir);

            Log.i(TAG, "Game files successfully copied and verified.");
            runOnUiThread(() -> {
                progressBar.setProgress(100);
                statusText.setText("GAME FILES VERIFIED & READY!");
                updatePlayButtonState(true);
                Toast.makeText(this, "I.G.I. 2 game files installed successfully!", Toast.LENGTH_LONG).show();
            });
        } catch (Exception e) {
            Log.e(TAG, "Failed to copy game files", e);
            runOnUiThread(() -> {
                statusText.setText("COPY ERROR: " + e.getMessage());
                Toast.makeText(this, "Failed to copy game files: " + e.getMessage(), Toast.LENGTH_SHORT).show();
            });
        }
    }

    // Sums a SAF tree's bytes without copying anything, so the install can show a
    // real determinate progress bar. Metadata-only (listFiles + length), no streams.
    private long countTreeBytes(DocumentFile doc) {
        long total = 0;
        DocumentFile[] files = doc.listFiles();
        for (DocumentFile file : files) {
            if (file.isDirectory()) {
                total += countTreeBytes(file);
            } else if (file.isFile()) {
                total += Math.max(0, file.length());
            }
        }
        return total;
    }

    // Reports copy progress to the UI thread, throttled so the bar is redrawn at
    // most ~7 times a second (the final 100% always goes through).
    private void reportCopyProgress() {
        if (copyTotalBytes <= 0) {
            return;
        }
        long now = SystemClock.uptimeMillis();
        int pct = (int) Math.min(100, (copyDoneBytes * 100L) / copyTotalBytes);
        if (pct < 100 && now - lastProgressUiMs < 150) {
            return;
        }
        lastProgressUiMs = now;
        final int p = pct;
        final long done = copyDoneBytes;
        final long total = copyTotalBytes;
        runOnUiThread(() -> {
            progressBar.setProgress(p);
            statusText.setText("INSTALLING GAME FILES... " + p + "% (" + formatMb(done) + " / " + formatMb(total) + ")");
        });
    }

    private static String formatMb(long bytes) {
        return String.format(Locale.US, "%.0f MB", bytes / (1024.0 * 1024.0));
    }

    private void copyDocumentTree(DocumentFile sourceDoc, File targetDir) throws IOException {
        if (!targetDir.exists()) {
            targetDir.mkdirs();
        }

        DocumentFile[] files = sourceDoc.listFiles();
        for (DocumentFile file : files) {
            if (file.isDirectory()) {
                File subDir = new File(targetDir, file.getName());
                copyDocumentTree(file, subDir);
            } else if (file.isFile()) {
                File targetFile = new File(targetDir, file.getName());
                // Already there with the same size: skip the copy but count it toward
                // progress, so re-selecting the same folder (or resuming) is nearly free.
                if (targetFile.exists() && targetFile.length() == file.length()) {
                    copyDoneBytes += Math.max(0, file.length());
                    reportCopyProgress();
                    continue;
                }
                try (InputStream in = getContentResolver().openInputStream(file.getUri());
                     OutputStream out = new FileOutputStream(targetFile)) {
                    if (in != null) {
                        byte[] buffer = new byte[262144];
                        int read;
                        while ((read = in.read(buffer)) != -1) {
                            out.write(buffer, 0, read);
                            copyDoneBytes += read;
                            reportCopyProgress();
                        }
                    }
                }
            }
        }
    }

    private void onPlayButtonClicked() {
        if (!isGameFolderVerified) {
            Toast.makeText(this, "Please select a valid I.G.I. 2 game folder first.", Toast.LENGTH_SHORT).show();
            return;
        }

        // Start game process immediately so renderer and engine loop do not freeze or wait!
        launchGameNow();
    }

    private void launchGameNow() {
        statusText.setText("STARTING " + ("webgpu".equals(rendererBackend) ? "WEBGPU" : "VULKAN") + " RENDERER...");
        if (pendingHolder != null && !isGameStarted) {
            startGameWithHolder(pendingHolder);
        }
    }

    private void exportLogcatToDownloads() {
        new Thread(() -> {
            try {
                // Sleep 2 seconds to capture full CoreCLR boot, JNI bridge, and Vulkan/GLES renderer logs
                Thread.sleep(2000);

                Process process = Runtime.getRuntime().exec("logcat -d -v time");
                BufferedReader reader = new BufferedReader(new InputStreamReader(process.getInputStream()));
                StringBuilder logBuilder = new StringBuilder();
                String line;
                while ((line = reader.readLine()) != null) {
                    logBuilder.append(line).append("\n");
                }

                String logData = logBuilder.toString();
                boolean exported = false;

                // 1. Try MediaStore.Downloads (Android 10+ / API 29+)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    try {
                        ContentValues values = new ContentValues();
                        values.put(MediaStore.MediaColumns.DISPLAY_NAME, "projectigi_logs.txt");
                        values.put(MediaStore.MediaColumns.MIME_TYPE, "text/plain");
                        values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS);

                        Uri externalUri = MediaStore.Downloads.EXTERNAL_CONTENT_URI;
                        try {
                            getContentResolver().delete(externalUri, MediaStore.MediaColumns.DISPLAY_NAME + "=?", new String[]{"projectigi_logs.txt"});
                        } catch (Exception ignored) {}

                        Uri uri = getContentResolver().insert(externalUri, values);
                        if (uri != null) {
                            try (OutputStream os = getContentResolver().openOutputStream(uri)) {
                                if (os != null) {
                                    os.write(logData.getBytes(StandardCharsets.UTF_8));
                                    exported = true;
                                }
                            }
                        }
                    } catch (Exception e1) {
                        Log.w(TAG, "MediaStore export failed, trying direct file fallback", e1);
                    }
                }

                // 2. Direct File System Fallback (Downloads folder)
                if (!exported) {
                    File downloadsDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS);
                    if (!downloadsDir.exists()) {
                        downloadsDir.mkdirs();
                    }

                    File logFile = new File(downloadsDir, "projectigi_logs.txt");
                    if (logFile.exists()) {
                        logFile.delete();
                    }

                    try (FileOutputStream fos = new FileOutputStream(logFile, false)) {
                        fos.write(logData.getBytes(StandardCharsets.UTF_8));
                        exported = true;
                    }
                }

                Log.i(TAG, "Logcat exported successfully!");
                runOnUiThread(() -> Toast.makeText(this, "Logs saved to Downloads/projectigi_logs.txt", Toast.LENGTH_LONG).show());
            } catch (Exception e) {
                Log.e(TAG, "Logcat export error", e);
                runOnUiThread(() -> Toast.makeText(this, "Failed to export logs: " + e.getMessage(), Toast.LENGTH_SHORT).show());
            }
        }).start();
    }

    private void deleteRecursively(File fileOrDir) {
        if (fileOrDir.isDirectory()) {
            File[] children = fileOrDir.listFiles();
            if (children != null) {
                for (File child : children) {
                    deleteRecursively(child);
                }
            }
        }
        fileOrDir.delete();
    }

    private void copyAssetFolder(AssetManager assetManager, String fromAssetPath, File targetFileOrDir) throws IOException {
        String[] list = assetManager.list(fromAssetPath);
        if (list == null || list.length == 0) {
            if (targetFileOrDir.getParentFile() != null) {
                targetFileOrDir.getParentFile().mkdirs();
            }
            try (InputStream in = assetManager.open(fromAssetPath);
                 OutputStream out = new FileOutputStream(targetFileOrDir)) {
                byte[] buffer = new byte[65536];
                int read;
                while ((read = in.read(buffer)) != -1) {
                    out.write(buffer, 0, read);
                }
            }
        } else {
            if (!targetFileOrDir.exists()) {
                targetFileOrDir.mkdirs();
            }
            for (String item : list) {
                String subFrom = fromAssetPath.isEmpty() ? item : fromAssetPath + "/" + item;
                File subTo = new File(targetFileOrDir, item);
                copyAssetFolder(assetManager, subFrom, subTo);
            }
        }
    }

    private void copySingleAssetSafe(String assetPath, File targetFile) {
        try {
            if (targetFile.getParentFile() != null) {
                targetFile.getParentFile().mkdirs();
            }
            InputStream in = null;
            try {
                in = getAssets().open(assetPath);
            } catch (Exception e1) {
                String alt = assetPath.startsWith("public/") ? assetPath.substring(7) : "public/" + assetPath;
                try {
                    in = getAssets().open(alt);
                } catch (Exception ignored) {}
            }
            if (in == null) return;
            try (InputStream is = in;
                 OutputStream out = new FileOutputStream(targetFile)) {
                byte[] buffer = new byte[65536];
                int read;
                while ((read = is.read(buffer)) != -1) {
                    out.write(buffer, 0, read);
                }
            }
        } catch (Exception ignored) {}
    }

    private synchronized void startGameWithHolder(SurfaceHolder holder) {
        if (isGameStarted) return;
        isGameStarted = true;

        // Hide splash after starting
        runOnUiThread(() -> {
            if (splashLayout != null) {
                splashLayout.setVisibility(View.GONE);
            }
        });

        int width = surfaceView.getWidth();
        int height = surfaceView.getHeight();
        if (width <= 0) width = getResources().getDisplayMetrics().widthPixels;
        if (height <= 0) height = getResources().getDisplayMetrics().heightPixels;

        int landscapeWidth = Math.max(width, height);
        int landscapeHeight = Math.min(width, height);

        String filesDir = getFilesDir().getAbsolutePath();
        String nativeLibDir = getApplicationInfo().nativeLibraryDir;

        Log.i(TAG, "Calling nativeStartGame: " + landscapeWidth + "x" + landscapeHeight);
        boolean started = NativeEngine.nativeStartGame(holder.getSurface(), landscapeWidth, landscapeHeight, filesDir, nativeLibDir, "-" + rendererBackend);
        if (started) {
            startQuitWatcher();
        } else {
            isGameStarted = false;
        }
    }

    // Watches the game thread: if it ends while this Activity is in the foreground, the
    // user pressed Quit in the main menu, so close the app entirely instead of leaving
    // a black SurfaceView behind. Backgrounding also ends the game thread, but then
    // isResumed is false, so this does nothing and the surface-restart path takes over.
    private void startQuitWatcher() {
        if (quitWatcherThread != null && quitWatcherThread.isAlive()) return;
        quitWatcherStopped = false;
        quitWatcherThread = new Thread(() -> {
            boolean sawRunning = false;
            while (!quitWatcherStopped) {
                try {
                    Thread.sleep(500);
                } catch (InterruptedException e) {
                    break;
                }
                boolean running = NativeEngine.nativeIsGameRunning();
                if (running) {
                    sawRunning = true;
                    continue;
                }
                if (sawRunning && isResumed && isGameStarted) {
                    Log.i(TAG, "Game exited in the foreground (Quit): closing the app");
                    gameQuitRequested = true;
                    runOnUiThread(() -> finishAffinity());
                    break;
                }
            }
        });
        quitWatcherThread.start();
    }

    @Override
    public void surfaceCreated(SurfaceHolder holder) {
        Log.i(TAG, "surfaceCreated");
        pendingHolder = holder;
        if (isExtracted && isGameFolderVerified && !isGameStarted) {
            // Holder is ready
        }
        // Returning from the background: the engine survived (it idles with no surface),
        // so hand it the new surface and it resumes in the mission, at the pause menu,
        // instead of restarting to the main menu.
        if (isGameStarted) {
            android.graphics.Rect frame = holder.getSurfaceFrame();
            int w = frame.width() > 0 ? frame.width() : surfaceView.getWidth();
            int h = frame.height() > 0 ? frame.height() : surfaceView.getHeight();
            Log.i(TAG, "surfaceCreated: rebinding live engine to new surface " + w + "x" + h);
            NativeEngine.nativeOnSurfaceRecreated(holder.getSurface(), w, h);
        }
    }

    @Override
    public void surfaceChanged(SurfaceHolder holder, int format, int width, int height) {
        int landscapeWidth = Math.max(width, height);
        int landscapeHeight = Math.min(width, height);
        Log.i(TAG, "surfaceChanged: " + landscapeWidth + "x" + landscapeHeight);
        NativeEngine.nativeOnSurfaceChanged(landscapeWidth, landscapeHeight);
    }

    @Override
    public void surfaceDestroyed(SurfaceHolder holder) {
        Log.i(TAG, "surfaceDestroyed");
        // The surface is gone (app backgrounded) but the engine keeps running: it idles with
        // no surface until a new one arrives, so the player returns to the pause menu in the
        // mission instead of the main menu. isGameStarted stays true.
        NativeEngine.nativeOnSurfaceDestroyed();
        pendingHolder = null;
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        if (!isGameStarted) return false;
        final int actionMasked = event.getActionMasked();
        final int actionIndex = event.getActionIndex();
        int pointerId = event.getPointerId(actionIndex);
        float x = event.getX(actionIndex);
        float y = event.getY(actionIndex);

        if (actionMasked == MotionEvent.ACTION_MOVE) {
            int pointerCount = event.getPointerCount();
            for (int i = 0; i < pointerCount; i++) {
                NativeEngine.nativeOnTouch(MotionEvent.ACTION_MOVE, event.getPointerId(i), event.getX(i), event.getY(i));
            }
        } else {
            NativeEngine.nativeOnTouch(actionMasked, pointerId, x, y);
        }
        return true;
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (!isGameStarted) return super.onKeyDown(keyCode, event);
        if (keyCode == KeyEvent.KEYCODE_BACK) {
            // Consume here without sending: onBackPressed() sends the Back key once.
            // Sending it here too pressed Escape twice, which opened the pause menu
            // and immediately closed it again (or skipped past it).
            return true;
        }
        NativeEngine.nativeOnKey(keyCode, true);
        return super.onKeyDown(keyCode, event);
    }

    @Override
    public boolean onKeyUp(int keyCode, KeyEvent event) {
        if (!isGameStarted) return super.onKeyUp(keyCode, event);
        NativeEngine.nativeOnKey(keyCode, false);
        return super.onKeyUp(keyCode, event);
    }

    @Override
    public void onBackPressed() {
        // If the soft keyboard is showing, the first Back dismisses it (and clears the
        // keyboard-icon request) instead of navigating — standard Android behaviour.
        if (isGameStarted && imeShown) {
            hideSoftKeyboard();
            NativeEngine.nativeSetKeyboardRequested(false);
            return;
        }
        if (isGameStarted) {
            NativeEngine.nativeOnKey(4, true);
            surfaceView.postDelayed(() -> NativeEngine.nativeOnKey(4, false), 50);
        } else {
            super.onBackPressed();
        }
    }

    @Override
    public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        applyImmersiveMode();
    }

    @Override
    protected void onPause() {
        super.onPause();
        isResumed = false;
        if (sensorManager != null && gyroListener != null) {
            sensorManager.unregisterListener(gyroListener);
        }
        lastGyroTimestampNs = 0;
        hideSoftKeyboard();
        // Clear the keyboard-icon request and the hidden field's focus, or the IME
        // poller (or a focused EditText) pops the keyboard up again the moment the
        // app returns to the foreground. The keyboard only comes back when the player
        // taps the keyboard icon again or focuses a text field.
        NativeEngine.nativeSetKeyboardRequested(false);
        if (hiddenInput != null) {
            hiddenInput.clearFocus();
        }
        // The game goes into its paused state when the app leaves the foreground (Home,
        // recents, switching apps): the same Back press that opens the pause menu in-game.
        // In the main menu Back is a no-op, so this is safe there too. The C# side also
        // forces the pause when the surface is gone, which covers a cutscene (Back alone
        // cannot pause there: skipping needs a hold and the pause check refuses while
        // one plays).
        if (isGameStarted) {
            NativeEngine.nativeOnKey(4, true);
            surfaceView.postDelayed(() -> NativeEngine.nativeOnKey(4, false), 50);
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        isResumed = true;
        if (sensorManager != null && gyroSensor != null && gyroListener != null) {
            sensorManager.registerListener(gyroListener, gyroSensor, SensorManager.SENSOR_DELAY_GAME);
        }
    }

    // Gyroscope aim + the soft keyboard for text fields (player-name box).
    private void setupGyroAndKeyboard() {
        sensorManager = (SensorManager) getSystemService(SENSOR_SERVICE);
        if (sensorManager != null) {
            gyroSensor = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE);
        }
        gyroListener = new SensorEventListener() {
            @Override
            public void onSensorChanged(SensorEvent event) {
                if (!isGameStarted || gyroSensor == null) {
                    return;
                }
                if (lastGyroTimestampNs == 0) {
                    lastGyroTimestampNs = event.timestamp;
                    return;
                }
                float dt = (event.timestamp - lastGyroTimestampNs) / 1_000_000_000f;
                lastGyroTimestampNs = event.timestamp;
                if (dt <= 0f || dt > 0.1f) {
                    return;
                }
                // Integrate the rates (rad/s) into per-event angle deltas (radians).
                // In landscape the axes are swapped: rotating around the device's X axis
                // (values[0]) tilts the view left/right, and rotating around the Y axis
                // (values[1]) aims up/down. Both are negated so the view follows the tilt:
                // calibrated from a device report where tilt-down moved the view right,
                // tilt-up moved it left, and tilt-right aimed down (axes swapped, signs
                // backwards). If a direction still feels backwards on a given device,
                // flip the sign of that line only.
                float yawDelta = -event.values[0] * dt;
                float pitchDelta = -event.values[1] * dt;
                NativeEngine.nativeOnGyro(yawDelta, pitchDelta);
            }

            @Override
            public void onAccuracyChanged(Sensor sensor, int accuracy) {
            }
        };

        // Hidden, zero-size EditText the IME types into. The game draws its own UI on
        // a SurfaceView, so there is no Android View for the keyboard to target; this
        // invisible field is the target, and its TextWatcher forwards typed text.
        hiddenInput = new EditText(this);
        // Visible (not INVISIBLE): some keyboards refuse to show for an invisible view.
        // It is 1x1 with no background and no cursor, so nothing is actually drawn.
        hiddenInput.setVisibility(View.VISIBLE);
        hiddenInput.setCursorVisible(false);
        hiddenInput.setFocusable(true);
        hiddenInput.setFocusableInTouchMode(true);
        hiddenInput.setInputType(android.text.InputType.TYPE_CLASS_TEXT
            | android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        hiddenInput.setBackground(null);
        rootLayout.addView(hiddenInput, new FrameLayout.LayoutParams(1, 1));
        final String[] lastText = {""};
        hiddenInput.addTextChangedListener(new TextWatcher() {
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {
            }

            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {
            }

            @Override
            public void afterTextChanged(Editable s) {
                String now = s.toString();
                String prev = lastText[0];
                if (now.length() > prev.length() && now.startsWith(prev)) {
                    // Inserted text: forward only the new characters.
                    NativeEngine.nativeOnText(now.substring(prev.length()));
                } else if (now.length() < prev.length()) {
                    // Deleted text: one Backspace per removed character.
                    int removed = prev.length() - now.length();
                    for (int i = 0; i < removed; i++) {
                        NativeEngine.nativeOnKey(67, true);
                        NativeEngine.nativeOnKey(67, false);
                    }
                }
                lastText[0] = now;
            }
        });

        // Polls whether the game has a text field focused and shows/hides the IME.
        // The game tells us via nativeIsTextFieldFocused(); the IME needs a focused View,
        // which is the hidden EditText above.
        imeHandler = new Handler(Looper.getMainLooper());
        imePoller = new Runnable() {
            @Override
            public void run() {
                updateImeVisibility();
                updateSocialsButtonVisibility();
                if (imeHandler != null) {
                    imeHandler.postDelayed(this, 250);
                }
            }
        };
        imeHandler.post(imePoller);
    }

    private void updateImeVisibility() {
        // Show the keyboard when a text field is focused OR when the player asked for it
        // via the keyboard icon in the main menu (under the cheats gear), so player names
        // and other menu text can be typed even when the automatic focus detection does
        // not fire. The icon toggles the request; Back clears it.
        boolean focused = isGameStarted && (NativeEngine.nativeIsTextFieldFocused()
            || NativeEngine.nativeIsKeyboardRequested());
        InputMethodManager imm = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
        if (focused && !imeShown) {
            if (hiddenInput != null && imm != null) {
                hiddenInput.requestFocus();
                imm.showSoftInput(hiddenInput, 0);
            }
            imeShown = true;
        } else if (!focused && imeShown) {
            hideSoftKeyboard();
        }
    }

    private void hideSoftKeyboard() {
        if (!imeShown) {
            return;
        }
        imeShown = false;
        InputMethodManager imm = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
        if (hiddenInput != null && imm != null) {
            imm.hideSoftInputFromWindow(hiddenInput.getWindowToken(), 0);
            hiddenInput.setText("");
        }
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) {
            applyImmersiveMode();
        }
    }

    @Override
    protected void onDestroy() {
        quitWatcherStopped = true;
        if (quitWatcherThread != null) quitWatcherThread.interrupt();
        if (imeHandler != null && imePoller != null) {
            imeHandler.removeCallbacks(imePoller);
        }
        if (socialsDialog != null && socialsDialog.isShowing()) {
            socialsDialog.dismiss();
        }
        if (sensorManager != null && gyroListener != null) {
            sensorManager.unregisterListener(gyroListener);
        }
        super.onDestroy();
        NativeEngine.nativeShutdown();
        // Genuine Quit only: terminate the process so the next PLAY is a fresh process.
        // CoreCLR is never shut down, so re-entering the runtime in this process crashes
        // on the second launch (WebGPU Crash A, ~23 ms into the second game thread).
        // This runs after the activity is finished and the game thread is joined, and
        // never on backgrounding — the pause-and-resume path needs the process to
        // survive a tab-out. killProcess (SIGKILL) is used over System.exit so no
        // finalizer or shutdown hook runs against native state that is mid-teardown.
        if (gameQuitRequested) {
            Log.i(TAG, "Game quit: terminating process so the next launch is fresh");
            android.os.Process.killProcess(android.os.Process.myPid());
        }
    }

    private void setupSocialsButton() {
        float density = getResources().getDisplayMetrics().density;
        int size = (int)(48 * density);
        socialsButton = new ImageButton(this);
        socialsButton.setImageResource(android.R.drawable.ic_menu_share);
        socialsButton.setColorFilter(Color.rgb(190, 215, 255));
        socialsButton.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
        socialsButton.setPadding((int)(10 * density), (int)(10 * density), (int)(10 * density), (int)(10 * density));
        socialsButton.setContentDescription("Socials");

        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.rgb(20, 35, 60));
        bg.setStroke((int)(1.5f * density), Color.rgb(80, 160, 255));
        bg.setCornerRadius(8 * density);
        socialsButton.setBackground(bg);

        // The main-menu logo fills roughly the top quarter of the screen (see the
        // reference menu screenshot), so the button sits just below it, in the empty
        // left column — top-left as asked, without covering the logo.
        int topMargin = (int)(getResources().getDisplayMetrics().heightPixels * 0.28);
        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(size, size);
        params.gravity = Gravity.TOP | Gravity.START;
        params.topMargin = topMargin;
        params.leftMargin = (int)(12 * density);
        socialsButton.setOnClickListener(v -> showSocialsPopup());
        rootLayout.addView(socialsButton, params);
        socialsButton.setVisibility(View.VISIBLE);
        socialsButtonShown = true;
    }

    private void updateSocialsButtonVisibility() {
        if (socialsButton == null) {
            return;
        }
        // On the file-picker screen while the splash is up; on the main menu once the
        // game is running (MenuMode, no pause, no mission). Hidden in-mission, while
        // loading, on the pause menu, and everywhere else.
        boolean show = splashLayout == null || splashLayout.getVisibility() == View.VISIBLE;
        if (!show && isGameStarted) {
            try {
                show = NativeEngine.nativeIsMainMenu();
            } catch (Throwable ignored) {
                // Native library not loaded yet: stay hidden until the game is up.
                show = false;
            }
        }
        if (show != socialsButtonShown) {
            socialsButtonShown = show;
            socialsButton.setVisibility(show ? View.VISIBLE : View.GONE);
        }
    }

    private void showSocialsPopup() {
        if (socialsDialog != null && socialsDialog.isShowing()) {
            return;
        }
        float density = getResources().getDisplayMetrics().density;

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding((int)(20 * density), (int)(16 * density), (int)(20 * density), (int)(16 * density));

        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.argb(225, 10, 16, 30));
        bg.setStroke((int)(2 * density), Color.rgb(80, 160, 255));
        bg.setCornerRadius(10 * density);
        layout.setBackground(bg);

        // Title row: SOCIALS + close X.
        LinearLayout titleRow = new LinearLayout(this);
        titleRow.setOrientation(LinearLayout.HORIZONTAL);
        titleRow.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = new TextView(this);
        title.setText("SOCIALS");
        title.setTextColor(Color.rgb(80, 160, 255));
        title.setTextSize(16);
        title.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        title.setLetterSpacing(0.12f);
        LinearLayout.LayoutParams titleLp = new LinearLayout.LayoutParams(
            0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        titleRow.addView(title, titleLp);
        TextView close = new TextView(this);
        close.setText("\u2715");
        close.setTextColor(Color.rgb(190, 215, 255));
        close.setTextSize(18);
        close.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        close.setPadding((int)(12 * density), 0, 0, 0);
        close.setOnClickListener(v -> {
            if (socialsDialog != null) socialsDialog.dismiss();
        });
        titleRow.addView(close);
        LinearLayout.LayoutParams titleRowLp = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        titleRowLp.bottomMargin = (int)(16 * density);
        layout.addView(titleRow, titleRowLp);

        layout.addView(makeSocialButton("DISCORD", "discord.gg/hSyz7KWZrg", DISCORD_URL, density));
        layout.addView(makeSocialButton("REDDIT", "u/project_igi_porter", REDDIT_URL, density));

        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        builder.setView(layout);
        builder.setCancelable(true);
        socialsDialog = builder.create();
        // Transparent window so only the themed card shows, not the stock dialog frame.
        if (socialsDialog.getWindow() != null) {
            socialsDialog.getWindow().setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));
        }
        socialsDialog.show();
    }

    private Button makeSocialButton(String name, String handle, final String url, float density) {
        Button button = new Button(this);
        button.setText(name + "\n" + handle);
        button.setTextSize(12);
        button.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        button.setTextColor(Color.rgb(190, 215, 255));
        button.setGravity(Gravity.CENTER);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.rgb(20, 35, 60));
        bg.setStroke((int)(1.5f * density), Color.rgb(80, 160, 255));
        bg.setCornerRadius(6 * density);
        button.setBackground(bg);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
            (int)(280 * density), LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.bottomMargin = (int)(10 * density);
        button.setOnClickListener(v -> openSocialUrl(url));
        button.setLayoutParams(lp);
        return button;
    }

    private void openSocialUrl(String url) {
        if (socialsDialog != null && socialsDialog.isShowing()) {
            socialsDialog.dismiss();
        }
        try {
            // ACTION_VIEW lets Android pick the installed app (Discord / Reddit) when
            // present and falls back to the browser otherwise.
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
        } catch (ActivityNotFoundException e) {
            Log.w(TAG, "No app can open " + url, e);
            Toast.makeText(this, "No app can open this link", Toast.LENGTH_SHORT).show();
        }
    }

    private void applyImmersiveMode() {
        View decorView = getWindow().getDecorView();
        if (decorView == null) return;

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            try {
                WindowInsetsController controller = decorView.getWindowInsetsController();
                if (controller != null) {
                    controller.hide(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
                    controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
                }
            } catch (Throwable ignored) {}
        } else {
            decorView.setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                | View.SYSTEM_UI_FLAG_FULLSCREEN);
        }
    }
}
