# 미사용 프리팹 조사 — 2026-09-22

## 결과

- 전체 4,058개: 자체 관리 205개, 외부 패키지·벤더 3,853개.
- 자체 관리 미사용 후보 61개: 직접 참조 없는 60개 + 그 후보에서만 참조하는 TraitBG 1개.
- 테스트·아트 미리보기에서 사용하는 8개는 별도로 분리.
- 자체 관리 136개는 실행/동적 로드 루트에서 의존성이 확인되어 후보에서 제외.
- 프리팹·메타·코드·씬·설정 변경 및 삭제 없음. 보고서와 외부 후보 목록만 작성.

## 조사 기준과 한계

Assets 및 ProjectSettings의 GUID/AssetReference 참조를 수집했다. 활성 빌드 씬, Addressables 등록 자산(폴더 포함), Resources, 프로젝트 설정을 시작점으로 의존성을 추적했다. 자기 meta GUID는 참조 수에서 제외했다. 후보 파일명과 경로는 코드·도구 스크립트에서도 재검색했다.
Addressables 등록만 남은 자산도 보수적으로 보존하므로 실제 미사용이 더 있을 수 있다. Unity AssetDatabase.GetDependencies 및 플레이 검증은 하지 않은 정적 분석이다. 바이너리 조명 데이터 3개는 제외했다. 바이너리 내부 참조, 외부 도구와 향후 저작 목적까지 확정하지는 않는다.

## 동적 로드 보존 근거

- Assets/Scripts/Utils/UiPrefabCache.cs:116 — UIPrefab 라벨 조회; :212 — 컴포넌트 타입 등록.
- Assets/Scripts/UI/UIManager/UIPoolManager.cs:191 — 타입 기반 생성.
- Assets/Scripts/Core/GameSceneLoadOperation.cs:34,46 — LobbyScene/BattleScene/MultiplayerTestScene 원격 로드.
- Assets/Scripts/Utils/UiPrefabCache.cs:104 및 Assets/Scripts/Core/Initialization/RuntimeContentCache.cs:34 — UI/콘텐츠 카탈로그 로드.
- Assets/Scripts/Test/VfxSlot.cs:71 — 에디터 폴더 검색. 현재 테스트 씬의 대상은 _Vendor 및 PurchasedAssets.

## 일반 후보 7개

| 프리팹 | 근거 |
|---|---|
| `Assets/Assets/Models/MultiplayerLobbyPanel.prefab` | GUID 참조 0, Addressables/Resources 등록 없음, 실행 루트 연결 없음 |
| `Assets/Assets/Particle/Global/HitDust.prefab` | GUID 참조 0, Addressables/Resources 등록 없음, 실행 루트 연결 없음 |
| `Assets/Assets/Particle/Prefab/FX_UI_Shine.prefab` | GUID 참조 0, Addressables/Resources 등록 없음, 실행 루트 연결 없음 |
| `Assets/Assets/Prefabs/DeckSlot.prefab` | GUID 참조 0, Addressables/Resources 등록 없음, 실행 루트 연결 없음 |
| `Assets/Assets/Prefabs/FloatingSlot.prefab` | GUID 참조 0, Addressables/Resources 등록 없음, 실행 루트 연결 없음 |
| `Assets/Assets/Prefabs/UI/LobbyUI/DeckUI/DeckCard.prefab` | GUID 참조 0, Addressables/Resources 등록 없음, 실행 루트 연결 없음; 코드 3곳의 주석에만 이름이 남음. 현재 DeckStripCard에 원본 GUID 참조 없음 |
| `Assets/Assets/Prefabs/TraitBG.prefab` | 미사용 후보 FloatingSlot.prefab에서만 참조; 함께 정리 검토 |

## Old_VFX 미사용 후보 54개

모두 GUID 참조 0이며, Addressables/Resources 등록 및 실행 루트 연결이 발견되지 않았다.

