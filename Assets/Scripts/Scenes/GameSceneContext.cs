using UnityEngine;

public class GameSceneContext : MonoBehaviour
{
    private const int CIRCLE_SEGMENTS = 64;

    [Header("3D drop")]
    [SerializeField, Min(0f)] private float _rotationSpeed = 90f;
    [SerializeField] private Vector2 _placementCenterOffset;
    [SerializeField, Min(0.1f)] private float _placementRadius = 9f;

    private void Start()
    {
        Managers.Input.SetInputMode(InputMode.Player);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RestoreCastle3D.CastleGame3D game = gameObject.AddComponent<RestoreCastle3D.CastleGame3D>();
        game.RotationSpeed = _rotationSpeed;
        game.PlacementCenterOffset = _placementCenterOffset;
        game.PlacementRadius = _placementRadius;
        Debug.Log("[GameSceneContext] Game scene initialized.");
    }

    private void OnDrawGizmosSelected()
    {
        GameObject bed = GameObject.Find("Flat Muddy Creek Bed");
        if (bed == null) return;

        Bounds bounds = bed.GetComponent<BoxCollider>().bounds;
        Vector3 center = new Vector3(bounds.center.x + _placementCenterOffset.x,
            bounds.max.y + 0.05f, bounds.center.z + _placementCenterOffset.y);
        Color previousColor = Gizmos.color;
        Gizmos.color = Color.cyan;
        Vector3 previous = center + Vector3.right * _placementRadius;
        for (int i = 1; i <= CIRCLE_SEGMENTS; i++)
        {
            float angle = i * Mathf.PI * 2f / CIRCLE_SEGMENTS;
            Vector3 next = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                * _placementRadius;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.color = previousColor;
    }
}
