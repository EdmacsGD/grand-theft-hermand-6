using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GTH6.EditorTools
{
    /// <summary>
    /// Menu GTH6 > Set Up Mixamo Files. Does every import setting from the phase 1 guide for you:
    /// 1. Moves animation files ("Character@Clip.fbx") from Art/Characters to Art/Animations.
    /// 2. Characters: Rig = Humanoid, Avatar = Create From This Model.
    /// 3. Animations: Rig = Humanoid, Avatar = Copy From the character's avatar.
    /// 4. Clips: renamed to the action ("Walking" instead of "mixamo.com"), Loop Time + Loop Pose on
    ///    looping moves, root rotation and height baked into the pose (root motion is off in this game).
    /// Run it again after adding new Mixamo downloads. It re-applies the settings to every file,
    /// so manual changes to these particular settings get overwritten.
    /// </summary>
    public static class MixamoImportSetup
    {
        const string CharactersFolder = "Assets/_Project/Art/Characters";
        const string AnimationsFolder = "Assets/_Project/Art/Animations";

        // Order matters: one-shot words win, so "Running Jump" does not loop.
        static readonly string[] OneShotWords = { "jump", "vault", "getting up", "get up", "stand up", "standing up", "death", "dying", "hit", "reaction", " to " };
        static readonly string[] LoopWords = { "idle", "walk", "jog", "run", "sprint", "strafe", "crouch", "fall", "sneak" };

        [MenuItem("GTH6/Set Up Mixamo Files")]
        static void Run()
        {
            ProjectFolderSetup.EnsureFolder(CharactersFolder);
            ProjectFolderSetup.EnsureFolder(AnimationsFolder);

            int moved = MoveAnimationFiles();
            List<string> characters = FindFbx(CharactersFolder).Where(p => !IsAnimationFile(p)).ToList();
            List<string> animations = FindFbx(AnimationsFolder).Where(IsAnimationFile).ToList();

            if (characters.Count == 0)
            {
                EditorUtility.DisplayDialog("Mixamo setup", "No character FBX found in " + CharactersFolder + ". Put the character (with skin) there first.", "OK");
                return;
            }

            var report = new List<string>();
            try
            {
                int step = 0, total = characters.Count + animations.Count;

                // Characters first: the animations copy their avatar.
                foreach (string path in characters)
                {
                    EditorUtility.DisplayProgressBar("Mixamo setup", Path.GetFileName(path), (float)step++ / total);
                    report.Add(SetUpCharacter(path));
                }

                foreach (string path in animations)
                {
                    EditorUtility.DisplayProgressBar("Mixamo setup", Path.GetFileName(path), (float)step++ / total);
                    report.Add(SetUpAnimation(path, characters));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string summary = $"Moved {moved} animation file(s) to Art/Animations.\n\n" + string.Join("\n", report);
            Debug.Log("[GTH6] Mixamo setup\n" + summary);
            EditorUtility.DisplayDialog("Mixamo setup done", summary, "OK");
        }

        static List<string> FindFbx(string folder) =>
            AssetDatabase.FindAssets("t:Model", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

        static bool IsAnimationFile(string path) => Path.GetFileNameWithoutExtension(path).Contains("@");

        /// <summary>"Y Bot@Fast Run" gives "Fast Run".</summary>
        static string ActionName(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            int at = file.IndexOf('@');
            return at >= 0 ? file.Substring(at + 1).Trim() : file;
        }

        static int MoveAnimationFiles()
        {
            int moved = 0;
            foreach (string path in FindFbx(CharactersFolder).Where(IsAnimationFile))
            {
                string target = AnimationsFolder + "/" + Path.GetFileName(path);
                string error = AssetDatabase.MoveAsset(path, target);
                if (string.IsNullOrEmpty(error)) moved++;
                else Debug.LogWarning($"[GTH6] Could not move {path}: {error}");
            }
            return moved;
        }

        static string SetUpCharacter(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false; // the character file only carries the mesh and skeleton
            importer.SaveAndReimport();

            Avatar avatar = AvatarAt(path);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                return $"PROBLEM {Path.GetFileName(path)}: Unity could not build a humanoid avatar. Open its Rig tab and press Configure.";
            return $"OK  {Path.GetFileName(path)}: humanoid avatar created";
        }

        static string SetUpAnimation(string path, List<string> characters)
        {
            string prefix = Path.GetFileNameWithoutExtension(path).Split('@')[0].Trim();
            string characterPath = characters.FirstOrDefault(c => Path.GetFileNameWithoutExtension(c) == prefix) ?? characters[0];
            Avatar avatar = AvatarAt(characterPath);

            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // no mesh in "Without Skin" files
            if (avatar != null && avatar.isValid)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            // First reimport makes Unity read the clips as humanoid, so defaultClipAnimations is correct below.
            importer.SaveAndReimport();

            importer = (ModelImporter)AssetImporter.GetAtPath(path);
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return $"PROBLEM {Path.GetFileName(path)}: no animation found in the file";

            string action = ActionName(path);
            bool loop = ShouldLoop(action);
            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation clip = clips[i];
                clip.name = clips.Length == 1 ? action : action + " " + (i + 1);
                clip.loopTime = loop;
                clip.loopPose = loop;

                // Root Transform Rotation: Bake Into Pose, based on Original.
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                // Root Transform Position (Y): Bake Into Pose, based on Feet.
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = false;
                clip.heightFromFeet = true;
                // Root Transform Position (XZ): Bake Into Pose, based on Original.
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            string avatarNote = avatar != null && avatar.isValid ? "avatar from " + Path.GetFileName(characterPath) : "OWN avatar (character avatar missing)";
            return $"OK  {Path.GetFileName(path)}: clip \"{action}\", {(loop ? "looping" : "plays once")}, {avatarNote}";
        }

        static bool ShouldLoop(string action)
        {
            string a = " " + action.ToLowerInvariant() + " ";
            if (OneShotWords.Any(w => a.Contains(w))) return false;
            return LoopWords.Any(w => a.Contains(w));
        }

        static Avatar AvatarAt(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
    }
}
