using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public void Die(bool fromDash = false)
    {
        if (fromDash && HitStop.Instance != null)
        {
            HitStop.Instance.Trigger(0.06f, 0.05f);
        }

        Destroy(gameObject);
    }
}
