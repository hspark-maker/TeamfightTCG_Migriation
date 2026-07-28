# 카드팩 개봉 화면 2D 전환 — 구현 기록

> 3D 팩(절차적 메시 + URP Lit)을 걷어내고 uGUI 아트 기반으로 재구성한 작업의 기록.
> 씬 재구성은 Unity MCP 스크립트로 수행했고, 이 문서는 그 결과와 근거를 남긴다.
> 최종 갱신: 2026-07-28 · 대상 씬 `Assets/Scenes/CardPack.unity`

---

## 왜 바꿨나

팩만 3D로 남아 있어 씬이 Perspective 카메라·Directional Light·Physics 콜라이더·스카이박스 ambient를 끌고 다녔다. 그런데 **머티리얼 3종에 텍스처가 하나도 없어**(`CardPack_Front/Back/Seal.mat` 전 슬롯 `fileID: 0`) 3D가 만들어내던 건 단색 실루엣 + 조명뿐이었다. 카드·결과격자·입력판은 이미 전부 uGUI였다.

1차로 단색 Image로 옮겼더니 입체감이 사라졌는데, 그건 2D의 한계가 아니라 **아트가 없어서**였다. 실제 팩 아트를 넣자 3D 시절보다 입체감이 올라갔다 — 명암·측면·크림프·광택이 전부 아트에 구워져 있기 때문이다.

`docs/PACK_FEEL_PLAN.md`의 게임필 개선안은 3D 전제라 폐기됐지만, **아이들 플로팅·그림자 연동·스펙큘러 스윕**은 2D에서도 유효해 여기서 구현했다.

---

## 지금 구성

### 씬 계층 (`UICanvas` 하위, 형제 순서 = 그리기 순서)

```
UICanvas (Overlay, ScaleWithScreenSize 1440×3120, Width 기준)
 ├ BG          Image(Background_01)  rc=0   sd(3358,5966) ap(19,−251)   ← shakeTarget
 ├ PackRoot    RectTransform  ap(0,−160)                                ← packRoot (등장 트윈 타깃)
 │   │         + PackTearHandle + PackIdleMotion + PackSpecularSweep
 │   ├ Glow      Image(Glow_Radial, 푸른빛 a.24) sd(1100,1400)
 │   ├ Shadow    Image(Glow_Radial, 검정 a.45)   sd(560,150) ap(0,−510)
 │   └ PackVisual  sd(677,1015.5) ap(0,14.55)                           ← 아이들 모션 타깃
 │       ├ BodyClip  RectMask2D  sd(677,885.9) ap(0,−64.8)
 │       │   ├ Body      Image(팩 아트) sd(677,1015.5) ap(0,64.8)
 │       │   └ Specular  Image(Sweep, 18° 회전) sd(150,1300)
 │       └ SealClip  RectMask2D  sd(677,129.6) ap(0,442.95)             ← sealRoot
 │           └ Seal      Image(팩 아트) sd(677,1015.5) ap(0,−442.95)
 ├ RevealPanel (기존 — AcquireButton + TutorialAnchor 유지)
 ├ SkipButton  ← RevealPanel에서 캔버스 직속으로 이동
 └ TearHint
```

씬 루트: `Main Camera`(SolidColor / cullingMask Nothing / AudioListener 숙주) · `PackOpenDirector` · `EventSystem` · `UICanvas`.
삭제됨: `Directional Light` · `CardPack` 프리팹 인스턴스 · 씬 루트 `BG`(SpriteRenderer).

### 뜯기 — 같은 아트를 두 마스크로 쪼갠다

`Body`와 `Seal`은 **같은 스프라이트 전체**를 쓰고, 각자의 `RectMask2D`가 보여줄 구간만 남긴다.

- 붙어 있을 때: 두 조각이 정확히 이어져 이음매 없는 한 장으로 보인다
- `sealRoot`(SealClip)가 올라가면: 마스크와 내용이 함께 움직여 **상단 크림프 띠만 통째로 들려 올라간다**

분할선은 눈대중이 아니라 실측이다 — 아트의 행별 가로 색 변화량을 스캔해 크림프 줄무늬가 매끈한 포일로 바뀌는 지점(팩 상단에서 148텍셀 = 팩 높이의 10.0%)을 찾았다.

### 좌표 상수의 출처

아트 실측: 텍스처 1024×1536, 팩 알파 bbox `x[88..934] y[5..1487]` → 팩 847×1483 (비율 0.5711)

| 상수 | 값 | 유도 |
|---|---|---|
| `FULL_W` | 677 | 팩 보이는 폭 560px 목표 → 560 × 1024/847 |
| `FULL_H` | 1015.5 | FULL_W × 1536/1024 |
| `CENTER_FIX` | 14.55 | 팩 bbox 중심이 이미지 중심보다 22텍셀 아래 → UI 환산 |
| `SEAL_H` | 129.6 | 이미지 상단에서 크림프 끝까지 196텍셀 → UI 환산 |
| `sealTornOffset` | (0, 129.6, 0) | 크림프 한 줄 높이 — 원래 자리가 그대로 빈 틈으로 남는다 |
| `packEnterDrop` | 811 | 3 world × 270.2 px/world (3D 시절 등가) |
| `shakeStrength` | 68 | 0.25 world × 270.2 |

환산 기준 **270.2 참조px / world unit** — 3D 카메라 가시 높이 `2·10·tan30° = 11.547` world를 3120px에 대응.

### 절차 생성 스프라이트

