using UnityEngine;

namespace VectorParkour
{
    /// <summary>
    /// Attach to the child VaultDetector trigger GameObject to signal the player controller.
    /// Detects upcoming vault blocks ahead of the player to enable contextual vault actions.
    /// </summary>
    public class VaultDetector : MonoBehaviour
    {
        private PlayerParkourController playerController;
        private Transform currentVaultTarget;

        private void Awake()
        {
            playerController = GetComponentInParent<PlayerParkourController>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Obstacle_Vault"))
            {
                if (playerController == null)
                {
                    playerController = GetComponentInParent<PlayerParkourController>();
                }

                currentVaultTarget = other.transform;
                playerController?.SetInVaultTrigger(true, currentVaultTarget);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Obstacle_Vault") && other.transform == currentVaultTarget)
            {
                if (playerController == null)
                {
                    playerController = GetComponentInParent<PlayerParkourController>();
                }

                currentVaultTarget = null;
                playerController?.SetInVaultTrigger(false, null);
            }
        }
    }
}