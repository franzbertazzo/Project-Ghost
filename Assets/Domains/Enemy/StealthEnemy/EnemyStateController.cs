using UnityEngine;

[RequireComponent(typeof(EnemyPerception))]
[RequireComponent(typeof(SteeringBehavior))]
public class EnemyStateController : MonoBehaviour
{
    public enum EnemyState
    {
        Patrol,
        Suspicious,
        Alerted
    }

    [Header("State Settings")]
    public EnemyState initialState = EnemyState.Patrol;

    
    [Header("Debug")]
    [SerializeField]
    private EnemyState _currentState = EnemyState.Patrol;
    public EnemyState CurrentState => _currentState;

    [Header("Alerted Behaviour")]
    public float loseTargetDelay = 3f;

    public Transform player;

    private EnemyPerception _perception;
    private SteeringBehavior _steering;
    private EnemyZeroGWaypointPatrol _patrol;

    private float _timeSinceLastSeen;

    private GameObject _suspiciousPointGO;
    private Transform _suspiciousPoint;
    private EnemyShooter _shooter;

    void Awake()
    {
        _perception = GetComponent<EnemyPerception>();
        _steering   = GetComponent<SteeringBehavior>();
        _patrol     = GetComponent<EnemyZeroGWaypointPatrol>();
        _shooter = GetComponent<EnemyShooter>();

        if (!player && _perception != null)
            player = _perception.player;

        _suspiciousPointGO = new GameObject($"{name}_SuspiciousPoint");
        _suspiciousPointGO.hideFlags = HideFlags.HideInHierarchy;
        _suspiciousPoint = _suspiciousPointGO.transform;
    }

    void OnEnable()
    {
        ChangeState(initialState);
    }

    void Update()
    {
        if (_perception == null) return;

        switch (_currentState)
        {
            case EnemyState.Patrol:
                UpdatePatrol();
                break;

            case EnemyState.Suspicious:
                UpdateSuspicious();
                break;

            case EnemyState.Alerted:
                UpdateAlerted();
                break;
        }
    }

    // =========================
    // STATE TRANSITIONS
    // =========================

    void ChangeState(EnemyState newState)
    {
        if (_currentState == newState) return;

        // EXIT LOGIC
        if (_currentState == EnemyState.Alerted && _shooter)
        {
            _shooter.StopFiring();
        }

        _currentState = newState;
        Debug.Log($"{name} STATE → {_currentState}");

        switch (newState)
        {
            case EnemyState.Patrol:
                EnterPatrol();
                break;

            case EnemyState.Suspicious:
                EnterSuspicious();
                break;

            case EnemyState.Alerted:
                EnterAlerted();
                break;
        }
    }

    void EnterPatrol()
    {
        _steering.ApplyPatrolPreset();
        _steering.SetTarget(null);

        if (_patrol)
        {
            _patrol.enabled = true;
            _patrol.JumpToClosestWaypoint();
        }
    }

    void EnterSuspicious()
    {
        _steering.ApplySuspiciousPreset();

        if (_patrol)
            _patrol.enabled = false;
    }

    void EnterAlerted()
    {
        _steering.ApplyAlertedPreset();

        _timeSinceLastSeen = 0f;

        if (_patrol)
            _patrol.enabled = false;

        _shooter.SetTarget(player);
        _shooter.StartFiring();
    }

    // =========================
    // STATE LOGIC
    // =========================

    void UpdatePatrol()
    {
        if (_perception.HasDirectVisual() || _perception.IsFullyAware)
        {
            ChangeState(EnemyState.Alerted);
            return;
        }

        if (_perception.IsSuspicious)
        {
            ChangeState(EnemyState.Suspicious);
        }
    }

    void UpdateSuspicious()
    {
        if (_perception.HasDirectVisual() || _perception.IsFullyAware)
        {
            ChangeState(EnemyState.Alerted);
            return;
        }

        if (!_perception.IsSuspicious)
        {
            ChangeState(EnemyState.Patrol);
            return;
        }

        if (_perception.HasLastKnownPosition)
        {
            _suspiciousPoint.position = _perception.LastKnownPosition;
            _steering.SetTarget(_suspiciousPoint);
        }
    }

    void UpdateAlerted()
    {
        if (!player)
        {
            player = _perception.player;
            if (!player) return;
        }

        _steering.SetTarget(player);

        bool hasVisual = _perception.HasDirectVisual();

        if (hasVisual)
            _timeSinceLastSeen = 0f;
        else
            _timeSinceLastSeen += Time.deltaTime;

        if (!hasVisual && _timeSinceLastSeen >= loseTargetDelay)
        {
            if (_perception.HasLastKnownPosition)
                ChangeState(EnemyState.Suspicious);
            else
                ChangeState(EnemyState.Patrol);
        }
    }

    void OnDestroy()
    {
        if (_suspiciousPointGO)
            Destroy(_suspiciousPointGO);
    }

    void OnDisable()
    {
        if (_shooter)
            _shooter.StopFiring();
    }
}
