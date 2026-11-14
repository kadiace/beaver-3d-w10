using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;

public class InputManager
{
    [Header("Input System")]
    private InputActionAsset _actions;
    private InputActionMap _playerMap;
    private InputActionMap _recyclingMap;
    private InputActionMap _uiMap;
    private InputMode _inputMode = InputMode.Player;
    private bool _useRecyclingControls;

    // Keep the shared EventSystem mouse bindings for the other game modes.
    private InputSystemUIInputModule _uiInputModule;
    private InputActionReference _savedPoint;
    private InputActionReference _savedLeftClick;
    private InputActionReference _savedRightClick;
    private InputActionReference _savedMiddleClick;
    private InputActionReference _savedScrollWheel;
    private bool _savedBackgroundDeselect;

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
    private InputAction _uiSubmit;
    private InputAction _uiCancel;
    private InputAction _uiSortExclude;

    // The active control scheme can prefer a connected pad. Read recycling keyboard
    // controls directly as well so a plugged-in gamepad never locks out the keyboard.
    public Vector2 MoveInput
    {
        get
        {
            if (_inputMode != InputMode.Player) return Vector2.zero;

            Vector2 input = _move.ReadValue<Vector2>();
            if (!_useRecyclingControls || Keyboard.current == null) return input;

            Keyboard keyboard = Keyboard.current;
            Vector2 arrows = new Vector2(
                (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f),
                (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f));
            return arrows.sqrMagnitude > 0f ? arrows.normalized : input;
        }
    }
    public bool HasPointer => _pointerPosition.controls.Count > 0;
    public Vector2 PointerPosition => _pointerPosition.ReadValue<Vector2>();
    public Vector2 PointerDelta => _pointerDelta.ReadValue<Vector2>();
    public bool PrimaryPressed => _primaryClick.WasPressedThisFrame();
    public bool PrimaryReleased => _primaryClick.WasReleasedThisFrame();
    public bool SecondaryPressed => _secondaryClick.WasPressedThisFrame();
    public bool SecondaryReleased => _secondaryClick.WasReleasedThisFrame();
    public bool SecondaryHeld => _secondaryClick.IsPressed();
    public float CameraZoom => _cameraZoom.ReadValue<Vector2>().y;
    public bool InteractPressed => _interact.WasPressedThisFrame() ||
        (_useRecyclingControls && WasBoundKeyboardButtonPressed(_interact));
    public bool SplitBundlePressed => _splitBundle.WasPressedThisFrame() ||
        (_useRecyclingControls && WasBoundKeyboardButtonPressed(_splitBundle));
    public bool CancelPressed => (_inputMode == InputMode.Player
        ? _cancel.WasPressedThisFrame() : _uiCancel.WasPressedThisFrame()) ||
        (_useRecyclingControls && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
    public bool RotateLeftPressed => _rotateLeft.WasPressedThisFrame();
    public bool RotateLeftHeld => _rotateLeft.IsPressed();
    public bool RotateRightPressed => _rotateRight.WasPressedThisFrame();
    public bool RotateRightHeld => _rotateRight.IsPressed();
    public bool FloodPreviewPressed => _floodPreview.WasPressedThisFrame();
    public bool ConfirmPressed => _inputMode == InputMode.UI && (_uiSubmit.WasPressedThisFrame() ||
        (_useRecyclingControls && Keyboard.current != null &&
         (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame ||
          Keyboard.current.numpadEnterKey.wasPressedThisFrame)));
    public bool SortExcludePressed => _inputMode == InputMode.UI && (_uiSortExclude.WasPressedThisFrame() ||
        (_useRecyclingControls && Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame));
    // Physical availability, not whichever device was used most recently.
    // Querying Gamepad.all also reflects hot-plugging while a modal pauses Time.timeScale.
    public bool GamePadConnected => Gamepad.all.Count > 0;

    /// <summary>액션의 키보드 바인딩을 읽어 현재 프레임에 해당 버튼이 눌렸는지 반환한다.</summary>
    private static bool WasBoundKeyboardButtonPressed(InputAction action)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return false;

        const string keyboardPath = "<Keyboard>/";
        foreach (InputBinding binding in action.bindings)
        {
            string path = binding.effectivePath;
            if (string.IsNullOrEmpty(path) || !path.StartsWith(keyboardPath, StringComparison.OrdinalIgnoreCase)) continue;
            ButtonControl button = keyboard.TryGetChildControl<ButtonControl>(path.Substring(keyboardPath.Length));
            if (button != null && button.wasPressedThisFrame) return true;
        }
        return false;
    }

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
        _uiSubmit = _uiMap.FindAction("Submit", true);
        _uiCancel = _uiMap.FindAction("Cancel", true);
        _uiSortExclude = _uiMap.FindAction("SortExclude", true);

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
        ConfigureUiPointerInput();
        if (mode == InputMode.Player)
        {
            (_useRecyclingControls ? _recyclingMap : _playerMap).Enable();
            Cursor.lockState = _useRecyclingControls ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = false;
            Time.timeScale = 1f;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !_useRecyclingControls;
            Time.timeScale = 0f;
        }
    }

    /// <summary>분리수거 모드에선 UI 포인터를 제거해 방향키/Submit으로만 버튼을 조작한다.</summary>
    private void ConfigureUiPointerInput()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;
        InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (module == null) return;

        if (_uiInputModule != module)
        {
            _uiInputModule = module;
            _savedPoint = module.point;
            _savedLeftClick = module.leftClick;
            _savedRightClick = module.rightClick;
            _savedMiddleClick = module.middleClick;
            _savedScrollWheel = module.scrollWheel;
            _savedBackgroundDeselect = module.deselectOnBackgroundClick;
        }

        module.point = _useRecyclingControls ? null : _savedPoint;
        module.leftClick = _useRecyclingControls ? null : _savedLeftClick;
        module.rightClick = _useRecyclingControls ? null : _savedRightClick;
        module.middleClick = _useRecyclingControls ? null : _savedMiddleClick;
        module.scrollWheel = _useRecyclingControls ? null : _savedScrollWheel;
        module.deselectOnBackgroundClick = !_useRecyclingControls && _savedBackgroundDeselect;
    }
}
