using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 덱 대표 이미지 후보 목록. 덱 생성 시 여기서 하나를 랜덤으로 뽑아 세이브에 고정한다.
/// 세이브에 남는 키는 인덱스가 아니라 스프라이트 에셋 이름이다 — 목록 순서를 바꿔도 기존 덱 그림이 뒤바뀌지 않게.
/// </summary>
[CreateAssetMenu(fileName = "DeckImageCatalog", menuName = "Card Battle/Deck Image Catalog")]
public class DeckImageCatalog : ScriptableObject
{
    [Header("덱 대표 이미지 후보 (에셋 이름이 곧 세이브 키 — 이름 변경 시 기존 덱은 폴백으로 떨어진다)")]
    [SerializeField] List<Sprite> images = new List<Sprite>();

    public IReadOnlyList<Sprite> Images
        => images != null ? images : (IReadOnlyList<Sprite>)System.Array.Empty<Sprite>();

    // 키로 스프라이트 찾기. 이름이 겹치면 앞선 항목이 이긴다.
    public Sprite Find(string _key)
    {
        if (string.IsNullOrEmpty(_key) || images == null) return null;

        for (int t_i = 0; t_i < images.Count; t_i++)
            if (images[t_i] != null && images[t_i].name == _key) return images[t_i];

        return null;
    }
}
