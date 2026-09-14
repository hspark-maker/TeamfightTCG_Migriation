# Android APK 용량 점검 — 2026-09-14

## 결과

현재 APK는 142,416,636 bytes, **135.82 MiB**다. 1차 감량 목표는 **95~110 MiB**, 시작 화면 이후 자산의 외부 로딩까지 정리하는 2차 목표는 **75~90 MiB**로 추정한다. 압축 비교 외 감소량은 새 APK 빌드로 검증하지 않은 작업 목표다. 각 항목의 효과는 겹치므로 단순 합산하지 않는다.

측정 대상은 `C:/Users/cookapps/Desktop/Builds/26.09.14.apk`다. Unity 최신 빌드 보고서의 출력 경로·시작 시각(2026-09-14 08:21:50 UTC)과 일치한다. 저장소 `Builds/TeamfightTCG_Migriation_live.apk`는 9월 2일의 오래된 파일이므로 분석 대상으로 쓰지 않았다.

이번 점검에서는 프로젝트 설정·리소스·코드를 변경하지 않았고 APK 재빌드·배포도 하지 않았다. 분석 결과만 `.codex-temp/`에 기록했다.

## 실제 APK 내부 구성

ZIP 엔트리의 compressed size 합계다. APK 서명·정렬·ZIP 디렉터리 때문에 전체 파일 크기와 작은 차이가 있다.

| 항목 | APK에서 차지하는 크기 |
|---|---:|
| Unity 데이터 전체 | 83.54 MiB |
| 네이티브 라이브러리 | 43.22 MiB |
| Java/Android DEX | 7.28 MiB |
| StreamingAssets | 0.57 MiB |
| 기타 | 1.13 MiB |

주요 파일:

- `data.unity3d`: 65.23 MiB
- `libil2cpp.so`: 31.24 MiB
- `libunity.so`: 10.16 MiB
- `sharedassets2.resource`: 9.60 MiB
- `global-metadata.dat`: 4.74 MiB
- `sharedassets1.resource`: 2.77 MiB

CardArt·RemoteUI 원격 번들이 APK에 통째로 들어간 것은 아니다. APK에 들어간 Addressables 번들은 unifiedraytracing와 작은 Unity 기본 자산 번들뿐이다. 다만 원격 자산과 같은 원본의 로컬 사본은 씬·Resources 직접 참조를 통해 들어간다.

## 감량 후보와 근거

### 1. LZ4 → LZ4HC

실제 빌드 옵션은 `CompressWithLz4`다. APK의 `data.unity3d`를 읽어 원본 UnityFS 블록을 해제하고 같은 블록 단위로 LZ4 HC level 12 압축을 비교했다.

- 원본 압축 데이터 블록 합계: 68,392,580 bytes
- 비교 압축 데이터 블록 합계: 60,028,891 bytes
- 차이: **8,363,689 bytes = 7.98 MiB**
- 나머지 파일이 같다고 가정하면 APK 약 **127.84 MiB**에 해당한다.

Unity 재빌드가 아니라 별도 LZ4 라이브러리를 이용한 압축 실험이다. Unity의 실제 LZ4HC 설정·블록 구성에 따라 결과는 달라질 수 있다. 분석 원본과 APK는 변경하지 않았다.

### 2. 카드 프레임·이모티콘·이펙트 텍스처 압축

빌드 보고서의 원본 자산별 packed size 합계는 다음과 같다. **APK 최종 압축 크기가 아니므로 그대로 절감량으로 사용할 수 없다.**

| 분류 | 외부 압축 전 packed size |
|---|---:|
| 카드 프레임 폴더 | 40.10 MiB |
| 이모티콘 | 18.95 MiB |
| 파티클 자산 | 25.27 MiB |

직접 확인한 `Frame_UR.png`는 770×1024 RGBA32, `Image_Emote_tired.png`는 1024×512 RGBA32다. 두 파일 모두 Android override가 없고 기본 Texture Compression이 Uncompressed여서 Android에도 RGBA32로 들어간다. 대상별 ASTC·최대 크기를 검토할 가치가 크다. 파티클 분류에는 텍스처 외 메시·프리팹 등도 포함되므로 전체를 텍스처 압축 대상으로 취급하지 않는다.

화질·알파 경계·이펙트 변화 확인이 필요하다. 먼저 1~2개 샘플을 비교한 뒤 일괄 범위를 정한다.

