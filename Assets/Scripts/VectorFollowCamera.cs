using UnityEngine;
using Vector3D.Core;

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
    /// Features zero-GC non-alloc occlusion checks, 3-axis smooth damping (X, Y, Z),
    /// collision push-in to eliminate geometry clipping through slide barriers & pillars,
    /// robust hysteresis for overhead corridor view, and frame-rate independent transitions.
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
        [SerializeField] private float sphereCastRadius = 0.35f;
        [SerializeField] private float nearClipMargin = 0.30f;
        [SerializeField] private float minCameraDistance = 1.6f;
        [SerializeField] private LayerMask occlusionMask = ~0;

        [Header("Damping Parameters")]
        [SerializeField] private float smoothTimeX = 0.06f; // Smooths lateral wall-run shifts
        [SerializeField] private float smoothTimeY = 0.08f; // Smooths vertical jump arcs
        [SerializeField] private float smoothTimeZ = 0.06f; // Smooths longitudinal transitions (overhead / side switches)

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
        private float velocityZ = 0f;

        private CameraSide currentSide = CameraSide.DefaultLeft;
        private Vector3 currentOffset = new Vector3(-3.4f, 1.9f, -4.2f);
        private Vector3 currentEulerAngle = new Vector3(9.0f, 24.0f, 0.0f);
        private float timeSinceSideSwitch = 0f;

        // Overhead debounce / hysteresis timers
        private float betweenWallsTimer = 0f;
        private float outsideWallsTimer = 0f;
        private const float OverheadEntryThreshold = 0.15f;
        private const float OverheadExitThreshold = 0.25f;

        // Pre-allocated raycast buffer to avoid per-frame garbage collection spikes
        private readonly RaycastHit[] occlusionHits = new RaycastHit[16];

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
            betweenWallsTimer = 0f;
            outsideWallsTimer = 0f;
            velocityX = 0f;
            velocityY = 0f;
            velocityZ = 0f;

            transform.position = target.position + currentOffset;
            transform.rotation = Quaternion.Euler(currentEulerAngle);

            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = baseFOV;
        }

        private Vector3 GetPlayerFocusPoint()
        {
            return target != null ? target.position + Vector3.up * 1.35f : Vector3.zero;
        }

        /// <summary>
        /// Validates whether a candidate offset line-of-sight from focus point to camera is obstructed.
        /// Performs sphere cast from character outwards to camera position.
        /// </summary>
        public bool IsCandidateObstructed(Vector3 candidateOffset)
        {
            if (target == null) return false;

            Vector3 focusPoint = GetPlayerFocusPoint();
            Vector3 candidateCamPos = target.position + candidateOffset;
            Vector3 toCam = candidateCamPos - focusPoint;
            float distance = toCam.magnitude;

            if (distance <= 0.05f) return false;

            Ray ray = new Ray(focusPoint, toCam / distance);
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

        private bool IsDefaultViewObstructed()
        {
            if (target == null) return false;

            if (playerController != null && playerController.IsWallRunning && playerController.WallSide == -1)
            {
                return true;
            }

            return IsCandidateObstructed(defaultOffset);
        }

        public bool IsBetweenBothWalls()
        {
            if (playerController != null)
            {
                if (playerController.IsInVerticalShaft) return true;
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

            // Apply debounce/hysteresis so entering or exiting corridor doesn't oscillate
            if (betweenBothWalls)
            {
                betweenWallsTimer += dt;
                outsideWallsTimer = 0f;
            }
            else
            {
                outsideWallsTimer += dt;
                betweenWallsTimer = 0f;
            }

            CameraSide desiredSide = currentSide;

            if (currentSide == CameraSide.OverheadTop)
            {
                if (outsideWallsTimer >= OverheadExitThreshold && timeSinceSideSwitch >= minSideDuration)
                {
                    desiredSide = CameraSide.DefaultLeft;
                }
            }
            else
            {
                if (betweenWallsTimer >= OverheadEntryThreshold)
                {
                    desiredSide = CameraSide.OverheadTop;
                }
                else
                {
                    bool defaultBlocked = IsDefaultViewObstructed();
                    if (defaultBlocked)
                    {
                        // Only switch to alternate side if alternate side is actually CLEAR!
                        bool altBlocked = IsCandidateObstructed(alternateOffset);
                        if (!altBlocked)
                        {
                            desiredSide = CameraSide.AlternateRight;
                        }
                        else
                        {
                            // If both sides are blocked (e.g. narrow pillars), remain on default side and let collision pull-in handle it
                            desiredSide = CameraSide.DefaultLeft;
                        }
                    }
                    else if (currentSide == CameraSide.AlternateRight)
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

            // Do not drift or oscillate if game is paused/frozen
            if (Time.timeScale <= 0f && GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

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

            // Frame-rate independent exponential smoothing of offsets and angles
            float activeTransitionSpeed = (currentSide == CameraSide.OverheadTop) ? overheadTransitionSpeed : sideTransitionSpeed;
            float lerpFactor = 1f - Mathf.Exp(-activeTransitionSpeed * dt);

            currentOffset = Vector3.Lerp(currentOffset, targetSideOffset, lerpFactor);
            currentEulerAngle = new Vector3(
                Mathf.LerpAngle(currentEulerAngle.x, targetSideEuler.x, lerpFactor),
                Mathf.LerpAngle(currentEulerAngle.y, targetSideEuler.y, lerpFactor),
                Mathf.LerpAngle(currentEulerAngle.z, targetSideEuler.z, lerpFactor)
            );

            // Compute ideal unoccluded target position
            Vector3 focusPoint = GetPlayerFocusPoint();
            Vector3 desiredCamPos = target.position + currentOffset;

            // Geometry collision check & controlled pull-in (prevents camera clipping through slide barriers & pillars)
            Vector3 toCam = desiredCamPos - focusPoint;
            float desiredDistance = toCam.magnitude;
            if (desiredDistance > 0.1f)
            {
                Vector3 camDir = toCam / desiredDistance;
                Ray ray = new Ray(focusPoint, camDir);
                int hitCount = Physics.SphereCastNonAlloc(ray, sphereCastRadius, occlusionHits, desiredDistance, occlusionMask, QueryTriggerInteraction.Ignore);

                float nearestDist = desiredDistance;
                for (int i = 0; i < hitCount; i++)
                {
                    Transform hitT = occlusionHits[i].transform;
                    if (hitT == null) continue;
                    if (hitT == target || hitT.IsChildOf(target)) continue;
                    if (chaser != null && (hitT == chaser || hitT.IsChildOf(chaser))) continue;

                    if (occlusionHits[i].distance < nearestDist)
                    {
                        nearestDist = occlusionHits[i].distance;
                    }
                }

                if (nearestDist < desiredDistance)
                {
                    float safeDist = Mathf.Max(minCameraDistance, nearestDist - nearClipMargin);
                    desiredCamPos = focusPoint + camDir * safeDist;
                }
            }

            // Coherent 3-axis SmoothDamp (X, Y, Z) eliminates any Z snapping during side or overhead transitions
            float posX = Mathf.SmoothDamp(transform.position.x, desiredCamPos.x, ref velocityX, smoothTimeX, Mathf.Infinity, dt);
            float posY = Mathf.SmoothDamp(transform.position.y, desiredCamPos.y, ref velocityY, smoothTimeY, Mathf.Infinity, dt);
            float posZ = Mathf.SmoothDamp(transform.position.z, desiredCamPos.z, ref velocityZ, smoothTimeZ, Mathf.Infinity, dt);

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