아트가 없어 코드로 만들었다 (`Assets/Assets/Sprites/CardPack/`):
- `Glow_Radial.png` 256² — 방사 감쇠(제곱 2.2). 바닥 그림자(검정 틴트)와 팩 뒤 광채(푸른 틴트)에 공용
- `Sweep.png` 128×256 — 가우시안 세로 밴드. 스펙큘러 스윕용

둘 다 RGB 흰색 고정이고 색은 `Image.color` 틴트로 준다. 그라디언트라 블록 압축 밴딩이 눈에 띄어 **Uncompressed**로 임포트했다.

### 모션 스크립트

| 파일 | 역할 |
|---|---|
| `PackIdleMotion.cs` | 부유(주기 2.4s) + 펄스(2.0s, 어긋난 주기) + **바닥 그림자 연동**(뜰수록 작아지고 옅어짐) + 광채 밝기. 뜯기 진행도가 붙으면 정지하고 원위치로 걷힌다 |
| `PackSpecularSweep.cs` | 주기적으로 표면을 훑는 빛줄기. `BodyClip`의 `RectMask2D` 안에 갇혀 팩 밖으로 새지 않는다 |

`PackIdleMotion`이 움직이는 건 `PackVisual`이지 `PackRoot`가 아니다 — `PackRoot`는 `PackRevealView`의 등장 트윈이 쓰고 있어 같이 만지면 두 트윈이 같은 값을 두고 싸운다. `Shadow`는 `PackVisual` **바깥**에 둬야 팩만 뜬다.

---

## 지켜야 할 제약

### 🚨 raycastTarget은 전부 OFF

`PackTearHandle`은 `EventSystem.IsPointerOverGameObject()` 가드를 쓴다. 팩·배경 Image의 raycastTarget이 켜져 있으면 **팩 위에서 시작한 드래그가 이 가드에 막혀 개봉이 안 된다.**

> ⚠️ 단, 이 가드의 무인자 버전은 **마우스(pointerId −1)만 조회**한다. 터치 기기에선 항상 false라 가드 자체가 동작하지 않는다 — 즉 raycastTarget 규율은 에디터/마우스에서만 유효하고, 실기에선 `SkipButton` 위 드래그가 스킵 클릭과 뜯기를 동시에 태운다. 기존 결함이며 별도 처리 대상.

### 🚨 팩 크기를 localScale로 주지 말 것

`PackRevealView.EnterEntering()`이 매 개봉마다 `packRoot`의 `localScale`을 **1로 스탬프**한다. 크기는 `sizeDelta`로만 잡는다. (이 스탬프 때문에 3D 프리팹의 `(2,2,2)`도 런타임에 무효화돼 있었다 — 씬 뷰의 3.6×5.8이 아니라 실제로는 1.8×2.9였다.)

### 🚨 이 체인에 LayoutGroup·ContentSizeFitter 금지

매 프레임 레이아웃이 트윈을 덮는다. 앵커·피벗은 전부 `(0.5, 0.5)` 중앙, stretch 금지 — 그래야 `localPosition ≡ anchoredPosition`이 되어 홈 캡처가 Canvas rect 갱신 타이밍과 무관해진다.

### ⚠️ 단위가 섞여 있다

- `packEnterDrop` · `sealTornOffset` — 캔버스 **참조px**
- `shakeStrength` — `DOShakePosition`이 월드를 흔드는데 Overlay 캔버스의 월드는 **디바이스 스크린px**. 68은 1440폭 기기에서만 정확하다
- `tearDistance` — `Input.mousePosition` 기준 **raw 디바이스px**. `PackCardStack.flickThreshold`(scaleFactor 정규화 참조px)와 단위가 다르다 → 720폭 기기에선 뜯기만 상대적으로 어려워진다 (기존 편차)

### ⚠️ Overlay 캔버스에 ParticleSystem은 안 뜬다

`burstEffect`는 의도적으로 미배선이다. 붙이려면 Screen Space-Camera 캔버스나 UI 파티클 솔루션이 필요하다.

---

## 남은 작업

1. **Play 검증** — `PackStandaloneBoot`(`alternateDuplicates = true`)로 씬 단독 실행. 실제 스와이프가 필요해 스크립트로 대신할 수 없다.
   - 팩 위에서 시작한 드래그로 뜯기는지 (raycastTarget 검증)
   - 크림프 띠가 들려 올라가고, 되감기·자동완주가 되는지
   - 아이들 부유에 그림자가 따라 반응하는지 / 드래그 시작 시 멈추는지
   - 카드 6장 넘기기 → 요약 3열 2행 → 획득 버튼
   - 스킵 1회/2회 어느 경로로도 `AcquireButton`이 노출되는지
2. **고아 에셋 정리** — Play 검증 통과 후 `Assets/Assets/Prefabs/CardPack.prefab` + `Assets/Assets/Models/CardPack/`(메시 2 + 머티리얼 3) 삭제. 참조는 이 씬 하나뿐이었다.
3. **팩 이름 정합** — 아트에 "MOONLIT GRIMOIRE / 문릿 그리모어"가 박혀 있는데 씬이 참조하는 SO는 `NormalPack_TEST` / `StarterPack_1`이다. 팩 종류가 늘면 `CardPackData`별 아트 슬롯이 필요해진다.
4. **커밋 분리** — `Assets/Assets/Prefabs/UI/WinUI.prefab`에 이 작업과 무관한 변경(`Image`→`VictoryPanel` 리네임 + 빈 `Images` 노드)이 섞여 있다.
