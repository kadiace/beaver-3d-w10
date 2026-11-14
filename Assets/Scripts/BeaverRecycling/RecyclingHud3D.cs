using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeaverRecycling
{
    public sealed class RecyclingHud3D : MonoBehaviour
    {
        [Header("HUD")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private Text _day;
        [SerializeField] private Text _score;
        [SerializeField] private Text _wood;
        [SerializeField] private Text _carry;
        [SerializeField] private Text _message;
        [SerializeField] private Text _hover;
        [SerializeField] private Text _toast;
        [SerializeField] private Button _pauseButton;

        [Header("Result and upgrades")]
        [SerializeField] private GameObject _modal;
        [SerializeField] private Text _modalTitle;
        [SerializeField] private Text _modalBody;
        [SerializeField] private Button _primary;
        [SerializeField] private Text _primaryText;
        [SerializeField] private Button _secondary;
        [SerializeField] private Text _secondaryText;
        [SerializeField] private GameObject _upgradeGroup;
        [SerializeField] private Button[] _upgradeButtons;
        [SerializeField] private Text[] _upgradeTexts;
        [Header("Sorting window")]
        [SerializeField] private RecyclingSortingWindow3D _sortingWindowPrefab;
        private float _messageUntil;
        private float _toastUntil;
        private Button[] _modalButtons;
        private Outline[] _focusOutlines;

        /// <summary>게임 콜백을 받아 씬에 저장된 버튼과 업그레이드 조작을 연결한다.</summary>
        public void Initialize(RecyclingGame3D game)
        {
            _pauseButton.onClick.AddListener(game.TogglePause);
            _primary.onClick.AddListener(game.PrimaryAction);
            _secondary.onClick.AddListener(game.SecondaryAction);
            for (int i = 0; i < _upgradeButtons.Length; i++)
            {
                int index = i;
                _upgradeButtons[i].onClick.AddListener(() => game.BuyUpgrade(index));
            }
            _hover.transform.parent.gameObject.SetActive(false);
            _toast.transform.parent.gameObject.SetActive(false);
            _message.text = "";
            SetupModalFocus();
        }

        /// <summary>모달 버튼에 게임패드 내비게이션과 선택 시 강조할 굵은 외곽선을 준비한다.</summary>
        private void SetupModalFocus()
        {
            _modalButtons = new Button[_upgradeButtons.Length + 2];
            _modalButtons[0] = _primary;
            _modalButtons[1] = _secondary;
            Array.Copy(_upgradeButtons, 0, _modalButtons, 2, _upgradeButtons.Length);
            _focusOutlines = new Outline[_modalButtons.Length];

            foreach (Button button in _modalButtons)
            {
                Navigation navigation = button.navigation;
                navigation.mode = Navigation.Mode.Automatic;
                button.navigation = navigation;
            }

            for (int i = 0; i < _modalButtons.Length; i++)
            {
                Outline outline = _modalButtons[i].gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 0.94f, 0.25f, 1f);
                outline.effectDistance = new Vector2(6f, -6f);
                outline.useGraphicAlpha = false;
                outline.enabled = false;
                _focusOutlines[i] = outline;
            }

            // During gameplay the pad south button means pickup, not UI submission.
            // The pause button remains clickable with the mouse; Start/Menu opens the modal.
            Navigation pauseNavigation = _pauseButton.navigation;
            pauseNavigation.mode = Navigation.Mode.None;
            _pauseButton.navigation = pauseNavigation;
        }

        /// <summary>현재 모달에서 선택 가능한 버튼에 포커스를 유지하고 없으면 기본 버튼을 선택한다.</summary>
        private void FocusModal()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || !_modal.activeInHierarchy) return;
            if (IsValidModalSelection(eventSystem.currentSelectedGameObject)) return;
            foreach (Button button in _modalButtons)
            {
                if (!button.gameObject.activeInHierarchy || !button.IsInteractable()) continue;
                eventSystem.SetSelectedGameObject(button.gameObject);
                return;
            }
        }

        private bool IsValidModalSelection(GameObject selected)
        {
            if (selected == null || _modalButtons == null) return false;
            foreach (Button button in _modalButtons)
                if (button.gameObject == selected && button.gameObject.activeInHierarchy && button.IsInteractable())
                    return true;
            return false;
        }

        /// <summary>일차·시간·점수·목재·운반 정보를 받아 최소 HUD 수치를 갱신한다.</summary>
        public void Refresh(int day, string phase, float seconds, int score, int target, int wood, string carry, bool working)
        {
            _day.text = day + "일차  ·  " + phase + (working ? " " + Mathf.CeilToInt(seconds) + "초" : "");
            _score.text = "획득 점수 " + score + " / " + target;
            _wood.text = "목재 " + wood;
            _carry.text = carry;
            _pauseButton.interactable = working;
        }

        /// <summary>제목·결과 문구·버튼 이름과 상점 여부를 받아 정산 또는 준비 패널을 표시한다.</summary>
        public void ShowModal(string title, string body, string primary, string secondary, bool upgrades)
        {
            _modal.SetActive(true);
            _modalTitle.text = title;
            _modalBody.text = body;
            _primaryText.text = primary;
            _secondary.gameObject.SetActive(!string.IsNullOrEmpty(secondary));
            _secondaryText.text = secondary;
            _upgradeGroup.SetActive(upgrades);
            RectTransform primaryRect = (RectTransform)_primary.transform;
            primaryRect.anchoredPosition = new Vector2(string.IsNullOrEmpty(secondary) ? 0f : -190f, primaryRect.anchoredPosition.y);
            _hover.transform.parent.gameObject.SetActive(false);
            FocusModal();
        }

        /// <summary>출근 또는 재개 시 고정 모달 패널을 닫는다.</summary>
        public void HideModal()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && IsValidModalSelection(eventSystem.currentSelectedGameObject))
                eventSystem.SetSelectedGameObject(null);
            _modal.SetActive(false);
        }

        /// <summary>제출 아이템과 투입구 종류를 받아 중앙 분류 창을 표시하고 제외 후 정산 결과를 알린다.</summary>
        public void ShowSorting(IReadOnlyList<RecyclingDefinition> definitions, RecyclingCategory chute,
            Action<RecyclingSubmission, int> completed)
        {
            RecyclingSortingWindow3D window = Instantiate(_sortingWindowPrefab, _root);
            window.transform.SetAsLastSibling();
            window.Play(definitions, chute, (submission, includedCount) =>
            {
                Destroy(window.gameObject);
                completed(submission, includedCount);
            });
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _hover.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>업그레이드 인덱스와 설명·구매 가능 상태를 받아 해당 구매 버튼을 갱신한다.</summary>
        public void SetUpgrade(int index, string description, bool available)
        {
            _upgradeTexts[index].text = description;
            _upgradeButtons[index].interactable = available;
            FocusModal();
        }

        /// <summary>짧은 조작 결과를 받아 HUD에 제한 시간 동안 표시한다.</summary>
        public void Message(string message)
        {
            _message.text = message;
            _messageUntil = Time.unscaledTime + 2f;
        }

        /// <summary>포인터 또는 기물의 화면 위치와 설명을 받아 화면 안에 호버 표지를 배치한다.</summary>
        public void Hint(string text, Vector2 screen)
        {
            _hover.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(text));
            if (string.IsNullOrEmpty(text)) return;
            _hover.text = text;
            Place((RectTransform)_hover.transform.parent, screen + new Vector2(24f, 35f));
        }

        /// <summary>점수·목재 문구와 정산 위치를 받아 월드 위치에 붙는 피드백을 표시한다.</summary>
        public void Event(string text, Vector3 world, Camera camera, Color color)
        {
            _toast.text = text;
            _toast.color = color;
            _toast.transform.parent.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 1.5f;
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f) screen = new Vector3(Screen.width * 0.5f, 130f, 0f);
            Place((RectTransform)_toast.transform.parent, screen);
        }

        /// <summary>UI 대상과 화면 좌표를 받아 Canvas 좌표로 변환하고 안전 영역 안에 둔다.</summary>
        private void Place(RectTransform rect, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
            Vector2 half = _root.rect.size * 0.5f;
            local.x = Mathf.Clamp(local.x, -half.x + 180f, half.x - 180f);
            local.y = Mathf.Clamp(local.y, -half.y + 120f, half.y - 110f);
            rect.anchoredPosition = local;
        }

        void Update()
        {
            if (Time.unscaledTime > _messageUntil) _message.text = "";
            if (Time.unscaledTime > _toastUntil) _toast.transform.parent.gameObject.SetActive(false);
            if (_focusOutlines == null) return;
            if (_modal.activeInHierarchy) FocusModal();
            GameObject selected = _modal.activeInHierarchy && EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            for (int i = 0; i < _focusOutlines.Length; i++)
                _focusOutlines[i].enabled = selected != null &&
                    _modalButtons[i].gameObject == selected && _modalButtons[i].IsInteractable();
        }
    }
}
