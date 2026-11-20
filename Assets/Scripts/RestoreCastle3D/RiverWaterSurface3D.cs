using System.Collections.Generic;
using UnityEngine;

namespace RestoreCastle3D
{
    /// <summary>
    /// Non-solid water volume and single shared wake publisher for all river sections.
    /// Sends only visible floating pieces to the water shader; no periodic base waves.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class RiverWaterSurface3D : MonoBehaviour
    {
        private const int MAX_VISIBLE_WAKES = 24;
        private static readonly int WakeCountId = Shader.PropertyToID("_RiverWakeCount");
        private static readonly int WakeObjectsId = Shader.PropertyToID("_RiverWakeObjects");
        private static readonly int WakeMotionId = Shader.PropertyToID("_RiverWakeMotion");
        private static readonly HashSet<CastlePiece3D> FloatingPieces =
            new HashSet<CastlePiece3D>();
        private static readonly List<CastlePiece3D> StalePieces =
            new List<CastlePiece3D>();
        private static readonly Vector4[] WakeObjects = new Vector4[MAX_VISIBLE_WAKES];
        private static readonly Vector4[] WakeMotion = new Vector4[MAX_VISIBLE_WAKES];
        private static readonly float[] WakeDistances = new float[MAX_VISIBLE_WAKES];
        private static RiverWaterSurface3D _publisher;

        [SerializeField, Min(0f)] private float _flowSpeed = 3f;
        [SerializeField, Range(0.1f, 1f)] private float _submergedFraction = 0.8f;
        private BoxCollider _volume;
        private Camera _mainCamera;

        public float SurfaceY => _volume.bounds.max.y;
        public float FlowSpeed => _flowSpeed;
        public float SubmergedFraction => _submergedFraction;

        public static void ForgetPiece(CastlePiece3D piece)
        {
            FloatingPieces.Remove(piece);
        }

        private void Awake()
        {
            _volume = GetComponent<BoxCollider>();
            if (!_volume.isTrigger)
                Debug.LogWarning("River surface requires a trigger BoxCollider.", this);
            if (_publisher == null) _publisher = this;
        }

        private void OnTriggerStay(Collider other)
        {
            CastlePiece3D piece = other.GetComponentInParent<CastlePiece3D>();
            if (piece == null) return;
            piece.TouchRiverSurface(this);
            if (piece.IsFloatingOnRiver)
                FloatingPieces.Add(piece);
        }

        private void OnTriggerExit(Collider other)
        {
            CastlePiece3D piece = other.GetComponentInParent<CastlePiece3D>();
            if (piece != null)
                piece.LeaveRiverSurface(this);
        }

        private void LateUpdate()
        {
            // Three volumes share the same water shader and must publish only
            // once per frame. Elect another volume if the first was disabled.
            if (_publisher == null) _publisher = this;
            if (_publisher != this) return;
            if (_mainCamera == null) _mainCamera = Camera.main;

            int count = 0;
            StalePieces.Clear();
            foreach (CastlePiece3D piece in FloatingPieces)
            {
                if (piece == null || !piece.IsFloatingOnRiver)
                {
                    StalePieces.Add(piece);
                    continue;
                }
                if (_mainCamera == null ||
                    !piece.TryGetRiverWake(out Vector4 footprint, out Vector4 motion))
                    continue;

                Vector3 center = new Vector3(footprint.x, piece.transform.position.y, footprint.y);
                Vector3 view = _mainCamera.WorldToViewportPoint(center);
                if (view.z <= 0f || view.x < -0.15f || view.x > 1.15f ||
                    view.y < -0.15f || view.y > 1.15f)
                    continue;

                float distance = (view.x - 0.5f) * (view.x - 0.5f) +
                    (view.y - 0.5f) * (view.y - 0.5f);
                int slot = count;
                if (count < MAX_VISIBLE_WAKES)
                {
                    count++;
                }
                else
                {
                    // Keep wakes nearest the visible center when there are
                    // hundreds of objects after a fountain burst.
                    slot = 0;
                    for (int j = 1; j < count; j++)
                        if (WakeDistances[j] > WakeDistances[slot]) slot = j;
                    if (distance >= WakeDistances[slot]) continue;
                }
                WakeObjects[slot] = footprint;
                WakeMotion[slot] = motion;
                WakeDistances[slot] = distance;
            }
            foreach (CastlePiece3D piece in StalePieces)
                FloatingPieces.Remove(piece);
            StalePieces.Clear();

            Shader.SetGlobalInt(WakeCountId, count);
            if (count == 0) return;
            // Fixed-length arrays avoid reallocation / Unity array resizing.
            Shader.SetGlobalVectorArray(WakeObjectsId, WakeObjects);
            Shader.SetGlobalVectorArray(WakeMotionId, WakeMotion);
        }

        private void OnDisable()
        {
            if (_publisher != this) return;
            _publisher = null;
            Shader.SetGlobalInt(WakeCountId, 0);
        }
    }
}
