using UnityEngine;
using UnityEngine.EventSystems;

namespace AndroidRpg.Client
{
    public static class MobileInput
    {
        private static Vector2 _keyboardMovement;

        public static Vector2 ReadMovement()
        {
            _keyboardMovement.x = Input.GetAxisRaw("Horizontal");
            _keyboardMovement.y = Input.GetAxisRaw("Vertical");

            if (_keyboardMovement.sqrMagnitude > 0f)
            {
                return _keyboardMovement.normalized;
            }

            foreach (Touch touch in Input.touches)
            {
                if (touch.position.x < Screen.width * 0.5f &&
                    !EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                {
                    Vector2 center = new Vector2(Screen.width * 0.25f, Screen.height * 0.25f);
                    Vector2 delta = touch.position - center;
                    return Vector2.ClampMagnitude(delta / (Screen.width * 0.20f), 1f);
                }
            }

            return Vector2.zero;
        }

        public static bool ReadAction()
        {
            return Input.GetButtonDown("Jump") ||
                   (Input.touchCount > 0 && Input.GetTouch(0).position.x > Screen.width * 0.5f);
        }
    }
}
