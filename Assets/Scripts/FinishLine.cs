using UnityEngine;

namespace VectorParkour
{
    /// <summary>
    /// Placed on the finish line gate at the end of the track.
    /// When the player passes through the trigger, it ends the game with victory.
    /// </summary>
    public class FinishLine : MonoBehaviour
    {
        private bool hasTriggered = false;

        private void OnTriggerEnter(Collider other)
        {
            if (hasTriggered) return;

            PlayerParkourController player = other.GetComponent<PlayerParkourController>();
            if (player == null) player = other.GetComponentInParent<PlayerParkourController>();

            if (player != null)
            {
                hasTriggered = true;
                player.ReachFinishLine();
            }
        }

        public void ResetFinishLine()
        {
            hasTriggered = false;
        }
    }
}
