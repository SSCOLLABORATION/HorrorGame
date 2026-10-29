using UnityEngine;

namespace Vector3D.Core
{
    /// <summary>
    /// Centralized Audio Manager controlling Background Music, Death Sounds, and Sound Effects.
    /// Manages persistent 2D AudioSources for background soundtrack, gameplay events, and UI audio feedback.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Object.FindFirstObjectByType<AudioManager>();
                }
                return _instance;
            }
            private set => _instance = value;
        }
        private static AudioManager _instance;

        [Header("Audio Sources")]
        [SerializeField] private AudioSource _musicSource;
        [SerializeField] private AudioSource _sfxSource;

        [Header("Audio Clips")]
        [SerializeField] private AudioClip _bgmClip;
        [SerializeField] private AudioClip _deathClip;
        [SerializeField] private AudioClip _buttonClickClip;
        [SerializeField] private AudioClip _wallClimbClip;

        [Header("Volume Settings")]
        [Range(0f, 1f)] [SerializeField] private float _musicVolume = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float _deathVolume = 1.0f;
        [Range(0f, 1f)] [SerializeField] private float _sfxVolume = 0.85f;

        private float _lastDeathSoundTime = -999f;
        private float _lastWallClimbSoundTime = -999f;
        private const float WallClimbSoundCooldown = 0.18f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureAudioSources();
        }

        private void Start()
        {
            PlayBGM();

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameOver += HandleGameOver;
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameOver -= HandleGameOver;
            }
        }

        private void HandleGameOver(bool won)
        {
            if (!won)
            {
                PlayDeathSound();
            }
        }

        private void EnsureAudioSources()
        {
            if (_musicSource == null)
            {
                AudioSource[] sources = GetComponents<AudioSource>();
                if (sources.Length > 0) _musicSource = sources[0];
                else _musicSource = gameObject.AddComponent<AudioSource>();
            }

            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f; // Pure 2D Stereo
            _musicSource.ignoreListenerPause = true; // Continues playing in menus
            _musicSource.volume = _musicVolume;
            if (_bgmClip != null)
            {
                _musicSource.clip = _bgmClip;
            }

            if (_sfxSource == null)
            {
                AudioSource[] sources = GetComponents<AudioSource>();
                if (sources.Length > 1) _sfxSource = sources[1];
                else _sfxSource = gameObject.AddComponent<AudioSource>();
            }

            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f; // Pure 2D Stereo
            _sfxSource.ignoreListenerPause = true; // Audio plays even during pause / timeScale = 0
            _sfxSource.volume = _sfxVolume;
        }

        /// <summary>
        /// Starts or resumes playing the background music track.
        /// </summary>
        public void PlayBGM()
        {
            if (_musicSource == null) EnsureAudioSources();

            if (_musicSource != null && _bgmClip != null)
            {
                _musicSource.clip = _bgmClip;
                _musicSource.volume = _musicVolume;
                if (!_musicSource.isPlaying)
                {
                    _musicSource.Play();
                }
            }
        }

        /// <summary>
        /// Stops the background music track.
        /// </summary>
        public void StopBGM()
        {
            if (_musicSource != null && _musicSource.isPlaying)
            {
                _musicSource.Stop();
            }
        }

        /// <summary>
        /// Plays the death sound effect once when the player falls off or gets caught.
        /// Uses unscaled time to avoid duplicate plays on the same frame.
        /// </summary>
        public void PlayDeathSound()
        {
            if (Time.unscaledTime - _lastDeathSoundTime < 0.4f) return;
            _lastDeathSoundTime = Time.unscaledTime;

            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _deathClip != null)
            {
                _sfxSource.PlayOneShot(_deathClip, _deathVolume);
            }
        }

        /// <summary>
        /// Plays the menu button click sound effect once.
        /// </summary>
        public void PlayButtonClick()
        {
            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _buttonClickClip != null)
            {
                _sfxSource.PlayOneShot(_buttonClickClip, _sfxVolume);
            }
        }

        /// <summary>
        /// Plays a punchy tactile parkour kick sound effect when scaling height along a wall.
        /// Throttled by cooldown to prevent audio clipping / distortion on rapid inputs.
        /// </summary>
        public void PlayWallClimbSound()
        {
            if (Time.unscaledTime - _lastWallClimbSoundTime < WallClimbSoundCooldown) return;
            _lastWallClimbSoundTime = Time.unscaledTime;

            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _wallClimbClip != null)
            {
                _sfxSource.pitch = Random.Range(1.05f, 1.25f);
                _sfxSource.PlayOneShot(_wallClimbClip, _sfxVolume * 0.75f);
                _sfxSource.pitch = 1.0f;
            }
        }

        /// <summary>
        /// Updates the music volume level.
        /// </summary>
        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            if (_musicSource != null)
            {
                _musicSource.volume = _musicVolume;
            }
        }

        /// <summary>
        /// Updates the death sound volume level.
        /// </summary>
        public void SetDeathVolume(float volume)
        {
            _deathVolume = Mathf.Clamp01(volume);
        }

        /// <summary>
        /// Updates the sound effects volume level.
        /// </summary>
        public void SetSFXVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
            if (_sfxSource != null)
            {
                _sfxSource.volume = _sfxVolume;
            }
        }
    }
}
