using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Claude.UnityMCP.Communication;
using Claude.UnityMCP.Tools;

namespace Claude.UnityMCP
{
    /// <summary>
    /// Minimal MCP server. ONE static EditorApplication.update callback that
    /// checks a lock-free queue — does nothing when empty (nanosecond cost).
    /// TCP server on background thread uses Socket.Poll — zero CPU when idle.
    /// NO async, NO Task, NO ThreadPool, NO dynamic hook management.
    /// </summary>
    [InitializeOnLoad]
    public static class MCPServer
    {
        private static StreamableHttpServer _server;
        public static bool IsRunning => _server?.IsRunning ?? false;
        public static int Port { get; private set; } = 9999;
        public static bool Enabled
        {
            get => EditorPrefs.GetBool("MCP_Enabled", true);
            set => EditorPrefs.SetBool("MCP_Enabled", value);
        }

        // ── Init ─────────────────────────────────────────────────────

        static MCPServer()
        {
            Port = EditorPrefs.GetInt("MCP_Port", 9999);
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;

            // delayCall ensures editor is fully initialized before we hook update + start
            EditorApplication.delayCall += () =>
            {
                EditorApplication.update += Tick;
                Enabled = true;
                Start();
            };
        }

        /// <summary>
        /// Called every editor frame. Checks the dispatcher queue.
        /// When empty: single boolean check (~0 ns). Zero overhead.
        /// </summary>
        private static void Tick()
        {
            MainThreadDispatcher.ProcessPending();
        }

        // ── Start / Stop ─────────────────────────────────────────────

        public static void Start()
        {
            if (_server != null && _server.IsRunning) return;
            Stop();

            _server = new StreamableHttpServer(Port);
            _server.OnRequestSync = HandleRequestSync;
            _server.Start();
            Debug.Log($"[MCP] Ready on port {Port}");
        }

        public static void Stop()
        {
            if (_server != null)
            {
                try { _server.Stop(); } catch { }
                try { _server.Dispose(); } catch { }
                _server = null;
            }
            MainThreadDispatcher.DrainAndCancel();
        }

        public static void Restart(int newPort = -1)
        {
            Stop();
            if (newPort > 0)
            {
                Port = newPort;
                EditorPrefs.SetInt("MCP_Port", newPort);
            }
            Start();
        }

        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (enabled) Start();
            else Stop();
        }

        // ── Synchronous Request Handler (runs on BG thread) ──────────

