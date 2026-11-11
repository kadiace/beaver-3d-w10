using UnityEngine;

public class GameSceneContext : MonoBehaviour
{
    private const int CIRCLE_SEGMENTS = 64;

    [Header("3D drop")]
    [SerializeField, Min(0f)] private float _rotationSpeed = 90f;
    [SerializeField] private Vector2 _placementCenterOffset;
    [SerializeField, Min(0.1f)] private float _placementRadius = 9f;

    [Header("Flood")]
    [SerializeField] private float _floodSurfaceY = -1.75f;

    [Header("Creek campaign")]
    [SerializeField, Min(1f)] private float _creekStageSeconds = 30f;
    [SerializeField] private int[] _creekStageTargets = { 20, 40, 60, 80, 100 };

    public float CreekStageSeconds => _creekStageSeconds;
    public int[] CreekStageTargets => _creekStageTargets;

    /// <summary>Inspector에서 설정한 스테이지 공통 홍수 수면의 월드 Y 좌표를 반환한다.</summary>
    public float FloodSurfaceY => _floodSurfaceY;

    private void Start()
    {
        Managers.Input.UseRecyclingControls(false);
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
