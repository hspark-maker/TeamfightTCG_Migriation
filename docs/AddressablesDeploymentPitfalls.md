# Addressables 배포 오류와 재발 방지

2026-09-16 작업에서 확인한 문제 기록. Addressables 빌드·카탈로그 수정·Firebase 리소스 배포 전에 읽는다.
배포 이력과 명령은 [FirebaseResourceHosting.md](FirebaseResourceHosting.md), UI 패킹 예외는 [UIAtlases.md](UIAtlases.md)를 함께 확인한다.

## 1. 카탈로그에 에디터 경로가 저장된 오류

**증상:** Android에서 `Unable to open archive file`, `Invalid path in AssetBundleProvider` 발생. 경로가 `Library/com.unity.addressables/aa/Android/Android/...`로 시작한다.

**확인된 원인:** 에이전트가 번들 파일명 충돌을 처리하면서 카탈로그를 재직렬화했다. `ContentCatalogData.CreateLocator()`가 런타임 토큰을 에디터 경로로 해석했고, 그 위치 객체를 `SetData()`에 넣어 저장하면서 PC 경로가 CDN에 배포됐다. Unity 원본 빌드 JSON에는 정상 토큰이 있었다.

- 정상 로컬 경로: `{UnityEngine.AddressableAssets.Addressables.RuntimePath}/Android/<bundle>`
- 잘못 저장된 경로: `Library/com.unity.addressables/aa/Android/Android/<bundle>`
- 원격 경로: `https://bm-cardbattle-assets.web.app/<version>/<platform>/<bundle>`

**규칙:** 해석된 위치 객체를 배포 카탈로그로 왕복 직렬화하지 않는다. 변경이 필요하면 원본의 미해석 경로를 보존하고, 카탈로그 해시도 함께 갱신한다. PC 경로를 기기에 맞는 경로로 간주하지 않는다.

**복구:** 잘못된 경로 3개만 수정했다. 공통 번들 2개는 HTTPS로 연결하고, raytracing 번들은 원본 RuntimePath 토큰을 복원했다. 키·옵션·의존성 직렬화는 보존했다.

**재발 방지:** `scripts/validate-resource-hosting.cjs`가 Library 경로와 절대 로컬 경로를 거부한다. 정상 RuntimePath를 허용하는 경우까지 포함해 테스트 14개 통과.

`Build/UiAtlasDeployment-20260916/fix-immutable-catalog.cs.txt`는 이 오류를 만든 과거 작업 기록이다. 재사용하지 않는다.

## 2. 공통 번들이 기존 APK와 달라질 수 있는 설정

`monoscripts`·`unitybuiltinassets`는 기본 그룹의 Build/Load Path를 따른다. 당시 기본 그룹은 비어 있었지만 경로는 Local이었다. 새 공통 번들이 생기면 리소스만 배포해도 설치된 APK 안에는 해당 파일이 없을 수 있다. 모든 빌드가 반드시 실패한다는 뜻은 아니다.

**규칙:** 이 프로젝트의 기본 그룹은 Remote Build/Load Path와 Can Change Post Release를 유지한다. `FirebaseResourceBuild`의 사전 검사가 이 설정을 확인한다. 공유 번들의 원격 경로와 실제 업로드 파일도 검사한다.

기존 로컬 의존성은 런타임 토큰 보존뿐 아니라 대상 APK에 실제 포함되는지도 확인한다. 리소스 배포는 C# 실행 코드를 갱신하지 않는다.

## 3. 원격 번들만 검증해서 놓친 로컬 의존성

앞선 검증은 원격 번들 247개의 크기·CRC와 CDN 다운로드를 확인했지만 로컬 의존성 3개를 제외했다. 카탈로그의 잘못된 PC 경로를 에디터에서는 읽을 수 있어 Android 오류를 놓쳤다.

**검증 기준:**

- 모든 번들 경로를 원격 URL·정상 런타임 토큰·잘못된 로컬 경로로 구분한다.
- Android APK 경로를 넣어 카탈로그를 해석했을 때 에디터 경로가 남지 않는지 확인한다. 모의 해석 결과를 배포 파일로 저장하지 않는다.
- 원격 의존성은 파일 존재·크기·CRC, CDN 다운로드 SHA-256을 확인한다.
- PC 검증, Android 경로 모의 검증, 실제 기기 검증을 구분해서 보고한다.

## 4. 이미 배포한 번들의 같은 파일명 덮어쓰기

이번 빌드에서 덱 편집·팩 개봉 번들 2개가 기존 파일명으로 생성됐지만 실제 바이트·CRC가 달랐다. 내용이 달라진 근본 원인은 확정하지 않았다. 파일명의 해시만 보고 동일 파일이라고 판단하면 안 된다.

**규칙:** 이미 배포한 `.bundle`은 같은 URL에서 내용을 바꾸지 않는다. CDN의 immutable 캐시 및 기기 캐시와 충돌한다. 배포 검사가 이를 막으면 검사를 우회하지 말고 이전 파일을 보존한다. 새 파일이 필요하면 새 URL·캐시 해시를 사용하고 자산 키·옵션·의존성·런타임 경로를 모두 검증한다.

이번 파일명 보정 과정이 1번 오류를 만들었다. 보정 성공이나 CRC 통과만으로 전체 배포가 안전하다고 결론 내리지 않는다.

## 5. 로컬 배포 폴더가 CDN 전체보다 오래된 상태

