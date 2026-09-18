using UnityEngine;

public partial class LobbyGainEffectDirector
{
    [Header("프로필 레벨업")]
    [SerializeField] RectTransform levelUpTarget;
    [SerializeField] ProfileLevelUpEffect levelUpPrefab;

    ProfileLevelUpEffect _levelUpEffect;

    bool IsLevelUpPlaying => _levelUpEffect != null && _levelUpEffect.IsPlaying;

    void TryPlayLevelUpAlongsideGains()
    {
        if (!AccountLevelUpHandoff.HasPending || !GuidanceCoordinator.CanPresentAlongsideGains) return;
        if (AlbumInsertSession.IsRunning) return;
        if (AlbumInsertQueue.HasPending && m_runId == m_finishedRunId) return;
        var pool = UIPoolManager.Instance;
        if (pool == null || pool.HasVisibleUIExcept()) return;
        TryPlayLevelUp();
    }

    void TryPlayLevelUp()
    {
        if (!AccountLevelUpHandoff.HasPending || IsLevelUpPlaying) return;
        if (levelUpTarget == null || levelUpPrefab == null || !levelUpPrefab.IsWired)
        {
            AccountLevelUpHandoff.Consume();
            Debug.LogWarning("[LobbyGainEffectDirector] Profile level-up presentation is not wired.");
            return;
        }
        if (!levelUpTarget.gameObject.activeInHierarchy) return;
        if (_levelUpEffect == null) _levelUpEffect = Instantiate(levelUpPrefab, transform);
        transform.SetAsLastSibling();
        _levelUpEffect.Play(levelUpTarget, AccountLevelUpHandoff.Consume());
    }

    void StopLevelUp()
    {
        if (_levelUpEffect != null) _levelUpEffect.Stop();
    }
}
