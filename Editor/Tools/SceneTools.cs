using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Claude.UnityMCP.Communication;
using Claude.UnityMCP.Serialization;
using Claude.UnityMCP.Utils;

namespace Claude.UnityMCP.Tools
{
    /// <summary>
    /// MCP tools for scene hierarchy operations:
    ///   - unity_get_scene: Read scene hierarchy with depth/filter
    ///   - unity_create_gameobject: Create GO with components
    ///   - unity_delete: Delete GameObjects
    ///   - unity_duplicate: Clone GameObjects
    /// </summary>
    public static class SceneTools
    {
        // ── unity_get_scene ──────────────────────────────────────────

        public static Dictionary<string, object> GetScene(Dictionary<string, object> args)
        {
            int depth = TypeConverter.GetInt(args, "depth", 2);
            bool includeInactive = TypeConverter.GetBool(args, "include_inactive", true);
            string sceneName = TypeConverter.GetString(args, "scene_name");

            // Parse filters
            HashSet<string> filterTags = null;
            var tagsArr = TypeConverter.GetList(args, "filter_tags");
            if (tagsArr != null && tagsArr.Count > 0)
            {
                filterTags = new HashSet<string>();
                foreach (var t in tagsArr) filterTags.Add(t.ToString());
            }

            HashSet<string> filterComponents = null;
            var compsArr = TypeConverter.GetList(args, "filter_components");
            if (compsArr != null && compsArr.Count > 0)
            {
                filterComponents = new HashSet<string>();
                foreach (var c in compsArr) filterComponents.Add(c.ToString());
            }

            // Get scene(s)
            var scenes = new List<object>();

            if (!string.IsNullOrEmpty(sceneName))
            {
                var scene = SceneManager.GetSceneByName(sceneName);
                if (scene.IsValid())
                    scenes.Add(GameObjectSerializer.SerializeScene(scene, depth, filterTags, filterComponents, includeInactive));
                else
                    return Error($"Scene not found: {sceneName}");
            }
            else
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded)
                        scenes.Add(GameObjectSerializer.SerializeScene(scene, depth, filterTags, filterComponents, includeInactive));
                }
            }

            return new Dictionary<string, object>
            {
                { "scene_count", SceneManager.sceneCount },
                { "active_scene", SceneManager.GetActiveScene().name },
                { "scenes", scenes }
            };
        }

        // ── unity_create_gameobject ──────────────────────────────────

        public static Dictionary<string, object> CreateGameObject(Dictionary<string, object> args)
        {
            string name = TypeConverter.GetString(args, "name", "New GameObject");

            UndoHelper.BeginGroup($"Create {name}");
            try
            {
                // Create as primitive if requested (auto mesh + collider)
                GameObject go;
                string primitiveType = TypeConverter.GetString(args, "primitive");
                if (!string.IsNullOrEmpty(primitiveType))
                {
                    PrimitiveType prim;
                    switch (primitiveType.ToLower())
                    {
                        case "cube":     prim = PrimitiveType.Cube; break;
                        case "sphere":   prim = PrimitiveType.Sphere; break;
                        case "capsule":  prim = PrimitiveType.Capsule; break;
                        case "cylinder": prim = PrimitiveType.Cylinder; break;
                        case "plane":    prim = PrimitiveType.Plane; break;
                        case "quad":     prim = PrimitiveType.Quad; break;
                        default:         prim = PrimitiveType.Cube; break;
                    }
                    // Use GameObject.CreatePrimitive directly — it preserves all
                    // built-in components (MeshFilter, MeshRenderer, Collider).
                    // NOTE: Undo.RegisterCreatedObjectUndo strips primitive components
                    // in Unity 6, so we skip it for primitives. The scene is still
                    // marked dirty so Unity prompts to save.
                    go = GameObject.CreatePrimitive(prim);
                    go.name = name;
                    Debug.Log($"[MCP DEBUG] Primitive '{name}' created with {go.GetComponents<Component>().Length} components");
                }
                else
                {
                    go = ObjectFactory.CreateGameObject(name);
                }

                // Set parent
                var parentId = args.ContainsKey("parent_id") ? args["parent_id"] : null;
                if (parentId != null)
                {
                    var parent = InstanceTracker.ResolveGameObject(parentId);
                    if (parent != null)
                        UndoHelper.SetParent(go.transform, parent.transform, "Set Parent");
                }

                // Set transform
                var transformDict = TypeConverter.GetDict(args, "transform");
                if (transformDict != null)
                {
                    ApplyTransform(go.transform, transformDict);
                }

                // Set tag
                string tag = TypeConverter.GetString(args, "tag");
                if (!string.IsNullOrEmpty(tag))
                    go.tag = tag;

                // Set layer
                if (args.ContainsKey("layer"))
                    go.layer = TypeConverter.GetInt(args, "layer");

                // Set static
                if (args.ContainsKey("is_static"))
                    go.isStatic = TypeConverter.GetBool(args, "is_static");

                // Add components
                var componentsList = TypeConverter.GetList(args, "components");
                if (componentsList != null)
                {
                    foreach (var compDef in componentsList)
                    {
                        if (compDef is string typeName)
                        {
                            AddComponentByName(go, typeName);
                        }
                        else if (compDef is Dictionary<string, object> compDict)
                        {
                            string compType = TypeConverter.GetString(compDict, "type");
                            var comp = AddComponentByName(go, compType);

                            // Initialize properties
                            var initProps = TypeConverter.GetDict(compDict, "properties");
                            if (comp != null && initProps != null)
                            {
                                var so = new SerializedObject(comp);
                                foreach (var kvp in initProps)
                                {
                                    SerializedPropertyHelper.WritePropertyByPath(so, kvp.Key, kvp.Value);
                                }
                            }
                        }
                    }
                }

                UndoHelper.MarkSceneDirty();
                UndoHelper.EndGroup();

                return new Dictionary<string, object>
                {
                    { "instance_id", go.GetInstanceID() },
                    { "name", go.name },
                    { "created", true }
                };
            }
            catch (Exception ex)
            {
                UndoHelper.EndGroup();
                return Error($"Failed to create GameObject: {ex.Message}");
            }
        }

        // ── unity_delete ─────────────────────────────────────────────

        public static Dictionary<string, object> Delete(Dictionary<string, object> args)
        {
            var ids = TypeConverter.GetList(args, "ids");
            if (ids == null || ids.Count == 0)
                return Error("No ids provided");

            string deleteType = TypeConverter.GetString(args, "type", "gameobject");
            var deleted = new List<object>();
            var errors = new List<object>();

            UndoHelper.BeginGroup("Delete Objects");

            foreach (var id in ids)
            {
                try
                {
                    if (deleteType == "asset")
                    {
                        string path = id.ToString();
                        if (AssetDatabase.DeleteAsset(path))
                            deleted.Add(path);
                        else
                            errors.Add($"Failed to delete asset: {path}");
                    }
                    else
                    {
                        var go = InstanceTracker.ResolveGameObject(id);
                        if (go != null)
                        {
                            deleted.Add(new Dictionary<string, object>
                            {
                                { "instance_id", go.GetInstanceID() },
                                { "name", go.name }
                            });
                            UndoHelper.DestroyObject(go, $"Delete {go.name}");
                        }
                        else
                        {
                            errors.Add($"GameObject not found: {id}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error deleting {id}: {ex.Message}");
                }
            }

            UndoHelper.MarkSceneDirty();
            UndoHelper.EndGroup();

            var result = new Dictionary<string, object>
            {
                { "deleted", deleted },
                { "deleted_count", deleted.Count }
            };
            if (errors.Count > 0)
                result["errors"] = errors;

            return result;
        }

        // ── unity_duplicate ──────────────────────────────────────────

        public static Dictionary<string, object> Duplicate(Dictionary<string, object> args)
        {
            var sourceId = args.ContainsKey("id") ? args["id"] : null;
            if (sourceId == null)
                return Error("No id provided");

            var source = InstanceTracker.ResolveGameObject(sourceId);
            if (source == null)
                return Error($"GameObject not found: {sourceId}");

            string newName = TypeConverter.GetString(args, "new_name");
            var newParentId = args.ContainsKey("new_parent_id") ? args["new_parent_id"] : null;

            UndoHelper.BeginGroup($"Duplicate {source.name}");

            var clone = UnityEngine.Object.Instantiate(source);
            UndoHelper.RegisterCreated(clone, $"Duplicate {source.name}");

            if (!string.IsNullOrEmpty(newName))
                clone.name = newName;
            else
                clone.name = source.name; // Remove "(Clone)" suffix

            if (newParentId != null)
            {
                var parent = InstanceTracker.ResolveGameObject(newParentId);
                if (parent != null)
                    UndoHelper.SetParent(clone.transform, parent.transform, "Set Parent");
            }
            else
            {
                // Keep same parent as source
                UndoHelper.SetParent(clone.transform, source.transform.parent, "Set Parent");
            }

            UndoHelper.MarkSceneDirty();
            UndoHelper.EndGroup();

            return new Dictionary<string, object>
            {
                { "instance_id", clone.GetInstanceID() },
                { "name", clone.name },
                { "source_id", source.GetInstanceID() }
            };
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static void ApplyTransform(Transform t, Dictionary<string, object> dict)
        {
            var pos = TypeConverter.GetDict(dict, "position") ?? TypeConverter.GetDict(dict, "local_position");
            if (pos != null) t.localPosition = TypeConverter.ToVector3(pos);

            var rot = TypeConverter.GetDict(dict, "rotation") ?? TypeConverter.GetDict(dict, "local_rotation");
            if (rot != null)
            {
                // Support euler angles
                if (rot.ContainsKey("euler_x") || rot.ContainsKey("euler_y") || rot.ContainsKey("euler_z"))
                    t.localRotation = TypeConverter.ToQuaternion(rot);
                else if (rot.ContainsKey("x"))
                    t.localEulerAngles = TypeConverter.ToVector3(rot);
            }

            var scale = TypeConverter.GetDict(dict, "scale") ?? TypeConverter.GetDict(dict, "local_scale");
            if (scale != null) t.localScale = TypeConverter.ToVector3(scale);
        }

        private static Component AddComponentByName(GameObject go, string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            var type = TypeResolver.ResolveComponentType(typeName);
            if (type == null)
            {
                Debug.LogWarning($"[MCP] Component type not found: {typeName}");
                return null;
            }

            return UndoHelper.AddComponent(go, type);
        }

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object>
            {
                { "error", message },
                { "suggestion", "Use unity_get_scene to find valid object IDs and names" }
            };
        }

        // ── Tool Definitions for MCP ─────────────────────────────────

        public static List<Dictionary<string, object>> GetToolDefinitions()
        {
            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "name", "unity_get_scene" },
                    { "description", "Get the scene hierarchy as structured data. Control detail level with depth parameter. Use filter_tags and filter_components to narrow results." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "depth", new Dictionary<string, object> { { "type", "integer" }, { "description", "Hierarchy depth (0=names only, 1=+transform/tags, 2=+component summaries, 3+=full properties). Default: 2" } } },
                                    { "scene_name", new Dictionary<string, object> { { "type", "string" }, { "description", "Specific scene name. Omit for all loaded scenes." } } },
                                    { "filter_tags", new Dictionary<string, object> { { "type", "array" }, { "items", new Dictionary<string, object> { { "type", "string" } } }, { "description", "Only include GameObjects with these tags" } } },
                                    { "filter_components", new Dictionary<string, object> { { "type", "array" }, { "items", new Dictionary<string, object> { { "type", "string" } } }, { "description", "Only include GameObjects with these component types" } } },
                                    { "include_inactive", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Include inactive GameObjects. Default: true" } } }
                                }
                            }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_create_gameobject" },
                    { "description", "Create a new GameObject with optional components, parent, and transform. Components can include initial property values." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "name", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject name" } } },
                                    { "primitive", new Dictionary<string, object> { { "type", "string" }, { "description", "Create as primitive with built-in mesh: cube, sphere, capsule, cylinder, plane, quad" } } },
                                    { "parent_id", new Dictionary<string, object> { { "description", "Parent instance ID, path, or name" } } },
                                    { "transform", new Dictionary<string, object> { { "type", "object" }, { "description", "Transform: {position:{x,y,z}, rotation:{x,y,z}, scale:{x,y,z}}" } } },
                                    { "tag", new Dictionary<string, object> { { "type", "string" } } },
                                    { "layer", new Dictionary<string, object> { { "type", "integer" } } },
                                    { "is_static", new Dictionary<string, object> { { "type", "boolean" } } },
                                    { "components", new Dictionary<string, object> { { "type", "array" }, { "description", "Components to add. Each: string type name OR {type, properties:{...}}" } } }
                                }
                            },
                            { "required", new List<object> { "name" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_delete" },
                    { "description", "Delete GameObjects or assets by ID/path. Undoable." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "ids", new Dictionary<string, object> { { "type", "array" }, { "description", "Instance IDs, paths, or names to delete" } } },
                                    { "type", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<object> { "gameobject", "asset" } }, { "description", "What to delete. Default: gameobject" } } }
                                }
                            },
                            { "required", new List<object> { "ids" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_duplicate" },
                    { "description", "Duplicate a GameObject. Optionally rename and reparent the clone." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "id", new Dictionary<string, object> { { "description", "Instance ID, path, or name of source GameObject" } } },
                                    { "new_name", new Dictionary<string, object> { { "type", "string" } } },
                                    { "new_parent_id", new Dictionary<string, object> { { "description", "Parent for the clone" } } }
                                }
                            },
                            { "required", new List<object> { "id" } }
                        }
                    }
                }
            };
        }
    }
}
