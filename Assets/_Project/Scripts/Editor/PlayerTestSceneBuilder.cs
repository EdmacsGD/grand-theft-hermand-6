using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GTH6.EditorTools
{
    /// <summary>
    /// Menu GTH6 > Build Player Test Scene. One click replaces sections 3 to 7 of the phase 1 guide:
    /// builds the Animator from your clips, the Player (all components wired, ragdoll included),
    /// both Cinemachine cameras and the test course, then saves Scenes/PlayerTest.
    /// Run GTH6 > Set Up Mixamo Files first.
    /// </summary>
    public static class PlayerTestSceneBuilder
    {
        const string CharactersFolder = "Assets/_Project/Art/Characters";
        const string AnimationsFolder = "Assets/_Project/Art/Animations";
        const string MaterialsFolder = "Assets/_Project/Art/Materials";
        const string ScenePath = "Assets/_Project/Scenes/PlayerTest.unity";

        // Cinemachine is reached by name through reflection, so this file compiles even if the
        // Cinemachine package is missing or renames something; you get a warning instead of an error.
        const string CmCamera = "Unity.Cinemachine.CinemachineCamera";
        const string CmBrain = "Unity.Cinemachine.CinemachineBrain";
        const string CmThirdPerson = "Unity.Cinemachine.CinemachineThirdPersonFollow";

        static readonly List<string> warnings = new List<string>();

        [MenuItem("GTH6/Build Player Test Scene")]
        static void Build()
        {
            warnings.Clear();

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer < 0)
            {
                EditorUtility.DisplayDialog("Player test scene", "There is no 'Player' layer. Add it in Edit > Project Settings > Tags and Layers, then try again.", "OK");
                return;
            }

            if (!FindCharacter(out GameObject characterModel, out Avatar avatar)) return;

            PlayerClipSet clips = PlayerClipSet.FindInFolder(AnimationsFolder);
            if (!clips.HasRequired)
            {
                EditorUtility.DisplayDialog("Player test scene",
                    "Missing required clips in Art/Animations. Needed: Idle, Walking, Jogging, Fast Run.\n\nRun GTH6 > Set Up Mixamo Files after downloading them.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            AnimatorController controller = PlayerAnimatorBuilder.Build(clips, PlayerAnimatorBuilder.DefaultPath, out List<string> missingClips);
            if (controller == null) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildCourse();
            BuildTestCars();
            RoadNetwork roads = TrafficSetup.CreateTestGrid();
            TrafficSetup.SetUpTraffic(roads, out _);
            GameObject player = BuildPlayer(characterModel, avatar, controller, playerLayer);
            BuildCameras(player);

            ProjectFolderSetup.EnsureFolder("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Selection.activeGameObject = player;

            string message = "Scenes/PlayerTest is ready. Press Play.\n\n" +
                             "Keys: WASD, mouse, Shift, Alt, Space, C, right mouse. Test: H, J, K, R.\n" +
                             "Cars behind you: F to get in/out, W/S, A/D, Space handbrake, Q/E radio.\n" +
                             "Streets with traffic and lights start 40 m in front of you.";
            if (missingClips.Count > 0) message += "\n\nOptional clips not found (fine for now): " + string.Join(", ", missingClips);
            if (warnings.Count > 0) message += "\n\nCheck by hand:\n- " + string.Join("\n- ", warnings);
            Debug.Log("[GTH6] " + message);
            EditorUtility.DisplayDialog("Player test scene", message, "OK");
        }

        // ---------------- Character ----------------

        static bool FindCharacter(out GameObject model, out Avatar avatar)
        {
            model = null;
            avatar = null;
            foreach (string path in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (System.IO.Path.GetFileNameWithoutExtension(path).Contains("@")) continue;
                Avatar a = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                if (a == null || !a.isValid || !a.isHuman) continue;
                model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                avatar = a;
                return true;
            }

            EditorUtility.DisplayDialog("Player test scene",
                "No humanoid character found in Art/Characters.\n\nPut the Mixamo character (with skin) there and run GTH6 > Set Up Mixamo Files.", "OK");
            return false;
        }

        // ---------------- Player ----------------

        static GameObject BuildPlayer(GameObject characterModel, Avatar avatar, RuntimeAnimatorController controller, int playerLayer)
        {
            var root = new GameObject("Player");
            root.tag = "Player";

            var cc = root.AddComponent<CharacterController>();
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.radius = 0.3f;
            cc.height = 1.8f;

            root.AddComponent<PlayerInputReader>();
            root.AddComponent<PlayerController>();
            var rig = root.AddComponent<PlayerCameraRig>();
            root.AddComponent<Health>();
            root.AddComponent<RagdollController>();
            root.AddComponent<PlayerDebugKeys>();
            root.AddComponent<CarRadio>();
            var interaction = root.AddComponent<PlayerVehicleInteraction>();
            var interactionSo = new SerializedObject(interaction);
            interactionSo.FindProperty("hideBodyWhileDriving").boolValue = true; // box cars have no interior yet
            interactionSo.ApplyModifiedPropertiesWithoutUndo();

            var model = (GameObject)PrefabUtility.InstantiatePrefab(characterModel);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = new Vector3(0f, -0.03f, 0f); // CharacterController floats one skin width up
            model.transform.localRotation = Quaternion.identity;

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            model.AddComponent<FootIK>();

            BuildRagdoll(animator);

            var cameraTarget = new GameObject("CameraTarget");
            cameraTarget.transform.SetParent(root.transform, false);
            cameraTarget.transform.localPosition = new Vector3(0f, 1.55f, 0f);

            SetLayerRecursively(root, playerLayer);

            var so = new SerializedObject(rig);
            so.FindProperty("cameraTarget").objectReferenceValue = cameraTarget.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        /// <summary>Runs Unity's own Ragdoll Wizard (GameObject > 3D Object > Ragdoll...) on the humanoid bones.</summary>
        static void BuildRagdoll(Animator animator)
        {
            Type wizardType = FindType("UnityEditor.RagdollBuilder");
            if (wizardType == null)
            {
                warnings.Add("Ragdoll: Unity's Ragdoll Wizard was not found. Run GameObject > 3D Object > Ragdoll... yourself (phase 1 guide, section 5).");
                return;
            }

            ScriptableObject wizard = ScriptableObject.CreateInstance(wizardType);
            try
            {
                Transform spine = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Spine);

                bool ok =
                    SetField(wizard, "pelvis", animator.GetBoneTransform(HumanBodyBones.Hips)) &
                    SetField(wizard, "leftHips", animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg)) &
                    SetField(wizard, "leftKnee", animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg)) &
                    SetField(wizard, "leftFoot", animator.GetBoneTransform(HumanBodyBones.LeftFoot)) &
                    SetField(wizard, "rightHips", animator.GetBoneTransform(HumanBodyBones.RightUpperLeg)) &
                    SetField(wizard, "rightKnee", animator.GetBoneTransform(HumanBodyBones.RightLowerLeg)) &
                    SetField(wizard, "rightFoot", animator.GetBoneTransform(HumanBodyBones.RightFoot)) &
                    SetField(wizard, "leftArm", animator.GetBoneTransform(HumanBodyBones.LeftUpperArm)) &
                    SetField(wizard, "leftElbow", animator.GetBoneTransform(HumanBodyBones.LeftLowerArm)) &
                    SetField(wizard, "rightArm", animator.GetBoneTransform(HumanBodyBones.RightUpperArm)) &
                    SetField(wizard, "rightElbow", animator.GetBoneTransform(HumanBodyBones.RightLowerArm)) &
                    SetField(wizard, "middleSpine", spine) &
                    SetField(wizard, "head", animator.GetBoneTransform(HumanBodyBones.Head)) &
                    SetField(wizard, "totalMass", 70f);

                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo update = wizardType.GetMethod("OnWizardUpdate", all);
                MethodInfo create = wizardType.GetMethod("OnWizardCreate", all);
                if (!ok || update == null || create == null)
                {
                    warnings.Add("Ragdoll: the Ragdoll Wizard has changed in this Unity version. Run GameObject > 3D Object > Ragdoll... yourself.");
                    return;
                }

                // The wizard window calls OnWizardUpdate first (it checks the bones and works out the body axes),
                // then OnWizardCreate when you press Create. Same order here.
                update.Invoke(wizard, null);
                string error = wizardType.GetProperty("errorString", all)?.GetValue(wizard) as string;
                if (!string.IsNullOrEmpty(error))
                {
                    warnings.Add("Ragdoll: the wizard reported \"" + error + "\". Run GameObject > 3D Object > Ragdoll... yourself.");
                    return;
                }
                create.Invoke(wizard, null);
            }
            catch (Exception e)
            {
                warnings.Add("Ragdoll: automatic setup failed (" + e.GetBaseException().Message + "). Run GameObject > 3D Object > Ragdoll... yourself.");
            }
            finally
            {
                Object.DestroyImmediate(wizard);
            }
        }

        static bool SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return false;
            field.SetValue(target, value);
            return true;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        // ---------------- Cameras ----------------

        static void BuildCameras(GameObject player)
        {
            Type camType = FindType(CmCamera), brainType = FindType(CmBrain), followType = FindType(CmThirdPerson);
            if (camType == null || brainType == null || followType == null)
            {
                warnings.Add("Cameras: Cinemachine 3 not found. Install it and set up the cameras by hand (phase 1 guide, section 6).");
                return;
            }

            Transform target = player.transform.Find("CameraTarget");
            Camera main = Camera.main;
            if (main == null) main = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" }.GetComponent<Camera>();
            Component brain = main.GetComponent(brainType);
            if (brain == null) brain = main.gameObject.AddComponent(brainType);
            var brainSo = new SerializedObject(brain);
            SetFloat(brainSo, "DefaultBlend.Time", 0.25f, "Brain");
            brainSo.ApplyModifiedPropertiesWithoutUndo();

            int collisionMask = ~(1 << player.layer);
            MakeCamera("CM_FollowCam", camType, followType, target, 10, 60f,
                new Vector3(0.1f, 0.25f, 0.3f), new Vector3(0.5f, 0.2f, 0f), 0.3f, 4.5f, 0.2f, collisionMask);
            GameObject aim = MakeCamera("CM_AimCam", camType, followType, target, 20, 40f,
                Vector3.zero, new Vector3(0.75f, 0.1f, 0f), 0.1f, 1.6f, 0.15f, collisionMask);
            aim.SetActive(false);
            GameObject vehicleCam = MakeCamera("CM_VehicleCam", camType, followType, target, 15, 60f,
                new Vector3(0.3f, 0.4f, 0.6f), new Vector3(0f, 0.6f, 0f), 0.4f, 7.5f, 0.3f, collisionMask);
            vehicleCam.SetActive(false);

            var rigSo = new SerializedObject(player.GetComponent<PlayerCameraRig>());
            rigSo.FindProperty("aimCamera").objectReferenceValue = aim;
            rigSo.FindProperty("vehicleCamera").objectReferenceValue = vehicleCam;
            rigSo.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject MakeCamera(string name, Type camType, Type followType, Transform target, int priority, float fov,
            Vector3 damping, Vector3 shoulder, float armLength, float distance, float radius, int collisionMask)
        {
            var go = new GameObject(name);
            go.transform.position = target.position - Vector3.forward * distance;
            Component cam = go.AddComponent(camType);
            Component follow = go.AddComponent(followType);

            var camSo = new SerializedObject(cam);
            SetPriority(camSo, priority, name);
            SetObject(camSo, "Target.TrackingTarget", target, name);
            SetFloat(camSo, "Lens.FieldOfView", fov, name);
            camSo.ApplyModifiedPropertiesWithoutUndo();

            var f = new SerializedObject(follow);
            SetVector3(f, "Damping", damping, name);
            SetVector3(f, "ShoulderOffset", shoulder, name);
            SetFloat(f, "VerticalArmLength", armLength, name);
            SetFloat(f, "CameraSide", 1f, name);
            SetFloat(f, "CameraDistance", distance, name);
            SetBool(f, "AvoidObstacles.Enabled", true, name);
            SetInt(f, "AvoidObstacles.CollisionFilter", collisionMask, name);
            SetString(f, "AvoidObstacles.IgnoreTag", "Player", name);
            SetFloat(f, "AvoidObstacles.CameraRadius", radius, name);
            f.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);

        // Priority is a plain int in some Cinemachine versions and a small struct (value + enabled) in others.
        static void SetPriority(SerializedObject so, int value, string owner)
        {
            SerializedProperty p = so.FindProperty("Priority");
            if (p == null) { Missing(owner, "Priority"); return; }
            if (p.propertyType == SerializedPropertyType.Integer) { p.intValue = value; return; }

            SerializedProperty end = p.GetEndProperty();
            SerializedProperty it = p.Copy();
            bool enterChildren = true;
            while (it.Next(enterChildren) && !SerializedProperty.EqualContents(it, end))
            {
                enterChildren = false;
                if (it.propertyType == SerializedPropertyType.Integer) it.intValue = value;
                else if (it.propertyType == SerializedPropertyType.Boolean) it.boolValue = true;
            }
        }

        static void SetFloat(SerializedObject so, string path, float v, string owner) { var p = so.FindProperty(path); if (p != null) p.floatValue = v; else Missing(owner, path); }
        static void SetInt(SerializedObject so, string path, int v, string owner) { var p = so.FindProperty(path); if (p != null) p.intValue = v; else Missing(owner, path); }
        static void SetBool(SerializedObject so, string path, bool v, string owner) { var p = so.FindProperty(path); if (p != null) p.boolValue = v; else Missing(owner, path); }
        static void SetString(SerializedObject so, string path, string v, string owner) { var p = so.FindProperty(path); if (p != null) p.stringValue = v; else Missing(owner, path); }
        static void SetVector3(SerializedObject so, string path, Vector3 v, string owner) { var p = so.FindProperty(path); if (p != null) p.vector3Value = v; else Missing(owner, path); }
        static void SetObject(SerializedObject so, string path, Object v, string owner) { var p = so.FindProperty(path); if (p != null) p.objectReferenceValue = v; else Missing(owner, path); }
        static void Missing(string owner, string path) => warnings.Add($"{owner}: set '{path}' by hand (see the phase 1 guide camera table).");

        // ---------------- Test course ----------------

        static void BuildCourse()
        {
            Material ground = GetMaterial("Blockout_Ground", new Color(0.32f, 0.33f, 0.35f));
            Material grey = GetMaterial("Blockout_Grey", new Color(0.7f, 0.7f, 0.72f));
            Material accent = GetMaterial("Blockout_Accent", new Color(0.95f, 0.55f, 0.2f));

            var course = new GameObject("TestCourse");

            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.SetParent(course.transform);
            plane.transform.localScale = new Vector3(60f, 1f, 60f); // 600 x 600 m, room for the test road grid
            plane.GetComponent<Renderer>().sharedMaterial = ground;

            // Slopes: centre height = half length * sin(angle), so the low end meets the ground.
            Box(course, "Ramp_20deg", new Vector3(8f, 4f * Mathf.Sin(20f * Mathf.Deg2Rad), 10f), new Vector3(4f, 0.2f, 8f), new Vector3(-20f, 0f, 0f), grey);
            Box(course, "Ramp_55deg_TooSteep", new Vector3(14f, 4f * Mathf.Sin(55f * Mathf.Deg2Rad), 10f), new Vector3(4f, 0.2f, 8f), new Vector3(-55f, 0f, 0f), accent);

            Box(course, "LowWall_Vault", new Vector3(-6f, 0.5f, 8f), new Vector3(3f, 1f, 0.2f), Vector3.zero, accent);
            Box(course, "Kerb", new Vector3(-12f, 0.125f, 8f), new Vector3(3f, 0.25f, 1f), Vector3.zero, grey);
            Box(course, "TallWall", new Vector3(-6f, 1.25f, 16f), new Vector3(3f, 2.5f, 0.3f), Vector3.zero, grey);

            Box(course, "CrawlGap_Roof", new Vector3(-14f, 1.3f, 16f), new Vector3(3f, 0.2f, 3f), Vector3.zero, accent);
            Box(course, "CrawlGap_PostL", new Vector3(-15.4f, 0.6f, 16f), new Vector3(0.2f, 1.2f, 3f), Vector3.zero, grey);
            Box(course, "CrawlGap_PostR", new Vector3(-12.6f, 0.6f, 16f), new Vector3(0.2f, 1.2f, 3f), Vector3.zero, grey);

            for (int i = 0; i < 6; i++)
            {
                float h = 0.2f * (i + 1);
                Box(course, "Step_" + (i + 1), new Vector3(0f, h * 0.5f, 12f + 0.3f * i), new Vector3(3f, h, 0.3f), Vector3.zero, grey);
            }
            Box(course, "Stairs_Top", new Vector3(0f, 0.6f, 15.3f), new Vector3(3f, 1.2f, 3f), Vector3.zero, grey);

            foreach (Transform t in course.transform) t.gameObject.isStatic = true;
        }

        static void BuildTestCars()
        {
            VehicleSetup.EnsureDataSet();
            var cars = new GameObject("TestCars");
            VehicleCategory[] lineup = { VehicleCategory.Hatchback, VehicleCategory.Sports, VehicleCategory.SUV };
            for (int i = 0; i < lineup.Length; i++)
            {
                VehicleData data = VehicleSetup.FindData(lineup[i]);
                if (data == null) continue;
                GameObject car = VehicleSetup.BuildCar(data, new Vector3(8f + i * 4f, 0f, -8f), Quaternion.identity);
                car.transform.SetParent(cars.transform, true);
            }
        }

        static void Box(GameObject parent, string name, Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform);
            box.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Material GetMaterial(string name, Color color)
        {
            ProjectFolderSetup.EnsureFolder(MaterialsFolder);
            string path = MaterialsFolder + "/" + name + ".mat";
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

        static void AddToBuildSettings(string path)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
