using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using Claude.UnityMCP.Communication;
using Claude.UnityMCP.Serialization;

namespace Claude.UnityMCP.Tools
{
    /// <summary>
    /// MCP tool for executing arbitrary C# code inside the Unity Editor.
    /// Uses CodeDomProvider for IN-MEMORY compilation — no temp files, no domain reload.
    /// </summary>
    public static class ExecuteTools
    {
        private const int MaxOutputLength = 100000;

        // ── unity_execute_csharp ─────────────────────────────────────

        public static Dictionary<string, object> ExecuteCSharp(Dictionary<string, object> args)
        {
            string code = TypeConverter.GetString(args, "code");
            if (string.IsNullOrEmpty(code))
                return Error("No code provided");

            // Safety validation
            var safetyIssue = ValidateSafety(code);
            if (safetyIssue != null)
                return Error($"Safety check failed: {safetyIssue}");

            try
            {
                return ExecuteInMemory(code);
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error", ex.Message },
                    { "stack_trace", ex.StackTrace }
                };
            }
        }

        // ── In-memory compilation via CodeDomProvider ─────────────────

        private static Dictionary<string, object> ExecuteInMemory(string code)
        {
            code = code.Trim();

            // Capture Debug.Log output
            var logMessages = new List<string>();
            Application.LogCallback logHandler = (msg, stackTrace, type) =>
            {
                logMessages.Add($"[{type}] {msg}");
            };
            Application.logMessageReceived += logHandler;

            object result = null;
            string error = null;

            try
            {
                string className = $"MCPExec_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                string wrappedCode = WrapCode(code, className);

                // Compile in memory using CodeDomProvider
                var provider = CodeDomProvider.CreateProvider("CSharp");
                var parameters = new CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    IncludeDebugInformation = false,
                    TreatWarningsAsErrors = false
                };

                // Add references to all loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                            parameters.ReferencedAssemblies.Add(assembly.Location);
                    }
                    catch { }
                }

                var results = provider.CompileAssemblyFromSource(parameters, wrappedCode);

                if (results.Errors.HasErrors)
                {
                    var errors = new StringBuilder();
                    foreach (CompilerError ce in results.Errors)
                    {
                        if (!ce.IsWarning)
                            errors.AppendLine($"Line {ce.Line}: {ce.ErrorText}");
                    }
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error", "Compilation failed:\n" + errors.ToString() },
                        { "code", wrappedCode }
                    };
                }

                // Execute
                var type = results.CompiledAssembly.GetType(className);
                if (type == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error", "Compiled type not found" }
                    };
                }

                var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    result = method.Invoke(null, null);
                }
            }
            catch (TargetInvocationException tie)
            {
                error = tie.InnerException?.Message ?? tie.Message;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                Application.logMessageReceived -= logHandler;
            }

            if (error != null)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error", error },
                    { "output", string.Join("\n", logMessages) }
                };
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "result", FormatResult(result) },
                { "output", string.Join("\n", logMessages) }
            };
        }

        // ── Code Wrapping ────────────────────────────────────────────

        private static string WrapCode(string code, string className)
        {
            code = code.Trim();

            // If it already contains a class definition, use as-is
            if (code.Contains("class ") && code.Contains("{"))
            {
                return $@"using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

{code}";
            }

            // Check if code has a return statement
            bool hasReturn = code.Contains("return ");

            string body;
            if (hasReturn)
            {
                body = code;
            }
            else
            {
                var lines = code.Split('\n').Select(l => l.TrimEnd()).Where(l => !string.IsNullOrEmpty(l)).ToList();
                if (lines.Count > 0)
                {
                    var lastLine = lines[lines.Count - 1].TrimEnd(';');
                    bool isExpression = !lastLine.StartsWith("var ") && !lastLine.StartsWith("int ") &&
                                       !lastLine.StartsWith("float ") && !lastLine.StartsWith("string ") &&
                                       !lastLine.StartsWith("bool ") && !lastLine.StartsWith("if ") &&
                                       !lastLine.StartsWith("for ") && !lastLine.StartsWith("foreach ") &&
                                       !lastLine.StartsWith("while ") && !Regex.IsMatch(lastLine, @"^\w+\s+\w+\s*=");

                    if (isExpression && lines.Count == 1)
                    {
                        body = $"return (object)({lastLine});";
                    }
                    else
                    {
                        body = code + "\nreturn null;";
                    }
                }
                else
                {
                    body = "return null;";
                }
            }

            return $@"using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class {className}
{{
    public static object Execute()
    {{
        {body}
    }}
}}";
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static string ValidateSafety(string code)
        {
            var blocked = new[]
            {
                @"Process\.Start", @"Process\.Kill",
                @"Environment\.Exit",
                @"AppDomain\.CreateDomain",
                @"Registry\.",
                @"EditorApplication\.Exit",
            };

            foreach (var pattern in blocked)
            {
                if (Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase))
                    return $"Blocked dangerous operation: {pattern}";
            }
            return null;
        }

        private static string FormatResult(object result)
        {
            if (result == null) return "null";
            if (result is string s) return s;
            if (result is bool b) return b.ToString().ToLower();

            if (result is UnityEngine.Object uObj)
                return $"{uObj.GetType().Name}({uObj.name}, id:{uObj.GetInstanceID()})";

            if (result is System.Collections.IEnumerable enumerable && !(result is string))
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                    items.Add(FormatResult(item));
                return $"[{string.Join(", ", items)}]";
            }

            return result.ToString();
        }

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object> { { "error", message } };
        }

        // ── Tool Definitions ─────────────────────────────────────────

        public static List<Dictionary<string, object>> GetToolDefinitions()
        {
            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "name", "unity_execute_csharp" },
                    { "description", "Execute arbitrary C# code inside Unity Editor. Code runs with full access to UnityEngine and UnityEditor APIs. Use this as an escape hatch for anything the other tools don't cover. Code is compiled as a temp script and executed immediately." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "code", new Dictionary<string, object>
                                        {
                                            { "type", "string" },
                                            { "description", "C# code to execute. Has access to UnityEngine, UnityEditor, System, System.Linq, etc. Last expression is returned as result. Use Debug.Log() for output." }
                                        }
                                    }
                                }
                            },
                            { "required", new List<object> { "code" } }
                        }
                    }
                }
            };
        }
    }
}
