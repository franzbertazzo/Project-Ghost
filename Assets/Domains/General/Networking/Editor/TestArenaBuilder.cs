using System.IO;
using PurrNet;
using PurrNet.Transports;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Networking.EditorTools
{
    /// <summary>
    /// Generates the throwaway test arena used for all multiplayer work.
    ///
    /// This is a generator rather than a hand-built scene on purpose: it keeps the network rig
    /// (NetworkManager flags, transport wiring, latency sim, bootstrap) reproducible, so the arena
    /// can be deleted and rebuilt whenever it accumulates junk without losing the setup.
    ///
    /// Menu: Tools > Ghost > Build Test Arena
    /// </summary>
    public static class TestArenaBuilder
    {
        private const string ScenePath = "Assets/Scenes/Arena_Test.unity";
        /// <summary>
        /// Sized against the dash, not picked round. surfaceDashSpeed is 150 m/s held for
        /// dashGraceTime 0.2s, so a single surface dash covers ~30m. A 60m box was one dash across
        /// - no room to fight in. 150m gives roughly five dash-lengths corner to corner while
        /// staying an enclosed volume rather than open space.
        ///
        /// Retune this when dash speed changes; the two numbers belong together.
        /// </summary>
        private const float ArenaSize = 150f;

        private const float WallThickness = 2f;
        /// <summary>
        /// 60Hz, not PurrNet's default 20 or a conservative 30.
        ///
        /// NetworkTransform buffers interpolation in *ticks*, and on a listen server a
        /// client-to-client path pays that buffer twice: the host buffers the owner's data when it
        /// arrives, then relays from the buffered state, and the receiving client buffers it again.
        /// With a 1-2 tick buffer that is 66-133ms of pure buffering at 30Hz before any real
        /// network time - visible as client-to-client lag that host-to-client does not have, and
        /// that turning the latency simulation off does not fix.
        ///
        /// The buffer is already at its floor, so halving the tick duration is what halves the
        /// delay. At 4-6 players a listen server carries 60Hz comfortably.
        /// </summary>
        private const int TickRate = 60;
        private const int SpawnPointCount = 6;

        [MenuItem("Tools/Ghost/Build Test Arena", priority = 0)]
        public static void BuildTestArena()
        {
            // Logged before anything can bail, so "did the tool actually run?" is answerable from
            // the Console instead of by comparing file timestamps.
            Debug.Log($"[TestArena] Build requested. Target: {ScenePath}");

            if (File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Rebuild test arena?",
                    $"{ScenePath} already exists.\n\nRebuilding replaces it completely. Anything you " +
                    "added to that scene by hand will be lost.",
                    "Rebuild", "Cancel");

                if (!overwrite)
                    return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            CreateArenaShell();
            CreateCover();
            CreateBlastZone();
            var spawnPoints = CreateSpawnPoints();
            CreateNetworkRig();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);

            Debug.Log($"[TestArena] Built {ScenePath} - {ArenaSize}m cube, {spawnPoints} spawn points, " +
                      $"NetworkManager at {TickRate}Hz with auto-start disabled.");

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
        }

        static void CreateLighting()
        {
            var light = new GameObject("Directional Light");
            var component = light.AddComponent<Light>();
            component.type = LightType.Directional;
            component.intensity = 1.1f;
            component.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            // Zero-g interiors read badly with no fill, and the test arena has no reflection probes.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.24f, 0.26f, 0.32f);
        }

        /// <summary>
        /// A hollow box: six inward-facing slabs. Every face is dashable surface, which is the whole
        /// point of the arena for P1 and P2.
        /// </summary>
        /// <summary>
        /// No shell at all - a compact cluster floating in open space.
        ///
        /// The arena began as a sealed box so every face was dashable. That is incompatible with
        /// ring-out scoring twice over: there is nowhere to be knocked out to, and even large
        /// floor/ceiling slabs still read as a box and block launches downward and upward.
        ///
        /// So the geometry is now a single island. It is small relative to the dash (~30m), which
        /// keeps surface dashing central - you are never far from something to kick off - while
        /// leaving every direction open to be launched through. The BlastZone sits far outside it.
        /// </summary>
        static void CreateArenaShell()
        {
            var root = new GameObject("Arena").transform;

            // Sized against the dash rather than picked round: roughly two dash-lengths across,
            // so crossing the island is one committed move and leaving it is a decision.
            float u = ArenaSize / 150f;

            // A rough 3D cluster, deliberately not a plane. In zero-g there is no "up", so
            // geometry stacked on one axis would quietly reintroduce a floor.
            CreateSlab(root, "Core", new Vector3(0, 0, 0), new Vector3(26, 26, 26) * u);
            CreateSlab(root, "Spur +X", new Vector3(28, 6, -4) * u, new Vector3(22, 12, 14) * u);
            CreateSlab(root, "Spur -X", new Vector3(-30, -8, 6) * u, new Vector3(20, 14, 16) * u);
            CreateSlab(root, "Spur +Y", new Vector3(4, 28, 8) * u, new Vector3(16, 18, 16) * u);
            CreateSlab(root, "Spur -Y", new Vector3(-6, -30, -8) * u, new Vector3(18, 16, 14) * u);
            CreateSlab(root, "Spur +Z", new Vector3(8, -6, 30) * u, new Vector3(14, 20, 18) * u);
            CreateSlab(root, "Spur -Z", new Vector3(-8, 10, -32) * u, new Vector3(16, 14, 20) * u);
        }

        /// <summary>
        /// The kill boundary. Sized well outside the geometry so a launch has room to read as a
        /// launch before it becomes a death.
        /// </summary>
        static void CreateBlastZone()
        {
            var zone = new GameObject("--- BLAST ZONE ---");
            zone.AddComponent<Game.Match.BlastZone>();
        }

        /// <summary>
        /// Interior blocks. Not level design - just enough geometry that a dash has something to
        /// push off in the middle of the volume, and that players can break line of sight.
        /// </summary>
        static void CreateCover()
        {
            var root = new GameObject("Cover").transform;

            // Small debris orbiting the island: extra dash surfaces and sightline breaks, placed
            // out where a launched player might still catch one and save themselves.
            float u = ArenaSize / 150f;

            CreateSlab(root, "Debris A", new Vector3(52, 20, 18) * u, new Vector3(8, 8, 8) * u);
            CreateSlab(root, "Debris B", new Vector3(-48, 26, -22) * u, new Vector3(7, 9, 7) * u);
            CreateSlab(root, "Debris C", new Vector3(18, -52, 30) * u, new Vector3(9, 7, 9) * u);
            CreateSlab(root, "Debris D", new Vector3(-24, -44, -40) * u, new Vector3(8, 8, 10) * u);
            CreateSlab(root, "Debris E", new Vector3(40, 8, -50) * u, new Vector3(10, 8, 8) * u);
        }

        static void CreateSlab(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            slab.transform.localPosition = position;
            slab.transform.localScale = scale;
            slab.isStatic = true;
            slab.layer = ResolveDashSurfaceLayer();
        }

        /// <summary>
        /// PlayerZeroGMovement's surfaceMask is set to the DashSurface layer, so geometry left on
        /// Default is invisible to the surface-dash check: you can fly into a wall and never kick
        /// off it, with no error to explain why. Every dashable surface in the arena has to be on
        /// this layer.
        /// </summary>
        static int ResolveDashSurfaceLayer()
        {
            const string layerName = "DashSurface";

            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
                return layer;

            Debug.LogError($"[TestArena] No '{layerName}' layer in this project. Arena geometry will be " +
                           "on Default and surface dashing will not work. Add the layer and rebuild.");
            return 0;
        }

        /// <summary>
        /// Spawn points on the Default layer as plain transforms. P1 wires these into the spawner;
        /// six of them so the 6-player ceiling can be tested without rebuilding the scene.
        /// </summary>
        static int CreateSpawnPoints()
        {
            var root = new GameObject("SpawnPoints").transform;
            float r = ArenaSize * 0.32f;

            for (int i = 0; i < SpawnPointCount; i++)
            {
                float angle = i * Mathf.PI * 2f / SpawnPointCount;
                // Alternate height so spawns are distributed in 3D, not around a flat ring.
                float y = (i % 2 == 0) ? -r * 0.5f : r * 0.5f;

                var point = new GameObject($"Spawn {i + 1}");
                point.transform.SetParent(root, false);
                point.transform.localPosition = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
                point.transform.rotation = Quaternion.LookRotation(-point.transform.localPosition.normalized);
            }

            return SpawnPointCount;
        }

        static void CreateNetworkRig()
        {
            var go = new GameObject("--- NETWORK ---");

            var transport = go.AddComponent<UDPTransport>();
            var manager = go.AddComponent<NetworkManager>();

            var serialized = new SerializedObject(manager);
            serialized.FindProperty("_transport").objectReferenceValue = transport;
            serialized.FindProperty("_tickRate").intValue = TickRate;

            // Auto-start is disabled so MultiplayerBootstrap is the single place that decides who
            // hosts. PurrNet's default flags would make every MPPM virtual player its own host,
            // because its clone detection looks for a symlinked Assets folder that MPPM never makes.
            serialized.FindProperty("_startServerFlags").intValue = 0;
            serialized.FindProperty("_startClientFlags").intValue = 0;

            var rules = serialized.FindProperty("_networkRules");
            if (rules != null && rules.objectReferenceValue == null)
            {
                // ServerOwner: the server spawns and despawns, but an owner may drive its own
                // object. That matches P1's client-authoritative movement, per CLAUDE.md's "start
                // permissive, tighten once the core is playable".
                //
                // Swap this to ServerStrict.asset at P4, when damage and kills become authoritative.
                // PurrNet has no rules asset by default and its inspector blocks on a setup prompt
                // until one is assigned, so this is set here rather than left empty.
                var asset = AssetDatabase.LoadAssetAtPath<NetworkRules>(
                    "Assets/PurrNet/Defaults/NetworkRules/ServerOwner.asset");
                if (asset != null)
                    rules.objectReferenceValue = asset;
                else
                    Debug.LogWarning("[TestArena] ServerOwner.asset not found - assign Network Rules by hand.");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            go.AddComponent<MultiplayerBootstrap>();
            go.AddComponent<NetworkLatencySimulator>();
            go.AddComponent<NetworkStatusHud>();

            var spawner = go.AddComponent<PlayerSpawner>();
            AssignPlayerPrefab(spawner);

            // Mouse look is unusable without this. It lives on the scene rig rather than the
            // player prefab so the cursor is captured from the moment the scene runs, before
            // anyone has spawned, and so it needs no owner gating - each instance has exactly one.
            //
            // It releases the cursor on focus loss, which is what makes alt-tabbing between MPPM
            // instances workable. In the editor, Escape also releases it.
            go.AddComponent<CursorLock>();

            // MatchManager is a NetworkBehaviour, so it is a scene NetworkIdentity. It gets its
            // own GameObject rather than riding on the NetworkManager: the manager object is
            // special to PurrNet, and putting a scene identity on it coincided exactly with
            // clients no longer being able to complete a join.
            var matchObject = new GameObject("--- MATCH ---");
            var match = matchObject.AddComponent<Game.Match.MatchManager>();
            AssignMatchRules(match, spawner);
        }

        /// <summary>
        /// Wires the player prefab if it already exists. The spawner also resolves its spawn points
        /// by name at runtime, so a rebuilt arena needs no manual wiring at all.
        /// </summary>
        static void AssignPlayerPrefab(PlayerSpawner spawner)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Domains/Player/PlayerEntities.prefab");
            if (prefab == null)
                return;

            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("playerPrefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Creates the Frag Limit ruleset asset if it does not exist and wires the manager to it.
        /// Frag Limit first because it is the forgiving mode: continuous respawns make playtesting
        /// far easier than one-life rounds while the game is still being built.
        /// </summary>
        static void AssignMatchRules(Game.Match.MatchManager match, PlayerSpawner spawner)
        {
            const string rulesPath = "Assets/Domains/General/Match/FragLimit.asset";

            var rules = AssetDatabase.LoadAssetAtPath<Game.Match.MatchRules>(rulesPath);
            if (rules == null)
            {
                rules = ScriptableObject.CreateInstance<Game.Match.MatchRules>();
                rules.displayName = "Frag Limit";
                rules.mode = Game.Match.MatchMode.FragLimit;
                rules.scoreToWin = 10;
                rules.respawnDelay = 3f;

                Directory.CreateDirectory(Path.GetDirectoryName(rulesPath) ?? "Assets");
                AssetDatabase.CreateAsset(rules, rulesPath);
                Debug.Log($"[TestArena] Created {rulesPath}.");
            }

            var serialized = new SerializedObject(match);
            serialized.FindProperty("rules").objectReferenceValue = rules;
            serialized.FindProperty("spawner").objectReferenceValue = spawner;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var entry in scenes)
            {
                if (entry.path == path)
                    return;
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updated, 0);
            updated[scenes.Length] = new EditorBuildSettingsScene(path, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}
