using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VectorParkour
{
    /// <summary>
    /// Core Player Parkour Controller inspired by Vector.
    /// Handles deterministic auto-run, snappy 1:1 gesture controls (W/Up, S/Down, D/Right),
    /// input-locked vault interpolation, collider-halving slides, fast-falling, and stumble penalties.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerParkourController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float baseSpeed = 9.5f;
        [SerializeField] private float startAccelerationTime = 1.0f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float jumpForce = 8.5f;
        [SerializeField] private float fastFallSpeed = -22f;

        [Header("Sprint Burst")]
        [SerializeField] private float sprintMultiplier = 1.4f;
        [SerializeField] private float sprintDuration = 1.2f;
        [SerializeField] private float sprintCooldown = 3.0f;

        [Header("Slide Settings")]
        [SerializeField] private float slideDuration = 0.6f;

        [Header("Vault Settings")]
        [SerializeField] private float vaultDuration = 0.25f;

        [Header("Stumble Settings")]
        [SerializeField] private float stumbleDuration = 0.5f;
        [SerializeField] private float stumbleSpeedMultiplier = 0.35f;

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
        public bool IsGrounded => characterController != null && characterController.isGrounded;
        public float CurrentSpeed => currentForwardSpeed;
        public float BaseSpeed => baseSpeed;
        public float SprintCooldownRemaining => Mathf.Max(0f, sprintCooldownTimer);

        // Timers & Interpolation
        private float runTimer = 0f;
        private float currentForwardSpeed = 0f;
        private float verticalVelocity = 0f;
        private float sprintTimer = 0f;
        private float sprintCooldownTimer = 0f;
        private float slideTimer = 0f;
        private float vaultTimer = 0f;
        private float stumbleTimer = 0f;

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

        // Stumble Recovery Tracking
        private float lastStumbleObstacleZ = -999f;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            originalHeight = characterController.height;
            originalCenter = characterController.center;

            if (visualRoot == null)
            {
                Transform foundVisual = transform.Find("Visual");
                if (foundVisual != null) visualRoot = foundVisual;
                else visualRoot = transform;
            }

            if (visualRoot != null && visualRoot != transform)
            {
                defaultVisualScale = visualRoot.localScale;
                defaultVisualLocalPos = visualRoot.localPosition;
            }

            animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (Time.timeScale <= 0f)
            {
                CheckRestartInput();
                return;
            }

            HandleCooldowns();
            HandleInput();
            UpdateMovement();
            UpdateVisualFeedback();
            UpdateAnimator();
            CheckFallOutOfBounds();
        }

        private void HandleCooldowns()
        {
            if (sprintCooldownTimer > 0f)
            {
                sprintCooldownTimer -= Time.deltaTime;
            }
        }

        private void HandleInput()
        {
            bool upPressed = false;
            bool downPressed = false;
            bool rightPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                upPressed = Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame;
                downPressed = Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame;
                rightPressed = Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame;
            }
#else
            upPressed = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            downPressed = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
            rightPressed = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
