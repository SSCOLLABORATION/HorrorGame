using UnityEngine;
using Vector3D.Core;

namespace VectorParkour
{
    /// <summary>
    /// Placed on the finish line gate at the end of the track.
    /// Acts as the single authoritative level completion trigger.
    /// Spatially and logically validates the player, active level index, and trigger bounds.
    /// </summary>
    public class FinishLine : MonoBehaviour
    {
        [Tooltip("Target level index this finish line belongs to (1 or 2).")]
        [SerializeField] private int targetLevelIndex = 1;

        public int TargetLevelIndex
        {
            get => targetLevelIndex;
            set => targetLevelIndex = value;
        }

        private bool hasTriggered = false;

        private void OnTriggerEnter(Collider other)
        {
            if (hasTriggered) return;

            // Ignore child trigger colliders (e.g. VaultDetector) to avoid premature activation
            if (other.isTrigger) return;

            // Verify GameManager state
            if (GameManager.Instance != null)
            {
                if (GameManager.Instance.CurrentState != GameState.Playing) return;
                if (GameManager.Instance.CurrentLevelIndex != targetLevelIndex) return;
            }

            // Verify the collider belongs to the actual Player
            PlayerParkourController player = other.GetComponent<PlayerParkourController>();
            if (player == null) player = other.GetComponentInParent<PlayerParkourController>();
            if (player == null) return;

            // Spatially verify that the player is on this track corridor (within 5m along X)
            if (Mathf.Abs(player.transform.position.x - transform.position.x) > 5.0f)
            {
                return;
            }

            hasTriggered = true;
            player.ReachFinishLine();
        }

        public void ResetFinishLine()
        {
            hasTriggered = false;
        }
    }
}
