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
        [Tooltip("Target level index this finish line belongs to (1 to 4).")]
        [SerializeField] private int targetLevelIndex = 1;

        public int TargetLevelIndex
        {
            get => targetLevelIndex;
            set => targetLevelIndex = value;
        }

        private bool hasTriggered = false;

        public void OnTriggerEnter(Collider other)
        {
            if (hasTriggered || other.isTrigger) return;

            if (GameManager.Instance != null)
            {
                if (GameManager.Instance.CurrentState != GameState.Playing) return;
                if (GameManager.Instance.CurrentLevelIndex != targetLevelIndex) return;
            }

            PlayerParkourController player = other.GetComponentInParent<PlayerParkourController>();
            if (player == null) return;

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