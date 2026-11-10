using System;
using System.Linq;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using TMPro;

namespace RestoreCastle3D
{
    /// <summary>
    /// Timed Creek campaign: five score challenges, followed by endless survival.
    /// Only trash actually reaching the scoring boundary increases pollution.
    /// </summary>
    public sealed class CastleCreekStageRun3D : MonoBehaviour
    {
        private const int CampaignStages = 5;
        private const int PollutionPerTrash = 5;
        private const int MaxPollution = 100;
        private static readonly int[] DefaultTargets = { 20, 40, 60, 80, 100 };

        private CastleCreekScore3D _score;
        private CastleGame3D _game;
        private Canvas _canvas;
        private TMP_FontAsset _font;
        private float _stageDuration = 30f;
        private int[] _targets = (int[])DefaultTargets.Clone();

        private int _stageIndex;
        private int _stageStartScore;
        private int _pollution;
        private float _remaining;
        private bool _stageTimerStarted;
        private float _stageStartedAt;
        private float _endlessElapsed;
        private float _lastPollutionTime = float.NegativeInfinity;
        private int _lastPollutionAdded;
        private bool _endless;
        private bool _awaitingContinue;
        private bool _gameOver;
        private bool _ownsPause;
        private float _previousTimeScale = 1f;

        private TextMeshProUGUI _stageLabel;
        private TextMeshProUGUI _timerLabel;
        private TextMeshProUGUI _goalLabel;
        private TextMeshProUGUI _pollutionLabel;
        private TextMeshProUGUI _resultTitle;
        private TextMeshProUGUI _resultBody;
        private TextMeshProUGUI _buttonLabel;
        private RectTransform _goalFill;
        private RectTransform _pollutionFill;
        private Image _pollutionFillImage;
        private GameObject _goalBar;
        private GameObject _overlay;
        private GameObject _effectsOverlay;
        private Button _continueButton;

        public bool IsPaused => _awaitingContinue || _gameOver || (_effectsOverlay != null && _effectsOverlay.activeSelf);
        public bool IsEndless => _endless;
        public bool StageTimerStarted => _stageTimerStarted;
        public int Pollution => _pollution;
        public int StageNumber => _stageIndex + 1;
        public int StageScore => _score == null ? 0 : _score.TotalScore - _stageStartScore;
        public int TargetScore => _targets[Mathf.Min(_stageIndex, CampaignStages - 1)];
        public float CreekSpeed => SpeedForStage(_stageIndex);
        public float CreekSpawnInterval => IntervalForStage(_stageIndex);

        public static float SpeedForStage(int stageIndex) =>
            Mathf.Lerp(1f, 2f, Mathf.Clamp01(stageIndex / 4f));
        public static float IntervalForStage(int stageIndex) => 1f / SpeedForStage(stageIndex);
        public static int CalculatePollution(int current, int trashCount) =>
            Mathf.Min(MaxPollution, Mathf.Clamp(current, 0, MaxPollution) +
                Mathf.Clamp(trashCount, 0, MaxPollution / PollutionPerTrash) * PollutionPerTrash);
        public static bool HasMetTarget(int stageScore, int target) => stageScore >= target;

        public void Initialize(CastleCreekScore3D score, Canvas canvas, TMP_FontAsset font)
        {
            _score = score;
            _canvas = canvas;
            _font = font;
            _game = GetComponentInParent<CastleGame3D>();

            GameSceneContext settings = GetComponentInParent<GameSceneContext>();
            if (settings != null)
            {
                _stageDuration = Mathf.Max(1f, settings.CreekStageSeconds);
                int[] configured = settings.CreekStageTargets;
                if (configured != null && configured.Length == CampaignStages)
                {
                    _targets = new int[CampaignStages];
                    for (int i = 0; i < CampaignStages; i++)
                        _targets[i] = Mathf.Max(1, configured[i]);
                }
            }

            _remaining = _stageDuration;
            CreateUi();
            RefreshHud();
        }

        private void Update()
        {
            if (_score == null) return;
            if (_effectsOverlay.activeSelf && Managers.Input.CancelPressed)
                HideEffects();
            if (!IsPaused)
            {
                if (_endless)
                    _endlessElapsed += Time.deltaTime;
                else if (_stageTimerStarted)
                {
                    // An absolute start time makes the full 30 seconds independent of
                    // MonoBehaviour Update order on the first arrival frame.
                    _remaining = Mathf.Max(0f, _stageDuration - (Time.time - _stageStartedAt));
                    if (_remaining <= 0f) FinishStage();
                }
            }
            RefreshHud();
        }