#endif

            // During an active vault, input is strictly locked
            if (IsVaulting) return;

            // 1. UP GESTURE (W / Up Arrow): Contextual Vault OR Jump
            if (upPressed)
            {
                if (canVaultInTrigger && currentVaultObstacle != null)
                {
                    StartVault(currentVaultObstacle);
                    return;
                }
                else if (characterController.isGrounded && !IsSliding)
                {
                    verticalVelocity = jumpForce;
                }
            }

            // 2. DOWN GESTURE (S / Down Arrow): Fast-fall OR Contextual Slide
            if (downPressed)
            {
                if (!characterController.isGrounded)
                {
                    // Fast-fall: Snaps downward velocity
                    verticalVelocity = fastFallSpeed;
                }
                else if (!IsSliding)
                {
                    StartSlide();
                }
            }

            // 3. RIGHT GESTURE (D / Right Arrow): Sprint Burst
            if (rightPressed && !IsSprinting && sprintCooldownTimer <= 0f)
            {
                StartSprint();
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

            // Stumble penalty
            if (IsStumbling)
            {
                stumbleTimer -= Time.deltaTime;
                calculatedSpeed *= stumbleSpeedMultiplier;
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
                if (verticalVelocity < 0f)
                {
                    verticalVelocity = -2f; // Slight downward grounding stick
                }
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
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
            vaultTargetZ = obstacleMaxZ + 1.2f;

            // Reset vertical velocity
            verticalVelocity = 0f;
        }

        private void EndVault()
        {
            IsVaulting = false;
            canVaultInTrigger = false;
            currentVaultObstacle = null;
            verticalVelocity = -2f;
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

        public void TriggerStumble(float duration, float obstacleZ)
        {
            // Avoid duplicate rapid stumble triggers for the same obstacle
            if (Mathf.Abs(obstacleZ - lastStumbleObstacleZ) < 1.0f && IsStumbling) return;

            lastStumbleObstacleZ = obstacleZ;
            IsStumbling = true;
            stumbleTimer = duration;

            // End active slide if stumbled
            if (IsSliding)
            {
                EndSlide();
            }

            // Push slightly forward/up to clear collision front face
            characterController.Move(new Vector3(0f, 0.4f, 0.5f));
        }

        #endregion

        #region Collision & Detection

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Detect hitting the front face of an obstacle (normal facing backwards along -Z)
            if (hit.normal.z < -0.55f)
            {
                if (hit.gameObject.CompareTag("Obstacle_Vault"))
                {
                    if (!IsVaulting)
                    {
                        TriggerStumble(stumbleDuration, hit.transform.position.z);
                    }
                }
                else if (hit.gameObject.CompareTag("Obstacle_Slide"))
                {
                    if (!IsSliding)
                    {
                        TriggerStumble(stumbleDuration, hit.transform.position.z);
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

            if (IsSliding)
            {
                // Slide pose: Squash down and lean backwards
                visualRoot.localScale = new Vector3(defaultVisualScale.x, defaultVisualScale.y * 0.5f, defaultVisualScale.z * 1.25f);
                visualRoot.localPosition = new Vector3(defaultVisualLocalPos.x, -0.45f, defaultVisualLocalPos.z - 0.2f);
                visualRoot.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            }
            else if (IsVaulting)
            {
                // Vault pose: Hurdle forward tilt
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = new Vector3(defaultVisualLocalPos.x, 0.2f, defaultVisualLocalPos.z + 0.1f);
                visualRoot.localRotation = Quaternion.Euler(22f, 0f, 0f);
            }
            else if (IsStumbling)
            {
                // Stumble pose: Stagger back
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = defaultVisualLocalPos;
                visualRoot.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            }
            else
            {
                // Normal running pose: Aerodynamic forward lean
                float leanAngle = IsSprinting ? 14f : 8f;
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = defaultVisualLocalPos;
                visualRoot.localRotation = Quaternion.Euler(leanAngle, 0f, 0f);
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;
            animator.SetBool("IsSliding", IsSliding);
            animator.SetBool("IsVaulting", IsVaulting);
        }

        #endregion

        #region Fall & Restart

        private void CheckFallOutOfBounds()
        {
            if (transform.position.y < -6f)
            {
                RestartRun();
            }
        }

        public void RestartRun()
        {
            Time.timeScale = 1f;
            transform.position = new Vector3(0f, 0.1f, 0f);
            currentForwardSpeed = 0f;
            verticalVelocity = 0f;
            runTimer = 0f;
            IsVaulting = false;
            IsSliding = false;
            IsStumbling = false;
            IsSprinting = false;
            sprintTimer = 0f;
            sprintCooldownTimer = 0f;

            if (characterController != null)
            {
                characterController.height = originalHeight;
                characterController.center = originalCenter;
            }

            // Also reset chaser position
            ChaserAI chaser = UnityEngine.Object.FindFirstObjectByType<ChaserAI>();
            if (chaser != null)
            {
                chaser.ResetChaser();
            }
        }

        private void CheckRestartInput()
        {
            bool rPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null) rPressed = Keyboard.current.rKey.wasPressedThisFrame;
#else
            rPressed = Input.GetKeyDown(KeyCode.R);
#endif
            if (rPressed)
            {
                RestartRun();
            }
        }

        #endregion

        #region Vector-Style GUI HUD

        private void OnGUI()
        {
            // Vector Minimalist HUD
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            headerStyle.normal.textColor = Color.white;

            GUIStyle statStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Normal
            };
            statStyle.normal.textColor = new Color(0.85f, 0.9f, 1.0f);

            // HUD Container Box
            GUI.Box(new Rect(15, 15, 290, 115), GUIContent.none);

            GUILayout.BeginArea(new Rect(25, 20, 270, 105));
            GUILayout.Label("VECTOR // PARKOUR RUNNER", headerStyle);
            GUILayout.Label($"SPEED: {currentForwardSpeed:F1} m/s", statStyle);
            GUILayout.Label($"DISTANCE: {transform.position.z:F0} m / 220 m", statStyle);

            string sprintStatus = IsSprinting ? "SPRINTING!" : (sprintCooldownTimer <= 0f ? "READY [D]" : $"COOLDOWN ({sprintCooldownTimer:F1}s)");
            GUIStyle sprintStyle = new GUIStyle(statStyle);
            sprintStyle.normal.textColor = IsSprinting ? Color.cyan : (sprintCooldownTimer <= 0f ? Color.green : Color.yellow);
            GUILayout.Label($"BURST: {sprintStatus}", sprintStyle);
            GUILayout.EndArea();

            // Controls Hint Box (Bottom-Left)
            GUI.Box(new Rect(15, Screen.height - 55, 420, 40), GUIContent.none);
            GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            hintStyle.normal.textColor = new Color(0.7f, 0.85f, 1f);
            GUI.Label(new Rect(25, Screen.height - 47, 400, 30), "CONTROLS: [W/Up] Jump/Vault | [S/Down] Slide/Fast-Fall | [D/Right] Sprint", hintStyle);

            // Stumble Alert
            if (IsStumbling)
            {
                GUIStyle stumbleStyle = new GUIStyle(headerStyle)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 24
                };
                stumbleStyle.normal.textColor = Color.red;
                GUI.Label(new Rect(Screen.width / 2f - 150, 80, 300, 40), "STUMBLED! HUNTER CLOSING IN!", stumbleStyle);
            }
        }

        #endregion
    }

    /// <summary>
    /// Attach to the child VaultDetector trigger GameObject to signal the player controller.
    /// </summary>
    public class VaultDetector : MonoBehaviour
    {
        private PlayerParkourController playerController;

        private void Awake()
        {
            playerController = GetComponentInParent<PlayerParkourController>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Obstacle_Vault"))
            {
                playerController?.SetInVaultTrigger(true, other.transform);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Obstacle_Vault"))
            {
                playerController?.SetInVaultTrigger(false, null);
            }
        }
    }
}
