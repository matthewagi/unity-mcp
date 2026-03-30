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
    /// MCP tools for editor operations:
    ///   - unity_editor_command: Execute editor commands (play, stop, save, compile, etc.)
    ///   - unity_get_selection: Read currently selected objects
    ///   - unity_set_selection: Change selection in hierarchy
    ///   - unity_scene_view: Control SceneView camera (frame, move, orbit)
    /// </summary>
    public static class EditorTools
    {
        // ── unity_editor_command ─────────────────────────────────────────

        public static Dictionary<string, object> EditorCommand(Dictionary<string, object> args)
        {
            string command = TypeConverter.GetString(args, "command");
            if (string.IsNullOrEmpty(command))
                return Error("No command provided");

            try
            {
                switch (command.ToLower())
                {
                    case "play":
                        EditorApplication.isPlaying = true;
                        return Success(command, "Play mode started");

                    case "pause":
                        EditorApplication.isPaused = !EditorApplication.isPaused;
                        return Success(command, EditorApplication.isPaused ? "Paused" : "Resumed");

                    case "stop":
                        EditorApplication.isPlaying = false;
                        return Success(command, "Play mode stopped");

                    case "step":
                        // Single frame step (only works when paused)
                        if (!EditorApplication.isPaused)
                            return Error("Cannot step: not in pause mode. Use 'pause' command first.");
                        EditorApplication.Step();
                        return Success(command, "Stepped one frame");

                    case "save_scene":
                        if (EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                            return Success(command, "Scene saved");
                        else
                            return Error("Failed to save scene");

                    case "save_all":
                        EditorSceneManager.SaveOpenScenes();
                        AssetDatabase.SaveAssets();
                        return Success(command, "All scenes and assets saved");

                    case "undo":
                        Undo.PerformUndo();
                        return Success(command, "Undo performed");

                    case "redo":
                        Undo.PerformRedo();
                        return Success(command, "Redo performed");

                    case "refresh_assets":
                        AssetDatabase.Refresh();
                        return Success(command, "Asset database refreshed");

                    case "compile":
                        // Trigger recompilation
                        AssetDatabase.Refresh();
                        return Success(command, "Recompilation requested");

                    default:
                        // Try as menu item
                        if (EditorApplication.ExecuteMenuItem(command))
                            return Success(command, $"Menu item executed: {command}");
                        else
                            return Error($"Unknown command or menu item: {command}");
                }
            }
            catch (Exception ex)
            {
                return Error($"Command failed: {ex.Message}");
            }
        }

        // ── unity_get_selection ──────────────────────────────────────────

        public static Dictionary<string, object> GetSelection(Dictionary<string, object> args)
        {
            int depth = TypeConverter.GetInt(args, "depth", 1);

            var selected = Selection.gameObjects;
            var result = new List<object>();

            foreach (var go in selected)
            {
                if (go != null)
                {
                    result.Add(new Dictionary<string, object>
                    {
                        { "instance_id", go.GetInstanceID() },
                        { "name", go.name },
                        { "path", GetGameObjectPath(go) },
                        { "scene", go.scene.name },
                        { "component_count", go.GetComponents<Component>().Length },
                        { "child_count", go.transform.childCount }
                    });
                }
            }

            // Also get selected assets
            var selectedAssets = Selection.objects;
            var assetList = new List<object>();
            foreach (var asset in selectedAssets)
            {
                if (asset != null && !(asset is GameObject))
                {
                    string assetPath = AssetDatabase.GetAssetPath(asset);
                    assetList.Add(new Dictionary<string, object>
                    {
                        { "instance_id", asset.GetInstanceID() },
                        { "name", asset.name },
                        { "type", asset.GetType().Name },
                        { "asset_path", assetPath }
                    });
                }
            }

            var response = new Dictionary<string, object>
            {
                { "gameobjects", result },
                { "gameobject_count", result.Count }
            };

            if (assetList.Count > 0)
            {
                response["assets"] = assetList;
                response["asset_count"] = assetList.Count;
            }

            return response;
        }

        // ── unity_set_selection ──────────────────────────────────────────

        public static Dictionary<string, object> SetSelection(Dictionary<string, object> args)
        {
            var ids = TypeConverter.GetList(args, "ids");
            bool ping = TypeConverter.GetBool(args, "ping", false);

            if (ids == null || ids.Count == 0)
                return Error("No ids provided");

            try
            {
                var objects = new List<UnityEngine.Object>();

                foreach (var id in ids)
                {
                    var go = InstanceTracker.ResolveGameObject(id);
                    if (go != null)
                    {
                        objects.Add(go);

                        if (ping)
                            EditorGUIUtility.PingObject(go);
                    }
                    else
                    {
                        // Try as asset
                        string path = id.ToString();
                        if (path.StartsWith("Assets/"))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (asset != null)
                            {
                                objects.Add(asset);
                                if (ping)
                                    EditorGUIUtility.PingObject(asset);
                            }
                        }
                    }
                }

                if (objects.Count == 0)
                    return Error($"No objects found matching ids");

                Selection.objects = objects.ToArray();

                return new Dictionary<string, object>
                {
                    { "selected_count", objects.Count },
                    { "ping", ping }
                };
            }
            catch (Exception ex)
            {
                return Error($"Failed to set selection: {ex.Message}");
            }
        }

        // ── unity_scene_view ────────────────────────────────────────────

        public static Dictionary<string, object> SceneViewCommand(Dictionary<string, object> args)
        {
            string command = TypeConverter.GetString(args, "command");
            if (string.IsNullOrEmpty(command))
                return Error("No command provided");

            try
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    return Error("No active SceneView");

                switch (command.ToLower())
                {
                    case "frame_selected":
                        if (Selection.gameObjects.Length == 0)
                            return Error("No objects selected");
                        sceneView.FrameSelected();
                        return Success(command, "Framed selected object(s)");

                    case "focus_point":
                        var targetDict = TypeConverter.GetDict(args, "target");
                        if (targetDict == null)
                            return Error("No target position provided");
                        var targetPos = TypeConverter.ToVector3(targetDict);
                        sceneView.LookAt(targetPos);
                        return Success(command, $"Focused on {targetPos}");

                    case "move_to":
                        var posDict = TypeConverter.GetDict(args, "position");
                        if (posDict == null)
                            return Error("No position provided");
                        var newPos = TypeConverter.ToVector3(posDict);
                        var camera = sceneView.camera;
                        camera.transform.position = newPos;
                        return Success(command, $"Moved to {newPos}");

                    case "orbit":
                        var orbitDict = TypeConverter.GetDict(args, "rotation");
                        if (orbitDict == null)
                            return Error("No rotation provided");
                        var orbitRot = TypeConverter.ToQuaternion(orbitDict);
                        var orbitCam = sceneView.camera;
                        orbitCam.transform.rotation = orbitRot;
                        return Success(command, "Orbited camera");

                    case "get_camera":
                        var camera2 = sceneView.camera;
                        return new Dictionary<string, object>
                        {
                            { "position", TypeConverter.ToJson(camera2.transform.position) },
                            { "rotation", TypeConverter.ToJson(camera2.transform.rotation) },
                            { "field_of_view", camera2.fieldOfView }
                        };

                    default:
                        return Error($"Unknown SceneView command: {command}");
                }
            }
            catch (Exception ex)
            {
                return Error($"SceneView command failed: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string GetGameObjectPath(GameObject go)
        {
            var path = go.name;
            var current = go.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private static Dictionary<string, object> Success(string command, string message)
        {
            return new Dictionary<string, object>
            {
                { "success", true },
                { "command", command },
                { "message", message }
            };
        }

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
                    { "name", "unity_editor_command" },
                    { "description", "Execute editor commands: play, pause, stop, step, save_scene, save_all, undo, redo, refresh_assets, compile. Can also execute menu items (e.g., 'Assets/Create/Folder')." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "command", new Dictionary<string, object> { { "type", "string" }, { "description", "Command name: play, pause, stop, step, save_scene, save_all, undo, redo, refresh_assets, compile, or EditorApplication.ExecuteMenuItem path" } } }
                                }
                            },
                            { "required", new List<object> { "command" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_get_selection" },
                    { "description", "Get all currently selected GameObjects and assets." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "depth", new Dictionary<string, object> { { "type", "integer" }, { "description", "Detail level. Default: 1" } } }
                                }
                            }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_set_selection" },
                    { "description", "Select one or more GameObjects or assets. Optionally ping them in the hierarchy." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "ids", new Dictionary<string, object> { { "type", "array" }, { "description", "GameObject instance IDs, asset paths, or names" } } },
                                    { "ping", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Flash selected object in hierarchy. Default: false" } } }
                                }
                            },
                            { "required", new List<object> { "ids" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_scene_view" },
                    { "description", "Control SceneView camera: frame_selected, focus_point, move_to, orbit, get_camera." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "command", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<object> { "frame_selected", "focus_point", "move_to", "orbit", "get_camera" } }, { "description", "SceneView command" } } },
                                    { "target", new Dictionary<string, object> { { "type", "object" }, { "description", "Target position for focus_point: {x, y, z}" } } },
                                    { "position", new Dictionary<string, object> { { "type", "object" }, { "description", "Camera position for move_to: {x, y, z}" } } },
                                    { "rotation", new Dictionary<string, object> { { "type", "object" }, { "description", "Camera rotation for orbit: {x, y, z, w} (quaternion) or {euler_x, euler_y, euler_z}" } } }
                                }
                            },
                            { "required", new List<object> { "command" } }
                        }
                    }
                }
            };
        }
    }
}
