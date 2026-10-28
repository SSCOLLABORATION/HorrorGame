using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Vector3D.Core;

namespace VectorParkour
{
    /// <summary>
    /// The Chaser AI (Hunter/Enforcer) inspired by Vector.
    /// Dynamically rubber-bands behind the player, surges forward when the player stumbles,
    /// autonomously vaults/slides/traverses chasms, and triggers Game Over upon catching the runner.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ChaserAI : MonoBehaviour
    {
        [Header("Target & Speed Settings")]
        [SerializeField] private Transform targetPlayer;
        [SerializeField] private float baseFollowDistance = 10.0f;
        [SerializeField] private float rubberBandAccSpeed = 8.0f;
        [SerializeField] private float creepRate = 0.12f;
        [SerializeField] private float catchDistance = 0.75f;

        [Header("Obstacle Autonav")]
        [SerializeField] private float detectionRange = 2.6f;
        [SerializeField] private float jumpForce = 9.2f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float vaultDuration = 0.28f;
        [SerializeField] private float slideDuration = 0.65f;

        [Header("References")]
        [SerializeField] private Transform visualRoot;

        // Cached Animator Hashes
        private static readonly int AnimIsJumping = Animator.StringToHash("IsJumping");
        private static readonly int AnimIsSliding = Animator.StringToHash("IsSliding");
        private static readonly int AnimIsVaulting = Animator.StringToHash("IsVaulting");

        private CharacterController characterController;
        private PlayerParkourController playerController;
        private Animator animator;
        private bool componentsInitialized = false;

        private float currentSpeed = 0f;
        private float verticalVelocity = 0f;
        private float lateralVelocity = 0f;
        private float originalHeight = 1.8f;
        private Vector3 originalCenter = new Vector3(0f, 0.9f, 0f);
        private float _trackCenterX = 0f;

        // States
        private bool isVaulting = false;
        private bool isSliding = false;
        private bool isTraversingGap = false;
        private float vaultTimer = 0f;
        private float slideTimer = 0f;
        private float gapTraverseTimer = 0f;
        private Vector3 vaultStartPos;
        private float vaultTargetZ;
        private float vaultPeakY;

        private bool hasCaughtPlayer = false;
        private float creepOffset = 0f;

        // Airborne tracking & debounce
        private bool isAIJumping = false;
        private float airborneTimer = 0f;
        public bool IsJumping => isAIJumping || (airborneTimer > 0.15f && !isVaulting && !isTraversingGap);

        private float settleTimer = 0f;

        private Vector3 defaultVisualScale = Vector3.one;
        private Vector3 defaultVisualLocalPos = Vector3.zero;

        public float DistanceToPlayer
        {
            get
            {
                if (targetPlayer == null) return 999f;
                return targetPlayer.position.z - transform.position.z;
            }
        }

        private void InitializeComponentsOnce()
        {
            if (componentsInitialized && characterController != null) return;

            characterController = GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.skinWidth = 0.015f;
                characterController.minMoveDistance = 0.0001f;
                if (characterController.height > 1.2f)
                {
                    originalHeight = characterController.height;
                    originalCenter = characterController.center;
                }
            }

            if (visualRoot == null || visualRoot == transform)
            {
                Transform foundVisual = transform.Find("Visual");
                visualRoot = foundVisual != null ? foundVisual : transform;
            }

            if (visualRoot != null && visualRoot != transform)
            {
                defaultVisualScale = visualRoot.localScale;
                defaultVisualLocalPos = visualRoot.localPosition;
            }

            animator = GetComponentInChildren<Animator>();
            componentsInitialized = true;
        }

        private void Awake()
        {
            InitializeComponentsOnce();
        }

        private void Start()
        {
            InitializeComponentsOnce();
            FindPlayerReference();

            if (playerController != null && characterController != null)
            {
                var pcc = playerController.GetComponent<CharacterController>();
                if (pcc != null) Physics.IgnoreCollision(characterController, pcc, true);
            }

            _trackCenterX = transform.position.x;
            currentSpeed = 0f;
            verticalVelocity = -1.2f;
            settleTimer = 0f;
            creepOffset = 0f;
        }

        private void FindPlayerReference()
        {
            if (targetPlayer == null)
            {
                PlayerParkourController pc = UnityEngine.Object.FindFirstObjectByType<PlayerParkourController>();
                if (pc != null)
                {
                    targetPlayer = pc.transform;
                    playerController = pc;
                }
            }
            else if (playerController == null)
            {
                playerController = targetPlayer.GetComponent<PlayerParkourController>();
            }
        }

