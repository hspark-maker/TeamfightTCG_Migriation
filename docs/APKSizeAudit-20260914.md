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

## 1차 적용 상태 — 빌드 보류

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
