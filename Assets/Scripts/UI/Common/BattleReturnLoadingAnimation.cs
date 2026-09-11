using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>전투 복귀 로딩의 4×2 펭귄 시트와 점 세 개를 실제 시간으로 반복한다.</summary>
public sealed class BattleReturnLoadingAnimation : MonoBehaviour
{
    const int Columns = 4;
    const int Rows = 2;
    const float FrameSeconds = 0.15f;
    const float DotSeconds = 0.35f;
    const string LoadingLabel = "로딩중...";

    [SerializeField] RawImage penguin;
    [SerializeField] TMP_Text loadingText;

    float m_elapsed;
    int m_frame = -1;
    int m_dotStep = -1;

    void OnEnable()
    {
        m_elapsed = 0f;
        m_frame = m_dotStep = -1;
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
        int t_frame = Mathf.FloorToInt(m_elapsed / FrameSeconds) % (Columns * Rows);
        if (penguin != null && t_frame != m_frame)
        {
            m_frame = t_frame;
            penguin.uvRect = new Rect((t_frame % Columns) / (float)Columns,
                1f - (t_frame / Columns + 1) / (float)Rows, 1f / Columns, 1f / Rows);
        }

        int t_dotStep = Mathf.FloorToInt(m_elapsed / DotSeconds) % 3;
        if (loadingText != null && t_dotStep != m_dotStep)
        {
            m_dotStep = t_dotStep;
            // 전체 문구의 정렬 폭을 유지하며 끝의 점만 숨긴다.
            loadingText.maxVisibleCharacters = LoadingLabel.Length - t_dotStep;
        }
    }
}
