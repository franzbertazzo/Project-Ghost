// using UnityEditor;
// using UnityEngine;

// [RequireComponent(typeof(Rigidbody))]
// public class SteeringBehavior : MonoBehaviour
// {
//     [SerializeField] private Transform[] _targets;
//     [SerializeField] private Rigidbody _rigidbody;

//     [Header("Seek and Arrive")]
//     [SerializeField] private float _slowRadius = 3f;
//     [SerializeField] private float _minDistanceRadius = 1.5f;
//     [SerializeField] private float _maxSpeed = 10f;
//     [SerializeField] private float _minSpeed = 0f;

//     [Header("Avoid")]
//     [SerializeField] private float _avoidWeight = 3f;
//     private float lookahead = 5f;
//     [SerializeField] private float obstacleAvoidRadius = 1f;

//     [Header("Wander")]
//     [SerializeField] private float _wanderingUpdateInterval = 2f;
//     [SerializeField] private float _wanderingRadius = 2f;
//     [SerializeField] public float _wanderWeight = 1f;

//     [Header("Projectile testing")]
//     public bool shoot = false;
//     public GameObject projectile;
//     public Transform projectileOrigin;
//     public float shootDistance = 300;
//     public float shootInterval = 0.3f;
//     private float shootTimer = 0;

//     private Vector3 _desiredDirection;
//     private Vector3 _steeringForce;

//     private Vector3 _avoidTarget;
//     private bool _evading = false;

//     private Vector3 _lastSteeringForce;
//     private Vector3 _lastWanderDirection;
//     private Vector3 randomSphereDirection = Vector3.zero;

//     void Awake()
//     {
//         _rigidbody = GetComponent<Rigidbody>();
//     }

//     void Start()
//     {
//         InvokeRepeating(nameof(UpdateRandomSphereDirection), 0f, _wanderingUpdateInterval);
//         randomSphereDirection = transform.forward;
//     }

//     // =========================
//     // INTEGRATION API (SAFE)
//     // =========================
//     public void SetTarget(Transform target)
//     {
//         if (target == null)
//         {
//             _targets = null;
//             return;
//         }

//         _targets = new Transform[1];
//         _targets[0] = target;
//     }

//     void FixedUpdate()
//     {
//         // No target = no steering (important for stability)
//         if (_targets == null || _targets.Length == 0)
//         {
//             return;
//         }

//         _steeringForce = Vector3.zero;
//         _desiredDirection = Vector3.zero;

//         float lookahead = _rigidbody.linearVelocity.magnitude;
//         float avoidDistance = lookahead / 5f;

//         Vector3 averageTargetsPosition = GetAverageTargetPosition();

//         // =========================
//         // WANDER
//         // =========================
//         _lastWanderDirection = Vector3.Slerp(
//             _lastWanderDirection,
//             randomSphereDirection,
//             Time.fixedDeltaTime
//         );

//         Vector3 wanderPosition =
//             transform.position +
//             _lastWanderDirection +
//             transform.forward * 3f;

//         // Stop evading close to evade position
//         if (_evading && (transform.position - _avoidTarget).magnitude < 2f)
//         {
//             _evading = false;
//         }

//         // =========================
//         // OBSTACLE AVOIDANCE (ORIGINAL)
//         // =========================
//         RaycastHit hit;
//         Vector3 p1 = transform.position;

//         if (Physics.SphereCast(
//             p1,
//             obstacleAvoidRadius,
//             _rigidbody.linearVelocity,
//             out hit,
//             lookahead
//         ))
//         {
//             _avoidTarget = hit.point + (hit.normal * avoidDistance);
//             Debug.DrawLine(hit.point, _avoidTarget, Color.magenta, 0.1f);
//             _evading = true;
//         }

//         // Free path check
//         if (!Physics.SphereCast(
//             p1,
//             obstacleAvoidRadius,
//             transform.forward,
//             out hit,
//             (averageTargetsPosition - transform.position).magnitude
//         ))
//         {
//             _evading = false;
//         }

//         // =========================
//         // SHOOT TESTING (SAFE – NO TAGS)
//         // =========================
//         if (shoot)
//         {
//             if (shootTimer <= 0f)
//             {
//                 shootTimer = shootInterval;

