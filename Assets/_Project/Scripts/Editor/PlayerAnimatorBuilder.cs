using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GTH6.EditorTools
{
    /// <summary>All clips the player Animator can use. Only Idle, Walk, Jog and Sprint forward are required.</summary>
    [Serializable]
    public class PlayerClipSet
    {
        public AnimationClip idle;
        public AnimationClip walkForward, walkBackward, walkLeft, walkRight;
        public AnimationClip jogForward, jogBackward, jogLeft, jogRight;
        public AnimationClip sprintForward;
        public AnimationClip crouchIdle, crouchForward, crouchBackward, crouchLeft, crouchRight;
        public AnimationClip jump, fall, vault, getUp;
        public AnimationClip driving, enterCar, exitCar;

        public bool HasRequired => idle != null && walkForward != null && jogForward != null && sprintForward != null;

        /// <summary>
        /// Fills the set from clip names in a folder ("Walking", "Fast Run", "Left Strafe Walking"...),
        /// the way GTH6 > Set Up Mixamo Files names them. Unknown names are ignored.
        /// </summary>
        public static PlayerClipSet FindInFolder(string folder)
        {
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p))
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToList();

            AnimationClip Pick(Func<string, bool> rule) => clips.FirstOrDefault(c => rule(c.name.ToLowerInvariant()));
            bool Has(string n, params string[] words) => words.All(n.Contains);
            bool Lacks(string n, params string[] words) => !words.Any(n.Contains);
            string[] directional = { "back", "left", "right", "strafe", "turn", "crouch" };

            var set = new PlayerClipSet
            {
                idle = Pick(n => Has(n, "idle") && Lacks(n, "crouch", "fight", "to ", "rifle", "pistol")),
                walkForward = Pick(n => Has(n, "walk") && Lacks(n, directional)),
                walkBackward = Pick(n => Has(n, "walk", "back") && Lacks(n, "crouch")),
                walkLeft = Pick(n => Has(n, "walk", "left") && Lacks(n, "crouch", "turn")),
                walkRight = Pick(n => Has(n, "walk", "right") && Lacks(n, "crouch", "turn")),
                jogForward = Pick(n => Has(n, "jog") && Lacks(n, directional)),
                jogBackward = Pick(n => (Has(n, "jog", "back") || Has(n, "run", "back")) && Lacks(n, "crouch")),
                jogLeft = Pick(n => Has(n, "strafe", "left") && Lacks(n, "walk", "crouch")),
                jogRight = Pick(n => Has(n, "strafe", "right") && Lacks(n, "walk", "crouch")),
                sprintForward = Pick(n => (Has(n, "fast run") || Has(n, "sprint")) && Lacks(n, directional)),
                crouchIdle = Pick(n => Has(n, "crouch", "idle")),
                crouchForward = Pick(n => Has(n, "crouch", "walk") && Lacks(n, "back", "left", "right")),
                crouchBackward = Pick(n => Has(n, "crouch", "back")),
                crouchLeft = Pick(n => Has(n, "crouch", "left")),
                crouchRight = Pick(n => Has(n, "crouch", "right")),
                jump = Pick(n => Has(n, "jump") && Lacks(n, "over", "running")),
                fall = Pick(n => Has(n, "fall")),
                vault = Pick(n => Has(n, "vault") || Has(n, "jump over")),
                getUp = Pick(n => Has(n, "getting up") || Has(n, "stand up") || Has(n, "standing up")),
                driving = Pick(n => Has(n, "driving") || Has(n, "sitting")),
                enterCar = Pick(n => Has(n, "enter") && Has(n, "car")),
                exitCar = Pick(n => Has(n, "exit") && Has(n, "car"))
            };
            // Without a jog clip, a run clip is the next best thing.
            if (set.jogForward == null) set.jogForward = Pick(n => n.Trim() == "running" || n.Trim() == "run");
            return set;
        }
    }

    /// <summary>
    /// Menu GTH6 > Player Animator Builder. Drag clips in (or press Auto-Fill), press Build, and it creates
    /// the whole Animator Controller: parameters, both blend trees, all states, tags and transitions.
    /// Blend positions are in gait units (1 walk, 2 jog, 3 sprint) to match PlayerController.
    /// </summary>
    public class PlayerAnimatorBuilder : EditorWindow
    {
        public const string DefaultPath = "Assets/_Project/Art/Animations/PlayerAnimator.controller";
        const string AnimationsFolder = "Assets/_Project/Art/Animations";

        [SerializeField] PlayerClipSet clips = new PlayerClipSet();
        [SerializeField] string outputPath = DefaultPath;
        Vector2 scroll;

        [MenuItem("GTH6/Player Animator Builder")]
        static void Open() => GetWindow<PlayerAnimatorBuilder>("Player Animator Builder");

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.HelpBox("Press Auto-Fill to find clips in Art/Animations by name, or drag the clip INSIDE each FBX into a field. Fields marked * are required.", MessageType.Info);
            if (GUILayout.Button("Auto-Fill From Art/Animations")) clips = PlayerClipSet.FindInFolder(AnimationsFolder);

            SectionLabel("Standing locomotion");
            clips.idle = ClipField("Idle *", clips.idle);
            clips.walkForward = ClipField("Walk Forward *", clips.walkForward);
            clips.walkBackward = ClipField("Walk Backward", clips.walkBackward);
            clips.walkLeft = ClipField("Walk Strafe Left", clips.walkLeft);
            clips.walkRight = ClipField("Walk Strafe Right", clips.walkRight);
            clips.jogForward = ClipField("Jog Forward *", clips.jogForward);
            clips.jogBackward = ClipField("Jog Backward", clips.jogBackward);
            clips.jogLeft = ClipField("Jog Strafe Left", clips.jogLeft);
            clips.jogRight = ClipField("Jog Strafe Right", clips.jogRight);
            clips.sprintForward = ClipField("Sprint Forward *", clips.sprintForward);

            SectionLabel("Crouch");
            clips.crouchIdle = ClipField("Crouch Idle", clips.crouchIdle);
            clips.crouchForward = ClipField("Crouch Walk Forward", clips.crouchForward);
            clips.crouchBackward = ClipField("Crouch Walk Backward", clips.crouchBackward);
            clips.crouchLeft = ClipField("Crouch Walk Left", clips.crouchLeft);
            clips.crouchRight = ClipField("Crouch Walk Right", clips.crouchRight);

            SectionLabel("Actions");
            clips.jump = ClipField("Jump", clips.jump);
            clips.fall = ClipField("Falling Loop", clips.fall);
            clips.vault = ClipField("Vault", clips.vault);
            clips.getUp = ClipField("Get Up (from back)", clips.getUp);

            SectionLabel("Vehicles (phase 3)");
            clips.driving = ClipField("Driving (seated loop)", clips.driving);
            clips.enterCar = ClipField("Entering Car", clips.enterCar);
            clips.exitCar = ClipField("Exiting Car", clips.exitCar);

            EditorGUILayout.Space();
            outputPath = EditorGUILayout.TextField("Output Path", outputPath);

            using (new EditorGUI.DisabledScope(!clips.HasRequired))
            {
                if (GUILayout.Button("Build Animator Controller", GUILayout.Height(32)))
                {
                    if (!File.Exists(outputPath) || EditorUtility.DisplayDialog("Overwrite?", outputPath + " already exists. Replace it?", "Replace", "Cancel"))
                    {
                        AnimatorController controller = Build(clips, outputPath, out List<string> missing);
                        if (controller != null)
                        {
                            Selection.activeObject = controller;
                            EditorGUIUtility.PingObject(controller);
                            if (missing.Count > 0) Debug.LogWarning("[GTH6] Animator built. Missing optional clips: " + string.Join(", ", missing), controller);
                            else Debug.Log("[GTH6] Animator built with all clips: " + outputPath, controller);
                        }
                    }
                }
            }
            if (!clips.HasRequired) EditorGUILayout.HelpBox("Assign all fields marked *.", MessageType.Warning);

            EditorGUILayout.EndScrollView();
        }

        static void SectionLabel(string text)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        static AnimationClip ClipField(string label, AnimationClip clip) =>
            (AnimationClip)EditorGUILayout.ObjectField(label, clip, typeof(AnimationClip), false);

        /// <summary>
        /// Builds the controller at path, replacing any existing one without asking.
        /// Returns null if the path is invalid or required clips are missing.
        /// </summary>
        public static AnimatorController Build(PlayerClipSet c, string path, out List<string> missing)
        {
            missing = new List<string>();
            if (!path.StartsWith("Assets/") || !path.EndsWith(".controller"))
            {
                Debug.LogError("[GTH6] Animator path must start with Assets/ and end with .controller: " + path);
                return null;
            }
            if (!c.HasRequired)
            {
                Debug.LogError("[GTH6] Animator needs at least Idle, Walk Forward, Jog Forward and Sprint Forward clips.");
                return null;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            ProjectFolderSetup.EnsureFolder(Path.GetDirectoryName(path));

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("InVehicle", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Vault", AnimatorControllerParameterType.Trigger);

            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].name == "Grounded") parameters[i].defaultBool = true;
            controller.parameters = parameters;

            // Layers are structs: copy, edit, write back.
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree locoTree, 0);
            ConfigureTree(locoTree);
            AddChild(locoTree, c.idle, 0f, 0f, missing, "Idle");
            AddChild(locoTree, c.walkForward, 0f, 1f, missing, "Walk Forward");
            AddChild(locoTree, c.walkBackward, 0f, -1f, missing, "Walk Backward");
            AddChild(locoTree, c.walkLeft, -1f, 0f, missing, "Walk Strafe Left");
            AddChild(locoTree, c.walkRight, 1f, 0f, missing, "Walk Strafe Right");
            AddChild(locoTree, c.jogForward, 0f, 2f, missing, "Jog Forward");
            AddChild(locoTree, c.jogBackward, 0f, -2f, missing, "Jog Backward");
            AddChild(locoTree, c.jogLeft, -2f, 0f, missing, "Jog Strafe Left");
            AddChild(locoTree, c.jogRight, 2f, 0f, missing, "Jog Strafe Right");
            AddChild(locoTree, c.sprintForward, 0f, 3f, missing, "Sprint Forward");

            AnimatorState crouch = controller.CreateBlendTreeInController("Crouch", out BlendTree crouchTree, 0);
            ConfigureTree(crouchTree);
            AddChild(crouchTree, c.crouchIdle != null ? c.crouchIdle : c.idle, 0f, 0f, missing, "Crouch Idle");
            if (c.crouchIdle == null) missing.Add("Crouch Idle (used Idle)");
            AddChild(crouchTree, c.crouchForward, 0f, 1f, missing, "Crouch Walk Forward");
            AddChild(crouchTree, c.crouchBackward, 0f, -1f, missing, "Crouch Walk Backward");
            AddChild(crouchTree, c.crouchLeft, -1f, 0f, missing, "Crouch Walk Left");
            AddChild(crouchTree, c.crouchRight, 1f, 0f, missing, "Crouch Walk Right");

            AnimatorState jumpState = AddState(sm, "Jump", Pick(c.jump, c.fall, c.idle), "NoIK", new Vector3(500f, -100f, 0f));
            AnimatorState airborne = AddState(sm, "Airborne", Pick(c.fall, c.idle), "NoIK", new Vector3(750f, 0f, 0f));
            AnimatorState vaultState = AddState(sm, "Vault", Pick(c.vault, c.jump, c.idle), "NoIK", new Vector3(500f, 200f, 0f));
            AnimatorState getUpState = AddState(sm, "GetUp", Pick(c.getUp, c.idle), "Locked", new Vector3(250f, 300f, 0f));
            AnimatorState driving = AddState(sm, "Driving", Pick(c.driving, c.crouchIdle, c.idle), "Locked", new Vector3(-50f, 300f, 0f));
            // Enter/exit states only exist when you have the clips; PlayerVehicleInteraction checks for them.
            AnimatorState enterCar = c.enterCar != null ? AddState(sm, "EnterCar", c.enterCar, "Locked", new Vector3(-300f, 200f, 0f)) : null;
            AnimatorState exitCar = c.exitCar != null ? AddState(sm, "ExitCar", c.exitCar, "Locked", new Vector3(-300f, 400f, 0f)) : null;
            if (c.jump == null) missing.Add("Jump");
            if (c.fall == null) missing.Add("Falling Loop");
            if (c.vault == null) missing.Add("Vault");
            if (c.getUp == null) missing.Add("Get Up");
            if (c.driving == null) missing.Add("Driving");

            sm.defaultState = locomotion;

            // Order matters: Unity checks transitions top to bottom, so Jump is added before "left the ground".
            Connect(locomotion, jumpState, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            Connect(locomotion, airborne, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            Connect(locomotion, crouch, 0.2f).AddCondition(AnimatorConditionMode.If, 0f, "Crouch");

            Connect(crouch, locomotion, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");
            Connect(crouch, airborne, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            Connect(jumpState, airborne, 0.15f, true, 0.75f);
            Connect(airborne, locomotion, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition toVault = sm.AddAnyStateTransition(vaultState);
            toVault.hasExitTime = false;
            toVault.duration = 0.1f;
            toVault.hasFixedDuration = true;
            toVault.canTransitionToSelf = false;
            toVault.AddCondition(AnimatorConditionMode.If, 0f, "Vault");
            Connect(vaultState, locomotion, 0.15f, true, 0.9f);

            Connect(getUpState, locomotion, 0.2f, true, 0.9f);

            // Vehicles: code plays EnterCar / Driving; clearing InVehicle leaves the seat.
            if (enterCar != null) Connect(enterCar, driving, 0.15f, true, 0.95f);
            if (exitCar != null)
            {
                Connect(driving, exitCar, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0f, "InVehicle");
                Connect(exitCar, locomotion, 0.2f, true, 0.9f);
            }
            else
            {
                Connect(driving, locomotion, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "InVehicle");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static void ConfigureTree(BlendTree tree)
        {
            // Freeform Directional: several clips can point the same way (walk and jog forward).
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveY";
        }

        static void AddChild(BlendTree tree, Motion motion, float x, float y, List<string> missing, string label)
        {
            if (motion == null)
            {
                missing.Add(label);
                return;
            }
            tree.AddChild(motion, new Vector2(x, y));
        }

        static Motion Pick(params Motion[] options)
        {
            foreach (Motion m in options)
                if (m != null) return m;
            return null;
        }

        static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, string tag, Vector3 position)
        {
            AnimatorState state = sm.AddState(name, position);
            state.motion = motion;
            state.tag = tag;
            return state;
        }

        static AnimatorStateTransition Connect(AnimatorState from, AnimatorState to, float duration, bool hasExitTime = false, float exitTime = 0f)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = hasExitTime;
            t.exitTime = exitTime;
            t.duration = duration;
            t.hasFixedDuration = true;
            return t;
        }
    }
}
