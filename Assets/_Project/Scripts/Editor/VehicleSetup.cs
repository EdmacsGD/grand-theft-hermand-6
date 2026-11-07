using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GTH6.EditorTools
{
    /// <summary>
    /// Phase 3 helpers.
    /// GTH6 > Vehicles > Create Vehicle Data Set: 13 cars with made-up brands, tuned per category.
    /// GTH6 > Vehicles > Create Test Car: a drivable placeholder car (boxes and cylinders) for the
    /// selected VehicleData, placed where the Scene view is looking. Swap in real models later.
    /// </summary>
    public static class VehicleSetup
    {
        public const string DataFolder = "Assets/_Project/Data/Vehicles";
        const string MaterialsFolder = "Assets/_Project/Art/Materials";

        [MenuItem("GTH6/Vehicles/Create Vehicle Data Set")]
        static void CreateDataSetMenu()
        {
            int created = EnsureDataSet();
            EditorUtility.DisplayDialog("Vehicle data", created > 0
                ? $"Created {created} vehicle data assets in Data/Vehicles."
                : "All vehicle data assets already exist. Existing ones were not changed.", "OK");
        }

        [MenuItem("GTH6/Vehicles/Create Test Car")]
        static void CreateTestCarMenu()
        {
            EnsureDataSet();
            VehicleData data = Selection.activeObject as VehicleData ?? AllData().FirstOrDefault();
            if (data == null) return;

            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                Vector3 pivot = view.pivot;
                position = Physics.Raycast(pivot + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f) ? hit.point : pivot;
                Vector3 forward = view.rotation * Vector3.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.01f) rotation = Quaternion.LookRotation(forward);
            }

            GameObject car = BuildCar(data, position, rotation);
            Undo.RegisterCreatedObjectUndo(car, "Create Test Car");
            Selection.activeGameObject = car;
        }

        public static IEnumerable<VehicleData> AllData() =>
            AssetDatabase.FindAssets("t:VehicleData", new[] { DataFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<VehicleData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null);

        public static VehicleData FindData(VehicleCategory category) => AllData().FirstOrDefault(d => d.category == category);

        // ---------------- Data set ----------------

        /// <summary>Creates any missing assets of the standard set. Returns how many were created.</summary>
        public static int EnsureDataSet()
        {
            ProjectFolderSetup.EnsureFolder(DataFolder);
            int created = 0;

            created += Make("Nordvik_Pip", "Nordvik", "Pip", VehicleCategory.Hatchback, 12000, 1050f, new Vector3(1.72f, 1.45f, 3.9f), 150f, 165f, Drivetrain.FrontWheelDrive, 1.2f, 1.3f);
            created += Make("Nordvik_PipGT", "Nordvik", "Pip GT", VehicleCategory.Hatchback, 22000, 1150f, new Vector3(1.75f, 1.42f, 3.95f), 250f, 205f, Drivetrain.FrontWheelDrive, 1.35f, 1.45f);
            created += Make("Kystline_Brygge", "Kystline", "Brygge", VehicleCategory.Sedan, 26000, 1450f, new Vector3(1.82f, 1.45f, 4.7f), 280f, 200f, Drivetrain.RearWheelDrive, 1.25f, 1.35f);
            created += Make("Kystline_Ulrik", "Kystline", "Ulrik Estate", VehicleCategory.Sedan, 30000, 1550f, new Vector3(1.85f, 1.5f, 4.9f), 320f, 210f, Drivetrain.AllWheelDrive, 1.3f, 1.4f);
            created += Make("Hardanger_Stryk", "Hardanger", "Stryk", VehicleCategory.Sports, 65000, 1350f, new Vector3(1.88f, 1.25f, 4.4f), 420f, 250f, Drivetrain.RearWheelDrive, 1.6f, 1.7f, d => d.downforce = 3f);
            created += Make("Hardanger_Lyn", "Hardanger", "Lyn", VehicleCategory.Sports, 85000, 1400f, new Vector3(1.9f, 1.24f, 4.45f), 520f, 270f, Drivetrain.AllWheelDrive, 1.7f, 1.8f, d => d.downforce = 3.5f);
            created += Make("Valkyr_Aurora", "Valkyr", "Aurora", VehicleCategory.Supercar, 250000, 1450f, new Vector3(2.0f, 1.15f, 4.6f), 750f, 330f, Drivetrain.AllWheelDrive, 2.0f, 2.1f, d =>
            {
                d.downforce = 5f;
                d.damageMultiplier = 1.3f;
                d.gearRatios = new[] { 3.2f, 2.2f, 1.65f, 1.3f, 1.05f, 0.85f, 0.7f };
            });
            created += Make("Vidde_Fjellrev", "Vidde Motors", "Fjellrev", VehicleCategory.SUV, 45000, 2000f, new Vector3(1.95f, 1.75f, 4.8f), 450f, 200f, Drivetrain.AllWheelDrive, 1.2f, 1.25f, d => Heavy(d, 0.3f, 55000f, 6000f, -0.3f));
            created += Make("Vidde_Bastion", "Vidde Motors", "Bastion", VehicleCategory.SUV, 60000, 2400f, new Vector3(2.05f, 1.9f, 5.1f), 600f, 190f, Drivetrain.AllWheelDrive, 1.15f, 1.2f, d => Heavy(d, 0.3f, 65000f, 7000f, -0.3f));
            created += Make("Fraktverk_Budbil", "Fraktverk", "Budbil", VehicleCategory.Van, 28000, 2200f, new Vector3(2.0f, 2.2f, 5.2f), 330f, 160f, Drivetrain.RearWheelDrive, 1.1f, 1.15f, d =>
            {
                Heavy(d, 0.25f, 60000f, 6500f, -0.2f);
                d.maxHealth = 1400f;
                d.damageMultiplier = 0.8f;
            });
            created += Make("Fraktverk_Kjempe", "Fraktverk", "Kjempe", VehicleCategory.Truck, 90000, 7500f, new Vector3(2.45f, 3.1f, 7.5f), 1800f, 120f, Drivetrain.RearWheelDrive, 1.0f, 1.1f, d =>
            {
                Heavy(d, 0.3f, 160000f, 16000f, -0.4f);
                d.wheelRadius = 0.5f;
                d.wheelMass = 80f;
                d.maxHealth = 3000f;
                d.damageMultiplier = 0.5f;
                d.maxSteerAngle = 30f;
                d.highSpeedSteerAngle = 12f;
                d.gearRatios = new[] { 6.0f, 3.8f, 2.5f, 1.7f, 1.25f, 1.0f };
                d.finalDrive = 4.1f;
                d.maxRpm = 3000f;
                d.idleRpm = 600f;
            });
            created += Make("Kystline_Taxi", "Kystline", "Brygge Taxi", VehicleCategory.Taxi, 0, 1480f, new Vector3(1.82f, 1.45f, 4.7f), 280f, 195f, Drivetrain.RearWheelDrive, 1.25f, 1.35f);
            created += Make("Kystline_Patrulje", "Kystline", "Patrulje", VehicleCategory.Police, 0, 1550f, new Vector3(1.85f, 1.5f, 4.8f), 380f, 230f, Drivetrain.AllWheelDrive, 1.45f, 1.55f, d =>
            {
                d.maxHealth = 1600f;
                d.damageMultiplier = 0.8f;
                d.downforce = 2.5f;
            });

            if (created > 0) AssetDatabase.SaveAssets();
            return created;
        }

        static void Heavy(VehicleData d, float suspension, float spring, float damper, float comY)
        {
            d.suspensionDistance = suspension;
            d.spring = spring;
            d.damper = damper;
            d.centerOfMassOffset = new Vector3(0f, comY, 0f);
            d.antiRoll = spring * 0.25f;
        }

        static int Make(string file, string brand, string model, VehicleCategory category, int price, float mass, Vector3 size,
            float torque, float topSpeed, Drivetrain drivetrain, float forwardGrip, float sideGrip, System.Action<VehicleData> extra = null)
        {
            string path = $"{DataFolder}/{file}.asset";
            if (AssetDatabase.LoadAssetAtPath<VehicleData>(path) != null) return 0;

            VehicleData d = ScriptableObject.CreateInstance<VehicleData>();
            d.brand = brand;
            d.modelName = model;
            d.category = category;
            d.price = price;
            d.mass = mass;
            d.size = size;
            d.maxTorque = torque;
            d.topSpeedKmh = topSpeed;
            d.drivetrain = drivetrain;
            d.forwardGrip = forwardGrip;
            d.sidewaysGrip = sideGrip;
            // Brakes and springs scale with weight so heavy cars still stop and do not sag.
            d.brakeTorque = mass * 2f;
            d.handbrakeTorque = mass * 3.5f;
            d.spring = Mathf.Max(30000f, mass * 28f);
            d.damper = d.spring * 0.11f;
            d.antiRoll = d.spring * 0.18f;
            extra?.Invoke(d);

            AssetDatabase.CreateAsset(d, path);
            return 1;
        }

        // ---------------- Placeholder car ----------------

        /// <summary>Builds a drivable box car for the data. Returns the root object.</summary>
        public static GameObject BuildCar(VehicleData data, Vector3 position, Quaternion rotation)
        {
            Vector3 size = data.size;
            float r = data.wheelRadius;
            float bodyBottom = r * 0.9f;
            float lowerHeight = size.y * 0.5f;
            float cabinHeight = Mathf.Max(0.3f, size.y - bodyBottom - lowerHeight);

            var root = new GameObject("Car_" + data.DisplayName.Replace(" ", ""));
            root.transform.SetPositionAndRotation(position, rotation);
            root.AddComponent<Rigidbody>().mass = data.mass;

            Material paint = GetMaterial("Paint_" + data.category, PaintFor(data.category));
            Material glass = GetMaterial("Car_Glass", new Color(0.12f, 0.16f, 0.2f));
            Material tyre = GetMaterial("Car_Tyre", new Color(0.08f, 0.08f, 0.08f));

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            Part(body, "Lower", new Vector3(0f, bodyBottom + lowerHeight * 0.5f, 0f), new Vector3(size.x, lowerHeight, size.z), paint, true);

            float cabinBase = bodyBottom + lowerHeight;
            switch (data.category)
            {
                case VehicleCategory.Van:
                    Part(body, "Cabin", new Vector3(0f, cabinBase + cabinHeight * 0.5f, -size.z * 0.05f), new Vector3(size.x * 0.96f, cabinHeight, size.z * 0.85f), glass, true);
                    break;
                case VehicleCategory.Truck:
                    Part(body, "Cab", new Vector3(0f, cabinBase + cabinHeight * 0.35f, size.z * 0.36f), new Vector3(size.x * 0.96f, cabinHeight * 0.7f, size.z * 0.22f), glass, true);
                    Part(body, "Cargo", new Vector3(0f, cabinBase + cabinHeight * 0.5f, -size.z * 0.12f), new Vector3(size.x, cabinHeight, size.z * 0.7f), paint, true);
                    break;
                default:
                    Part(body, "Cabin", new Vector3(0f, cabinBase + cabinHeight * 0.5f, -size.z * 0.05f), new Vector3(size.x * 0.88f, cabinHeight, size.z * 0.5f), glass, true);
                    break;
            }

            float roof = bodyBottom + lowerHeight + cabinHeight;
            if (data.category == VehicleCategory.Taxi)
                Part(body, "TaxiSign", new Vector3(0f, roof + 0.1f, -size.z * 0.05f), new Vector3(0.6f, 0.2f, 0.25f), GetMaterial("Car_TaxiSign", new Color(1f, 0.85f, 0.2f)), false);
            if (data.category == VehicleCategory.Police)
            {
                Part(body, "LightRed", new Vector3(-0.3f, roof + 0.07f, -size.z * 0.05f), new Vector3(0.5f, 0.14f, 0.25f), GetMaterial("Car_LightRed", new Color(0.9f, 0.1f, 0.1f)), false);
                Part(body, "LightBlue", new Vector3(0.3f, roof + 0.07f, -size.z * 0.05f), new Vector3(0.5f, 0.14f, 0.25f), GetMaterial("Car_LightBlue", new Color(0.1f, 0.3f, 1f)), false);
            }

            // Wheels: the collider does the physics, the cylinder is only the picture.
            var collidersRoot = new GameObject("WheelColliders");
            collidersRoot.transform.SetParent(root.transform, false);
            float x = size.x * 0.5f - 0.13f;
            float z = size.z * 0.5f - r - 0.35f;
            var wheelEntries = new List<(WheelCollider collider, Transform visual, bool front)>();
            foreach ((string name, float wx, float wz, bool front) in new[] { ("FL", -x, z, true), ("FR", x, z, true), ("RL", -x, -z, false), ("RR", x, -z, false) })
            {
                var colliderGo = new GameObject("Wheel_" + name);
                colliderGo.transform.SetParent(collidersRoot.transform, false);
                colliderGo.transform.localPosition = new Vector3(wx, r + data.suspensionDistance * 0.5f, wz);
                WheelCollider wc = colliderGo.AddComponent<WheelCollider>();
                wc.radius = r;
                wc.suspensionDistance = data.suspensionDistance;

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "Visual_" + name;
                Object.DestroyImmediate(visual.GetComponent<Collider>()); // the WheelCollider is the only wheel collision
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(wx, r, wz);
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                visual.transform.localScale = new Vector3(r * 2f, 0.125f, r * 2f);
                visual.GetComponent<Renderer>().sharedMaterial = tyre;

                wheelEntries.Add((wc, visual.transform, front));
            }

            Transform seat = Empty(root, "DriverSeat", new Vector3(-size.x * 0.22f, bodyBottom + 0.05f, size.z * 0.02f));
            Transform exit = Empty(root, "ExitPoint", new Vector3(-size.x * 0.5f - 0.7f, 0f, size.z * 0.02f));
            Transform anchor = Empty(root, "CameraAnchor", new Vector3(0f, size.y * 0.75f, 0f));

            var trigger = new GameObject("CharacterHitTrigger");
            trigger.transform.SetParent(root.transform, false);
            BoxCollider box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            box.size = new Vector3(size.x + 0.5f, size.y, size.z + 0.6f);

            VehicleController controller = root.AddComponent<VehicleController>();
            var so = new SerializedObject(controller);
            so.FindProperty("data").objectReferenceValue = data;
            so.FindProperty("driverSeat").objectReferenceValue = seat;
            so.FindProperty("exitPoint").objectReferenceValue = exit;
            so.FindProperty("cameraAnchor").objectReferenceValue = anchor;
            SerializedProperty wheels = so.FindProperty("wheels");
            wheels.arraySize = wheelEntries.Count;
            for (int i = 0; i < wheelEntries.Count; i++)
            {
                SerializedProperty w = wheels.GetArrayElementAtIndex(i);
                w.FindPropertyRelative("collider").objectReferenceValue = wheelEntries[i].collider;
                w.FindPropertyRelative("visual").objectReferenceValue = wheelEntries[i].visual;
                w.FindPropertyRelative("front").boolValue = wheelEntries[i].front;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<VehicleDamage>();
            root.AddComponent<VehicleAudio>();
            return root;
        }

        static void Part(GameObject parent, string name, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            if (!keepCollider) Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Transform Empty(GameObject parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        static Color PaintFor(VehicleCategory c)
        {
            switch (c)
            {
                case VehicleCategory.Hatchback: return new Color(0.2f, 0.55f, 0.85f);
                case VehicleCategory.Sedan: return new Color(0.75f, 0.75f, 0.78f);
                case VehicleCategory.Sports: return new Color(0.85f, 0.15f, 0.12f);
                case VehicleCategory.Supercar: return new Color(0.95f, 0.6f, 0.05f);
                case VehicleCategory.SUV: return new Color(0.15f, 0.3f, 0.2f);
                case VehicleCategory.Van: return new Color(0.92f, 0.92f, 0.9f);
                case VehicleCategory.Truck: return new Color(0.55f, 0.35f, 0.2f);
                case VehicleCategory.Taxi: return new Color(0.1f, 0.1f, 0.12f);
                case VehicleCategory.Police: return new Color(0.95f, 0.95f, 0.98f);
                default: return Color.grey;
            }
        }

        static Material GetMaterial(string name, Color color)
        {
            ProjectFolderSetup.EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
