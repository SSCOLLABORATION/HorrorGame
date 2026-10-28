using UnityEngine;

namespace VectorParkour
{
    public enum CameraSide
    {
        DefaultLeft,
        AlternateRight,
        OverheadTop
    }

    /// <summary>
    /// 3D side-angled chase camera inspired by Vector.
    /// Features zero-GC non-alloc occlusion checks, rock-solid Z lockstep,
    /// SmoothDamp X/Y tracking, frame-rate independent transitions,
    /// and contextual overhead/top-down follow view when between both walls.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VectorFollowCamera : MonoBehaviour
    {
        [Header("Target Tracking")]
        [SerializeField] private Transform target;
        [SerializeField] private Transform chaser;
        [SerializeField] private Vector3 defaultOffset = new Vector3(-3.4f, 1.9f, -4.2f);
        [SerializeField] private Vector3 defaultEulerAngle = new Vector3(9.0f, 24.0f, 0.0f);
        [SerializeField] private Vector3 alternateOffset = new Vector3(3.4f, 1.9f, -4.2f);
        [SerializeField] private Vector3 alternateEulerAngle = new Vector3(9.0f, -24.0f, 0.0f);

        [Header("Overhead / Top View Settings")]
        [Tooltip("Follow offset when player is between both walls (overhead/top view).")]
        [SerializeField] private Vector3 overheadOffset = new Vector3(0.0f, 7.5f, -3.0f);

        [Tooltip("Follow Euler angles (pitch, yaw, roll) looking down from above.")]
        [SerializeField] private Vector3 overheadEulerAngle = new Vector3(62.0f, 0.0f, 0.0f);

        [Tooltip("Speed of transition into/out of overhead top view.")]
        [SerializeField] private float overheadTransitionSpeed = 5.0f;

        [Header("Occlusion & Side Transition")]
        [SerializeField] private float sideTransitionSpeed = 6.0f;
        [SerializeField] private float minSideDuration = 0.35f;
        [SerializeField] private float sphereCastRadius = 0.25f;
        [SerializeField] private LayerMask occlusionMask = ~0;

        [Header("Damping Parameters")]
        [SerializeField] private float smoothTimeX = 0.06f; // Smooths lateral wall-run shifts
        [SerializeField] private float smoothTimeY = 0.08f; // Smooths vertical jump arcs

        [Header("Dynamic FOV")]
        [SerializeField] private float baseFOV = 50.0f;
        [SerializeField] private float sprintFOV = 56.0f;
        [SerializeField] private float threatFOV = 46.0f;
        [SerializeField] private float threatDistanceThreshold = 3.0f;
        [SerializeField] private float fovTransitionSpeed = 4.0f;

        private Camera cam;
        private PlayerParkourController playerController;
        private float velocityX = 0f;
        private float velocityY = 0f;

        private CameraSide currentSide = CameraSide.DefaultLeft;
        private Vector3 currentOffset = new Vector3(-3.4f, 1.9f, -4.2f);
        private Vector3 currentEulerAngle = new Vector3(9.0f, 24.0f, 0.0f);
        private float timeSinceSideSwitch = 0f;

        // Pre-allocated raycast buffer to avoid per-frame garbage collection spikes
        private readonly RaycastHit[] occlusionHits = new RaycastHit[12];

        public CameraSide CurrentSide => currentSide;
        public Vector3 CurrentOffset => currentOffset;
        public Vector3 CurrentEulerAngle => currentEulerAngle;
        public bool IsObstructed => IsDefaultViewObstructed();
        public bool IsOverhead => currentSide == CameraSide.OverheadTop;
        public bool IsBetweenWalls => IsBetweenBothWalls();

        public void SetTargets(Transform playerTarget, Transform chaserTarget)
        {
            target = playerTarget;
            chaser = chaserTarget;
            if (target != null)
            {
                playerController = target.GetComponent<PlayerParkourController>();
            }
            SnapToTarget();
        }

        public void SetOffset(Vector3 newOffset, Vector3 newAngle)
        {
            defaultOffset = newOffset;
            defaultEulerAngle = newAngle;
            if (currentSide == CameraSide.DefaultLeft)
            {
                currentOffset = newOffset;
                currentEulerAngle = newAngle;
            }
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            currentOffset = defaultOffset;
            currentEulerAngle = defaultEulerAngle;
        }

        private void Start()
        {
            FindReferencesIfMissing();
            SnapToTarget();
        }

        private void FindReferencesIfMissing()
        {
            if (target == null)
            {
                PlayerParkourController pc = Object.FindFirstObjectByType<PlayerParkourController>();
                if (pc != null)
                {
                    target = pc.transform;
                    playerController = pc;
                }
            }
            else if (playerController == null)
            {
                playerController = target.GetComponent<PlayerParkourController>();
            }

            if (chaser == null)
            {
                ChaserAI c = Object.FindFirstObjectByType<ChaserAI>();
                if (c != null) chaser = c.transform;
            }
        }

        public void SnapToTarget()
        {
            if (target == null)
            {
                FindReferencesIfMissing();
                if (target == null) return;
            }

            currentSide = CameraSide.DefaultLeft;
            currentOffset = defaultOffset;
            currentEulerAngle = defaultEulerAngle;
            timeSinceSideSwitch = 0f;
            velocityX = 0f;
            velocityY = 0f;

            transform.position = target.position + currentOffset;
            transform.rotation = Quaternion.Euler(currentEulerAngle);
        }

        private bool IsDefaultViewObstructed()
        {
            if (target == null) return false;

            if (playerController != null && playerController.IsWallRunning && playerController.WallSide == -1)
            {
                return true;
            }

            Vector3 focusPoint = target.position + Vector3.up * 1.35f;
            Vector3 candidateCamPos = target.position + defaultOffset;
            Vector3 toFocus = focusPoint - candidateCamPos;
            float distance = toFocus.magnitude;

            if (distance <= 0.01f) return false;

            Ray ray = new Ray(candidateCamPos, toFocus / distance);
            int hitCount = Physics.SphereCastNonAlloc(ray, sphereCastRadius, occlusionHits, distance, occlusionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Transform hitT = occlusionHits[i].transform;
                if (hitT == null) continue;
                if (hitT == target || hitT.IsChildOf(target)) continue;
                if (chaser != null && (hitT == chaser || hitT.IsChildOf(chaser))) continue;

                return true;
            }

            return false;
        }

        public bool IsBetweenBothWalls()
        {
            if (playerController != null)
            {
                return playerController.IsBetweenWalls;
            }

            if (target == null) return false;
            Vector3 origin = target.position + Vector3.up * 1.0f;
            float probeDist = 4.2f;
            bool hasLeft = Physics.Raycast(origin, -Vector3.right, out RaycastHit hitL, probeDist, occlusionMask, QueryTriggerInteraction.Ignore) &&
                           IsWallObstacle(hitL.collider);
            bool hasRight = Physics.Raycast(origin, Vector3.right, out RaycastHit hitR, probeDist, occlusionMask, QueryTriggerInteraction.Ignore) &&
                            IsWallObstacle(hitR.collider);
            return hasLeft && hasRight;
        }

        private bool IsWallObstacle(Collider col)
        {
            if (col == null) return false;
            return col.CompareTag("WallRunObstacle") || col.gameObject.name.Contains("WallRun");
        }

        private void UpdateCameraSide(float dt)
        {
            timeSinceSideSwitch += dt;

            bool betweenBothWalls = IsBetweenBothWalls();
            CameraSide desiredSide = currentSide;

            if (betweenBothWalls)
            {
                // Core Requirement: Switch to overhead/top-down follow angle when between both walls
                desiredSide = CameraSide.OverheadTop;
            }
            else
            {
                bool defaultBlocked = IsDefaultViewObstructed();
                if (defaultBlocked)
                {
                    desiredSide = CameraSide.AlternateRight;
                }
                else if (currentSide == CameraSide.AlternateRight || currentSide == CameraSide.OverheadTop)
                {
                    if (timeSinceSideSwitch >= minSideDuration)
                    {
                        desiredSide = CameraSide.DefaultLeft;
                    }
                }
                else
                {
                    desiredSide = CameraSide.DefaultLeft;
                }
            }

            if (desiredSide != currentSide)
            {
                currentSide = desiredSide;
                timeSinceSideSwitch = 0f;
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                FindReferencesIfMissing();
                if (target == null) return;
            }

            // Use deltaTime during active gameplay to match CharacterController interpolation, unscaled when paused
            float dt = Time.timeScale > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            UpdateCameraSide(dt);

            Vector3 targetSideOffset;
            Vector3 targetSideEuler;

            switch (currentSide)
            {
                case CameraSide.OverheadTop:
                    targetSideOffset = overheadOffset;
                    targetSideEuler = overheadEulerAngle;
                    break;
                case CameraSide.AlternateRight:
                    targetSideOffset = alternateOffset;
                    targetSideEuler = alternateEulerAngle;
                    break;
                case CameraSide.DefaultLeft:
                default:
                    targetSideOffset = defaultOffset;
                    targetSideEuler = defaultEulerAngle;
                    break;
            }

            // Frame-rate independent exponential smoothing
            float activeTransitionSpeed = (currentSide == CameraSide.OverheadTop) ? overheadTransitionSpeed : sideTransitionSpeed;
            float lerpFactor = 1f - Mathf.Exp(-activeTransitionSpeed * dt);

            currentOffset = Vector3.Lerp(currentOffset, targetSideOffset, lerpFactor);
            currentEulerAngle = new Vector3(
                Mathf.LerpAngle(currentEulerAngle.x, targetSideEuler.x, lerpFactor),
                Mathf.LerpAngle(currentEulerAngle.y, targetSideEuler.y, lerpFactor),
                Mathf.LerpAngle(currentEulerAngle.z, targetSideEuler.z, lerpFactor)
            );

            Vector3 targetPos = target.position + currentOffset;

            // Lockstep Z for zero forward jitter, SmoothDamp X and Y for fluid lateral & vertical motion
            float posZ = targetPos.z;
            float posX = Mathf.SmoothDamp(transform.position.x, targetPos.x, ref velocityX, smoothTimeX, Mathf.Infinity, dt);
            float posY = Mathf.SmoothDamp(transform.position.y, targetPos.y, ref velocityY, smoothTimeY, Mathf.Infinity, dt);

            transform.position = new Vector3(posX, posY, posZ);
            transform.rotation = Quaternion.Euler(currentEulerAngle);

            UpdateDynamicFOV(dt);
        }

        private void UpdateDynamicFOV(float dt)
        {
            if (cam == null) return;

            float targetFOV = baseFOV;

            if (playerController != null && playerController.IsSprinting)
            {
                targetFOV = sprintFOV;
            }
            else if (chaser != null)
            {
                float chaserDistance = target.position.z - chaser.position.z;
                if (chaserDistance <= threatDistanceThreshold && chaserDistance > 0f)
                {
                    float threatFactor = Mathf.Clamp01(chaserDistance / threatDistanceThreshold);
                    targetFOV = Mathf.Lerp(threatFOV, baseFOV, threatFactor);
                }
            }

            cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, targetFOV, fovTransitionSpeed * 6f * dt);
        }
    }
}