        private static string HandleRequestSync(string rawJson)
        {
            var request = JsonRpcHandler.ParseRequest(rawJson);
            if (request == null)
                return JsonRpcHandler.ErrorResponse(null, JsonRpcHandler.PARSE_ERROR, "Parse failed");
            if (string.IsNullOrEmpty(request.method))
                return JsonRpcHandler.ErrorResponse(request.id, JsonRpcHandler.INVALID_REQUEST, "No method");

            try
            {
                switch (request.method)
                {
                    case "initialize":
                        return JsonRpcHandler.InitializeResponse(request.id);
                    case "initialized":
                        return JsonRpcHandler.SuccessResponse(request.id, new Dictionary<string, object>());
                    case "ping":
                        return JsonRpcHandler.SuccessResponse(request.id, new Dictionary<string, object> { { "status", "ok" } });
                    case "tools/list":
                        return HandleToolsList(request.id);
                    case "tools/call":
                        return HandleToolCall(request.id, request.@params);
                    default:
                        return JsonRpcHandler.ErrorResponse(request.id, JsonRpcHandler.METHOD_NOT_FOUND, $"Unknown: {request.method}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] {request.method}: {ex}");
                return JsonRpcHandler.ErrorResponse(request.id, JsonRpcHandler.INTERNAL_ERROR, ex.Message);
            }
        }

        private static string HandleToolsList(object requestId)
        {
            var allTools = new List<object>();
            allTools.AddRange(SceneTools.GetToolDefinitions());
            allTools.AddRange(ComponentTools.GetToolDefinitions());
            allTools.AddRange(AssetTools.GetToolDefinitions());
            allTools.AddRange(ScriptTools.GetToolDefinitions());
            allTools.AddRange(EditorTools.GetToolDefinitions());
            allTools.AddRange(SettingsTools.GetToolDefinitions());
            allTools.AddRange(BuildTools.GetToolDefinitions());
            allTools.AddRange(ExecuteTools.GetToolDefinitions());
            return JsonRpcHandler.SuccessResponse(requestId, new Dictionary<string, object> { { "tools", allTools } });
        }

        /// <summary>
        /// Fully synchronous tool call. Blocks BG thread while main thread executes.
        /// </summary>
        private static string HandleToolCall(object requestId, string paramsJson)
        {
            if (string.IsNullOrEmpty(paramsJson))
                return JsonRpcHandler.ErrorResponse(requestId, JsonRpcHandler.INVALID_PARAMS, "Missing params");

            var paramsDict = MiniJson.DeserializeObject(paramsJson);
            if (paramsDict == null)
                return JsonRpcHandler.ErrorResponse(requestId, JsonRpcHandler.INVALID_PARAMS, "Bad params");

            string toolName = paramsDict.ContainsKey("name") ? paramsDict["name"]?.ToString() : null;
            if (string.IsNullOrEmpty(toolName))
                return JsonRpcHandler.ErrorResponse(requestId, JsonRpcHandler.INVALID_PARAMS, "Missing tool name");

            var arguments = paramsDict.ContainsKey("arguments")
                ? paramsDict["arguments"] as Dictionary<string, object>
                : new Dictionary<string, object>();
            if (arguments == null)
            {
                var argsStr = paramsDict.ContainsKey("arguments") ? paramsDict["arguments"]?.ToString() : null;
                arguments = !string.IsNullOrEmpty(argsStr)
                    ? MiniJson.DeserializeObject(argsStr) ?? new Dictionary<string, object>()
                    : new Dictionary<string, object>();
            }

            // Run tool on main thread, block until done
            Dictionary<string, object> toolResult = null;
            MainThreadDispatcher.RunOnMainThreadBlocking(() =>
            {
                toolResult = DispatchTool(toolName, arguments);
            });

            var content = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "type", "text" },
                    { "text", MiniJson.Serialize(toolResult) }
                }
            };

            return JsonRpcHandler.SuccessResponse(requestId, new Dictionary<string, object>
            {
                { "content", content },
                { "isError", toolResult != null && toolResult.ContainsKey("error") }
            });
        }

        // ── Dispatch ─────────────────────────────────────────────────

        private static Dictionary<string, object> DispatchTool(string toolName, Dictionary<string, object> args)
        {
            try
            {
                switch (toolName)
                {
                    case "unity_get_scene": return SceneTools.GetScene(args);
                    case "unity_create_gameobject": return SceneTools.CreateGameObject(args);
                    case "unity_delete": return SceneTools.Delete(args);
                    case "unity_duplicate": return SceneTools.Duplicate(args);
                    case "unity_get_components": return ComponentTools.GetComponents(args);
                    case "unity_set_property": return ComponentTools.SetProperty(args);
                    case "unity_add_component": return ComponentTools.AddComponent(args);
                    case "unity_remove_component": return ComponentTools.RemoveComponent(args);
                    case "unity_search_assets": return AssetTools.SearchAssets(args);
                    case "unity_get_asset": return AssetTools.GetAsset(args);
                    case "unity_create_asset": return AssetTools.CreateAsset(args);
                    case "unity_import_asset": return AssetTools.ImportAsset(args);
                    case "unity_create_script": return ScriptTools.CreateScript(args);
                    case "unity_modify_script": return ScriptTools.ModifyScript(args);
                    case "unity_editor_command": return EditorTools.EditorCommand(args);
                    case "unity_get_selection": return EditorTools.GetSelection(args);
                    case "unity_set_selection": return EditorTools.SetSelection(args);
                    case "unity_scene_view": return EditorTools.SceneViewCommand(args);
                    case "unity_get_settings": return SettingsTools.GetSettings(args);
                    case "unity_set_settings": return SettingsTools.SetSettings(args);
                    case "unity_build": return BuildTools.UnityBuild(args);
                    case "unity_manage_packages": return BuildTools.UnityManagePackages(args);
                    case "unity_get_console": return BuildTools.UnityGetConsole(args);
                    case "unity_execute_csharp": return ExecuteTools.ExecuteCSharp(args);
                    default:
                        return new Dictionary<string, object> { { "error", $"Unknown tool: {toolName}" } };
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "error", ex.Message },
                    { "tool", toolName },
                    { "stack_trace", ex.StackTrace }
                };
            }
        }
    }
}
