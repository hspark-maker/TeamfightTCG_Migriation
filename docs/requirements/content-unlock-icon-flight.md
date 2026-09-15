# 컨텐츠 해금 아이콘 이동 연출

## Overview

`Assets/Assets/Prefabs/UI/OverlayUI/ContentUnlockIntroView.prefab`의 확인 버튼을 누르면 주변 요소가 사라지고, 중앙 아이콘이 해당 컨텐츠 버튼으로 이동하며 해금을 알린다.

현재 `ContentUnlockPresentation.ShowCurrent`는 팝업을 표시할 때 버튼 해금 효과도 재생한다. 버튼 해금 효과의 시작을 중앙 아이콘 도착 시점으로 옮겨, 소개한 컨텐츠와 실제 진입 버튼을 연결한다.

## Goals

- 팝업 확인 후 배경·문구·확인 버튼·장식을 제거한다.
- 중앙 아이콘은 끊김 없이 유지한다.
- 아이콘을 대상 버튼으로 이동·축소한다.
- 도착 순간 버튼 잠금 표시를 해제하고 기존 해금 효과를 재생한다.

## Out of Scope

- 컨텐츠 해금 조건·저장 데이터 변경.
- 팝업 등장 연출·로비 레이아웃 재설계.
- 대상 컨텐츠 화면 자동 진입.

## Technical Requirements

- 기존 `ContentUnlockIntroView`, `ContentUnlockPresentation`, `FeatureLockView` 연결을 활용한다.
- 연출은 기존 확인 버튼을 누른 뒤 시작한다.
- `CanvasGroup`으로 주변 요소만 페이드아웃한다. 중앙 아이콘은 부모 알파의 영향을 받지 않도록 분리한다.
- 대상 버튼의 `RectTransform`을 기준으로 이동하며 해상도와 Canvas 배율을 반영한다.
- 기존 DOTween을 사용한다. 퇴장·이동 시간과 곡선은 Inspector에서 조정할 수 있어야 한다.
- 실제 해금 판정은 유지하고 버튼의 잠김 표현만 아이콘 도착까지 보류한다.
- 이동 중 암막을 걷어 목적지를 노출한다. 입력 차단은 도착 효과 완료까지 유지한다.
- 여러 컨텐츠를 연속 소개하면 각 아이콘의 이동과 버튼 해금 효과가 완료된 뒤 다음 팝업을 표시한다.
- 대상 버튼이 없거나 비활성이면 이동을 생략하고 정상 종료한다.
- 중단 시 트윈·입력 차단을 정리하고 아이콘 위치와 크기를 복구한다.
- 아이콘 이동과 버튼 해금 효과가 완료된 뒤 후속 온보딩 단계에 완료를 통지한다.

참고 파일:

- `Assets/Assets/Prefabs/UI/OverlayUI/ContentUnlockIntroView.prefab`
- `Assets/Scripts/UI/Tutorial/ContentUnlockIntroView.cs`
- `Assets/Scripts/UI/Lobby/ContentUnlockPresentation.cs`
- `Assets/Scripts/UI/Common/FeatureLockView.cs`
- `Assets/Scripts/Editor/Tutorial/ContentUnlockIntroValidation.cs`

## Acceptance Criteria

- [ ] 주변 요소가 사라지는 동안 중앙 아이콘은 계속 보인다.
- [ ] 아이콘이 해당 버튼에 정확히 도착한다.
- [ ] 도착 전 버튼 해금 효과가 먼저 나오지 않는다.
- [ ] 도착 순간 아이콘이 버튼에 합쳐지고 해금 효과가 한 번 재생된다.
- [ ] 완료 후 다음 온보딩 단계가 진행된다.
- [ ] 연속 확인·중단·재표시에도 중복 완료나 입력 잠김이 없다.
- [ ] 다른 화면 비율에서도 이동 위치가 맞는다.
- [ ] 여러 컨텐츠 소개 시 각 이동·해금 효과 완료 후 다음 소개가 열린다.
- [ ] 대상 버튼이 없거나 비활성이어도 진행이 멈추지 않는다.

## Open Questions

없음. 기존 확인 버튼을 누른 뒤 이동 연출을 시작하는 흐름으로 사용자 확정.