        /// <summary>
        /// Begin this stage's countdown when its first piece actually crosses the
        /// downstream scoring boundary (z = 15), even if that piece earns zero points.
        /// Further settlements must never restart or extend the countdown.
        /// </summary>
        public void OnScoringBoundaryReached()
        {
            if (IsPaused || _endless || _stageTimerStarted) return;
            _stageTimerStarted = true;
            _stageStartedAt = Time.time;
            _remaining = _stageDuration;
            RefreshHud();
        }

        /// <summary>Called once per scoring-boundary settlement, never for Y despawns or explosions.</summary>
        public int OnTrashReachedBoundary(int trashCount)
        {
            if (trashCount <= 0 || IsPaused) return 0;
            int previous = _pollution;
            _pollution = CalculatePollution(previous, trashCount);
            _lastPollutionAdded = _pollution - previous;
            _lastPollutionTime = Time.unscaledTime;
            if (_pollution >= MaxPollution)
            {
                _gameOver = true;
                PauseWorld();
                ShowResult("게임 오버", "하천 오염도가 100에 도달했습니다.\n최종 점수 " +
                    _score.TotalScore + "점", "다시 시작", RestartRun);
            }
            RefreshHud();
            return _lastPollutionAdded;
        }

        private void FinishStage()
        {
            bool reachedTarget = HasMetTarget(StageScore, TargetScore);
            if (!reachedTarget)
            {
                _gameOver = true;
                PauseWorld();
                ShowResult("게임 오버", "스테이지 " + StageNumber + " 목표 점수 미달\n이번 스테이지 " +
                    StageScore + " / " + TargetScore + "점\n최종 점수 " +
                    _score.TotalScore + "점", "다시 시작", RestartRun);
                return;
            }

            _awaitingContinue = true;
            PauseWorld();
            if (_stageIndex == CampaignStages - 1)
            {
                ShowResult("승리!", "5개 스테이지를 모두 통과했습니다!\n이번 스테이지 " +
                    StageScore + " / " + TargetScore + "점\n누적 " + _score.TotalScore +
                    "점 · 다음은 목표 점수 없는 무한모드입니다.", "무한모드 시작", ContinueRun);
            }
            else
            {
                ShowResult("스테이지 통과", "스테이지 " + StageNumber + " 목표 달성!\n이번 스테이지 " +
                    StageScore + " / " + TargetScore + "점\n누적 " + _score.TotalScore +
                    "점 · 오염도 " + _pollution + " / " + MaxPollution,
                    "다음 스테이지", ContinueRun);
            }
        }

        private void ContinueRun()
        {
            if (!_awaitingContinue || _gameOver) return;
            _awaitingContinue = false;
            _stageIndex++;
            if (_stageIndex >= CampaignStages)
            {
                _endless = true;
                _endlessElapsed = 0f;
            }
            else
            {
                _stageStartScore = _score.TotalScore;
                _stageTimerStarted = false;
                _stageStartedAt = 0f;
                _remaining = _stageDuration;
            }
            _overlay.SetActive(false);
            ResumeWorld();
            RefreshHud();
        }

        /// <summary>일시 정지를 해제하고 매니저 정리를 거쳐 현재 게임 씬을 다시 로드한다.</summary>
        private void RestartRun()
        {
            ResumeWorld();
            Managers.Scene.ReloadCurrentScene();
        }

        private void PauseWorld()
        {
            if (_ownsPause) return;
            _game?.SuspendCreekInput();
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            _ownsPause = true;
        }

        private void ResumeWorld()
        {
            if (!_ownsPause) return;
            Time.timeScale = _previousTimeScale;
            _ownsPause = false;
        }

        private void OnDestroy()
        {
            if (_effectsOverlay != null && _effectsOverlay.activeSelf) HideEffects();
            ResumeWorld();
        }

        /// <summary>모든 자재의 고유 능력을 표시하고 게임 시간을 멈춘다.</summary>
        private void ShowEffects()
        {
            if (IsPaused) return;
            _game.SuspendCreekInput();
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            _effectsOverlay.SetActive(true);
            _effectsOverlay.transform.SetAsLastSibling();
        }

        /// <summary>효과 표를 닫고 열기 전의 시간 배율을 복원한다.</summary>
        private void HideEffects()
        {
            if (!_effectsOverlay.activeSelf) return;
            _effectsOverlay.SetActive(false);
            Time.timeScale = _previousTimeScale;
        }

