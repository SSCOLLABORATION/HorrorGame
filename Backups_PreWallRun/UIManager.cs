using UnityEngine;
using UnityEngine.UI;
using Vector3D.Core;

namespace Vector3D.UI
{
    /// <summary>
    /// Manages the UI overlay screens (Start Screen and End Game Screen).
    /// Subscribes to GameManager state updates and routes button clicks to GameManager actions.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject _startScreenPanel;
        [SerializeField] private GameObject _endScreenPanel;

        [Header("Buttons")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _startQuitButton;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _endQuitButton;

        [Header("Status Feedback")]
        [SerializeField] private Text _statusHeaderText;

        private void Awake()
        {
            if (_playButton != null)
            {
                _playButton.onClick.AddListener(OnPlayClicked);
            }

            if (_startQuitButton != null)
            {
                _startQuitButton.onClick.AddListener(OnQuitClicked);
            }

            if (_restartButton != null)
            {
                _restartButton.onClick.AddListener(OnRestartClicked);
            }

            if (_endQuitButton != null)
            {
                _endQuitButton.onClick.AddListener(OnQuitClicked);
            }
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged += HandleStateChanged;
                GameManager.Instance.OnGameOver += HandleGameOver;
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
            }
        }

        private void HandleStateChanged(GameState state)
        {
            UpdateUI(state);
        }

        private void HandleGameOver(bool won)
        {
            if (_statusHeaderText != null)
            {
                _statusHeaderText.text = won ? "TRACK ESCAPED" : "CAUGHT BY HUNTER";
                _statusHeaderText.color = won ? new Color(0.2f, 0.9f, 0.3f, 1f) : new Color(1f, 0.25f, 0.25f, 1f);
            }
        }

        private void UpdateUI(GameState state)
        {
            if (_startScreenPanel != null)
            {
                _startScreenPanel.SetActive(state == GameState.WaitingToStart);
            }

            if (_endScreenPanel != null)
            {
                _endScreenPanel.SetActive(state == GameState.GameOver);
            }
        }

        private void OnPlayClicked()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartGame();
            }
        }

        private void OnRestartClicked()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RestartGame();
            }
        }

        private void OnQuitClicked()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.QuitGame();
            }
        }
    }
}
