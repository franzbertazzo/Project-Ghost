using UnityEditor;
using UnityEngine;

namespace Game.Player.EditorTools
{
    /// <summary>
    /// Wires the versus HUD into the player prefab: enemy diamonds on the camera, and the ring
    /// radar switched over to tracking rockets.
    ///
    /// Done as a menu tool rather than by hand because both changes live deep in a large prefab,
    /// and one of them is a serialized bool that is currently OFF - a code default cannot fix that,
    /// since the serialized value always wins. Running this is the difference between the feature
    /// existing and the feature appearing.
    ///
    /// Menu: Tools > Ghost > Setup HUD
    /// </summary>
    public static class HudSetup
    {
        private const string PlayerPrefabPath = "Assets/Domains/Player/PlayerEntities.prefab";
        private const string EnemyMarkerPath = "Assets/Domains/UI/EnemyMarkerPrefab.prefab";
        private const string ProjectileMarkerPath = "Assets/Domains/UI/ProjectileMarkerPrefab.prefab";

        [MenuItem("Tools/Ghost/Setup HUD", priority = 3)]
        public static void SetupHud()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool changed = false;

            try
            {
                changed |= AddEnemyMarkers(root);
                changed |= EnableProjectileRings(root);
                changed |= RetireDashHologram(root);

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                    Debug.Log("[HudSetup] Player prefab updated.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            changed |= MatchRocketArrowToEnemyArrow();

            if (changed)
            {
                AssetDatabase.SaveAssets();
            }
            else
            {
                Debug.Log("[HudSetup] Nothing to change; the HUD is already wired.");
            }
        }

        /// <summary>
        /// Enemy diamonds go on the camera, which gives owner gating for free: NetworkPlayer
        /// disables the entire camera rig on remote players, so a remote copy never draws a HUD.
        /// </summary>
        private static bool AddEnemyMarkers(GameObject root)
        {
            var camera = root.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                Debug.LogWarning("[HudSetup] No camera in the player prefab; enemy markers not added.");
                return false;
            }

            if (camera.GetComponent<EnemyScreenMarkers>() != null)
                return false;

            camera.gameObject.AddComponent<EnemyScreenMarkers>();
            Debug.Log("[HudSetup] Added EnemyScreenMarkers to the player camera.");
            return true;
        }

        /// <summary>
        /// Turns on the radar's projectile rings.
        ///
        /// They were serialized off, which made sense while the layer they scanned held nothing.
        /// Now that rockets report themselves, this is the switch that makes an incoming rocket
        /// visible when it is behind you.
        /// </summary>
        private static bool EnableProjectileRings(GameObject root)
        {
            var radar = root.GetComponentInChildren<RingRadarController>(true);
            if (radar == null)
            {
                Debug.LogWarning("[HudSetup] No RingRadarController in the player prefab.");
                return false;
            }

            // Through SerializedObject because the field is private: the alternative is making it
            // public purely so an editor script can reach it, which would let anything write it.
            var serialized = new SerializedObject(radar);
            var property = serialized.FindProperty("enableProjectileRings");

            if (property == null)
            {
                Debug.LogWarning("[HudSetup] enableProjectileRings not found on the radar.");
                return false;
            }

            if (property.boolValue)
                return false;

            property.boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[HudSetup] Enabled projectile rings on the ring radar.");
            return true;
        }

        /// <summary>
        /// Switches off the world-space dash hologram on the character's back.
        ///
        /// Its job moved to the reticle. Two reasons it is not worth keeping in parallel: reading
        /// your charges meant looking away from the centre of the screen at your own back, and on
        /// every remote player the hologram was simply wrong - dash charges are local state that is
        /// never networked, so a remote copy sits at full charges forever.
        ///
        /// Disabled, not deleted. Re-tick the GameObject in the prefab to get it back.
        /// </summary>
        private static bool RetireDashHologram(GameObject root)
        {
            var dashUi = root.GetComponentInChildren<DashChargeUI>(true);
            if (dashUi == null)
                return false;

            // The canvas, not the component: leaving an enabled canvas with a disabled driver
            // would freeze the hologram at whatever it last showed rather than hide it.
            var canvas = dashUi.GetComponentInParent<Canvas>();
            var target = canvas != null ? canvas.gameObject : dashUi.gameObject;

            if (!target.activeSelf)
                return false;

            target.SetActive(false);
            Debug.Log($"[HudSetup] Disabled the back dash hologram ('{target.name}'); charges are " +
                      "on the reticle now. Re-enable that object to bring it back.");
            return true;
        }

        /// <summary>
        /// Makes the rocket arrow the same size as the enemy arrow, and lets the radar tint it.
        ///
        /// Both markers embed the same arrow prefab, but only the enemy one scaled it down - the
        /// rocket arrow was inheriting the source prefab's full size and drawing roughly eight
        /// times larger. The runtime scale that the radar applies is identical for both, so no
        /// amount of code could have evened them out: the difference lived in the prefab.
        ///
        /// The rocket marker also had no indicatorRenderer assigned, so UpdateIndicatorColor had
        /// nothing to tint and the arrow kept whatever colour its material shipped with. Wiring it
        /// is what makes the red actually apply.
        ///
        /// Copied from the enemy marker rather than hardcoded, so retuning one keeps them matched.
        /// </summary>
        private static bool MatchRocketArrowToEnemyArrow()
        {
            Vector3 reference;

            var enemy = PrefabUtility.LoadPrefabContents(EnemyMarkerPath);
            try
            {
                var enemyIndicator = FindIndicator(enemy.transform);
                if (enemyIndicator == null)
                {
                    Debug.LogWarning("[HudSetup] No Indicator under the enemy marker; arrow sizes " +
                                     "left alone.");
                    return false;
                }

                reference = enemyIndicator.localScale;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(enemy);
            }

            var projectile = PrefabUtility.LoadPrefabContents(ProjectileMarkerPath);
            bool changed = false;

            try
            {
                var indicator = FindIndicator(projectile.transform);
                if (indicator == null)
                {
                    Debug.LogWarning("[HudSetup] No Indicator under the rocket marker.");
                    return false;
                }

                if (indicator.localScale != reference)
                {
                    indicator.localScale = reference;
                    Debug.Log($"[HudSetup] Rocket arrow scaled to match the enemy arrow ({reference.x}).");
                    changed = true;
                }

                var marker = projectile.GetComponent<RingMarker>();
                var renderer = indicator.GetComponentInChildren<Renderer>(true);

                if (marker != null && renderer != null)
                {
                    var serialized = new SerializedObject(marker);
                    var property = serialized.FindProperty("indicatorRenderer");

                    if (property != null && property.objectReferenceValue != renderer)
                    {
                        property.objectReferenceValue = renderer;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        Debug.Log("[HudSetup] Wired the rocket arrow's renderer so the radar can " +
                                  "colour it.");
                        changed = true;
                    }
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(projectile, ProjectileMarkerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(projectile);
            }

            return changed;
        }

        private static Transform FindIndicator(Transform root)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && child.name == "Indicator")
                    return child;
            }

            return null;
        }
    }
}
