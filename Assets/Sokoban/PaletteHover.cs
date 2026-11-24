/**
 * @file PaletteHover.cs
 * @brief Pointer entry and exit animate the card contents inside the existing scroll layout. OnDisable restores the resting angle for reused cards.
 */
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sokoban
{
    public sealed class PaletteHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        bool over;
        public void OnPointerEnter(PointerEventData data) => over=true;
        public void OnPointerExit(PointerEventData data) => over=false;
        void Update() { transform.localRotation=Quaternion.RotateTowards(transform.localRotation,Quaternion.Euler(0,0,over?1.5f:0),Time.unscaledDeltaTime*30); }
        void OnDisable() { over=false;transform.localRotation=Quaternion.identity; }
    }
}
