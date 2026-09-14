using TMPro;
using UnityEngine;

/// <summary>전투 복귀 로딩의 점 세 개를 실제 시간으로 반복한다. 펭귄은 피벗을 맞춘 스프라이트 클립을 Animator가 재생한다.</summary>
public sealed class BattleReturnLoadingAnimation : MonoBehaviour
{
    const float DotSeconds = 0.35f;
    const string LoadingLabel = "로딩중...";

    [SerializeField] TMP_Text loadingText;

    float m_elapsed;
    int m_dotStep = -1;

    void OnEnable()
    {
        m_elapsed = 0f;
        m_dotStep = -1;
        if (loadingText != null) loadingText.text = LoadingLabel;
        Refresh();
    }

    void Update()
    {
        m_elapsed += Time.unscaledDeltaTime;
        Refresh();
    }

    void Refresh()
    {
        int t_dotStep = Mathf.FloorToInt(m_elapsed / DotSeconds) % 3;
        if (loadingText != null && t_dotStep != m_dotStep)
        {
            m_dotStep = t_dotStep;
            // 전체 문구의 정렬 폭을 유지하며 끝의 점만 숨긴다.
            loadingText.maxVisibleCharacters = LoadingLabel.Length - t_dotStep;
        }
    }
}
