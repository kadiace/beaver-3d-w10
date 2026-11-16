using UnityEngine;

namespace BeaverRecycling
{
    /// <summary>Stationary cliff bumper; the separate child mesh animates without moving the hitbox.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(MeshCollider))]
    public sealed class RecyclingBumperStone : MonoBehaviour
    {
        [SerializeField] private Transform _visual;
        [SerializeField, Min(0.1f)] private float _minimumBounceSpeed = 3.6f;
        [SerializeField, Min(1f)] private float _bounceMultiplier = 1.2f;
        [SerializeField, Min(0f)] private float _outwardKick = 0.45f;
        [SerializeField, Min(0f)] private float _flowPauseSeconds = 0.7f;
        [SerializeField, Min(0.1f)] private float _animationDuration = 0.65f;
        [SerializeField, Min(1f)] private float _oscillationsPerSecond = 7.5f;
        [SerializeField, Range(0f, 0.5f)] private float _squash = 0.24f;
        [SerializeField, Min(0f)] private float _wobbleDegrees = 16f;

        private Vector3 _restScale;
        private Quaternion _restRotation;
        private float _bounceTime = float.PositiveInfinity;
        private float _wobbleDirection = 1f;

        void Awake()
        {
            // Unity 6 may remap the imported OBJ mesh subasset's serialized local ID.
            MeshCollider hitbox = GetComponent<MeshCollider>();
            MeshFilter visibleMesh = _visual.GetComponent<MeshFilter>();
            if (hitbox.sharedMesh == null || visibleMesh.sharedMesh == null)
            {
                Mesh imported = null;
                foreach (Mesh candidate in Resources.LoadAll<Mesh>("Prefabs/ForestPebble"))
                    if (candidate != null && candidate.vertexCount > 0) { imported = candidate; break; }
                if (imported == null)
                {
                    GameObject model = Resources.Load<GameObject>("Prefabs/ForestPebble");
                    if (model != null)
                    {
                        MeshFilter fallback = model.GetComponentInChildren<MeshFilter>(true);
                        if (fallback != null) imported = fallback.sharedMesh;
                    }
                }
                if (imported != null)
                {
                    if (hitbox.sharedMesh == null) hitbox.sharedMesh = imported;
                    if (visibleMesh.sharedMesh == null) visibleMesh.sharedMesh = imported;
                }
            }
            _restScale = _visual.localScale;
            _restRotation = _visual.localRotation;
        }

        void OnDisable()
        {
            if (_visual == null) return;
            _visual.localScale = _restScale;
            _visual.localRotation = _restRotation;
            _bounceTime = float.PositiveInfinity;
        }

        void OnCollisionEnter(Collision collision)
        {
            Rigidbody otherBody = collision.rigidbody;
            if (otherBody == null || otherBody.isKinematic || collision.contactCount == 0) return;
            RecyclingItem item = otherBody.GetComponent<RecyclingItem>();
            BombExplosion3D bomb = item == null ? otherBody.GetComponent<BombExplosion3D>() : null;
            if ((item == null || item.IsHeld || item.IsResolved) && bomb == null) return;

            Vector3 normal = Vector3.ProjectOnPlane(collision.GetContact(0).normal, Vector3.up);
            Vector3 outward = Vector3.ProjectOnPlane(otherBody.worldCenterOfMass - transform.position, Vector3.up);
            if (normal.sqrMagnitude < 0.005f) normal = outward;
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.forward;
            normal.Normalize();
            if (outward.sqrMagnitude > 0.0001f && Vector3.Dot(normal, outward) < 0f) normal = -normal;

            Vector3 incoming = Vector3.ProjectOnPlane(collision.relativeVelocity, Vector3.up);
            if (incoming.sqrMagnitude < 0.01f)
                incoming = Vector3.ProjectOnPlane(otherBody.linearVelocity, Vector3.up);
            if (incoming.sqrMagnitude < 0.01f || Vector3.Dot(incoming, normal) >= -0.01f) return;

            Vector3 reflected = Vector3.Reflect(incoming, normal);
            reflected = (reflected + normal * _outwardKick).normalized *
                Mathf.Max(_minimumBounceSpeed, incoming.magnitude * _bounceMultiplier);
            if (item != null)
            {
                foreach (RecyclingItem member in RecyclingItem.Collect(item))
                    member.ReceiveStoneBounce(reflected, _flowPauseSeconds);
            }
            else bomb.ReceiveStoneBounce(reflected, _flowPauseSeconds);

            _bounceTime = 0f;
            _wobbleDirection = outward.x < 0f ? -1f : 1f;
        }

        void Update()
        {
            if (_bounceTime >= _animationDuration) return;
            _bounceTime += Time.deltaTime;
            float phase = _bounceTime * _oscillationsPerSecond * Mathf.PI * 2f;
            float amplitude = Mathf.Exp(-6f * _bounceTime) * Mathf.Sin(phase);
            float stretch = amplitude * _squash;
            _visual.localScale = Vector3.Scale(_restScale,
                new Vector3(1f - stretch, 1f + stretch * 1.35f, 1f - stretch * 0.7f));
            _visual.localRotation = _restRotation *
                Quaternion.Euler(amplitude * _wobbleDegrees * 0.35f,
                    0f, amplitude * _wobbleDegrees * _wobbleDirection);
            if (_bounceTime >= _animationDuration)
            {
                _visual.localScale = _restScale;
                _visual.localRotation = _restRotation;
            }
        }
    }
}
