# 키워드 강화·먹이·한계돌파 제거 배포

2026-09-21 사용자 승인으로 `bm-cardbattle / asia-northeast3` 공용 서버에 반영했다. test/live는 데이터를 분리하지만 Functions는 공용이다. 최종 default 함수 40개 모두 `ACTIVE`이며 `enhanceKeyword`, `limitBreakCard`는 삭제했다.

## 기존 세이브 정리

- 대상: `cardbattle` DB의 테스트 14개·라이브 9개 세이브, 모두 schemaVersion 8.
- 삭제: 최상위 `keywordGrowth` 23개, 카드 성장 항목 174개의 `snack`·`limitBreak` 필드.
- 카드 레벨·샤드 진행도·소유·덱·튜토리얼 등 나머지 값은 그대로 보존했다. 변경된 각 세이브의 revision만 1 증가시키고 updatedAt을 갱신했다.
- 원본 백업 23개, 세이브 변경, 완료 기록을 하나의 Firestore 트랜잭션에 저장했다. 모든 원본 백업과 변경 후 세이브를 대조해 위 삭제 필드·revision·updatedAt 외 값이 동일함을 확인했다. 별도 지갑 23개도 전후 문서가 동일하다.
- 완료 기록: `maintenance/retiredGrowth20260921`. 원본 백업: 그 문서의 `backups` 하위 컬렉션. 클라이언트 접근은 기본 거부된다.
- 재조회 결과 폐기 필드 잔존 0건. 스키마 버전이나 다른 시스템의 마이그레이션은 변경하지 않았다. 과거 거래 영수증은 감사 기록으로 보존한다.

## 최종 코드와 규칙

서버와 클라이언트의 저장 모델에서 폐기 필드를 제거했다. 신규 계정도 해당 필드를 생성하지 않는다. Firestore 규칙은 keywordGrowth 재추가를 거절하고, 카드 성장값은 클라이언트 저장으로 바꿀 수 없도록 서버 원본과 동등함을 요구한다. 카드 성장 변경은 기존 서버 callable이 담당한다.

임시 관리 함수 `migrateRetiredGrowthOnce`는 IAM private으로 배포했다. 로컬 gcloud 재인증 문제로 실제 실행에는 Cloud Scheduler의 서비스 계정 OIDC 인증을 사용했다. 작업 성공 후 임시 Scheduler 작업을 삭제했다. 이어 마이그레이션 소스·export·컴파일 산출물을 삭제하고 전체 default 함수와 최종 저장 규칙을 다시 배포했다. 임시 함수와 그 Cloud Run 서비스도 삭제 완료했으며 원격 조회가 404임을 확인했다. 재실행 가능한 마이그레이션 코드는 최종 소스와 배포 패키지에 남아 있지 않다.

## 검증과 기록

- 전체 서버 lint·TypeScript build·콘텐츠 세대 검사 통과.
- 저장 코덱 관련 회귀 25개, 현행 Firestore 규칙 회귀 9개 통과.
- 임시 마이그레이션의 보존·멱등성·후보 수·백업 원자성 검증 통과.
- Unity 컴파일 및 OnboardingPlayTestValidation 통과.
- 실제 `cardbattle` 규칙 릴리스가 로컬 규칙과 일치함을 확인했다.
- `SpecData.bytes`와 자동 생성 테이블 C#은 변경하지 않았다. APK·Addressables는 이번 배포 대상이 아니다.

배포 로그와 전후 검증은 `.codex_tmp/retired-growth-deploy-20260921/`에 보관했다. `verification.json`, `deployment-verification.json`, `rules-verification.json`이 최종 검증 요약이다. 개인 세이브 원본·지갑 스냅샷 파일은 외부에 공유하지 않는다.
