using UnityEngine;

// タイトル画面のカードにマウスを乗せたときの通知（TitleView が選んでいるカードを切り替える）
public class TitleCardHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler
{
    public System.Action Entered;

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        Entered?.Invoke();
    }
}
