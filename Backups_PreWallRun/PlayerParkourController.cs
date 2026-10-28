using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Vector3D.Core;

namespace VectorParkour
{
    /// <summary>
    /// Movement states for the forward-runner player controller.
    /// Exactly one state is active at any time.
    /// </summary>
    public enum MovementState
    {
        Running,
        Jumping,
        Falling,
        Sliding,
        Dead
    }

    /// <summary>
    /// Authoritative Forward-Runner Player Controller for Unity 6.3 LTS.
    /// Features:
    /// - Deterministic automatic forward movement (constant velocity during run, jump, and slide).
    /// - Clean physics-based jumping with stable grounding and coyote debounce.
    /// - Reliable sliding with collider halving that preserves ground-contact point (feet at Y=0).
    /// - Overhead clearance checking before restoring standing collider.
    /// - Zero position snapping, teleportation, or depenetration popping.
    /// - Single authoritative movement call (characterController.Move) exactly once per frame.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerParkourController : MonoBehaviour
    {
        [Header("Forward Movement")]
        [Tooltip("Constant auto-run speed along the forward axis (m/s).")]
        [SerializeField] private float baseSpeed = 9.5f;

        [Tooltip("Time to smoothly accelerate from 0 to base speed on start (seconds).")]
        [SerializeField] private float accelerationTime = 0.4f;

        [Header("Jump Settings")]
        [Tooltip("Initial upward impulse velocity for jumping (m/s). 9.5 m/s yields ~1.88m peak height.")]
        [SerializeField] private float jumpForce = 9.5f;

        [Tooltip("Downward gravitational acceleration (m/s^2).")]
        [SerializeField] private float gravity = -24.0f;

        [Tooltip("Downward terminal velocity clamp (m/s).")]
        [SerializeField] private float maxFallSpeed = -28.0f;

        [Tooltip("Down input mid-air fast-fall velocity (m/s).")]
        [SerializeField] private float fastFallSpeed = -20.0f;

        [Tooltip("Grace period (seconds) after leaving a ledge where jumping is still permitted.")]
        [SerializeField] private float coyoteTime = 0.1f;

        [Tooltip("Window (seconds) to buffer jump input before landing.")]
        [SerializeField] private float inputBufferWindow = 0.18f;

        [Header("Slide Settings")]
        [Tooltip("Duration of slide before attempting to stand up (seconds).")]
        [SerializeField] private float slideDuration = 0.65f;

        [Tooltip("Height of the CharacterController capsule while sliding.")]
        [SerializeField] private float slidingHeight = 0.85f;

        [Tooltip("Standing height of the CharacterController capsule.")]
        [SerializeField] private float standingHeight = 1.8f;

        [Header("Sprint Burst")]
        [SerializeField] private float sprintMultiplier = 1.4f;
        [SerializeField] private float sprintDuration = 1.2f;
        [SerializeField] private float sprintCooldown = 3.0f;

        [Header("Stumble / Collision Settings")]
        [SerializeField] private float stumbleDuration = 0.45f;
        [SerializeField] private float stumbleSpeedMultiplier = 0.65f;

        [Header("Level / Goal Settings")]
        [SerializeField] private float finishZ = 216.0f;

        [Header("Visual & Animation")]
        [SerializeField] private Transform visualRoot;

        // Components
        private CharacterController characterController;
        private Animator animator;

        // State Machine
        private MovementState currentState = MovementState.Running;
        public MovementState CurrentState => currentState;

        // Public Query Properties for other systems
        public bool IsGrounded => characterController != null && characterController.isGrounded;
        public bool IsJumping => currentState == MovementState.Jumping || currentState == MovementState.Falling;
        public bool IsSliding => currentState == MovementState.Sliding;
        public bool IsVaulting => false; // Vaulting unified into responsive jump
        public bool IsSprinting => isSprinting;
        public bool IsStumbling => isStumbling;
        public bool HasCompletedLevel => hasCompletedLevel;
        public float CurrentSpeed => currentSpeed;
        public float BaseSpeed => baseSpeed;
        public float SprintCooldownRemaining => Mathf.Max(0f, sprintCooldownTimer);

        // Movement Runtime Variables
        private float currentSpeed = 0f;
        private float verticalVelocity = -2.0f;
        private float runTimer = 0f;
        private float airborneTimer = 0f;

        // Action Timers
        private float jumpBufferTimer = 0f;
        private float slideBufferTimer = 0f;
        private float slideTimer = 0f;
        private float sprintTimer = 0f;
        private float sprintCooldownTimer = 0f;
        private float stumbleTimer = 0f;

        // State Flags
        private bool isSprinting = false;
        private bool isStumbling = false;
        private bool hasCompletedLevel = false;

        // Collider Geometry Cache
        private Vector3 standingCenter;
        private Vector3 slidingCenter;

        // Stumble Debounce
        private float lastStumbleZ = -999f;
        private float lastClearedObstacleZ = -999f;

        // Cached GUI Styles for zero-GC HUD
        private GUIStyle headerStyle;
        private GUIStyle statStyle;
        private GUIStyle sprintStyle;
        private GUIStyle hintStyle;
        private GUIStyle stumbleStyle;
        private GUIStyle winTitleStyle;
        private GUIStyle subStyle;
        private bool stylesInitialized = false;

        private void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            EnsureComponents();
        }

