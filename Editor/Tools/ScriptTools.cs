using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Claude.UnityMCP.Communication;
using Claude.UnityMCP.Serialization;
using Claude.UnityMCP.Utils;

namespace Claude.UnityMCP.Tools
{
    /// <summary>
    /// MCP tools for script creation and modification:
    ///   - unity_create_script: Create new C# scripts with templates
    ///   - unity_modify_script: Edit existing scripts (replace, find_replace, add_method, add_using)
    /// </summary>
    public static class ScriptTools
    {
        // ── unity_create_script ──────────────────────────────────────────

        public static Dictionary<string, object> CreateScript(Dictionary<string, object> args)
        {
            string path = TypeConverter.GetString(args, "path");
            string scriptTemplate = TypeConverter.GetString(args, "template", "monobehaviour");
            string className = TypeConverter.GetString(args, "class_name");
            string baseClass = TypeConverter.GetString(args, "base_class");
            var usings = TypeConverter.GetList(args, "usings");

            if (string.IsNullOrEmpty(path))
                return Error("No path provided");

            // Ensure .cs extension
            if (!path.EndsWith(".cs"))
                path = path + ".cs";

            // Ensure path is in Assets folder
            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path;

            // Infer class name from filename if not provided
            if (string.IsNullOrEmpty(className))
                className = Path.GetFileNameWithoutExtension(path);

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

            // Check if file already exists
            if (File.Exists(path))
                return Error($"Script already exists at {path}");

            try
            {
                string content = GenerateScriptTemplate(scriptTemplate, className, baseClass, usings);

                File.WriteAllText(path, content);
                AssetDatabase.Refresh();

                return new Dictionary<string, object>
                {
                    { "created", true },
                    { "path", path },
                    { "class_name", className },
                    { "template", scriptTemplate }
                };
            }
            catch (Exception ex)
            {
                return Error($"Failed to create script: {ex.Message}");
            }
        }

        // ── unity_modify_script ──────────────────────────────────────────

