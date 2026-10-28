using UnityEngine;
using UnityEngine.UI;
using Vector3D.Core;

namespace Vector3D.UI
{
    /// <summary>
    /// Manages the UI overlay screens:
    /// - Start Screen (Play, Level Select, Quit)
    /// - Level Select Screen (Sector 1, Sector 2, Back)
    /// - End Game Screen (Next Level, Level Select, Restart, Quit)
    /// Subscribes to GameManager state updates, plays UI audio feedback,
    /// and routes button clicks to GameManager actions.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject _startScreenPanel;
        [SerializeField] private GameObject _endScreenPanel;
        [SerializeField] private GameObject _levelSelectPanel;

        [Header("Main Menu Buttons")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _levelSelectButton;
        [SerializeField] private Button _startQuitButton;

        [Header("Level Select Buttons")]
        [SerializeField] private Button _level1Button;
        [SerializeField] private Button _level2Button;
        [SerializeField] private Button _level3Button;
        [SerializeField] private Button _levelSelectBackButton;

        [Header("End Game Buttons")]
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _nextLevelButton;
        [SerializeField] private Button _endLevelSelectButton;
        [SerializeField] private Button _endQuitButton;

        [Header("Status Feedback")]
        [SerializeField] private Text _statusHeaderText;
        [SerializeField] private Text _statusSubText;
        [SerializeField] private Text _currentLevelLabel;

        [Header("Audio Feedback")]
        [SerializeField] private AudioSource _fallbackSfxSource;
        [SerializeField] private AudioClip _buttonClickClip;

        private bool _cameFromGameOver = false;

        private void Awake()
        {
            if (_playButton != null)
            {
                _playButton.onClick.RemoveAllListeners();
                _playButton.onClick.AddListener(OnPlayClicked);
            }

            if (_levelSelectButton != null)
            {
                _levelSelectButton.onClick.RemoveAllListeners();
                _levelSelectButton.onClick.AddListener(OnLevelSelectClicked);
            }

            if (_startQuitButton != null)
            {
                _startQuitButton.onClick.RemoveAllListeners();
                _startQuitButton.onClick.AddListener(OnQuitClicked);
            }

            if (_level1Button != null)
            {
                _level1Button.onClick.RemoveAllListeners();
                _level1Button.onClick.AddListener(() => OnLevelChosen(1));
            }

            if (_level2Button != null)
            {
                _level2Button.onClick.RemoveAllListeners();
                _level2Button.onClick.AddListener(() => OnLevelChosen(2));
            }

            if (_level3Button != null)
            {
                _level3Button.onClick.RemoveAllListeners();
                _level3Button.onClick.AddListener(() => OnLevelChosen(3));
            }

            if (_levelSelectBackButton != null)
            {
                _levelSelectBackButton.onClick.RemoveAllListeners();
                _levelSelectBackButton.onClick.AddListener(OnLevelSelectBackClicked);
            }

            if (_restartButton != null)
            {
                _restartButton.onClick.RemoveAllListeners();
                _restartButton.onClick.AddListener(OnRestartClicked);
            }

            if (_nextLevelButton != null)
            {
                _nextLevelButton.onClick.RemoveAllListeners();
                _nextLevelButton.onClick.AddListener(OnNextLevelClicked);
            }

            if (_endLevelSelectButton != null)
            {
                _endLevelSelectButton.onClick.RemoveAllListeners();
                _endLevelSelectButton.onClick.AddListener(OnEndLevelSelectClicked);
            }

            if (_endQuitButton != null)
            {
                _endQuitButton.onClick.RemoveAllListeners();
                _endQuitButton.onClick.AddListener(OnQuitClicked);
            }
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged += HandleStateChanged;
                GameManager.Instance.OnGameOver += HandleGameOver;
                GameManager.Instance.OnLevelChanged += HandleLevelChanged;
                UpdateUI(GameManager.Instance.CurrentState);
            }
            else
            {
                UpdateUI(GameState.WaitingToStart);
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= HandleStateChanged;
                GameManager.Instance.OnGameOver -= HandleGameOver;
                GameManager.Instance.OnLevelChanged -= HandleLevelChanged;
            }
        }

        private void HandleStateChanged(GameState state)
        {
            UpdateUI(state);
        }

        private void HandleLevelChanged(int levelIndex)
        {
            UpdateCurrentLevelLabel(levelIndex);
        }

        private void UpdateCurrentLevelLabel(int levelIndex)
        {
            if (_currentLevelLabel != null)
            {
                _currentLevelLabel.text = $"ACTIVE SECTOR: 0{levelIndex}";
            }
        }

