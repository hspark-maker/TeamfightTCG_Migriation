using System.Collections.Generic;
using UnityEngine;

// 페이크 매칭이 상대 표시용으로 뽑아 쓰는 이름·동상 후보 풀
[CreateAssetMenu(fileName = "OpponentProfilePool", menuName = "Card Battle/Opponent Profile Pool")]
public class OpponentProfilePool : ScriptableObject
{
    public const string FALLBACK_NAME = "도전자";

    [Tooltip("상대 닉네임 후보. 매칭할 때마다 하나를 무작위로 뽑는다. " +
             "비워두거나 뽑힌 항목이 공백이면 \"" + FALLBACK_NAME + "\"이 대신 쓰인다 — 빈 이름이 화면에 나가는 일은 없다.")]
    public List<string> names = new List<string>();

    [Tooltip("뽑은 이름 뒤에 임의의 숫자를 붙인다. 후보 목록이 짧아도 같은 이름이 연달아 보이지 않게 하려는 장치다 — " +
             "후보를 충분히(수십 개) 저작했다면 꺼도 된다.")]
    public bool appendNumber = true;

    [Tooltip("붙일 숫자의 최댓값(1 ~ 이 값 사이에서 뽑는다). appendNumber가 꺼져 있거나 이 값이 1보다 작으면 무시되고 숫자가 붙지 않는다.")]
    public int numberMax = 999;

    [Tooltip("상대 동상(프로필 이미지) 후보. 비워두면 매칭 화면이 프리팹에 저작된 기본 이미지를 그대로 쓴다.")]
    public List<Sprite> avatars = new List<Sprite>();

    public string PickName()
    {
        string t_name = FALLBACK_NAME;

        if (this.names != null && this.names.Count > 0)
        {
            string t_picked = this.names[Random.Range(0, this.names.Count)];
            if (!string.IsNullOrWhiteSpace(t_picked)) t_name = t_picked;
        }

        if (!this.appendNumber || this.numberMax < 1) return t_name;
        return $"{t_name}{Random.Range(1, this.numberMax + 1)}";
    }

    public Sprite PickAvatar()
    {
        if (this.avatars == null || this.avatars.Count == 0) return null;
        return this.avatars[Random.Range(0, this.avatars.Count)];
    }
}
