using System;
using System.Collections.Generic;
using UnityEngine;

namespace Claude.UnityMCP.Serialization
{
    /// <summary>
    /// Converts between Unity types and JSON-friendly dictionaries.
    /// Handles Vector2/3/4, Quaternion, Color, Rect, Bounds, etc.
    /// </summary>
    public static class TypeConverter
    {
        // ── Unity → JSON ─────────────────────────────────────────────

        public static object ToJson(object value)
        {
            if (value == null) return null;

            switch (value)
            {
                case Vector2 v:
                    return new Dictionary<string, object>
                    {
                        { "_type", "vector2" }, { "x", v.x }, { "y", v.y }
                    };
                case Vector3 v:
                    return new Dictionary<string, object>
                    {
                        { "_type", "vector3" }, { "x", v.x }, { "y", v.y }, { "z", v.z }
                    };
                case Vector4 v:
                    return new Dictionary<string, object>
                    {
                        { "_type", "vector4" }, { "x", v.x }, { "y", v.y }, { "z", v.z }, { "w", v.w }
                    };
                case Vector2Int v:
                    return new Dictionary<string, object>
                    {
                        { "_type", "vector2int" }, { "x", v.x }, { "y", v.y }
                    };
                case Vector3Int v:
                    return new Dictionary<string, object>
                    {
                        { "_type", "vector3int" }, { "x", v.x }, { "y", v.y }, { "z", v.z }
                    };
                case Quaternion q:
                    // Also provide euler for human readability
                    var euler = q.eulerAngles;
                    return new Dictionary<string, object>
                    {
                        { "_type", "quaternion" },
                        { "x", q.x }, { "y", q.y }, { "z", q.z }, { "w", q.w },
                        { "euler_x", euler.x }, { "euler_y", euler.y }, { "euler_z", euler.z }
                    };
                case Color c:
                    return new Dictionary<string, object>
                    {
                        { "_type", "color" }, { "r", c.r }, { "g", c.g }, { "b", c.b }, { "a", c.a }
                    };
                case Color32 c:
                    return new Dictionary<string, object>
                    {
                        { "_type", "color32" }, { "r", (int)c.r }, { "g", (int)c.g }, { "b", (int)c.b }, { "a", (int)c.a }
                    };
                case Rect r:
                    return new Dictionary<string, object>
                    {
                        { "_type", "rect" }, { "x", r.x }, { "y", r.y }, { "width", r.width }, { "height", r.height }
                    };
                case RectInt r:
                    return new Dictionary<string, object>
                    {
                        { "_type", "rectint" }, { "x", r.x }, { "y", r.y }, { "width", r.width }, { "height", r.height }
                    };
                case Bounds b:
                    return new Dictionary<string, object>
                    {
                        { "_type", "bounds" },
                        { "center", ToJson(b.center) },
                        { "size", ToJson(b.size) }
                    };
                case BoundsInt b:
                    return new Dictionary<string, object>
                    {
                        { "_type", "boundsint" },
                        { "position", ToJson(b.position) },
                        { "size", ToJson(b.size) }
                    };
                case Matrix4x4 m:
                    var values = new List<object>(16);
                    for (int i = 0; i < 16; i++) values.Add(m[i]);
                    return new Dictionary<string, object> { { "_type", "matrix4x4" }, { "values", values } };
                case AnimationCurve curve:
                    var keys = new List<object>();
                    foreach (var key in curve.keys)
                    {
                        keys.Add(new Dictionary<string, object>
                        {
                            { "time", key.time }, { "value", key.value },
                            { "inTangent", key.inTangent }, { "outTangent", key.outTangent },
                            { "inWeight", key.inWeight }, { "outWeight", key.outWeight },
                            { "weightedMode", (int)key.weightedMode }
                        });
                    }
                    return new Dictionary<string, object>
                    {
                        { "_type", "animationcurve" },
                        { "preWrapMode", curve.preWrapMode.ToString() },
                        { "postWrapMode", curve.postWrapMode.ToString() },
                        { "keys", keys }
                    };
                case Gradient gradient:
                    var colorKeys = new List<object>();
                    foreach (var ck in gradient.colorKeys)
                    {
                        colorKeys.Add(new Dictionary<string, object>
                        {
                            { "color", ToJson(ck.color) }, { "time", ck.time }
                        });
                    }
                    var alphaKeys = new List<object>();
                    foreach (var ak in gradient.alphaKeys)
                    {
                        alphaKeys.Add(new Dictionary<string, object>
                        {
                            { "alpha", ak.alpha }, { "time", ak.time }
                        });
                    }
                    return new Dictionary<string, object>
                    {
                        { "_type", "gradient" }, { "colorKeys", colorKeys }, { "alphaKeys", alphaKeys },
                        { "mode", gradient.mode.ToString() }
                    };
                case LayerMask lm:
                    return new Dictionary<string, object>
                    {
                        { "_type", "layermask" }, { "value", lm.value }
                    };

                // Unity Object references
                case UnityEngine.Object obj:
                    return ObjectReferenceToJson(obj);

                default:
                    return value;
            }
        }