- `Assets/Assets/Particle/Old_VFX/Attack/Hit01_fire.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/PeerlessSlash.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/SparkLoopYellow.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_ElectricBall01_Yellow.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Fireball05_Blue.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Fireball05_Green.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Fireball05_Orange.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Firefrost01_Blue.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Firefrost01_Green.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Firefrost01_Purple.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_IceSpike01_Blue.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Orb03_Red.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Orb04_Yellow.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Orb05_Purple.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Trail04_Purple.prefab`
- `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_WaterBall01_Blue.prefab`
- `Assets/Assets/Particle/Old_VFX/Execute/MagicCircleSimpleYellow.prefab`
- `Assets/Assets/Particle/Old_VFX/Execute/vfx_Muzzle_ElectricBall01_Green.prefab`
- `Assets/Assets/Particle/Old_VFX/Global/Glow.prefab`
- `Assets/Assets/Particle/Old_VFX/Global/HitDust.prefab`
- `Assets/Assets/Particle/Old_VFX/Heal/HealBig.prefab`
- `Assets/Assets/Particle/Old_VFX/Heal/Par_HealExplosion 1.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/NovaBlue.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/SharpExplosionBlue.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/SharpExplosionFire.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/SharpExplosionGreen.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/SharpExplosionPink.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/SpikyExplosionBlue.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/SpikyExplosionFire.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/SpikyExplosionGreen.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/SpikyExplosionPink.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/StarIntenseExplosionBlue.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/StarIntenseExplosionGreen.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/StarIntenseExplosionOrange.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/StarIntenseExplosionPink.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/StarlineExplosionBlue.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/StarlineExplosionGreen.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/StarlineExplosionOrange.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/StarlineExplosionPink.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/VFX_Arrow_Shot_Impact.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_ElectricBall01_Blue.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_ElectricBall01_Green.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_ElectricBall01_Yellow.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_IceSpike01_Blue.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_IceSpike01_Orange.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_IceSpike01_Teal.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_Orb05_Orange.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_Orb05_Purple.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_Slash01_Blue.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_Slash01_Orange.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_Slash01_Purple.prefab`
- `Assets/Assets/Particle/Old_VFX/Hit/vfx_Hit_WaterBall01_Blue.prefab`
- `Assets/Assets/Particle/Old_VFX/Synergy/FlowWave.prefab`
- `Assets/Assets/Particle/Old_VFX/Synergy/SwordWaveBlue.prefab`

## Old_VFX에서 보존할 3개

폴더 이름과 달리 현재 전투 설정에서 참조한다. 폴더 전체 삭제 대상이 아니다.

| 프리팹 | 실제 참조 |
|---|---|
| `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Orb03_Yellow.prefab` | `Assets/SO/BattleVfxLibrary.asset` |
| `Assets/Assets/Particle/Old_VFX/Attack/vfx_Projectile_Orb05_Orange.prefab` | `Assets/SO/Synergies/Vfx/BrandSynergyVfx.asset` |
| `Assets/Assets/Particle/Old_VFX/Cunning/FogHeavyGrey.prefab` | `Assets/SO/BattleVfxLibrary.asset` |

## 테스트·아트 미리보기용 8개

게임 진입 루트 연결은 발견되지 않았지만 아래 자산의 실제 참조가 있으므로 미사용 후보 61개에서 제외했다.

| 프리팹 | 참조 자산 |
|---|---|
| `Assets/ArtPrototypes/BalloonPengIsometric/BalloonPeng_Isometric.prefab` | `Assets/ArtPrototypes/BalloonPengIsometric/BalloonPeng_Preview.unity` |
| `Assets/ArtPrototypes/BalloonPengIsometric/BalloonPeng_Walk.prefab` | `Assets/ArtPrototypes/BalloonPengIsometric/BalloonPeng_Walk_Preview.unity` |
| `Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Cutout_Idle.prefab` | `Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Idle_Preview.unity` |
| `Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Cutout_Motions.prefab` | `Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Motions_Preview.unity` |
| `Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Cutout_Walk.prefab` | `Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Cutout_Idle.prefab`; `Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Cutout_Motions.prefab`; `Assets/ArtPrototypes/BalloonPengIsometric/Cutout/BalloonPeng_Cutout_Preview.unity` |
| `Assets/Assets/Particle/Old_VFX/Heal/vfx_Projectile_Trail01_Green.prefab` | `Assets/Scenes/TEST/TutorialSetup.unity` |
| `Assets/Assets/Particle/Prefab/FX_Common_ fragment.prefab` | `Assets/Scenes/TEST/AttackAnimScene.unity` |
| `Assets/Assets/Prefabs/UI/PlayerDeckPile.prefab` | `Assets/Scenes/TEST/AttackTestScene.unity` |

## 외부 패키지·벤더

런타임 연결 미확인 3,811개 중 직접 참조 0개는 1,313개다. 나머지는 데모·다른 외부 자산 등의 참조가 있다. 에디터 폴더 검색 대상도 있으므로 자체 후보와 같은 삭제 기준을 적용하지 않는다.
전체 경로와 직접 참조 수: `unused-prefabs-third-party-2026-09-22.txt`.

## 제외한 바이너리 자산

- `Assets/PurchasedAssets/Hovl Studio/Epic Toon VFX/Demo scene/LightingData.asset`
- `Assets/PurchasedAssets/GabrielAguiarProductions/Settings/UniqueProjectiles02_Demo_LightingData.asset`
- `Assets/PurchasedAssets/GabrielAguiarProductions/Settings/UniqueProjectiles02_Demo2D_LightingData.asset`
