using System.IO;
using UnityEditor;
using UnityEngine;

namespace GTH6.EditorTools
{
    /// <summary>Menu GTH6 > Create Folder Structure. Safe to run more than once.</summary>
    public static class ProjectFolderSetup
    {
        static readonly string[] Folders =
        {
            "Assets/_Project/Art/Characters",
            "Assets/_Project/Art/Animations",
            "Assets/_Project/Art/Materials",
            "Assets/_Project/Art/Models",
            "Assets/_Project/Art/Textures",
            "Assets/_Project/Art/VFX",
            "Assets/_Project/Audio/Music",
            "Assets/_Project/Audio/Radio",
            "Assets/_Project/Audio/SFX",
            "Assets/_Project/Data/Vehicles",
            "Assets/_Project/Data/Weapons",
            "Assets/_Project/Data/Missions",
            "Assets/_Project/Data/Shop",
            "Assets/_Project/Prefabs/Player",
            "Assets/_Project/Prefabs/Vehicles",
            "Assets/_Project/Prefabs/NPC",
            "Assets/_Project/Prefabs/World",
            "Assets/_Project/Scenes",
            "Assets/_Project/Scripts/Camera",
            "Assets/_Project/Scripts/Combat",
            "Assets/_Project/Scripts/Core",
            "Assets/_Project/Scripts/Player",
            "Assets/_Project/Scripts/Editor",
            "Assets/_Project/Settings",
            "Assets/_Project/Timeline"
        };

        [MenuItem("GTH6/Create Folder Structure")]
        static void CreateFolders()
        {
            foreach (string folder in Folders) EnsureFolder(folder);
            AssetDatabase.Refresh();
            Debug.Log("[GTH6] Folder structure ready under Assets/_Project.");
        }

        /// <summary>Creates a folder and any missing parents, e.g. "Assets/A/B/C".</summary>
        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
