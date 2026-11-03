using System.IO;
using PurrNet;
using UnityEditor;
using UnityEngine;

namespace Game.Networking.EditorTools
{
    /// <summary>
    /// Turns PlayerEntities.prefab into a spawnable network prefab and wires it into the arena.
    ///
    /// Scripted rather than done by hand for the same reason the arena is generated: the wiring is
    /// several non-obvious steps (identity on the root, transform sync on the body, a prefab
    /// registry asset that PurrNet needs but does not create itself) and any one of them missed
    /// produces a player that spawns but never moves, with no error explaining why.
    ///
    /// Safe to re-run. Every step checks before it adds.
    ///
    /// Menu: Tools > Ghost > Setup Networked Player
    /// </summary>
    public static class NetworkedPlayerSetup
    {
        private const string PlayerPrefabPath = "Assets/Domains/Player/PlayerEntities.prefab";
        private const string NetworkPrefabsPath = "Assets/Domains/General/Networking/NetworkPrefabs.asset";
        private const string ArenaScenePath = "Assets/Scenes/Arena_Test.unity";
        private const string ScanFolder = "Assets/Domains";
        private const string BodyChildName = "Player";

        /// <summary>Interpolation buffer depth, in ticks. NetworkDash is kept in sync with this.</summary>
        private const int MinBuffer = 2;
        private const int MaxBuffer = 6;