        private void EnsureComponents()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (characterController != null)
            {
                characterController.skinWidth = 0.015f;
                characterController.minMoveDistance = 0.001f;
                characterController.stepOffset = 0.3f;
                characterController.slopeLimit = 45f;
                standingHeight = characterController.height;
                standingCenter = characterController.center;
                // Calculate sliding center so the bottom of the capsule (feet) stays strictly at ground level:
                // Bottom = center.y - height / 2.
                // Standing bottom = standingCenter.y - standingHeight / 2.
                // Sliding center.y = Standing bottom + slidingHeight / 2.
                float groundY = standingCenter.y - (standingHeight * 0.5f);
                slidingCenter = new Vector3(standingCenter.x, groundY + (slidingHeight * 0.5f), standingCenter.z);
            }

            if (visualRoot == null)
            {
                Transform foundVisual = transform.Find("Visual");
                visualRoot = foundVisual != null ? foundVisual : transform;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator != null)
            {
                animator.applyRootMotion = false;
            }
        }

        private void Start()
        {
            EnsureComponents();

            // Ignore collision with Chaser so the hunter never pushes or jostles the player
            ChaserAI chaser = UnityEngine.Object.FindFirstObjectByType<ChaserAI>();
            if (chaser != null)
            {
                var chaserCC = chaser.GetComponent<CharacterController>();
                if (chaserCC != null && characterController != null)
                {
                    Physics.IgnoreCollision(characterController, chaserCC, true);
                }
            }

            currentSpeed = 0f;
            verticalVelocity = -2.0f;
            currentState = MovementState.Running;
        }

        private void Update()
        {
            EnsureComponents();

            if (currentState == MovementState.Dead || hasCompletedLevel)
            {
                CheckRestartInput();
                return;
            }

            if (Time.timeScale <= 0f)
            {
                CheckRestartInput();
                return;
            }

            // 1. Process Timers & Cooldowns
            UpdateTimers();

            // 2. Read Player Input
            ProcessInput();

            // 3. Update Movement State Machine
            UpdateStateMachine();

            // 4. Calculate & Apply Single Authoritative Motion
            ApplyAuthoritativeMovement();

            // 5. Update Animation Parameters
            UpdateAnimator();

            // 6. Check Win/Fall Conditions
            CheckBoundsAndGoals();
        }

        #region Timers & Input

        private void UpdateTimers()
        {
            // Acceleration timer
            runTimer += Time.deltaTime;

            // Sprint cooldown & duration
            if (sprintCooldownTimer > 0f)
            {
                sprintCooldownTimer -= Time.deltaTime;
            }

            if (isSprinting)
            {
                sprintTimer -= Time.deltaTime;
                if (sprintTimer <= 0f)
                {
                    isSprinting = false;
                    sprintCooldownTimer = sprintCooldown;
                }
            }

            // Stumble timer
            if (isStumbling)
            {
                stumbleTimer -= Time.deltaTime;
                if (stumbleTimer <= 0f)
                {
                    isStumbling = false;
                }
            }

            // Input buffer timers
            if (jumpBufferTimer > 0f)
            {
                jumpBufferTimer -= Time.deltaTime;
            }

            if (slideBufferTimer > 0f)
            {
                slideBufferTimer -= Time.deltaTime;
            }
        }

        private void ProcessInput()
        {
            bool jumpDown = false;
            bool slideDown = false;
            bool sprintDown = false;

            if (Keyboard.current != null)
            {
                jumpDown = Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame;
                slideDown = Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame;
                sprintDown = Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame;
            }
            else
            {
                try
                {
                    jumpDown = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
                    slideDown = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
                    sprintDown = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
                }
                catch { }
            }

            // Jump input: buffer or fast-fall
            if (jumpDown)
            {
                jumpBufferTimer = inputBufferWindow;
            }

            // Slide input: buffer or fast-fall
            if (slideDown)
            {
                if (currentState == MovementState.Jumping || currentState == MovementState.Falling)
                {
                    // Mid-air fast fall
                    verticalVelocity = Mathf.Min(verticalVelocity, fastFallSpeed);
                }
                else
                {
                    slideBufferTimer = inputBufferWindow;
                }
            }

            // Sprint input
            if (sprintDown && !isSprinting && sprintCooldownTimer <= 0f)
            {
                isSprinting = true;
                sprintTimer = sprintDuration;
            }
        }

        #endregion

        #region Movement State Machine

        private void UpdateStateMachine()
        {
            bool grounded = characterController.isGrounded;

            switch (currentState)
            {
                case MovementState.Running:
                    if (!grounded)
                    {
                        airborneTimer += Time.deltaTime;
                        if (airborneTimer > coyoteTime)
                        {
                            currentState = MovementState.Falling;
                        }
                    }
                    else
                    {
                        airborneTimer = 0f;
                        verticalVelocity = -2.0f; // Firm grounding stick
                    }

                    // Check for buffered Jump
                    if (jumpBufferTimer > 0f && (grounded || airborneTimer <= coyoteTime))
                    {
                        ExecuteJump();
                        return;
                    }

                    // Check for buffered Slide
                    if (slideBufferTimer > 0f && grounded)
                    {
                        ExecuteSlide();
                        return;
                    }
                    break;

                case MovementState.Jumping:
                    airborneTimer += Time.deltaTime;
                    verticalVelocity += gravity * Time.deltaTime;
                    verticalVelocity = Mathf.Max(verticalVelocity, maxFallSpeed);

                    // Check if downward motion has hit the ground
                    if (verticalVelocity < 0f && grounded && airborneTimer > 0.1f)
                    {
                        Land();
                    }
                    break;

                case MovementState.Falling:
                    airborneTimer += Time.deltaTime;
                    verticalVelocity += gravity * Time.deltaTime;
                    verticalVelocity = Mathf.Max(verticalVelocity, maxFallSpeed);

                    // Coyote jump check
                    if (jumpBufferTimer > 0f && airborneTimer <= coyoteTime)
                    {
                        ExecuteJump();
                        return;
                    }

                    if (grounded && airborneTimer > 0.05f)
                    {
                        Land();
                    }
                    break;

                case MovementState.Sliding:
                    if (!grounded)
                    {
                        // Slipped off an edge while sliding -> fall
                        EndSlideDirect();
                        currentState = MovementState.Falling;
                        return;
                    }

                    verticalVelocity = -2.0f;
                    slideTimer -= Time.deltaTime;

                    // Allow jumping directly out of slide if headroom is clear
                    if (jumpBufferTimer > 0f && HasHeadroom())
                    {
                        EndSlideDirect();
                        ExecuteJump();
                        return;
                    }

                    // End slide when timer expires AND headroom is clear
                    if (slideTimer <= 0f)
                    {
                        if (HasHeadroom())
                        {
                            EndSlideDirect();
                            currentState = MovementState.Running;
                        }
                        // If headroom is obstructed (e.g. still under a beam), stay sliding until clear!
                    }
                    break;

                case MovementState.Dead:
                    currentSpeed = 0f;
                    break;
            }
        }

        private void ExecuteJump()
        {
            jumpBufferTimer = 0f;
            airborneTimer = 0f;
            verticalVelocity = jumpForce;
            currentState = MovementState.Jumping;
        }

        private void Land()
        {
            airborneTimer = 0f;
            verticalVelocity = -2.0f;
            currentState = MovementState.Running;

            // Check if jump was buffered right before landing
            if (jumpBufferTimer > 0f)
            {
                ExecuteJump();
            }
            else if (slideBufferTimer > 0f)
            {
                ExecuteSlide();
            }
        }

        private void ExecuteSlide()
        {
            slideBufferTimer = 0f;
            slideTimer = slideDuration;
            currentState = MovementState.Sliding;

            // Halve collider height cleanly keeping bottom at ground level
            characterController.height = slidingHeight;
            characterController.center = slidingCenter;
        }

        private void EndSlideDirect()
        {
            // Restore standing collider dimensions smoothly
            characterController.height = standingHeight;
            characterController.center = standingCenter;
        }

        /// <summary>
        /// Checks whether the space above the sliding player is clear before standing up.
        /// Prevents the player's head from popping into overhead obstacle beams.
        /// </summary>
        public bool HasHeadroom()
        {
            if (characterController == null) return true;

            // Cast from top of sliding capsule upward to standing height
            float radius = characterController.radius * 0.85f;
            Vector3 bottomSphere = transform.position + Vector3.up * (slidingHeight - radius);
            float castDistance = (standingHeight - slidingHeight) + 0.1f;

            int obstacleLayerMask = LayerMask.GetMask("Default", "Obstacle_Slide");
            if (obstacleLayerMask == 0) obstacleLayerMask = ~LayerMask.GetMask("Player");

            bool blocked = Physics.SphereCast(
                bottomSphere,
                radius,
                Vector3.up,
                out RaycastHit hit,
                castDistance,
                obstacleLayerMask,
                QueryTriggerInteraction.Collide
            );

            return !blocked;
        }

        #endregion

        #region Authoritative Movement

        private void ApplyAuthoritativeMovement()
        {
            // 1. Calculate target forward speed
            float accelRatio = Mathf.Clamp01(runTimer / accelerationTime);
            float targetBase = Mathf.Lerp(0f, baseSpeed, accelRatio);
            float speed = targetBase * (isSprinting ? sprintMultiplier : 1.0f);

            // Stumble penalty with smooth recovery curve
            if (isStumbling)
            {
                float ratio = Mathf.Clamp01(stumbleTimer / stumbleDuration);
                float mult = Mathf.Lerp(1.0f, stumbleSpeedMultiplier, ratio);
                speed *= mult;
            }

            currentSpeed = speed;

            // 2. Track centering: gently correct any lateral X drift
            float lateralCorrection = -transform.position.x * 8.0f;

            // 3. Single authoritative velocity vector
            Vector3 velocity = new Vector3(lateralCorrection, verticalVelocity, currentSpeed);

            // 4. Authoritative displacement application
            characterController.Move(velocity * Time.deltaTime);
        }

        #endregion

        #region Collisions & Obstacles

        private void OnTriggerEnter(Collider other)
        {
            // Finish line / Goal check
            if (other.CompareTag("Finish") || other.gameObject.name.Contains("Finish") || other.gameObject.name.Contains("Goal"))
            {
                ReachFinishLine();
                return;
            }

            // Vault Block obstacle
            if (other.CompareTag("Obstacle_Vault"))
            {
                // If player is airborne (jumping) with clearance, obstacle is cleared!
                if (IsJumping && transform.position.y >= 0.7f)
                {
                    lastClearedObstacleZ = other.transform.position.z;
                    return;
                }

                // Front face hit -> stumble
                if (Mathf.Abs(other.transform.position.z - lastClearedObstacleZ) > 1.5f)
                {
                    TriggerStumble(stumbleDuration, other.transform.position.z, true);
                }
                return;
            }

            // Slide Barrier obstacle
            if (other.CompareTag("Obstacle_Slide"))
            {
                // If currently sliding, obstacle is cleared!
                if (IsSliding)
                {
                    lastClearedObstacleZ = other.transform.position.z;
                    return;
                }

                // Front face hit -> stumble
                if (Mathf.Abs(other.transform.position.z - lastClearedObstacleZ) > 1.5f)
                {
                    TriggerStumble(stumbleDuration, other.transform.position.z, false);
                }
                return;
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Check for front-face obstacle impacts
            if (hit.normal.z < -0.5f)
            {
                if (hit.gameObject.CompareTag("Obstacle_Vault") && !IsJumping)
                {
                    if (Mathf.Abs(hit.transform.position.z - lastClearedObstacleZ) > 1.5f)
                    {
                        TriggerStumble(stumbleDuration, hit.transform.position.z, true);
                    }
                }
                else if (hit.gameObject.CompareTag("Obstacle_Slide") && !IsSliding)
                {
                    if (Mathf.Abs(hit.transform.position.z - lastClearedObstacleZ) > 1.5f)
                    {
                        TriggerStumble(stumbleDuration, hit.transform.position.z, false);
                    }
                }
            }
        }

        public void TriggerStumble(float duration, float obstacleZ, bool isVaultBlock)
        {
            if (Mathf.Abs(obstacleZ - lastStumbleZ) < 1.0f && isStumbling) return;

            lastStumbleZ = obstacleZ;
            isStumbling = true;
            stumbleTimer = duration;

            if (IsSliding && HasHeadroom())
            {
                EndSlideDirect();
                currentState = MovementState.Running;
            }
        }

        /// <summary>
        /// Legacy interface for VaultDetector child object compatibility.
        /// </summary>
        public void SetInVaultTrigger(bool inTrigger, Transform obstacleTransform)
        {
            // Maintained for compatibility with VaultDetector component.
        }

        #endregion

        #region Animation & Visuals

        private void UpdateAnimator()
        {
            if (animator == null) return;
            animator.SetBool("IsJumping", IsJumping);
            animator.SetBool("IsSliding", IsSliding);
            animator.SetBool("IsVaulting", false);
        }

        #endregion

        #region Goal & Restart

        public void ReachFinishLine()
        {
            if (hasCompletedLevel) return;
            hasCompletedLevel = true;
            Time.timeScale = 0f;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerGameOver(true);
            }
        }

        private void CheckBoundsAndGoals()
        {
            if (transform.position.z >= (finishZ - 0.5f) && !hasCompletedLevel)
            {
                ReachFinishLine();
                return;
            }

            if (transform.position.y < -5f)
            {
                currentState = MovementState.Dead;
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.TriggerGameOver(false);
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

            currentSpeed = 0f;
            verticalVelocity = -2.0f;
            runTimer = 0f;
            airborneTimer = 0f;
            currentState = MovementState.Running;
            isSprinting = false;
            isStumbling = false;
            hasCompletedLevel = false;
            sprintTimer = 0f;
            sprintCooldownTimer = 0f;
            jumpBufferTimer = 0f;
            slideBufferTimer = 0f;
            lastStumbleZ = -999f;
            lastClearedObstacleZ = -999f;

            if (characterController != null)
            {
                characterController.height = standingHeight;
                characterController.center = standingCenter;
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

        #region HUD & Vector Style Overlay

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
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            InitStyles();

            // HUD Container Box
            GUI.Box(new Rect(15, 15, 290, 115), GUIContent.none);

            GUILayout.BeginArea(new Rect(25, 20, 270, 105));
            GUILayout.Label("VECTOR // PARKOUR RUNNER", headerStyle);
            GUILayout.Label($"SPEED: {currentSpeed:F1} m/s", statStyle);
            GUILayout.Label($"DISTANCE: {Mathf.Max(0f, transform.position.z):F0} m / {finishZ:F0} m", statStyle);

            string sprintStatus = isSprinting ? "SPRINTING!" : (sprintCooldownTimer <= 0f ? "READY [D / Right]" : $"COOLDOWN ({sprintCooldownTimer:F1}s)");
            sprintStyle.normal.textColor = isSprinting ? Color.cyan : (sprintCooldownTimer <= 0f ? Color.green : Color.yellow);
            GUILayout.Label($"BURST: {sprintStatus}", sprintStyle);
            GUILayout.EndArea();

            // Controls Hint Box (Bottom-Left)
            GUI.Box(new Rect(15, Screen.height - 55, 460, 40), GUIContent.none);
            GUI.Label(new Rect(25, Screen.height - 47, 440, 30), "CONTROLS: [W/Up] Jump | [S/Down] Slide | [D/Right] Sprint", hintStyle);

            // Stumble Alert
            if (isStumbling)
            {
                GUI.Label(new Rect(Screen.width / 2f - 180, 80, 360, 40), "STUMBLED! HUNTER CLOSING IN!", stumbleStyle);
            }

            // Victory Screen
            if (hasCompletedLevel)
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
