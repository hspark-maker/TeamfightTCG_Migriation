# 카드팩 찢김 연출 — 설정 · 아트 교체 가이드

> 대상 씬 `Assets/Scenes/CardPack.unity` · 최종 갱신 2026-07-29
> 이 문서는 **만지는 법**만 담는다. 왜 이렇게 만들었는지는 각 스크립트 헤더 주석에 있다.
> 이전 구성(`BodyClip`/`SealClip` 직사각형 마스크)을 기록한 [`PACK_2D_HANDOFF.md`](./PACK_2D_HANDOFF.md)는 폐기됐다.

---

## 0. 30초 요약

팩은 그림 한 장이 아니라 **앞뒤 두 껍데기 사이에 카드가 낀 통**이다.

```
UICanvas                       ← 형제 순서 = 그리기 순서
 ├ BG                          배경 아트 (shakeTarget)
 ├ Dim                         화면 어둠 (상시, 페이드 없음)
 ├ PackShadow                  바닥 그림자 (무대 밖 = 팩만 뜬다)
 ├ PackStage        scale 1    ← 등장·부유가 움직이는 무대
 │   ├ ShellBack    scale 1.55   Glow, PackBack        ← 팩 뒷면
 │   ├ CardHost     scale 1      StackAnchor + 카드들   ← 카드 (앞뒷면 사이!)
 │   └ ShellFront   scale 1.55   PackFront, MouthShadow,
 │                               SpecularClip>Specular,
 │                               LidPivot>PackLid, TearGlow
 ├ RevealPanel                 결과 UI + 입력판(StackInput)
 ├ SkipButton
 └ TearHint
```

찢김선은 마스크가 아니라 **셰이더가 픽셀 단위로 그린다**(`UI/PackTear`).

```
찢김선(u) = _TearY + 노이즈(u)      ← u마다 높이가 달라 종이 결이 생긴다
뜯긴 곳(u) = u < _TearProgress      ← 손가락이 지나간 왼쪽만 뜯긴다
```

앞면이 지워진 자리로 카드가 저절로 드러난다. **카드를 "등장"시키는 코드는 없다.**

흐름: `등장 → 찢기(가로로 그어) → 뽑기(뭉치째 솟아오름) → 넘기기 → 요약`

---

## 1. 팩 아트를 다른 이미지로 바꾸려면

### 1-1. 스프라이트 요구사항

| 조건 | 이유 |
|---|---|
| **배경 투명(알파 컷아웃)** | 팩 실루엣 밖은 알파 0이어야 빛·그늘이 새지 않는다 |
| **SpriteAtlas에 넣지 말 것** | 셰이더가 UV 0~1 전체를 팩으로 전제한다. 아틀라스에 들어가면 찢김선이 엉뚱한 높이에 생긴다 |
| Sprite Mode = Single, Mesh Type = **Full Rect** | Tight면 UV가 어긋난다 |

현재 아트: `Assets/Assets/Images/Cards/Packs/ChatGPT Image 2026년 7월 28일 오후 08_37_35.png` (1024×1536)

### 1-2. 갈아끼울 곳 — Image 5개

`PackStage` 아래 **다섯 Image가 같은 스프라이트를 공유**한다. 전부 바꿔야 한다.

| 오브젝트 | 재질 | 색 | 역할 |
|---|---|---|---|
| `ShellBack/PackBack` | `PackTear_Back` | `(0.14, 0.13, 0.18, 1)` | 봉지 뒷면(어두운 실루엣) |
| `ShellFront/PackFront` | `PackTear_Body` | 흰색 | 팩 앞면 |
| `ShellFront/MouthShadow` | `PackTear_Mouth` | `(0, 0, 0, 0.6)` | 카드 밑동 그늘 |
| `ShellFront/LidPivot/PackLid` | `PackTear_Lid` | 흰색 | 뜯겨 날아가는 조각 |
| `ShellFront/TearGlow` | `PackTear_Glow` | `(1, 0.94, 0.78, 0.9)` | 찢김선을 따라 새는 빛 |

**다섯의 `Width/Height`가 반드시 같아야 한다**(현재 `677 × 1015.5`). 하나라도 다르면 UV가 어긋나 조각과 구멍이 안 맞물린다.

### 1-3. 새 아트에 맞춰 찢김선 다시 잡기

봉인(크림프) 밴드 **바로 아래**가 찢을 자리다. 이미지에서 그 픽셀 높이 `y`(위에서부터)를 재고:

```
_TearY(앞면) = 1 − y / 텍스처높이
_TearY(뒷면) = 앞면 + 0.05      ← 이 차이가 곧 "봉지 안쪽 입구"의 깊이
```

현재 아트 기준: 크림프 밴드가 `y ≈ 195 / 1536` → 앞면 `0.873`, 뒷면 `0.925`.

