using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Claude.UnityMCP.Communication;
using Claude.UnityMCP.Serialization;
using Claude.UnityMCP.Utils;

namespace Claude.UnityMCP.Tools
{
    /// <summary>
    /// MCP tools for asset operations:
    ///   - unity_search_assets: Find assets by name/type filter
    ///   - unity_get_asset: Read asset metadata and properties
    ///   - unity_create_asset: Create Materials, ScriptableObjects, etc.
    ///   - unity_import_asset: Re-import an asset with force update
    /// </summary>
    public static class AssetTools
    {
        // ── unity_search_assets ──────────────────────────────────────────

        public static Dictionary<string, object> SearchAssets(Dictionary<string, object> args)
        {
            string filter = TypeConverter.GetString(args, "filter", "");
            int maxResults = TypeConverter.GetInt(args, "max_results", 50);
            string assetType = TypeConverter.GetString(args, "asset_type");

            // Build search filter
            string searchFilter = filter;
            if (!string.IsNullOrEmpty(assetType))
            {
                // t: prefix filters by type (e.g., "t:Material", "t:ScriptableObject")
                searchFilter = string.IsNullOrEmpty(filter) ? $"t:{assetType}" : $"{filter} t:{assetType}";
            }

            try
            {
                var results = AssetSerializer.SearchAssets(searchFilter, maxResults);
                return new Dictionary<string, object>
                {
                    { "results", results },
                    { "count", results.Count },
                    { "filter", searchFilter }
                };
            }
            catch (Exception ex)
            {
                return Error($"Search failed: {ex.Message}");
            }
        }

        // ── unity_get_asset ──────────────────────────────────────────────

        public static Dictionary<string, object> GetAsset(Dictionary<string, object> args)
        {
            string path = TypeConverter.GetString(args, "path");
            if (string.IsNullOrEmpty(path))
                return Error("No path provided");

            int depth = TypeConverter.GetInt(args, "depth", 2);

            try
            {
                var result = AssetSerializer.Serialize(path, depth);

                if (result.ContainsKey("error"))
                    return result;

                return result;
            }
            catch (Exception ex)
            {
                return Error($"Failed to load asset: {ex.Message}");
            }
        }

        // ── unity_create_asset ───────────────────────────────────────────

        public static Dictionary<string, object> CreateAsset(Dictionary<string, object> args)
        {
            string assetType = TypeConverter.GetString(args, "asset_type");
            string path = TypeConverter.GetString(args, "path");
            string name = TypeConverter.GetString(args, "name");

            if (string.IsNullOrEmpty(assetType))
                return Error("No asset_type provided (e.g., 'Material', 'ScriptableObject')");
            if (string.IsNullOrEmpty(path))
                return Error("No path provided");

            // Ensure path is in Assets folder and has proper extension
            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path;

            // Add extension if missing
            string extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
            {
                extension = GetExtensionForType(assetType);
                path = path + extension;
            }

            // Ensure directory exists
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    return Error($"Failed to create directory: {ex.Message}");
                }
            }

            UndoHelper.BeginGroup($"Create {assetType}");

            try
            {
                UnityEngine.Object asset = null;

                // Create the appropriate asset type
                switch (assetType.ToLower())
                {
                    case "material":
                        asset = new Material(Shader.Find("Standard"));
                        if (!string.IsNullOrEmpty(name))
                            asset.name = name;
                        AssetDatabase.CreateAsset(asset, path);
                        break;

                    case "scriptableobject":
                        // Default to generic ScriptableObject (user can modify after)
                        asset = ScriptableObject.CreateInstance("ScriptableObject");
                        if (asset == null)
                            return Error("Failed to create ScriptableObject instance");
                        if (!string.IsNullOrEmpty(name))
                            asset.name = name;
                        AssetDatabase.CreateAsset(asset, path);
                        break;

                    case "folder":
                        if (!AssetDatabase.IsValidFolder(path))
                            AssetDatabase.CreateFolder(Path.GetDirectoryName(path), Path.GetFileName(path));
                        return new Dictionary<string, object>
                        {
                            { "created", true },
                            { "type", "Folder" },
                            { "path", path }
                        };

                    case "texture2d":
                        // Create a simple white texture
                        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                        tex.SetPixel(0, 0, Color.white);
                        tex.Apply();
                        if (!string.IsNullOrEmpty(name))
                            tex.name = name;
                        byte[] pngData = tex.EncodeToPNG();
                        UnityEngine.Object.DestroyImmediate(tex);
                        File.WriteAllBytes(path, pngData);
                        asset = null; // Will be loaded after import
                        break;

                    case "prefab":
                        // Create an empty GameObject and save as prefab
                        var go = new GameObject(string.IsNullOrEmpty(name) ? "NewPrefab" : name);
                        asset = PrefabUtility.SaveAsPrefabAsset(go, path);
                        UnityEngine.Object.DestroyImmediate(go);
                        break;

                    default:
                        return Error($"Unsupported asset type: {assetType}. Supported: Material, ScriptableObject, Folder, Texture2D, Prefab");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (asset != null)
                {
                    return new Dictionary<string, object>
                    {
                        { "created", true },
                        { "type", assetType },
                        { "path", path },
                        { "name", asset.name },
                        { "instance_id", asset.GetInstanceID() },
                        { "guid", AssetDatabase.AssetPathToGUID(path) }
                    };
                }
                else
                {
                    // For assets that need reimport
                    asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    return new Dictionary<string, object>
                    {
                        { "created", true },
                        { "type", assetType },
                        { "path", path },
                        { "guid", AssetDatabase.AssetPathToGUID(path) }
                    };
                }
            }
            catch (Exception ex)
            {
                UndoHelper.EndGroup();
                return Error($"Failed to create asset: {ex.Message}");
            }
            finally
            {
                UndoHelper.EndGroup();
            }
        }

