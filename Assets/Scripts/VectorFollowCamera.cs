using UnityEngine;

namespace VectorParkour
{
    /// <summary>
    /// 3D side-angled chase camera inspired by Vector.
    /// Provides an angled side perspective (Offset X: -6, Y: 3.5, Z: -4, 25 deg Y-angle)
    /// to clearly display both the player's upcoming obstacles and the Chaser pursuing from behind.
    /// Uses SmoothDamp with decoupled Y-axis damping, and dynamic FOV based on speed & threat proximity.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VectorFollowCamera : MonoBehaviour
    {
        [Header("Target Tracking")]
        [SerializeField] private Transform target;
        [SerializeField] private Transform chaser;
        [SerializeField] private Vector3 offset = new Vector3(-7.2f, 3.8f, -8.5f);
        [SerializeField] private Vector3 targetEulerAngle = new Vector3(13.0f, 28.0f, 0.0f);

        [Header("Damping Parameters")]
        [SerializeField] private float smoothTimeXZ = 0.15f;
        [SerializeField] private float smoothTimeY = 0.35f; // Tighter damping so jumps feel grounded

        [Header("Dynamic FOV")]
        [SerializeField] private float baseFOV = 60.0f;
        [SerializeField] private float sprintFOV = 68.0f;
        [SerializeField] private float threatFOV = 52.0f;
        [SerializeField] private float threatDistanceThreshold = 2.5f;
        [SerializeField] private float fovTransitionSpeed = 5.0f;

        private Camera cam;
        private PlayerParkourController playerController;
        private float velocityX = 0f;
        private float velocityY = 0f;
        private float velocityZ = 0f;

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

            // Decoupled SmoothDamp: X and Z follow rapidly, Y has tighter damping for grounded jumps
            float posX = Mathf.SmoothDamp(transform.position.x, targetPos.x, ref velocityX, smoothTimeXZ);
            float posY = Mathf.SmoothDamp(transform.position.y, targetPos.y, ref velocityY, smoothTimeY);
            float posZ = Mathf.SmoothDamp(transform.position.z, targetPos.z, ref velocityZ, smoothTimeXZ);

            transform.position = new Vector3(posX, posY, posZ);
            transform.rotation = Quaternion.Euler(targetEulerAngle);

            UpdateDynamicFOV();
        }

        private void UpdateDynamicFOV()
        {
            if (cam == null) return;

            float targetFOV = baseFOV;

            // 1. Sprint burst widens FOV
            if (playerController != null && playerController.IsSprinting)
            {
                targetFOV = sprintFOV;
            }
            // 2. Chaser within threat distance narrows FOV for tension
            else if (chaser != null)
            {
                float chaserDistance = target.position.z - chaser.position.z;
                if (chaserDistance <= threatDistanceThreshold)
                {
                    float threatFactor = Mathf.Clamp01(chaserDistance / threatDistanceThreshold);
                    targetFOV = Mathf.Lerp(threatFOV, baseFOV, threatFactor);
                }
            }

            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, Time.deltaTime * fovTransitionSpeed);
        }
    }
}
