using System;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(LongPressDetector))]
public class KeywordIconButton : MonoBehaviour, IPointerUpHandler
{
    public Action onPointerUp;

    public void OnPointerUp(PointerEventData _) => onPointerUp?.Invoke();
}
