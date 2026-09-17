# 사운드 Addressables 전환 (2026-09-16)

## 구현 결과

사용 중인 **BGM 2개·효과음 28개**와 설정 SO 2개를 `RemoteAudio` 그룹에 등록했다. 구현 후 `0.0.1` 전체 리소스 빌드·CDN 배포·릴리스 APK 빌드를 완료했다. 이후 검증 결과는 [배포 기록](FirebaseResourceHosting.md)의 `Android 0.0.1 리소스·APK 빌드 완료` 항목을 따른다. 아래 확인 결과는 구현 당시의 기록이다.

- Remote Build/Load Path, `RemoteAudio` 라벨, Pack Separately, Can Change Post Release.
- 번들 캐시·CRC 사용, 요청 제한 30초, 재시도 2회. 미사용 사운드 후보는 등록하지 않았다.
- 클립 원본 합계 18,788,066 bytes(약 17.92 MiB). 실제 다운로드 용량은 플랫폼 압축을 적용한 Addressables 빌드 후 확정한다.
- 로비·전투 곡, 효과음 배정, SFX 기본 볼륨 0.85와 사건별 볼륨은 그대로다.

## 로딩 순서

1. 기존 로그인·스펙 동기화 이후 `RemoteCardArtDownload`가 Cards/Packs/RemoteUI/RemoteScenes/**RemoteAudio** 라벨을 함께 조회한다.
2. 기존 다운로드 용량·진행 표시에서 미캐시 번들을 합산해 받는다. Union 모드로 공유 의존성을 중복 계산하지 않는다.
3. `RuntimeContentCache`가 원격 `RuntimeContentCatalog`와 그 의존 설정·사운드를 비동기로 적재한다.
4. `RuntimeContentPreloadStep`이 `SoundManager.Configure`에 SoundConfig와 OutgameSoundBank를 주입하고 SFX 볼륨을 적용한다.
5. 로비 BGM은 설정 주입 완료 후, 전투 BGM은 기존 전투 인트로 시점에 시작한다. 각 곡의 페이드 시간은 유지한다.

다운로드나 카탈로그 적재 실패는 기존 초기화 실패·재시도 흐름을 사용한다. 로비 단독 Play의 BGM 대기는 재시도 중 유지되고, 씬 파괴 시 취소된다. 다운로드 전의 시작·로그인 UI는 사운드 설정이 없으므로 공용 효과음을 재생하지 않는다.

RuntimeContentCache의 Addressables handle은 기존대로 세션 동안 설정과 클립 의존성을 보유한다. 개별 효과음 재생 시 네트워크 요청을 보내는 방식은 아니다.

## 앱 내장 참조 제거

- `Initialize.prefab`의 SoundConfig·OutgameSoundBank 직접 참조 제거. SoundManager의 두 필드는 런타임 주입용으로 바꿨다.
- `LobbyScene`과 `BattleScene`의 AudioClip 직접 참조를 SoundConfig의 `lobbyBgm`·`battleBgm`으로 이동했다.
- SoundConfig·OutgameSoundBank는 원격 RuntimeContentCatalog에서만 참조한다. 클립도 명시적으로 개별 등록해 설정 번들 사이의 암시적 복제를 방지한다.

씬이나 Resources의 직접 참조는 Addressable 등록 여부와 별개로 플레이어에 자산을 포함시킬 수 있어 함께 제거했다. [Unity 자산 의존성 문서](https://docs.unity.cn/Packages/com.unity.addressables%401.21/manual/AssetDependencies.html).

## 빌드 검사

`Tools > Addressables > Validate Remote Audio` 메뉴를 추가했다. `Build Firebase Resources`와 앱 빌드 전처리에서도 같은 검사를 실행한다.

- RemoteAudio 원격 경로·개별 번들·변경 가능 설정·캐시·카탈로그 라벨 사용 확인.
- 설정이 참조하는 클립 전량의 그룹·라벨 확인. 그룹에 미사용 항목이 있으면 중단.
- 시작 씬·Resources·preloadedAssets의 Unity 의존성에 사운드 설정/클립이 다시 포함되면 중단.

새 효과음을 SoundConfig/OutgameSoundBank에 연결할 때 그 클립도 RemoteAudio 그룹과 라벨에 등록한다. 기존 클립을 교체해 더 이상 사용하지 않으면 그룹에서 이전 항목을 제거한다.

## 확인 결과와 한계

- 정적 감사: 사용 클립 30개 + 설정 2개 = 원격 항목 32개 일치, 미사용 항목·누락 GUID 0개.
- 활성 시작 씬·Resources·preloadedAssets 47개 루트에서 262개 YAML 의존성을 순회했다. 사운드·설정의 내장 참조 경로 0개.
- 기존 SoundConfig의 효과음 목록·볼륨, OutgameSoundBank 전체가 변경 전과 동일함을 대조했다.
- Unity 설치의 Roslyn을 별도 출력 폴더에서 사용해 변경 런타임 코드와 에디터 검증 코드의 분리 컴파일 통과. 소스 생성기·임포터·Unity 에디터는 실행하지 않았다.
- 전체 소스 컴파일은 비활성화한 SpecData 소스 생성기의 심볼 28개가 없어 완료되지 않았다. `RuntimeContentPreloadStep`은 기존 assembly 내부 접근 때문에 분리 컴파일에서 제외했으며 전체 컴파일 진단에 이 파일 오류는 없었다. 이를 전체 Unity 컴파일 성공으로 간주하지 않는다.
- 실제 아틀라스 패킹·Addressables 번들 생성·HTTP 다운로드·캐시 재실행·오프라인 실패 후 재시도·기기 사운드 재생은 미검증이다. Unity 연결이 철회된 상태여서 에디터 작업을 우회하지 않았다.
- `SpecData.bytes` 생성·수정 없음. 커밋·푸시·배포 없음.

로컬 검증 기록은 `Build/RemoteAudio-20260916/`에 있다(Git 제외). UI 아틀라스 누락 6개 보완은 [별도 감사 기록](UIAtlasAudit-20260916.md)을 따른다.

## 실제 배포 시

**새 앱 빌드가 필요하다.** SoundManager 초기화 방식과 SoundConfig/씬의 직렬화 구조를 바꿨으므로 기존 APK에 리소스만 덮어 배포하지 않는다.

새 앱 버전의 원격 경로로 전체 리소스 빌드 → 배포 검사·Hosting 배포 → 해당 카탈로그를 사용하는 앱 빌드 순서로 진행한다. 앱 빌드에서 Addressables를 다시 생성하지 않는 기존 설정을 유지한다. 과거 앱 카탈로그의 키·의존성 순서나 런타임 토큰을 수동으로 재작성하지 않는다.

실기기에서 첫 다운로드에 사운드 용량이 포함되는지, 다음 실행에서 캐시가 재사용되는지, 실패 후 재시도와 로비/전투 BGM·효과음이 정상인지 확인한다. 상세 규칙은 [리소스 배포 문서](FirebaseResourceHosting.md)와 [오류·재발 방지 기록](AddressablesDeploymentPitfalls.md)을 따른다.
