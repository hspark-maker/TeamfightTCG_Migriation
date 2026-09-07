# BattleCore 골든 러너

Unity가 기록한 `functions/testdata/golden/*.json`을 공용 C# `BattleReplay`로 재생한다.
시너지 수치는 `docs/SpecData/Synergy*Def_sheet.csv`에서 읽는다.

```powershell
dotnet run --project Tools/BattleCoreGolden/TeamfightTCG.BattleCoreGolden.csproj -- .
```

각 벡터의 해시·RNG 소비 횟수·잔존 카드·승패·체크포인트를 비교하며,
하나라도 다르면 종료 코드 1을 반환한다. `eligible=false` 또는 `boardOrder`가 없는 벡터는 건너뛴다.