- 앞면·조각·그늘·빛 → `PackTear_Body / Lid / Mouth / Glow` 넷 다 `_TearY` **같은 값**
- 뒷면만 `PackTear_Back`에서 더 높은 값

### 1-4. 종횡비가 다르면

`677 × 1015.5`는 현재 아트의 2:3 비율에 맞춘 값이다. 비율이 다르면 다섯 Image의 크기를 새 비율로 통일하고, `ShellBack`/`ShellFront`의 `scale`(현재 1.55)로 화면 크기를 맞춘다. `PackStage`의 scale은 **1로 둘 것**(카드 좌표계의 기준이다).

바닥 그림자(`PackShadow`)와 방사광(`Glow`)은 팩 크기에 맞춰 눈으로 조정.

---

## 2. 튜닝 노브

### 2-1. 재질 — `Assets/Assets/Materials/CardPack/`

| 프로퍼티 | 현재 | 크게 하면 |
|---|---|---|
| `_TearY` | 앞 0.873 / 뒤 0.925 | 찢김선이 위로 (0=아래, 1=위) |
| `_JagAmpA` | 0.028 | 굵은 결이 깊게 파인다 |
| `_JagAmpB` | 0.008 | 잔결이 거칠어진다 |
| `_JagFreqA` | 1 | **바꾸지 말 것** — 결 개수는 `jagSegments`로 조절 |
| `_JagFreqB` | 4.37 | 잔결이 촘촘해진다 (정수는 피할 것 — 굵은 결과 마디가 겹친다) |
| `_MouthDepth` | 0.03 (Mouth) | 카드 밑동 그늘이 위로 길어진다 |
| `_GlowWidth` | 0.010 (Glow) | 찢김선 빛이 두꺼워진다 |
| `_HeadWidth` | 0.05 (Glow) | 찢고 있는 선단의 밝은 점이 커진다 |
| `_FrontFeather` | 0.004 | 찢김 선단이 뭉개진다 |
| `_EdgeSoft` | 0.0015 | 찢김선 가장자리가 흐려진다 |

> ⚠ `_JagAmp*` · `_JagFreq*` 는 **다섯 재질이 전부 같은 값**이어야 한다. 다르면 뜯긴 조각과 구멍의 결이 어긋나 "뜯었다"가 즉시 깨진다.
> ⚠ `_TearProgress`는 인스펙터에서 만지지 말 것 — 런타임에 `PackTearSkin`이 덮어쓴다.

### 2-2. `PackTearSkin` (PackStage)

| 필드 | 현재 | 의미 |
|---|---|---|
| `jagSegments` | 14 | 팩 폭을 가로지르는 굵은 결의 개수. 작을수록 크게 뜯긴다 |
| `jagSeed` | 20260729 | 결 모양. 바꾸면 다른 모양으로 찢긴다(고정이라 매번 같다) |
| `peelAngle` | 26 | 조각이 들리는 각도(도). **작으면 조각이 제가 뚫은 입구를 도로 덮는다** |
| `peelLift` | 40 | 조각이 떠오르는 거리 |
| `flyOffset` | (−180, 420) | 조각이 날아가는 방향·거리 |
| `flySpin` / `flyDuration` | 55 / 0.45 | 비산 회전량 / 시간 |

### 2-3. `PackTearHandle` (PackStage) — 조작감

| 필드 | 현재 | 의미 |
|---|---|---|
| `tearScreenRatio` | 0.55 | 다 찢는 데 필요한 손가락 가로 이동량(**화면 너비 대비** — 기기 무관) |
| `commitThreshold` | 0.45 | 이만큼 넘긴 채 손을 떼면 자동 완주. 못 미치면 되감긴다 |
| `flickSpeed` | 1400 | 이 속도(px/s)로 튕기면 거리가 짧아도 완주 |
| `flickMinProgress` | 0.15 | 속도 완주의 최소 진행도(스치는 터치 방지) |
| `deadZone` | 12 | 이만큼(px) 움직여야 찢기 시작. 방향을 잠그는 기준 |
| `finishDuration` / `rewindDuration` | 0.22 / 0.28 | 자동 완주 / 되감기 시간 |

### 2-4. `PackRevealView` (PackOpenDirector) — 연출 타이밍

