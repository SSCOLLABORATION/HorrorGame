using UnityEngine;
using VectorParkour;

namespace Vector3D.Core
{
    /// <summary>
    /// Explicit sector metadata component attached to each level root in the scene.
    /// Decouples level identity, spawns, and goals from fragile hardcoded coordinates.
    /// </summary>
    public class ParkourSector : MonoBehaviour
    {
        [Tooltip("1-based Sector number (1, 2, 3, 4).")]
        [SerializeField] private int sectorNumber = 1;

        [Tooltip("Display name for UI / HUD.")]
        [SerializeField] private string sectorName = "SECTOR 01";

        [Tooltip("Spawn point for the player in this sector.")]
        [SerializeField] private Transform playerSpawnPoint;

        [Tooltip("Spawn point for the chaser in this sector.")]
        [SerializeField] private Transform chaserSpawnPoint;

        [Tooltip("Finish line gate at the end of this sector.")]
        [SerializeField] private FinishLine finishGate;

        [Tooltip("Local or world center X of the track.")]
        [SerializeField] private float trackCenterX = 0f;

        public int SectorNumber
        {
            get => sectorNumber;
            set => sectorNumber = value;
        }

        public string SectorName => sectorName;
        public float TrackCenterX
        {
            get => trackCenterX;
            set => trackCenterX = value;
        }

        public Vector3 PlayerSpawnPosition => playerSpawnPoint != null ? playerSpawnPoint.position : transform.position + new Vector3(trackCenterX, 1.1f, 0f);
        public Vector3 ChaserSpawnPosition => chaserSpawnPoint != null ? chaserSpawnPoint.position : transform.position + new Vector3(trackCenterX, 1.1f, -6f);
        public FinishLine FinishGate => finishGate;

        private void Awake()
        {
            if (finishGate == null)
            {
                finishGate = GetComponentInChildren<FinishLine>(true);
            }
        }
    }
}