        private void HandleGameOver(bool won)
        {
            int currentLevel = GameManager.Instance != null ? GameManager.Instance.CurrentLevelIndex : 1;

            if (_endLevelSelectButton != null)
            {
                _endLevelSelectButton.gameObject.SetActive(true);
            }

            if (won)
            {
                if (currentLevel == 1)
                {
                    if (_statusHeaderText != null)
                    {
                        _statusHeaderText.text = "LEVEL 1 CLEARED!";
                        _statusHeaderText.color = new Color(0.2f, 0.9f, 0.3f, 1f);
                    }
                    if (_statusSubText != null)
                    {
                        _statusSubText.text = "Rooftops conquered. Prepare for Sector 2 wall runs.";
                    }
                    if (_nextLevelButton != null)
                    {
                        _nextLevelButton.gameObject.SetActive(true);
                    }
                }
                else if (currentLevel == 2)
                {
                    if (_statusHeaderText != null)
                    {
                        _statusHeaderText.text = "LEVEL 2 CLEARED!";
                        _statusHeaderText.color = new Color(0.2f, 0.9f, 0.3f, 1f);
                    }
                    if (_statusSubText != null)
                    {
                        _statusSubText.text = "Highrise conquered. Prepare for Sector 3 vertical wall jumps.";
                    }
                    if (_nextLevelButton != null)
                    {
                        _nextLevelButton.gameObject.SetActive(true);
                    }
                }
                else
                {
                    if (_statusHeaderText != null)
                    {
                        _statusHeaderText.text = "ALL LEVELS COMPLETED!";
                        _statusHeaderText.color = new Color(0.1f, 1.0f, 0.5f, 1f);
                    }
                    if (_statusSubText != null)
                    {
                        _statusSubText.text = "You escaped the enforcer across all rooftop sectors!";
                    }
                    if (_nextLevelButton != null)
                    {
                        _nextLevelButton.gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                if (_statusHeaderText != null)
                {
                    _statusHeaderText.text = "CAUGHT BY HUNTER";
                    _statusHeaderText.color = new Color(1f, 0.25f, 0.25f, 1f);
                }
                if (_statusSubText != null)
                {
                    _statusSubText.text = "The enforcer terminated your rooftop run.";
                }
                if (_nextLevelButton != null)
                {
                    _nextLevelButton.gameObject.SetActive(false);
                }
            }
        }

        private void UpdateUI(GameState state)
        {
            if (_startScreenPanel != null)
            {
                _startScreenPanel.SetActive(state == GameState.WaitingToStart);
            }

            if (_levelSelectPanel != null)
            {
                _levelSelectPanel.SetActive(false);
            }

            if (_endScreenPanel != null)
            {
                _endScreenPanel.SetActive(state == GameState.GameOver);
            }

            if (state != GameState.GameOver && _nextLevelButton != null)
            {
                _nextLevelButton.gameObject.SetActive(false);
            }

            if (GameManager.Instance != null)
            {
                UpdateCurrentLevelLabel(GameManager.Instance.CurrentLevelIndex);
            }
        }

        public void PlayButtonClick()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayButtonClick();
            }
            else if (_fallbackSfxSource != null && _buttonClickClip != null)
            {
                _fallbackSfxSource.PlayOneShot(_buttonClickClip);
            }
        }

        private void OnPlayClicked()
        {
            PlayButtonClick();
            GameManager.Instance?.StartGame();
        }

        private void OnLevelSelectClicked()
        {
            PlayButtonClick();
            _cameFromGameOver = false;
            if (_startScreenPanel != null)
            {
                _startScreenPanel.SetActive(false);
            }

            if (_levelSelectPanel != null)
            {
                _levelSelectPanel.SetActive(true);
            }
        }

        private void OnEndLevelSelectClicked()
        {
            PlayButtonClick();
            _cameFromGameOver = true;
            if (_endScreenPanel != null)
            {
                _endScreenPanel.SetActive(false);
            }

            if (_levelSelectPanel != null)
            {
                _levelSelectPanel.SetActive(true);
            }
        }

        private void OnLevelSelectBackClicked()
        {
            PlayButtonClick();
            if (_levelSelectPanel != null)
            {
                _levelSelectPanel.SetActive(false);
            }

            if (_cameFromGameOver)
            {
                if (_endScreenPanel != null)
                {
                    _endScreenPanel.SetActive(true);
                }
            }
            else
            {
                if (_startScreenPanel != null)
                {
                    _startScreenPanel.SetActive(true);
                }
            }
        }

        private void OnLevelChosen(int levelIndex)
        {
            PlayButtonClick();
            _cameFromGameOver = false;
            if (_levelSelectPanel != null)
            {
                _levelSelectPanel.SetActive(false);
            }
            if (_endScreenPanel != null)
            {
                _endScreenPanel.SetActive(false);
            }
            if (_startScreenPanel != null)
            {
                _startScreenPanel.SetActive(false);
            }

            GameManager.Instance?.SelectLevel(levelIndex, startPlaying: true);
        }

        private void OnRestartClicked()
        {
            PlayButtonClick();
            GameManager.Instance?.RestartGame();
        }

        private void OnNextLevelClicked()
        {
            PlayButtonClick();
            GameManager.Instance?.StartNextLevel();
        }

        private void OnQuitClicked()
        {
            PlayButtonClick();
            GameManager.Instance?.QuitGame();
        }
    }
}
