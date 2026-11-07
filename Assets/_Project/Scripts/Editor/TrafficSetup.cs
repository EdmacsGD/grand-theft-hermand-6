using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GTH6.EditorTools
{
    /// <summary>
    /// Phase 4 helpers.
    /// GTH6 > Roads > Create Test Road Grid: a 4 x 4 street grid with traffic lights, for testing without drawing roads.
    /// GTH6 > Traffic > Set Up Traffic In Scene: traffic car prefabs + a Traffic object (signals and manager) wired to your RoadNetwork.
    /// </summary>
    public static class TrafficSetup
    {
        const string PrefabFolder = "Assets/_Project/Prefabs/Vehicles";

        static readonly VehicleCategory[] TrafficMix =
        {
            VehicleCategory.Hatchback, VehicleCategory.Sedan, VehicleCategory.SUV,
            VehicleCategory.Van, VehicleCategory.Taxi, VehicleCategory.Sports
        };

        [MenuItem("GTH6/Roads/Create Test Road Grid")]
        static void CreateTestGridMenu()
        {
            RoadNetwork network = CreateTestGrid();
            Undo.RegisterCreatedObjectUndo(network.gameObject, "Create Test Road Grid");
            Selection.activeGameObject = network.gameObject;
            Debug.Log("[GTH6] Test road grid created: 16 nodes, 4 traffic lights, 80 m blocks. Next: GTH6 > Traffic > Set Up Traffic In Scene.");
        }

        /// <summary>Builds the grid north of the test course (z 40 to 280) and its road surface.</summary>
        public static RoadNetwork CreateTestGrid()
        {
            var root = new GameObject("RoadNetwork_TestGrid");
            RoadNetwork network = root.AddComponent<RoadNetwork>();
            network.roadMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Blockout_Ground.mat");

            const int count = 4;
            const float spacing = 80f;
            var grid = new RoadNode[count, count];
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    var go = new GameObject($"Node_{i}_{j}");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = new Vector3(-120f + i * spacing, 0f, 40f + j * spacing);
                    RoadNode node = go.AddComponent<RoadNode>();
                    bool inner = i > 0 && i < count - 1 && j > 0 && j < count - 1;
                    node.hasTrafficLight = inner;
                    grid[i, j] = node;
                }
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    bool edge = i == 0 || j == 0 || i == count - 1 || j == count - 1;
                    float speed = edge ? 60f : 50f;
                    if (i + 1 < count) Connect(grid[i, j], grid[i + 1, j], speed);
                    if (j + 1 < count) Connect(grid[i, j], grid[i, j + 1], speed);
                }
            }

            network.Rebuild();
            Mesh mesh = RoadMeshBuilder.Build(network);
            var surface = new GameObject("RoadSurface", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            surface.transform.SetParent(root.transform, false);
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            surface.GetComponent<MeshCollider>().sharedMesh = mesh;
            if (network.roadMaterial != null) surface.GetComponent<MeshRenderer>().sharedMaterial = network.roadMaterial;
            surface.isStatic = true;
            return network;
        }

        static void Connect(RoadNode a, RoadNode b, float speed)
        {
            a.SetLink(b, 1, speed);
            b.SetLink(a, 1, speed);
        }

        [MenuItem("GTH6/Traffic/Set Up Traffic In Scene")]
        static void SetUpTraffic()
        {
            RoadNetwork network = Object.FindFirstObjectByType<RoadNetwork>();
            if (network == null)
            {
                EditorUtility.DisplayDialog("Traffic", "No RoadNetwork in this scene. Draw roads (phase 2) or use GTH6 > Roads > Create Test Road Grid first.", "OK");
                return;
            }

            GameObject traffic = SetUpTraffic(network, out int types);
            if (traffic == null) return;
            Selection.activeGameObject = traffic;
            EditorUtility.DisplayDialog("Traffic", $"Traffic is set up with {types} car types. Press Play: cars appear 60 to 150 m from the player, out of view.", "OK");
        }

        /// <summary>Creates or updates the "Traffic" object for this network. Returns null if no prefabs could be made.</summary>
        public static GameObject SetUpTraffic(RoadNetwork network, out int carTypes)
        {
            List<GameObject> prefabs = EnsureTrafficPrefabs();
            carTypes = prefabs.Count;
            if (prefabs.Count == 0) return null;

            GameObject traffic = GameObject.Find("Traffic");
            if (traffic == null)
            {
                traffic = new GameObject("Traffic");
                Undo.RegisterCreatedObjectUndo(traffic, "Set Up Traffic");
            }

            // No "??" here: in the editor a missing component is a fake null object that "??" treats as real.
            TrafficSignals signals = traffic.GetComponent<TrafficSignals>();
            if (signals == null) signals = traffic.AddComponent<TrafficSignals>();
            TrafficManager manager = traffic.GetComponent<TrafficManager>();
            if (manager == null) manager = traffic.AddComponent<TrafficManager>();

            var signalsSo = new SerializedObject(signals);
            signalsSo.FindProperty("network").objectReferenceValue = network;
            signalsSo.ApplyModifiedProperties();

            var managerSo = new SerializedObject(manager);
            managerSo.FindProperty("network").objectReferenceValue = network;
            SerializedProperty list = managerSo.FindProperty("carPrefabs");
            list.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            managerSo.ApplyModifiedProperties();
            return traffic;
        }

        /// <summary>One placeholder prefab per traffic car type, with a TrafficDriver. Existing prefabs are reused.</summary>
        static List<GameObject> EnsureTrafficPrefabs()
        {
            VehicleSetup.EnsureDataSet();
            ProjectFolderSetup.EnsureFolder(PrefabFolder);
            var result = new List<GameObject>();

            foreach (VehicleCategory category in TrafficMix)
            {
                foreach (VehicleData data in VehicleSetup.AllData().Where(d => d.category == category))
                {
                    string path = $"{PrefabFolder}/Traffic_{data.brand.Replace(" ", "")}_{data.modelName.Replace(" ", "")}.prefab";
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        GameObject car = VehicleSetup.BuildCar(data, Vector3.zero, Quaternion.identity);
                        car.AddComponent<TrafficDriver>();
                        prefab = PrefabUtility.SaveAsPrefabAsset(car, path);
                        Object.DestroyImmediate(car);
                    }
                    if (prefab != null) result.Add(prefab);
                }
            }
            return result;
        }
    }
}
