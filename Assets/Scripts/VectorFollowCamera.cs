using UnityEngine;

namespace VectorParkour
{
    public enum CameraSide
    {
        DefaultLeft,
        AlternateRight
    }

    /// <summary>
    /// 3D side-angled chase camera inspired by Vector.
    /// Provides an angled chase perspective positioned close to the runner
    /// so the player clearly sees character animations, incoming 3D obstacles,
    /// and the Hunter pursuing from behind.
    /// Uses rock-solid Z-axis lockstep tracking to eliminate forward/backward stutter,
    /// smooth Y-damping for grounded jump arcs, dynamic FOV based on speed & threat proximity,
    /// and an intelligent dual-rig occlusion system that smoothly transitions to an alternate
    /// right-side perspective whenever solid walls or obstacles obstruct the default left view.
    /// Includes SnapToTarget() for zero-drift instantaneous camera repositioning during level transitions.
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

        [Header("Occlusion & Side Transition")]
        [SerializeField] private float sideTransitionSpeed = 6.0f;
        [SerializeField] private float minSideDuration = 0.35f;
        [SerializeField] private float sphereCastRadius = 0.25f;
        [SerializeField] private LayerMask occlusionMask = ~0;

        [Header("Damping Parameters")]
        [SerializeField] private float smoothTimeY = 0.08f; // Responsive vertical damping for jumps

        [Header("Dynamic FOV")]
        [SerializeField] private float baseFOV = 50.0f;
        [SerializeField] private float sprintFOV = 56.0f;
        [SerializeField] private float threatFOV = 46.0f;
        [SerializeField] private float threatDistanceThreshold = 3.0f;
        [SerializeField] private float fovTransitionSpeed = 4.0f;

        private Camera cam;
        private PlayerParkourController playerController;
        private float velocityY = 0f;

        private CameraSide currentSide = CameraSide.DefaultLeft;
        private Vector3 currentOffset = new Vector3(-3.4f, 1.9f, -4.2f);
        private Vector3 currentEulerAngle = new Vector3(9.0f, 24.0f, 0.0f);
        private float timeSinceSideSwitch = 0f;

        public CameraSide CurrentSide => currentSide;
        public Vector3 CurrentOffset => currentOffset;
        public Vector3 CurrentEulerAngle => currentEulerAngle;
        public bool IsObstructed => IsDefaultViewObstructed();

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

        /// <summary>
        /// Immediately snaps camera position and orientation to the target with zero damping lag.
        /// Clears all stale velocity/smoothing accumulators.
        /// </summary>
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
            velocityY = 0f;

            transform.position = target.position + currentOffset;
            transform.rotation = Quaternion.Euler(currentEulerAngle);
        }

        private bool IsDefaultViewObstructed()
        {
            if (target == null) return false;

            // 1. Explicit coordination: If player is wall running on left wall, left wall is between runner and default cam
            if (playerController != null && playerController.IsWallRunning && playerController.WallSide == -1)
            {
                return true;
            }

            // 2. Physical geometry test: SphereCast from candidate default position to player focus point
            Vector3 focusPoint = target.position + Vector3.up * 1.35f;
            Vector3 candidateCamPos = target.position + defaultOffset;
            Vector3 toFocus = focusPoint - candidateCamPos;
            float distance = toFocus.magnitude;

            if (distance <= 0.01f) return false;

            Ray ray = new Ray(candidateCamPos, toFocus / distance);
            RaycastHit[] hits = Physics.SphereCastAll(ray, sphereCastRadius, distance, occlusionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitT = hits[i].transform;
                if (hitT == target || hitT.IsChildOf(target)) continue;
                if (chaser != null && (hitT == chaser || hitT.IsChildOf(chaser))) continue;

                // An obstacle or wall is in the line of sight
                return true;
            }

            return false;
        }

        private void UpdateCameraSide()
        {
            float dt = Time.unscaledDeltaTime;
            timeSinceSideSwitch += dt;

            bool defaultBlocked = IsDefaultViewObstructed();
            CameraSide desiredSide = currentSide;

            if (defaultBlocked)
            {
                desiredSide = CameraSide.AlternateRight;
            }
            else
            {
                // Default view is clear!
                if (currentSide == CameraSide.AlternateRight)
                {
                    // Only restore default after hysteresis duration has elapsed
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

            UpdateCameraSide();

            float dt = Time.unscaledDeltaTime;

            // Smoothly interpolate current offset and Euler angles towards active side target
            Vector3 targetSideOffset = (currentSide == CameraSide.DefaultLeft) ? defaultOffset : alternateOffset;
            Vector3 targetSideEuler = (currentSide == CameraSide.DefaultLeft) ? defaultEulerAngle : alternateEulerAngle;

            currentOffset = Vector3.Lerp(currentOffset, targetSideOffset, dt * sideTransitionSpeed);
            currentEulerAngle = new Vector3(
                Mathf.Lerp(currentEulerAngle.x, targetSideEuler.x, dt * sideTransitionSpeed),
                Mathf.LerpAngle(currentEulerAngle.y, targetSideEuler.y, dt * sideTransitionSpeed),
                Mathf.Lerp(currentEulerAngle.z, targetSideEuler.z, dt * sideTransitionSpeed)
            );

            // Desired camera position
            Vector3 targetPos = target.position + currentOffset;

            // Direct forward Z lockstep: guarantees runner stays locked on screen along the run axis without micro-stutter
            float posZ = targetPos.z;

            // Lateral tracking on X
            float posX = targetPos.x;

            // Smooth vertical damping on Y so jumps feel grounded and don't jerk the camera violently
            float posY = Mathf.SmoothDamp(transform.position.y, targetPos.y, ref velocityY, smoothTimeY);

            transform.position = new Vector3(posX, posY, posZ);
            transform.rotation = Quaternion.Euler(currentEulerAngle);

            UpdateDynamicFOV();
        }

        private void UpdateDynamicFOV()
        {
            if (cam == null) return;

            float targetFOV = baseFOV;
            float dt = Time.unscaledDeltaTime;

            // 1. Sprint burst widens FOV for high-speed rush feel
            if (playerController != null && playerController.IsSprinting)
            {
                targetFOV = sprintFOV;
            }
            // 2. Chaser within threat distance narrows FOV for tension
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