        public static Dictionary<string, object> ModifyScript(Dictionary<string, object> args)
        {
            string path = TypeConverter.GetString(args, "path");
            string operation = TypeConverter.GetString(args, "operation", "replace_all");

            if (string.IsNullOrEmpty(path))
                return Error("No path provided");

            if (!File.Exists(path))
                return Error($"Script not found: {path}");

            try
            {
                string content = File.ReadAllText(path);
                string modified = content;
                var operationResults = new List<object>();

                switch (operation.ToLower())
                {
                    case "replace_all":
                        // Replace entire content
                        string newContent = TypeConverter.GetString(args, "content");
                        if (string.IsNullOrEmpty(newContent))
                            return Error("No content provided for replace_all");
                        modified = newContent;
                        operationResults.Add(new Dictionary<string, object>
                        {
                            { "operation", "replace_all" },
                            { "success", true },
                            { "lines_replaced", content.Split('\n').Length }
                        });
                        break;

                    case "find_replace":
                        // Find and replace substring
                        string find = TypeConverter.GetString(args, "find");
                        string replace = TypeConverter.GetString(args, "replace");
                        if (string.IsNullOrEmpty(find))
                            return Error("No find pattern provided");

                        if (modified.Contains(find))
                        {
                            modified = modified.Replace(find, replace ?? "");
                            operationResults.Add(new Dictionary<string, object>
                            {
                                { "operation", "find_replace" },
                                { "success", true },
                                { "found", true }
                            });
                        }
                        else
                        {
                            operationResults.Add(new Dictionary<string, object>
                            {
                                { "operation", "find_replace" },
                                { "success", false },
                                { "error", "Pattern not found in script" }
                            });
                        }
                        break;

                    case "add_method":
                        // Add method before closing brace
                        string methodCode = TypeConverter.GetString(args, "method_code");
                        if (string.IsNullOrEmpty(methodCode))
                            return Error("No method_code provided");

                        modified = AddMethodToScript(modified, methodCode);
                        operationResults.Add(new Dictionary<string, object>
                        {
                            { "operation", "add_method" },
                            { "success", true }
                        });
                        break;

                    case "add_using":
                        // Add using statement at the top
                        string usingStatement = TypeConverter.GetString(args, "using_statement");
                        if (string.IsNullOrEmpty(usingStatement))
                            return Error("No using_statement provided");

                        if (!usingStatement.StartsWith("using "))
                            usingStatement = "using " + usingStatement;
                        if (!usingStatement.EndsWith(";"))
                            usingStatement = usingStatement + ";";

                        modified = AddUsingToScript(modified, usingStatement);
                        operationResults.Add(new Dictionary<string, object>
                        {
                            { "operation", "add_using" },
                            { "success", true }
                        });
                        break;

                    default:
                        return Error($"Unknown operation: {operation}");
                }

                // Write modified content if anything changed
                if (modified != content)
                {
                    File.WriteAllText(path, modified);
                    AssetDatabase.Refresh();
                }

                return new Dictionary<string, object>
                {
                    { "modified", modified != content },
                    { "path", path },
                    { "operations", operationResults }
                };
            }
            catch (Exception ex)
            {
                return Error($"Failed to modify script: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string GenerateScriptTemplate(string template, string className, string baseClass, List<object> usings)
        {
            var sb = new StringBuilder();

            // Usings
            var defaultUsings = new List<string>
            {
                "using UnityEngine;"
            };

            if (usings != null && usings.Count > 0)
            {
                foreach (var u in usings)
                {
                    string usingStr = u.ToString();
                    if (!usingStr.StartsWith("using ")) usingStr = "using " + usingStr;
                    if (!usingStr.EndsWith(";")) usingStr = usingStr + ";";
                    defaultUsings.Add(usingStr);
                }
            }

            // Remove duplicates
            var uniqueUsings = new HashSet<string>(defaultUsings);
            foreach (var u in uniqueUsings)
                sb.AppendLine(u);

            sb.AppendLine();

            // Class declaration
            string classDecl = $"public class {className}";
            if (!string.IsNullOrEmpty(baseClass))
                classDecl += $" : {baseClass}";
            else
            {
                // Default base classes based on template
                switch (template.ToLower())
                {
                    case "monobehaviour":
                        classDecl += " : MonoBehaviour";
                        break;
                    case "scriptableobject":
                        classDecl += " : ScriptableObject";
                        break;
                    case "editorwindow":
                        classDecl += " : EditorWindow";
                        break;
                }
            }

            sb.AppendLine(classDecl);
            sb.AppendLine("{");

            // Template-specific content
            switch (template.ToLower())
            {
                case "monobehaviour":
                    sb.AppendLine("    private void Start()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        ");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    sb.AppendLine("    private void Update()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        ");
                    sb.AppendLine("    }");
                    break;

                case "scriptableobject":
                    sb.AppendLine("    // Add your serializable fields here");
                    sb.AppendLine("    // They will appear in the Inspector");
                    break;

                case "editorwindow":
                    sb.AppendLine("    [MenuItem(\"Window/My Editor Window\")]");
                    sb.AppendLine("    public static void ShowWindow()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        GetWindow<" + className + ">(\"My Window\");");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    sb.AppendLine("    private void OnGUI()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        GUILayout.Label(\"Hello from Editor Window\", EditorStyles.largeLabel);");
                    sb.AppendLine("    }");
                    break;

                case "utility":
                case "static":
                    sb.AppendLine("    // Add your static methods here");
                    break;

                default:
                    sb.AppendLine("    // Custom implementation");
                    break;
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string AddMethodToScript(string scriptContent, string methodCode)
        {
            // Find the last closing brace (class closing brace)
            int lastBraceIndex = scriptContent.LastIndexOf('}');
            if (lastBraceIndex < 0)
                return scriptContent;

            // Insert before the last brace
            return scriptContent.Insert(lastBraceIndex, "\n" + methodCode + "\n");
        }

        private static string AddUsingToScript(string scriptContent, string usingStatement)
        {
            // Check if using already exists
            if (scriptContent.Contains(usingStatement))
                return scriptContent;

            // Find the last using statement
            int lastUsingIndex = scriptContent.LastIndexOf("using ");
            if (lastUsingIndex < 0)
            {
                // No using statements, add at the beginning
                return usingStatement + "\n" + scriptContent;
            }

            // Find the end of the last using statement (semicolon)
            int endOfLastUsing = scriptContent.IndexOf(';', lastUsingIndex);
            if (endOfLastUsing < 0)
                return scriptContent;

            // Insert after the last using statement
            return scriptContent.Insert(endOfLastUsing + 1, "\n" + usingStatement);
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
                    { "name", "unity_create_script" },
                    { "description", "Create a new C# script from a template (MonoBehaviour, ScriptableObject, EditorWindow, or static utility)." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Script path (e.g., 'Assets/Scripts/MyScript'). .cs added automatically." } } },
                                    { "template", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<object> { "monobehaviour", "scriptableobject", "editorwindow", "utility", "static" } }, { "description", "Script template type. Default: monobehaviour" } } },
                                    { "class_name", new Dictionary<string, object> { { "type", "string" }, { "description", "Class name. Defaults to filename." } } },
                                    { "base_class", new Dictionary<string, object> { { "type", "string" }, { "description", "Override default base class (e.g., 'MyBaseClass')" } } },
                                    { "usings", new Dictionary<string, object> { { "type", "array" }, { "items", new Dictionary<string, object> { { "type", "string" } } }, { "description", "Additional using statements (without 'using' prefix)" } } }
                                }
                            },
                            { "required", new List<object> { "path" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "unity_modify_script" },
                    { "description", "Modify an existing script via find_replace, replace_all, add_method, or add_using operations." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to script to modify" } } },
                                    { "operation", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<object> { "replace_all", "find_replace", "add_method", "add_using" } }, { "description", "Type of modification. Default: replace_all" } } },
                                    { "content", new Dictionary<string, object> { { "type", "string" }, { "description", "New full content (for replace_all)" } } },
                                    { "find", new Dictionary<string, object> { { "type", "string" }, { "description", "Pattern to find (for find_replace)" } } },
                                    { "replace", new Dictionary<string, object> { { "type", "string" }, { "description", "Replacement text (for find_replace)" } } },
                                    { "method_code", new Dictionary<string, object> { { "type", "string" }, { "description", "Method code to insert (for add_method). Will be placed before closing class brace." } } },
                                    { "using_statement", new Dictionary<string, object> { { "type", "string" }, { "description", "Using statement to add, e.g., 'System.Collections' (for add_using)" } } }
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
