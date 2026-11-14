# 분류기 투입 미니게임 구현 준비

## 현재 연결 지점

- `RecyclingGame3D.HandleInteract()`가 활성 분류기 앞에서 집기·놓기 입력을 받으면 `Submit(chute)`를 호출한다.
- `Submit()`은 현재 운반 묶음의 정의를 복사하고 분류 창을 연다. 창의 최종 결과를 받은 뒤 점수와 통계를 갱신한다.
- `RecyclingRules.Evaluate()`가 분류 결과와 묶음 보너스를 계산한다. 목재는 분류기에 넣어도 점수와 자원을 얻지 않는다.
- `RecyclingHud3D`는 `Game.unity`의 화면 전체 uGUI Canvas에 연결되어 있고, 별도 분류 창 프리팹을 띄운다.
- `Managers.Input.SetInputMode(InputMode.UI)`는 입력을 UI로 바꾸고 `Time.timeScale`을 0으로 설정한다. 작업 재개에는 `InputMode.Player`를 사용한다.

## 구현 방향

1. **제출 스냅샷:** 제출 입력 시 기물의 `RecyclingDefinition`과 분류기 종류를 순서대로 복사한다. 풀로 반환된 `RecyclingItem` 참조를 미니게임 화면에 보관하지 않는다.
2. **월드 정지:** 제출을 확정한 뒤 작업 단계를 별도 `Sorting` 상태로 전환하고 UI 입력 모드로 바꾼다. 기존 `Paused` 메뉴와 구분해 Esc가 미니게임을 잘못 닫지 않게 한다. 이 상태에서는 하루 타이머, 공급, 비버 조작, 분류기 상호작용이 멈춘다.
3. **화면 연출:** `Recycling HUD` Canvas 위에 별도 중앙 창 프리팹을 띄우고 제출 기물마다 `Sprites/Voxels/{아이템 ID}` 이미지를 표시한다. 매 제출마다 순서를 섞고 Feed Board 오른쪽에서 차례로 생성한다. Rigidbody2D와 CircleCollider2D를 가진 아이템은 보드, 두 쓰레기통 벽, 바닥의 BoxCollider2D에 부딪히며 굴러간다. 월드 일시정지 중에는 `Time.unscaledDeltaTime`으로 2D 물리를 수동 진행한다.
4. **판정선과 정산:** 붉은 판정선에 가장 가까운 아이템을 흰색으로 강조한다. Z 또는 게임패드 South를 눌렀을 때 그 아이템이 선과 겹치면 하나를 제외한다. 제외된 아이템은 점수와 오분류 통계에서 빠지고, 묶음 보너스는 남은 아이템 기준으로 계산한다. 모든 아이템이 투입된 뒤 마지막에 점수를 한 번만 계산하고 표시한다.
5. **복귀:** 마지막 아이템 투입 후 물리 이동 시간과 결과 표시가 끝나면 패널을 닫고 이전 작업 단계(`Supply` 또는 `Closing`)로 돌아간다. 그때 UI 입력 모드를 Player로 되돌린다. 마감 조건은 복귀 후 기존 `Update()`에서 판정한다.

## 텍스트와 조작 안내

- 제목, 조작 안내, 상태, 점수는 확대된 물리 영역과 분리한 `Text Layer` 아래에 둔다. 계층의 배율을 1로 유지하고 글꼴 크기와 사각형 크기로 실제 표시 크기를 정해 글자 가장자리의 확대 흐림을 줄인다.
- 제목 아래 `Keyboard Controls`와 `Gamepad Controls`에 조작 설명을 프리팹 텍스트로 작성한다. 스크립트는 장치에 맞는 안내 오브젝트만 활성화하며 문구를 덮어쓰지 않는다.
- 조작 안내는 낙하, 아이템 제외, 완료 결과 표시 중에도 계속 보인다. 하단 `Status`는 분류 진행, 아이템 제외 결과, 완료 상태를 표시하고 `Score`는 정산 점수를 표시한다.
- 창을 연 뒤 첫 아이템이 떨어지기까지의 유예시간은 프리팹의 `Initial Drop Delay`로 설정한다. 기본값은 2초이며, 게임 시간이 정지된 미니게임에서도 실제 경과 시간으로 기다린다. 0으로 설정하면 바로 투입한다.

## 수정 대상

| 대상 | 필요한 변경 |
|---|---|
| `Assets/Scripts/BeaverRecycling/RecyclingGame3D.cs` | `Submit()`에서 미니게임 시작, `Sorting` 단계와 완료 콜백 연결, 점수 적용 시점 관리 |
| `Assets/Scripts/BeaverRecycling/RecyclingHud3D.cs` | 전용 패널 표시·닫기와 기존 모달/일시정지 버튼의 표시 우선순위 연결 |
| `Assets/Scripts/BeaverRecycling/RecyclingSortingWindow3D.cs` | 제출 스냅샷으로 아이템별 이미지를 생성하고 비정지 시간으로 낙하시키며 완료를 알림 |
| `Assets/Resources/Prefabs/UIs/RecyclingSortingWindow.prefab` | 중앙 창과 낙하 영역, 바닥, 점수 문구를 별도 프리팹으로 보관 |
| `Assets/Scenes/Game.unity` | `RecyclingHud3D`에 분류 창 프리팹 참조 연결 |

## 구현 시 확인할 경계 조건

- 한 묶음의 모든 아이템이 각각 자신의 `Sprites/Voxels` 이미지로 나타난다. 플라스틱·유리·비닐·금속·목재 및 오분류가 빠지지 않는다.
- 낙하 중에는 강물 물리, 폭탄, 하루 타이머가 진행되지 않는다. 기존 `RecyclingItem.Absorb()`는 `Time.unscaledDeltaTime`으로 움직이므로, 분류기 제출 아이템의 3D 흡수 연출을 먼저 끝내거나 사용하지 않도록 처리한다.
- 기존 일시정지 메뉴가 미니게임 위에 열리지 않고, 완료 후 정상적으로 다시 열 수 있다.
- 마지막 제출이 마감 조건을 충족해도 낙하 화면이 끝난 후 하루 정산으로 넘어간다.
- 해상도와 Canvas Scaler 변화에도 네모가 낙하 영역 안에서 보인다.

## 게임 규칙에서 아직 정할 부분

현재 구현은 섞인 아이템의 물리 낙하, Z 또는 게임패드 South로 판정선의 아이템 하나 제외, 마지막 일괄 정산을 제공한다. 성공·실패 조건과 추가 조작은 후속 작업이다.
