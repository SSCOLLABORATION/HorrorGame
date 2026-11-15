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
        private float _lastJumpSoundTime = -999f;
        private float _lastLandSoundTime = -999f;
        private float _lastSlideSoundTime = -999f;
        private float _lastHeartbeatSoundTime = -999f;
        private const float WallClimbSoundCooldown = 0.18f;

        private AudioClip _slideClip;
        private AudioClip _vaultClip;
        private AudioClip _victoryClip;
        private AudioClip _heartbeatClip;

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

            GenerateProceduralClips();
        }

        private void GenerateProceduralClips()
        {
            const int sampleRate = 44100;

            if (_slideClip == null)
            {
                int count = (int)(sampleRate * 0.35f);
                float[] samples = new float[count];
                float filter = 0f;
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / count;
                    float env = Mathf.Sin(t * Mathf.PI);
                    float white = Random.value * 2f - 1f;
                    filter = Mathf.Lerp(filter, white, 0.28f);
                    samples[i] = filter * env * 0.45f;
                }
                _slideClip = AudioClip.Create("SFX_Slide", count, 1, sampleRate, false);
                _slideClip.SetData(samples, 0);
            }

            if (_vaultClip == null)
            {
                int count = (int)(sampleRate * 0.22f);
                float[] samples = new float[count];
                float phase = 0f;
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / count;
                    float env = Mathf.Sin(t * Mathf.PI);
                    float freq = Mathf.Lerp(220f, 540f, t);
                    phase += 2f * Mathf.PI * freq / sampleRate;
                    float noise = (Random.value * 2f - 1f) * 0.15f;
                    samples[i] = (Mathf.Sin(phase) * 0.35f + noise) * env * 0.5f;
                }
                _vaultClip = AudioClip.Create("SFX_Vault", count, 1, sampleRate, false);
                _vaultClip.SetData(samples, 0);
            }

            if (_victoryClip == null)
            {
                int count = (int)(sampleRate * 0.75f);
                float[] samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / count;
                    float env = Mathf.Exp(-3.2f * t);
                    float c5 = Mathf.Sin(2f * Mathf.PI * 523.25f * t);
                    float e5 = Mathf.Sin(2f * Mathf.PI * 659.25f * t);
                    float g5 = Mathf.Sin(2f * Mathf.PI * 783.99f * t);
                    float c6 = Mathf.Sin(2f * Mathf.PI * 1046.50f * t);
                    samples[i] = (c5 * 0.3f + e5 * 0.25f + g5 * 0.25f + c6 * 0.2f) * env * 0.65f;
                }
                _victoryClip = AudioClip.Create("SFX_Victory", count, 1, sampleRate, false);
                _victoryClip.SetData(samples, 0);
            }

            if (_heartbeatClip == null)
            {
                int count = (int)(sampleRate * 0.45f);
                float[] samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / count;
                    float env1 = (t < 0.18f) ? Mathf.Sin(t / 0.18f * Mathf.PI) : 0f;
                    float env2 = (t >= 0.22f && t < 0.40f) ? Mathf.Sin((t - 0.22f) / 0.18f * Mathf.PI) * 0.75f : 0f;
                    float wave = Mathf.Sin(2f * Mathf.PI * 65f * t);
                    samples[i] = wave * (env1 + env2) * 0.75f;
                }
                _heartbeatClip = AudioClip.Create("SFX_Heartbeat", count, 1, sampleRate, false);
                _heartbeatClip.SetData(samples, 0);
            }
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
        /// Plays a springy upward jump takeoff sound effect.
        /// </summary>
        public void PlayJumpSound()
        {
            if (Time.unscaledTime - _lastJumpSoundTime < 0.12f) return;
            _lastJumpSoundTime = Time.unscaledTime;

            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _wallClimbClip != null)
            {
                _sfxSource.pitch = Random.Range(1.35f, 1.48f);
                _sfxSource.PlayOneShot(_wallClimbClip, _sfxVolume * 0.70f);
                _sfxSource.pitch = 1.0f;
            }
        }

        /// <summary>
        /// Plays a deep grounding landing thud when touching down on rooftops.
        /// </summary>
        public void PlayLandSound(float intensity = 1.0f)
        {
            if (Time.unscaledTime - _lastLandSoundTime < 0.15f) return;
            _lastLandSoundTime = Time.unscaledTime;

            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _wallClimbClip != null)
            {
                _sfxSource.pitch = Random.Range(0.68f, 0.78f);
                float vol = Mathf.Clamp(_sfxVolume * (0.6f + intensity * 0.4f), 0.3f, 1.0f);
                _sfxSource.PlayOneShot(_wallClimbClip, vol);
                _sfxSource.pitch = 1.0f;
            }
        }

        /// <summary>
        /// Plays a concrete slide friction whoosh when ducking under low obstacles.
        /// </summary>
        public void PlaySlideSound()
        {
            if (Time.unscaledTime - _lastSlideSoundTime < 0.25f) return;
            _lastSlideSoundTime = Time.unscaledTime;

            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _slideClip != null)
            {
                _sfxSource.pitch = Random.Range(0.95f, 1.05f);
                _sfxSource.PlayOneShot(_slideClip, _sfxVolume * 0.75f);
                _sfxSource.pitch = 1.0f;
            }
        }

        /// <summary>
        /// Plays an athletic vault swoosh with pitch modulated by vault timing rating.
        /// </summary>
        public void PlayVaultSound(int quality = 1)
        {
            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _vaultClip != null)
            {
                float pitch = quality == 1 ? 1.25f : (quality == 0 ? 0.95f : 1.1f);
                _sfxSource.pitch = pitch;
                _sfxSource.PlayOneShot(_vaultClip, _sfxVolume * 0.85f);
                _sfxSource.pitch = 1.0f;
            }
        }

        /// <summary>
        /// Plays a dull heavy impact thud when stumbling into an obstacle.
        /// </summary>
        public void PlayStumbleSound()
        {
            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _wallClimbClip != null)
            {
                _sfxSource.pitch = 0.55f;
                _sfxSource.PlayOneShot(_wallClimbClip, _sfxVolume * 1.0f);
                _sfxSource.pitch = 1.0f;
            }
        }

        /// <summary>
        /// Plays a triumphant multi-tone victory chime upon completing a rooftop sector.
        /// </summary>
        public void PlayVictorySound()
        {
            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _victoryClip != null)
            {
                _sfxSource.pitch = 1.0f;
                _sfxSource.PlayOneShot(_victoryClip, _sfxVolume * 0.95f);
            }
        }

        /// <summary>
        /// Plays a pulsing heartbeat throb when the hunter closes in.
        /// </summary>
        public void PlayHeartbeat(float dangerFactor)
        {
            float cooldown = Mathf.Lerp(0.85f, 0.40f, dangerFactor);
            if (Time.unscaledTime - _lastHeartbeatSoundTime < cooldown) return;
            _lastHeartbeatSoundTime = Time.unscaledTime;

            if (_sfxSource == null) EnsureAudioSources();

            if (_sfxSource != null && _heartbeatClip != null)
            {
                _sfxSource.pitch = Mathf.Lerp(0.95f, 1.20f, dangerFactor);
                _sfxSource.PlayOneShot(_heartbeatClip, _sfxVolume * Mathf.Clamp01(dangerFactor * 0.9f));
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
