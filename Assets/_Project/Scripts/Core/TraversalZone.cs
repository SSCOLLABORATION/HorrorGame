using UnityEngine;
using VectorParkour;

namespace Vector3D.Core
{
    public enum TraversalZoneType
    {
        VerticalShaft,
        RooftopDrop,
        GapCrossing,
        WallRunZone
    }

    /// <summary>
    /// Explicit, inspector-configurable traversal zone identifying vertical shafts,
    /// rooftop drops, gap crossings, and wall-run corridors.
    /// Replaces hardcoded coordinate thresholds with robust spatial trigger volumes.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TraversalZone : MonoBehaviour
    {
        [Tooltip("Type of parkour traversal required in this zone.")]
        [SerializeField] private TraversalZoneType zoneType = TraversalZoneType.VerticalShaft;

        [Tooltip("Sector number this traversal zone belongs to (1 to 4).")]
        [SerializeField] private int sectorNumber = 1;

        [Tooltip("Target upper/landing elevation (meters) if applicable.")]
        [SerializeField] private float targetElevation = 6.0f;

        [Tooltip("Optional reference to the left flanking wall.")]
        [SerializeField] private Transform leftWall;

        [Tooltip("Optional reference to the right flanking wall.")]
        [SerializeField] private Transform rightWall;

        public TraversalZoneType ZoneType
        {
            get => zoneType;
            set => zoneType = value;
        }

        public int SectorNumber
        {
            get => sectorNumber;
            set => sectorNumber = value;
        }

        public float TargetElevation
        {
            get => targetElevation;
            set => targetElevation = value;
        }

        public Transform LeftWall => leftWall;
        public Transform RightWall => rightWall;

        private void Reset()
        {
            var col = GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true;
        }

        private void Awake()
        {
            var col = GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.isTrigger) return;

            PlayerParkourController player = other.GetComponentInParent<PlayerParkourController>();
            if (player != null)
            {
                player.OnEnterTraversalZone(this);
                return;
            }

            ChaserAI chaser = other.GetComponentInParent<ChaserAI>();
            if (chaser != null)
            {
                chaser.OnEnterTraversalZone(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.isTrigger) return;

            PlayerParkourController player = other.GetComponentInParent<PlayerParkourController>();
            if (player != null)
            {
                player.OnExitTraversalZone(this);
                return;
            }

            ChaserAI chaser = other.GetComponentInParent<ChaserAI>();
            if (chaser != null)
            {
                chaser.OnExitTraversalZone(this);
            }
        }
    }
}