        [MenuItem("Tools/Ghost/Setup Networked Player", priority = 1)]
        public static void SetupNetworkedPlayer()
        {
            Debug.Log($"[NetworkPlayerSetup] Setup requested. Target: {PlayerPrefabPath}");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[NetworkPlayerSetup] No prefab at {PlayerPrefabPath}.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool changed = false;

            try
            {
                changed |= EnsureNetworkPlayer(root);
                changed |= EnsureTransformSyncOnBody(root);
                changed |= EnsureAimTargetSurvivesCameraShutdown(root);
                changed |= EnsureNetworkAim(root);
                changed |= EnsureNetworkDash(root);
                changed |= EnsureComponent<PlayerVitals>(root, "PlayerVitals (alive/dead state)");
                changed |= EnsureNetworkAnimators(root);

                // Last, so every component it touches definitely exists.
                SyncDashBufferTicks(root, MinBuffer);

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var prefabs = EnsureNetworkPrefabsAsset();
            RegisterPrefab(prefabs, prefab);
            AssignPrefabsToManagers(prefabs);

            AssetDatabase.SaveAssets();

            Debug.Log(changed
                ? $"[NetworkPlayerSetup] {Path.GetFileName(PlayerPrefabPath)} is now a network prefab and is registered."
                : $"[NetworkPlayerSetup] {Path.GetFileName(PlayerPrefabPath)} was already set up; registry refreshed.");

            Debug.Log($"[NetworkPlayerSetup] Next: open {ArenaScenePath} and confirm the PlayerSpawner " +
                      "on the --- NETWORK --- object has this prefab assigned.");
        }

        /// <summary>
        /// Ownership lives on the spawn root. PlayerEntities carries only a Transform, but it is
        /// what gets instantiated, so it is what has to be identifiable and ownable.
        ///
        /// NetworkPlayer supplies that identity on its own - NetworkBehaviour derives from
        /// NetworkIdentity. Adding a bare NetworkIdentity alongside it would put TWO identities on
        /// one GameObject, and `GetComponent&lt;NetworkIdentity&gt;()` in the spawner could then hand
        /// ownership to the one that isn't doing the owner gating: every client would see itself as
        /// a remote player, kill its own camera, and stare at a black screen.
        /// </summary>
        static bool EnsureNetworkPlayer(GameObject root)
        {
            if (root.GetComponent<NetworkPlayer>() != null)
            {
                WarnOnRedundantIdentity(root);
                return false;
            }

            root.AddComponent<NetworkPlayer>();
            Debug.Log("[NetworkPlayerSetup] Added NetworkPlayer to the root (it is the NetworkIdentity too).");
            WarnOnRedundantIdentity(root);
            return true;
        }

        /// <summary>
        /// Warns only about a BARE NetworkIdentity sitting alongside the NetworkBehaviours.
        ///
        /// The exact-type test matters: NetworkBehaviour derives from NetworkIdentity, so
        /// GetComponents&lt;NetworkIdentity&gt;() also returns NetworkPlayer, NetworkAim, NetworkDash,
        /// PlayerVitals and RocketLauncher. Several NetworkBehaviours on one object is normal -
        /// each gets its own component index - and counting them was a false alarm.
        /// </summary>
        static void WarnOnRedundantIdentity(GameObject root)
        {
            int bare = 0;
            foreach (var identity in root.GetComponents<NetworkIdentity>())
            {
                if (identity.GetType() == typeof(NetworkIdentity))
                    bare++;
            }

            if (bare == 0)
                return;

            Debug.LogWarning($"[NetworkPlayerSetup] The root has {bare} plain NetworkIdentity " +
                             "component(s) alongside NetworkPlayer. Remove them, or ownership may " +
                             "be assigned to the wrong one.");
        }

        /// <summary>
        /// The root never moves - the Rigidbody body is a child, and the camera rig is its sibling
        /// so it does not inherit body roll. Syncing the root would therefore sync nothing, so the
        /// NetworkTransform goes on the body instead.
        /// </summary>
        static bool EnsureTransformSyncOnBody(GameObject root)
        {
            var body = FindBody(root);
            if (body == null)
            {
                Debug.LogError($"[NetworkPlayerSetup] No '{BodyChildName}' child with a Rigidbody found. " +
                               "Transform sync not added - the prefab layout must have changed.");
                return false;
            }

            var existing = body.GetComponent<NetworkTransform>();
            bool added = false;

            if (existing == null)
            {
                existing = body.AddComponent<NetworkTransform>();
                Debug.Log($"[NetworkPlayerSetup] Added NetworkTransform to '{body.name}' (the Rigidbody body).");
                added = true;
            }

            return TuneInterpolationBuffer(existing) || added;
        }

        /// <summary>
        /// Deepens the interpolation buffer, which PurrNet ships at min 1 / max 2.
        ///
        /// That default has no slack. Interpolated.Add discards samples and resets its timer the
        /// moment the buffer is full, and Advance freezes on the last value when it runs dry - so
        /// jittery delivery produces freeze, jump, freeze, jump. At a 14 m/s cruise each artefact
        /// is a couple of centimetres and invisible. At the 150 m/s dash speed each one is 2.5m or
        /// more, which reads as the body rubber-banding through the dash twice.
        ///
        /// Deeper buffer costs latency: min 2 at 60Hz means remote bodies render ~33ms further in
        /// the past. NetworkDash's lead offset is what buys that back for the moment it matters.
        /// </summary>
        static bool TuneInterpolationBuffer(NetworkTransform transform)
        {
            var serialized = new SerializedObject(transform);
            var min = serialized.FindProperty("_minBufferSize");
            var max = serialized.FindProperty("_maxBufferSize");

            if (min == null || max == null)
            {
                Debug.LogWarning("[NetworkPlayerSetup] Could not find NetworkTransform buffer fields; " +
                                 "set Min/Max Buffer Size by hand if dashes rubber-band.");
                return false;
            }

            if (min.intValue == MinBuffer && max.intValue == MaxBuffer)
                return false;

            Debug.Log($"[NetworkPlayerSetup] Interpolation buffer {min.intValue}/{max.intValue} " +
                      $"-> {MinBuffer}/{MaxBuffer} to absorb jitter at dash speed.");

            min.intValue = MinBuffer;
            max.intValue = MaxBuffer;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return true;
        }

        /// <summary>
        /// NetworkDash has to know the interpolation buffer depth to compute its lead, and
        /// NetworkTransform does not expose it publicly. Rather than let two numbers drift apart
        /// silently - which would show up as dashes that lead slightly wrong and no clue why -
        /// the tool that sets one sets the other.
        /// </summary>
        static void SyncDashBufferTicks(GameObject root, int bufferTicks)
        {
            var dash = root.GetComponentInChildren<NetworkDash>(true);
            if (dash == null)
                return;

            var serialized = new SerializedObject(dash);
            var property = serialized.FindProperty("interpolationBufferTicks");
            if (property == null || property.intValue == bufferTicks)
                return;

            property.intValue = bufferTicks;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[NetworkPlayerSetup] NetworkDash buffer ticks -> {bufferTicks} (matches NetworkTransform).");
        }

        /// <summary>
        /// Moves the aim Target out of the CameraRig branch and onto the player root.
        ///
        /// The spine's MultiAimConstraint aims at this transform, but it was parented under
        /// CameraOffset - inside the branch that gets switched off on remote players. A disabled
        /// parent means a frozen target and a spine bent toward a stale point on every screen but
        /// the owner's.
        ///
        /// Reparenting is safe because Aim.cs writes the target's *world* position every frame, so
        /// it never depended on inheriting the camera's transform. Conceptually it belongs here
        /// anyway: where a player aims is player state, not camera state, and P3 needs it for
        /// rocket direction.
        /// </summary>
        static bool EnsureAimTargetSurvivesCameraShutdown(GameObject root)
        {
            var aim = root.GetComponentInChildren<Aim>(true);
            if (aim == null || aim.aimIndicatorPrefab == null)
            {
                Debug.LogWarning("[NetworkPlayerSetup] No Aim component with a target assigned; " +
                                 "aim replication may not work.");
                return false;
            }

            var target = aim.aimIndicatorPrefab.transform;
            if (target.parent == root.transform)
                return false;

            var formerParent = target.parent != null ? target.parent.name : "(none)";
            target.SetParent(root.transform, worldPositionStays: true);

            Debug.Log($"[NetworkPlayerSetup] Moved aim target '{target.name}' from '{formerParent}' " +
                      "to the player root so it survives the camera rig being disabled.");
            return true;
        }

        /// <summary>
        /// Adds a NetworkAnimator beside each active Animator and points it at that Animator.
        ///
        /// With autoSyncParameters on (its default) this replicates float and bool parameters with
        /// no gameplay code change at all - PlayerAnimationStateController's SetFloat calls are
        /// picked up by polling. It also gives the controller something to route dash CrossFades
        /// through, which parameters alone cannot carry.
        ///
        /// Inactive model variants are skipped: the prefab carries a legacy 3DModelOld alongside
        /// the live 3DModel, and syncing an animator nobody renders is wasted bandwidth.
        /// </summary>
        static bool EnsureNetworkAnimators(GameObject root)
        {
            bool changed = false;

            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (!animator.gameObject.activeInHierarchy)
                {
                    Debug.Log($"[NetworkPlayerSetup] Skipped inactive animator on '{animator.name}'.");
                    continue;
                }

                var networkAnimator = animator.GetComponent<NetworkAnimator>();
                if (networkAnimator == null)
                {
                    networkAnimator = animator.gameObject.AddComponent<NetworkAnimator>();
                    Debug.Log($"[NetworkPlayerSetup] Added NetworkAnimator to '{animator.name}'.");
                    changed = true;
                }

                if (networkAnimator.animator != animator)
                {
                    networkAnimator.SetAnimator(animator);
                    EditorUtility.SetDirty(networkAnimator);
                    changed = true;
                }

                // Wire the controller on the same object so dash CrossFades replicate.
                var controller = animator.GetComponent<PlayerAnimationStateController>();
                if (controller != null)
                {
                    var serialized = new SerializedObject(controller);
                    var property = serialized.FindProperty("networkAnimator");
                    if (property != null && property.objectReferenceValue == null)
                    {
                        property.objectReferenceValue = networkAnimator;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        Debug.Log($"[NetworkPlayerSetup] Routed '{controller.name}' dash CrossFades through NetworkAnimator.");
                        changed = true;
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// Replicates dash impulses so remote players see a dash start on time instead of gliding
        /// into it several metres late. See NetworkDash for why the interpolated stream alone is
        /// not enough at 150 m/s.
        /// </summary>
        /// <summary>Adds a component to the root if it is not already there.</summary>
        static bool EnsureComponent<T>(GameObject root, string description) where T : Component
        {
            if (root.GetComponent<T>() != null)
                return false;

            root.AddComponent<T>();
            Debug.Log($"[NetworkPlayerSetup] Added {description}.");
            return true;
        }

        static bool EnsureNetworkDash(GameObject root)
        {
            if (root.GetComponent<NetworkDash>() != null)
                return false;

            root.AddComponent<NetworkDash>();
            Debug.Log("[NetworkPlayerSetup] Added NetworkDash (replicates dash impulses).");
            return true;
        }

        static bool EnsureNetworkAim(GameObject root)
        {
            if (root.GetComponent<NetworkAim>() != null)
                return false;

            root.AddComponent<NetworkAim>();
            Debug.Log("[NetworkPlayerSetup] Added NetworkAim (replicates the spine aim pose).");
            return true;
        }

        static GameObject FindBody(GameObject root)
        {
            var named = root.transform.Find(BodyChildName);
            if (named != null && named.GetComponent<Rigidbody>() != null)
                return named.gameObject;

            // Fall back to whatever actually carries the Rigidbody, so a rename doesn't break this.
            var rigidbody = root.GetComponentInChildren<Rigidbody>(true);
            return rigidbody != null ? rigidbody.gameObject : null;
        }

        /// <summary>
        /// PurrNet needs a NetworkPrefabs registry to map a prefab to a network id, and does not
        /// create one on its own - its inspector blocks with a setup prompt until one exists.
        /// </summary>
        static NetworkPrefabs EnsureNetworkPrefabsAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(NetworkPrefabsPath);
            if (existing != null)
                return existing;

            var asset = ScriptableObject.CreateInstance<NetworkPrefabs>();
            asset.autoGenerate = true;
            asset.networkOnly = true;

            // The scan folder is mandatory, not optional: with autoGenerate on and no folder,
            // NetworkPrefabs.Generate() CLEARS the list. Pointing it at Assets/Domains means every
            // future networked prefab - projectiles, pickups - registers itself by existing,
            // and networkOnly keeps the non-networked enemy and FX prefabs out.
            asset.folder = AssetDatabase.LoadAssetAtPath<Object>(ScanFolder);
            if (asset.folder == null)
                Debug.LogWarning($"[NetworkPlayerSetup] Scan folder {ScanFolder} not found; " +
                                 "set Folder on the NetworkPrefabs asset by hand or the list will stay empty.");

            Directory.CreateDirectory(Path.GetDirectoryName(NetworkPrefabsPath) ?? "Assets");
            AssetDatabase.CreateAsset(asset, NetworkPrefabsPath);
            Debug.Log($"[NetworkPlayerSetup] Created {NetworkPrefabsPath} scanning {ScanFolder}.");
            return asset;
        }

        /// <summary>
        /// Registration is by folder scan, not by manual list entry. A hand-added entry would be
        /// wiped the next time PurrNet regenerates the list, which is the kind of failure that
        /// shows up days later as "the player just stopped spawning".
        /// </summary>
        static void RegisterPrefab(NetworkPrefabs prefabs, GameObject prefab)
        {
            if (prefabs == null)
                return;

            if (prefabs.folder == null)
            {
                prefabs.folder = AssetDatabase.LoadAssetAtPath<Object>(ScanFolder);
                EditorUtility.SetDirty(prefabs);
            }

            prefabs.Generate();
            prefabs.Refresh();
            EditorUtility.SetDirty(prefabs);

            foreach (var entry in prefabs.prefabs)
            {
                if (entry.prefab == prefab)
                {
                    Debug.Log($"[NetworkPlayerSetup] {prefab.name} is registered as a network prefab.");
                    return;
                }
            }

            Debug.LogError($"[NetworkPlayerSetup] {prefab.name} did NOT end up in the network prefab list. " +
                           $"Check that it lives under {ScanFolder} and has a NetworkIdentity on its root; " +
                           "without registration it cannot be spawned over the network.");
        }

        /// <summary>
        /// Assigns the registry to every NetworkManager asset in the project's scenes that does not
        /// have one. Only touches the arena scene, which is the one this project tests with.
        /// </summary>
        static void AssignPrefabsToManagers(NetworkPrefabs prefabs)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(ArenaScenePath);
            if (!scene.isLoaded)
            {
                Debug.LogWarning($"[NetworkPlayerSetup] {ArenaScenePath} is not open, so its NetworkManager " +
                                 "was not updated. Open the scene and run this again, or assign " +
                                 "Network Prefabs on the manager by hand.");
                return;
            }

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                var manager = rootObject.GetComponentInChildren<NetworkManager>(true);
                if (manager == null)
                    continue;

                var serialized = new SerializedObject(manager);
                var property = serialized.FindProperty("_networkPrefabs");
                if (property != null && property.objectReferenceValue == null)
                {
                    property.objectReferenceValue = prefabs;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    Debug.Log("[NetworkPlayerSetup] Assigned the prefab registry to the arena's NetworkManager.");
                }

                var spawner = rootObject.GetComponentInChildren<PlayerSpawner>(true);
                if (spawner != null)
                {
                    var spawnerSerialized = new SerializedObject(spawner);
                    var prefabProperty = spawnerSerialized.FindProperty("playerPrefab");
                    if (prefabProperty != null && prefabProperty.objectReferenceValue == null)
                    {
                        prefabProperty.objectReferenceValue =
                            AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                        spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                        Debug.Log("[NetworkPlayerSetup] Assigned the player prefab to the PlayerSpawner.");
                    }
                }
            }
        }
    }
}