        private void Update()
        {
            if (!componentsInitialized) InitializeComponentsOnce();

            if (targetPlayer == null)
            {
                FindPlayerReference();
                if (targetPlayer == null) return;
            }

            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            if (hasCaughtPlayer || (playerController != null && playerController.HasCompletedLevel))
            {
                CheckRestartInput();
                return;
            }

            CheckPlayerTrackConditions();

            settleTimer += Time.deltaTime;

            UpdateAI();
            UpdateVisualFeedback();
            UpdateAnimator();
            CheckCatchCondition();
            CheckFallOutOfBounds();
        }

        private void CheckPlayerTrackConditions()
        {
            if (targetPlayer != null && targetPlayer.position.y <= -5f)
            {
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.TriggerGameOver(false);
                }
            }
        }

        private void UpdateAI()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float playerZ = targetPlayer.position.z;
            float currentZ = transform.position.z;
            float distance = playerZ - currentZ;

            float pBaseSpeed = playerController != null ? playerController.BaseSpeed : 9.5f;
            float pCurrentSpeed = playerController != null ? playerController.CurrentSpeed : 9.5f;
            bool playerStumbling = playerController != null && playerController.IsStumbling;
            bool playerSprinting = playerController != null && playerController.IsSprinting;

            // Rubber-band creep management
            if (playerSprinting)
            {
                creepOffset = Mathf.Max(0f, creepOffset - 2.5f * dt);
            }
            else if (playerStumbling)
            {
                // Permanently close the follow distance when the player stumbles so 1-2 stumbles = caught
                creepOffset = Mathf.Min(baseFollowDistance - 0.3f, creepOffset + 7.5f * dt);
            }
            else
            {
                creepOffset = Mathf.Min(baseFollowDistance - 2.2f, creepOffset + creepRate * dt);
            }

            float desiredDistance = Mathf.Max(0.3f, baseFollowDistance - creepOffset);

            float targetSpeed;
            if (playerStumbling)
            {
                // Surge aggressively past player's slowed speed to tackle them
                targetSpeed = pBaseSpeed * 1.22f;
            }
            else
            {
                float gapError = distance - desiredDistance;
                targetSpeed = pCurrentSpeed + (gapError * 0.85f);
                targetSpeed = Mathf.Clamp(targetSpeed, pBaseSpeed * 0.5f, pBaseSpeed * 1.15f);
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rubberBandAccSpeed * dt);

            // Obstacle anticipation & handling
            if (characterController.isGrounded && !isTraversingGap)
            {
                DetectAndHandleObstaclesAhead();
            }

            // Smooth Vault Interpolation
            if (isVaulting)
            {
                vaultTimer += dt;
                float t = Mathf.Clamp01(vaultTimer / vaultDuration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                float newZ = Mathf.Lerp(vaultStartPos.z, vaultTargetZ, smoothT);
                float arcY = Mathf.Sin(t * Mathf.PI) * (vaultPeakY - vaultStartPos.y);
                float newY = vaultStartPos.y + arcY;
                float newX = Mathf.SmoothDamp(transform.position.x, _trackCenterX, ref lateralVelocity, 0.1f);

                Vector3 targetPos = new Vector3(newX, newY, newZ);
                characterController.Move(targetPos - transform.position);

                if (t >= 1.0f)
                {
                    isVaulting = false;
                    verticalVelocity = -1.2f;
                }
                return;
            }

            // Slide duration timer
            if (isSliding)
            {
                slideTimer -= dt;
                if (slideTimer <= 0f)
                {
                    isSliding = false;
                    characterController.height = originalHeight;
                    characterController.center = originalCenter;
                }
            }

            // Gap Traversal mode (Level 2 wide chasms & wall-run sections)
            if (isTraversingGap)
            {
                gapTraverseTimer += dt;
                verticalVelocity = 0f;

                Vector3 downProbe = transform.position + Vector3.up * 0.5f;
                bool reachedLanding = Physics.Raycast(downProbe, Vector3.down, out RaycastHit groundHit, 1.6f, ~0, QueryTriggerInteraction.Ignore) && groundHit.point.y > -0.5f;

                if (reachedLanding && gapTraverseTimer > 0.4f)
                {
                    isTraversingGap = false;
                    verticalVelocity = -1.2f;
                }
                else if (gapTraverseTimer > 4.0f)
                {
                    isTraversingGap = false;
                }
            }
            else if ((_trackCenterX > 250f && transform.position.z >= 86f && transform.position.z <= 130f) ||
                     (_trackCenterX > 150f && _trackCenterX < 250f && transform.position.z >= 62f && transform.position.z <= 108f))
            {
                // Vertical Shaft (Level 3 or 4): controlled climbing pursuit behind player
                float targetY = (targetPlayer != null) ? Mathf.Clamp(targetPlayer.position.y - 0.4f, 0.5f, 6.1f) : 6.1f;
                float newY = Mathf.MoveTowards(transform.position.y, targetY, 5.0f * dt);
                verticalVelocity = 0f;
                float stepY = newY - transform.position.y;

                float climbDampedX = Mathf.SmoothDamp(transform.position.x, _trackCenterX, ref lateralVelocity, 0.12f);
                float climbLateralStep = climbDampedX - transform.position.x;

                Vector3 climbStep = new Vector3(climbLateralStep, stepY, currentSpeed * dt);
                characterController.Move(climbStep);
                return;
            }
            else if ((_trackCenterX > 250f && transform.position.z >= 178f && transform.position.z <= 192f && transform.position.y > 1.2f) ||
                     (_trackCenterX > 150f && _trackCenterX < 250f && transform.position.z >= 168f && transform.position.z <= 182f && transform.position.y > 1.2f))
            {
                // Rooftop Drop (Level 3 or 4): smooth descent to ground landing platform
                float targetDropY = (targetPlayer != null) ? targetPlayer.position.y : 0.5f;
                float newDropY = Mathf.MoveTowards(transform.position.y, targetDropY, 9.0f * dt);
                verticalVelocity = 0f;
                float stepY = newDropY - transform.position.y;

                float dropDampedX = Mathf.SmoothDamp(transform.position.x, _trackCenterX, ref lateralVelocity, 0.12f);
                float dropLateralStep = dropDampedX - transform.position.x;

                Vector3 dropStep = new Vector3(dropLateralStep, stepY, currentSpeed * dt);
                characterController.Move(dropStep);
                return;
            }
            else
            {
                if (characterController.isGrounded)
                {
                    airborneTimer = 0f;
                    if (verticalVelocity <= 0.1f)
                    {
                        isAIJumping = false;
                        verticalVelocity = -1.2f;
                    }
                }
                else
                {
                    airborneTimer += dt;
                    verticalVelocity += gravity * dt;
                    verticalVelocity = Mathf.Max(verticalVelocity, -28f);
                }
            }

            float dampedX = Mathf.SmoothDamp(transform.position.x, _trackCenterX, ref lateralVelocity, 0.12f);
            float lateralStep = dampedX - transform.position.x;

            Vector3 moveStep = new Vector3(lateralStep, verticalVelocity * dt, currentSpeed * dt);
            characterController.Move(moveStep);
        }

        private void DetectAndHandleObstaclesAhead()
        {
            if (isVaulting || isTraversingGap) return;
            if (_trackCenterX > 250f && transform.position.z >= 84f && transform.position.z <= 130f) return;
            if (_trackCenterX > 150f && _trackCenterX < 250f && transform.position.z >= 60f && transform.position.z <= 108f) return;

            Vector3 originWaist = transform.position + Vector3.up * 0.7f;
            Vector3 originHead = transform.position + Vector3.up * 1.6f;

            // 1. Check for VaultBlock ahead
            if (Physics.Raycast(originWaist, Vector3.forward, out RaycastHit hitWaist, detectionRange, ~0, QueryTriggerInteraction.Collide))
            {
                if (hitWaist.collider.CompareTag("Obstacle_Vault"))
                {
                    StartAIVault(hitWaist.collider);
                    return;
                }
            }

            // 2. Check for SlideBarrier ahead
            if (Physics.Raycast(originHead, Vector3.forward, out RaycastHit hitHead, detectionRange, ~0, QueryTriggerInteraction.Collide))
            {
                if (hitHead.collider.CompareTag("Obstacle_Slide") && !isSliding)
                {
                    StartAISlide();
                    return;
                }
            }

            // 3. Check for Gaps in floor ahead
            Vector3 gapCheck1 = transform.position + Vector3.forward * 1.8f + Vector3.up * 0.3f;
            Vector3 gapCheck2 = transform.position + Vector3.forward * 3.5f + Vector3.up * 0.3f;

            bool floor1 = Physics.Raycast(gapCheck1, Vector3.down, 2.0f, ~0, QueryTriggerInteraction.Ignore);
            bool floor2 = Physics.Raycast(gapCheck2, Vector3.down, 2.0f, ~0, QueryTriggerInteraction.Ignore);

            if (!floor1 && !floor2)
            {
                Vector3 farCheck = transform.position + Vector3.forward * 6.5f + Vector3.up * 0.3f;
                bool floorFar = Physics.Raycast(farCheck, Vector3.down, 3.0f, ~0, QueryTriggerInteraction.Ignore);

                if (!floorFar)
                {
                    isTraversingGap = true;
                    gapTraverseTimer = 0f;
                    verticalVelocity = 0f;
                }
                else
                {
                    isAIJumping = true;
                    verticalVelocity = jumpForce;
                }
            }
        }

        private void StartAIVault(Collider obstacle)
        {
            isVaulting = true;
            vaultTimer = 0f;
            vaultStartPos = transform.position;

            float obstacleTop = obstacle.bounds.max.y;
            vaultPeakY = obstacleTop + 0.35f;
            vaultTargetZ = obstacle.bounds.max.z + 1.3f;
            verticalVelocity = 0f;
        }

        private void StartAISlide()
        {
            isSliding = true;
            slideTimer = slideDuration;
            characterController.height = originalHeight * 0.5f;
            characterController.center = new Vector3(originalCenter.x, originalCenter.y * 0.5f, originalCenter.z);
        }

        private void CheckCatchCondition()
        {
            if (settleTimer < 1.0f || targetPlayer == null) return;

            float xDiff = Mathf.Abs(targetPlayer.position.x - transform.position.x);
            if (xDiff > 3.5f) return;

            float horizontalDistance = targetPlayer.position.z - transform.position.z;
            float verticalDiff = Mathf.Abs(targetPlayer.position.y - transform.position.y);

            if (horizontalDistance <= catchDistance && horizontalDistance >= -0.75f && verticalDiff < 1.8f)
            {
                TriggerCatch();
            }
        }

        private void TriggerCatch()
        {
            hasCaughtPlayer = true;
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerGameOver(false);
            }
            else
            {
                Time.timeScale = 0f;
            }
        }

        private void CheckFallOutOfBounds()
        {
            if (transform.position.y < -5f && targetPlayer != null)
            {
                characterController.enabled = false;
                transform.position = new Vector3(_trackCenterX, targetPlayer.position.y + 0.3f, targetPlayer.position.z - baseFollowDistance);
                characterController.enabled = true;
                verticalVelocity = -1.2f;
                lateralVelocity = 0f;
                currentSpeed = playerController != null ? playerController.CurrentSpeed : 9.5f;
                isTraversingGap = false;
            }
        }

        public void TeleportToStart(Vector3 spawnPoint)
        {
            InitializeComponentsOnce();

            hasCaughtPlayer = false;
            isVaulting = false;
            isSliding = false;
            isTraversingGap = false;
            creepOffset = 0f;

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            transform.position = spawnPoint;
            transform.rotation = Quaternion.identity;
            if (visualRoot != null && visualRoot != transform)
            {
                visualRoot.localRotation = Quaternion.identity;
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = defaultVisualLocalPos;
            }

            if (characterController != null)
            {
                characterController.height = originalHeight;
                characterController.center = originalCenter;
                characterController.enabled = true;
            }

            _trackCenterX = spawnPoint.x;
            currentSpeed = 0f;
            verticalVelocity = -1.2f;
            lateralVelocity = 0f;
            settleTimer = 0f;
            isAIJumping = false;
            airborneTimer = 0f;
            gapTraverseTimer = 0f;
        }

        public void ResetChaser()
        {
            TeleportToStart(new Vector3(_trackCenterX, 1.1f, -baseFollowDistance));
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
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.RestartGame();
                }
                else if (playerController != null)
                {
                    playerController.RestartRun();
                }
            }
        }

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

            float smoothFactor = 1f - Mathf.Exp(-14f * Time.deltaTime);

            Vector3 targetScale = defaultVisualScale;
            Vector3 targetPos = defaultVisualLocalPos;
            Quaternion targetRot = Quaternion.Euler(10f, 0f, 0f);

            if (isSliding)
            {
                targetScale = new Vector3(defaultVisualScale.x, defaultVisualScale.y * 0.5f, defaultVisualScale.z * 1.25f);
                targetRot = Quaternion.Euler(-18f, 0f, 0f);
            }
            else if (isVaulting)
            {
                targetPos = new Vector3(defaultVisualLocalPos.x, 0.2f, defaultVisualLocalPos.z + 0.1f);
                targetRot = Quaternion.Euler(22f, 0f, 0f);
            }

            visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, targetScale, smoothFactor);
            visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, targetPos, smoothFactor);
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, targetRot, smoothFactor);
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;
            animator.SetBool(AnimIsJumping, IsJumping);
            animator.SetBool(AnimIsSliding, isSliding);
            animator.SetBool(AnimIsVaulting, isVaulting);
        }
    }
}