# UI 이미지 아틀라스

## 프로필 원형 마스크 회귀 수정 (2026-09-15)

아래 추가 등록 중 아바타 사진 3개는 `UIProfile`에서 다시 제외했다. `ProfileAvatarMask.shader`는 얼굴과 마스크에 같은 UV를 사용하므로 **얼굴·판 모두 단독 텍스처**여야 한다. 마스크 판의 Texture2D 직접 참조만 검사하면 얼굴 아틀라스 등록으로 생기는 회귀를 놓친다.

- `UIProfile`에는 테두리 6개만 남긴다. 현재 전체 아틀라스 19개 / 원본 304개 / 이번 순증 112개다.
- 아바타 3개의 UV 0~1 및 GPU 렌더 결과를 검사했다. 중앙 알파 1, 네 모서리 알파 0으로 원형 잘림을 확인했다. 결과 PNG는 `Build/ProfileMaskFix-20260915/`에 있다.
- 프로필 아바타와 마스크 판은 향후 자동 등록 대상에서도 제외한다. 현재 셰이더를 아틀라스 UV에 대응시키기 전에는 이 제외를 유지한다.

## 추가 점검·등록 (2026-09-15)

새 UI와 RemoteUI 의존성을 다시 점검해 소형 UI 이미지 115개를 추가했다. 현재 UI 아틀라스 18개와 CardIcons 1개에 원본 307개가 등록돼 있다. 아래 2026-09-14 표는 최초 구성 기록이다.

- 새 필터 UI의 `Button_Round06_White`, `Popup02~09_Topber_White_Bg`, `InputField_Frame` 누락을 보완했다. 덱 검색·필터 아이콘, 프로필·덱 이미지와 기존 UI 소형 이미지도 포함한다.
- 프로필 9개는 `UIProfile.spriteatlas`를 만들고 `RemoteUI` 그룹의 `Atlases/UIProfile` 주소·`RemoteUI` 라벨로 등록했다. 나머지는 기존 화면별 아틀라스에 추가했다.
- Android 실제 패킹 및 스프라이트 바인딩 307개 통과. 전부 ASTC 6×6이며 UILobby만 2048×2048 + 1024×1024의 2페이지, 나머지는 1페이지다. UILobby 추가분의 별도 묶음은 페이지 하나가 더 필요하므로 현재 2페이지를 유지했다.
- GUID 중복·신규 누락 0개. 원본 메타 115개의 크기·피벗·PPU·border·sprite rect·알파·mipmap·wrap 보존을 검증했다. 압축만 109개에서 해제했고 6개는 이미 비압축이었다.
- 기동·로그인 UI, RawImage·재질의 직접 텍스처 참조, 배경·기존 애니메이션 시트 등 69개는 제외했다. 대형 카드 프레임·장식 29개는 해상도·페이지 구성의 별도 검토 대상으로 남겼다.
- 상세 후보·제외 사유: `Build/UiAtlasDeployment-20260915/audit.json`. 패킹: `packing-android.tsv`, 원본 검수: `metadata-validation.json`. 같은 폴더의 `build-summary.json`에 빌드 결과를 기록했다.

현재 RemoteUI는 `Pack Separately`이며, 아래 최초 구성의 `Pack Together` 설명보다 `FirebaseResourceHosting.md`의 최신 배포 설정을 따른다.

## 최초 구성 (2026-09-14)

2026-09-14 기준. `Assets/Assets/Atlases/`에 UI 아틀라스 17개, 원본 이미지 161개를 등록했다.
기존 `CardIcons.spriteatlas`의 키워드·시너지 31개는 별도로 유지한다.

## 구성과 패킹 결과

Unity Editor에서 Android 대상으로 실제 패킹한 결과다. 각 아틀라스는 1페이지이며 모두 ASTC 6×6이다.

| 아틀라스 | 이미지 수 | 패킹 크기 |
|---|---:|---|
| UISharedButtons | 13 | 2048×2048 |
| UISharedFrames | 24 | 1024×2048 |
| UICurrency | 8 | 1024×2048 |
| UIRewards | 7 | 2048×1024 |
| UILobby | 18 | 2048×2048 |
| UIDeck | 6 | 1024×1024 |
| UIAlbum | 6 | 1024×1024 |
| UIBattle | 9 | 2048×2048 |
| UIAdventure | 2 | 2048×1024 |
| UIShop | 6 | 512×2048 |
| UIMission | 4 | 1024×1024 |
| UIMatch | 8 | 2048×1024 |
| UIPass | 15 | 2048×1024 |
| UIDetail | 3 | 512×1024 |
| UIRoulette | 6 | 2048×2048 |
| UIRank | 10 | 1024×512 |
| UIVictory | 16 | 2048×2048 |

## 설정과 추가 방법

