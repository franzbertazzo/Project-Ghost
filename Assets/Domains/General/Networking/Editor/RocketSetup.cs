using System.IO;
using Game.Weapons;
using PurrNet;
using UnityEditor;
using UnityEngine;

namespace Game.Networking.EditorTools
{
    /// <summary>
    /// Builds a placeholder rocket prefab and adds the launcher to the player.
    ///
    /// The rocket is generated rather than authored so the networking wiring (identity, transform
    /// sync, layer, no rigidbody) is reproducible while the art is still a capsule. Replace the
    /// visual later without touching any of it.
    ///
    /// Menu: Tools > Ghost > Setup Rockets
    /// </summary>
    public static class RocketSetup
    {
        private const string RocketPrefabPath = "Assets/Domains/Weapons/Rocket.prefab";
        private const string PlayerPrefabPath = "Assets/Domains/Player/PlayerEntities.prefab";

        [MenuItem("Tools/Ghost/Setup Rockets", priority = 2)]
        public static void SetupRockets()
        {
            Debug.Log("[RocketSetup] Setup requested.");

            var rocketPrefab = EnsureRocketPrefab();
            if (rocketPrefab == null)
                return;

            AttachLauncherToPlayer(rocketPrefab);
            AttachReticleToCamera();
            AssignDeathExplosion();

            AssetDatabase.SaveAssets();
            Debug.Log("[RocketSetup] Done. Re-run Tools > Ghost > Setup Networked Player so the " +
                      "rocket is picked up by the NetworkPrefabs folder scan.");
        }

        static GameObject EnsureRocketPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RocketPrefabPath);
            if (existing != null)
            {
                Debug.Log($"[RocketSetup] {RocketPrefabPath} already exists; left alone.");
                return existing;
            }

            // Empty root: its forward (+Z) IS the direction of travel, and Rocket.cs moves along
            // transform.forward. Keeping the root free of any mesh means the placeholder art can
            // be swapped without disturbing the axis the flight code depends on.
            var root = new GameObject("Rocket");

            // The capsule mesh runs along its own +Y, so the visual child is pitched 90 degrees to
            // line up with the root's forward. CreatePrimitive returns a bare object with no
            // children of its own - an earlier version of this tool assumed otherwise and threw.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(0.4f, 0.8f, 0.4f);

            // No collider anywhere: the host sweeps a SphereCast each frame rather than relying on
            // physics contacts, so a collider would only snag on players the rocket flies past.
            var collider = visual.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            root.AddComponent<Rocket>();
            root.AddComponent<NetworkTransform>();

            Directory.CreateDirectory(Path.GetDirectoryName(RocketPrefabPath) ?? "Assets");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RocketPrefabPath);
            Object.DestroyImmediate(root);

            Debug.Log($"[RocketSetup] Created {RocketPrefabPath} (placeholder capsule).");
            return prefab;
        }

        /// <summary>
        /// Puts the lock-on circle on the player camera. Owner gating comes free: NetworkPlayer
        /// disables the whole camera rig on remote players.
        /// </summary>
        static void AttachReticleToCamera()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool changed = false;

            try
            {
                var camera = root.GetComponentInChildren<Camera>(true);
                if (camera == null)
                {
                    Debug.LogWarning("[RocketSetup] No camera found; lock-on reticle not added.");
                    return;
                }

                if (camera.GetComponent<Game.Player.LockOnReticle>() == null)
                {
                    camera.gameObject.AddComponent<Game.Player.LockOnReticle>();
                    Debug.Log("[RocketSetup] Added LockOnReticle to the player camera.");
                    changed = true;
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Reuses an existing explosion for the death VFX. Placeholder art, but it means death
        /// reads instantly instead of a body silently vanishing.
        /// </summary>
        static void AssignDeathExplosion()
        {
            const string explosionPath =
                "Assets/Domains/Enemy/Turret/TurretComponents/Explosion large.prefab";

            var explosion = AssetDatabase.LoadAssetAtPath<GameObject>(explosionPath);
            if (explosion == null)
            {
                Debug.LogWarning($"[RocketSetup] No explosion at {explosionPath}; " +
                                 "assign death and rocket VFX by hand.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool changed = false;

            try
            {
                var vitals = root.GetComponent<Game.Networking.PlayerVitals>();
                if (vitals != null)
                {
                    var serialized = new SerializedObject(vitals);
                    var property = serialized.FindProperty("deathExplosionPrefab");
                    if (property != null && property.objectReferenceValue == null)
                    {
                        property.objectReferenceValue = explosion;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        Debug.Log("[RocketSetup] Assigned the death explosion.");
                        changed = true;
                    }
                }
                else
                {
                    Debug.LogWarning("[RocketSetup] No PlayerVitals yet; run Setup Networked Player first.");
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssignRocketExplosion(explosion);
        }

        static void AssignRocketExplosion(GameObject explosion)
        {
            var rocketRoot = PrefabUtility.LoadPrefabContents(RocketPrefabPath);

            try
            {
                var rocket = rocketRoot.GetComponent<Rocket>();
                if (rocket == null)
                    return;

                var serialized = new SerializedObject(rocket);
                var property = serialized.FindProperty("explosionPrefab");
                if (property == null || property.objectReferenceValue != null)
                    return;

                property.objectReferenceValue = explosion;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(rocketRoot, RocketPrefabPath);
                Debug.Log("[RocketSetup] Assigned the rocket explosion.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(rocketRoot);
            }
        }

        static void AttachLauncherToPlayer(GameObject rocketPrefab)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool changed = false;

            try
            {
                if (root.GetComponent<Blaster>() == null)
                {
                    root.AddComponent<Blaster>();
                    Debug.Log("[RocketSetup] Added Blaster (tap-fire laser).");
                    changed = true;
                }

                var launcher = root.GetComponent<RocketLauncher>();
                if (launcher == null)
                {
                    launcher = root.AddComponent<RocketLauncher>();
                    Debug.Log("[RocketSetup] Added RocketLauncher to the player root.");
                    changed = true;
                }

                var serialized = new SerializedObject(launcher);
                var prefabProperty = serialized.FindProperty("rocketPrefab");
                if (prefabProperty != null && prefabProperty.objectReferenceValue == null)
                {
                    prefabProperty.objectReferenceValue = rocketPrefab;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[RocketSetup] Assigned the rocket prefab to the launcher.");
                    changed = true;
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
