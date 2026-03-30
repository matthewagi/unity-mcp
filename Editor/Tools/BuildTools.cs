using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Claude.UnityMCP.Communication;
using Claude.UnityMCP.Serialization;

#if UNITY_2021_2_OR_NEWER
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
#endif

namespace Claude.UnityMCP.Tools
{
    /// <summary>
    /// Build-related tools for Unity MCP plugin.
    /// Handles building, package management, and console access.
    /// </summary>
    public static class BuildTools
    {
        private static List<ConsoleEntry> ConsoleEntries = new List<ConsoleEntry>();
        private static bool ConsoleListenerRegistered = false;

        private class ConsoleEntry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
            public double Timestamp;
        }

        /// <summary>
        /// Builds the Unity project for the specified target platform.
        /// </summary>
        public static Dictionary<string, object> UnityBuild(Dictionary<string, object> input)
        {
            try
            {
                if (!input.ContainsKey("target"))
                    return new Dictionary<string, object> { { "error", "Missing 'target' parameter" } };

                string targetStr = input["target"].ToString().ToLower();
                BuildTarget buildTarget = ParseBuildTarget(targetStr);

                if (buildTarget == BuildTarget.NoTarget)
                    return new Dictionary<string, object> { { "error", $"Unknown build target: {targetStr}" } };

                if (!input.ContainsKey("output_path"))
                    return new Dictionary<string, object> { { "error", "Missing 'output_path' parameter" } };

                string outputPath = input["output_path"].ToString();

                // Get scenes
                string[] scenes = GetScenesFromInput(input);
                if (scenes == null || scenes.Length == 0)
                {
                    // Use EditorBuildSettings.scenes if not specified
                    scenes = EditorBuildSettings.scenes.Select(s => s.path).ToArray();
                }

                // Get development flag
                bool development = false;
                if (input.ContainsKey("development") && bool.TryParse(input["development"].ToString(), out bool devFlag))
                    development = devFlag;

                // Build options
                BuildOptions options = BuildOptions.None;
                if (development)
                    options |= BuildOptions.Development;

                // Add any additional options from input
                if (input.ContainsKey("options") && input["options"] is List<object> optionsList)
                {
                    foreach (var opt in optionsList)
                    {
                        if (Enum.TryParse<BuildOptions>(opt.ToString(), true, out var buildOpt))
                            options |= buildOpt;
                    }
                }

                // Perform build
                BuildReport report = BuildPipeline.BuildPlayer(scenes, outputPath, buildTarget, options);

                // Build result
                var result = new Dictionary<string, object>
                {
                    { "success", report.summary.result == BuildResult.Succeeded },
                    { "result", report.summary.result.ToString() },
                    { "total_time", report.summary.totalTime },
                    { "total_size", report.summary.totalSize },
                    { "output_path", outputPath },
                    { "target", targetStr }
                };

                if (report.summary.result != BuildResult.Succeeded)
                {
                    var errors = new List<string>();
                    foreach (var step in report.steps)
                    {
                        foreach (var msg in step.messages)
                        {
                            if (msg.type == LogType.Error)
                                errors.Add(msg.content);
                        }
                    }
                    result["errors"] = errors;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// Manages packages (add, remove, list).
        /// </summary>
        public static Dictionary<string, object> UnityManagePackages(Dictionary<string, object> input)
        {
            try
            {
#if !UNITY_2021_2_OR_NEWER
                return new Dictionary<string, object> { { "error", "PackageManager API requires Unity 2021.2 or newer" } };
#else
                if (!input.ContainsKey("operation"))
                    return new Dictionary<string, object> { { "error", "Missing 'operation' parameter (add/remove/list)" } };

                string operation = input["operation"].ToString().ToLower();

                if (operation == "list")
                {
                    return ListPackages();
                }
                else if (operation == "add")
                {
                    if (!input.ContainsKey("package_name"))
                        return new Dictionary<string, object> { { "error", "Missing 'package_name' parameter" } };

                    string packageName = input["package_name"].ToString();
                    string version = input.ContainsKey("version") ? input["version"].ToString() : null;

                    return AddPackage(packageName, version);
                }
                else if (operation == "remove")
                {
                    if (!input.ContainsKey("package_name"))
                        return new Dictionary<string, object> { { "error", "Missing 'package_name' parameter" } };

                    string packageName = input["package_name"].ToString();
                    return RemovePackage(packageName);
                }
                else
                {
                    return new Dictionary<string, object> { { "error", $"Unknown operation: {operation}" } };
                }
#endif
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// Reads console entries with optional filtering.
        /// </summary>
        public static Dictionary<string, object> UnityGetConsole(Dictionary<string, object> input)
        {
            try
            {
                // Register listener if not already done
                if (!ConsoleListenerRegistered)
                {
                    Application.logMessageReceived += OnLogMessageReceived;
                    ConsoleListenerRegistered = true;
                }

                // Parse filter type
                string filterTypeStr = "all";
                if (input.ContainsKey("filter_type"))
                    filterTypeStr = input["filter_type"].ToString().ToLower();

                LogType? filterType = null;
                if (filterTypeStr == "error")
                    filterType = LogType.Error;
                else if (filterTypeStr == "warning")
                    filterType = LogType.Warning;
                else if (filterTypeStr == "log")
                    filterType = LogType.Log;

                // Parse max entries
                int maxEntries = 50;
                if (input.ContainsKey("max_entries") && int.TryParse(input["max_entries"].ToString(), out int max))
                    maxEntries = max;

                // Parse since timestamp
                double? sinceTimestamp = null;
                if (input.ContainsKey("since_timestamp") && double.TryParse(input["since_timestamp"].ToString(), out double ts))
                    sinceTimestamp = ts;

                // Get entries
                var filteredEntries = ConsoleEntries.AsEnumerable();

                if (filterType.HasValue)
                    filteredEntries = filteredEntries.Where(e => e.Type == filterType.Value);

                if (sinceTimestamp.HasValue)
                    filteredEntries = filteredEntries.Where(e => e.Timestamp >= sinceTimestamp.Value);

                var entries = filteredEntries
                    .OrderByDescending(e => e.Timestamp)
                    .Take(maxEntries)
                    .Select(e => new Dictionary<string, object>
                    {
                        { "message", e.Message },
                        { "type", e.Type.ToString() },
                        { "timestamp", e.Timestamp },
                        { "stack_trace", e.StackTrace ?? "" }
                    })
                    .ToList();

                var result = new Dictionary<string, object>
                {
                    { "entries", entries },
                    { "count", entries.Count },
                    { "filter_type", filterTypeStr }
                };

                // Clear if requested
                if (input.ContainsKey("clear") && bool.TryParse(input["clear"].ToString(), out bool shouldClear) && shouldClear)
                {
                    ConsoleEntries.Clear();
                    result["cleared"] = true;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// Gets the MCP tool definitions for all build tools.
        /// </summary>
        public static List<Dictionary<string, object>> GetToolDefinitions()
        {
            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "name", "unity_build" },
                    { "description", "Build the Unity project for a specified target platform" },
                    { "inputSchema", new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                        {
                            { "target", new Dictionary<string, object>
                            {
                                { "type", "string" },
                                { "description", "Build target platform (windows, mac, linux, webgl, android, ios)" }
                            }},
                            { "output_path", new Dictionary<string, object>
                            {
                                { "type", "string" },
                                { "description", "Output path for the build" }
                            }},
                            { "scenes", new Dictionary<string, object>
                            {
                                { "type", "array" },
                                { "items", new Dictionary<string, object> { { "type", "string" } } },
                                { "description", "List of scene paths to include in build (optional, uses EditorBuildSettings if not provided)" }
                            }},
                            { "development", new Dictionary<string, object>
                            {
                                { "type", "boolean" },
                                { "description", "Whether to create a development build" }
                            }},
                            { "options", new Dictionary<string, object>
                            {
                                { "type", "array" },
                                { "items", new Dictionary<string, object> { { "type", "string" } } },
                                { "description", "Additional BuildOptions (e.g., AutoRunPlayer, AllowDebugging)" }
                            }}
                        }},
                        { "required", new List<string> { "target", "output_path" } }
                    }}
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_manage_packages" },
                    { "description", "Manage Unity packages (add, remove, list)" },
                    { "inputSchema", new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                        {
                            { "operation", new Dictionary<string, object>
                            {
                                { "type", "string" },
                                { "description", "Package operation: add, remove, or list" }
                            }},
                            { "package_name", new Dictionary<string, object>
                            {
                                { "type", "string" },
                                { "description", "Package name or identifier (required for add/remove)" }
                            }},
                            { "version", new Dictionary<string, object>
                            {
                                { "type", "string" },
                                { "description", "Package version (optional for add)" }
                            }}
                        }},
                        { "required", new List<string> { "operation" } }
                    }}
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_get_console" },
                    { "description", "Read Unity console entries with optional filtering" },
                    { "inputSchema", new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                        {
                            { "filter_type", new Dictionary<string, object>
                            {
                                { "type", "string" },
                                { "description", "Filter by log type: all, error, warning, log" }
                            }},
                            { "max_entries", new Dictionary<string, object>
                            {
                                { "type", "integer" },
                                { "description", "Maximum number of entries to return (default 50)" }
                            }},
                            { "since_timestamp", new Dictionary<string, object>
                            {
                                { "type", "number" },
                                { "description", "Only return entries after this timestamp" }
                            }},
                            { "clear", new Dictionary<string, object>
                            {
                                { "type", "boolean" },
                                { "description", "Clear console entries after reading" }
                            }}
                        }},
                        { "required", new List<string>() }
                    }}
                }
            };
        }

        // ===== Helper Methods =====

        private static BuildTarget ParseBuildTarget(string target)
        {
            return target switch
            {
                "windows" => BuildTarget.StandaloneWindows64,
                "mac" => BuildTarget.StandaloneOSX,
                "linux" => BuildTarget.StandaloneLinux64,
                "webgl" => BuildTarget.WebGL,
                "android" => BuildTarget.Android,
                "ios" => BuildTarget.iOS,
                _ => BuildTarget.NoTarget
            };
        }

        private static string[] GetScenesFromInput(Dictionary<string, object> input)
        {
            if (!input.ContainsKey("scenes"))
                return null;

            if (input["scenes"] is List<object> sceneList)
                return sceneList.Select(s => s.ToString()).ToArray();

            return null;
        }

        private static void OnLogMessageReceived(string message, string stackTrace, LogType type)
        {
            ConsoleEntries.Add(new ConsoleEntry
            {
                Message = message,
                StackTrace = stackTrace,
                Type = type,
                Timestamp = EditorApplication.timeSinceStartup
            });

            // Keep memory bounded
            if (ConsoleEntries.Count > 1000)
                ConsoleEntries.RemoveRange(0, ConsoleEntries.Count - 1000);
        }

