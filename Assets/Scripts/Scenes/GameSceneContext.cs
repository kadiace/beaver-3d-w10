using UnityEngine;

public class GameSceneContext : MonoBehaviour
{
    [Header("3D drop")]
    [SerializeField, Min(0f)] private float _rotationSpeed = 90f;

    private void Start()
    {
        Managers.Input.SetInputMode(InputMode.Player);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        gameObject.AddComponent<RestoreCastle3D.CastleGame3D>().RotationSpeed = _rotationSpeed;
        Debug.Log("[GameSceneContext] Game scene initialized.");
    }
}
