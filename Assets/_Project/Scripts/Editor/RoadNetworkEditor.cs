using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GTH6.EditorTools
{
    /// <summary>Shared road-editing actions. Every change goes through Undo, so Ctrl+Z works.</summary>
    static class RoadTools
    {
        public const string SurfaceName = "RoadSurface";

        public static RoadNetwork NetworkOf(Component c) => c != null ? c.GetComponentInParent<RoadNetwork>() : null;

        public static RoadNode CreateNode(RoadNetwork network, Vector3 position)
        {
            var go = new GameObject("Node_" + network.transform.childCount.ToString("000"));
            Undo.RegisterCreatedObjectUndo(go, "Add Road Node");
            go.transform.SetParent(network.transform, false);
            go.transform.position = position;
            go.layer = network.gameObject.layer;
            RoadNode node = Undo.AddComponent<RoadNode>(go);
            return node;
        }

        public static void Connect(RoadNode a, RoadNode b, bool twoWay, int lanes, float speed)
        {
            if (a == null || b == null || a == b) return;
            Undo.RecordObjects(new Object[] { a, b }, "Connect Road Nodes");
            a.SetLink(b, lanes, speed);
            if (twoWay) b.SetLink(a, lanes, speed);
            else b.RemoveLink(a);
            EditorUtility.SetDirty(a);
            EditorUtility.SetDirty(b);
        }

        public static void Disconnect(RoadNode a, RoadNode b)
        {
            Undo.RecordObjects(new Object[] { a, b }, "Disconnect Road Nodes");
            a.RemoveLink(b);
            b.RemoveLink(a);
            EditorUtility.SetDirty(a);
            EditorUtility.SetDirty(b);
        }

        /// <summary>Puts a new node halfway along the road between a and b and keeps the lane data.</summary>
        public static RoadNode InsertBetween(RoadNetwork network, RoadNode a, RoadNode b)
        {
            RoadLink ab = a.GetLinkTo(b);
            RoadLink ba = b.GetLinkTo(a);
            if (ab == null && ba == null) return null;

            RoadNode middle = CreateNode(network, Vector3.Lerp(a.Position, b.Position, 0.5f));
            Undo.RecordObjects(new Object[] { a, b, middle }, "Insert Road Node");

            if (ab != null)
            {
                a.RemoveLink(b);
                a.SetLink(middle, ab.lanes, ab.speedLimit);
                middle.SetLink(b, ab.lanes, ab.speedLimit);
            }
            if (ba != null)
            {
                b.RemoveLink(a);
                b.SetLink(middle, ba.lanes, ba.speedLimit);
                middle.SetLink(a, ba.lanes, ba.speedLimit);
            }

            EditorUtility.SetDirty(a);
            EditorUtility.SetDirty(b);
            EditorUtility.SetDirty(middle);
            return middle;
        }

        /// <summary>Scene point under the mouse: real geometry first, then a flat plane at fallbackHeight.</summary>
        public static bool MousePoint(Vector2 guiPosition, float fallbackHeight, out Vector3 point)
        {
            if (HandleUtility.PlaceObject(guiPosition, out point, out _)) return true;

            Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, fallbackHeight, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        public static RoadNode NodeNearMouse(RoadNetwork network, Vector2 guiPosition, float maxPixels, RoadNode ignore)
        {
            RoadNode best = null;
            float bestDistance = maxPixels;
            foreach (RoadNode node in network.GetComponentsInChildren<RoadNode>())
            {
                if (node == ignore) continue;
                float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(node.Position), guiPosition);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = node;
                }
            }
            return best;
        }

        public static void SnapAllToGround(RoadNetwork network)
        {
            Transform surface = network.transform.Find(SurfaceName);
            Collider surfaceCollider = surface != null ? surface.GetComponent<Collider>() : null;
            bool wasEnabled = surfaceCollider != null && surfaceCollider.enabled;
            if (surfaceCollider != null) surfaceCollider.enabled = false; // don't snap onto our own road

            Physics.SyncTransforms();
            int moved = 0;
            foreach (RoadNode node in network.GetComponentsInChildren<RoadNode>())
            {
                Vector3 from = node.Position + Vector3.up * 100f;
                if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Undo.RecordObject(node.transform, "Snap Road Nodes");
                    node.transform.position = hit.point;
                    moved++;
                }
            }

            if (surfaceCollider != null) surfaceCollider.enabled = wasEnabled;
            Debug.Log($"[Roads] Snapped {moved} nodes to the ground. Nodes over empty space were left where they were.", network);
        }

        public static void GenerateSurface(RoadNetwork network)
        {
            Mesh mesh = RoadMeshBuilder.Build(network);

            Transform surface = network.transform.Find(SurfaceName);
            GameObject go;
            if (surface == null)
            {
                go = new GameObject(SurfaceName, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                Undo.RegisterCreatedObjectUndo(go, "Generate Road Surface");
                go.transform.SetParent(network.transform, false);
                go.isStatic = true;
            }
            else
            {
                go = surface.gameObject;
                Undo.RecordObjects(new Object[] { go.GetComponent<MeshFilter>(), go.GetComponent<MeshCollider>(), go.GetComponent<MeshRenderer>() }, "Generate Road Surface");
            }

            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
            if (network.roadMaterial != null) go.GetComponent<MeshRenderer>().sharedMaterial = network.roadMaterial;

            Debug.Log($"[Roads] Road surface built: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles.", go);
        }

        public static void Validate(RoadNetwork network)
        {
            var nodes = network.GetComponentsInChildren<RoadNode>();
            int broken = 0;
            foreach (RoadNode node in nodes)
            {
                Undo.RecordObject(node, "Validate Roads");
                broken += node.RemoveBrokenLinks();
            }
            network.Rebuild();

            var isolated = nodes.Where(n => network.Degree(n) == 0).ToList();
            var deadEnds = nodes.Where(n => network.Degree(n) == 1).ToList();
            var noExit = nodes.Where(n => network.Degree(n) > 0 && n.Links.Count == 0).ToList();

            string report =
                $"Nodes: {nodes.Length}\n" +
                $"Broken links removed: {broken}\n" +
                $"Isolated nodes (no roads): {isolated.Count}\n" +
                $"Dead ends: {deadEnds.Count} (fine at map edges; traffic will U-turn there)\n" +
                $"No way out (one-way streets only lead IN): {noExit.Count}  <- traffic gets stuck here, fix these";

            Debug.Log("[Roads] Validation\n" + report, network);
            if (noExit.Count > 0 || isolated.Count > 0)
            {
                Selection.objects = noExit.Concat(isolated).Select(n => (Object)n.gameObject).ToArray();
                report += "\n\nThe problem nodes are now selected.";
            }
            EditorUtility.DisplayDialog("Road network check", report, "OK");
        }

        public static void DrawSceneHelp(string text)
        {
            Handles.BeginGUI();
            GUI.Box(new Rect(10, 10, 430, 66), text, EditorStyles.helpBox);
            Handles.EndGUI();
        }
    }

    [CustomEditor(typeof(RoadNetwork))]
    public class RoadNetworkInspector : Editor
    {
        void OnSceneGUI()
        {
            var network = (RoadNetwork)target;
            RoadTools.DrawSceneHelp("ROAD NETWORK\nShift + Click: place the first node.\nThen keep Shift + Clicking to draw a road from the selected node.");

            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.shift && e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            if (e.type == EventType.MouseDown && e.button == 0 && e.shift && !e.alt)
            {
                if (RoadTools.MousePoint(e.mousePosition, network.transform.position.y, out Vector3 point))
                {
                    RoadNode node = RoadTools.CreateNode(network, point);
                    Selection.activeGameObject = node.gameObject;
                }
                e.Use();
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var network = (RoadNetwork)target;

            EditorGUILayout.Space();
            network.Rebuild();
            int links = network.Nodes.Sum(n => n != null ? n.Links.Count : 0);
            EditorGUILayout.HelpBox($"{network.Nodes.Count} nodes, {links} links (one per direction).", MessageType.None);

            if (GUILayout.Button("Add Node At Scene View Centre"))
            {
                Vector3 centre = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : network.transform.position;
                Selection.activeGameObject = RoadTools.CreateNode(network, centre).gameObject;
            }
            if (GUILayout.Button("Check Network")) RoadTools.Validate(network);
            if (GUILayout.Button("Snap All Nodes To Ground")) RoadTools.SnapAllToGround(network);
            if (GUILayout.Button("Generate Road Surface", GUILayout.Height(28))) RoadTools.GenerateSurface(network);
        }
    }

    [CustomEditor(typeof(RoadNode)), CanEditMultipleObjects]
    public class RoadNodeInspector : Editor
    {
        int bulkLanes = 1;
        float bulkSpeed = 50f;

        void OnEnable()
        {
            var network = RoadTools.NetworkOf((RoadNode)target);
            if (network != null)
            {
                bulkLanes = network.defaultLanes;
                bulkSpeed = network.defaultSpeedLimit;
            }
        }

        void OnSceneGUI()
        {
            if (Selection.gameObjects.Length != 1) return;
            var node = (RoadNode)target;
            RoadNetwork network = RoadTools.NetworkOf(node);
            if (network == null) return;

            RoadTools.DrawSceneHelp("ROAD NODE\nShift + Click: new node connected to this one.\nCtrl + Shift + Click another node: connect to it.");

            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.shift && e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (e.type != EventType.MouseDown || e.button != 0 || !e.shift || e.alt) return;

            if (EditorGUI.actionKey)
            {
                RoadNode other = RoadTools.NodeNearMouse(network, e.mousePosition, 25f, node);
                if (other != null)
                {
                    RoadTools.Connect(node, other, network.defaultTwoWay, network.defaultLanes, network.defaultSpeedLimit);
                    Selection.activeGameObject = other.gameObject;
                }
            }
            else if (RoadTools.MousePoint(e.mousePosition, node.Position.y, out Vector3 point))
            {
                RoadNode created = RoadTools.CreateNode(network, point);
                RoadTools.Connect(node, created, network.defaultTwoWay, network.defaultLanes, network.defaultSpeedLimit);
                Selection.activeGameObject = created.gameObject;
            }
            e.Use();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            RoadNode[] selected = targets.Cast<RoadNode>().ToArray();
            RoadNetwork network = RoadTools.NetworkOf(selected[0]);
            if (network == null)
            {
                EditorGUILayout.HelpBox("This node is not inside a RoadNetwork. Drag it under one.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space();

            if (selected.Length == 2)
            {
                // The active (last clicked) object is "second", so one-way direction is predictable.
                RoadNode second = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<RoadNode>() : null;
                if (second == null || !selected.Contains(second)) second = selected[1];
                RoadNode first = selected[0] == second ? selected[1] : selected[0];

                EditorGUILayout.LabelField("Two nodes selected", EditorStyles.boldLabel);
                if (GUILayout.Button("Connect Two-Way"))
                    RoadTools.Connect(first, second, true, network.defaultLanes, network.defaultSpeedLimit);
                if (GUILayout.Button($"Connect One-Way: {first.name} -> {second.name}"))
                    RoadTools.Connect(first, second, false, network.defaultLanes, network.defaultSpeedLimit);
                if (GUILayout.Button("Disconnect"))
                    RoadTools.Disconnect(first, second);

                using (new EditorGUI.DisabledScope(!first.HasLinkTo(second) && !second.HasLinkTo(first)))
                {
                    if (GUILayout.Button("Insert Node Between"))
                    {
                        RoadNode middle = RoadTools.InsertBetween(network, first, second);
                        if (middle != null) Selection.activeGameObject = middle.gameObject;
                    }
                }
                EditorGUILayout.Space();
            }

            EditorGUILayout.LabelField("Set every link leaving the selected node(s)", EditorStyles.boldLabel);
            bulkLanes = Mathf.Max(1, EditorGUILayout.IntField("Lanes", bulkLanes));
            bulkSpeed = Mathf.Max(5f, EditorGUILayout.FloatField("Speed Limit (km/h)", bulkSpeed));
            if (GUILayout.Button("Apply"))
            {
                Undo.RecordObjects(selected.Cast<Object>().ToArray(), "Set Road Links");
                foreach (RoadNode node in selected)
                {
                    foreach (RoadLink link in node.Links.ToList())
                        if (link != null && link.target != null) node.SetLink(link.target, bulkLanes, bulkSpeed);
                    EditorUtility.SetDirty(node);
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Select Road Network")) Selection.activeGameObject = network.gameObject;
        }
    }
}
