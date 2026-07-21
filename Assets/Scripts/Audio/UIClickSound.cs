using UnityEngine;
using UnityEngine.EventSystems;

public class UIClickSound : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] AudioClip[] overrideClips;

    public void OnPointerClick(PointerEventData _)
    {
        if (overrideClips != null && overrideClips.Length > 0)
            SoundManager.Instance?.PlayRandom(overrideClips);
        else
            SoundManager.Instance?.PlayUIClick();
    }
}