//                 if (Physics.Raycast(
//                     p1 + projectileOrigin.forward * 2f,
//                     projectileOrigin.forward,
//                     out hit,
//                     shootDistance
//                 ))
//                 {
//                     Instantiate(
//                         projectile,
//                         projectileOrigin.position,
//                         projectileOrigin.rotation
//                     )
//                     .GetComponent<Rigidbody>()
//                     .AddForce(
//                         Random.onUnitSphere / 10f +
//                         projectileOrigin.forward * 100f,
//                         ForceMode.Impulse
//                     );
//                 }
//             }
//             else
//             {
//                 shootTimer -= Time.fixedDeltaTime;
//             }
//         }

//         // =========================
//         // APPLY STEERING (ORIGINAL)
//         // =========================
//         if (_evading)
//         {
//             _steeringForce += _avoidWeight * GetArriveForce(_avoidTarget);
//         }
//         else
//         {
//             _steeringForce += GetArriveForce(averageTargetsPosition);
//         }

//         _steeringForce += _wanderWeight * GetArriveForce(wanderPosition);

//         _steeringForce = Vector3.Slerp(
//             _lastSteeringForce,
//             _steeringForce,
//             5f * Time.fixedDeltaTime
//         );

//         _rigidbody.AddForce(_steeringForce, ForceMode.Force);

//         // Correct min speed
//         if (_rigidbody.linearVelocity.magnitude < _minSpeed)
//         {
//             _rigidbody.linearVelocity =
//                 _rigidbody.linearVelocity.normalized * _minSpeed;
//         }

//         _lastSteeringForce = _steeringForce;

//         // =========================
//         // ROTATION (ORIGINAL)
//         // =========================
//         Vector3 estimatedUp =
//             _steeringForce.normalized +
//             Vector3.up +
//             transform.up;

//         Quaternion newRotation =
//             Quaternion.LookRotation(_rigidbody.linearVelocity, estimatedUp);

//         transform.rotation = Quaternion.Slerp(
//             transform.rotation,
//             newRotation,
//             0.8f * lookahead * Time.fixedDeltaTime
//         );
//     }

//     Vector3 GetAverageTargetPosition()
//     {
//         Vector3 average = Vector3.zero;

//         foreach (Transform target in _targets)
//         {
//             if (target == null) continue;
//             Debug.DrawLine(transform.position, target.position, Color.white);
//             average += target.position;
//         }

//         return average / _targets.Length;
//     }

//     // Arrive = seek with slow radius
//     Vector3 GetArriveForce(Vector3 targetPosition)
//     {
//         Vector3 targetDirection = targetPosition - transform.position;
//         float targetDistance = targetDirection.magnitude;

//         float desiredSpeed =
//             _maxSpeed * ((targetDistance - _minDistanceRadius) / _slowRadius);

//         if (desiredSpeed > _maxSpeed)
//         {
//             desiredSpeed = _maxSpeed;
//         }
//         else if (desiredSpeed < _minSpeed)
//         {
//             desiredSpeed = _minSpeed;
//         }

//         Vector3 desiredVelocity = desiredSpeed * targetDirection.normalized;
//         _desiredDirection += desiredVelocity;

//         Debug.DrawRay(transform.position, desiredVelocity, Color.red);
//         return desiredVelocity - _rigidbody.linearVelocity;
//     }

//     void UpdateRandomSphereDirection()
//     {
//         randomSphereDirection = Random.onUnitSphere * _wanderingRadius;
//     }

//     void OnDrawGizmos()
//     {
//         Vector3 velocity;
//         Vector3 forward;

//         if (_rigidbody != null)
//         {
//             velocity = _rigidbody.linearVelocity;
//             forward = velocity.normalized;
//         }
//         else
//         {
//             velocity = transform.forward;
//             forward = velocity;
//         }

//         Vector3 start = transform.position;
//         Vector3 end = start + forward * lookahead;

//         Gizmos.color = Color.cyan;
//         Gizmos.DrawWireSphere(end, obstacleAvoidRadius);
//         Gizmos.DrawLine(start, end);

//         Handles.Label(transform.position + _desiredDirection, "Desired Direction");
//         Debug.DrawRay(transform.position, _desiredDirection, Color.red);

//         Handles.Label(transform.position + _steeringForce, $"SF:{_steeringForce.magnitude}");
//         Debug.DrawRay(transform.position, _steeringForce, Color.yellow);

//         Gizmos.color = Color.magenta;
//         if (_evading) Gizmos.DrawWireSphere(_avoidTarget, 1f);

//         Handles.Label(transform.position + velocity, $"V:{velocity.magnitude}");
//         Debug.DrawRay(transform.position, velocity, Color.green);
//     }
// }
