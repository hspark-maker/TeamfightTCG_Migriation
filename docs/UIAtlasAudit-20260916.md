# UI 아틀라스·원격 등록 감사 (2026-09-16)

## 결과

현재 사용하는 UI의 **아틀라스 누락 6개를 보완**했다. 이 6개는 이미 `RemoteUI` 설정·프리팹의 의존 자산이므로 원격 등록 자체가 빠진 상태는 아니었다. 별도 Addressable 엔트리를 중복으로 만들지 않고 기존 아틀라스의 `packables`만 추가했다.

| 아틀라스 | 추가 이미지 | 실제 사용처 |
|---|---|---|
| UILobby | `EventMessage/Adventure_Map_Icon_512.png` | 콘텐츠 해금 소개 |
| UILobby | `EventMessage/Guide_Mission_Icon_512.png` | 콘텐츠 해금 소개 |
| UILobby | `EventMessage/Mission_Icon_512.png` | 콘텐츠 해금 소개 |
| UILobby | `EventMessage/Spin_Roulette_Icon_512.png` | 콘텐츠 해금 소개 |
| UILobby | `EventMessage/Strengthen_Icon_512.png` | 콘텐츠 해금 소개 |
| UICurrency | `Item_AccountExp.png` | 미션 행·가이드 미션·미션 보상·공용 보상 팝업 |

이미지 경로의 공통 접두사는 `Assets/Assets/Images/UI/`다. 해금 아이콘은 `ContentUnlockConfig.asset`의 `Sprite`가 `ContentUnlockIntroView`의 일반 `Image.sprite`에 전달된다. 이 5개는 512×512이며, 계정 경험치 이미지는 원본 1254×1254 / 기존 Importer 최대 512다. 모두 Single Sprite·Clamp이고 조사한 직렬화 경로에 Texture2D 직접 참조가 없다.

- 전체 아틀라스 **19개 유지**, packable 원본 **307 → 313개**.
- `UILobby` 27 → 32개, `UICurrency` 13 → 14개.
- EventMessage 폴더로 이동한 `Rank_Battle_Icon_v2`는 GUID가 유지돼 기존 `UILobby` 등록이 유효하다. 재등록하지 않았다.
- 모든 아틀라스 19개가 `RemoteUI` 그룹의 `Atlases/<이름>` 주소와 `RemoteUI` 라벨로 등록돼 있다.
- RemoteUI 스키마는 Remote Build/Load Path, HTTPS Hosting 주소, `Pack Separately`를 유지한다.

## 조사 범위와 원격 포함 판단

프로젝트의 UI·설정·씬·애니메이션·재질 등 자체 직렬화 파일 **536개**, `RemoteUI` 엔트리 **69개**의 GUID 참조를 정적으로 추적했다. 이 범위에서 Sprite 텍스처 **431개**를 발견했다. 아틀라스 파일의 에디터 캐시 참조는 사용처 집계에서 제외하고 `packables`만 패킹 의도로 읽었다.

직접 UI에서 참조하는 이미지뿐 아니라 `RemoteUI → 설정/프리팹 → 하위 자산` 경로도 조사했다. 일반 Sprite가 원격 프리팹이나 설정의 의존성으로 포함되면 개별 Addressable 엔트리가 없어도 누락은 아니다. 이번 6개와 유지한 모험 배경 4개는 이 경로가 있다. **아틀라스 미포함과 다운로드 누락을 구분해야 한다.**

모든 로컬 씬 자산을 원격화한 것은 아니다. 기동 UI·테스트 씬·사용되지 않는 벤더 샘플의 로컬 참조는 자동 원격 등록 대상으로 취급하지 않았다. `Resources.Load`, 코드에서 조합하는 경로, Unity가 무시하는 삭제된 직렬화 필드 등은 단순 GUID 추적으로 확정할 수 없으므로 이 감사 결과를 실제 빌드 의존성 결과와 동일하게 보지 않는다.

## 유지한 예외와 보류

아틀라스 밖의 Sprite 텍스처는 수정 후 **127개**다. 아래 수치는 기존 `UIAtlases.md`의 예외와 이번 정적 조사 범위를 합친 값이다. 127개를 모두 실사용 누락으로 해석하면 안 된다.

