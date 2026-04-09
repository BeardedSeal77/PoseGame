using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Central manager for global game settings, state, and shared references.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { MainMenu, Playing, GameOver }
    public GameState CurrentState { get; private set; } = GameState.MainMenu;

    [Header("Global References")]
    [Tooltip("Master reference to the player object.")]
    public Transform playerTransform;

    [Tooltip("The number of ground tiles to spawn ahead of the player.")]
    [Range(5, 50)]
    public int renderDistance = 25;

    [Header("Testing & Gameplay Settings")]
    [Tooltip("If true, the player will phase through obstacles.")]
    public bool isGhost = false;

    [Tooltip("Global speed multiplier for the game environment.")]
    [Range(0.1f, 5f)]
    public float gameSpeed = 1.0f;

    [Tooltip("Amount of lives the player starts with.")]
    public int startingLives = 3;
    public int CurrentLives { get; private set; }

    [Header("Speed Settings")]
    public float baseSpeed = 10f;
    public float currentSpeed;
    public float maxSpeed = 30f;

    [Tooltip("How much the speed increases per second")]
    public float speedIncreaseRate = 0.25f;

    [Tooltip("How much speed is lost when the player takes damage")]
    public float speedPenaltyOnLifeLost = 5f;

    [Header("Score Settings")]
    [Tooltip("Score awarded for successfully clearing a wall.")]
    public int wallClearScore = 100;

    [Tooltip("Extra score awarded for each orb touched while clearing a wall.")]
    public int orbScore = 25;

    public int Score { get; private set; }
    public int ClearedWalls { get; private set; }
    public int CollectedOrbs { get; private set; }

    [Header("UI Panels")]
    public GameObject hudScreen;
    public GameObject startScreen;
    public GameObject gameOverScreen;
    public GameObject settingsScreen;

    [Header("UI Text")]
    public TMP_Text hudText;
    public TMP_Text startPromptText;
    public TMP_Text gameOverSummaryText;

    [Header("HUD Counters (individual — preferred over hudText)")]
    [Tooltip("Parent panel/container for the lives counter (e.g. a RawImage).")]
    public GameObject livesPanel;
    [Tooltip("Displays lives with a heart icon, e.g. ♥ 3. Top-left.")]
    public TMP_Text livesCounterText;
    [Tooltip("Parent panel/container for the score counter (e.g. a RawImage).")]
    public GameObject scorePanel;
    [Tooltip("Displays the current score. Top-right.")]
    public TMP_Text scoreCounterText;
    [Tooltip("Final score shown on the game-over screen.")]
    public TMP_Text finalScoreText;

    [Header("UI Buttons (optional — assign for button-driven flow)")]
    [Tooltip("Button on the start screen that begins the game.")]
    public Button startButton;
    [Tooltip("Button on the game-over screen that restarts.")]
    public Button restartButton;
    [Tooltip("Button on the game-over screen that quits.")]
    public Button quitButton;

    [Header("Input Mode")]
    [Tooltip("When true, start/restart require a button click. When false, any key works (legacy).")]
    public bool useButtonInput = true;

    [Header("Gameplay")]
    [Tooltip("Seconds to wait after pressing Start before the first wall spawns.")]
    public float startDelay = 3f;

    [Header("Scene Names")]
    public string mainMenuSceneName = "StartScene";
    public string gameplaySceneName = "GameScene";
    public string gameOverSceneName = "EndScene";

    private bool wasPlayingBeforeSettings;

    private void Awake()
    {
        // Enforce Singleton
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        EnsureRuntimeUi(true);
        EnsureEventSystem();
        BindButtons();

        if (playerTransform == null)
            Debug.LogError("GameManager: Player Transform is not assigned!");

        // --- Diagnostic logs ---
        Debug.Log($"[GameManager] startScreen: {(startScreen != null ? startScreen.name : "NULL")}");
        Debug.Log($"[GameManager] gameOverScreen: {(gameOverScreen != null ? gameOverScreen.name : "NULL")}");
        Debug.Log($"[GameManager] startButton: {(startButton != null ? startButton.name : "NULL")}");
        Debug.Log($"[GameManager] restartButton: {(restartButton != null ? restartButton.name : "NULL")}");

        // Initialize lives and speed
        ResetRunState();

        // Always start on the menu; clicking Start begins gameplay.
        if (startScreen != null)
            ShowMainMenu();
        else
            BeginGameplay();
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Debug.Log("[GameManager] Created missing EventSystem.");
        }
    }

    private void BindButtons()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(StartGame);
            Debug.Log($"[GameManager] Wired StartGame to '{startButton.name}'");
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(RestartGame);
            Debug.Log($"[GameManager] Wired RestartGame to '{restartButton.name}'");
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(QuitGame);
            Debug.Log($"[GameManager] Wired QuitGame to '{quitButton.name}'");
        }
    }

    private void Update()
    {
        // Legacy any-key input (skipped when useButtonInput is true)
        if (!useButtonInput)
        {
            if (CurrentState == GameState.MainMenu && Input.anyKeyDown)
            {
                StartGame();
                return;
            }

            if (CurrentState == GameState.GameOver && Input.anyKeyDown)
            {
                RestartGame();
            }
        }

        if (CurrentState == GameState.Playing)
        {
            // Gradually increase the speed over time, clamping it at maxSpeed
            if (currentSpeed < maxSpeed)
            {
                // --- SMART CALCULATION ---
                // Ratio of starting lives to current lives. 
                // Mathf.Max(1, CurrentLives) ensures we never accidentally divide by zero if lives hit 0.
                float lifeMultiplier = (float)startingLives / Mathf.Max(1, CurrentLives);

                // Calculate the exact rate for this frame
                float dynamicIncreaseRate = speedIncreaseRate * lifeMultiplier;

                // Apply the increase
                currentSpeed += dynamicIncreaseRate * gameSpeed * Time.deltaTime;
                currentSpeed = Mathf.Min(currentSpeed, maxSpeed);
            }

            RefreshHud();
        }
    }

    public void ShowMainMenu()
    {
        CurrentState = GameState.MainMenu;
        wasPlayingBeforeSettings = false;

        if (hudScreen != null) hudScreen.SetActive(false);
        if (startScreen != null) startScreen.SetActive(true);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);
        if (settingsScreen != null) settingsScreen.SetActive(false);
        SetHudCountersVisible(false);

        // Freeze gameplay while the menu is up
        Time.timeScale = 0f;
        RefreshMenuText();
    }

    public void StartGame()
    {
        ResetRunState();
        BeginGameplay();
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        ResetRunState();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void RegisterWallResult(bool passed, int orbHits, int orbTotal)
    {
        if (CurrentState != GameState.Playing)
        {
            return;
        }

        orbTotal = Mathf.Max(0, orbTotal);
        orbHits = Mathf.Clamp(orbHits, 0, orbTotal);

        if (!passed)
        {
            return;
        }

        ClearedWalls += 1;
        CollectedOrbs += orbHits;
        Score += wallClearScore + (orbHits * orbScore);

        Debug.Log($"Wall cleared. Score: {Score}, Orbs: {orbHits}/{orbTotal}, Walls: {ClearedWalls}");
        RefreshHud();
        RefreshMenuText();
    }

    public void LoseLife()
    {
        if (CurrentState == GameState.GameOver || CurrentLives <= 0)
        {
            return;
        }

        // Ghost mode: skip life loss (flash still plays via the wall controller)
        if (isGhost)
        {
            Debug.Log("[GameManager] Ghost mode — life loss skipped.");
            return;
        }

        CurrentLives = Mathf.Max(0, CurrentLives - 1);
        Debug.Log("Lost a life! Lives remaining: " + CurrentLives);

        // Reduce the speed, but don't let it drop below the base starting speed
        currentSpeed -= speedPenaltyOnLifeLost;
        currentSpeed = Mathf.Max(currentSpeed, baseSpeed);

        if (CurrentLives <= 0)
        {
            TriggerGameOver();
            return;
        }

        RefreshHud();
    }

    public void TriggerGameOver()
    {
        if (CurrentState == GameState.GameOver) return;

        CurrentState = GameState.GameOver;
        StopAllCoroutines();

        // Let the wall finish its flash animation before showing the game-over screen.
        // This avoids the red flash getting stuck on screen.
        StartCoroutine(GameOverAfterWallFinishes());
    }

    private IEnumerator GameOverAfterWallFinishes()
    {
        var wallController = FindFirstObjectByType<ScreenWallFitController>();

        // Wait for the wall to return to Idle (flash fades out naturally)
        if (wallController != null)
        {
            while (!wallController.IsIdle)
                yield return null;
        }

        Time.timeScale = 0f;

        if (hudScreen != null) hudScreen.SetActive(false);
        if (startScreen != null) startScreen.SetActive(false);
        if (settingsScreen != null) settingsScreen.SetActive(false);
        if (gameOverScreen != null) gameOverScreen.SetActive(true);
        SetHudCountersVisible(false);
        RefreshMenuText();
    }

    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void OpenSettings()
    {
        wasPlayingBeforeSettings = CurrentState == GameState.Playing;

        if (startScreen != null) startScreen.SetActive(false);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);
        if (settingsScreen != null) settingsScreen.SetActive(true);
        if (hudScreen != null) hudScreen.SetActive(false);

        Time.timeScale = 0f;
    }

    public void CloseSettings()
    {
        if (settingsScreen != null) settingsScreen.SetActive(false);

        if (wasPlayingBeforeSettings)
        {
            BeginGameplay();
        }
        else
        {
            ShowMainMenu();
        }
    }

    public void SetGameSpeed(float value)
    {
        gameSpeed = Mathf.Clamp(value, 0.1f, 5f);
    }

    public void SetGhostMode(bool enabled)
    {
        isGhost = enabled;
    }

    public void SetRenderDistance(float value)
    {
        renderDistance = Mathf.RoundToInt(value);
    }

    public void SetStartingLives(float value)
    {
        startingLives = Mathf.Clamp(Mathf.RoundToInt(value), 1, 10);
        CurrentLives = startingLives;
    }

    private void ResetRunState()
    {
        CurrentLives = startingLives;
        currentSpeed = baseSpeed;
        Score = 0;
        ClearedWalls = 0;
        CollectedOrbs = 0;
        RefreshHud();
        RefreshMenuText();
    }

    private void BeginGameplay()
    {
        CurrentState = GameState.Playing;
        Time.timeScale = 1f;

        if (hudScreen != null) hudScreen.SetActive(true);
        if (startScreen != null) startScreen.SetActive(false);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);
        if (settingsScreen != null) settingsScreen.SetActive(false);
        SetHudCountersVisible(true);

        // Clear any leftover wall, then spawn a fresh one after a delay
        var wallController = FindFirstObjectByType<ScreenWallFitController>();
        if (wallController != null)
        {
            wallController.StopAndClear();
            StartCoroutine(DelayedWallStart(wallController, startDelay));
        }

        RefreshHud();
    }

    private IEnumerator DelayedWallStart(ScreenWallFitController wallController, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (CurrentState == GameState.Playing && wallController != null)
            wallController.BeginWall();
    }

    private void SetHudCountersVisible(bool visible)
    {
        if (livesPanel != null)
            livesPanel.SetActive(visible);
        else if (livesCounterText != null)
            livesCounterText.gameObject.SetActive(visible);

        if (scorePanel != null)
            scorePanel.SetActive(visible);
        else if (scoreCounterText != null)
            scoreCounterText.gameObject.SetActive(visible);
    }

    // ----------------------------------------------------------------
    //  Public scoring API — call from any script to award points
    // ----------------------------------------------------------------

    /// <summary>
    /// Add an arbitrary amount of score. Use this to hook up any future
    /// scoring source (e.g. time survived, combos, pickups).
    /// </summary>
    public void AddScore(int amount)
    {
        if (CurrentState != GameState.Playing || amount <= 0) return;
        Score += amount;
        RefreshHud();
    }

    // ----------------------------------------------------------------
    //  HUD refresh
    // ----------------------------------------------------------------

    private void RefreshHud()
    {
        // Individual counters (preferred)
        if (livesCounterText != null)
            livesCounterText.text = $"\u2665 {CurrentLives}";

        if (scoreCounterText != null)
            scoreCounterText.text = $"Score: {Score}";

        // Legacy combined HUD text
        if (hudText != null)
            hudText.text = $"Score {Score}\nLives {CurrentLives}\nWalls {ClearedWalls}\nOrbs {CollectedOrbs}";
    }

    private void RefreshMenuText()
    {
        if (startPromptText != null)
            startPromptText.text = "POSE GAME";

        if (gameOverSummaryText != null)
            gameOverSummaryText.text = $"GAME OVER\nScore: {Score}\nWalls: {ClearedWalls}\nOrbs: {CollectedOrbs}";

        if (finalScoreText != null)
            finalScoreText.text = $"Score: {Score}";
    }

    private void EnsureRuntimeUi(bool allowCreate)
    {
        // Find the canvas so we can search inactive children (GameObject.Find skips them).
        Canvas canvas = FindFirstCanvas();
        Transform canvasRoot = canvas != null ? canvas.transform : null;

        hudScreen = hudScreen != null ? hudScreen : FindChild(canvasRoot, "HUD");
        startScreen = startScreen != null ? startScreen : FindChild(canvasRoot, "StartScreen");
        gameOverScreen = gameOverScreen != null ? gameOverScreen : FindChild(canvasRoot, "GameOverScreen");
        settingsScreen = settingsScreen != null ? settingsScreen : FindChild(canvasRoot, "SettingsScreen");

        hudText = hudText != null ? hudText : FindChildText(canvasRoot, "HUDText");
        startPromptText = startPromptText != null ? startPromptText : FindChildText(canvasRoot, "StartText");
        gameOverSummaryText = gameOverSummaryText != null ? gameOverSummaryText : FindChildText(canvasRoot, "GameOverText");
        livesCounterText = livesCounterText != null ? livesCounterText : FindChildText(canvasRoot, "LivesCounter");
        scoreCounterText = scoreCounterText != null ? scoreCounterText : FindChildText(canvasRoot, "ScoreCounter");
        finalScoreText = finalScoreText != null ? finalScoreText : FindChildText(canvasRoot, "FinalScoreText");

        // Auto-find buttons if not assigned
        if (startButton == null)   startButton   = FindChildButton(canvasRoot, "StartButton");
        if (restartButton == null)  restartButton  = FindChildButton(canvasRoot, "RestartButton");
        if (quitButton == null)    quitButton    = FindChildButton(canvasRoot, "QuitButton");

        if (!allowCreate)
        {
            return;
        }

        if (hudScreen != null && startScreen != null && gameOverScreen != null)
        {
            return;
        }

        // Don't auto-create runtime UI — panels should be set up manually on the Canvas.
        Debug.LogWarning("[GameManager] Some UI panels are not assigned. " +
            $"hudScreen={hudScreen != null}, startScreen={startScreen != null}, " +
            $"gameOverScreen={gameOverScreen != null}, settingsScreen={settingsScreen != null}");
    }

    private static Canvas FindFirstCanvas()
    {
        // FindObjectsByType includes inactive objects when using FindObjectsInactive.Include
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas)
                return canvas;
        }
        return null;
    }

    /// <summary>Recursive search that works on inactive children.</summary>
    private static GameObject FindChild(Transform root, string childName)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName) return t.gameObject;
        }
        return null;
    }

    private static TMP_Text FindChildText(Transform root, string childName)
    {
        GameObject go = FindChild(root, childName);
        return go != null ? go.GetComponent<TMP_Text>() : null;
    }

    private static Button FindChildButton(Transform root, string childName)
    {
        GameObject go = FindChild(root, childName);
        return go != null ? go.GetComponent<Button>() : null;
    }
}
