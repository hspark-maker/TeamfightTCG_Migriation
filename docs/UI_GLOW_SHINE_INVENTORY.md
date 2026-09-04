# 빛·반짝임(Glow / Shine) 연출 인벤토리 — 아트 인계용

작성 시점: 2026-09-04. 조사 범위는 자체 코드 `Assets/Scripts/` 와 `Assets/Assets/Prefabs/UI/`, 씬 2개.
서드파티(`Photon/`, `AmplifyShaderEditor/`, `PurchasedAssets/`, `Plugins/`, `GUIPackCartoon/`)는 제외.

읽는 법
- **구동**: 그 노드를 켜고/끄고/움직이는 주체. 아트가 스프라이트만 바꾸면 되는지, 코드 수정이 필요한지 갈린다.
- **기법**: `Animator` = 애니메이션 클립(아트가 직접 만질 수 있음) / `DOTween` = 코드 트윈(파라미터 바꾸려면 코드 수정) / `동적 생성` = 텍스처를 코드가 런타임에 만듦(아트 에셋 없음).
- **확인 불가**: 이번 조사에서 배선을 특정하지 못한 항목. 작업 전 각 항목의 담당 뷰 스크립트에서 재확인 필요.

---

## 1. 프리팹별 노드

### `Assets/Assets/Prefabs/UI/VictoryBanner.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| LetterShine_V/I/C/T/O/R/Y (7개) | Image | **없음 — `ShineBandSprite` 가 런타임 Texture2D 생성** | on | `VictoryBannerView` — Animator 상태 + 알파 직접 대입 |
| ForegroundMedalStarShine | Image | `Assets/Assets/Sprites/CardPack/Glow_Radial.png` | on | Animator 클립 |
| ForegroundMedalStarShineMask | Image | `Assets/Assets/Sprites/CardPack/Glow_Radial.png` | on | Animator 클립 |
| SwapGlow | `UiGlowBlink` | — | on | `UiGlowBlink.Apply()` — UIEffect colorIntensity 삼각함수 호흡 |
| RearBurstLeft / RearBurstRight | `VictoryUiParticleGraphic` | ParticleSystem 입자를 UI 메시로 렌더 | on | `VictoryBannerView.PlayRearBurst()` |

### `Assets/Assets/Prefabs/UI/OverlayUI/RankPromoteOverlay.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| Bronze/Silver/Gold/Platinum/Diamond `_Glow` · `_shine` (총 10개) | Image | 확인 불가 | off | 등급별 상태 전환에서 알파·색 DOTween (정확한 메서드 확인 불가) |

### `Assets/Assets/Prefabs/UI/OverlayUI/RewardClaimPopup.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| Glow | Image | `Assets/Assets/Particle/_Vendor/Epic Toon FX/Textures/glow2.png` | off | `RewardRevealFx` (rayBurst) — DOTween 회전 + 확대 |
| GlowFill | Image | `Assets/Assets/Sprites/CardPack/Glow_Radial.png` | off | 동일 |

### `Assets/Assets/Prefabs/UI/OverlayUI/CardRewardOverlay.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| Glow | Image | `Assets/Assets/Particle/_Vendor/Epic Toon FX/Textures/glow2.png` | off | `CardRewardOverlay.OnReveal()` — DOTween 확대 + 호흡 |
| GlowFill | Image | `Assets/Assets/Sprites/CardPack/Glow_Radial.png` | off | 동일 |

### `Assets/Assets/Prefabs/UI/LobbyUI/PackUI/PackOpenOverlay.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| TearGlow | Image (전용 머티리얼 `Assets/Assets/Materials/CardPack/PackTear_Glow.mat`) | 확인 불가 | on | `PackShellRig` 찢김 연출 — 알파·변형 |
| Glow | Image | 확인 불가 | off | 확인 불가 |

### `Assets/Assets/Prefabs/UI/LobbyUI/PackUI/PackCard.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| Glow | Image | `Assets/Assets/Images/UI/Button_Bar_06.png` | off | `PackCardView.revealFlash` — DOFade |

### `Assets/Assets/Prefabs/UI/Battle/CardView/CardView.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| Glow | Image | 확인 불가 | off | `CardView` — DOTween 알파 또는 Animator (확정 못 함) |

### `Assets/Assets/Prefabs/UI/Battle/SynergyIcon.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| Glow | Image | `Assets/Assets/Sprites/CardPack/Glow_Radial.png` | off | `SynergyIconView.SetGlow()` — 알파 직접 대입 |

### `Assets/Assets/Prefabs/UI/MatchUI/MatchDeckPanel.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| BgGlow | Image | `Assets/Assets/Images/UI/Battle_My_Bar.psd` | off | 확인 불가 |

