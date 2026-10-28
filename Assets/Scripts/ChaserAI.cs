using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Vector3D.Core;

namespace VectorParkour
{
    /// <summary>
    /// The Chaser AI (Hunter/Enforcer) inspired by Vector.
    /// Spawns 6m behind the player, dynamically rubber-bands with player speed,
    /// surges when the player stumbles, autonomously negotiates obstacles without getting stuck,
    /// and triggers a Game Over when catching the player (<= 0.8m).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ChaserAI : MonoBehaviour
    {
        [Header("Target & Speed Settings")]
        [SerializeField] private Transform targetPlayer;
        [SerializeField] private float baseFollowDistance = 14.0f;
        [SerializeField] private float rubberBandAccSpeed = 6.0f;
        [SerializeField] private float creepRate = 0.04f; // Very slow creep
        [SerializeField] private float catchDistance = 0.5f;

        [Header("Obstacle Autonav")]
        [SerializeField] private float detectionRange = 2.6f;
        [SerializeField] private float jumpForce = 9.2f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float vaultDuration = 0.28f;
        [SerializeField] private float slideDuration = 0.65f;

        [Header("References")]
        [SerializeField] private Transform visualRoot;

        private CharacterController characterController;
        private PlayerParkourController playerController;
        private Animator animator;

        private float currentSpeed = 0f;
        private float verticalVelocity = 0f;
        private float originalHeight = 1.8f;
        private Vector3 originalCenter = new Vector3(0f, 0.9f, 0f);

        // States
        private bool isVaulting = false;
        private bool isSliding = false;
        private float vaultTimer = 0f;
        private float slideTimer = 0f;
        private Vector3 vaultStartPos;
        private float vaultTargetZ;
        private float vaultPeakY;

        private bool hasCaughtPlayer = false;
        private float creepOffset = 0f;

        // Airborne tracking & debounce
        private bool isAIJumping = false;
        private float airborneTimer = 0f;
        public bool IsJumping => isAIJumping || (airborneTimer > 0.15f && !isVaulting);

        // Grace period: allows physics & colliders to settle
        private float settleTimer = 0f;
        private const float SettleGracePeriod = 0.25f;

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

        private void EnsureComponents()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.skinWidth = 0.015f;
                    characterController.minMoveDistance = 0f;
                    originalHeight = characterController.height;
                    originalCenter = characterController.center;
                }
            }

            if (visualRoot == null)
            {
                Transform foundVisual = transform.Find("Visual");
                if (foundVisual != null) visualRoot = foundVisual;
                else visualRoot = transform;
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
            EnsureComponents();
        }

        private void Start()
        {
            EnsureComponents();
            FindPlayerReference();

            // Zero collision with player
            if (playerController != null && characterController != null)
            {
                var pcc = playerController.GetComponent<CharacterController>();
                if (pcc != null) Physics.IgnoreCollision(characterController, pcc, true);
            }

            // Start at 0 forward speed so chaser accelerates alongside the player
            currentSpeed = 0f;
            if (characterController != null) characterController.Move(Vector3.down * 0.5f);
            verticalVelocity = -4f;
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
            EnsureComponents();
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

            // Settle grace period: gently stick to ground
            settleTimer += Time.deltaTime;
            if (settleTimer < SettleGracePeriod)
            {
                if (!characterController.isGrounded)
                {
                    characterController.Move(Vector3.down * 4f * Time.deltaTime);
                }
                return;
            }

            UpdateAI();
            UpdateVisualFeedback();
            UpdateAnimator();
            CheckCatchCondition();
            CheckFallOutOfBounds();
        }

        private void CheckPlayerTrackConditions()
        {
            if (targetPlayer == null) return;

            // Player crossed end of 250m track
            if (targetPlayer.position.z >= 245f)
            {
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.TriggerGameOver(true);
                }
            }
            // Player fell below track
            else if (targetPlayer.position.y <= -5f)
            {
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.TriggerGameOver(false);
                }
            }
        }

        private void UpdateAI()
        {
            float playerZ = targetPlayer.position.z;
            float currentZ = transform.position.z;
            float distance = playerZ - currentZ;

            float pBaseSpeed = playerController != null ? playerController.BaseSpeed : 9.5f;
            float pCurrentSpeed = playerController != null ? playerController.CurrentSpeed : 9.5f;

            // Manage creep distance over time:
            // Sprint pushes hunter back; normal running lets hunter creep closer slowly
            if (playerController != null && playerController.IsSprinting)
            {
                creepOffset = Mathf.Max(0f, creepOffset - 2.5f * Time.deltaTime);
            }
            else if (playerController != null && !playerController.IsStumbling)
            {
                creepOffset = Mathf.Min(3.2f, creepOffset + creepRate * Time.deltaTime);
            }

            float desiredDistance = Mathf.Max(2.5f, baseFollowDistance - creepOffset);

            float targetSpeed;
            if (playerController != null && playerController.IsStumbling)
            {
                // Player made a mistake! Hunter picks up pace
                targetSpeed = pBaseSpeed * 1.15f;
            }
            else
            {
                // Follow comfortably behind without crowding
                float gapError = distance - desiredDistance;
                targetSpeed = pCurrentSpeed + (gapError * 0.5f);
                targetSpeed = Mathf.Clamp(targetSpeed, 0f, pBaseSpeed * 1.05f);
            }

            // Never crowd closer than 8 meters
            if (distance < 8.0f)
            {
                targetSpeed = Mathf.Min(targetSpeed, pCurrentSpeed * 0.75f);
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rubberBandAccSpeed * Time.deltaTime);

            // Autonomous obstacle anticipation & handling
            if (characterController.isGrounded)
            {
                DetectAndHandleObstaclesAhead();
            }

            // Vault interpolation mode
            if (isVaulting)
            {
                vaultTimer += Time.deltaTime;
                float t = Mathf.Clamp01(vaultTimer / vaultDuration);

                float newZ = Mathf.Lerp(vaultStartPos.z, vaultTargetZ, t);
                float arcY = Mathf.Sin(t * Mathf.PI) * (vaultPeakY - vaultStartPos.y);
                float newY = vaultStartPos.y + arcY;

                Vector3 targetPos = new Vector3(0f, newY, newZ);
                Vector3 motion = targetPos - transform.position;
                characterController.Move(motion);

                if (t >= 1.0f)
                {
                    isVaulting = false;
                    verticalVelocity = -2f;
                }
                return;
            }

            // Slide duration timer
            if (isSliding)
            {
                slideTimer -= Time.deltaTime;
                if (slideTimer <= 0f)
                {
                    isSliding = false;
                    characterController.height = originalHeight;
                    characterController.center = originalCenter;
                }
            }

            // Gravity & Vertical physics
            if (characterController.isGrounded)
            {
                airborneTimer = 0f;
                if (verticalVelocity <= 0.1f)
                {
                    isAIJumping = false;
                    verticalVelocity = -4f;
                }
            }
            else
            {
                airborneTimer += Time.deltaTime;
                verticalVelocity += gravity * Time.deltaTime;
                verticalVelocity = Mathf.Max(verticalVelocity, -28f);
            }

            Vector3 moveDirection = new Vector3(0f, verticalVelocity, currentSpeed);
            characterController.Move(moveDirection * Time.deltaTime);
        }

        private void DetectAndHandleObstaclesAhead()
        {
            if (isVaulting) return;

            Vector3 originWaist = transform.position + Vector3.up * 0.7f;
            Vector3 originHead = transform.position + Vector3.up * 1.6f;

            // 1. Check for VaultBlock ahead
            if (Physics.Raycast(originWaist, Vector3.forward, out RaycastHit hitWaist, detectionRange))
            {
                if (hitWaist.collider.CompareTag("Obstacle_Vault"))
                {
                    StartAIVault(hitWaist.collider);
                    return;
                }
            }

            // 2. Check for SlideBarrier ahead
            if (Physics.Raycast(originHead, Vector3.forward, out RaycastHit hitHead, detectionRange))
            {
                if (hitHead.collider.CompareTag("Obstacle_Slide") && !isSliding)
                {
                    StartAISlide();
                    return;
                }
            }

            // 3. Check for Gaps in floor ahead using dual raycasts
            Vector3 gapCheck1 = transform.position + Vector3.forward * 1.8f + Vector3.up * 0.3f;
            Vector3 gapCheck2 = transform.position + Vector3.forward * 2.8f + Vector3.up * 0.3f;

            bool floor1 = Physics.Raycast(gapCheck1, Vector3.down, 2.0f);
            bool floor2 = Physics.Raycast(gapCheck2, Vector3.down, 2.0f);

            if (!floor1 && !floor2)
            {
                // Leap across gap
                isAIJumping = true;
                verticalVelocity = jumpForce;
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
            // Give a 1.5s grace period on start so player gets moving
            if (settleTimer < 1.5f) return;
            if (targetPlayer == null) return;

            float horizontalDistance = targetPlayer.position.z - transform.position.z;
            float verticalDiff = Mathf.Abs(targetPlayer.position.y - transform.position.y);

            // Hunter catches player from behind within catch distance
            if (horizontalDistance <= catchDistance && horizontalDistance >= -0.5f && verticalDiff < 1.5f)
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
                // Hunter fell off a gap -> respawn safely 6m behind player
                characterController.enabled = false;
                transform.position = new Vector3(0f, targetPlayer.position.y + 0.3f, targetPlayer.position.z - baseFollowDistance);
                characterController.enabled = true;
                verticalVelocity = -2f;
                currentSpeed = playerController != null ? playerController.CurrentSpeed : 9.5f;
            }
        }

        public void ResetChaser()
        {
            hasCaughtPlayer = false;
            isVaulting = false;
            isSliding = false;
            creepOffset = 0f;

            characterController.enabled = false;
            transform.position = new Vector3(0f, 0.5f, -baseFollowDistance);
            characterController.enabled = true;

            currentSpeed = 0f;
            verticalVelocity = -4f;
            settleTimer = 0f;
            isAIJumping = false;
            airborneTimer = 0f;

            if (characterController != null)
            {
                characterController.height = originalHeight;
                characterController.center = originalCenter;
            }

            characterController.Move(Vector3.down * 0.5f);
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
                if (playerController != null)
                {
                    playerController.RestartRun();
                }
                else
                {
                    ResetChaser();
                    Time.timeScale = 1f;
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

            if (isSliding)
            {
                visualRoot.localScale = new Vector3(defaultVisualScale.x, defaultVisualScale.y * 0.5f, defaultVisualScale.z * 1.25f);
                visualRoot.localPosition = defaultVisualLocalPos;
                visualRoot.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            }
            else if (isVaulting)
            {
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = new Vector3(defaultVisualLocalPos.x, 0.2f, defaultVisualLocalPos.z + 0.1f);
                visualRoot.localRotation = Quaternion.Euler(22f, 0f, 0f);
            }
            else
            {
                visualRoot.localScale = defaultVisualScale;
                visualRoot.localPosition = defaultVisualLocalPos;
                visualRoot.localRotation = Quaternion.Euler(10f, 0f, 0f);
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;
            animator.SetBool("IsJumping", IsJumping);
            animator.SetBool("IsSliding", isSliding);
            animator.SetBool("IsVaulting", isVaulting);
        }

        private GUIStyle caughtTitleStyle;
        private GUIStyle subStyle;
        private bool stylesInitialized = false;

        private void InitStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            caughtTitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 36,
                fontStyle = FontStyle.Bold
            };
            caughtTitleStyle.normal.textColor = new Color(1f, 0.2f, 0.2f);

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
            if (GameManager.Instance != null) return;

            if (hasCaughtPlayer)
            {
                InitStyles();

                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;

                float boxW = 500;
                float boxH = 200;
                float startX = (Screen.width - boxW) / 2f;
                float startY = (Screen.height - boxH) / 2f;

                GUI.Box(new Rect(startX, startY, boxW, boxH), GUIContent.none);
                GUI.Label(new Rect(startX, startY + 25, boxW, 45), "CAUGHT BY THE HUNTER", caughtTitleStyle);
                GUI.Label(new Rect(startX, startY + 80, boxW, 30), "You couldn't outrun the enforcer.", subStyle);

                if (GUI.Button(new Rect(startX + 150, startY + 130, 200, 45), "RESTART RUN [R]"))
                {
                    if (playerController != null) playerController.RestartRun();
                    else
                    {
                        ResetChaser();
                        Time.timeScale = 1f;
                    }
                }
            }
        }
    }
}