| 필드 | 현재 | 의미 |
|---|---|---|
| `packEnterDrop` / `packEnterDuration` | 811 / 0.45 | 팩이 아래에서 올라오는 거리 / 시간 |
| **`cardInPackCenter`** | (0, 219) | **팩 속 카드 뭉치의 중심.** y를 올리면 찢었을 때 카드가 더 많이 삐져나와 보인다 |
| `cardInPackScale` | 0.8 | 팩 속 카드 배율. 최종(1.0)과 갭이 크면 "뽑혔다"가 아니라 "커졌다"로 읽힌다 |
| `cardPullDelay` / `cardPullDuration` | 0.12 / 0.55 | 다 뜯긴 뒤 뜸 / 카드가 솟아오르는 시간 |
| `pullHold` | 0.35 | 뽑기 후 카드 조작을 열기까지의 여유 |
| `packLift` | 26 | 카드에 딸려 팩이 잠깐 들리는 거리 |
| `packSagSquash` | (0.97, 0.93) | 속을 비운 팩이 시드는 정도 |
| `packExitDelay` | 0.45 | 카드가 이만큼(뽑기 시간 대비) 나온 뒤 팩이 빠진다. 1에 가까우면 두 동작이 끊겨 보인다 |
| `packExitDrop` / `packExitTilt` / `packExitDuration` | 2400 / −9 / 0.5 | 팩 퇴장 거리 / 기울기 / 시간 |
| `shakeStrength` / `shakeDuration` | 48 / 0.3 | 개봉 순간 배경 흔들림(**디바이스 스크린px** — 참조px 아님) |

### 2-5. `PackShellRig` (PackStage) — 대기 중 몸짓

`floatDistance` 22 / `floatPeriod` 2.4 (부유) · `pulseScale` 1.03 / `pulsePeriod` 2.0 (호흡) · `shadowScaleAtTop` 0.82 / `shadowAlphaAtTop` 0.55 (뜰수록 그림자가 작고 옅게) · `glowAlphaMin/Max` 0.18/0.34 · `settleSpeed` 12 (손 닿으면 멈추는 속도).

> 두 주기(2.4 / 2.0)를 **서로 나누어떨어지지 않게** 둘 것 — 맞으면 반복 패턴이 눈에 읽힌다.

---

## 3. 깨뜨리면 안 되는 것

1. **형제 순서**: `BG → Dim → PackShadow → PackStage → RevealPanel → SkipButton → TearHint`
   `Dim`이 `PackStage` 뒤로 가면 팩 속 카드를 덮어버린다.
2. **`PackStage` 안 순서**: `ShellBack → CardHost → ShellFront`
   카드가 가운데 있어야 "팩 속"이 성립한다. 순서가 바뀌면 카드가 팩 위에 얹힌 스티커가 된다.
3. **팩 Image 5개는 `raycastTarget` 끌 것.** 켜져 있으면 팩 위에서 시작한 드래그가 막혀 개봉 자체가 안 된다.
4. **`RevealPanel`의 `CanvasGroup.blocksRaycasts`는 뽑기 전까지 꺼져 있어야 한다.** `StackInput`이 화면을 덮고 있어 찢기 드래그를 가로챈다(뷰가 자동 관리하니 손대지 말 것).
5. **`PackStage`의 scale은 1**, 배율은 껍데기(1.55)가 진다. 무대는 카드 좌표계의 기준이다.
6. **`PackTear_*.mat`을 다른 곳에서 재사용하지 말 것.** 런타임에 사본으로 갈리므로 원본은 안전하지만, 다른 화면이 같은 재질을 쓰면 찢김선을 공유하게 된다.

---

## 4. 파일 지도

| 파일 | 역할 |
|---|---|
| `Assets/Assets/Shaders/UI/PackTear.shader` | 찢김선 셰이더. `_TearMode` 0=몸통 1=조각 2=그늘 3=빛 |
| `Assets/Assets/Materials/CardPack/*.mat` | Body / Back / Lid / Mouth / Glow |
| `Assets/Scripts/UI/Shop/PackTearSkin.cs` | 진행도 하나를 다섯 재질에 물리고 조각을 들어 올린다 |
| `Assets/Scripts/UI/Shop/PackTearHandle.cs` | 가로로 그어 찢는 제스처. 진행도만 알린다 |
| `Assets/Scripts/UI/Shop/PackShellRig.cs` | 무대·앞뒤 껍데기·그림자의 몸짓 합성(단일 창구) |
| `Assets/Scripts/UI/Shop/PackRevealView.cs` | 스테이지 진행자(등장→찢기→뽑기→넘기기→요약) + 스킵 |
| `Assets/Scripts/UI/Shop/PackCardStack.cs` | 카드 더미 생성·팩 속 배치·솟아오름·한 장씩 밀어내기 |
| `Assets/Scripts/UI/Shop/PackSpecularSweep.cs` | 대기 중 표면 광택. 찢기 시작하면 멈춘다 |

> `PackIdleMotion.cs`는 **LobbyScene 상점 진열용**으로 남아 있다. CardPack 씬에서는 `PackShellRig`가 대체했다 — 이 씬에 다시 붙이지 말 것.
