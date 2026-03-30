using System;
using System.Collections.Generic;
using UnityEngine;

namespace Claude.UnityMCP.Communication
{
    /// <summary>
    /// JSON-RPC 2.0 protocol handler.
    /// Parses incoming requests and formats responses per the spec.
    /// </summary>
    public static class JsonRpcHandler
    {
        // ── Request Parsing ──────────────────────────────────────────

        [Serializable]
        public class JsonRpcRequest
        {
            public string jsonrpc = "2.0";
            public string method;
            public string @params; // raw JSON string, parsed by each tool
            public object id;
        }

        [Serializable]
        public class JsonRpcResponse
        {
            public string jsonrpc = "2.0";
            public object id;
            public object result;
            public JsonRpcError error;
        }

        [Serializable]
        public class JsonRpcError
        {
            public int code;
            public string message;
            public object data;
        }

        // ── Standard Error Codes ─────────────────────────────────────

        public const int PARSE_ERROR = -32700;
        public const int INVALID_REQUEST = -32600;
        public const int METHOD_NOT_FOUND = -32601;
        public const int INVALID_PARAMS = -32602;
        public const int INTERNAL_ERROR = -32603;

        // ── Parse a raw JSON string into a request ───────────────────

        public static JsonRpcRequest ParseRequest(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                // Use Unity's JsonUtility for the outer structure
                // but we need manual parsing for params since they're dynamic
                var parsed = ParseJsonObject(json);

                var request = new JsonRpcRequest();
                if (parsed.TryGetValue("jsonrpc", out var version))
                    request.jsonrpc = version?.ToString();
                if (parsed.TryGetValue("method", out var method))
                    request.method = method?.ToString();
                if (parsed.TryGetValue("id", out var id))
                    request.id = id;

                // Extract params as raw JSON substring
                request.@params = ExtractJsonValue(json, "params");

                return request;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP] Failed to parse JSON-RPC request: {e.Message}");
                return null;
            }
        }

        // ── Response Builders ────────────────────────────────────────

        public static string SuccessResponse(object id, object result)
        {
            var response = new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", id },
                { "result", result }
            };
            return MiniJson.Serialize(response);
        }

        public static string ErrorResponse(object id, int code, string message, object data = null)
        {
            var error = new Dictionary<string, object>
            {
                { "code", code },
                { "message", message }
            };
            if (data != null)
                error["data"] = data;

            var response = new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", id },
                { "error", error }
            };
            return MiniJson.Serialize(response);
        }

        // ── MCP Protocol Helpers ─────────────────────────────────────

        public static string InitializeResponse(object id)
        {
            var capabilities = new Dictionary<string, object>
            {
                { "tools", new Dictionary<string, object> { { "listChanged", true } } }
            };

            var serverInfo = new Dictionary<string, object>
            {
                { "name", "unity-mcp" },
                { "version", "1.0.0" }
            };

            var result = new Dictionary<string, object>
            {
                { "protocolVersion", "2024-11-05" },
                { "capabilities", capabilities },
                { "serverInfo", serverInfo }
            };

            return SuccessResponse(id, result);
        }

        // ── Minimal JSON Parsing (no external dependencies) ──────────

        private static Dictionary<string, object> ParseJsonObject(string json)
        {
            var dict = new Dictionary<string, object>();
            json = json.Trim();
            if (json.Length < 2 || json[0] != '{') return dict;

            // Simple top-level key-value extraction
            int i = 1;
            while (i < json.Length - 1)
            {
                // Find key
                int keyStart = json.IndexOf('"', i);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                // Find colon
                int colon = json.IndexOf(':', keyEnd + 1);
                if (colon < 0) break;

                // Extract value (simplified - handles strings, numbers, booleans, null)
                i = colon + 1;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;

                if (json[i] == '"')
                {
                    int valEnd = FindClosingQuote(json, i);
                    dict[key] = json.Substring(i + 1, valEnd - i - 1);
                    i = valEnd + 1;
                }
                else if (json[i] == '{' || json[i] == '[')
                {
                    int valEnd = FindClosingBracket(json, i);
                    dict[key] = json.Substring(i, valEnd - i + 1);
                    i = valEnd + 1;
                }
                else
                {
                    int valEnd = i;
                    while (valEnd < json.Length && json[valEnd] != ',' && json[valEnd] != '}')
                        valEnd++;
                    string val = json.Substring(i, valEnd - i).Trim();
                    if (val == "null") dict[key] = null;
                    else if (val == "true") dict[key] = true;
                    else if (val == "false") dict[key] = false;
                    else if (double.TryParse(val, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double d))
                        dict[key] = d;
                    else dict[key] = val;
                    i = valEnd;
                }

                // Skip comma
                while (i < json.Length && (json[i] == ',' || char.IsWhiteSpace(json[i]))) i++;
            }

            return dict;
        }

        private static string ExtractJsonValue(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + searchKey.Length);
            if (colonIdx < 0) return null;

            int i = colonIdx + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;

            if (i >= json.Length) return null;

            if (json[i] == '{' || json[i] == '[')
            {
                int end = FindClosingBracket(json, i);
                return json.Substring(i, end - i + 1);
            }
            else if (json[i] == '"')
            {
                int end = FindClosingQuote(json, i);
                return json.Substring(i, end - i + 1);
            }
            else
            {
                int end = i;
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']')
                    end++;
                return json.Substring(i, end - i).Trim();
            }
        }

        private static int FindClosingQuote(string json, int openQuote)
        {
            for (int i = openQuote + 1; i < json.Length; i++)
            {
                if (json[i] == '\\') { i++; continue; }
                if (json[i] == '"') return i;
            }
            return json.Length - 1;
        }

        private static int FindClosingBracket(string json, int open)
        {
            char openChar = json[open];
            char closeChar = openChar == '{' ? '}' : ']';
            int depth = 1;
            bool inString = false;
            for (int i = open + 1; i < json.Length; i++)
            {
                if (json[i] == '\\' && inString) { i++; continue; }
                if (json[i] == '"') inString = !inString;
                if (!inString)
                {
                    if (json[i] == openChar) depth++;
                    else if (json[i] == closeChar) { depth--; if (depth == 0) return i; }
                }
            }
            return json.Length - 1;
        }
    }
}