### 3. 로컬 폰트와 Resources 참조 정리

폰트 분류 packed size 합계는 **41.06 MiB**다. 이 중 `MalgunGothic.ttf`는 `resources.assets`에 **13,461,102 bytes(12.84 MiB)** 들어간다.

- `Assets/Resources/Fonts/MalgunGothic_TMP.asset`가 해당 원본 폰트를 직접 참조한다.
- `Assets/Fonts/UI/MalgunGothic_TMP.asset`도 같은 원본을 참조하며, 두 TMP 자산 모두 Dynamic 모드다.
- 로비·전투 씬 의존성에도 맑은 고딕 원본이 있다. Resources 파일 하나만 옮겨서 완전히 빠지는 구조는 아니다.
- Jalnan SDF 3종과 ONE Mobile POP SDF는 각각 약 4 MiB가 포함된다. 이름만 보고 중복 삭제하지 않고 실제 글리프·머티리얼·참조를 확인해야 한다.

시작·로그인·다운로드 실패 안내에 필요한 폰트는 로컬에 유지하고, 그 이후 UI 폰트의 직접 참조를 원격 로딩으로 정리하는 방향이 적절하다. 한글 닉네임 등 동적 글리프에 필요한 원본 폰트를 무작정 제거하면 안 된다.

### 4. 원격 UI와 로컬 씬 사이 중복

APK 빌드 보고서에 UISharedButtons·UIVictory·UILobby·UIBattle·CardIcons 등 아틀라스 텍스처가 나타나며, 아틀라스 분류 합계는 **15.18 MiB packed size**다.

Addressables 빌드 보고서와 APK 보고서의 원본 경로 교집합은 314개, APK 측 packed size 약 52.09 MiB다. 이는 중복 조사 대상의 상한 지표이며, 엔진/스크립트 경로·메타데이터·필수 시작 화면 자산도 포함하므로 그대로 제거 가능 용량이 아니다. 생성된 아틀라스 텍스처는 별도 경로이므로 이 숫자와 아틀라스 크기를 단순 합산하지 않는다.

`StartScene`에서 `CardFrameConfig`와 이모티콘에 도달하는 의존성이 확인된다. `OutgameConfigStep`의 SO 직접 참조, 로비·전투 씬의 UI 직접 참조를 다운로드 이후 주소 기반 초기화로 분리해야 효과가 있다. 아틀라스 Include in Build 체크만 일괄 해제하는 방식은 사용하지 않는다.

### 5. 코드·SDK

현재 설정은 ARM64 단독, IL2CPP, Release, Development=false, Engine Code Stripping=true다. 따라서 ABI 중복 제거·개발 빌드 해제로 얻을 큰 추가 감소는 없다.

남은 후보:

- Managed Stripping Level: **Minimal** → Medium부터 검증
- IL2CPP Code Generation: **OptimizeSpeed** → OptimizeSize 비교
- Android Release Minify: **꺼짐** → R8 검증

네이티브 코드와 DEX, IL2CPP 메타데이터만 현재 약 **55.2 MiB**다. 50 MiB 이하 목표는 리소스 외부화만으로 달성할 수 없으며 코드·SDK 구성까지 추가로 줄여야 한다. 스트리핑 변경은 Firebase·JSON/리플렉션·원격 UI 생성·로그인·세이브 경로의 실기기 검증이 필요하다.

### 6. 기타

- `sharedassets2.resource`의 9.60 MiB 중 약 8.52 MiB는 빌드 보고서에서 원본 경로가 비어 있다. 오디오·영상으로 단정하지 않고 별도 매핑이 필요하다.
- MultiplayerTestScene이 빌드에 포함되지만 다른 씬과 공유하지 않는 확인 가능한 자산은 약 5 KiB, 씬 자체도 작다. 정리 대상이지만 대규모 감량 근거는 아니다.
- `SpecData.bytes`는 작은 필수 산출물이므로 이번 감량 대상에서 제외한다. 수정·생성하지 않는다.

## 권장 진행 순서와 목표

1. LZ4HC 비교 빌드, 카드 프레임·이모티콘 Android 압축, 폰트 참조 정리, Medium 스트리핑 검증: **95~110 MiB** 목표.
2. 시작 이후 UI·설정 SO·이펙트·오디오의 로컬 의존성 해소 및 외부 로딩: **75~90 MiB** 목표.

