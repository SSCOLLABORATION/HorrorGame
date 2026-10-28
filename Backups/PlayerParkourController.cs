using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VectorParkour
{
    /// <summary>
    /// Core Player Parkour Controller inspired by Vector.
    /// Handles deterministic auto-run, snappy 1:1 gesture controls (W/Up, S/Down, D/Right),
    /// input-buffered vault interpolation, collider-halving slides, fast-falling, stumble penalties,
    /// zero-lag cached HUD, and victory / finish line detection.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerParkourController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float baseSpeed = 9.5f;
        [SerializeField] private float startAccelerationTime = 0.8f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float jumpForce = 9.2f;
        [SerializeField] private float fastFallSpeed = -22f;

        [Header("Sprint Burst")]
        [SerializeField] private float sprintMultiplier = 1.4f;
        [SerializeField] private float sprintDuration = 1.2f;
        [SerializeField] private float sprintCooldown = 3.0f;

        [Header("Slide Settings")]
        [SerializeField] private float slideDuration = 0.65f;

        [Header("Vault Settings")]
        [SerializeField] private float vaultDuration = 0.28f;

        [Header("Stumble Settings")]
        [SerializeField] private float stumbleDuration = 0.45f;
        [SerializeField] private float stumbleSpeedMultiplier = 0.5f;

        [Header("Goal")]
        [SerializeField] private float finishZ = 216.0f;

        [Header("References")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Collider vaultDetectorCollider;

        // Character Controller and Dimensions
        private CharacterController characterController;
        private float originalHeight = 1.8f;
        private Vector3 originalCenter = new Vector3(0f, 0.9f, 0f);

        // State Flags
        public bool IsVaulting { get; private set; }
        public bool IsSliding { get; private set; }
        public bool IsStumbling { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool HasCompletedLevel { get; private set; }
        public bool IsGrounded => characterController != null && characterController.isGrounded;
        public bool IsJumping => isIntentionalJump || (airborneTimer > 0.15f && !IsVaulting);
        public float CurrentSpeed => currentForwardSpeed;
        public float BaseSpeed => baseSpeed;
        public float SprintCooldownRemaining => Mathf.Max(0f, sprintCooldownTimer);

        // Airborne tracking & coyote debounce to eliminate animation flicker
        private bool isIntentionalJump = false;
        private float airborneTimer = 0f;

        // Timers & Interpolation
        private float runTimer = 0f;
        private float currentForwardSpeed = 0f;
        private float verticalVelocity = 0f;
        private float sprintTimer = 0f;
        private float sprintCooldownTimer = 0f;
        private float slideTimer = 0f;
        private float vaultTimer = 0f;
        private float stumbleTimer = 0f;

        // Input Buffering (0.18s window for fluid responsiveness)
        private float jumpInputBufferTimer = 0f;
        private float slideInputBufferTimer = 0f;
        private const float InputBufferDuration = 0.18f;

        // Grace period for initial ground settling
        private float settleTimer = 0f;
        private const float SettleGracePeriod = 0.15f;

        // Vault Math
        private Vector3 vaultStartPos;
        private float vaultTargetZ;
        private float vaultPeakY;
        private Transform currentVaultObstacle;
        private bool canVaultInTrigger = false;

        // Visual Transforms & Animation
        private Vector3 defaultVisualScale = Vector3.one;
        private Vector3 defaultVisualLocalPos = Vector3.zero;
        private Animator animator;

        // Stumble & Obstacle Clearance Tracking
        private float lastStumbleObstacleZ = -999f;
        private float lastClearedVaultObstacleZ = -999f;
        private float lastClearedSlideObstacleZ = -999f;

        // Cached GUIStyles to eliminate GC stutter during play
        private GUIStyle headerStyle;
        private GUIStyle statStyle;
        private GUIStyle sprintStyle;
        private GUIStyle hintStyle;
        private GUIStyle stumbleStyle;
        private GUIStyle winTitleStyle;
        private GUIStyle subStyle;
        private bool stylesInitialized = false;

        private void EnsureComponents()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.skinWidth = 0.015f;
                    characterController.minMoveDistance = 0.001f;
                    originalHeight = characterController.height;
                    originalCenter = characterController.center;
                }
            }

            if (visualRoot == null)
            {
                Transform foundVisual = transform.Find("Visual");
                visualRoot = foundVisual != null ? foundVisual : transform;
            }

            if (visualRoot != null && visualRoot != transform && defaultVisualScale == Vector3.one)
            {
                defaultVisualScale = visualRoot.localScale;
                defaultVisualLocalPos = visualRoot.localPosition;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        private void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            EnsureComponents();
        }

        private void Start()
        {
            EnsureComponents();

            // Ignore collision between Player and Chaser so they NEVER push or jostle each other
            ChaserAI chaser = UnityEngine.Object.FindFirstObjectByType<ChaserAI>();
            if (chaser != null)
            {
                var chaserCC = chaser.GetComponent<CharacterController>();
                if (chaserCC != null && characterController != null)
                {
                    Physics.IgnoreCollision(characterController, chaserCC, true);
                }
            }

            // Clean initial vertical velocity: gentle grounding avoids penetration popping
            verticalVelocity = -1.2f;
            settleTimer = 0f;
        }

        private void Update()
        {
            EnsureComponents();

            if (HasCompletedLevel)
            {
                CheckRestartInput();
                return;
            }

            if (Time.timeScale <= 0f)
            {
                CheckRestartInput();
                return;
            }

            settleTimer += Time.deltaTime;

            HandleCooldowns();
            HandleInput();
            ProcessBufferedActions();
            UpdateMovement();
            UpdateVisualFeedback();
            UpdateAnimator();
            CheckFinishLine();
            CheckFallOutOfBounds();
        }

        private void HandleCooldowns()
        {
            if (sprintCooldownTimer > 0f)
            {
                sprintCooldownTimer -= Time.deltaTime;
            }

            if (jumpInputBufferTimer > 0f)
            {
                jumpInputBufferTimer -= Time.deltaTime;
            }

            if (slideInputBufferTimer > 0f)
            {
                slideInputBufferTimer -= Time.deltaTime;
            }
        }

        private void HandleInput()
        {
            bool upPressed = false;
            bool downPressed = false;
            bool rightPressed = false;

            if (Keyboard.current != null)
            {
                upPressed = Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame;
                downPressed = Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame;
                rightPressed = Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame;
            }
            else
            {
                try
                {
                    upPressed = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
                    downPressed = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
                    rightPressed = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
                }
                catch { }
            }

            if (IsVaulting) return;

            // Buffer Jump / Vault
            if (upPressed)
            {
                jumpInputBufferTimer = InputBufferDuration;
            }

            // Buffer Slide / Fast-Fall
            if (downPressed)
            {
                if (!characterController.isGrounded)
                {
                    verticalVelocity = fastFallSpeed;
                }
                else
                {
                    slideInputBufferTimer = InputBufferDuration;
                }
            }

            // Sprint Burst
            if (rightPressed && !IsSprinting && sprintCooldownTimer <= 0f)
            {
                StartSprint();
            }
        }

        private void ProcessBufferedActions()
        {
            if (IsVaulting) return;

            // Process Jump / Vault buffer
            if (jumpInputBufferTimer > 0f)
            {
                if (canVaultInTrigger && currentVaultObstacle != null)
                {
                    StartVault(currentVaultObstacle);
                    jumpInputBufferTimer = 0f;
                    return;
                }
                else if (characterController.isGrounded && !IsSliding)
                {
                    isIntentionalJump = true;
                    verticalVelocity = jumpForce;
                    jumpInputBufferTimer = 0f;
                }
            }

            // Process Slide buffer
            if (slideInputBufferTimer > 0f)
            {
                if (characterController.isGrounded && !IsSliding)
                {
                    StartSlide();
                    slideInputBufferTimer = 0f;
                }
            }
        }

        private void UpdateMovement()
        {
            // Auto-Run forward acceleration curve on start
            runTimer += Time.deltaTime;
            float accelRatio = Mathf.Clamp01(runTimer / startAccelerationTime);
            float targetBase = Mathf.SmoothStep(0f, baseSpeed, accelRatio);

            // Sprint Speed Boost
            if (IsSprinting)
            {
                sprintTimer -= Time.deltaTime;
                if (sprintTimer <= 0f)
                {
                    IsSprinting = false;
                    sprintCooldownTimer = sprintCooldown;
                }
            }

            // Forward speed calculation
            float calculatedSpeed = targetBase * (IsSprinting ? sprintMultiplier : 1.0f);

            // Stumble penalty with smooth recovery curve
            if (IsStumbling)
            {
                stumbleTimer -= Time.deltaTime;
                float stumbleRatio = Mathf.Clamp01(stumbleTimer / stumbleDuration);
                float speedMultiplier = Mathf.Lerp(1.0f, stumbleSpeedMultiplier, stumbleRatio);
                calculatedSpeed *= speedMultiplier;
                if (stumbleTimer <= 0f)
                {
                    IsStumbling = false;
                }
            }

            currentForwardSpeed = calculatedSpeed;

            // Vault interpolation mode
            if (IsVaulting)
            {
                vaultTimer += Time.deltaTime;
                float t = Mathf.Clamp01(vaultTimer / vaultDuration);

                // Deterministic smooth sinusoidal arc over obstacle bounds
                float newZ = Mathf.Lerp(vaultStartPos.z, vaultTargetZ, t);
                float arcY = Mathf.Sin(t * Mathf.PI) * (vaultPeakY - vaultStartPos.y);
                float newY = vaultStartPos.y + arcY;

                Vector3 targetPos = new Vector3(0f, newY, newZ);
                Vector3 motion = targetPos - transform.position;
                characterController.Move(motion);

                if (t >= 1.0f)
                {
                    EndVault();
                }
                return;
            }

            // Slide duration timer
            if (IsSliding)
            {
                slideTimer -= Time.deltaTime;
                if (slideTimer <= 0f)
                {
                    EndSlide();
                }
            }

            // Vertical physics / Gravity
            if (characterController.isGrounded)
            {
                airborneTimer = 0f;
                if (verticalVelocity <= 0.1f)
                {
                    isIntentionalJump = false;
                    // Gentle grounding stick keeps CC grounded without forcing deep penetration
                    verticalVelocity = -1.2f;
                }
            }
            else
            {
                airborneTimer += Time.deltaTime;
                verticalVelocity += gravity * Time.deltaTime;
                verticalVelocity = Mathf.Max(verticalVelocity, -28f);
            }

            // Apply forward velocity (along Z+) and vertical velocity
            Vector3 moveDirection = new Vector3(0f, verticalVelocity, currentForwardSpeed);
            characterController.Move(moveDirection * Time.deltaTime);
        }

        #region Parkour Actions

        private void StartVault(Transform obstacle)
        {
            IsVaulting = true;
            vaultTimer = 0f;
            vaultStartPos = transform.position;

            // Calculate obstacle top and clearance
            Collider obstacleCollider = obstacle.GetComponent<Collider>();
            float obstacleTop = obstacleCollider != null ? obstacleCollider.bounds.max.y : obstacle.position.y + 0.6f;
            vaultPeakY = obstacleTop + 0.35f;

            // Target landing point is past the obstacle's Z bounds
            float obstacleMaxZ = obstacleCollider != null ? obstacleCollider.bounds.max.z : obstacle.position.z + 0.6f;
            vaultTargetZ = obstacleMaxZ + 1.3f;
            lastClearedVaultObstacleZ = obstacle.position.z;

            verticalVelocity = 0f;
        }

        private void EndVault()
        {
            IsVaulting = false;
            canVaultInTrigger = false;
            currentVaultObstacle = null;
            verticalVelocity = -1.2f;
        }

        private void StartSlide()
        {
            IsSliding = true;
            slideTimer = slideDuration;

            // Snappy collider halving: 1.8m -> 0.9m, center Y 0.9m -> 0.45m
            characterController.height = originalHeight * 0.5f;
            characterController.center = new Vector3(originalCenter.x, originalCenter.y * 0.5f, originalCenter.z);
        }

        private void EndSlide()
        {
            IsSliding = false;

            // Snap back to full character height
            characterController.height = originalHeight;
            characterController.center = originalCenter;
        }

        private void StartSprint()
        {
            IsSprinting = true;
            sprintTimer = sprintDuration;
        }

        public void TriggerStumble(float duration, float obstacleZ, bool isVaultBlock)
        {
            if (Mathf.Abs(obstacleZ - lastStumbleObstacleZ) < 1.0f && IsStumbling) return;

            lastStumbleObstacleZ = obstacleZ;
            IsStumbling = true;
            stumbleTimer = duration;

            if (IsSliding)
            {
                EndSlide();
            }

            // Speed penalty alone handles stumble cleanly — NO recursive Move to prevent physics pushback
        }

        #endregion

        #region Collision & Detection

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Obstacle_Vault"))
            {
                if (IsVaulting)
                {
                    lastClearedVaultObstacleZ = other.transform.position.z;
                }
                else if (Mathf.Abs(other.transform.position.z - lastClearedVaultObstacleZ) > 1.5f)
                {
                    TriggerStumble(stumbleDuration, other.transform.position.z, true);
                }
            }
            else if (other.CompareTag("Obstacle_Slide"))
            {
                if (IsSliding)
                {
                    lastClearedSlideObstacleZ = other.transform.position.z;
                }
                else if (Mathf.Abs(other.transform.position.z - lastClearedSlideObstacleZ) > 1.5f)
                {
                    TriggerStumble(stumbleDuration, other.transform.position.z, false);
                }
            }
            else if (other.CompareTag("FinishLine") || other.gameObject.name.Contains("Finish") || other.gameObject.name.Contains("Goal"))
            {
                ReachFinishLine();
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.z < -0.5f)
            {
                if (hit.gameObject.CompareTag("Obstacle_Vault") && !IsVaulting)
                {
                    if (Mathf.Abs(hit.transform.position.z - lastClearedVaultObstacleZ) > 1.5f)
                    {
                        TriggerStumble(stumbleDuration, hit.transform.position.z, true);
                    }
                }
                else if (hit.gameObject.CompareTag("Obstacle_Slide") && !IsSliding)
                {
                    if (Mathf.Abs(hit.transform.position.z - lastClearedSlideObstacleZ) > 1.5f)
                    {
                        TriggerStumble(stumbleDuration, hit.transform.position.z, false);
                    }
                }
            }
        }

        public void SetInVaultTrigger(bool inTrigger, Transform obstacleTransform)
        {
            canVaultInTrigger = inTrigger;
            currentVaultObstacle = inTrigger ? obstacleTransform : null;
        }

        #endregion

        #region Visual Posing Feedback

        private void UpdateVisualFeedback()
        {
            if (visualRoot == null || visualRoot == transform) return;

            if (animator != null)
            {
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = defaultVisualLocalPos;
                visualRoot.localRotation = Quaternion.identity;
                return;
            }

            if (IsSliding)
            {
                visualRoot.localScale = new Vector3(defaultVisualScale.x, defaultVisualScale.y * 0.5f, defaultVisualScale.z * 1.25f);
                visualRoot.localPosition = defaultVisualLocalPos;
                visualRoot.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            }
            else if (IsVaulting)
            {
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = new Vector3(defaultVisualLocalPos.x, 0.2f, defaultVisualLocalPos.z + 0.1f);
                visualRoot.localRotation = Quaternion.Euler(22f, 0f, 0f);
            }
            else if (IsStumbling)
            {
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = defaultVisualLocalPos;
                visualRoot.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            }
            else
            {
                float leanAngle = IsSprinting ? 14f : 8f;
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = defaultVisualLocalPos;
                visualRoot.localRotation = Quaternion.Euler(leanAngle, 0f, 0f);
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;
            animator.SetBool("IsJumping", IsJumping);
            animator.SetBool("IsSliding", IsSliding);
            animator.SetBool("IsVaulting", IsVaulting);
        }

        #endregion

        #region Fall & Restart & Finish

        public void ReachFinishLine()
        {
            if (HasCompletedLevel) return;
            HasCompletedLevel = true;
            Time.timeScale = 0f;
        }

        private void CheckFinishLine()
        {
            if (transform.position.z >= (finishZ - 0.5f) && !HasCompletedLevel)
            {
                ReachFinishLine();
            }
        }

        private void CheckFallOutOfBounds()
        {
            if (transform.position.y < -5f)
            {
                if (Vector3D.Core.GameManager.Instance != null)
                {
                    Vector3D.Core.GameManager.Instance.TriggerGameOver(false);
                }
                else
                {
                    RestartRun();
                }
            }
        }

        public void RestartRun()
        {
            Time.timeScale = 1f;

            characterController.enabled = false;
            transform.position = new Vector3(0f, 0.5f, 0f);
            characterController.enabled = true;

            currentForwardSpeed = 0f;
            verticalVelocity = -1.2f;
            runTimer = 0f;
            settleTimer = 0f;
            isIntentionalJump = false;
            airborneTimer = 0f;
            IsVaulting = false;
            IsSliding = false;
            IsStumbling = false;
            IsSprinting = false;
            HasCompletedLevel = false;
            sprintTimer = 0f;
            sprintCooldownTimer = 0f;
            jumpInputBufferTimer = 0f;
            slideInputBufferTimer = 0f;
            lastClearedVaultObstacleZ = -999f;
            lastClearedSlideObstacleZ = -999f;
            lastStumbleObstacleZ = -999f;

            if (characterController != null)
            {
                characterController.height = originalHeight;
                characterController.center = originalCenter;
            }

            ChaserAI chaser = UnityEngine.Object.FindFirstObjectByType<ChaserAI>();
            if (chaser != null)
            {
                chaser.ResetChaser();
            }

            FinishLine fl = UnityEngine.Object.FindFirstObjectByType<FinishLine>();
            if (fl != null)
            {
                fl.ResetFinishLine();
            }
        }

        private void CheckRestartInput()
        {
            bool rPressed = false;
            if (Keyboard.current != null)
            {
                rPressed = Keyboard.current.rKey.wasPressedThisFrame;
            }
            else
            {
                try { rPressed = Input.GetKeyDown(KeyCode.R); } catch { }
            }
            if (rPressed)
            {
                RestartRun();
            }
        }

        #endregion

        #region Zero-Allocation Vector-Style GUI HUD

        private void InitStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            headerStyle.normal.textColor = Color.white;

            statStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Normal
            };
            statStyle.normal.textColor = new Color(0.85f, 0.9f, 1.0f);

            sprintStyle = new GUIStyle(statStyle);

            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            hintStyle.normal.textColor = new Color(0.7f, 0.85f, 1f);

            stumbleStyle = new GUIStyle(headerStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24
            };
            stumbleStyle.normal.textColor = Color.red;

            winTitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 36,
                fontStyle = FontStyle.Bold
            };
            winTitleStyle.normal.textColor = new Color(0.1f, 1.0f, 0.4f);

            subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            subStyle.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            if (Vector3D.Core.GameManager.Instance != null && Vector3D.Core.GameManager.Instance.CurrentState != Vector3D.Core.GameState.Playing)
            {
                return;
            }

            InitStyles();

            // HUD Container Box
            GUI.Box(new Rect(15, 15, 290, 115), GUIContent.none);

            GUILayout.BeginArea(new Rect(25, 20, 270, 105));
            GUILayout.Label("VECTOR // PARKOUR RUNNER", headerStyle);
            GUILayout.Label($"SPEED: {currentForwardSpeed:F1} m/s", statStyle);
            GUILayout.Label($"DISTANCE: {Mathf.Max(0f, transform.position.z):F0} m / {finishZ:F0} m", statStyle);

            string sprintStatus = IsSprinting ? "SPRINTING!" : (sprintCooldownTimer <= 0f ? "READY [D / Right]" : $"COOLDOWN ({sprintCooldownTimer:F1}s)");
            sprintStyle.normal.textColor = IsSprinting ? Color.cyan : (sprintCooldownTimer <= 0f ? Color.green : Color.yellow);
            GUILayout.Label($"BURST: {sprintStatus}", sprintStyle);
            GUILayout.EndArea();

            // Controls Hint Box (Bottom-Left)
            GUI.Box(new Rect(15, Screen.height - 55, 460, 40), GUIContent.none);
            GUI.Label(new Rect(25, Screen.height - 47, 440, 30), "CONTROLS: [W/Up] Jump/Vault | [S/Down] Slide/Fast-Fall | [D/Right] Sprint", hintStyle);

            // Stumble Alert
            if (IsStumbling)
            {
                GUI.Label(new Rect(Screen.width / 2f - 180, 80, 360, 40), "STUMBLED! HUNTER CLOSING IN!", stumbleStyle);
            }

            // Victory Screen
            if (HasCompletedLevel)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;

                float boxW = 500;
                float boxH = 210;
                float startX = (Screen.width - boxW) / 2f;
                float startY = (Screen.height - boxH) / 2f;

                GUI.Box(new Rect(startX, startY, boxW, boxH), GUIContent.none);
                GUI.Label(new Rect(startX, startY + 25, boxW, 45), "ESCAPED! LEVEL COMPLETE", winTitleStyle);
                GUI.Label(new Rect(startX, startY + 80, boxW, 30), "You outran the enforcer across the rooftops.", subStyle);

                if (GUI.Button(new Rect(startX + 150, startY + 135, 200, 45), "PLAY AGAIN [R]"))
                {
                    RestartRun();
                }
            }
        }

        #endregion
    }
}