### `Assets/Assets/Prefabs/UI/LobbyUI/CardDetailUI/CardDetailOverlay.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| CardBackGlow | Image | 확인 불가 | off | 강화 연출 중 `CardEnhanceShading` 의 Gleam 트윈 |

### `Assets/Assets/Prefabs/UI/LobbyUI/DeckUI/DeckEditSlot.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| SwapGlow | `UiGlowBlink` | — | on | `UiGlowBlink.Apply()` — UIEffect colorIntensity 호흡 |

### `Assets/Assets/Prefabs/UI/LobbyUI/Adventure/AdventureNode.prefab`
| 노드 | 컴포넌트 | 스프라이트 | 초기활성 | 구동 / 기법 |
|---|---|---|---|---|
| Medallion | `UiGlowBlink` | — | off | `AdventureNodeView` — medallionBlink(호흡) + finalGlow(페이드) |

### 씬
- `Assets/Scenes/BattleScene.unity` — `Glow` 노드 있음 (배선 미확인)
- `Assets/Scenes/TEST/AttackAnimScene.unity` — `Glow` 노드 있음 (테스트 씬)

---

## 2. 코드로 파티클을 흉내내는 UI 연출

ParticleSystem 을 안 쓰고 UI 오브젝트 + DOTween 으로 입자·파편·빛줄기를 만든다.
**프리팹에 저작되어 있지 않다 — 전부 코드가 런타임에 조각을 만든다.** 아트가 프리팹에서 찾아도 안 나오는 이유.

| 스크립트 | 만드는 그림 | 진입점 |
|---|---|---|
| `UI/Common/UiGainBurst.cs` | 흩어졌다 목표로 수렴하는 입자 궤적 | static `Build()` |
| `UI/Common/UiConfettiBurst.cs` | 분출 후 낙하하는 색종이 | static `Build()` |
| `UI/Common/UiLightStreak.cs` | 꼬리 달린 빛줄기 비행 | static `Build()` |
| `UI/Common/UiCrumble.cs` | UI 를 조각내 무너뜨림 | static |
| `UI/Common/UiPunch.cs` | 스케일 튀김 강조 | static `Play()` |
| `UI/Common/CardGainFlightEffect.cs` | 카드 부채꼴 펼침→수렴 (`UiGainBurst` 기반) | `LobbyCanvas.prefab` / GainEffectLayer 에 컴포넌트로 붙음 |
| `UI/Common/RewardRevealFx.cs` | 빛 회전 + 파편 분출 (`UiConfettiBurst` 기반) | 뷰의 `[Serializable]` 필드 |
| `UI/Growth/CardEnhanceHalo.cs` | 강화 후광 (알파·스케일) | `CardEnhanceRitualView` 필드 |
| `UI/Growth/CardEnhanceShading.cs` | 강화 다축 연출 (Heat/Shake/Grey/Blind/Cover/Snuff/Gleam) | `CardEnhanceRitualView` 필드 |
| `Battle/DropAndShineEmblem.cs` | 엠블럼 낙하 + 반짝임 띠 + 페이드 | `SynergyEmblemVfx` spec 자식 타입 |
| `Utils/ShineBandSprite.cs` | **반짝임 띠 텍스처를 코드가 Texture2D 로 생성** | static `Get()` |

---

## 3. 아트 작업 시 주의

1. `ShineBandSprite` 는 아트 에셋이 아니라 코드가 만든 텍스처다. VictoryBanner 의 LetterShine 7개가 이걸 쓴다 — 스프라이트를 교체하려면 코드 수정이 필요하다.
2. `Glow_Radial.png` 한 장을 4곳(RewardClaimPopup · CardRewardOverlay · SynergyIcon · VictoryBanner 메달)이 공유한다. 고치면 네 화면이 같이 바뀐다.
3. `RewardClaimPopup` · `CardRewardOverlay` 의 Glow 는 서드파티 `Epic Toon FX` 텍스처(`glow2.png`)를 그대로 쓴다. 자체 에셋으로 교체할 후보.
4. `MatchDeckPanel` 의 BgGlow 는 스프라이트가 `Battle_My_Bar.psd` 다 — 이름과 용도가 안 맞는다(바 이미지 재활용). 전용 에셋 필요 여부 확인.
5. DOTween 기법인 항목은 타이밍·세기를 아트가 인스펙터에서 못 바꾼다. 조정이 필요하면 코드 담당과 함께.
6. "확인 불가" 표기 항목(RankPromoteOverlay 10개, PackOpenOverlay Glow, CardView Glow, CardDetailOverlay CardBackGlow, MatchDeckPanel BgGlow 구동)은 배선을 특정하지 못했다. 해당 화면 작업 전 재조사 필요.