        private void RefreshHud()
        {
            if (_stageLabel == null) return;
            _stageLabel.text = _endless ? "무한모드  ∞" :
                "STAGE " + StageNumber + " / " + CampaignStages;
            if (_endless)
            {
                int elapsed = Mathf.FloorToInt(_endlessElapsed);
                _timerLabel.text = (elapsed / 60).ToString("00") + ":" +
                    (elapsed % 60).ToString("00");
                _goalLabel.text = "무한 생존 · 누적 " + _score.TotalScore + "점";
                _goalBar.SetActive(false);
            }
            else
            {
                _timerLabel.text = _stageTimerStarted
                    ? "남은 시간 " + Mathf.CeilToInt(_remaining).ToString("00") + "초"
                    : "첫 기물 도착 대기";
                _goalLabel.text = "이번 스테이지 점수  " + StageScore + " / " + TargetScore + "점";
                _goalBar.SetActive(true);
                SetFill(_goalFill, (float)Mathf.Max(0, StageScore) / TargetScore);
            }

            _pollutionLabel.text = "하천 오염도  " + _pollution + " / " + MaxPollution +
                (Time.unscaledTime - _lastPollutionTime < 1.3f ?
                    "   +" + _lastPollutionAdded + " 오염도" : "");
            SetFill(_pollutionFill, (float)_pollution / MaxPollution);
            bool danger = _pollution >= 75;
            _pollutionFillImage.color = danger ? new Color(0.98f, 0.25f, 0.24f) :
                new Color(0.95f, 0.53f, 0.28f);
            _pollutionLabel.color = danger ? new Color(1f, 0.43f, 0.39f) : Color.white;
            _timerLabel.color = !_endless && _stageTimerStarted && _remaining <= 5f ?
                new Color(1f, 0.48f, 0.36f) : Color.white;
        }

        private static void SetFill(RectTransform fill, float fraction)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        /// <summary>공용 EventSystem을 연결하고 캠페인 상태와 결과를 표시할 UI를 생성한다.</summary>
        private void CreateUi()
        {
            if (EventSystem.current == null)
                Managers.Resource.Instantiate("Prefabs/UIs/EventSystem");
            if (_canvas.GetComponent<GraphicRaycaster>() == null)
                _canvas.gameObject.AddComponent<GraphicRaycaster>();

            RectTransform root = (RectTransform)_canvas.transform;
            RectTransform panel = NewImage("Stage and Pollution HUD", root,
                new Color(0.045f, 0.08f, 0.12f, 0.90f), false);
            Place(panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), new Vector2(566f, 166f));

            _stageLabel = Label("Stage Name", panel, 25, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(22f, -10f), new Vector2(315f, 38f));
            _stageLabel.fontStyle = FontStyles.Bold;
            _timerLabel = Label("Stage Countdown", panel, 22, TextAlignmentOptions.Right,
                new Vector2(1f, 1f), new Vector2(-20f, -12f), new Vector2(230f, 35f));
            _goalLabel = Label("Stage Goal", panel, 19, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(22f, -51f), new Vector2(525f, 29f));

            RectTransform goalBar = NewImage("Goal Progress Bar", panel,
                new Color(0.23f, 0.27f, 0.30f, 1f), false);
            Place(goalBar, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -84f), new Vector2(521f, 12f));
            _goalBar = goalBar.gameObject;
            _goalFill = NewImage("Goal Filled", goalBar,
                new Color(0.45f, 0.9f, 0.58f), false);
            StretchFill(_goalFill);

