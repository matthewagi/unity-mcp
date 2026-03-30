#pragma warning disable CS0618 // InstanceIDToObject(int) obsolete in Unity 6
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Claude.UnityMCP.Serialization
{
    /// <summary>
    /// Universal property reader/writer using Unity's SerializedProperty system.
    /// This is the "secret weapon" — it can read/write ANY property on ANY component
    /// without needing type-specific code. Handles all SerializedPropertyType variants.
    /// </summary>
    public static class SerializedPropertyHelper
    {
        // ── Read a SerializedProperty to JSON-friendly value ─────────

        public static object ReadProperty(SerializedProperty prop, int depth = 2)
        {
            if (prop == null) return null;

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return prop.floatValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Enum:
                    return new Dictionary<string, object>
                    {
                        { "_type", "enum" },
                        { "value", prop.enumValueIndex },
                        { "name", prop.enumNames != null && prop.enumValueIndex >= 0 &&
                                  prop.enumValueIndex < prop.enumNames.Length
                            ? prop.enumNames[prop.enumValueIndex] : "Unknown" },
                        { "options", new List<object>(prop.enumNames ?? Array.Empty<string>()) }
                    };
                case SerializedPropertyType.Color:
                    return TypeConverter.ToJson(prop.colorValue);
                case SerializedPropertyType.Vector2:
                    return TypeConverter.ToJson(prop.vector2Value);
                case SerializedPropertyType.Vector3:
                    return TypeConverter.ToJson(prop.vector3Value);
                case SerializedPropertyType.Vector4:
                    return TypeConverter.ToJson(prop.vector4Value);
                case SerializedPropertyType.Vector2Int:
                    return TypeConverter.ToJson(prop.vector2IntValue);
                case SerializedPropertyType.Vector3Int:
                    return TypeConverter.ToJson(prop.vector3IntValue);
                case SerializedPropertyType.Quaternion:
                    return TypeConverter.ToJson(prop.quaternionValue);
                case SerializedPropertyType.Rect:
                    return TypeConverter.ToJson(prop.rectValue);
                case SerializedPropertyType.RectInt:
                    return TypeConverter.ToJson(prop.rectIntValue);
                case SerializedPropertyType.Bounds:
                    return TypeConverter.ToJson(prop.boundsValue);
                case SerializedPropertyType.BoundsInt:
                    return TypeConverter.ToJson(prop.boundsIntValue);
                case SerializedPropertyType.AnimationCurve:
                    return TypeConverter.ToJson(prop.animationCurveValue);
                case SerializedPropertyType.Gradient:
                    // Gradient requires reflection to access from SerializedProperty
                    return new Dictionary<string, object> { { "_type", "gradient" }, { "note", "use unity_execute_csharp for gradient editing" } };
                case SerializedPropertyType.LayerMask:
                    return new Dictionary<string, object>
                    {
                        { "_type", "layermask" }, { "value", prop.intValue }
                    };
                case SerializedPropertyType.ObjectReference:
                    return ReadObjectReference(prop, depth);
                case SerializedPropertyType.ExposedReference:
                    return ReadObjectReference(prop, depth);
                case SerializedPropertyType.ArraySize:
                    return prop.intValue;
                case SerializedPropertyType.Character:
                    return prop.intValue;
                case SerializedPropertyType.FixedBufferSize:
                    return prop.intValue;
                case SerializedPropertyType.Hash128:
                    return prop.hash128Value.ToString();

                // Generic / nested object — recurse if depth allows
                case SerializedPropertyType.Generic:
                    return ReadGenericProperty(prop, depth);

                // ManagedReference (Unity 2019.3+)
                case SerializedPropertyType.ManagedReference:
                    return ReadGenericProperty(prop, depth);

                default:
                    return $"<unsupported:{prop.propertyType}>";
            }
        }

        // ── Read all visible properties of a SerializedObject ────────

        public static Dictionary<string, object> ReadAllProperties(SerializedObject so, int depth = 2)
        {
            var result = new Dictionary<string, object>();
            var iterator = so.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip the "m_Script" field (internal Unity reference)
                if (iterator.name == "m_Script") continue;

                try
                {
                    result[iterator.name] = ReadProperty(iterator.Copy(), depth);
                }
                catch (Exception ex)
                {
                    result[iterator.name] = $"<error:{ex.Message}>";
                }
            }

            return result;
        }

        // ── Read a specific property by path ─────────────────────────

        public static object ReadPropertyByPath(SerializedObject so, string propertyPath, int depth = 2)
        {
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
                return null;
            return ReadProperty(prop, depth);
        }

        // ── Write a value to a SerializedProperty ────────────────────

        public static bool WriteProperty(SerializedProperty prop, object value)
        {
            if (prop == null) return false;

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = Convert.ToInt32(value);
                        return true;
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = Convert.ToBoolean(value);
                        return true;
                    case SerializedPropertyType.Float:
                        prop.floatValue = Convert.ToSingle(value);
                        return true;
                    case SerializedPropertyType.String:
                        prop.stringValue = value?.ToString() ?? "";
                        return true;
                    case SerializedPropertyType.Enum:
                        if (value is string enumName)
                        {
                            int idx = Array.IndexOf(prop.enumNames, enumName);
                            if (idx >= 0) { prop.enumValueIndex = idx; return true; }
                        }
                        prop.enumValueIndex = Convert.ToInt32(value);
                        return true;
                    case SerializedPropertyType.Color:
                        prop.colorValue = DictToColor(value);
                        return true;
                    case SerializedPropertyType.Vector2:
                        prop.vector2Value = DictToVector2(value);
                        return true;
                    case SerializedPropertyType.Vector3:
                        prop.vector3Value = DictToVector3(value);
                        return true;
                    case SerializedPropertyType.Vector4:
                        prop.vector4Value = DictToVector4(value);
                        return true;
                    case SerializedPropertyType.Vector2Int:
                        var v2 = DictToVector2(value);
                        prop.vector2IntValue = new Vector2Int((int)v2.x, (int)v2.y);
                        return true;
                    case SerializedPropertyType.Vector3Int:
                        var v3 = DictToVector3(value);
                        prop.vector3IntValue = new Vector3Int((int)v3.x, (int)v3.y, (int)v3.z);
                        return true;
                    case SerializedPropertyType.Quaternion:
                        prop.quaternionValue = DictToQuaternion(value);
                        return true;
                    case SerializedPropertyType.Rect:
                        prop.rectValue = DictToRect(value);
                        return true;
                    case SerializedPropertyType.Bounds:
                        prop.boundsValue = DictToBounds(value);
                        return true;
                    case SerializedPropertyType.LayerMask:
                        prop.intValue = Convert.ToInt32(value);
                        return true;
                    case SerializedPropertyType.ObjectReference:
                        return WriteObjectReference(prop, value);
                    case SerializedPropertyType.ArraySize:
                        prop.intValue = Convert.ToInt32(value);
                        return true;

                    // For arrays/generic, handle specially
                    case SerializedPropertyType.Generic:
                        if (prop.isArray && value is List<object> list)
                        {
                            return WriteArray(prop, list);
                        }
                        return false;

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP] Failed to write property {prop.propertyPath}: {ex.Message}");
                return false;
            }
        }

        // ── Write by path with undo support ──────────────────────────

        public static bool WritePropertyByPath(SerializedObject so, string propertyPath, object value, string undoLabel = null)
        {
            so.Update();
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
            {
                Debug.LogWarning($"[MCP] Property not found: {propertyPath}");
                return false;
            }

            if (!string.IsNullOrEmpty(undoLabel))
                Undo.RecordObject(so.targetObject, undoLabel);

            bool success = WriteProperty(prop, value);
            if (success)
                so.ApplyModifiedProperties();

            return success;
        }

        // ── Private Helpers ──────────────────────────────────────────

        private static object ReadObjectReference(SerializedProperty prop, int depth)
        {
            var obj = prop.objectReferenceValue;
            if (obj == null)
                return new Dictionary<string, object> { { "_type", "null_reference" } };

            var result = new Dictionary<string, object>
            {
                { "_type", "object_reference" },
                { "instance_id", obj.GetInstanceID() },
                { "name", obj.name },
                { "type", obj.GetType().Name }
            };

            // If depth > 0 and it's an asset, include the path
            if (depth > 0)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                    result["asset_path"] = path;
            }

            return result;
        }

        private static bool WriteObjectReference(SerializedProperty prop, object value)
        {
            if (value == null)
            {
                prop.objectReferenceValue = null;
                return true;
            }

            if (value is Dictionary<string, object> dict)
            {
                // By instance ID
                if (dict.TryGetValue("instance_id", out var idVal))
                {
                    int id = Convert.ToInt32(idVal);
                    var obj = EditorUtility.InstanceIDToObject(id);
                    if (obj != null) { prop.objectReferenceValue = obj; return true; }
                }

                // By asset path
                if (dict.TryGetValue("asset_path", out var pathVal))
                {
                    string path = pathVal.ToString();
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (obj != null) { prop.objectReferenceValue = obj; return true; }
                }
            }

            // If value is an instance ID directly
            if (value is int instanceId || (value is long l && (instanceId = (int)l) == l) ||
                (value is double d && (instanceId = (int)d) == d))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj != null) { prop.objectReferenceValue = obj; return true; }
            }

            return false;
        }

        private static Dictionary<string, object> ReadGenericProperty(SerializedProperty prop, int depth)
        {
            if (depth <= 0)
                return new Dictionary<string, object> { { "_type", "truncated" }, { "path", prop.propertyPath } };

            // Handle arrays
            if (prop.isArray)
            {
                var arr = new List<object>();
                int count = Math.Min(prop.arraySize, 100); // Cap at 100 to avoid huge payloads
                for (int i = 0; i < count; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    arr.Add(ReadProperty(elem, depth - 1));
                }
                var result = new Dictionary<string, object>
                {
                    { "_type", "array" },
                    { "length", prop.arraySize },
                    { "elements", arr }
                };
                if (prop.arraySize > 100)
                    result["truncated"] = true;
                return result;
            }

            // Handle nested objects
            var dict = new Dictionary<string, object>();
            var child = prop.Copy();
            var endProp = prop.Copy();
            endProp.Next(false); // Move to next sibling to define iteration boundary

            if (child.Next(true)) // Enter children
            {
                do
                {
                    if (SerializedProperty.EqualContents(child, endProp)) break;
                    try
                    {
                        dict[child.name] = ReadProperty(child.Copy(), depth - 1);
                    }
                    catch { }
                }
                while (child.Next(false));
            }

            return dict;
        }

        private static bool WriteArray(SerializedProperty prop, List<object> values)
        {
            prop.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                var elem = prop.GetArrayElementAtIndex(i);
                if (!WriteProperty(elem, values[i]))
                    return false;
            }
            return true;
        }

        // ── Value conversion helpers ─────────────────────────────────

        private static Vector2 DictToVector2(object value)
        {
            if (value is Dictionary<string, object> d)
                return TypeConverter.ToVector2(d);
            return Vector2.zero;
        }

        private static Vector3 DictToVector3(object value)
        {
            if (value is Dictionary<string, object> d)
                return TypeConverter.ToVector3(d);
            return Vector3.zero;
        }

        private static Vector4 DictToVector4(object value)
        {
            if (value is Dictionary<string, object> d)
                return TypeConverter.ToVector4(d);
            return Vector4.zero;
        }

        private static Quaternion DictToQuaternion(object value)
        {
            if (value is Dictionary<string, object> d)
                return TypeConverter.ToQuaternion(d);
            return Quaternion.identity;
        }

        private static Color DictToColor(object value)
        {
            if (value is Dictionary<string, object> d)
                return TypeConverter.ToColor(d);
            return Color.white;
        }

        private static Rect DictToRect(object value)
        {
            if (value is Dictionary<string, object> d)
                return TypeConverter.ToRect(d);
            return Rect.zero;
        }

        private static Bounds DictToBounds(object value)
        {
            if (value is Dictionary<string, object> d)
                return TypeConverter.ToBounds(d);
            return new Bounds();
        }
    }
}
