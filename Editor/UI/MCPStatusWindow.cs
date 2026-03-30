using UnityEditor;
using UnityEngine;

namespace Claude.UnityMCP.UI
{
    /// <summary>
    /// Editor window showing MCP server status, connection info, and controls.
    /// Access via Window → Claude MCP.
    /// </summary>
    public class MCPStatusWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private int _port;
        private bool _showAdvanced;

        [MenuItem("Window/Claude MCP")]
        public static void ShowWindow()
        {
            var window = GetWindow<MCPStatusWindow>("Claude MCP");
            window.minSize = new Vector2(300, 200);
        }

        private void OnEnable()
        {
            _port = MCPServer.Port;
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // ── Header ───────────────────────────────────────────────
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Claude Unity MCP Server", EditorStyles.boldLabel);
            GUILayout.Space(5);

            // ── Status ───────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status:", GUILayout.Width(60));
            if (MCPServer.IsRunning)
            {
                var style = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.green } };
                EditorGUILayout.LabelField("Running", style);
            }
            else
            {
                var style = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.red } };
                EditorGUILayout.LabelField("Stopped", style);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"Endpoint: http://localhost:{MCPServer.Port}/mcp");
            GUILayout.Space(10);

            // ── Enable/Disable Toggle ────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("MCP Server:", GUILayout.Width(85));
            bool wasEnabled = MCPServer.Enabled;
            bool nowEnabled = EditorGUILayout.Toggle(wasEnabled, GUILayout.Width(20));
            EditorGUILayout.LabelField(nowEnabled ? "Enabled" : "Disabled (won't auto-start)");
            if (nowEnabled != wasEnabled)
                MCPServer.SetEnabled(nowEnabled);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            // ── Controls ─────────────────────────────────────────────
            EditorGUI.BeginDisabledGroup(!MCPServer.Enabled);
            EditorGUILayout.BeginHorizontal();
            if (MCPServer.IsRunning)
            {
                if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
                    MCPServer.Stop();
            }
            else
            {
                if (GUILayout.Button("Start Server", GUILayout.Height(30)))
                    MCPServer.Start();
            }

            if (GUILayout.Button("Restart", GUILayout.Height(30)))
                MCPServer.Restart();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(10);

            // ── Connection Info ───────────────────────────────────────
            EditorGUILayout.LabelField("Connection Info", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Add this to your Claude Code / MCP client config:\n\n" +
                "{\n" +
                "  \"mcpServers\": {\n" +
                "    \"unity\": {\n" +
                $"      \"url\": \"http://localhost:{MCPServer.Port}/mcp\"\n" +
                "    }\n" +
                "  }\n" +
                "}",
                MessageType.Info);

            if (GUILayout.Button("Copy Config to Clipboard"))
            {
                GUIUtility.systemCopyBuffer =
                    "{\n" +
                    "  \"mcpServers\": {\n" +
                    "    \"unity\": {\n" +
                    $"      \"url\": \"http://localhost:{MCPServer.Port}/mcp\"\n" +
                    "    }\n" +
                    "  }\n" +
                    "}";
                Debug.Log("[MCP] Config copied to clipboard.");
            }

            GUILayout.Space(10);

            // ── Advanced Settings ─────────────────────────────────────
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced Settings");
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Port:", GUILayout.Width(40));
                _port = EditorGUILayout.IntField(_port);
                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    EditorPrefs.SetInt("MCP_Port", _port);
                    MCPServer.Restart(_port);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);

            // ── Available Tools ───────────────────────────────────────
            EditorGUILayout.LabelField("Available Tools (24)", EditorStyles.boldLabel);

            DrawToolCategory("Scene & Hierarchy", new[] { "unity_get_scene", "unity_create_gameobject", "unity_delete", "unity_duplicate" });
            DrawToolCategory("Components", new[] { "unity_get_components", "unity_set_property", "unity_add_component", "unity_remove_component" });
            DrawToolCategory("Assets", new[] { "unity_search_assets", "unity_get_asset", "unity_create_asset", "unity_import_asset" });
            DrawToolCategory("Scripts", new[] { "unity_create_script", "unity_modify_script" });
            DrawToolCategory("Editor", new[] { "unity_editor_command", "unity_get_selection", "unity_set_selection", "unity_scene_view" });
            DrawToolCategory("Settings & Build", new[] { "unity_get_settings", "unity_set_settings", "unity_build", "unity_manage_packages" });
            DrawToolCategory("Utility", new[] { "unity_get_console", "unity_execute_csharp" });

            EditorGUILayout.EndScrollView();

            // No continuous repaint — status updates when user interacts with window
        }

        private void DrawToolCategory(string category, string[] tools)
        {
            EditorGUILayout.LabelField($"  {category}:", EditorStyles.miniLabel);
            foreach (var tool in tools)
            {
                EditorGUILayout.LabelField($"    {tool}", EditorStyles.miniLabel);
            }
        }
    }
}
