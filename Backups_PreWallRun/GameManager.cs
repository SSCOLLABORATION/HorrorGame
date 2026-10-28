using UnityEngine;
using UnityEngine.SceneManagement;

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
    /// Manages Time.timeScale, scene reloading, and game flow.
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

        public GameState CurrentState { get; private set; } = GameState.WaitingToStart;
        public bool IsGameWon { get; private set; } = false;

        public event System.Action<GameState> OnStateChanged;
        public event System.Action<bool> OnGameOver;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
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
        }

        /// <summary>
        /// Resets time scale and reloads the current active scene.
        /// </summary>
        public void RestartGame()
        {
            Time.timeScale = 1f;
            int buildIndex = SceneManager.GetActiveScene().buildIndex;
            if (buildIndex >= 0)
            {
                SceneManager.LoadScene(buildIndex);
            }
            else
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
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