        // ── unity_import_asset ───────────────────────────────────────────

        public static Dictionary<string, object> ImportAsset(Dictionary<string, object> args)
        {
            string path = TypeConverter.GetString(args, "path");
            if (string.IsNullOrEmpty(path))
                return Error("No path provided");

            bool forceUpdate = TypeConverter.GetBool(args, "force_update", true);

            try
            {
                if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path))
                    return Error($"Asset not found: {path}");

                // Import with force update
                ImportAssetOptions options = ImportAssetOptions.Default;
                if (forceUpdate)
                    options = ImportAssetOptions.ForceUpdate;

                AssetDatabase.ImportAsset(path, options);
                AssetDatabase.Refresh();

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                return new Dictionary<string, object>
                {
                    { "imported", true },
                    { "path", path },
                    { "type", asset != null ? asset.GetType().Name : "Unknown" },
                    { "guid", AssetDatabase.AssetPathToGUID(path) },
                    { "force_update", forceUpdate }
                };
            }
            catch (Exception ex)
            {
                return Error($"Import failed: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string GetExtensionForType(string assetType)
        {
            switch (assetType.ToLower())
            {
                case "material": return ".mat";
                case "scriptableobject": return ".asset";
                case "texture2d": return ".png";
                case "prefab": return ".prefab";
                case "animationclip": return ".anim";
                case "audioclip": return ".wav";
                default: return ".asset";
            }
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
                    { "name", "unity_search_assets" },
                    { "description", "Search for assets by name filter and/or type. Returns matching assets with paths and GUIDs." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "filter", new Dictionary<string, object> { { "type", "string" }, { "description", "Search filter (e.g., 'MyMaterial' or 'player')" } } },
                                    { "asset_type", new Dictionary<string, object> { { "type", "string" }, { "description", "Asset type to filter by (e.g., 'Material', 'ScriptableObject', 'Texture2D')" } } },
                                    { "max_results", new Dictionary<string, object> { { "type", "integer" }, { "description", "Maximum results to return. Default: 50" } } }
                                }
                            }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_get_asset" },
                    { "description", "Read detailed metadata and properties of a single asset. Returns type-specific information." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Asset path (e.g., 'Assets/Materials/MyMaterial.mat')" } } },
                                    { "depth", new Dictionary<string, object> { { "type", "integer" }, { "description", "Property detail depth (0=basic, 1=+importer, 2=+properties). Default: 2" } } }
                                }
                            },
                            { "required", new List<object> { "path" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_create_asset" },
                    { "description", "Create a new asset (Material, ScriptableObject, Texture2D, Prefab, etc.) at the specified path." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "asset_type", new Dictionary<string, object> { { "type", "string" }, { "description", "Type of asset: Material, ScriptableObject, Texture2D, Prefab, Folder, AnimationClip, AudioClip" } } },
                                    { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Full or relative path (e.g., 'Assets/New/MyMaterial'). Extension added automatically." } } },
                                    { "name", new Dictionary<string, object> { { "type", "string" }, { "description", "Display name for the asset" } } }
                                }
                            },
                            { "required", new List<object> { "asset_type", "path" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_import_asset" },
                    { "description", "Re-import an asset, optionally with force update to pick up external changes." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Asset path to re-import" } } },
                                    { "force_update", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Force re-import even if unchanged. Default: true" } } }
                                }
                            },
                            { "required", new List<object> { "path" } }
                        }
                    }
                }
            };
        }
    }
}
