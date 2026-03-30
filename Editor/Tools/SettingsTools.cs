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
    /// MCP tools for project settings:
    ///   - unity_get_settings: Read project settings by category
    ///   - unity_set_settings: Write project settings via SerializedProperty
    ///
    /// Supported categories: Physics, Player, Quality, Audio, Time, Graphics, Input, Tags, Editor, Navigation
    /// </summary>
    public static class SettingsTools
    {
        // ── Settings category → asset path mapping ────────────────────

        private static Dictionary<string, string> GetCategoryPath(string category)
        {
            var categoryMap = new Dictionary<string, string>
            {
                { "physics", "ProjectSettings/DynamicsManager.asset" },
                { "player", "ProjectSettings/ProjectSettings.asset" },
                { "quality", "ProjectSettings/QualitySettings.asset" },
                { "audio", "ProjectSettings/AudioManager.asset" },
                { "time", "ProjectSettings/TimeManager.asset" },
                { "graphics", "ProjectSettings/GraphicsSettings.asset" },
                { "input", "ProjectSettings/InputManager.asset" },
                { "tags", "ProjectSettings/TagManager.asset" },
                { "editor", "ProjectSettings/EditorSettings.asset" },
                { "navigation", "ProjectSettings/NavigationSettings.asset" }
            };

            category = category.ToLower();
            if (categoryMap.ContainsKey(category))
            {
                return new Dictionary<string, string>
                {
                    { "path", categoryMap[category] },
                    { "category", category }
                };
            }

            return null;
        }

        // ── unity_get_settings ───────────────────────────────────────────

        public static Dictionary<string, object> GetSettings(Dictionary<string, object> args)
        {
            string category = TypeConverter.GetString(args, "category");
            var properties = TypeConverter.GetList(args, "properties");
            int depth = TypeConverter.GetInt(args, "depth", 2);

            if (string.IsNullOrEmpty(category))
                return Error("No category provided. Supported: Physics, Player, Quality, Audio, Time, Graphics, Input, Tags, Editor, Navigation");

            var pathMap = GetCategoryPath(category);
            if (pathMap == null)
                return Error($"Unknown category: {category}");

            string settingsPath = pathMap["path"];

            try
            {
                var settingsAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(settingsPath);
                if (settingsAsset == null)
                    return Error($"Settings asset not found: {settingsPath}");

                var so = new SerializedObject(settingsAsset);

                var result = new Dictionary<string, object>
                {
                    { "category", category },
                    { "settings_path", settingsPath }
                };

                // If specific properties requested, read only those
                if (properties != null && properties.Count > 0)
                {
                    var requestedProps = new Dictionary<string, object>();
                    foreach (var prop in properties)
                    {
                        string propPath = prop.ToString();
                        requestedProps[propPath] = SerializedPropertyHelper.ReadPropertyByPath(so, propPath, depth);
                    }
                    result["requested_properties"] = requestedProps;
                }
                else
                {
                    // Read all properties
                    result["all_properties"] = SerializedPropertyHelper.ReadAllProperties(so, depth);
                }

                return result;
            }
            catch (Exception ex)
            {
                return Error($"Failed to read settings: {ex.Message}");
            }
        }

        // ── unity_set_settings ───────────────────────────────────────────

        public static Dictionary<string, object> SetSettings(Dictionary<string, object> args)
        {
            string category = TypeConverter.GetString(args, "category");
            var operations = TypeConverter.GetList(args, "operations");

            if (string.IsNullOrEmpty(category))
                return Error("No category provided");

            if (operations == null || operations.Count == 0)
                return Error("No operations provided. Expected: operations[{property_path, value}]");

            var pathMap = GetCategoryPath(category);
            if (pathMap == null)
                return Error($"Unknown category: {category}");

            string settingsPath = pathMap["path"];

            try
            {
                var settingsAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(settingsPath);
                if (settingsAsset == null)
                    return Error($"Settings asset not found: {settingsPath}");

                UndoHelper.BeginGroup($"Modify {category} settings");

                var so = new SerializedObject(settingsAsset);
                var results = new List<object>();
                int successCount = 0;
                int errorCount = 0;

                foreach (var op in operations)
                {
                    var opDict = op as Dictionary<string, object>;
                    if (opDict == null)
                    {
                        results.Add(new Dictionary<string, object> { { "error", "Invalid operation format" } });
                        errorCount++;
                        continue;
                    }

                    string propPath = TypeConverter.GetString(opDict, "property_path");
                    var value = opDict.ContainsKey("value") ? opDict["value"] : null;

                    if (string.IsNullOrEmpty(propPath))
                    {
                        results.Add(new Dictionary<string, object> { { "error", "Missing property_path" } });
                        errorCount++;
                        continue;
                    }

                    try
                    {
                        bool success = SerializedPropertyHelper.WritePropertyByPath(so, propPath, value, $"Set {propPath}");
                        results.Add(new Dictionary<string, object>
                        {
                            { "property_path", propPath },
                            { "success", success },
                            { "new_value", success ? value : null }
                        });

                        if (success) successCount++;
                        else errorCount++;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            { "property_path", propPath },
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
                    { "category", category },
                    { "results", results },
                    { "success_count", successCount },
                    { "error_count", errorCount },
                    { "total", operations.Count }
                };
            }
            catch (Exception ex)
            {
                return Error($"Failed to modify settings: {ex.Message}");
            }
        }

        // ── Helper ───────────────────────────────────────────────────────

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object> { { "error", message } };
        }

        // ── Tool Definitions for MCP ─────────────────────────────────────

        public static List<Dictionary<string, object>> GetToolDefinitions()
        {
            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "name", "unity_get_settings" },
                    { "description", "Read project settings by category. Returns serialized property values for the specified category." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "category", new Dictionary<string, object>
                                        {
                                            { "type", "string" },
                                            { "enum", new List<object> { "Physics", "Player", "Quality", "Audio", "Time", "Graphics", "Input", "Tags", "Editor", "Navigation" } },
                                            { "description", "Settings category" }
                                        }
                                    },
                                    { "properties", new Dictionary<string, object> { { "type", "array" }, { "items", new Dictionary<string, object> { { "type", "string" } } }, { "description", "Specific property paths to read (e.g., 'm_Gravity', 'm_DefaultMaterial'). Omit to read all." } } },
                                    { "depth", new Dictionary<string, object> { { "type", "integer" }, { "description", "Property detail depth. Default: 2" } } }
                                }
                            },
                            { "required", new List<object> { "category" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_set_settings" },
                    { "description", "Modify project settings via SerializedProperty. Changes are saved and undoable." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "category", new Dictionary<string, object>
                                        {
                                            { "type", "string" },
                                            { "enum", new List<object> { "Physics", "Player", "Quality", "Audio", "Time", "Graphics", "Input", "Tags", "Editor", "Navigation" } },
                                            { "description", "Settings category" }
                                        }
                                    },
                                    { "operations", new Dictionary<string, object>
                                        {
                                            { "type", "array" },
                                            { "description", "Array of property modifications" },
                                            { "items", new Dictionary<string, object>
                                                {
                                                    { "type", "object" },
                                                    { "properties", new Dictionary<string, object>
                                                        {
                                                            { "property_path", new Dictionary<string, object> { { "type", "string" }, { "description", "SerializedProperty path (e.g., 'm_Gravity', 'm_DefaultMaterial')" } } },
                                                            { "value", new Dictionary<string, object> { { "description", "New value. Type depends on property." } } }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            { "required", new List<object> { "category", "operations" } }
                        }
                    }
                }
            };
        }
    }
}
