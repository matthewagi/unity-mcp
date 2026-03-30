using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Claude.UnityMCP.Serialization
{
    /// <summary>
    /// Serializes Unity assets (Materials, ScriptableObjects, Textures, etc.) to JSON.
    /// </summary>
    public static class AssetSerializer
    {
        public static Dictionary<string, object> Serialize(string assetPath, int depth = 2, bool includeProperties = true)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return new Dictionary<string, object> { { "error", $"Asset not found: {assetPath}" } };

            return Serialize(asset, assetPath, depth, includeProperties);
        }

        public static Dictionary<string, object> Serialize(UnityEngine.Object asset, string assetPath = null, int depth = 2, bool includeProperties = true)
        {
            if (asset == null)
                return new Dictionary<string, object> { { "error", "null asset" } };

            if (string.IsNullOrEmpty(assetPath))
                assetPath = AssetDatabase.GetAssetPath(asset);

            var result = new Dictionary<string, object>
            {
                { "name", asset.name },
                { "type", asset.GetType().Name },
                { "full_type", asset.GetType().FullName },
                { "instance_id", asset.GetInstanceID() },
                { "asset_path", assetPath },
                { "guid", AssetDatabase.AssetPathToGUID(assetPath) }
            };

            // File info
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer != null)
            {
                result["importer_type"] = importer.GetType().Name;
                result["asset_bundle"] = importer.assetBundleName;
            }

            // Labels
            var labels = AssetDatabase.GetLabels(asset);
            if (labels.Length > 0)
                result["labels"] = new List<object>(labels);

            // Sub-assets
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (subAssets.Length > 1)
            {
                var subs = new List<object>();
                foreach (var sub in subAssets)
                {
                    if (sub == asset || sub == null) continue;
                    subs.Add(new Dictionary<string, object>
                    {
                        { "name", sub.name },
                        { "type", sub.GetType().Name },
                        { "instance_id", sub.GetInstanceID() }
                    });
                }
                if (subs.Count > 0)
                    result["sub_assets"] = subs;
            }

            // Type-specific metadata
            AddTypeSpecificInfo(asset, result, depth);

            // Serialized properties (for depth >= 2)
            if (includeProperties && depth >= 2)
            {
                try
                {
                    var so = new SerializedObject(asset);
                    result["properties"] = SerializedPropertyHelper.ReadAllProperties(so, depth - 1);
                }
                catch (Exception ex)
                {
                    result["properties_error"] = ex.Message;
                }
            }

            return result;
        }

        // ── Type-Specific Info ───────────────────────────────────────

        private static void AddTypeSpecificInfo(UnityEngine.Object asset, Dictionary<string, object> result, int depth)
        {
            switch (asset)
            {
                case Material mat:
                    result["shader"] = mat.shader != null ? mat.shader.name : "null";
                    result["render_queue"] = mat.renderQueue;
                    result["pass_count"] = mat.passCount;
                    if (depth >= 2)
                    {
                        // List shader properties
                        var shaderProps = new List<object>();
                        if (mat.shader != null)
                        {
                            int count = mat.shader.GetPropertyCount();
                            for (int i = 0; i < count; i++)
                            {
                                var propName = mat.shader.GetPropertyName(i);
                                var propType = mat.shader.GetPropertyType(i);
                                var propInfo = new Dictionary<string, object>
                                {
                                    { "name", propName },
                                    { "type", propType.ToString() },
                                    { "description", mat.shader.GetPropertyDescription(i) }
                                };
                                // Read current value
                                switch (propType)
                                {
                                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                                        propInfo["value"] = TypeConverter.ToJson(mat.GetColor(propName));
                                        break;
                                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                                        propInfo["value"] = mat.GetFloat(propName);
                                        break;
                                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                        var tex = mat.GetTexture(propName);
                                        propInfo["value"] = tex != null ? tex.name : null;
                                        break;
                                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                        propInfo["value"] = TypeConverter.ToJson(mat.GetVector(propName));
                                        break;
                                    case UnityEngine.Rendering.ShaderPropertyType.Int:
                                        propInfo["value"] = mat.GetInt(propName);
                                        break;
                                }
                                shaderProps.Add(propInfo);
                            }
                        }
                        result["shader_properties"] = shaderProps;
                        result["keywords"] = new List<object>(mat.shaderKeywords);
                    }
                    break;

                case Texture tex:
                    result["width"] = tex.width;
                    result["height"] = tex.height;
                    result["filter_mode"] = tex.filterMode.ToString();
                    result["wrap_mode"] = tex.wrapMode.ToString();
                    if (tex is Texture2D tex2d)
                    {
                        result["format"] = tex2d.format.ToString();
                        result["mipmap_count"] = tex2d.mipmapCount;
                    }
                    break;

                case AudioClip clip:
                    result["length_seconds"] = clip.length;
                    result["channels"] = clip.channels;
                    result["frequency"] = clip.frequency;
                    result["samples"] = clip.samples;
                    break;

                case Mesh mesh:
                    result["vertex_count"] = mesh.vertexCount;
                    result["triangle_count"] = mesh.triangles.Length / 3;
                    result["sub_mesh_count"] = mesh.subMeshCount;
                    result["bounds"] = TypeConverter.ToJson(mesh.bounds);
                    result["is_readable"] = mesh.isReadable;
                    break;

                case AnimationClip animClip:
                    result["length_seconds"] = animClip.length;
                    result["frame_rate"] = animClip.frameRate;
                    result["is_looping"] = animClip.isLooping;
                    result["is_human_motion"] = animClip.isHumanMotion;
                    result["wrap_mode"] = animClip.wrapMode.ToString();
                    break;

                case ScriptableObject so:
                    result["is_scriptable_object"] = true;
                    break;

                case GameObject prefab:
                    result["is_prefab"] = true;
                    if (depth >= 2)
                        result["hierarchy"] = GameObjectSerializer.Serialize(prefab, depth - 1, true);
                    break;
            }
        }

        // ── Search Helper ────────────────────────────────────────────

        public static List<Dictionary<string, object>> SearchAssets(string filter, int maxResults = 50)
        {
            var guids = AssetDatabase.FindAssets(filter);
            var results = new List<Dictionary<string, object>>();

            int count = Math.Min(guids.Length, maxResults);
            for (int i = 0; i < count; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

                results.Add(new Dictionary<string, object>
                {
                    { "guid", guids[i] },
                    { "path", path },
                    { "name", asset != null ? asset.name : System.IO.Path.GetFileNameWithoutExtension(path) },
                    { "type", asset != null ? asset.GetType().Name : "Unknown" }
                });
            }

            return results;
        }
    }
}