- 최대 크기 2048, Padding 4. Rotation·Tight Packing·Mip Maps·Read/Write는 끈다. Bilinear·sRGB·Include in Build는 켠다.
- Android와 iOS 플랫폼 압축은 ASTC 6×6. 실제 패킹 검증 대상은 Android다.
- 원본은 압축을 해제하고 아틀라스에서 최종 압축한다. 기존 원본 Max Size는 유지한다.
- 원본 파일과 `.meta`를 유지한다. 프리팹의 Sprite 참조는 그대로 사용한다.
- 새 이미지는 함께 표시되는 화면의 아틀라스 `Objects for Packing`에 개별 추가한다. 여러 화면에서 쓰면 공용 묶음을 검토한다. 폴더 전체 자동 등록은 사용하지 않는다.
- UI 아틀라스 17개와 CardIcons는 Addressables의 `RemoteUI` 그룹에 `Atlases/<아틀라스 이름>` 주소로 등록하고 `RemoteUI` 라벨을 붙인다. UI와 같은 원격 `Pack Together` 그룹을 사용한다. 새 아틀라스를 만들면 이 등록도 추가한다.
- 한 이미지는 한 아틀라스에만 등록한다. 이미지를 수정하면 Unity가 다시 패킹하므로 UI 완성 전에도 계속 작업할 수 있다.
- 추가 후 Pack Preview에서 페이지 수·크기·화질을 확인한다. 페이지가 늘면 사용 화면별 분리를 먼저 검토한다.

## 제외 대상

UI 이미지 201개 중 40개는 이번 패킹에서 제외했다. 현재 직렬화 참조가 없는 이미지, 전체 화면 배경, Repeat UV 텍스처, Texture2D를 직접 사용하는 재질, 기존 애니메이션·파티클 시트, 2048 패킹 시 여백 때문에 해상도 변경이 필요한 큰 원본이 해당한다.

`BackGroundPattern_01/02`는 RawImage의 반복 UV를 사용한다. `OverlayBG`는 재질에서 Texture2D를 직접 읽는다. 이들은 일반 UI Sprite와 함께 패킹하지 않는다. `Loading_Penguins`와 `victory_particle_color_atlas`는 이미 한 장에 여러 프레임·색상을 모은 시트라 유지한다.

## 검증

- 161개 스프라이트 모두 해당 아틀라스에 바인딩 가능함을 Unity API로 확인.
- 패킹 전후 크기·피벗·Pixels Per Unit·9-slice border 동일, 회전 없음.
- 원본 메타데이터 161개를 백업과 비교. 압축 관련 값 외 변경 없음. 실제 변경된 원본 Importer는 114개이며 나머지는 이미 비압축이었다.
- 기존 CardIcons를 포함한 192개 패킹 원본 GUID 중복 없음.
- 최초 등록 시 Android Addressables 빌드 보고서에서 아틀라스 18개가 동일한 UI 번들에 명시적으로 포함된 것을 확인했다. 이후 해당 UI 그룹을 원격으로 전환했다. 앱 내장 리소스와의 중복 여부는 앱 빌드로 별도 확인해야 한다.
- 실제 플레이 화면의 화질·드로콜·기기 메모리 측정과 앱 빌드는 아직 실행하지 않았다. 아틀라스 제작만으로 FPS 개선 수치를 확정하지 않는다.

## 리소스 빌드

`Tools > Addressables > Build Firebase Resources`로 리소스를 빌드한다. 아틀라스는 원격 UI 번들에 들어간다. 이번 로컬→원격 전환은 새 앱 빌드가 필요하다. 기존 로컬 UI 앱에 Firebase 업로드만으로 이 전환을 적용할 수 없다. 전환 후 UI 리소스 패치는 출시 상태 파일을 사용하는 콘텐츠 업데이트 절차를 따른다.

Android에는 `ENABLE_JSON_CATALOG` 컴파일 심볼을 설정했다. Addressables의 `EnableJsonCatalog` 값만 켜고 심볼이 없으면 바이너리 카탈로그가 생성되므로, 빌드 도구가 이 불일치를 빌드 전에 차단한다. 다른 플랫폼에서도 JSON 카탈로그를 사용하려면 해당 플랫폼에 같은 심볼을 설정하고 컴파일을 마친다.

배포용 Android 빌드 보고서: `Library/com.unity.addressables/BuildReports/buildlayout_2026.09.14.17.07.21.json`. 아틀라스 18개를 포함한 RemoteUI 항목 48개가 HTTPS 주소의 UI 번들 한 개(27,039,681 bytes)에 포함된다. 번들 간 중복 자산 0개, 빌드 오류 없음(기존 코드의 컴파일 경고는 있음). Firebase Hosting 업로드와 실제 CDN 다운로드 해시 검증까지 완료했다. 앱 빌드·실기기 검증은 미실행이다. 상세 기록은 `FirebaseResourceHosting.md`를 따른다.

원본 메타데이터 백업: `Build/UiAtlasBackups/20260914-161247/`.
