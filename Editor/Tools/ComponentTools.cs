using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Claude.UnityMCP.Communication;
using Claude.UnityMCP.Serialization;
using Claude.UnityMCP.Utils;

namespace Claude.UnityMCP.Tools
{
    /// <summary>
    /// MCP tools for component operations:
    ///   - unity_get_components: Read component data via SerializedProperty
    ///   - unity_set_property: Set ANY serialized property (batched)
    ///   - unity_add_component: Add component to GameObject
    ///   - unity_remove_component: Remove component
    /// </summary>
    public static class ComponentTools
    {
        // ── unity_get_components ──────────────────────────────────────

        public static Dictionary<string, object> GetComponents(Dictionary<string, object> args)
        {
            var objId = args.ContainsKey("object_id") ? args["object_id"] : null;
            if (objId == null) return Error("No object_id provided");

            var go = InstanceTracker.ResolveGameObject(objId);
            if (go == null) return Error($"GameObject not found: {objId}");

            int depth = TypeConverter.GetInt(args, "depth", 2);
            string componentType = TypeConverter.GetString(args, "component_type");

            var components = go.GetComponents<Component>();
            var result = new List<object>();

            foreach (var comp in components)
            {
                if (comp == null) continue;

                // Filter by type if specified
                if (!string.IsNullOrEmpty(componentType) &&
                    comp.GetType().Name != componentType &&
                    comp.GetType().FullName != componentType)
                    continue;

                result.Add(GameObjectSerializer.SerializeComponent(comp, depth));
            }

            // Read specific properties if requested
            var propertyPaths = TypeConverter.GetList(args, "properties");
            Dictionary<string, object> specificProperties = null;
            if (propertyPaths != null && propertyPaths.Count > 0 && !string.IsNullOrEmpty(componentType))
            {
                var comp = go.GetComponent(TypeResolver.ResolveComponentType(componentType));
                if (comp != null)
                {
                    var so = new SerializedObject(comp);
                    specificProperties = new Dictionary<string, object>();
                    foreach (var path in propertyPaths)
                    {
                        string propPath = path.ToString();
                        specificProperties[propPath] = SerializedPropertyHelper.ReadPropertyByPath(so, propPath, depth);
                    }
                }
            }

            var response = new Dictionary<string, object>
            {
                { "object_id", go.GetInstanceID() },
                { "object_name", go.name },
                { "components", result },
                { "component_count", result.Count }
            };

            if (specificProperties != null)
                response["requested_properties"] = specificProperties;

            // During play mode, add live runtime data (SerializedProperty is stale)
            if (EditorApplication.isPlaying)
            {
                var runtime = new Dictionary<string, object>();

                // Live transform
                runtime["position"] = new Dictionary<string, object> {
                    { "x", go.transform.position.x },
                    { "y", go.transform.position.y },
                    { "z", go.transform.position.z }
                };
                runtime["rotation"] = new Dictionary<string, object> {
                    { "x", go.transform.eulerAngles.x },
                    { "y", go.transform.eulerAngles.y },
                    { "z", go.transform.eulerAngles.z }
                };

                // Live Rigidbody data
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    runtime["velocity"] = new Dictionary<string, object> {
                        { "x", rb.linearVelocity.x },
                        { "y", rb.linearVelocity.y },
                        { "z", rb.linearVelocity.z },
                        { "magnitude", rb.linearVelocity.magnitude }
                    };
                    runtime["is_sleeping"] = rb.IsSleeping();
                }

                response["runtime"] = runtime;
            }

            return response;
        }

        // ── unity_set_property (batched) ─────────────────────────────

