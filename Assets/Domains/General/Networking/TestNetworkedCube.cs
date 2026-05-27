using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    public class TestNetworkedCube : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;

        private void Update()
        {
            if (!isOwner)
                return;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            float y = 0f;
            if (Input.GetKey(KeyCode.E)) y += 1f;
            if (Input.GetKey(KeyCode.Q)) y -= 1f;

            var dir = new Vector3(h, y, v);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            transform.position += dir * (moveSpeed * Time.deltaTime);
        }
    }
}