#if UNITY_2021_2_OR_NEWER
        private static Dictionary<string, object> ListPackages()
        {
            var listRequest = Client.List(true);

            // Wait for async operation to complete (with timeout)
            var startTime = DateTime.Now;
            while (!listRequest.IsCompleted)
            {
                if ((DateTime.Now - startTime).TotalSeconds > 30)
                    return new Dictionary<string, object> { { "error", "Package listing timeout" } };

                System.Threading.Thread.Sleep(100);
            }

            if (listRequest.Status == StatusCode.Failure)
                return new Dictionary<string, object> { { "error", listRequest.Error.message } };

            var packages = new List<Dictionary<string, object>>();
            foreach (var package in listRequest.Result)
            {
                packages.Add(new Dictionary<string, object>
                {
                    { "name", package.name },
                    { "version", package.version },
                    { "displayName", package.displayName ?? "" },
                    { "category", package.category ?? "" }
                });
            }

            return new Dictionary<string, object>
            {
                { "packages", packages },
                { "count", packages.Count }
            };
        }

        private static Dictionary<string, object> AddPackage(string packageName, string version)
        {
            string identifier = string.IsNullOrEmpty(version) ? packageName : $"{packageName}@{version}";
            var addRequest = Client.Add(identifier);

            // Wait for async operation (with timeout)
            var startTime = DateTime.Now;
            while (!addRequest.IsCompleted)
            {
                if ((DateTime.Now - startTime).TotalSeconds > 30)
                    return new Dictionary<string, object> { { "error", "Package add timeout" } };

                System.Threading.Thread.Sleep(100);
            }

            if (addRequest.Status == StatusCode.Failure)
                return new Dictionary<string, object> { { "error", addRequest.Error.message } };

            return new Dictionary<string, object>
            {
                { "success", true },
                { "package_name", addRequest.Result.name },
                { "version", addRequest.Result.version }
            };
        }

        private static Dictionary<string, object> RemovePackage(string packageName)
        {
            var removeRequest = Client.Remove(packageName);

            // Wait for async operation (with timeout)
            var startTime = DateTime.Now;
            while (!removeRequest.IsCompleted)
            {
                if ((DateTime.Now - startTime).TotalSeconds > 30)
                    return new Dictionary<string, object> { { "error", "Package remove timeout" } };

                System.Threading.Thread.Sleep(100);
            }

            if (removeRequest.Status == StatusCode.Failure)
                return new Dictionary<string, object> { { "error", removeRequest.Error.message } };

            return new Dictionary<string, object>
            {
                { "success", true },
                { "package_name", packageName }
            };
        }
#endif
    }
}
