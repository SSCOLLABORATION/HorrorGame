using UnityEngine;

namespace VectorParkour
{
    /// <summary>
    /// 3D side-angled chase camera inspired by Vector.
    /// Provides an angled chase perspective positioned close to the runner
    /// so the player clearly sees character animations, incoming 3D obstacles,
    /// and the Hunter pursuing from behind.
    /// Uses rock-solid Z-axis lockstep tracking to eliminate forward/backward stutter,
    /// smooth Y-damping for grounded jump arcs, and dynamic FOV based on speed & threat proximity.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VectorFollowCamera : MonoBehaviour
    {
        [Header("Target Tracking")]
        [SerializeField] private Transform target;
        [SerializeField] private Transform chaser;
        [SerializeField] private Vector3 offset = new Vector3(-3.4f, 1.9f, -4.2f);
        [SerializeField] private Vector3 targetEulerAngle = new Vector3(9.0f, 24.0f, 0.0f);

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

        public void SetTargets(Transform playerTarget, Transform chaserTarget)
        {
            target = playerTarget;
            chaser = chaserTarget;
            if (target != null)
            {
                playerController = target.GetComponent<PlayerParkourController>();
            }
        }

        public void SetOffset(Vector3 newOffset, Vector3 newAngle)
        {
            offset = newOffset;
            targetEulerAngle = newAngle;
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
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
                PlayerParkourController pc = UnityEngine.Object.FindFirstObjectByType<PlayerParkourController>();
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
                ChaserAI c = UnityEngine.Object.FindFirstObjectByType<ChaserAI>();
                if (c != null) chaser = c.transform;
            }
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            transform.position = target.position + offset;
            transform.rotation = Quaternion.Euler(targetEulerAngle);
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                FindReferencesIfMissing();
                if (target == null) return;
            }

            // Desired camera position
            Vector3 targetPos = target.position + offset;

            // Direct forward Z lockstep: guarantees the player stays locked on screen along the run axis without any micro-stutter
            float posZ = targetPos.z;

            // Fast, responsive lateral X tracking
            float posX = targetPos.x;

            // Smooth vertical damping on Y so jumps feel grounded and don't jerk the camera violently
            float posY = Mathf.SmoothDamp(transform.position.y, targetPos.y, ref velocityY, smoothTimeY);

            transform.position = new Vector3(posX, posY, posZ);
            transform.rotation = Quaternion.Euler(targetEulerAngle);

            UpdateDynamicFOV();
        }

        private void UpdateDynamicFOV()
        {
            if (cam == null) return;

            float targetFOV = baseFOV;

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

            cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, targetFOV, fovTransitionSpeed * 6f * Time.deltaTime);
        }
    }
}