            _pollutionLabel = Label("Pollution", panel, 19, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(22f, -104f), new Vector2(525f, 27f));
            RectTransform pollutionBar = NewImage("Pollution Bar", panel,
                new Color(0.23f, 0.27f, 0.30f, 1f), false);
            Place(pollutionBar, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -138f), new Vector2(521f, 12f));
            _pollutionFill = NewImage("Pollution Filled", pollutionBar,
                new Color(0.95f, 0.53f, 0.28f), false);
            _pollutionFillImage = _pollutionFill.GetComponent<Image>();
            StretchFill(_pollutionFill);

            RectTransform effectsButton = NewImage("Effects Table Button", root,
                new Color(0.18f, 0.39f, 0.45f, 1f), true);
            Place(effectsButton, new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(297f, -18f), new Vector2(118f, 48f));
            effectsButton.gameObject.AddComponent<Button>().onClick.AddListener(ShowEffects);
            Label("Effects Button Label", effectsButton, 20, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 42f)).text = "효과 표";

            RectTransform effectsOverlay = NewImage("Effects Table Overlay", root,
                new Color(0.015f, 0.025f, 0.04f, 0.84f), true);
            effectsOverlay.anchorMin = Vector2.zero;
            effectsOverlay.anchorMax = Vector2.one;
            effectsOverlay.offsetMin = Vector2.zero;
            effectsOverlay.offsetMax = Vector2.zero;
            _effectsOverlay = effectsOverlay.gameObject;
            RectTransform table = NewImage("Effects Table", effectsOverlay,
                new Color(0.085f, 0.14f, 0.19f, 1f), false);
            table.anchorMin = new Vector2(0.5f, 0.5f);
            table.anchorMax = new Vector2(0.5f, 0.5f);
            table.pivot = new Vector2(0.5f, 0.5f);
            table.sizeDelta = new Vector2(760f, 0f);
            table.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(650f, Screen.height - 40f));
            Label("Effects Title", table, 28, TextAlignmentOptions.Left,
                new Vector2(0.5f, 1f), new Vector2(-340f, -12f), new Vector2(550f, 48f)).text = "오브젝트별 효과";
            RectTransform close = NewImage("Close Effects", table,
                new Color(0.35f, 0.46f, 0.50f, 1f), true);
            Place(close, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-14f, -12f), new Vector2(52f, 46f));
            close.gameObject.AddComponent<Button>().onClick.AddListener(HideEffects);
            Label("Close Label", close, 28, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 42f)).text = "×";

            RectTransform viewport = NewImage("Effects Viewport", table,
                new Color(0.04f, 0.075f, 0.10f, 1f), true);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(22f, 20f);
            viewport.offsetMax = new Vector2(-22f, -72f);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = new GameObject("Effects Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            CastleCatalog3D catalog = new CastleCatalog3D();
            float rowTop = 0f;
            foreach (CastleMaterial3D material in catalog.All.OrderBy(m => m.category).ThenBy(m => m.name))
            {
                string abilities = material.abilities == null || material.abilities.Length == 0
                    ? "고유 효과 없음" : string.Join(" · ", material.abilities.Where(a => !string.IsNullOrWhiteSpace(a)));
                TextMeshProUGUI row = Label(material.id, content, 18, TextAlignmentOptions.TopLeft,
                    new Vector2(0f, 1f), Vector2.zero, new Vector2(680f, 40f));
                row.rectTransform.anchorMin = new Vector2(0f, 1f);
                row.rectTransform.anchorMax = new Vector2(1f, 1f);
                row.rectTransform.pivot = new Vector2(0f, 1f);
                row.rectTransform.sizeDelta = new Vector2(-24f, 0f);
                row.rectTransform.anchoredPosition = new Vector2(12f, -rowTop);
                row.enableWordWrapping = true;
                row.text = "<b>" + material.name + "</b>  [" + material.category + "]  " + abilities;
                row.ForceMeshUpdate();
                float height = Mathf.Max(42f, row.preferredHeight + 12f);
                row.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                rowTop += height + 4f;
            }
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowTop);
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 35f;
            _effectsOverlay.SetActive(false);

            RectTransform overlay = NewImage("Stage Result Overlay", root,
                new Color(0.015f, 0.025f, 0.04f, 0.80f), true);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            _overlay = overlay.gameObject;
            RectTransform card = NewImage("Result Card", overlay,
                new Color(0.085f, 0.14f, 0.19f, 1f), false);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(590f, 330f));
            TextMeshProUGUI eyebrow = Label("Result Eyebrow", card, 18,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 112f), new Vector2(500f, 30f));
            eyebrow.text = "BEAVER CREEK";
            eyebrow.color = new Color(0.55f, 0.9f, 0.8f);
            _resultTitle = Label("Result Title", card, 39,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 59f), new Vector2(530f, 59f));
            _resultTitle.fontStyle = FontStyles.Bold;
            _resultBody = Label("Result Description", card, 22,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -20f), new Vector2(525f, 102f));
            _resultBody.enableWordWrapping = true;

            RectTransform buttonRect = NewImage("Result Action", card,
                new Color(0.35f, 0.78f, 0.61f, 1f), true);
            Place(buttonRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -118f), new Vector2(260f, 58f));
            _continueButton = buttonRect.gameObject.AddComponent<Button>();
            _buttonLabel = Label("Action Label", buttonRect, 23,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(245f, 52f));
            _buttonLabel.fontStyle = FontStyles.Bold;
            _buttonLabel.color = new Color(0.035f, 0.10f, 0.12f);
            _overlay.SetActive(false);
        }

        private void ShowResult(string title, string body, string action, UnityEngine.Events.UnityAction callback)
        {
            _resultTitle.text = title;
            _resultBody.text = body;
            _buttonLabel.text = action;
            _continueButton.onClick.RemoveAllListeners();
            _continueButton.onClick.AddListener(callback);
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
        }

        private TextMeshProUGUI Label(string name, Transform parent, int fontSize,
            TextAlignmentOptions alignment, Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            obj.transform.SetParent(parent, false);
            TextMeshProUGUI label = obj.GetComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            Place((RectTransform)obj.transform, anchor, anchor, position, size);
            return label;
        }

        private static RectTransform NewImage(string name, Transform parent, Color color, bool raycast)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(parent, false);
            Image image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return (RectTransform)obj.transform;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void StretchFill(RectTransform fill)
        {
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }
    }
}
