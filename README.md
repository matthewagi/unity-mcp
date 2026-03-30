# Claude Unity MCP

A lightweight MCP (Model Context Protocol) server that runs inside the Unity Editor, giving Claude direct access to manage scripts, scenes, components, assets, settings, and builds. Designed for zero lag — uses a single background thread with kernel-level socket polling, so it costs nothing when idle.

## Features

- **20 tools** giving Claude full control over the Unity Editor
- **Zero lag** — single background thread with `Socket.Poll`, nanosecond-cost idle check
- **No async / no ThreadPool** — no thread pollution, no lingering tasks
- **Works with Claude Desktop, Claude Code, Cursor**, or any MCP-compatible client
- **Always on** — auto-starts with Unity, survives domain reloads
- **Undo support** — all destructive operations are undoable

---

## Quick Setup

### 1. Copy the package

Copy the `com.claude.unity-mcp` folder into your Unity project:

```
YourProject/
  Packages/
    com.claude.unity-mcp/       <-- this folder
      Editor/
      package.json
      mcp-bridge.mjs
      ...
```

### 2. Open Unity

The package auto-compiles. You'll see in the console:

```
[MCP] Ready on port 9999
```

Verify via **Window > Claude MCP** — shows server status, port, and available tools.

### 3. Configure your MCP client

#### Claude Desktop (stdio bridge)

Add to your config file:

- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": [
        "/FULL/PATH/TO/YourProject/Packages/com.claude.unity-mcp/mcp-bridge.mjs"
      ]
    }
  }
}
```

#### Claude Code / Cursor / Direct HTTP

If your client supports HTTP transport:

```json
{
  "mcpServers": {
    "unity": {
      "url": "http://localhost:9999/mcp"
    }
  }
}
```

### 4. Restart your MCP client

Restart Claude Desktop / Claude Code. It connects to Unity's MCP server automatically.

---

## All 20 Tools

### Scripts (2 tools)

| Tool | Description |
|------|-------------|
| `unity_create_script` | Create new C# scripts from templates (MonoBehaviour, ScriptableObject, EditorWindow, static utility). Supports custom base classes and using statements. |
| `unity_modify_script` | Modify existing scripts — full replacement, find/replace, add methods, or add using statements. |

### Scene & Hierarchy (4 tools)

| Tool | Description |
|------|-------------|
| `unity_get_scene` | Read the full scene hierarchy as structured data. Supports depth control, tag/component filtering, and inactive objects. |
| `unity_create_gameobject` | Create GameObjects with optional primitives (cube, sphere, etc.), components, parent, transform, tag, layer, and static flag. |
| `unity_delete` | Delete GameObjects or assets by ID, path, or name. Fully undoable. |
| `unity_duplicate` | Duplicate a GameObject with optional rename and reparent. |

### Components (4 tools)

| Tool | Description |
|------|-------------|
| `unity_get_components` | Read all component data from a GameObject including serialized properties. Filter by type or specific property paths. |
| `unity_set_property` | Set serialized properties on components. Supports batched operations for multiple properties at once. |
| `unity_add_component` | Add a component (Rigidbody, BoxCollider, AudioSource, etc.) with optional initial property values. |
| `unity_remove_component` | Remove a component from a GameObject. |

### Assets (4 tools)

| Tool | Description |
|------|-------------|
| `unity_search_assets` | Search assets by name and/or type (Material, Texture2D, ScriptableObject, Prefab, etc.). |
| `unity_get_asset` | Read detailed metadata and properties of any asset. |
| `unity_create_asset` | Create new assets — Materials, ScriptableObjects, Textures, Prefabs, Folders, AnimationClips. |
| `unity_import_asset` | Re-import assets to pick up external changes. |

### Editor Control (4 tools)

| Tool | Description |
|------|-------------|
| `unity_editor_command` | Execute editor commands: play, pause, stop, step, save, undo, redo, compile, refresh. Also supports running any Unity menu item. |
| `unity_get_selection` | Get all currently selected GameObjects and assets. |
| `unity_set_selection` | Select objects in the hierarchy/project with optional ping highlight. |
| `unity_scene_view` | Control the Scene View camera — frame selection, focus point, move, orbit. |

### Settings (2 tools)

| Tool | Description |
|------|-------------|
| `unity_get_settings` | Read project settings by category: Physics, Player, Quality, Audio, Time, Graphics, Input, Tags, Editor, Navigation. |
| `unity_set_settings` | Modify project settings via SerializedProperty. All changes are undoable. |

### Build & Packages (3 tools)

| Tool | Description |
|------|-------------|
| `unity_build` | Build for Windows, macOS, Linux, WebGL, Android, or iOS. Supports development builds and custom BuildOptions. |
| `unity_manage_packages` | Add, remove, or list Unity packages. |
| `unity_get_console` | Read Unity console logs with filtering (errors, warnings, all). Supports timestamp filtering and clearing. |

### Code Execution (1 tool)

| Tool | Description |
|------|-------------|
| `unity_execute_csharp` | Execute arbitrary C# code inside the editor with full access to UnityEngine and UnityEditor APIs. Compiled in-memory, no domain reload. Safety checks block dangerous operations. |

---

## Architecture

```
Claude Desktop / Claude Code / Cursor
        |
        | stdio (JSON-RPC)
        v
  mcp-bridge.mjs              Node.js — translates stdio <-> HTTP
        |
        | HTTP POST localhost:9999/mcp
        v
  Unity Editor (MCPServer)
        |
        |-- StreamableHttpServer      Raw TcpListener, single BG thread, Socket.Poll
        |-- MainThreadDispatcher      ConcurrentQueue + AutoResetEvent
        |-- Tools/*                   ScriptTools, SceneTools, ComponentTools, etc.
        |-- Serialization/*           GameObjectSerializer, AssetSerializer, etc.
```

### Why zero lag

| Component | Idle cost |
|-----------|-----------|
| `StreamableHttpServer` | Single thread blocked on `Socket.Poll(1s)` — true kernel wait, zero CPU |
| `MainThreadDispatcher` | `ConcurrentQueue.TryDequeue` returns false — single boolean check per frame (~0 ns) |
| `MCPServer.Tick()` | One `EditorApplication.update` callback calling `ProcessPending()` — nanosecond cost when empty |
| Status Window | No continuous repaint — only updates on user interaction |

No async. No Task. No ThreadPool. No polling loops. No timers.

---

## File Structure

```
com.claude.unity-mcp/
  package.json                          Unity package manifest
  mcp-bridge.mjs                        Node.js stdio-to-HTTP bridge
  Editor/
    MCPServer.cs                        Main server — init, routing, start/stop
    Communication/
      StreamableHttpServer.cs           Raw TCP server (single thread, Socket.Poll)
      MainThreadDispatcher.cs           BG thread -> main thread work queue
      JsonRpcHandler.cs                 JSON-RPC 2.0 message parsing
      MiniJson.cs                       Lightweight JSON parser (no dependencies)
    Tools/
      ScriptTools.cs                    Create / modify C# scripts
      SceneTools.cs                     Scene hierarchy operations
      ComponentTools.cs                 Component get/set/add/remove
      AssetTools.cs                     Asset search/inspect/create/import
      EditorTools.cs                    Editor commands, selection, scene view
      SettingsTools.cs                  Project settings read/write
      BuildTools.cs                     Build and package management
      ExecuteTools.cs                   Run arbitrary C# in editor
    Serialization/
      GameObjectSerializer.cs           Serialize GameObjects to JSON
      AssetSerializer.cs                Serialize assets to JSON
      TypeConverter.cs                  Type conversion utilities
      SerializedPropertyHelper.cs       SerializedProperty read/write helpers
    UI/
      MCPStatusWindow.cs                Window > Claude MCP status panel
    Utils/
      InstanceTracker.cs                GameObject instance lookup cache
      UndoHelper.cs                     Undo operation wrapper
```

---

## Status Window

Open **Window > Claude MCP** in Unity:

- **Start / Stop** the server manually
- **Enable / Disable** toggle — controls auto-start
- **Restart** — stop + start (useful after config changes)
- **Port** — default 9999, changeable in Advanced Settings
- **Copy Config** — copies MCP client config JSON to clipboard

---

## Compatibility

- **Unity** 2021.3+ (tested on Unity 6.0)
- **Render pipelines**: URP, HDRP, Built-in
- **Platforms**: macOS, Windows
- **MCP clients**: Claude Desktop, Claude Code, Cursor, or any MCP-compatible tool

---

## Troubleshooting

**"Cannot connect to Unity MCP server"**
- Make sure Unity is open with the MCP package loaded
- Check **Window > Claude MCP** — should show "Running"
- Verify nothing else is using port 9999

**Server not starting**
- Check the MCP toggle is enabled in Window > Claude MCP
- Server auto-starts via `[InitializeOnLoad]` after every domain reload

**Port conflict**
- Open Window > Claude MCP > Advanced Settings
- Change to another port (e.g., 10000)
- Update your Claude config to match

---

## License

MIT
