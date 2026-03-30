#pragma warning disable CS0618 // InstanceIDToObject(int) obsolete in Unity 6
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Claude.UnityMCP.Utils
{
    /// <summary>
    /// Tracks Unity Object instance IDs and provides fast lookup.
    /// Handles the fact that instance IDs can change after undo/redo or domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class InstanceTracker
    {
        // No per-frame updates — zero overhead at all times.
        static InstanceTracker() { }

        public static void HookUpdate() { }
        public static void UnhookUpdate() { }

        // ── Lookup ───────────────────────────────────────────────────

        /// <summary>
        /// Find a GameObject by instance ID. Returns null if not found.
        /// </summary>
        public static GameObject FindGameObject(int instanceId)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceId) as UnityEngine.Object;
            if (obj is GameObject go) return go;
            if (obj is Component comp) return comp.gameObject;
            return null;
        }

        /// <summary>
        /// Find any UnityEngine.Object by instance ID.
        /// </summary>
        public static UnityEngine.Object FindObject(int instanceId)
        {
            return EditorUtility.InstanceIDToObject(instanceId) as UnityEngine.Object;
        }

        /// <summary>
        /// Find a Component by instance ID.
        /// </summary>
        public static Component FindComponent(int instanceId)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceId) as UnityEngine.Object;
            return obj as Component;
        }

        /// <summary>
        /// Find a GameObject by path in the hierarchy (e.g., "Player/Weapon/Model").
        /// </summary>
        public static GameObject FindByPath(string path)
        {
            // Try direct find first
            var go = GameObject.Find(path);
            if (go != null) return go;

            // Try with leading /
            if (!path.StartsWith("/"))
            {
                go = GameObject.Find("/" + path);
                if (go != null) return go;
            }

            // Search all root objects in all loaded scenes
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == path) return root;
                    var found = root.transform.Find(path);
                    if (found != null) return found.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Find GameObjects by name (partial match).
        /// </summary>
        public static List<GameObject> FindByName(string name, bool exactMatch = false)
        {
            var results = new List<GameObject>();
            string lowerName = name.ToLowerInvariant();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    SearchRecursive(root, lowerName, exactMatch, results);
                }
            }

            return results;
        }

        /// <summary>
        /// Find GameObjects with a specific component type.
        /// </summary>
        public static List<GameObject> FindWithComponent(string typeName)
        {
            var results = new List<GameObject>();
            var type = TypeResolver.ResolveComponentType(typeName);
            if (type == null) return results;

            var objects = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None);
            foreach (var obj in objects)
            {
                if (obj is Component comp)
                    results.Add(comp.gameObject);
            }

            return results;
        }

        // ── Resolve flexible ID input ────────────────────────────────

        /// <summary>
        /// Resolve a flexible identifier to a GameObject.
        /// Accepts: instance ID (int), path (string), or name (string).
        /// </summary>
        public static GameObject ResolveGameObject(object identifier)
        {
            if (identifier == null) return null;

            // Instance ID
            if (identifier is int id) return FindGameObject(id);
            if (identifier is long l) return FindGameObject((int)l);
            if (identifier is double d) return FindGameObject((int)d);

            // String path or name
            if (identifier is string s)
            {
                if (int.TryParse(s, out int parsedId))
                    return FindGameObject(parsedId);

                var byPath = FindByPath(s);
                if (byPath != null) return byPath;

                var byName = FindByName(s, true);
                return byName.Count > 0 ? byName[0] : null;
            }

            return null;
        }

        // ── Private Helpers ──────────────────────────────────────────

        private static void SearchRecursive(GameObject go, string lowerName, bool exactMatch, List<GameObject> results)
        {
            string goLower = go.name.ToLowerInvariant();
            if (exactMatch ? goLower == lowerName : goLower.Contains(lowerName))
                results.Add(go);

            for (int i = 0; i < go.transform.childCount; i++)
                SearchRecursive(go.transform.GetChild(i).gameObject, lowerName, exactMatch, results);
        }

        // No per-frame callbacks. Nothing to invalidate.
    }

    /// <summary>
    /// Resolves component type names to System.Type.
    /// Handles both short names ("Rigidbody") and fully qualified ("UnityEngine.Rigidbody").
    /// </summary>
    public static class TypeResolver
    {
        private static readonly Dictionary<string, System.Type> _typeCache = new Dictionary<string, System.Type>();

        public static System.Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            if (_typeCache.TryGetValue(typeName, out var cached))
                return cached;

            System.Type type = null;

            // Try direct lookup
            type = System.Type.GetType(typeName);

            // Try UnityEngine namespace
            if (type == null)
                type = System.Type.GetType($"UnityEngine.{typeName}, UnityEngine");

            // Try UnityEngine.UI
            if (type == null)
                type = System.Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");

            // Search all loaded assemblies
            if (type == null)
            {
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(typeName);
                    if (type != null) break;

                    // Try with UnityEngine prefix
                    type = assembly.GetType($"UnityEngine.{typeName}");
                    if (type != null) break;
                }
            }

            if (type != null)
                _typeCache[typeName] = type;

            return type;
        }
    }
}
