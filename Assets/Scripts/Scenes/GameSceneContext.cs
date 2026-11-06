using UnityEngine;

public class GameSceneContext : MonoBehaviour
{
    private void Start()
    {
        Managers.Input.SetInputMode(InputMode.Player);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        gameObject.AddComponent<RestoreCastle3D.CastleGame3D>();
        Debug.Log("[GameSceneContext] Game scene initialized.");
    }
}
