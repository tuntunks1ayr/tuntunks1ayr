using UnityEngine;

namespace AndroidRpg.Client
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float turnSpeed = 12f;

        private CharacterController _controller;
        private Vector3 _velocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            Vector2 movement = MobileInput.ReadMovement();
            Vector3 worldDirection = new Vector3(movement.x, 0f, movement.y);

            if (worldDirection.sqrMagnitude > 0.01f)
            {
                worldDirection.Normalize();
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(worldDirection),
                    turnSpeed * Time.deltaTime);

                _controller.Move(worldDirection * (moveSpeed * Time.deltaTime));
            }

            if (_controller.isGrounded && _velocity.y < 0f)
            {
                _velocity.y = -2f;
            }

            _velocity.y += gravity * Time.deltaTime;
            _controller.Move(_velocity * Time.deltaTime);
        }

        public void RequestJump()
        {
            if (_controller.isGrounded)
            {
                _velocity.y = 8f;
            }
        }
    }
}