        public static Dictionary<string, object> ObjectReferenceToJson(UnityEngine.Object obj)
        {
            if (obj == null)
                return new Dictionary<string, object> { { "_type", "null_reference" } };

            var result = new Dictionary<string, object>
            {
                { "_type", "object_reference" },
                { "instance_id", obj.GetInstanceID() },
                { "name", obj.name },
                { "type", obj.GetType().Name }
            };

            if (obj is GameObject go)
            {
                result["tag"] = go.tag;
                result["layer"] = go.layer;
                result["active"] = go.activeSelf;
            }
            else if (obj is Component comp)
            {
                result["gameobject"] = comp.gameObject.name;
                result["gameobject_id"] = comp.gameObject.GetInstanceID();
            }

            return result;
        }

        // ── JSON → Unity ─────────────────────────────────────────────

        public static Vector2 ToVector2(Dictionary<string, object> dict)
        {
            return new Vector2(GetFloat(dict, "x"), GetFloat(dict, "y"));
        }

        public static Vector3 ToVector3(Dictionary<string, object> dict)
        {
            return new Vector3(GetFloat(dict, "x"), GetFloat(dict, "y"), GetFloat(dict, "z"));
        }

        public static Vector4 ToVector4(Dictionary<string, object> dict)
        {
            return new Vector4(GetFloat(dict, "x"), GetFloat(dict, "y"), GetFloat(dict, "z"), GetFloat(dict, "w"));
        }

        public static Quaternion ToQuaternion(Dictionary<string, object> dict)
        {
            // Support euler angles as input (more intuitive for Claude)
            if (dict.ContainsKey("euler_x") || dict.ContainsKey("euler_y") || dict.ContainsKey("euler_z"))
            {
                return Quaternion.Euler(
                    GetFloat(dict, "euler_x"),
                    GetFloat(dict, "euler_y"),
                    GetFloat(dict, "euler_z")
                );
            }
            return new Quaternion(GetFloat(dict, "x"), GetFloat(dict, "y"), GetFloat(dict, "z"), GetFloat(dict, "w"));
        }

        public static Color ToColor(Dictionary<string, object> dict)
        {
            return new Color(GetFloat(dict, "r"), GetFloat(dict, "g"), GetFloat(dict, "b"), GetFloat(dict, "a", 1f));
        }

        public static Rect ToRect(Dictionary<string, object> dict)
        {
            return new Rect(GetFloat(dict, "x"), GetFloat(dict, "y"), GetFloat(dict, "width"), GetFloat(dict, "height"));
        }

        public static Bounds ToBounds(Dictionary<string, object> dict)
        {
            var center = dict.ContainsKey("center") ? ToVector3(dict["center"] as Dictionary<string, object>) : Vector3.zero;
            var size = dict.ContainsKey("size") ? ToVector3(dict["size"] as Dictionary<string, object>) : Vector3.zero;
            return new Bounds(center, size);
        }

        // ── Helpers ──────────────────────────────────────────────────

        public static float GetFloat(Dictionary<string, object> dict, string key, float defaultValue = 0f)
        {
            if (!dict.TryGetValue(key, out var val)) return defaultValue;
            if (val is double d) return (float)d;
            if (val is float f) return f;
            if (val is int i) return i;
            if (val is long l) return l;
            if (float.TryParse(val?.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                return parsed;
            return defaultValue;
        }

        public static int GetInt(Dictionary<string, object> dict, string key, int defaultValue = 0)
        {
            if (!dict.TryGetValue(key, out var val)) return defaultValue;
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is double d) return (int)d;
            if (val is float f) return (int)f;
            if (int.TryParse(val?.ToString(), out int parsed)) return parsed;
            return defaultValue;
        }

        public static bool GetBool(Dictionary<string, object> dict, string key, bool defaultValue = false)
        {
            if (!dict.TryGetValue(key, out var val)) return defaultValue;
            if (val is bool b) return b;
            return defaultValue;
        }

        public static string GetString(Dictionary<string, object> dict, string key, string defaultValue = null)
        {
            if (!dict.TryGetValue(key, out var val)) return defaultValue;
            return val?.ToString() ?? defaultValue;
        }

        public static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var val)) return null;
            return val as List<object>;
        }

        public static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var val)) return null;
            return val as Dictionary<string, object>;
        }
    }
}
