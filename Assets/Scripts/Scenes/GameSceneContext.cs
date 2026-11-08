using UnityEngine;

public class GameSceneContext : MonoBehaviour
{
    [Header("Object rotation")]
    [SerializeField, Min(0f)] private float _rotationSpeed = 90f;

    [Header("Camera")]
    [SerializeField, Min(0f)] private float _cameraYawPerScrollStep = 5f;

    [Header("Flood")]
    [SerializeField, Min(0f)] private float _floodImpulse = 60f;

    [Header("Settlement")]
    [SerializeField, Min(0f)] private float _maxDropWait = 7f;
    [SerializeField, Min(0f)] private float _maxExplosionSettleWait = 7f;

    [Header("Destruction boundary")]
    [SerializeField] private float _destructionBoundaryZ = -28.2f;

    public float FloodImpulse => _floodImpulse;
    public float MaxDropWait => _maxDropWait;
    public float MaxExplosionSettleWait => _maxExplosionSettleWait;
    public float DestructionBoundaryZ => _destructionBoundaryZ;

    private void Start()
    {
        Managers.Input.SetInputMode(InputMode.Player);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RestoreCastle3D.CastleGame3D game = gameObject.AddComponent<RestoreCastle3D.CastleGame3D>();
        game.RotationSpeed = _rotationSpeed;
        game.CameraYawPerScrollStep = _cameraYawPerScrollStep;
        Debug.Log("[GameSceneContext] Game scene initialized.");
    }
}