배포 전 CDN 파일 71개가 로컬 `ServerData`에 없었다. 그대로 배포하면 기존 앱에 필요한 파일이 제거될 수 있었다. CDN에서 복원하고 SHA-256을 확인했다.

**규칙:** 배포 전 공개 `resources-manifest.json`과 비교해 기존 앱 버전·플랫폼·카탈로그·번들을 보존한다. 현재 프로젝트의 상태 파일과 카탈로그를 확인한 뒤 업데이트한다. CDN에서 날짜가 가장 최신인 카탈로그가 현재 프로젝트의 대상이라고 추측하지 않는다.

## 6. 의존성 순서 변경으로 다른 번들에서 자산을 찾은 오류

**증상:** 2026-09-16 12:50 Android에서 `Unable to load asset of type RuntimeContentCatalog from location Assets/SO/RuntimeContentCatalog.asset` 발생. 1번의 PC 경로를 복구한 뒤 드러난 추가 오류다.

**확인된 원인:** 같은 카탈로그 재직렬화가 의존성 순서도 변경했다. 원본에서는 `RuntimeContentCatalog`의 첫 번째 의존성이 본체 번들이었지만, 잘못된 카탈로그에서는 `monoscripts`가 첫 번째가 되고 본체는 26번째로 밀렸다.

`BundledAssetProvider.InternalOp.LoadBundleFromDependecies`는 **첫 번째 `IAssetBundleResource`를 자산이 들어 있는 번들로 선택한다.** 따라서 MonoScript 번들에서 `RuntimeContentCatalog.asset`를 찾다가 실패했다. 첫 본체 번들이 잘못된 자산은 총 63개였다.

재직렬화 과정에서 locator의 자동 생성 dependency-set 키까지 entry 키로 수집한 것이 문제였다. `SetData()`가 기존 키를 재사용하면서 원래 의존성 순서 대신 entry 순서로 묶었다. 타입 이름·파일 존재·CRC가 모두 맞아도 이 오류가 발생한다.

**놓친 검증:**

- 의존성 목록에 `OrderBy`를 적용해 비교했다. 구성원이 같으면 통과하므로 순서 변경을 숨겼다.
- 초기 수동 로드 검사는 번들 이름으로 올바른 본체를 골랐다. 실제 Provider가 잘못된 첫 번들을 선택하는 경로를 재현하지 못했다.
- 기기 APK와 로컬 APK의 SHA-256, IL2CPP 타입·필드·생성자, 신·구 MonoScript의 클래스명·pathID를 확인했다. 이 사례의 원인은 타입 스트리핑이나 다른 APK가 아니었다.

**복구:** 원본 `catalog.raw-build.json`의 Entry/Bucket/Key 데이터를 그대로 복원했다. 이미 검증한 URL 4개와 동일 길이 캐시 해시 2개만 반영하고 카탈로그 해시를 갱신했다. 번들 파일·APK는 바꾸지 않고 카탈로그와 해시만 재배포했다.

**재발 방지 규칙:**

- 의존성은 순서까지 의미가 있다. 목록을 정렬하거나 집합으로 바꿔 동등성을 판단하지 않는다.
- 모든 자산의 첫 번째 의존 번들과 전체 의존성 순서를 원본 빌드와 대조한다. 의도한 URL 변경만 허용한다.
- 키·옵션 보존뿐 아니라 Entry/Bucket/Key 직렬화와 런타임 경로 토큰 보존도 확인한다. locator를 재구성해 카탈로그를 저장하지 않는다.
- 자산 로드 검사는 실제 Provider처럼 첫 번째 번들을 선택해야 한다. 본체 파일명을 알고 골라 읽는 검사를 대신 사용하지 않는다.
- `RuntimeContentCatalog`는 타입 지정 로드뿐 아니라 `Validate()`로 필수 설정 참조까지 검사한다.

**검증 결과:** 692개 위치·830개 키·256개 자산의 의존성 순서와 첫 번들 보존을 독립 검증했다. 실제 첫 번들 선택 방식으로 의존 번들 26개를 로드하고 `RuntimeContentCatalog` 타입 로드·필수 참조 검사를 통과했다. 재배포 후 **사용자가 실제 기기에서 통과했다고 확인했다.**

기록: `Build/RuntimeCatalogLoadFix-20260916/verification-independent.json`, `typed-load-verification.txt`, `serialization-verification.json`. 복구 카탈로그 해시: `6a89913098c4ba6b1b5792524c34338a`.

## 이번 복구의 완료 범위

- 대상: Android / 앱 버전 `0.0.0` / `catalog_2026.09.15.09.20.47`.
- 출시 상태: `Build/AddressablesState/0.0.0/Android/20260915-092344-669/addressables_content_state.bin`.
- 수정 카탈로그·공통 번들 재배포 완료. CDN 파일 438개 확인, 변경 파일 4개 다운로드 SHA-256 일치.
- Android 경로 모의 해석·공통 번들 CRC·배포 검사 테스트 통과. 이후 6번의 의존성 순서 오류까지 복구했고, 사용자 기기 재실행 통과 확인을 받았다.
- `SpecData.bytes` 변경 없음. APK 재빌드 없음.
- 상세 증거: `Build/RuntimeCatalogLoadFix-20260916/`, `Build/AddressablesPathFix-20260916/`, `Build/UiAtlasDeployment-20260916/`. `Build/`는 로컬 작업 기록이므로 다른 환경에 없을 수 있다.
