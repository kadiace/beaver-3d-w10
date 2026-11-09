using System.Collections.Generic;
using UnityEngine;

namespace RestoreCastle3D
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CreekDespawnZone : MonoBehaviour
    {
        private const float DESPAWN_DELAY = 5f;

        private readonly Dictionary<Collider, float> _enteredAt = new Dictionary<Collider, float>();

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<CastlePiece3D>() != null)
                _enteredAt[other] = Time.time;
        }

        private void OnTriggerStay(Collider other)
        {
            if (!_enteredAt.TryGetValue(other, out float enteredAt) ||
                Time.time - enteredAt < DESPAWN_DELAY) return;

            _enteredAt.Remove(other);
            CastlePiece3D piece = other.GetComponentInParent<CastlePiece3D>();
            if (piece != null) piece.Despawn();
        }

        private void OnTriggerExit(Collider other)
        {
            _enteredAt.Remove(other);
        }

        private void OnDisable()
        {
            _enteredAt.Clear();
        }
    }
}
