using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GTH6.EditorTools
{
    /// <summary>
    /// Menu GTH6 > Player Animator Builder. Drag your Mixamo clips in, press Build, and it creates
    /// the whole Animator Controller: parameters, both blend trees, all states, tags and transitions.
    /// Blend positions are in gait units (1 walk, 2 jog, 3 sprint) to match PlayerController.
    /// </summary>
    public class PlayerAnimatorBuilder : EditorWindow
    {
        [SerializeField] AnimationClip idle;
        [SerializeField] AnimationClip walkForward, walkBackward, walkLeft, walkRight;
        [SerializeField] AnimationClip jogForward, jogBackward, jogLeft, jogRight;
        [SerializeField] AnimationClip sprintForward;
        [SerializeField] AnimationClip crouchIdle, crouchForward, crouchBackward, crouchLeft, crouchRight;
        [SerializeField] AnimationClip jump, fall, vault, getUp;
        [SerializeField] string outputPath = "Assets/_Project/Art/Animations/PlayerAnimator.controller";

        Vector2 scroll;

        [MenuItem("GTH6/Player Animator Builder")]
        static void Open() => GetWindow<PlayerAnimatorBuilder>("Player Animator Builder");

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.HelpBox("Drag the clip INSIDE each FBX (click the FBX's arrow to expand it). Fields marked * are required.", MessageType.Info);

            SectionLabel("Standing locomotion");
            idle = ClipField("Idle *", idle);
            walkForward = ClipField("Walk Forward *", walkForward);
            walkBackward = ClipField("Walk Backward", walkBackward);
            walkLeft = ClipField("Walk Strafe Left", walkLeft);
            walkRight = ClipField("Walk Strafe Right", walkRight);
            jogForward = ClipField("Jog Forward *", jogForward);
            jogBackward = ClipField("Jog Backward", jogBackward);
            jogLeft = ClipField("Jog Strafe Left", jogLeft);
            jogRight = ClipField("Jog Strafe Right", jogRight);
            sprintForward = ClipField("Sprint Forward *", sprintForward);

            SectionLabel("Crouch");
            crouchIdle = ClipField("Crouch Idle", crouchIdle);
            crouchForward = ClipField("Crouch Walk Forward", crouchForward);
            crouchBackward = ClipField("Crouch Walk Backward", crouchBackward);
            crouchLeft = ClipField("Crouch Walk Left", crouchLeft);
            crouchRight = ClipField("Crouch Walk Right", crouchRight);

            SectionLabel("Actions");
            jump = ClipField("Jump", jump);
            fall = ClipField("Falling Loop", fall);
            vault = ClipField("Vault", vault);
            getUp = ClipField("Get Up (from back)", getUp);

            EditorGUILayout.Space();
            outputPath = EditorGUILayout.TextField("Output Path", outputPath);

            bool ready = idle != null && walkForward != null && jogForward != null && sprintForward != null;
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("Build Animator Controller", GUILayout.Height(32))) Build();
            }
            if (!ready) EditorGUILayout.HelpBox("Assign all fields marked *.", MessageType.Warning);

            EditorGUILayout.EndScrollView();
        }

        static void SectionLabel(string text)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        static AnimationClip ClipField(string label, AnimationClip clip) =>
            (AnimationClip)EditorGUILayout.ObjectField(label, clip, typeof(AnimationClip), false);

        void Build()
        {
            if (!outputPath.StartsWith("Assets/") || !outputPath.EndsWith(".controller"))
            {
                EditorUtility.DisplayDialog("Invalid path", "Output Path must start with Assets/ and end with .controller", "OK");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(outputPath) != null)
            {
                if (!EditorUtility.DisplayDialog("Overwrite?", outputPath + " already exists. Replace it?", "Replace", "Cancel")) return;
                AssetDatabase.DeleteAsset(outputPath);
            }

            ProjectFolderSetup.EnsureFolder(Path.GetDirectoryName(outputPath));
            var missing = new List<string>();

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(outputPath);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
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
            AddChild(locoTree, idle, 0f, 0f, missing, "Idle");
            AddChild(locoTree, walkForward, 0f, 1f, missing, "Walk Forward");
            AddChild(locoTree, walkBackward, 0f, -1f, missing, "Walk Backward");
            AddChild(locoTree, walkLeft, -1f, 0f, missing, "Walk Strafe Left");
            AddChild(locoTree, walkRight, 1f, 0f, missing, "Walk Strafe Right");
            AddChild(locoTree, jogForward, 0f, 2f, missing, "Jog Forward");
            AddChild(locoTree, jogBackward, 0f, -2f, missing, "Jog Backward");
            AddChild(locoTree, jogLeft, -2f, 0f, missing, "Jog Strafe Left");
            AddChild(locoTree, jogRight, 2f, 0f, missing, "Jog Strafe Right");
            AddChild(locoTree, sprintForward, 0f, 3f, missing, "Sprint Forward");

            AnimatorState crouch = controller.CreateBlendTreeInController("Crouch", out BlendTree crouchTree, 0);
            ConfigureTree(crouchTree);
            AddChild(crouchTree, crouchIdle != null ? crouchIdle : idle, 0f, 0f, missing, "Crouch Idle");
            if (crouchIdle == null) missing.Add("Crouch Idle (used Idle)");
            AddChild(crouchTree, crouchForward, 0f, 1f, missing, "Crouch Walk Forward");
            AddChild(crouchTree, crouchBackward, 0f, -1f, missing, "Crouch Walk Backward");
            AddChild(crouchTree, crouchLeft, -1f, 0f, missing, "Crouch Walk Left");
            AddChild(crouchTree, crouchRight, 1f, 0f, missing, "Crouch Walk Right");

            AnimatorState jumpState = AddState(sm, "Jump", Pick(jump, fall, idle), "NoIK", new Vector3(500f, -100f, 0f));
            AnimatorState airborne = AddState(sm, "Airborne", Pick(fall, idle), "NoIK", new Vector3(750f, 0f, 0f));
            AnimatorState vaultState = AddState(sm, "Vault", Pick(vault, jump, idle), "NoIK", new Vector3(500f, 200f, 0f));
            AnimatorState getUpState = AddState(sm, "GetUp", Pick(getUp, idle), "Locked", new Vector3(250f, 300f, 0f));
            if (jump == null) missing.Add("Jump");
            if (fall == null) missing.Add("Falling Loop");
            if (vault == null) missing.Add("Vault");
            if (getUp == null) missing.Add("Get Up");

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

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);

            if (missing.Count > 0) Debug.LogWarning("[GTH6] Animator built. Missing optional clips: " + string.Join(", ", missing), controller);
            else Debug.Log("[GTH6] Animator built with all clips: " + outputPath, controller);
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