예상 범위이며 단계별 실제 APK 크기로 갱신해야 한다. 외부화는 최초 설치 APK 크기를 줄이지만 앱 실행 후 다운로드와 CDN 전송량으로 이동하는 것이다. AAB의 기기별 다운로드 크기는 이 APK 크기와 별도로 측정한다.

## 측정 자료

- `.codex-temp/apk-size-analysis.json`: APK 엔트리와 자산별 크기
- `.codex-temp/apk-packed-assets.tsv`: 최신 Unity BuildReport 원본 경로·packed size
- `.codex-temp/apk-scene-dependencies.tsv`: 활성 빌드 씬 의존성
- `.codex-temp/apk-addressable-overlap.json`: Addressables와 로컬 자산의 경로 교집합
- `.codex-temp/apk-compression-estimate.json`: 데이터 블록 압축 비교
- `.codex-temp/measure-apk-compression.py`: 압축 실험 스크립트

공식 근거: [Unity LZ4/LZ4HC 빌드 압축](https://docs.unity.cn/6000.3/Documentation/ScriptReference/BuildOptions.CompressWithLz4.html), [씬 직접 참조와 Addressables 로컬 사본](https://docs.unity.cn/Packages/com.unity.addressables%401.19/manual/LoadingAddressableAssets.html).

## 1차 적용 내역

사용자의 1차 적용 요청으로 다음 변경을 적용했다. 이후 사용자가 빌드는 나중에 하기로 지정하여 APK 빌드·배포·최종 용량 측정은 보류했다.

- 무압축 카드 프레임 12개: Android override ASTC 4×4, 고품질. 기존 해상도 유지.
- 무압축 이모티콘 5개: Android override ASTC 6×6, 고품질. 기존 해상도 유지.
- 변경된 17개 메타 파일에서 Android 플랫폼 블록 외 내용이 백업과 동일함을 확인했다. 스프라이트 분할·피벗·다른 플랫폼 설정은 보존했다.
- Android Managed Stripping Level: Medium.
- Android IL2CPP Code Generation: OptimizeSize.
- 현재 PC의 Android Build Profile Compression Method: LZ4HC. `Library/BuildProfiles`의 로컬 설정이므로 다른 PC에서는 별도 확인해야 한다.
- 미참조 `Assets/Resources/Fonts/MalgunGothic_TMP.asset`와 meta는 Assets 밖 백업으로 옮겼다. 사용하는 `Assets/Fonts/UI/MalgunGothic_TMP.asset`와 동적 원본은 유지했다. Resources 사본 제거만으로 맑은 고딕 원본 용량이 줄어드는 것은 아니다.
- Jalnan 폰트 변형은 외곽선·패딩 차이가 확인되어 유지했다. 한글·닉네임 동적 글리프도 유지했다.
- 리소스 빌드 시 발견한 `RankPromoteOverlay`의 제거된 필드에 남아 있던 중복 Tooltip 하나를 정리했다.

백업: `Build/ApkPhase1Backups/20260914/`. 텍스처 적용 내역: `.codex-temp/apk-phase1-textures.tsv`. `SpecData.bytes` SHA-256은 기존과 동일하다.

보류 지시 전 리소스 빌드를 두 차례 시도했으나 동시 변경 중인 UI 스크립트의 컴파일 오류로 실패했다. 성공한 신규 리소스 배포나 신규 APK는 없다. 다음 작업은 스크립트 컴파일 확인 → 리소스 빌드·배포 → 동일 버전 APK 빌드 순서다. 새 APK에서는 Medium 스트리핑의 로그인·세이브·원격 UI 생성과 텍스처 표시를 실기기에서 확인해야 한다. 이번 점검 시 ADB 기기는 offline 상태였다.

## 2차 적용 및 병합 후 배포 — 2026-09-14

- 시작 씬의 게임 설정 SO 직접 참조 14개를 `RuntimeContentCatalog`로 옮겼다. 로그인·스펙 동기화 이후 원격 리소스를 내려받고 설정을 적재한 뒤 기존 초기화를 진행한다.
- LobbyScene·BattleScene·MultiplayerTestScene을 `RemoteScenes` 그룹으로 옮겼다. 플레이어 내장 씬은 StartScene 하나이며, 씬 전환은 Addressables 활성화 완료를 기다린다. 다운로드·씬 로드 실패 시 재시도 경로를 유지했다.
- `RemoteUI`는 63개 엔트리, `RemoteScenes`는 3개 엔트리다. 원격 씬으로 옮긴 뒤에도 빌드 전 Live 데이터 검증이 해당 씬 의존성을 검사한다.
- 시작 씬 의존성은 적용 전 528개에서 88개로 감소했다. 의존성 개수나 자산 packed size 감소는 APK 압축 용량 감소와 같지 않다.
- 사용자 병합 완료 후 Android Addressables 빌드 성공. `catalog_2026.09.14.09.35.06.json/hash`를 Firebase Hosting에 배포했다. 이전 카탈로그와 번들은 보존했다.
- 배포 검증: 180개 파일 HTTPS 응답·CORS·캐시 헤더 정상. 카탈로그·대표 번들 17개와 게임 씬 번들 3개의 실제 다운로드 크기·SHA-256이 로컬과 일치했다. 보고서: `Build/ResourceDeploymentChecks/1789378633960.json`.
- APK 빌드 중 Addressables 자동 재빌드는 비활성화했다. APK와 업로드 카탈로그가 달라져 발생했던 404 재발을 방지한다.
- 병합 후 `SpecData.bytes` 기준 SHA-256: `DEA280C6E807D819F85BE007058E444D94CE560F7B09B9321114FB1722668EDA`. 이번 빌드·배포에서 산출물을 재생성하지 않았다.

### 실제 APK 결과

Android ARM64 / IL2CPP / Release / Medium stripping / OptimizeSize / LZ4HC로 빌드 성공했다. 오류 0개, 경고 18개, 소요 시간 2분 8초다.

| 항목 | 이전 APK | 최적화 APK |
| --- | ---: | ---: |
| 실제 파일 크기 | 142,416,636 bytes (135.82 MiB) | 53,242,091 bytes (50.78 MiB) |
| data.unity3d 압축 크기 | 65.23 MiB | 9.90 MiB |
| libil2cpp.so ZIP 압축 크기 | 31.24 MiB | 15.10 MiB |

총 85.04 MiB, **62.6% 감소**했다. 리소스 외부화와 압축·스트리핑을 함께 적용한 결과이며 각 변경의 독립적인 감소량은 측정하지 않았다. 최초 실행 시 원격 다운로드 용량은 별도다.

- APK: `C:/Users/cookapps/Desktop/Builds/26.09.14-optimized.apk`.
- APK 내 `assets/aa/settings.json`이 배포 완료된 `catalog_2026.09.14.09.35.06.hash`를 참조함을 확인했다.
- 측정 원본: `.codex-temp/apk-optimized-measurement.json`. 빌드 결과: `.codex-temp/apk-build-20260914-merged.txt`.
- Samsung SM-S936N / Android 16 기기에 `adb install -r` 성공. 패키지 `com.BurgerMonster.CardBattlee` 실행 및 Unity Release/IL2CPP 시작 로그 확인.
- 기기가 보안 잠금·화면 꺼짐 상태로 앱을 일시 정지했다. 로비 진입·로그인·원격 UI·전투의 실기기 검증은 잠금 해제 후 필요하다. 현재 시작 로그만으로 기능 검증 완료로 판정하지 않는다.
- 남은 빌드 경고에는 Android 입력 설정 Both, CookAppsAuth 및 SRDebugger 프리팹의 누락 스크립트, 앱 아이콘 압축, Localization Android App Info 미설정 등이 있다. 이번 빌드에는 오류로 집계되지 않았다.

### 실기기 후속 수정: Callable 직렬화 보존

잠금 해제 후 `ensureAccount`가 `Unknown env:`로 실패했다. 원인은 환경 프로필이 아니라 Medium stripping에 의한 요청 객체 getter 제거다. `CallablePayload`는 익명 요청을 Newtonsoft 리플렉션으로 직렬화하는데, 이전 APK의 `ManagedStripped/Assembly-CSharp.dll`에는 익명 타입 19개의 속성이 전부 제거되어 있었다. 계정 요청의 `env/deviceId/appVersion`도 제거돼 빈 맵이 전달됐다.

`Assets/Scripts/OutGame/Save/link.xml`에 익명 타입 접두사 보존과 실제 접근자가 제거된 응답·중첩 모델·세이브 타입 29개를 추가했다. Medium 설정과 원격 카탈로그는 유지한다. [Unity link.xml 보존 규칙](https://docs.unity.cn/6000.0/Documentation/Manual/managed-code-stripping-xml-formatting.html)에 따른 타입 단위 보존이다.

Cecil로 원본과 APK용 어셈블리를 대조하는 `.codex-temp/check-callable-stripping.ps1` 검증 결과:

- 수정 전: 직렬화 속성·익명 요청 getter 누락 177개.
- 수정 후: `[JsonProperty]`·`[FirestoreProperty]` 속성 259개와 익명 요청 타입 21개 검사, 누락 0개.

수정 APK `C:/Users/cookapps/Desktop/Builds/26.09.14-optimized-fix.apk`는 53,249,815 bytes (50.78 MiB), 빌드 오류 0개다. 기기 재설치 후 18:49:40에 `ensureAccount created=True revision=1 env=live`와 원격 세이브 채택 성공을 확인했다.

다음 차단은 별도 배포 불일치다. 병합된 가이드 미션 변경은 콘텐츠 세대 6만 지원하지만 live 서버 `_index`는 `5.4` / 최소 세대 5다. 앱은 `Remote content major 5 is not supported by this app`로 업데이트 안내를 표시했다. Google Play 서비스의 `Phenotype.API/providerinstaller` 경고 뒤에도 인증·계정 생성은 성공했으므로 해당 경고는 이번 진입 차단의 원인이 아니다.

세대 6 서버 코드 빌드·버전 일관성 검사 및 가이드 미션/구세대 호환 테스트 9개 통과. Addressables Hosting 배포와 Firestore 콘텐츠 표 공개는 별개이며, 현재 live 표는 변경하지 않았다. 세대 6 공개는 세대 5 앱의 진입에도 영향을 주므로 별도 배포 범위 확인이 필요하다.

### 실기기 후속 수정: 스펙 테이블 필드 보존

이후 live가 외부 작업으로 `6.5`에 공개된 상태에서 앱은 버전 검사를 통과했지만 `CardPack column count mismatch`로 중단됐다. `OfflineAllowed`는 이 실패를 전투용 게이트가 반환한 결과이며, 초기화는 이를 허용하지 않아 종료됐다.

실제 원인: 원본 CardPack은 13열인데 APK에서는 `channel`·`refundAmount`가 제거되어 11열이었다. `SpecPayloadCodec`가 public 필드를 리플렉션으로 읽어 열 수·순서·해시를 검증하므로 직접 사용하는 코드가 없는 필드도 필수다. 전체 검사에서 7개 테이블의 필드와 PassSeason·PassLevel 매니저 getter가 제거된 것을 확인했다.

`Assets/Scripts/OutGame/Spec/link.xml`에 클라이언트 동기화 행 타입 21개의 필드, SpecDataManager와 InnerData 컨테이너를 보존했다. 자동 생성 C#·SpecData.bytes·서버 스펙 값은 이 수정에서 변경하지 않았다. 보존 적용 후 스키마 필드 이름·순서·자료형 및 매니저/All 접근자 검사 누락 0개다.

최종 APK: `C:/Users/cookapps/Desktop/Builds/26.09.14-optimized-fix2.apk`, 53,255,207 bytes (50.79 MiB). 오류 0개로 빌드·덮어 설치했다. 스펙 수정 시작 시 bytes 기준 해시 `73FACF17CC1CB491765D56D2854D080B78733D60D4B7F321F2788819F3FB1CF6`는 빌드 후에도 동일하다.

실기기에서 18:58:47에 서버 테이블 21/21 수신·캐시 채택 성공. 이후 APK가 참조한 `catalog_2026.09.14.09.42.30.hash`의 미배포를 발견했다. 앞서 예약된 리소스 빌드가 뒤늦게 완료된 산출물이며, 기존 파일을 보존한 채 Hosting에 추가 배포했다. 185개 HTTPS 파일과 카탈로그·대표 번들 다운로드 20개의 해시 검증 통과(`Build/ResourceDeploymentChecks/1789380092552.json`).

19:01 재실행 후 서버 `6.5`와 로컬 21개 표 모두 일치, 초기화·세이브 저장·시작 화면 표시 성공. 시작 버튼을 눌러 원격 BattleScene의 첫 전투 튜토리얼 진입, 카드 이미지·폰트·UI 표시를 확인했다. 해당 실행의 Unity Error 및 AndroidRuntime Error 로그는 없었다. 전체 튜토리얼 플레이·로비의 모든 기능까지 검증한 것은 아니다.
