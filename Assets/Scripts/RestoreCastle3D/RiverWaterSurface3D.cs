using UnityEngine;

namespace RestoreCastle3D
{
    /// <summary>River trigger that lets pieces pass through while controlling floating.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class RiverWaterSurface3D : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _flowSpeed = 3f;
        [SerializeField, Range(0.1f, 1f)] private float _submergedFraction = 0.8f;
        private BoxCollider _volume;

        public float SurfaceY => _volume.bounds.max.y;
        public float FlowSpeed => _flowSpeed;
        public float SubmergedFraction => _submergedFraction;

        private void Awake()
        {
            _volume = GetComponent<BoxCollider>();
            if (!_volume.isTrigger)
                Debug.LogWarning("River surface requires a trigger BoxCollider.", this);
        }

        private void OnTriggerStay(Collider other)
        {
            CastlePiece3D piece = other.GetComponentInParent<CastlePiece3D>();
            if (piece != null) piece.TouchRiverSurface(this);
        }

        private void OnTriggerExit(Collider other)
        {
            CastlePiece3D piece = other.GetComponentInParent<CastlePiece3D>();
            if (piece != null) piece.LeaveRiverSurface(this);
        }
    }
}
