using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Claude.UnityMCP.Utils
{
    /// <summary>
    /// Wraps all modification operations in Undo groups.
    /// Every change Claude makes is fully undoable via Ctrl+Z.
    /// </summary>
    public static class UndoHelper
    {
        /// <summary>
        /// Begin an undo group for a batch of related modifications.
        /// </summary>
        public static void BeginGroup(string name)
        {
            Undo.SetCurrentGroupName(name);
        }

        /// <summary>
        /// End the current undo group.
        /// </summary>
        public static void EndGroup()
        {
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        /// <summary>
        /// Record object state before modification.
        /// </summary>
        public static void RecordObject(UnityEngine.Object obj, string label)
        {
            if (obj != null)
                Undo.RecordObject(obj, $"MCP: {label}");
        }

        /// <summary>
        /// Register a newly created GameObject with the undo system.
        /// </summary>
        public static void RegisterCreated(GameObject go, string label)
        {
            Undo.RegisterCreatedObjectUndo(go, $"MCP: {label}");
        }

        /// <summary>
        /// Register a newly added component with the undo system.
        /// Returns the component (for chaining).
        /// </summary>
        public static Component AddComponent(GameObject go, System.Type type)
        {
            return Undo.AddComponent(go, type);
        }

        /// <summary>
        /// Destroy an object with undo support.
        /// </summary>
        public static void DestroyObject(UnityEngine.Object obj, string label)
        {
            Undo.DestroyObjectImmediate(obj);
        }

        /// <summary>
        /// Set parent with undo support.
        /// </summary>
        public static void SetParent(Transform child, Transform parent, string label)
        {
            Undo.SetTransformParent(child, parent, $"MCP: {label}");
        }

        /// <summary>
        /// Mark the active scene as dirty so Unity knows to save it.
        /// </summary>
        public static void MarkSceneDirty()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Perform undo.
        /// </summary>
        public static void PerformUndo()
        {
            Undo.PerformUndo();
        }

        /// <summary>
        /// Perform redo.
        /// </summary>
        public static void PerformRedo()
        {
            Undo.PerformRedo();
        }
    }
}
