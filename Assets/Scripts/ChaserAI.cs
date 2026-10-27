using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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
        [SerializeField] private float baseFollowDistance = 6.0f;
        [SerializeField] private float rubberBandAccSpeed = 9.0f;
        [SerializeField] private float creepRate = 0.35f; // Creeps closer if player doesn't sprint
        [SerializeField] private float catchDistance = 0.8f;

        [Header("Obstacle Autonav")]
        [SerializeField] private float detectionRange = 2.4f;
        [SerializeField] private float jumpForce = 8.5f;
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

        private void Start()
        {
            FindPlayerReference();
            if (targetPlayer != null)
            {
                currentSpeed = playerController != null ? playerController.BaseSpeed : 9.5f;
            }
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
            if (targetPlayer == null)
            {
                FindPlayerReference();
                if (targetPlayer == null) return;
            }

            if (hasCaughtPlayer)
            {
                CheckRestartInput();
                return;
            }

            UpdateAI();
            UpdateVisualFeedback();
            UpdateAnimator();
            CheckCatchCondition();
        }

        private void UpdateAI()
        {
            float playerZ = targetPlayer.position.z;
            float currentZ = transform.position.z;
            float distance = playerZ - currentZ;

            float pBaseSpeed = playerController != null ? playerController.BaseSpeed : 9.5f;
            float pCurrentSpeed = playerController != null ? playerController.CurrentSpeed : 9.5f;

            // Target speed based on rubber-banding & player status
            float targetSpeed = pBaseSpeed;

            if (playerController != null && playerController.IsStumbling)
            {
                // Player stumbled! Chaser rushes in aggressively to capitalize
                targetSpeed = pBaseSpeed * 1.3f;
            }
            else if (playerController != null && playerController.IsSprinting)
            {
                // Player is sprinting away -> Chaser maintains pursuit without losing track
                targetSpeed = pBaseSpeed * 1.05f;
            }
            else
            {
                // Normal running: Creep closer gradually unless already within dangerous proximity
                if (distance > 4.5f)
                {
                    targetSpeed = pBaseSpeed + creepRate;
                }
                else if (distance < 2.0f)
                {
                    // Match player closely so player has a small reaction window
                    targetSpeed = pCurrentSpeed * 0.98f;
                }
                else
                {
                    targetSpeed = pBaseSpeed + (creepRate * 0.5f);
                }
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rubberBandAccSpeed * Time.deltaTime);

            // Autonomous obstacle anticipation & handling
            DetectAndHandleObstaclesAhead();

            // Vault interpolation handling
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

            // Slide timer handling
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
                if (verticalVelocity < 0f) verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            Vector3 moveDirection = new Vector3(0f, verticalVelocity, currentSpeed);
            characterController.Move(moveDirection * Time.deltaTime);
        }

        private void DetectAndHandleObstaclesAhead()
        {
            if (isVaulting) return;

            Vector3 originWaist = transform.position + Vector3.up * 0.7f;
            Vector3 originHead = transform.position + Vector3.up * 1.6f;
            Vector3 groundCheckAhead = transform.position + Vector3.forward * 2.2f + Vector3.up * 0.3f;

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

            // 3. Check for Gaps in the floor ahead
            if (characterController.isGrounded && !Physics.Raycast(groundCheckAhead, Vector3.down, 1.5f))
            {
                // Floor ends ahead! Leap across the gap
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
            vaultTargetZ = obstacle.bounds.max.z + 1.2f;
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
            float horizontalDistance = targetPlayer.position.z - transform.position.z;
            float verticalDiff = Mathf.Abs(targetPlayer.position.y - transform.position.y);

            if (horizontalDistance <= catchDistance && verticalDiff < 1.6f)
            {
                TriggerCatch();
            }
        }

        private void TriggerCatch()
        {
            hasCaughtPlayer = true;
            Time.timeScale = 0f;
        }

        public void ResetChaser()
        {
            hasCaughtPlayer = false;
            isVaulting = false;
            isSliding = false;
            transform.position = new Vector3(0f, 0.1f, -baseFollowDistance);
            currentSpeed = playerController != null ? playerController.BaseSpeed : 9.5f;
            verticalVelocity = 0f;

            if (characterController != null)
            {
                characterController.height = originalHeight;
                characterController.center = originalCenter;
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

            if (isSliding)
            {
                visualRoot.localScale = new Vector3(defaultVisualScale.x, defaultVisualScale.y * 0.5f, defaultVisualScale.z * 1.25f);
                visualRoot.localPosition = new Vector3(defaultVisualLocalPos.x, -0.45f, defaultVisualLocalPos.z - 0.2f);
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
                visualRoot.localRotation = Quaternion.Euler(10f, 0f, 0f); // Relentless hunter forward lean
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;
            animator.SetBool("IsSliding", isSliding);
            animator.SetBool("IsVaulting", isVaulting);
        }

        private void OnGUI()
        {
            // Game Over / Caught Screen
            if (hasCaughtPlayer)
            {
                // Screen tint
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUIStyle caughtTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 36,
                    fontStyle = FontStyle.Bold
                };
                caughtTitleStyle.normal.textColor = new Color(1f, 0.2f, 0.2f);

                GUIStyle subStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold
                };
                subStyle.normal.textColor = Color.white;

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
