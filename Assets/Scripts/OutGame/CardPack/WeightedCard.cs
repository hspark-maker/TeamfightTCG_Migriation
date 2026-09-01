using UnityEngine;

[System.Serializable]
public struct WeightedCard
{
    [CardId] public int cardId;
    [Min(0)] public int weight;

    public int EffectiveWeight => weight > 0 ? weight : 1;
    public int CardId => cardId;
}
