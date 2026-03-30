using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Claude.UnityMCP.Serialization
{
    /// <summary>
    /// Serializes GameObjects and their hierarchies to JSON-friendly dictionaries.
    /// Supports depth-limited traversal to prevent context explosion.
    ///
    /// Depth levels:
    ///   0 = name + instance_id only
    ///   1 = + component type names, transform, tag, layer
    ///   2 = + component property summaries (key properties only)
    ///   3+ = + full component property trees via SerializedPropertyHelper
    /// </summary>
    public static class GameObjectSerializer
    {
        // ── Main Serialization Entry Point ───────────────────────────

        public static Dictionary<string, object> Serialize(
            GameObject go,
            int depth = 2,
            bool includeChildren = true,
            HashSet<string> filterTags = null,
            HashSet<string> filterComponents = null,
            bool includeInactive = true)
        {
            if (go == null) return null;

            // Skip inactive if not requested
            if (!includeInactive && !go.activeInHierarchy) return null;

            // Tag filter
            if (filterTags != null && filterTags.Count > 0 && !filterTags.Contains(go.tag))
            {
                // Still include if children might match
                if (!includeChildren) return null;
            }

            var result = new Dictionary<string, object>
            {
                { "instance_id", go.GetInstanceID() },
                { "name", go.name }
            };

            if (depth >= 1)
            {
                result["tag"] = go.tag;
                result["layer"] = go.layer;
                result["layer_name"] = LayerMask.LayerToName(go.layer);
                result["active_self"] = go.activeSelf;
                result["active_hierarchy"] = go.activeInHierarchy;
                result["is_static"] = go.isStatic;

                // Transform (always included at depth >= 1)
                var t = go.transform;
                result["transform"] = new Dictionary<string, object>
                {
                    { "local_position", TypeConverter.ToJson(t.localPosition) },
                    { "local_rotation", TypeConverter.ToJson(t.localRotation) },
                    { "local_scale", TypeConverter.ToJson(t.localScale) },
                    { "world_position", TypeConverter.ToJson(t.position) },
                    { "world_rotation", TypeConverter.ToJson(t.rotation) }
                };

                // Component list (type names at depth 1, details at depth 2+)
                result["components"] = SerializeComponents(go, depth, filterComponents);

                // Prefab info
                if (PrefabUtility.IsPartOfAnyPrefab(go))
                {
                    result["prefab"] = new Dictionary<string, object>
                    {
                        { "is_prefab_instance", PrefabUtility.IsPartOfPrefabInstance(go) },
                        { "is_prefab_asset", PrefabUtility.IsPartOfPrefabAsset(go) },
                        { "prefab_path", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) }
                    };
                }
            }

            // Children
            if (includeChildren && depth > 0)
            {
                var children = new List<object>();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = go.transform.GetChild(i).gameObject;
                    var serialized = Serialize(child, depth - 1, true, filterTags, filterComponents, includeInactive);
                    if (serialized != null)
                        children.Add(serialized);
                }
                if (children.Count > 0)
                    result["children"] = children;
                result["child_count"] = go.transform.childCount;
            }
            else if (go.transform.childCount > 0)
            {
                result["child_count"] = go.transform.childCount;
            }

            return result;
        }

        // ── Component Serialization ──────────────────────────────────

        private static object SerializeComponents(GameObject go, int depth, HashSet<string> filter)
        {
            var components = go.GetComponents<Component>();
            var result = new List<object>();

            foreach (var comp in components)
            {
                if (comp == null) continue; // Missing script
                if (comp is Transform) continue; // Transform handled separately

                string typeName = comp.GetType().Name;

                // Apply component filter
                if (filter != null && filter.Count > 0 && !filter.Contains(typeName))
                    continue;

                if (depth <= 1)
                {
                    // Just type names
                    result.Add(typeName);
                }
                else
                {
                    // Serialize component properties
                    result.Add(SerializeComponent(comp, depth - 1));
                }
            }

            return result;
        }

        public static Dictionary<string, object> SerializeComponent(Component comp, int depth = 2)
        {
            if (comp == null)
                return new Dictionary<string, object> { { "error", "missing_script" } };

            var result = new Dictionary<string, object>
            {
                { "type", comp.GetType().Name },
                { "full_type", comp.GetType().FullName },
                { "instance_id", comp.GetInstanceID() }
            };

            // Enabled state (for Behaviour-derived components)
            if (comp is Behaviour behaviour)
                result["enabled"] = behaviour.enabled;
            if (comp is Renderer renderer)
                result["enabled"] = renderer.enabled;
            if (comp is Collider collider)
                result["enabled"] = collider.enabled;

            // Serialize all properties via SerializedObject
            if (depth > 0)
            {
                try
                {
                    var so = new SerializedObject(comp);
                    result["properties"] = SerializedPropertyHelper.ReadAllProperties(so, depth);
                }
                catch (Exception ex)
                {
                    result["properties_error"] = ex.Message;
                }
            }

            return result;
        }

        // ── Scene Serialization ──────────────────────────────────────

        public static Dictionary<string, object> SerializeScene(
            UnityEngine.SceneManagement.Scene scene,
            int depth = 2,
            HashSet<string> filterTags = null,
            HashSet<string> filterComponents = null,
            bool includeInactive = true)
        {
            var result = new Dictionary<string, object>
            {
                { "name", scene.name },
                { "path", scene.path },
                { "is_dirty", scene.isDirty },
                { "is_loaded", scene.isLoaded },
                { "build_index", scene.buildIndex },
                { "root_count", scene.rootCount }
            };

            if (scene.isLoaded)
            {
                var roots = new List<object>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    var serialized = Serialize(root, depth, true, filterTags, filterComponents, includeInactive);
                    if (serialized != null)
                        roots.Add(serialized);
                }
                result["root_objects"] = roots;
            }

            return result;
        }
    }
}
