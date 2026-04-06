using UnityEngine;

public class LevelEvents : MonoBehaviour
{
    [Header("Battleship")]
    [Tooltip("The battleship GameObject in the scene (should start inactive).")]
    public GameObject battleship;

    private int enemiesAlive;

    void Start()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);

        foreach (var enemy in enemies)
        {
            // Skip the battleship and its children
            if (battleship != null && enemy.transform.IsChildOf(battleship.transform))
            {
                continue;
            }

            // Skip non-damageable root healths (like battleship root if active)
            if (!enemy.isDamageable)
            {
                continue;
            }

            enemiesAlive++;
            enemy.onDeath += OnEnemyKilled;
        }
    }

    void OnEnemyKilled()
    {
        enemiesAlive--;

        if (enemiesAlive <= 0)
        {
            SpawnBattleship();
        }
    }

    void SpawnBattleship()
    {
        if (battleship != null)
        {
            battleship.SetActive(true);
        }
    }
}