| 분류 | 개수 | 처리 이유 |
|---|---:|---|
| 카드 프레임·장식 | 29 | 큰 원본과 카드 표시 조합에 따른 별도 페이지 설계 대상. 이번 일반 UI 아틀라스에 일괄 편입하지 않음 |
| 이모트·애니메이션 시트 | 23 | 이미 시트로 묶인 리소스 계열. 추가로 잡힌 LongCat은 애니메이션 자체 참조만 있으며 RemoteUI 의존 경로 없음 |
| 벤더 샘플 프리팹 전용 | 24 | `_Vendor/.../Prefabs/`의 샘플 사용처만 확인, RemoteUI 의존 경로 없음 |
| 테스트 씬 전용 | 10 | 실제 배포 UI 참조와 구분 |
| 원본 크기·패딩 검토 필요 | 9 | 2048 아틀라스 패딩을 넣을 때 해상도 변경 가능성 때문에 기존 보류 유지 |
| 기동·로그인 UI | 9 | 원격 UI를 내려받기 전에 필요한 자산. 원격 아틀라스 의존을 만들지 않음 |
| 전체 화면·모험 배경 | 11 | 대형 배경을 일반 소형 UI 아틀라스와 분리. 새로 확인한 모험 배경 4개도 같은 원칙 적용 |
| Repeat UV | 3 | 반복 UV 의미 보존 |
| 프로필 아바타 | 3 | `ProfileAvatarMask`가 얼굴·마스크의 독립 0~1 UV를 요구함. 기존 원형 마스크 회귀 방지 |
| 전투 턴 배너 | 2 | 폭 2172의 SpriteRenderer 자산. 실제 Importer 크기·패딩·해상도 검토 후 결정 |
| RawImage·재질의 Texture2D 직접 사용 | 2 | 아틀라스 UV로 전환하면 원본 텍스처 의미가 달라짐 |
| 승리 파티클 색상 시트 | 1 | 이미 시트인 `victory_particle_color_atlas` 유지 |
| 삭제된 필드의 직렬화 잔재 | 1 | `PackOpenOverlay.disabledPlate`는 현재 코드 필드가 없고 이전 Unity 의존성 감사에서도 제외됐음 |

추가로 확인한 모험 배경은 `Ember_Volcano_BackGround`, `Frozen_Tundra_Background`, `MistForset_Background`, `background_01`이다. 모두 1249×1812 원본이며 `AdventureConfig.asset`을 통해 RemoteUI에서 도달한다.

`BackGroundPattern_01/02`, `OverlayBG`처럼 Texture2D로만 참조하는 자산은 Sprite 사용처 집계와 별개다. RawImage·재질의 원본 UV 의미를 보존한다. 카드 아트는 기존 전용 다운로드 경로, 팩 이미지·Glow 재질은 기존 팩/재질 의존 경로를 따르며 일반 UI 아틀라스로 일괄 이동하지 않았다.

## 검증과 한계

정적 검증 통과:

- 아틀라스 간 중복 GUID 0개, 존재하지 않는 packable GUID 0개.
- 19개 아틀라스의 RemoteUI 그룹·주소·라벨 정상.
- 6개 신규 이미지의 원본 파일·`.meta`를 현재 HEAD와 비교해 동일함을 확인. 크기·피벗·PPU·9-slice border·압축 설정 변경 없음.
- 두 아틀라스에서 `packables` 추가 외 변경 없음. 기존 packable 전부 보존.
- 이번에 추가한 6개는 모두 RemoteUI 의존 경로가 있으며 Texture2D 직접 참조 없음.

**Unity 실제 패킹·Sprite 바인딩·페이지 수·모바일 표시·빌드·업로드는 미실행이다.** 특히 기존 `UILobby`는 2페이지였으므로 5개 추가 후 페이지 수와 메모리를 실제 Pack Preview에서 확인해야 한다. 저장된 `m_PackedSprites` 등 캐시는 수동으로 조작하지 않았으며 Unity 재임포트·패킹 시 갱신된다. 정적 등록 검증을 이미 배포된 CDN의 최신 상태 검증으로 대신하지 않는다.

현재 저장소에 일반 UI 아틀라스 생성·검증 전용 에디터 도구는 발견하지 못했다. 기존 `UIAtlases.md`의 설정·패킹 기록과 `FirebaseResourceBuild.cs`의 원격 경로/공통 번들 검사를 확인했다. `VictoryBannerTestBuilder`의 전용 파티클 시트 생성은 별도 기능으로 구분했다. 과거 오류를 만든 카탈로그 재직렬화 스크립트는 실행하지 않았다.

## 로컬 재현 기록

`Build/RemoteAudio-20260916/`:

- `audit-ui-atlases.py`: GUID·UI 참조·아틀라스·RemoteUI 의존 경로의 읽기 전용 정적 조사.
- `ui-atlas-audit.before.json`, `ui-atlas-audit.json`: 변경 전후 전체 참조·예외 후보 목록.
- `verify-ui-atlas-audit.py`, `ui-atlas-verification.json`: 6개 추가와 원본·Importer 보존 검증.

감사 스크립트는 예외 비교를 위해 기존 `Build/UiAtlasDeployment-20260916/audit-static.json`을 읽는다. `Build/`는 로컬 기록이므로 다른 환경에서는 해당 이전 기록이 필요하다. 검증 스크립트의 HEAD 비교는 이번 변경을 커밋하기 전 기준이다.