        public static Dictionary<string, object> SetProperty(Dictionary<string, object> args)
        {
            var operations = TypeConverter.GetList(args, "operations");
            if (operations == null || operations.Count == 0)
                return Error("No operations provided. Expected: operations[{object_id, property_path, value}]");

            UndoHelper.BeginGroup("Set Properties");

            var results = new List<object>();
            int successCount = 0;
            int errorCount = 0;

            foreach (var op in operations)
            {
                var opDict = op as Dictionary<string, object>;
                if (opDict == null) { errorCount++; continue; }

                var objId = opDict.ContainsKey("object_id") ? opDict["object_id"] : null;
                string propPath = TypeConverter.GetString(opDict, "property_path");
                var value = opDict.ContainsKey("value") ? opDict["value"] : null;
                string componentType = TypeConverter.GetString(opDict, "component_type");

                if (objId == null || string.IsNullOrEmpty(propPath))
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error", "Missing object_id or property_path" }
                    });
                    errorCount++;
                    continue;
                }

                try
                {
                    var go = InstanceTracker.ResolveGameObject(objId);
                    if (go == null)
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            { "success", false },
                            { "error", $"GameObject not found: {objId}" }
                        });
                        errorCount++;
                        continue;
                    }

                    // Determine target: component or GameObject
                    UnityEngine.Object target;
                    if (!string.IsNullOrEmpty(componentType))
                    {
                        var type = TypeResolver.ResolveComponentType(componentType);
                        target = type != null ? go.GetComponent(type) : null;
                        if (target == null)
                        {
                            results.Add(new Dictionary<string, object>
                            {
                                { "success", false },
                                { "error", $"Component {componentType} not found on {go.name}" }
                            });
                            errorCount++;
                            continue;
                        }
                    }
                    else
                    {
                        // Try to find the property on any component
                        target = FindPropertyOwner(go, propPath);
                        if (target == null)
                        {
                            results.Add(new Dictionary<string, object>
                            {
                                { "success", false },
                                { "error", $"Property {propPath} not found on any component of {go.name}" },
                                { "suggestion", "Specify component_type to target a specific component" }
                            });
                            errorCount++;
                            continue;
                        }
                    }

                    var so = new SerializedObject(target);
                    bool success = SerializedPropertyHelper.WritePropertyByPath(so, propPath, value, "Set Property");

                    results.Add(new Dictionary<string, object>
                    {
                        { "success", success },
                        { "object_name", go.name },
                        { "component", target.GetType().Name },
                        { "property", propPath }
                    });

                    if (success) successCount++;
                    else errorCount++;
                }
                catch (Exception ex)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error", ex.Message }
                    });
                    errorCount++;
                }
            }

            UndoHelper.MarkSceneDirty();
            UndoHelper.EndGroup();

            return new Dictionary<string, object>
            {
                { "results", results },
                { "success_count", successCount },
                { "error_count", errorCount },
                { "total", operations.Count }
            };
        }

        // ── unity_add_component ──────────────────────────────────────

        public static Dictionary<string, object> AddComponent(Dictionary<string, object> args)
        {
            var objId = args.ContainsKey("object_id") ? args["object_id"] : null;
            string typeName = TypeConverter.GetString(args, "component_type");

            if (objId == null) return Error("No object_id provided");
            if (string.IsNullOrEmpty(typeName)) return Error("No component_type provided");

            var go = InstanceTracker.ResolveGameObject(objId);
            if (go == null) return Error($"GameObject not found: {objId}");

            var type = TypeResolver.ResolveComponentType(typeName);
            if (type == null) return Error($"Component type not found: {typeName}");

            // Check if already has this component (for non-multi components)
            if (!type.GetCustomAttributes(typeof(DisallowMultipleComponent), true).GetEnumerator().MoveNext() == false)
            {
                var existing = go.GetComponent(type);
                if (existing != null)
                    return Error($"{go.name} already has a {typeName} component");
            }

            var comp = UndoHelper.AddComponent(go, type);
            if (comp == null)
                return Error($"Failed to add {typeName} to {go.name}");

            // Initialize properties if provided
            var initProps = TypeConverter.GetDict(args, "init_properties");
            if (initProps != null)
            {
                var so = new SerializedObject(comp);
                foreach (var kvp in initProps)
                {
                    SerializedPropertyHelper.WritePropertyByPath(so, kvp.Key, kvp.Value);
                }
            }

            UndoHelper.MarkSceneDirty();

            return new Dictionary<string, object>
            {
                { "instance_id", comp.GetInstanceID() },
                { "type", comp.GetType().Name },
                { "gameobject", go.name },
                { "gameobject_id", go.GetInstanceID() }
            };
        }

        // ── unity_remove_component ───────────────────────────────────

        public static Dictionary<string, object> RemoveComponent(Dictionary<string, object> args)
        {
            var objId = args.ContainsKey("object_id") ? args["object_id"] : null;
            string typeName = TypeConverter.GetString(args, "component_type");

            if (objId == null) return Error("No object_id provided");
            if (string.IsNullOrEmpty(typeName)) return Error("No component_type provided");

            var go = InstanceTracker.ResolveGameObject(objId);
            if (go == null) return Error($"GameObject not found: {objId}");

            var type = TypeResolver.ResolveComponentType(typeName);
            if (type == null) return Error($"Component type not found: {typeName}");

            var comp = go.GetComponent(type);
            if (comp == null) return Error($"{go.name} does not have a {typeName} component");

            // Prevent removing Transform
            if (comp is Transform)
                return Error("Cannot remove Transform component");

            UndoHelper.DestroyObject(comp, $"Remove {typeName}");
            UndoHelper.MarkSceneDirty();

            return new Dictionary<string, object>
            {
                { "removed", typeName },
                { "gameobject", go.name },
                { "gameobject_id", go.GetInstanceID() }
            };
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static UnityEngine.Object FindPropertyOwner(GameObject go, string propertyPath)
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                if (so.FindProperty(propertyPath) != null)
                    return comp;
            }
            return null;
        }

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object> { { "error", message } };
        }

        // ── Tool Definitions ─────────────────────────────────────────

        public static List<Dictionary<string, object>> GetToolDefinitions()
        {
            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "name", "unity_get_components" },
                    { "description", "Read component data from a GameObject. Returns all components with their serialized properties. Use depth to control detail level." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "object_id", new Dictionary<string, object> { { "description", "Instance ID, path, or name of target GameObject" } } },
                                    { "component_type", new Dictionary<string, object> { { "type", "string" }, { "description", "Filter to specific component type (e.g., 'Rigidbody')" } } },
                                    { "properties", new Dictionary<string, object> { { "type", "array" }, { "description", "Specific property paths to read" } } },
                                    { "depth", new Dictionary<string, object> { { "type", "integer" }, { "description", "Property detail depth. Default: 2" } } }
                                }
                            },
                            { "required", new List<object> { "object_id" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_set_property" },
                    { "description", "Set serialized properties on components. Supports batching multiple operations in one call. Uses Unity's SerializedProperty system so it works with ANY component type." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "operations", new Dictionary<string, object>
                                        {
                                            { "type", "array" },
                                            { "description", "Array of property set operations" },
                                            { "items", new Dictionary<string, object>
                                                {
                                                    { "type", "object" },
                                                    { "properties", new Dictionary<string, object>
                                                        {
                                                            { "object_id", new Dictionary<string, object> { { "description", "Target GameObject ID/path/name" } } },
                                                            { "component_type", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type name (e.g., 'Rigidbody'). Omit to auto-detect." } } },
                                                            { "property_path", new Dictionary<string, object> { { "type", "string" }, { "description", "SerializedProperty path (e.g., 'm_Mass', 'm_UseGravity')" } } },
                                                            { "value", new Dictionary<string, object> { { "description", "New value. Type depends on property." } } }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            { "required", new List<object> { "operations" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_add_component" },
                    { "description", "Add a component to a GameObject. Optionally set initial property values." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "object_id", new Dictionary<string, object> { { "description", "Target GameObject" } } },
                                    { "component_type", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type (e.g., 'Rigidbody', 'BoxCollider', 'AudioSource')" } } },
                                    { "init_properties", new Dictionary<string, object> { { "type", "object" }, { "description", "Initial property values: {property_path: value}" } } }
                                }
                            },
                            { "required", new List<object> { "object_id", "component_type" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_remove_component" },
                    { "description", "Remove a component from a GameObject. Cannot remove Transform." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "object_id", new Dictionary<string, object> { { "description", "Target GameObject" } } },
                                    { "component_type", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type to remove" } } }
                                }
                            },
                            { "required", new List<object> { "object_id", "component_type" } }
                        }
                    }
                }
            };
        }
    }
}
