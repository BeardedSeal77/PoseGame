using UnityEngine;
using UnityEngine.SceneManagement;

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

    [Header("UI Panels")]
    public GameObject startScreen;
    public GameObject gameOverScreen;
    public GameObject settingsScreen;

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
        string activeSceneName = SceneManager.GetActiveScene().name;

        if (activeSceneName == gameplaySceneName && playerTransform == null)
        {
            Debug.LogError("GameManager: Player Transform is not assigned in the gameplay scene!");
        }

        // Initialize lives and speed
        CurrentLives = startingLives;
        currentSpeed = baseSpeed;

        // Set the correct state for the scene that is currently open.
        if (activeSceneName == gameplaySceneName)
        {
            CurrentState = GameState.Playing;
            if (startScreen != null) startScreen.SetActive(false);
            if (gameOverScreen != null) gameOverScreen.SetActive(false);
            if (settingsScreen != null) settingsScreen.SetActive(false);
            Time.timeScale = 1f;
        }
        else if (activeSceneName == gameOverSceneName)
        {
            CurrentState = GameState.GameOver;
            if (startScreen != null) startScreen.SetActive(false);
            if (gameOverScreen != null) gameOverScreen.SetActive(true);
            if (settingsScreen != null) settingsScreen.SetActive(false);
            Time.timeScale = 0f;
        }
        else
        {
            ShowMainMenu();
        }
    }

    private void Update()
    {
        // If game over, wait for any key press to return to the start scene
        if (CurrentState == GameState.GameOver && Input.anyKeyDown)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else if (CurrentState == GameState.Playing)
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
        }
    }

    public void ShowMainMenu()
    {
        CurrentState = GameState.MainMenu;
        wasPlayingBeforeSettings = false;

        if (startScreen != null) startScreen.SetActive(true);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);
        if (settingsScreen != null) settingsScreen.SetActive(false);

        // Freeze gameplay while the menu is up
        Time.timeScale = 0f;
    }

    public void StartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void LoseLife()
    {
        if (CurrentState == GameState.GameOver || CurrentLives <= 0)
        {
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
        }
    }

    public void TriggerGameOver()
    {
        if (CurrentState == GameState.GameOver) return;

        CurrentState = GameState.GameOver;
        Time.timeScale = 0f;

        if (!string.IsNullOrWhiteSpace(gameOverSceneName) &&
            SceneManager.GetActiveScene().name != gameOverSceneName)
        {
            SceneManager.LoadScene(gameOverSceneName);
            return;
        }

        if (gameOverScreen != null) gameOverScreen.SetActive(true);
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

        Time.timeScale = 0f;
    }

    public void CloseSettings()
    {
        if (settingsScreen != null) settingsScreen.SetActive(false);

        if (wasPlayingBeforeSettings)
        {
            CurrentState = GameState.Playing;
            Time.timeScale = 1f;
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
}
