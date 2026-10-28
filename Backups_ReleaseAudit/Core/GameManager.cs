using UnityEngine;
using UnityEngine.SceneManagement;
using VectorParkour;

namespace Vector3D.Core
{
    public enum GameState
    {
        WaitingToStart,
        Playing,
        GameOver
    }

    /// <summary>
    /// Centralized Game Manager controlling the 3 main game states:
    /// WaitingToStart, Playing, and GameOver.
    /// Coordinates multi-level transitions, same-scene restarts, Time.timeScale, and game flow.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Object.FindFirstObjectByType<GameManager>();
                }
                return _instance;
            }
            private set => _instance = value;
        }
        private static GameManager _instance;

        [SerializeField] private GameState _initialState = GameState.WaitingToStart;
        [SerializeField] private int _currentLevelIndex = 1;

        [Header("Level Roots for Isolation")]
        [SerializeField] private GameObject _level1Root;
        [SerializeField] private GameObject _level2Root;
        [SerializeField] private GameObject _level3Root;
        [SerializeField] private GameObject _level4Root;

        public GameState CurrentState { get; private set; } = GameState.WaitingToStart;
        public int CurrentLevelIndex => _currentLevelIndex;
        public bool IsGameWon { get; private set; } = false;

        public event System.Action<GameState> OnStateChanged;
        public event System.Action<bool> OnGameOver;
        public event System.Action<int> OnLevelChanged;

        // Centralized Spawns
        public static readonly Vector3 Level1PlayerSpawn = new Vector3(0f, 1.1f, 0f);
        public static readonly Vector3 Level1ChaserSpawn = new Vector3(0f, 1.1f, -6f);
        public static readonly Vector3 Level2PlayerSpawn = new Vector3(100f, 1.1f, 0f);
        public static readonly Vector3 Level2ChaserSpawn = new Vector3(100f, 1.1f, -6f);
        public static readonly Vector3 Level3PlayerSpawn = new Vector3(200f, 1.1f, 0f);
        public static readonly Vector3 Level3ChaserSpawn = new Vector3(200f, 1.1f, -6f);
        public static readonly Vector3 Level4PlayerSpawn = new Vector3(300f, 1.1f, 0f);
        public static readonly Vector3 Level4ChaserSpawn = new Vector3(300f, 1.1f, -6f);

        private Vector3 GetPlayerSpawnForLevel(int level) => level switch
        {
            1 => Level1PlayerSpawn,
            2 => Level2PlayerSpawn,
            3 => Level3PlayerSpawn,
            4 => Level4PlayerSpawn,
            _ => Level1PlayerSpawn
        };

        private Vector3 GetChaserSpawnForLevel(int level) => level switch
        {
            1 => Level1ChaserSpawn,
            2 => Level2ChaserSpawn,
            3 => Level3ChaserSpawn,
            4 => Level4ChaserSpawn,
            _ => Level1ChaserSpawn
        };

        private bool _isTransitioning = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            EnsureLevelRoots();
            UpdateLevelVisibility();
        }

        private void Start()
        {
            _currentLevelIndex = 1;
            EnsureLevelRoots();
            UpdateLevelVisibility();
            SetState(_initialState);
        }

        public void SetState(GameState newState)
        {
            CurrentState = newState;

            switch (newState)
            {
                case GameState.WaitingToStart:
                    Time.timeScale = 0f;
                    break;
                case GameState.Playing:
                    Time.timeScale = 1f;
                    break;
                case GameState.GameOver:
                    Time.timeScale = 0f;
                    break;
            }

            OnStateChanged?.Invoke(CurrentState);
        }

        /// <summary>
        /// Transitions to Playing state, resumes time, and lets player and chaser move.
        /// </summary>
        public void StartGame()
        {
            SetState(GameState.Playing);
        }

        /// <summary>
        /// Transitions to GameOver state, halts time, and triggers end UI.
        /// </summary>
        public void TriggerGameOver(bool won = false)
        {
            if (CurrentState == GameState.GameOver) return;

            IsGameWon = won;
            SetState(GameState.GameOver);
            OnGameOver?.Invoke(won);

            if (!won)
            {
                AudioManager.Instance?.PlayDeathSound();
            }
        }

        /// <summary>
        /// Selects and switches directly to a specific level (Level 1, 2, or 3).
        /// Teleports Player, Chaser, snaps camera, resets finish lines, updates level visibility, and updates game state.
        /// </summary>
        public void SelectLevel(int levelIndex, bool startPlaying = true)
        {
            _currentLevelIndex = Mathf.Clamp(levelIndex, 1, 4);
            UpdateLevelVisibility();

            Vector3 playerSpawn = GetPlayerSpawnForLevel(_currentLevelIndex);
            Vector3 chaserSpawn = GetChaserSpawnForLevel(_currentLevelIndex);

            PlayerParkourController player = Object.FindFirstObjectByType<PlayerParkourController>();
            ChaserAI chaser = Object.FindFirstObjectByType<ChaserAI>();
            VectorFollowCamera cam = Object.FindFirstObjectByType<VectorFollowCamera>();

            if (player != null)
            {
                player.TeleportToStart(playerSpawn);
            }

            if (chaser != null)
            {
                chaser.TeleportToStart(chaserSpawn);
            }

            if (cam != null)
            {
                cam.SnapToTarget();
            }

            ResetAllFinishLines();

            IsGameWon = false;
            OnLevelChanged?.Invoke(_currentLevelIndex);

            if (startPlaying)
            {
                SetState(GameState.Playing);
            }
            else
            {
                SetState(GameState.WaitingToStart);
            }
        }

        /// <summary>
        /// Transitions the game to the next level (1 -> 2 -> 3) in the same scene.
        /// Teleports Player, Chaser, snaps camera, updates track center, activates the next level, and resumes gameplay.
        /// </summary>
        public void StartNextLevel()
        {
            if (_isTransitioning) return;
            if (!IsGameWon || _currentLevelIndex >= 4) return;

            _isTransitioning = true;
            _currentLevelIndex++;
            UpdateLevelVisibility();

            Vector3 playerSpawn = GetPlayerSpawnForLevel(_currentLevelIndex);
            Vector3 chaserSpawn = GetChaserSpawnForLevel(_currentLevelIndex);

            PlayerParkourController player = Object.FindFirstObjectByType<PlayerParkourController>();
            ChaserAI chaser = Object.FindFirstObjectByType<ChaserAI>();
            VectorFollowCamera cam = Object.FindFirstObjectByType<VectorFollowCamera>();

            if (player != null)
            {
                player.TeleportToStart(playerSpawn);
            }

            if (chaser != null)
            {
                chaser.TeleportToStart(chaserSpawn);
            }

            if (cam != null)
            {
                cam.SnapToTarget();
            }

            ResetAllFinishLines();

            IsGameWon = false;
            _isTransitioning = false;

            OnLevelChanged?.Invoke(_currentLevelIndex);
            SetState(GameState.Playing);
        }

        /// <summary>
        /// Resets the current active level in the same scene without reloading the scene.
        /// Preserves the active level index (_currentLevelIndex) and enforces level isolation.
        /// </summary>
        public void RestartGame()
        {
            UpdateLevelVisibility();

            Vector3 playerSpawn = GetPlayerSpawnForLevel(_currentLevelIndex);
            Vector3 chaserSpawn = GetChaserSpawnForLevel(_currentLevelIndex);

            PlayerParkourController player = Object.FindFirstObjectByType<PlayerParkourController>();
            ChaserAI chaser = Object.FindFirstObjectByType<ChaserAI>();
            VectorFollowCamera cam = Object.FindFirstObjectByType<VectorFollowCamera>();

            if (player != null)
            {
                player.TeleportToStart(playerSpawn);
            }

            if (chaser != null)
            {
                chaser.TeleportToStart(chaserSpawn);
            }

            if (cam != null)
            {
                cam.SnapToTarget();
            }

            ResetAllFinishLines();

            IsGameWon = false;
            SetState(GameState.Playing);
        }

        /// <summary>
        /// Ensures only the active level's root hierarchy is enabled,
        /// completely hiding other levels and preventing rendering or physics bleed.
        /// </summary>
        public void UpdateLevelVisibility()
        {
            EnsureLevelRoots();

            if (_level1Root != null)
            {
                _level1Root.SetActive(_currentLevelIndex == 1);
            }
            if (_level2Root != null)
            {
                _level2Root.SetActive(_currentLevelIndex == 2);
            }
            if (_level3Root != null)
            {
                _level3Root.SetActive(_currentLevelIndex == 3);
            }
            if (_level4Root != null)
            {
                _level4Root.SetActive(_currentLevelIndex == 4);
            }
        }

        private void EnsureLevelRoots()
        {
            if (_level1Root == null)
            {
                GameObject go = GameObject.Find("ParkourLevel/Level 1");
                if (go == null) go = GameObject.Find("Level 1");
                _level1Root = go;
            }

            if (_level2Root == null)
            {
                GameObject go = GameObject.Find("ParkourLevel/Level 2");
                if (go == null) go = GameObject.Find("Level 2");
                _level2Root = go;
            }

            if (_level3Root == null)
            {
                GameObject go = GameObject.Find("ParkourLevel/Level 3");
                if (go == null) go = GameObject.Find("Level 3");
                _level3Root = go;
            }

            if (_level4Root == null)
            {
                GameObject go = GameObject.Find("ParkourLevel/Level 4");
                if (go == null) go = GameObject.Find("Level 4");
                _level4Root = go;
            }
        }

        private void ResetAllFinishLines()
        {
            FinishLine[] finishLines = Object.FindObjectsByType<FinishLine>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var fl in finishLines)
            {
                fl.ResetFinishLine();
            }
        }

        /// <summary>
        /// Exits play mode in Editor or terminates application in standalone builds.
        /// </summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
