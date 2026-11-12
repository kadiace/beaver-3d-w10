using System;

using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager
{
    [Header("Input System")]
    private InputActionAsset _actions;
    private InputActionMap _playerMap;
    private InputActionMap _recyclingMap;
    private InputActionMap _uiMap;
    private InputMode _inputMode = InputMode.Player;
    private bool _useRecyclingControls;

    [Header("Player actions")]
    private InputAction _move;
    private InputAction _pointerPosition;
    private InputAction _pointerDelta;
    private InputAction _primaryClick;
    private InputAction _secondaryClick;
    private InputAction _cameraZoom;
    private InputAction _cancel;
    private InputAction _rotateLeft;
    private InputAction _rotateRight;
    private InputAction _floodPreview;
    private InputAction _interact;
    private InputAction _splitBundle;

    [Header("UI actions")]
    private InputAction _uiClick;
    private InputAction _uiCancel;

    public Vector2 MoveInput => _inputMode == InputMode.Player ? _move.ReadValue<Vector2>() : Vector2.zero;
    public bool HasPointer => _pointerPosition.controls.Count > 0;
    public Vector2 PointerPosition => _pointerPosition.ReadValue<Vector2>();
    public Vector2 PointerDelta => _pointerDelta.ReadValue<Vector2>();
    public bool PrimaryPressed => _primaryClick.WasPressedThisFrame();
    public bool PrimaryReleased => _primaryClick.WasReleasedThisFrame();
    public bool SecondaryPressed => _secondaryClick.WasPressedThisFrame();
    public bool SecondaryReleased => _secondaryClick.WasReleasedThisFrame();
    public bool SecondaryHeld => _secondaryClick.IsPressed();
    public float CameraZoom => _cameraZoom.ReadValue<Vector2>().y;
    public bool InteractPressed => _interact.WasPressedThisFrame();
    public bool SplitBundlePressed => _splitBundle.WasPressedThisFrame();
    public bool CancelPressed => _inputMode == InputMode.Player
        ? _cancel.WasPressedThisFrame() : _uiCancel.WasPressedThisFrame();
    public bool RotateLeftPressed => _rotateLeft.WasPressedThisFrame();
    public bool RotateLeftHeld => _rotateLeft.IsPressed();
    public bool RotateRightPressed => _rotateRight.WasPressedThisFrame();
    public bool RotateRightHeld => _rotateRight.IsPressed();
    public bool FloodPreviewPressed => _floodPreview.WasPressedThisFrame();
    public bool ConfirmPressed => _inputMode == InputMode.UI && _uiClick.WasPressedThisFrame();
    public bool GamePadConnected { get; private set; }

    /// <summary>프로젝트 공용 Input Actions에서 게임과 UI 액션을 찾아 보관하고 입력 모드를 초기화한다.</summary>
    public void Init()
    {
        if (_actions != null) return;
        _actions = InputSystem.actions;
        if (_actions == null) throw new InvalidOperationException("Project-wide Input Actions asset is not configured.");

        _playerMap = _actions.FindActionMap("Player", true);
        _recyclingMap = _actions.FindActionMap("Recycling", true);
        _uiMap = _actions.FindActionMap("UI", true);
        CacheGameplayActions(_playerMap);
        _rotateLeft = _playerMap.FindAction("RotateLeft", true);
        _rotateRight = _playerMap.FindAction("RotateRight", true);
        _floodPreview = _playerMap.FindAction("ToggleFloodPreview", true);
        _interact = _recyclingMap.FindAction("Interact", true);
        _splitBundle = _recyclingMap.FindAction("SplitBundle", true);
        _uiClick = _uiMap.FindAction("Click", true);
        _uiCancel = _uiMap.FindAction("Cancel", true);

        InputSystem.onActionChange += CheckDeviceType;
        SetInputMode(InputMode.Player);
    }

    /// <summary>분리수거 모드 여부를 받아 공용 조작의 참조와 활성 맵을 전환하고 기존 Player 맵을 보존한다.</summary>
    public void UseRecyclingControls(bool enabled)
    {
        _useRecyclingControls = enabled;
        CacheGameplayActions(enabled ? _recyclingMap : _playerMap);
        SetInputMode(_inputMode);
    }

    /// <summary>선택된 게임 액션 맵을 받아 이동·포인터·버튼·취소 액션 참조를 갱신한다.</summary>
    private void CacheGameplayActions(InputActionMap map)
    {
        _move = map.FindAction("Move", true);
        _pointerPosition = map.FindAction("PointerPosition", true);
        _pointerDelta = map.FindAction("PointerDelta", true);
        _primaryClick = map.FindAction("PrimaryClick", true);
        _secondaryClick = map.FindAction("SecondaryClick", true);
        _cameraZoom = map.FindAction("CameraZoom", true);
        _cancel = map.FindAction("Cancel", true);
    }

    /// <summary>입력 모드에 따라 게임 액션을 켜거나 끄고 UI 액션과 커서·시간 상태를 갱신한다.</summary>
    public void SetInputMode(InputMode mode)
    {
        _inputMode = mode;
        if (_actions == null) return;

        _uiMap.Enable();
        _playerMap.Disable();
        _recyclingMap.Disable();
        if (mode == InputMode.Player)
        {
            (_useRecyclingControls ? _recyclingMap : _playerMap).Enable();
            Cursor.lockState = _useRecyclingControls ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _useRecyclingControls;
            Time.timeScale = 1f;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
        }
    }

    /// <summary>공용 액션이 수행된 입력 장치를 확인하고 게임패드 사용 상태를 갱신한다.</summary>
    private void CheckDeviceType(object actionObject, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed) return;
        if (actionObject is InputAction action && action.actionMap?.asset == _actions)
            GamePadConnected = action.activeControl?.device is Gamepad;
    }
}
