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
        Vaulting,
        Falling,
        Sliding,
        WallRunning,
        WallAttached,
        WallJumpTransferring,
        Dead
    }

    /// <summary>
    /// Contextual vault timing / quality categories.
    /// Determines animation clip, speed, and trajectory based on approach timing.
    /// </summary>
    public enum VaultQuality
    {
        None = 0,
        Long = 1,       // Pressed earlier than required
        Good = 2,       // Input received in expected time frame
        Perfect = 3,    // Input received in sweet spot interval
        Imperfect = 4   // Missed the correct interval (too late / on collision)
    }

    /// <summary>
    /// Authoritative Forward-Runner Player Controller for Unity 6.3 LTS.
    /// Optimised for zero-GC runtime execution, smooth lateral damping,
    /// contextual vaulting, and bug-free capsule resizing.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerParkourController : MonoBehaviour
    {
        [Header("Forward Movement")]
        [Tooltip("Constant auto-run speed along the forward axis (m/s).")]
        [SerializeField] private float baseSpeed = 9.5f;

        [Tooltip("Time to smoothly accelerate from 0 to base speed on start (seconds).")]
        [SerializeField] private float accelerationTime = 0.4f;

        [Header("Jump & Vault Settings")]
        [Tooltip("Initial upward impulse velocity for jumping (m/s).")]
        [SerializeField] private float jumpForce = 9.5f;

        [Tooltip("Duration of a contextual obstacle vault (seconds).")]
        [SerializeField] private float vaultDuration = 0.42f;

        [Tooltip("Downward gravitational acceleration (m/s^2).")]
        [SerializeField] private float gravity = -24.0f;

        [Tooltip("Downward terminal velocity clamp (m/s).")]
        [SerializeField] private float maxFallSpeed = -28.0f;

        [Tooltip("Down input mid-air fast-fall velocity (m/s).")]
        [SerializeField] private float fastFallSpeed = -20.0f;

        [Tooltip("Grace period (seconds) after leaving a ledge where jumping is still permitted.")]
        [SerializeField] private float coyoteTime = 0.12f;

        [Tooltip("Window (seconds) to buffer jump/vault input before landing or reaching trigger.")]
        [SerializeField] private float inputBufferWindow = 0.18f;

        [Header("Slide Settings")]
        [Tooltip("Duration of slide before attempting to stand up (seconds).")]
        [SerializeField] private float slideDuration = 0.65f;

        [Tooltip("Height of the CharacterController capsule while sliding.")]
        [SerializeField] private float slidingHeight = 0.85f;

        [Tooltip("Standing height of the CharacterController capsule.")]
        [SerializeField] private float standingHeight = 1.8f;

        [Header("Wall Run Settings")]
        [Tooltip("Speed multiplier applied to baseSpeed during wall run (~1.15x).")]
        [SerializeField] private float wallRunSpeedMultiplier = 1.15f;

        [Tooltip("Probing range for detecting left or right wall-run colliders.")]
        [SerializeField] private float wallDetectionRange = 2.85f;

        [Tooltip("Visual tilt angle in degrees away from the supporting wall around Z.")]
        [SerializeField] private float wallTiltAngle = 18f;

        [Tooltip("Speed of visual tilt transition smoothing.")]
        [SerializeField] private float wallTiltSpeed = 14f;

        [Tooltip("Upward hop force when jumping off or exiting a wall run.")]
        [SerializeField] private float wallDisengageHopForce = 9.0f;

        [Tooltip("SmoothDamp time for lateral track centering.")]
        [SerializeField] private float lateralSmoothTime = 0.11f;

        [Tooltip("Maximum allowed duration of a single continuous wall run in seconds.")]
        [SerializeField] private float wallRunMaxDuration = 3.5f;

        [Tooltip("Cooldown period after leaving a wall before another wall run can start.")]
        [SerializeField] private float wallExitCooldown = 0.35f;

        [Header("Wall Climb by Space Settings")]
        [Tooltip("Initial upward impulse velocity for vertical wall climb via Space (m/s).")]
        [SerializeField] private float wallClimbImpulse = 9.2f;

        [Tooltip("Gravity acceleration applied during upward wall climb (m/s^2).")]
        [SerializeField] private float wallClimbGravity = -14.0f;

        [Tooltip("Maximum height the player can scale along the wall (meters).")]
        [SerializeField] private float maxWallClimbY = 7.5f;

        // Legacy compatibility fields
        [SerializeField] private float wallJumpUpImpulse = 8.5f;
        [SerializeField] private float wallJumpHorizontalSpeed = 7.5f;
        [SerializeField] private float wallJumpForwardSpeed = 5.5f;
        [SerializeField] private float wallJumpGravity = -18.0f;
        [SerializeField] private float wallJumpMaxTransferTime = 0.85f;
        [SerializeField] private float wallAttachmentTolerance = 0.35f;
        [SerializeField] private float wallAttachedMaxTime = 3.5f;

        [Header("Sprint Burst")]
        [SerializeField] private float sprintMultiplier = 1.4f;
        [SerializeField] private float sprintDuration = 1.2f;
        [SerializeField] private float sprintCooldown = 3.0f;

        [Header("Stumble / Collision Settings")]
        [SerializeField] private float stumbleDuration = 0.45f;
        [SerializeField] private float stumbleSpeedMultiplier = 0.65f;

        [Header("Level / Goal Settings")]
        [SerializeField] private float finishZ = 246.0f;

        [Header("Visual & Animation")]
        [SerializeField] private Transform visualRoot;

        // Traversal Zone Integration
        private TraversalZone currentTraversalZone = null;
        public TraversalZone CurrentTraversalZone => currentTraversalZone;
        public bool IsInVerticalShaft => currentTraversalZone != null && currentTraversalZone.ZoneType == TraversalZoneType.VerticalShaft;

        public void OnEnterTraversalZone(TraversalZone zone)
        {
            currentTraversalZone = zone;
        }

        public void OnExitTraversalZone(TraversalZone zone)
        {
            if (currentTraversalZone == zone)
            {
                currentTraversalZone = null;
            }
        }

        // Cached Animator Hashes
        private static readonly int AnimIsJumping = Animator.StringToHash("IsJumping");
        private static readonly int AnimIsSliding = Animator.StringToHash("IsSliding");
        private static readonly int AnimIsVaulting = Animator.StringToHash("IsVaulting");
        private static readonly int AnimVaultType = Animator.StringToHash("VaultType");
        private static readonly int AnimIsFalling = Animator.StringToHash("IsFalling");
        private static readonly int AnimIsWallRunning = Animator.StringToHash("IsWallRunning");
        private static readonly int AnimIsStumbling = Animator.StringToHash("IsStumbling");
        private static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");

        private bool hasAnimVaultType = false;
        private bool hasAnimFalling = false;
        private bool hasAnimWallRunning = false;
        private bool hasAnimStumbling = false;
        private bool hasAnimGrounded = false;

        // Vault Timing Quality & Feedback
        private VaultQuality currentVaultQuality = VaultQuality.None;
        public VaultQuality CurrentVaultQuality => currentVaultQuality;
        private string vaultRatingText = "";
        private Color vaultRatingColor = Color.white;
        private float vaultRatingDisplayTimer = 0f;

        // Components
        private CharacterController characterController;
        private Animator animator;
        private bool componentsInitialized = false;
        private int playerLayerMask;
        private int headroomLayerMask;

        // State Machine
        private MovementState currentState = MovementState.Running;
        public MovementState CurrentState => currentState;

        // Public Query Properties for other systems
        public bool IsGrounded => characterController != null && characterController.isGrounded;
        public bool IsJumping => currentState == MovementState.Jumping || currentState == MovementState.Falling;
        public bool IsSliding => currentState == MovementState.Sliding;
        public bool IsWallRunning => currentState == MovementState.WallRunning;
        public bool IsWallAttached => currentState == MovementState.WallAttached;
        public bool IsWallJumpTransferring => currentState == MovementState.WallJumpTransferring;
        public bool IsInWallJumpTraversal => currentState == MovementState.WallAttached || currentState == MovementState.WallJumpTransferring || (currentState == MovementState.WallRunning && verticalVelocity > 0.1f);
        public bool IsBetweenWalls => CheckIsBetweenBothWalls();
        public int NextRequiredDirection => nextRequiredDirection;
        public bool IsVaulting => currentState == MovementState.Vaulting;
        public bool IsSprinting => isSprinting;
        public bool IsStumbling => isStumbling;
        public bool HasCompletedLevel => hasCompletedLevel;
        public float CurrentSpeed => currentSpeed;
        public float BaseSpeed => baseSpeed;
        public float SprintCooldownRemaining => Mathf.Max(0f, sprintCooldownTimer);
        public float TrackCenterX => _trackCenterX;
        public int WallSide => wallSide;
        public bool IsLeftWallHeld => isLeftWallHeld;
        public bool IsRightWallHeld => isRightWallHeld;
        public float LeftWallBufferTimer => leftWallBufferTimer;
        public float RightWallBufferTimer => rightWallBufferTimer;

        // Automated Input Injection Fields
        private bool overrideWallInput = false;
        private bool overrideLeft = false;
        private bool overrideRight = false;
        private bool injectedShift = false;
        private int injectedJumpDir = 0;
        private bool injectedJump = false;

        /// <summary>
        /// Explicit input injector for manual wall-run triggers and automated testing.
        /// </summary>
        public void SetWallInput(bool left, bool right)
        {
            overrideWallInput = left || right;
            overrideLeft = left;
            overrideRight = right;
            if (left) leftWallBufferTimer = inputBufferWindow;
            if (right) rightWallBufferTimer = inputBufferWindow;
            isLeftWallHeld = left;
            isRightWallHeld = right;
        }

        public void ClearWallInputOverride()
        {
            overrideWallInput = false;
            overrideLeft = false;
            overrideRight = false;
            isLeftWallHeld = false;
            isRightWallHeld = false;
        }

        public void InjectShiftInput()
        {
            injectedShift = true;
        }

        public void InjectJumpInput()
        {
            injectedJump = true;
        }

        public void InjectDirectionalJump(int direction)
        {
            injectedJumpDir = direction;
        }

        // Movement Runtime Variables
        private float currentSpeed = 0f;
        private float verticalVelocity = -2.0f;
        private float lateralVelocity = 0f;
        private float runTimer = 0f;
        private float airborneTimer = 0f;
        private float _trackCenterX = 0f;

        // Action Timers
        private float jumpBufferTimer = 0f;
        private float slideBufferTimer = 0f;
        private float slideTimer = 0f;
        private float sprintTimer = 0f;
        private float sprintCooldownTimer = 0f;
        private float stumbleTimer = 0f;

        // Contextual Vault Runtime Variables
        private bool inVaultZone = false;
        private Collider pendingVaultCollider = null;
        private float vaultTimer = 0f;
        private Vector3 vaultStartPos;
        private float vaultTargetZ;
        private float vaultPeakY;

        // Wall Run State Variables
        private int wallSide = 0; // -1 for left wall, +1 for right wall, 0 for none
        private Collider activeWallCollider;
        private float wallRunTimer = 0f;
        private float wallCooldownTimer = 0f;
        private float targetWallX = 0f;
        private bool justStartedWallRun = false;
        private Quaternion defaultVisualLocalRotation = Quaternion.identity;

        // Manual Wall-Run Input Buffering (A = Left Wall, D = Right Wall)
        private float leftWallBufferTimer = 0f;
        private float rightWallBufferTimer = 0f;
        private bool isLeftWallHeld = false;
        private bool isRightWallHeld = false;

        // Vertical Alternating Wall-Jump Traversal State
        private float wallJumpTimer = 0f;
        private float wallAttachedTimer = 0f;
        private float currentAttachedWallX = 0f;
        private float targetWallJumpX = 0f;
        private int targetWallSide = 0; // -1 for Left, +1 for Right
        private int nextRequiredDirection = 0; // -1 for Left (A), +1 for Right (D)

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
        private bool stylesInitialized = false;

        private void Awake()
        {
            Application.runInBackground = true;
            InitializeComponentsOnce();
        }

        private void InitializeComponentsOnce()
        {
            if (componentsInitialized && characterController != null) return;

            characterController = GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.skinWidth = 0.015f;
                characterController.minMoveDistance = 0.0001f;
                characterController.stepOffset = 0.3f;
                characterController.slopeLimit = 45f;

                // Cache standing geometry ONCE so sliding never overwrites standingHeight
                if (characterController.height > 1.2f)
                {
                    standingHeight = characterController.height;
                    standingCenter = characterController.center;
                }
                else
                {
                    standingCenter = new Vector3(0f, standingHeight * 0.5f, 0f);
                }

                float groundY = standingCenter.y - (standingHeight * 0.5f);
                slidingCenter = new Vector3(standingCenter.x, groundY + (slidingHeight * 0.5f), standingCenter.z);
            }

            if (visualRoot == null || visualRoot == transform)
            {
                Transform foundVisual = transform.Find("Visual");
                visualRoot = foundVisual != null ? foundVisual : transform;
            }

            if (visualRoot != null)
            {
                defaultVisualLocalRotation = visualRoot.localRotation;
            }

            animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                foreach (var param in animator.parameters)
                {
                    if (param.nameHash == AnimVaultType) hasAnimVaultType = true;
                    if (param.nameHash == AnimIsFalling) hasAnimFalling = true;
                    if (param.nameHash == AnimIsWallRunning) hasAnimWallRunning = true;
                    if (param.nameHash == AnimIsStumbling) hasAnimStumbling = true;
                    if (param.nameHash == AnimIsGrounded) hasAnimGrounded = true;
                }
            }

            var vd = GetComponentInChildren<VaultDetector>();
            if (vd != null)
            {
                var bc = vd.GetComponent<BoxCollider>();
                if (bc != null)
                {
                    bc.center = new Vector3(0f, 0.90f, 3.20f);
                    bc.size = new Vector3(4.20f, 1.80f, 6.00f);
                }
            }

            playerLayerMask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            headroomLayerMask = LayerMask.GetMask("Default", "Obstacle_Slide");
            if (headroomLayerMask == 0) headroomLayerMask = playerLayerMask;

            componentsInitialized = true;
        }

        private void Start()
        {
            InitializeComponentsOnce();

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

            _trackCenterX = transform.position.x;
            currentSpeed = 0f;
            verticalVelocity = -2.0f;
            currentState = MovementState.Running;
        }

        private void Update()
        {
            if (!componentsInitialized) InitializeComponentsOnce();

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

            UpdateTimers();
            ProcessInput();
            UpdateStateMachine();
            ApplyAuthoritativeMovement();
            UpdateVisualTilt();
            UpdateAnimator();
            CheckBoundsAndGoals();

            justStartedWallRun = false;
        }

        #region Timers & Input

        private void UpdateTimers()
        {
            float dt = Time.deltaTime;
            runTimer += dt;

            if (sprintCooldownTimer > 0f) sprintCooldownTimer -= dt;

            if (isSprinting)
            {
                sprintTimer -= dt;
                if (sprintTimer <= 0f)
                {
                    isSprinting = false;
                    sprintCooldownTimer = sprintCooldown;
                }
            }

            if (isStumbling)
            {
                stumbleTimer -= dt;
                if (stumbleTimer <= 0f) isStumbling = false;
            }

            if (wallCooldownTimer > 0f) wallCooldownTimer -= dt;
            if (leftWallBufferTimer > 0f) leftWallBufferTimer -= dt;
            if (rightWallBufferTimer > 0f) rightWallBufferTimer -= dt;
            if (jumpBufferTimer > 0f) jumpBufferTimer -= dt;
            if (slideBufferTimer > 0f) slideBufferTimer -= dt;
        }

        private void ProcessInput()
        {
            bool jumpDown = false;
            bool slideDown = false;
            bool leftPressed = false;
            bool leftHeld = false;
            bool rightPressed = false;
            bool rightHeld = false;
            bool sprintDown = false;

            if (Keyboard.current != null)
            {
                jumpDown = Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame;
                slideDown = Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame;
                leftPressed = Keyboard.current.aKey.wasPressedThisFrame || Keyboard.current.leftArrowKey.wasPressedThisFrame;
                leftHeld = Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed;
                rightPressed = Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame;
                rightHeld = Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed;
                sprintDown = Keyboard.current.leftShiftKey.wasPressedThisFrame || Keyboard.current.rightShiftKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame;
            }

            if (overrideWallInput)
            {
                if (overrideLeft) { leftPressed = true; leftHeld = true; }
                if (overrideRight) { rightPressed = true; rightHeld = true; }
            }

            if (injectedShift)
            {
                sprintDown = true;
                injectedShift = false;
            }

            if (injectedJump)
            {
                jumpDown = true;
                injectedJump = false;
            }

            if (injectedJumpDir == -1)
            {
                leftPressed = true;
                injectedJumpDir = 0;
            }
            else if (injectedJumpDir == 1)
            {
                rightPressed = true;
                injectedJumpDir = 0;
            }

            if (leftPressed) leftWallBufferTimer = inputBufferWindow;
            if (rightPressed) rightWallBufferTimer = inputBufferWindow;
            isLeftWallHeld = leftHeld;
            isRightWallHeld = rightHeld;

            if (currentState == MovementState.WallRunning)
            {
                // Shift key activates vertical alternating wall-jump mode
                if (sprintDown)
                {
                    isSprinting = false;
                    sprintTimer = 0f;
                    ActivateWallJumpMode();
                    return;
                }

                // Core Mechanic: Pressing Space multiple times scales height along the wall!
                if (jumpDown)
                {
                    ScaleWallHeight();
                    return;
                }

                if (slideDown)
                {
                    DisengageWallRun(userInitiated: true);
                    return;
                }

                if (wallSide == -1 && rightPressed && !justStartedWallRun)
                {
                    DisengageWallRun(userInitiated: true);
                    return;
                }
                if (wallSide == 1 && leftPressed && !justStartedWallRun)
                {
                    DisengageWallRun(userInitiated: true);
                    return;
                }
            }
            else if (currentState == MovementState.WallAttached)
            {
                // Alternating directional inputs (A/D) launch transfer to opposite wall
                if (nextRequiredDirection == 1 && (rightPressed || rightHeld))
                {
                    ExecuteWallJumpTransfer(1);
                    return;
                }
                else if (nextRequiredDirection == -1 && (leftPressed || leftHeld))
                {
                    ExecuteWallJumpTransfer(-1);
                    return;
                }

                // Space key also transfers to opposite wall when attached
                if (jumpDown)
                {
                    ExecuteWallJumpTransfer(nextRequiredDirection != 0 ? nextRequiredDirection : (wallSide == -1 ? 1 : -1));
                    return;
                }

                if (slideDown)
                {
                    ExitWallJumpTraversal(false);
                    currentState = MovementState.Falling;
                    return;
                }
                return;
            }
            else if (currentState == MovementState.WallJumpTransferring)
            {
                // Airborne mid-transfer: reject repeated inputs until attachment
                return;
            }
            else if (currentState != MovementState.Vaulting)
            {
                if (jumpDown)
                {
                    jumpBufferTimer = inputBufferWindow;
                }

                if (leftWallBufferTimer > 0f || isLeftWallHeld)
                {
                    if (wallCooldownTimer <= 0f && CheckWallRunSurface(out int side, out RaycastHit hit) && side == -1)
                    {
                        leftWallBufferTimer = 0f;
                        StartWallRun(side, hit);
                        return;
                    }
                }

                if (rightWallBufferTimer > 0f || isRightWallHeld)
                {
                    if (wallCooldownTimer <= 0f && CheckWallRunSurface(out int side, out RaycastHit hit) && side == 1)
                    {
                        rightWallBufferTimer = 0f;
                        StartWallRun(side, hit);
                        return;
                    }
                }

                // Contextual auto-latch when airborne between opposing corridor walls
                if (wallCooldownTimer <= 0f && !characterController.isGrounded && CheckIsBetweenBothWalls())
                {
                    if (CheckWallRunSurface(out int corridorSide, out RaycastHit corridorHit))
                    {
                        if (jumpDown || isLeftWallHeld || isRightWallHeld || leftWallBufferTimer > 0f || rightWallBufferTimer > 0f || sprintDown)
                        {
                            StartWallRun(corridorSide, corridorHit);
                            if (jumpDown) ScaleWallHeight();
                            return;
                        }
                    }
                }
            }

            if (slideDown)
            {
                if (currentState == MovementState.Jumping || currentState == MovementState.Falling)
                {
                    verticalVelocity = Mathf.Min(verticalVelocity, fastFallSpeed);
                    slideBufferTimer = inputBufferWindow; // Also buffer slide immediately on landing
                }
                else if (currentState != MovementState.Vaulting)
                {
                    slideBufferTimer = inputBufferWindow;
                }
            }

            if (sprintDown && !isSprinting && sprintCooldownTimer <= 0f)
            {
                isSprinting = true;
                sprintTimer = sprintDuration;
            }
        }

        #endregion

        #region Wall Run & Vault Detection

        private bool IsWallRunObstacle(Collider col)
        {
            if (col == null) return false;
            return col.CompareTag("WallRunObstacle") || col.gameObject.name.Contains("WallRun");
        }

        public bool CheckWallRunSurface(out int side, out RaycastHit hit)
        {
            side = 0;
            hit = default;

            Vector3 probeOrigin = transform.position + Vector3.up * 1.0f;

            if (Physics.Raycast(probeOrigin, -Vector3.right, out RaycastHit hitLeft, wallDetectionRange, playerLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (IsWallRunObstacle(hitLeft.collider) && hitLeft.normal.x > 0.35f)
                {
                    if (hitLeft.collider.bounds.max.z > transform.position.z + 0.3f)
                    {
                        side = -1;
                        hit = hitLeft;
                        return true;
                    }
                }
            }

            if (Physics.Raycast(probeOrigin, Vector3.right, out RaycastHit hitRight, wallDetectionRange, playerLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (IsWallRunObstacle(hitRight.collider) && hitRight.normal.x < -0.35f)
                {
                    if (hitRight.collider.bounds.max.z > transform.position.z + 0.3f)
                    {
                        side = 1;
                        hit = hitRight;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Probes both left and right sides to determine if the player is currently inside a dual-wall corridor.
        /// </summary>
        public bool CheckIsBetweenBothWalls(float probeDistance = 4.2f)
        {
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            bool hasLeft = Physics.Raycast(origin, -Vector3.right, out RaycastHit hitLeft, probeDistance, playerLayerMask, QueryTriggerInteraction.Ignore) &&
                           IsWallRunObstacle(hitLeft.collider);
            bool hasRight = Physics.Raycast(origin, Vector3.right, out RaycastHit hitRight, probeDistance, playerLayerMask, QueryTriggerInteraction.Ignore) &&
                            IsWallRunObstacle(hitRight.collider);
            return hasLeft && hasRight;
        }

        public VaultQuality EvaluateVaultQuality(Collider obstacle)
        {
            if (obstacle == null) return VaultQuality.Good;
            float frontZ = obstacle.bounds.min.z;
            float playerFrontZ = transform.position.z + (characterController != null ? characterController.radius : 0.35f);
            float dist = frontZ - playerFrontZ;
            return EvaluateVaultQuality(dist);
        }

        public VaultQuality EvaluateVaultQuality(float distance)
        {
            // At running speed ~9.5 m/s:
            // >= 4.2m (up to 6.2m): Pressed earlier than required -> Long Jump
            // 2.4m .. 4.2m: Input received in expected time frame (forgiving window) -> Good Jump
            // 0.45m .. 2.4m: Input received in sweet spot interval (generous sweet spot) -> Perfect Jump
            // < 0.45m: Missed the correct interval (too late / on collision) -> Imperfect Jump
            if (distance >= 4.2f)
            {
                return VaultQuality.Long;
            }
            else if (distance >= 2.4f)
            {
                return VaultQuality.Good;
            }
            else if (distance >= 0.45f)
            {
                return VaultQuality.Perfect;
            }
            else
            {
                return VaultQuality.Imperfect;
            }
        }

        private bool TryDetectVaultObstacle(out Collider vaultCollider, out float distanceToObstacle)
        {
            if (inVaultZone && pendingVaultCollider != null)
            {
                vaultCollider = pendingVaultCollider;
                float frontZ = vaultCollider.bounds.min.z;
                float playerFrontZ = transform.position.z + (characterController != null ? characterController.radius : 0.35f);
                distanceToObstacle = Mathf.Max(0f, frontZ - playerFrontZ);
                return true;
            }

            // Forward BoxCast ahead up to 6.2m to reliably detect upcoming vault blocks across the track
            Vector3 waistOrigin = transform.position + Vector3.up * 0.65f;
            Vector3 halfExtents = new Vector3(1.5f, 0.4f, 0.1f);
            if (Physics.BoxCast(waistOrigin, halfExtents, Vector3.forward, out RaycastHit hit, Quaternion.identity, 6.2f, playerLayerMask, QueryTriggerInteraction.Collide))
            {
                if (hit.collider.CompareTag("Obstacle_Vault"))
                {
                    vaultCollider = hit.collider;
                    float frontZ = hit.collider.bounds.min.z;
                    float playerFrontZ = transform.position.z + (characterController != null ? characterController.radius : 0.35f);
                    distanceToObstacle = Mathf.Max(0f, frontZ - playerFrontZ);
                    return true;
                }
            }

            vaultCollider = null;
            distanceToObstacle = 999f;
            return false;
        }

        private bool TryDetectVaultObstacle(out Collider vaultCollider)
        {
            return TryDetectVaultObstacle(out vaultCollider, out _);
        }

        public void ExecuteVault(Collider obstacle, VaultQuality quality)
        {
            if (currentState == MovementState.Sliding) EndSlideDirect();

            currentState = MovementState.Vaulting;
            currentVaultQuality = quality;
            jumpBufferTimer = 0f;
            vaultTimer = 0f;
            vaultStartPos = transform.position;
            verticalVelocity = 0f;

            Bounds b = obstacle.bounds;
            lastClearedObstacleZ = obstacle.transform.position.z;

            // Configure trajectory, duration, and speed based on vault timing quality
            switch (quality)
            {
                case VaultQuality.Long:
                    // Pressed earlier than required -> Long Jump leaping from far (slower, floating airtime)
                    vaultDuration = 0.85f;
                    vaultPeakY = b.max.y + 0.45f;
                    vaultTargetZ = b.max.z + 1.6f;
                    currentSpeed = baseSpeed * 1.05f;
                    SetVaultRatingBanner("LONG JUMP (EARLY)", new Color(1.0f, 0.65f, 0.2f));
                    break;

                case VaultQuality.Good:
                    // Input received in expected time frame
                    vaultDuration = 0.45f;
                    vaultPeakY = b.max.y + 0.25f;
                    vaultTargetZ = b.max.z + 1.2f;
                    currentSpeed = baseSpeed;
                    SetVaultRatingBanner("GOOD VAULT", new Color(0.2f, 1.0f, 0.5f));
                    break;

                case VaultQuality.Perfect:
                    // Input received in sweet spot interval
                    vaultDuration = 0.40f;
                    vaultPeakY = b.max.y + 0.20f;
                    vaultTargetZ = b.max.z + 1.3f;
                    currentSpeed = baseSpeed * 1.15f; // Crisp speed reward!
                    SetVaultRatingBanner("PERFECT VAULT! +SPEED", new Color(0.2f, 0.9f, 1.0f));
                    break;

                case VaultQuality.Imperfect:
                    // Missed the correct interval (too late / on collision)
                    vaultDuration = 0.55f;
                    vaultPeakY = b.max.y + 0.15f;
                    vaultTargetZ = b.max.z + 0.85f;
                    currentSpeed = baseSpeed * 0.70f; // Stumble slowdown penalty
                    isStumbling = true;
                    stumbleTimer = 0.55f;
                    SetVaultRatingBanner("IMPERFECT (MISSED TIMING)", new Color(1.0f, 0.35f, 0.35f));
                    break;

                default:
                    vaultDuration = 0.42f;
                    vaultPeakY = b.max.y + 0.25f;
                    vaultTargetZ = b.max.z + 1.15f;
                    currentSpeed = baseSpeed;
                    break;
            }

            inVaultZone = false;
            pendingVaultCollider = null;

            if (animator != null)
            {
                animator.SetBool(AnimIsVaulting, true);
                if (hasAnimVaultType) animator.SetInteger(AnimVaultType, (int)quality);
            }
        }

        public void ExecuteVault(Collider obstacle)
        {
            float dist = 2.5f;
            if (obstacle != null)
            {
                float frontZ = obstacle.bounds.min.z;
                float playerFrontZ = transform.position.z + (characterController != null ? characterController.radius : 0.35f);
                dist = Mathf.Max(0f, frontZ - playerFrontZ);
            }
            ExecuteVault(obstacle, EvaluateVaultQuality(dist));
        }

        private void SetVaultRatingBanner(string text, Color color)
        {
            vaultRatingText = text;
            vaultRatingColor = color;
            vaultRatingDisplayTimer = 1.4f;
        }

        private void StartWallRun(int side, RaycastHit hit)
        {
            if (currentState == MovementState.Sliding) EndSlideDirect();

            currentState = MovementState.WallRunning;
            wallSide = side;
            activeWallCollider = hit.collider;
            wallRunTimer = 0f;
            justStartedWallRun = true;
            verticalVelocity = 0f;
            lateralVelocity = 0f;
            airborneTimer = 0f;
            jumpBufferTimer = 0f;
            slideBufferTimer = 0f;

            float faceX = hit.point.x;
            float safeBuffer = (characterController != null ? characterController.radius : 0.35f) + 0.12f;
            targetWallX = side == -1 ? (faceX + safeBuffer) : (faceX - safeBuffer);
        }

        private void ScaleWallHeight()
        {
            if (transform.position.y < maxWallClimbY)
            {
                // Dynamic impulse: reward rhythmic presses with compounding momentum
                if (verticalVelocity > 0f)
                {
                    verticalVelocity = Mathf.Clamp(verticalVelocity + wallClimbImpulse * 0.65f, wallClimbImpulse, wallClimbImpulse * 1.4f);
                }
                else
                {
                    verticalVelocity = wallClimbImpulse;
                }

                // Grant extra wall-run duration per climb so player can comfortably scale tall towers
                wallRunTimer = Mathf.Max(0f, wallRunTimer - 1.0f);

                Vector3D.Core.AudioManager.Instance?.PlayWallClimbSound();
            }
        }

        private void EndWallRunAndLand()
        {
            currentState = MovementState.Running;
            verticalVelocity = -1.0f;
            wallSide = 0;
            activeWallCollider = null;
            wallRunTimer = 0f;
            airborneTimer = 0f;
            wallCooldownTimer = wallExitCooldown;
            if (visualRoot != null)
            {
                visualRoot.localRotation = defaultVisualLocalRotation;
            }
        }

        private void DisengageWallRun(bool userInitiated)
        {
            currentState = MovementState.Jumping;
            airborneTimer = 0f;
            verticalVelocity = wallDisengageHopForce;
            wallCooldownTimer = wallExitCooldown;
            wallSide = 0;
            activeWallCollider = null;
            justStartedWallRun = false;
        }

        private void ActivateWallJumpMode()
        {
            currentState = MovementState.WallAttached;
            wallAttachedTimer = 0f;
            overrideWallInput = false;
            overrideLeft = false;
            overrideRight = false;
            injectedShift = false;
            injectedJumpDir = 0;
            wallJumpTimer = 0f;
            verticalVelocity = 0f;
            lateralVelocity = 0f;
            airborneTimer = 0f;
            jumpBufferTimer = 0f;
            slideBufferTimer = 0f;
            currentAttachedWallX = targetWallX;

            // If attached to Left wall (-1), next jump must be to Right wall (+1, D key)
            // If attached to Right wall (+1), next jump must be to Left wall (-1, A key)
            nextRequiredDirection = (wallSide == -1) ? 1 : -1;
        }

        private void ExecuteWallJumpTransfer(int toSide)
        {
            currentState = MovementState.WallJumpTransferring;
            targetWallSide = toSide;
            overrideWallInput = false;
            overrideLeft = false;
            overrideRight = false;
            injectedShift = false;
            injectedJumpDir = 0;
            wallJumpTimer = 0f;
            verticalVelocity = (transform.position.y >= 6.2f) ? 1.5f : wallJumpUpImpulse;
            lateralVelocity = 0f;

            // Determine target wall X
            Vector3 probeDir = toSide == 1 ? Vector3.right : -Vector3.right;
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            float safeBuffer = (characterController != null ? characterController.radius : 0.35f) + 0.12f;

            if (Physics.Raycast(origin, probeDir, out RaycastHit hit, 6.0f, playerLayerMask, QueryTriggerInteraction.Ignore) && IsWallRunObstacle(hit.collider))
            {
                targetWallJumpX = toSide == 1 ? (hit.point.x - safeBuffer) : (hit.point.x + safeBuffer);
            }
            else
            {
                float offsetFromCenter = currentAttachedWallX - _trackCenterX;
                targetWallJumpX = _trackCenterX - offsetFromCenter;
            }
        }

        private void AttachToWall(int side, RaycastHit hit)
        {
            currentState = MovementState.WallAttached;
            wallSide = side;
            wallAttachedTimer = 0f;
            overrideWallInput = false;
            overrideLeft = false;
            overrideRight = false;
            injectedShift = false;
            injectedJumpDir = 0;
            wallJumpTimer = 0f;
            verticalVelocity = 0f;
            lateralVelocity = 0f;

            if (hit.collider != null)
            {
                activeWallCollider = hit.collider;
                float safeBuffer = (characterController != null ? characterController.radius : 0.35f) + 0.12f;
                currentAttachedWallX = side == 1 ? (hit.point.x - safeBuffer) : (hit.point.x + safeBuffer);
            }
            else
            {
                currentAttachedWallX = targetWallJumpX;
            }

            // Alternating input: reverse next required direction
            nextRequiredDirection = (side == 1) ? -1 : 1;
        }

        private void ExitWallJumpTraversal(bool reachedPlatform)
        {
            wallSide = 0;
            activeWallCollider = null;
            overrideWallInput = false;
            overrideLeft = false;
            overrideRight = false;
            injectedShift = false;
            injectedJumpDir = 0;
            wallJumpTimer = 0f;
            wallAttachedTimer = 0f;
            nextRequiredDirection = 0;
            leftWallBufferTimer = 0f;
            rightWallBufferTimer = 0f;
            jumpBufferTimer = 0f;
            slideBufferTimer = 0f;

            if (reachedPlatform)
            {
                wallCooldownTimer = wallExitCooldown;
                if (visualRoot != null)
                {
                    visualRoot.localRotation = defaultVisualLocalRotation;
                }
            }
        }

        private void UpdateVisualTilt()
        {
            if (visualRoot == null) return;
            float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;

            Quaternion targetRot = defaultVisualLocalRotation;

            if (currentState == MovementState.WallRunning || currentState == MovementState.WallAttached)
            {
                float targetZAngle = wallSide == -1 ? -wallTiltAngle : wallTiltAngle;
                targetRot = defaultVisualLocalRotation * Quaternion.Euler(0f, 0f, targetZAngle);
            }
            else if (currentState == MovementState.WallJumpTransferring)
            {
                float targetZAngle = targetWallSide == -1 ? -wallTiltAngle : wallTiltAngle;
                targetRot = defaultVisualLocalRotation * Quaternion.Euler(0f, 0f, targetZAngle);
            }
            else if (currentState == MovementState.Vaulting)
            {
                targetRot = defaultVisualLocalRotation * Quaternion.Euler(16f, 0f, 0f);
            }
            else if (isStumbling)
            {
                float stumbleRatio = Mathf.Clamp01(stumbleTimer / stumbleDuration);
                float stumblePitch = Mathf.Sin(stumbleRatio * Mathf.PI) * 22f;
                targetRot = defaultVisualLocalRotation * Quaternion.Euler(stumblePitch, 0f, 0f);
            }

            // Frame-rate independent exponential smoothing
            float t = 1f - Mathf.Exp(-wallTiltSpeed * dt);
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, targetRot, t);
        }

        #endregion

        #region Movement State Machine

        private void UpdateStateMachine()
        {
            bool grounded = characterController.isGrounded;
            float dt = Time.deltaTime;

            switch (currentState)
            {
                case MovementState.Running:
                    if (!grounded)
                    {
                        airborneTimer += dt;
                        if (airborneTimer > coyoteTime)
                        {
                            currentState = MovementState.Falling;
                        }
                    }
                    else
                    {
                        airborneTimer = 0f;
                        verticalVelocity = -2.0f;
                    }

                    if (jumpBufferTimer > 0f && (grounded || airborneTimer <= coyoteTime))
                    {
                        if (TryDetectVaultObstacle(out Collider vaultCol, out float distToObs))
                        {
                            VaultQuality q = EvaluateVaultQuality(distToObs);
                            ExecuteVault(vaultCol, q);
                        }
                        else
                        {
                            ExecuteJump();
                        }
                        return;
                    }

                    if (slideBufferTimer > 0f && grounded)
                    {
                        ExecuteSlide();
                        return;
                    }
                    break;

                case MovementState.Vaulting:
                    vaultTimer += dt;
                    if (vaultTimer >= vaultDuration)
                    {
                        currentState = grounded ? MovementState.Running : MovementState.Falling;
                        verticalVelocity = -2.0f;
                        currentVaultQuality = VaultQuality.None;
                    }
                    break;

                case MovementState.Jumping:
                    airborneTimer += dt;
                    verticalVelocity += gravity * dt;
                    verticalVelocity = Mathf.Max(verticalVelocity, maxFallSpeed);

                    // Mid-air contextual vault if player jumped slightly early into a vault block
                    if (TryDetectVaultObstacle(out Collider airVaultCol, out float airDist) && transform.position.y < airVaultCol.bounds.max.y + 0.2f)
                    {
                        VaultQuality q = EvaluateVaultQuality(airDist);
                        ExecuteVault(airVaultCol, q);
                        return;
                    }

                    if (wallCooldownTimer <= 0f && CheckWallRunSurface(out int jumpSide, out RaycastHit jumpHit))
                    {
                        bool inputMatches = (jumpSide == -1 && (leftWallBufferTimer > 0f || isLeftWallHeld)) ||
                                            (jumpSide == 1 && (rightWallBufferTimer > 0f || isRightWallHeld));
                        if (inputMatches)
                        {
                            leftWallBufferTimer = 0f;
                            rightWallBufferTimer = 0f;
                            StartWallRun(jumpSide, jumpHit);
                            return;
                        }
                    }

                    if (verticalVelocity <= 0f && grounded && airborneTimer > 0.08f)
                    {
                        Land();
                    }
                    break;

                case MovementState.Falling:
                    airborneTimer += dt;
                    verticalVelocity += gravity * dt;
                    verticalVelocity = Mathf.Max(verticalVelocity, maxFallSpeed);

                    if (wallCooldownTimer <= 0f && CheckWallRunSurface(out int fallSide, out RaycastHit fallHit))
                    {
                        bool inputMatches = (fallSide == -1 && (leftWallBufferTimer > 0f || isLeftWallHeld)) ||
                                            (fallSide == 1 && (rightWallBufferTimer > 0f || isRightWallHeld));
                        if (inputMatches)
                        {
                            leftWallBufferTimer = 0f;
                            rightWallBufferTimer = 0f;
                            StartWallRun(fallSide, fallHit);
                            return;
                        }
                    }

                    if (jumpBufferTimer > 0f && airborneTimer <= coyoteTime)
                    {
                        ExecuteJump();
                        return;
                    }

                    if (grounded && airborneTimer > 0.04f)
                    {
                        Land();
                    }
                    break;

                case MovementState.WallRunning:
                    wallRunTimer += dt;

                    // Vertical climb physics via Space key
                    if (verticalVelocity > 0f)
                    {
                        verticalVelocity += wallClimbGravity * dt;
                        if (verticalVelocity < 0f)
                        {
                            verticalVelocity = 0f; // Settle at new climbed altitude along the wall
                        }
                    }
                    else if (verticalVelocity < 0f)
                    {
                        verticalVelocity = Mathf.MoveTowards(verticalVelocity, -0.6f, 6.0f * dt);
                    }

                    // Soft dampening near max climb ceiling
                    if (transform.position.y >= maxWallClimbY && verticalVelocity > 0f)
                    {
                        verticalVelocity = Mathf.MoveTowards(verticalVelocity, 0f, 20.0f * dt);
                    }

                    // Check if upper platform is reached while climbing
                    Vector3 downProbe = transform.position + Vector3.up * 0.2f;
                    bool hasFloorBelow = Physics.Raycast(downProbe, Vector3.down, out RaycastHit wallFloorHit, 1.4f, playerLayerMask, QueryTriggerInteraction.Ignore);
                    Vector3 forwardDownProbe = transform.position + Vector3.forward * 0.8f + Vector3.up * 0.2f;
                    bool forwardFloor = Physics.Raycast(forwardDownProbe, Vector3.down, out RaycastHit fwdFloorHit, 1.6f, playerLayerMask, QueryTriggerInteraction.Ignore);

                    if (transform.position.y >= 5.8f && (grounded || (hasFloorBelow && wallFloorHit.point.y >= 5.7f) || (forwardFloor && fwdFloorHit.point.y >= 5.7f)))
                    {
                        EndWallRunAndLand();
                        return;
                    }

                    Vector3 probeDir = wallSide == -1 ? -Vector3.right : Vector3.right;
                    Vector3 origin = transform.position + Vector3.up * 1.0f;

                    bool stillTouching = Physics.Raycast(origin, probeDir, out RaycastHit ongoingHit, wallDetectionRange + 0.6f, playerLayerMask, QueryTriggerInteraction.Ignore) &&
                                         IsWallRunObstacle(ongoingHit.collider);

                    bool reachedWallEnd = activeWallCollider != null && (transform.position.z >= activeWallCollider.bounds.max.z - 0.15f);

                    if (!stillTouching || reachedWallEnd || wallRunTimer > wallRunMaxDuration)
                    {
                        DisengageWallRun(userInitiated: false);
                        return;
                    }
                    else
                    {
                        float safeBuffer = (characterController != null ? characterController.radius : 0.35f) + 0.12f;
                        targetWallX = wallSide == -1 ? (ongoingHit.point.x + safeBuffer) : (ongoingHit.point.x - safeBuffer);
                    }
                    break;

                case MovementState.WallAttached:
                    wallAttachedTimer += dt;
                    verticalVelocity = 0f;

                    bool floorBelowAttached = Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit attachGroundHit, 0.6f, playerLayerMask, QueryTriggerInteraction.Ignore);
                    if ((grounded || (floorBelowAttached && attachGroundHit.point.y >= 5.7f)) && transform.position.y > 4.0f)
                    {
                        ExitWallJumpTraversal(true);
                        Land();
                        return;
                    }

                    if (wallAttachedTimer > wallAttachedMaxTime)
                    {
                        ExitWallJumpTraversal(false);
                        currentState = MovementState.Falling;
                        return;
                    }
                    break;

                case MovementState.WallJumpTransferring:
                    wallJumpTimer += dt;
                    verticalVelocity += wallJumpGravity * dt;

                    // 1. Check if landed on upper platform during transfer
                    bool floorBelowTransfer = Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit transferGroundHit, 0.6f, playerLayerMask, QueryTriggerInteraction.Ignore);
                    if ((grounded || (floorBelowTransfer && transferGroundHit.point.y >= 5.7f)) && transform.position.y > 4.0f && verticalVelocity <= 1.5f)
                    {
                        ExitWallJumpTraversal(true);
                        Land();
                        return;
                    }

                    // 2. Check for attachment with the target wall
                    float distToTarget = Mathf.Abs(transform.position.x - targetWallJumpX);
                    Vector3 probeTargetDir = targetWallSide == 1 ? Vector3.right : -Vector3.right;
                    Vector3 probeTargetOrigin = transform.position + Vector3.up * 1.0f;

                    bool targetWallInSight = Physics.Raycast(probeTargetOrigin, probeTargetDir, out RaycastHit targetHit, wallDetectionRange, playerLayerMask, QueryTriggerInteraction.Ignore) && IsWallRunObstacle(targetHit.collider);

                    float ccRadius = characterController != null ? characterController.radius : 0.35f;
                    if ((distToTarget <= wallAttachmentTolerance || (targetWallInSight && targetHit.distance <= (ccRadius + 0.15f))) && wallJumpTimer > 0.12f)
                    {
                        AttachToWall(targetWallSide, targetWallInSight ? targetHit : default);
                        return;
                    }

                    // 3. Max transfer time exceeded without attachment
                    if (wallJumpTimer >= wallJumpMaxTransferTime)
                    {
                        ExitWallJumpTraversal(false);
                        currentState = MovementState.Falling;
                        return;
                    }
                    break;

                case MovementState.Sliding:
                    if (!grounded)
                    {
                        EndSlideDirect();
                        currentState = MovementState.Falling;
                        return;
                    }

                    verticalVelocity = -2.0f;
                    slideTimer -= dt;

                    if (jumpBufferTimer > 0f && HasHeadroom())
                    {
                        EndSlideDirect();
                        if (TryDetectVaultObstacle(out Collider slideVaultCol, out float slideDist))
                        {
                            VaultQuality q = EvaluateVaultQuality(slideDist);
                            ExecuteVault(slideVaultCol, q);
                        }
                        else
                        {
                            ExecuteJump();
                        }
                        return;
                    }

                    if (slideTimer <= 0f && HasHeadroom())
                    {
                        EndSlideDirect();
                        currentState = MovementState.Running;
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

            if (jumpBufferTimer > 0f)
            {
                if (TryDetectVaultObstacle(out Collider vaultCol))
                    ExecuteVault(vaultCol);
                else
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

            characterController.height = slidingHeight;
            characterController.center = slidingCenter;
        }

        private void EndSlideDirect()
        {
            characterController.height = standingHeight;
            characterController.center = standingCenter;
        }

        public bool HasHeadroom()
        {
            if (characterController == null) return true;

            float radius = characterController.radius * 0.85f;
            Vector3 bottomSphere = transform.position + Vector3.up * (slidingHeight - radius);
            float castDistance = (standingHeight - slidingHeight) + 0.1f;

            bool blocked = Physics.SphereCast(
                bottomSphere,
                radius,
                Vector3.up,
                out _,
                castDistance,
                headroomLayerMask,
                QueryTriggerInteraction.Collide
            );

            return !blocked;
        }

        #endregion

        #region Authoritative Movement

        private void ApplyAuthoritativeMovement()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 1. Deterministic Vault Interpolation
            if (currentState == MovementState.Vaulting)
            {
                float t = Mathf.Clamp01(vaultTimer / vaultDuration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                float newZ = Mathf.Lerp(vaultStartPos.z, vaultTargetZ, smoothT);
                float arcY = Mathf.Sin(t * Mathf.PI) * (vaultPeakY - vaultStartPos.y);
                float newY = vaultStartPos.y + arcY;
                float newX = Mathf.SmoothDamp(transform.position.x, _trackCenterX, ref lateralVelocity, lateralSmoothTime);

                Vector3 targetPos = new Vector3(newX, newY, newZ);
                characterController.Move(targetPos - transform.position);
                return;
            }

            // 2. Wall Running Motion
            if (currentState == MovementState.WallRunning)
            {
                currentSpeed = baseSpeed * wallRunSpeedMultiplier;
                float newX = Mathf.SmoothDamp(transform.position.x, targetWallX, ref lateralVelocity, 0.05f);
                float stepX = newX - transform.position.x;

                Vector3 moveStep = new Vector3(stepX, verticalVelocity * dt, currentSpeed * dt);
                characterController.Move(moveStep);
                return;
            }

            // 2b. Vertical Wall-Jump Traversal Motion
            if (currentState == MovementState.WallAttached)
            {
                currentSpeed = 0f;
                float stepX = currentAttachedWallX - transform.position.x;
                Vector3 moveStep = new Vector3(stepX, verticalVelocity * dt, 0f);
                characterController.Move(moveStep);
                return;
            }

            if (currentState == MovementState.WallJumpTransferring)
            {
                currentSpeed = wallJumpForwardSpeed;
                float newX = Mathf.MoveTowards(transform.position.x, targetWallJumpX, wallJumpHorizontalSpeed * dt);
                float stepX = newX - transform.position.x;

                Vector3 moveStep = new Vector3(stepX, verticalVelocity * dt, currentSpeed * dt);
                characterController.Move(moveStep);
                return;
            }

            // 3. Standard Running / Jumping / Sliding Motion
            float accelRatio = Mathf.Clamp01(runTimer / accelerationTime);
            float targetBase = Mathf.SmoothStep(0f, baseSpeed, accelRatio);
            float speed = targetBase * (isSprinting ? sprintMultiplier : 1.0f);

            if (isStumbling)
            {
                float ratio = Mathf.Clamp01(stumbleTimer / stumbleDuration);
                float mult = Mathf.Lerp(1.0f, stumbleSpeedMultiplier, ratio);
                speed *= mult;
            }

            currentSpeed = speed;

            // SmoothDamp lateral centering prevents jerky snapping after wall-runs
            float dampedX = Mathf.SmoothDamp(transform.position.x, _trackCenterX, ref lateralVelocity, lateralSmoothTime);
            float lateralStep = dampedX - transform.position.x;

            Vector3 normalStep = new Vector3(lateralStep, verticalVelocity * dt, currentSpeed * dt);
            characterController.Move(normalStep);
        }

        #endregion

        #region Collisions & Obstacles

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Finish"))
            {
                if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                {
                    return;
                }

                FinishLine finishLine = other.GetComponent<FinishLine>() ?? other.GetComponentInParent<FinishLine>();
                if (finishLine != null)
                {
                    if (GameManager.Instance != null && GameManager.Instance.CurrentLevelIndex != finishLine.TargetLevelIndex)
                    {
                        return;
                    }
                }

                if (Mathf.Abs(other.transform.position.x - transform.position.x) < 5.0f)
                {
                    ReachFinishLine();
                    return;
                }
            }

            if (other.CompareTag("Obstacle_Vault"))
            {
                if (IsVaulting || (IsJumping && transform.position.y >= other.bounds.max.y - 0.35f))
                {
                    lastClearedObstacleZ = other.transform.position.z;
                    return;
                }

                // If player buffered jump right as they hit the trigger, vault seamlessly
                if (jumpBufferTimer > 0f)
                {
                    ExecuteVault(other);
                    return;
                }

                if (Mathf.Abs(other.transform.position.z - lastClearedObstacleZ) > 1.5f)
                {
                    // Missed the vault interval!
                    ExecuteVault(other, VaultQuality.Imperfect);
                    TriggerStumble(stumbleDuration, other.transform.position.z, true);
                }
                return;
            }

            if (other.CompareTag("Obstacle_Slide"))
            {
                if (IsSliding)
                {
                    lastClearedObstacleZ = other.transform.position.z;
                    return;
                }

                // If player buffered slide right at the barrier, slide seamlessly
                if (slideBufferTimer > 0f && IsGrounded)
                {
                    ExecuteSlide();
                    lastClearedObstacleZ = other.transform.position.z;
                    return;
                }

                if (Mathf.Abs(other.transform.position.z - lastClearedObstacleZ) > 1.5f)
                {
                    TriggerStumble(stumbleDuration, other.transform.position.z, false);
                }
                return;
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.z < -0.5f)
            {
                if (hit.gameObject.CompareTag("Obstacle_Vault") && !IsJumping && !IsVaulting)
                {
                    if (jumpBufferTimer > 0f)
                    {
                        ExecuteVault(hit.collider);
                        return;
                    }

                    if (Mathf.Abs(hit.transform.position.z - lastClearedObstacleZ) > 1.5f)
                    {
                        // Collided with obstacle -> Imperfect Jump!
                        ExecuteVault(hit.collider, VaultQuality.Imperfect);
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

            if (currentState == MovementState.WallRunning)
            {
                DisengageWallRun(userInitiated: false);
            }

            if (IsSliding && HasHeadroom())
            {
                EndSlideDirect();
                currentState = MovementState.Running;
            }
        }

        /// <summary>
        /// Connected to VaultDetector child trigger collider.
        /// </summary>
        public void SetInVaultTrigger(bool inTrigger, Transform obstacleTransform)
        {
            inVaultZone = inTrigger;
            pendingVaultCollider = (inTrigger && obstacleTransform != null) ? obstacleTransform.GetComponent<Collider>() : null;

            // Immediate execution if jump was already buffered before entering the trigger zone
            if (inVaultZone && pendingVaultCollider != null && jumpBufferTimer > 0f && currentState != MovementState.Vaulting)
            {
                float frontZ = pendingVaultCollider.bounds.min.z;
                float playerFrontZ = transform.position.z + (characterController != null ? characterController.radius : 0.35f);
                float dist = Mathf.Max(0f, frontZ - playerFrontZ);
                VaultQuality q = EvaluateVaultQuality(dist);
                ExecuteVault(pendingVaultCollider, q);
            }
        }

        #endregion

        #region Animation & Visuals

        private void UpdateAnimator()
        {
            if (animator == null) return;
            animator.SetBool(AnimIsJumping, IsJumping);
            animator.SetBool(AnimIsSliding, IsSliding);
            animator.SetBool(AnimIsVaulting, IsVaulting);
            if (hasAnimVaultType) animator.SetInteger(AnimVaultType, (int)currentVaultQuality);
            if (hasAnimFalling) animator.SetBool(AnimIsFalling, currentState == MovementState.Falling);
            if (hasAnimWallRunning) animator.SetBool(AnimIsWallRunning, currentState == MovementState.WallRunning || currentState == MovementState.WallAttached);
            if (hasAnimStumbling) animator.SetBool(AnimIsStumbling, isStumbling);
            if (hasAnimGrounded) animator.SetBool(AnimIsGrounded, characterController != null && characterController.isGrounded);
        }

        #endregion

        #region Goal & Teleport API

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

        public void TeleportToStart(Vector3 spawnPoint)
        {
            InitializeComponentsOnce();

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            transform.position = spawnPoint;
            transform.rotation = Quaternion.identity;

            if (characterController != null)
            {
                characterController.height = standingHeight;
                characterController.center = standingCenter;
                characterController.enabled = true;
            }

            _trackCenterX = spawnPoint.x;
            currentSpeed = 0f;
            verticalVelocity = -2.0f;
            lateralVelocity = 0f;
            runTimer = 0f;
            airborneTimer = 0f;
            currentState = MovementState.Running;
            isSprinting = false;
            isStumbling = false;
            hasCompletedLevel = false;
            inVaultZone = false;
            pendingVaultCollider = null;
            vaultTimer = 0f;
            currentVaultQuality = VaultQuality.None;
            vaultRatingDisplayTimer = 0f;
            sprintTimer = 0f;
            sprintCooldownTimer = 0f;
            jumpBufferTimer = 0f;
            slideBufferTimer = 0f;
            lastStumbleZ = -999f;
            lastClearedObstacleZ = -999f;
            wallSide = 0;
            activeWallCollider = null;
            wallRunTimer = 0f;
            wallCooldownTimer = 0f;
            leftWallBufferTimer = 0f;
            rightWallBufferTimer = 0f;
            isLeftWallHeld = false;
            isRightWallHeld = false;
            overrideWallInput = false;
            overrideLeft = false;
            overrideRight = false;
            injectedShift = false;
            injectedJumpDir = 0;
            wallJumpTimer = 0f;
            wallAttachedTimer = 0f;
            currentAttachedWallX = 0f;
            targetWallJumpX = 0f;
            targetWallSide = 0;
            nextRequiredDirection = 0;

            if (visualRoot != null)
            {
                visualRoot.localRotation = defaultVisualLocalRotation;
            }
        }

        public void RestartRun()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RestartGame();
            }
            else
            {
                TeleportToStart(new Vector3(_trackCenterX, 1.1f, 0f));
            }
        }

        private void CheckRestartInput()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
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
        }

        private void OnGUI()
        {
            if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            if (hasCompletedLevel || currentState == MovementState.Dead)
            {
                return;
            }

            InitStyles();

            GUI.Box(new Rect(15, 15, 290, 115), GUIContent.none);

            GUILayout.BeginArea(new Rect(25, 20, 270, 105));
            int levelNum = GameManager.Instance != null ? GameManager.Instance.CurrentLevelIndex : 1;
            GUILayout.Label($"VECTOR // SECTOR {levelNum}", headerStyle);
            GUILayout.Label($"SPEED: {currentSpeed:F1} m/s", statStyle);
            GUILayout.Label($"DISTANCE: {Mathf.Max(0f, transform.position.z):F0} m / {finishZ:F0} m", statStyle);

            string sprintStatus = isSprinting ? "SPRINTING!" : (sprintCooldownTimer <= 0f ? "READY [Shift/E]" : $"COOLDOWN ({sprintCooldownTimer:F1}s)");
            sprintStyle.normal.textColor = isSprinting ? Color.cyan : (sprintCooldownTimer <= 0f ? Color.green : Color.yellow);
            GUILayout.Label($"BURST: {sprintStatus}", sprintStyle);
            GUILayout.EndArea();

            GUI.Box(new Rect(15, Screen.height - 55, 680, 40), GUIContent.none);
            string controlsHint = "CONTROLS: [W/Space] Jump/Vault | [S] Slide | [A] Left Wall | [D] Right Wall | [Shift/E] Sprint";
            if (currentState == MovementState.WallRunning)
            {
                controlsHint = "WALL RUN: [Shift/E] Alternate Wall-Jumps | [Space] Climb Height | [S/Opposite] Jump Off";
            }
            else if (currentState == MovementState.WallAttached)
            {
                controlsHint = nextRequiredDirection == 1 ? "WALL JUMP: Press [D] -> Launch toward Right Wall" : "WALL JUMP: Press [A] -> Launch toward Left Wall";
            }
            GUI.Label(new Rect(25, Screen.height - 47, 660, 30), controlsHint, hintStyle);

            if (vaultRatingDisplayTimer > 0f)
            {
                vaultRatingDisplayTimer -= Time.deltaTime;
                GUIStyle ratingStyle = new GUIStyle(headerStyle);
                ratingStyle.alignment = TextAnchor.MiddleCenter;
                ratingStyle.fontSize = 22;
                ratingStyle.fontStyle = FontStyle.Bold;
                ratingStyle.normal.textColor = vaultRatingColor;

                float alpha = Mathf.Clamp01(vaultRatingDisplayTimer / 0.3f);
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Box(new Rect(Screen.width * 0.5f - 220, 20, 440, 44), GUIContent.none);
                GUI.Label(new Rect(Screen.width * 0.5f - 220, 25, 440, 34), vaultRatingText, ratingStyle);
                GUI.color = oldColor;
            }

            if (isStumbling)
            {
                GUI.Label(new Rect(Screen.width / 2f - 180, 80, 360, 40), "STUMBLED! HUNTER CLOSING IN!", stumbleStyle);
            }
        }

        #endregion
    }
}