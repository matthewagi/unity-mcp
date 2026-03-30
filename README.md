<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity" alt="Unity 2021.3+"/>
  <img src="https://img.shields.io/badge/License-MIT-green" alt="MIT License"/>
  <img src="https://img.shields.io/badge/MCP-Compatible-blue" alt="MCP Compatible"/>
  <img src="https://img.shields.io/badge/Tools-20-orange" alt="20 Tools"/>
  <img src="https://img.shields.io/badge/CPU_When_Idle-0%25-brightgreen" alt="0% CPU When Idle"/>
</p>

# Claude Unity MCP

> **Give Claude full control of the Unity Editor.** 20 tools for scripts, scenes, components, assets, settings, and builds — all through the Model Context Protocol.

A lightweight MCP server that runs directly inside Unity's editor process. It uses a single background thread with kernel-level socket polling, so it costs **zero CPU when idle** and responds instantly when Claude sends a command. No async, no ThreadPool, no external runtimes — pure C# with raw socket I/O.

---

## Why Use This?

- **Talk to Unity in plain English** — ask Claude to create scripts, build scenes, tweak physics settings, or kick off a build
- **Zero performance impact** — the server sleeps until Claude talks to it. Your editor stays snappy.
- **Works with any MCP client** — Claude Desktop, Claude Code, Cursor, or anything that speaks MCP
- **Always on** — starts automatically with Unity, survives domain reloads and recompiles
- **Fully undoable** — every destructive operation goes through Unity's Undo system

---

## Quick Start

### 1. Copy the package into your Unity project

```
YourProject/
  Packages/
    com.claude.unity-mcp/       <-- copy this entire folder here
      Editor/
      package.json
      mcp-bridge.mjs
```

You can clone this repo and copy the files, or add it as a local package.

### 2. Open Unity

The package auto-compiles. You'll see in the console:

```
[MCP] Ready on port 9999
```

Open **Window > Claude MCP** to verify the server is running and see all available tools.

### 3. Configure your MCP client

<details>
<summary><strong>Claude Desktop (recommended)</strong></summary>

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

> **Tip:** Open Window > Claude MCP in Unity and click **Copy Config** to get this JSON with the correct path pre-filled.

</details>

<details>
<summary><strong>Claude Code / Cursor / Direct HTTP</strong></summary>

If your client supports Streamable HTTP transport:

```json
{
  "mcpServers": {
    "unity": {
      "url": "http://localhost:9999/mcp"
    }
  }
}
```

</details>

### 4. Restart your MCP client

Restart Claude Desktop or your MCP client. It connects to Unity's MCP server automatically. You're ready to go.

---

## All 20 Tools

### Scripts

| Tool | What it does |
|------|-------------|
| `unity_create_script` | Create C# scripts from templates — MonoBehaviour, ScriptableObject, EditorWindow, static utility. Supports custom base classes and using statements. |
| `unity_modify_script` | Modify existing scripts — full replacement, find/replace, add methods, or add using statements. |

### Scene & Hierarchy

| Tool | What it does |
|------|-------------|
| `unity_get_scene` | Read the full scene hierarchy as structured JSON. Supports depth control, tag/component filtering, and inactive objects. |
| `unity_create_gameobject` | Create GameObjects with primitives (cube, sphere, etc.), components, parent, transform, tag, layer, and static flag. |
| `unity_delete` | Delete GameObjects or assets by ID, path, or name. Fully undoable. |
| `unity_duplicate` | Duplicate a GameObject with optional rename and reparent. |

### Components

| Tool | What it does |
|------|-------------|
| `unity_get_components` | Read all component data from a GameObject including serialized properties. Filter by type or property path. |
| `unity_set_property` | Set serialized properties on components. Supports batched operations for multiple properties at once. |
| `unity_add_component` | Add any component (Rigidbody, BoxCollider, AudioSource, etc.) with optional initial property values. |
| `unity_remove_component` | Remove a component from a GameObject. |

### Assets

| Tool | What it does |
|------|-------------|
| `unity_search_assets` | Search assets by name and/or type (Material, Texture2D, ScriptableObject, Prefab, etc.). |
| `unity_get_asset` | Read detailed metadata and properties of any asset. |
| `unity_create_asset` | Create new assets — Materials, ScriptableObjects, Textures, Prefabs, Folders, AnimationClips. |
| `unity_import_asset` | Re-import assets to pick up external changes. |

### Editor Control

| Tool | What it does |
|------|-------------|
| `unity_editor_command` | Execute editor commands: play, pause, stop, step, save, undo, redo, compile, refresh. Also run any Unity menu item. |
| `unity_get_selection` | Get all currently selected GameObjects and assets. |
| `unity_set_selection` | Select objects in the hierarchy/project with optional ping highlight. |
| `unity_scene_view` | Control the Scene View camera — frame selection, focus point, move, orbit. |

### Settings & Build

| Tool | What it does |
|------|-------------|
| `unity_get_settings` | Read project settings: Physics, Player, Quality, Audio, Time, Graphics, Input, Tags, Editor, Navigation. |
| `unity_set_settings` | Modify project settings via SerializedProperty. All changes are undoable. |
| `unity_build` | Build for Windows, macOS, Linux, WebGL, Android, or iOS. Supports development builds and custom BuildOptions. |
| `unity_manage_packages` | Add, remove, or list Unity packages. |
| `unity_get_console` | Read Unity console logs with filtering (errors, warnings, all). Supports timestamp filtering and clearing. |

### Code Execution

| Tool | What it does |
|------|-------------|
| `unity_execute_csharp` | Execute arbitrary C# code inside the editor with full access to UnityEngine and UnityEditor APIs. Compiled in-memory, no domain reload. |

---

## Architecture

```
Claude Desktop / Claude Code / Cursor
        |
        | stdio (JSON-RPC 2.0)
        v
  mcp-bridge.mjs                    Node.js — translates stdio <-> HTTP
        |
        | HTTP POST localhost:9999/mcp
        v
  Unity Editor (MCPServer)
        |
        |-- StreamableHttpServer       Raw TcpListener, single BG thread, Socket.Poll
        |-- MainThreadDispatcher       ConcurrentQueue + AutoResetEvent
        |-- JsonRpcHandler             JSON-RPC 2.0 message parsing
        |-- Tools/*                    20 tool handlers
        |-- Serialization/*            GameObjectSerializer, AssetSerializer, etc.
```

### How it stays at zero CPU when idle

| Component | What happens when idle |
|-----------|----------------------|
| `StreamableHttpServer` | Single thread blocked on `Socket.Poll(1s)` — true kernel wait, zero CPU |
| `MainThreadDispatcher` | `ConcurrentQueue.TryDequeue` returns false — one boolean check per frame (~0 ns) |
| `MCPServer.Tick()` | One `EditorApplication.update` callback, nanosecond cost when queue is empty |
| Status Window | No continuous repaint — only updates on user interaction |

**No async. No Task. No ThreadPool. No polling loops. No timers.**

---

## File Structure

```
com.claude.unity-mcp/
  package.json                          Unity package manifest
  mcp-bridge.mjs                        Node.js stdio-to-HTTP bridge
  LICENSE                               MIT license
  Editor/
    MCPServer.cs                        Main server — init, routing, start/stop/restart
    Communication/
      StreamableHttpServer.cs           Raw TCP listener (single thread, Socket.Poll)
      MainThreadDispatcher.cs           Background thread -> main thread work queue
      JsonRpcHandler.cs                 JSON-RPC 2.0 message parsing & dispatch
      MiniJson.cs                       Lightweight JSON parser (zero dependencies)
    Tools/
      ScriptTools.cs                    Create / modify C# scripts
      SceneTools.cs                     Scene hierarchy read / create / delete / duplicate
      ComponentTools.cs                 Component get / set / add / remove
      AssetTools.cs                     Asset search / inspect / create / import
      EditorTools.cs                    Editor commands, selection, scene view camera
      SettingsTools.cs                  Project settings read / write
      BuildTools.cs                     Build player + package management + console
      ExecuteTools.cs                   Run arbitrary C# in-editor
    Serialization/
      GameObjectSerializer.cs           Serialize GameObjects to structured JSON
      AssetSerializer.cs                Serialize assets to structured JSON
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

Open **Window > Claude MCP** in Unity to monitor and control the server:

- **Start / Stop** — manually control the server
- **Enable / Disable** — toggle auto-start on editor launch
- **Restart** — stop + start (useful after config changes)
- **Port** — default 9999, configurable in Advanced Settings
- **Copy Config** — copies the MCP client config JSON to your clipboard with the correct path

---

## Compatibility

| | Supported |
|---|-----------|
| **Unity** | 2021.3 LTS and newer (tested on Unity 6.0) |
| **Render Pipeline** | URP, HDRP, Built-in |
| **OS** | macOS, Windows |
| **MCP Clients** | Claude Desktop, Claude Code, Cursor, or any MCP-compatible client |

---

## Troubleshooting

**Can't connect to the MCP server**
- Make sure Unity is open with the MCP package installed
- Check **Window > Claude MCP** — status should show "Running"
- Verify nothing else is using port 9999 (`lsof -i :9999` on macOS)

**Server not starting automatically**
- Open Window > Claude MCP and make sure the Enable toggle is on
- The server auto-starts via `[InitializeOnLoad]` after every domain reload

**Port conflict**
- Open Window > Claude MCP > Advanced Settings
- Change the port (e.g., 10000)
- Update your MCP client config to match the new port

**Bridge not connecting (Claude Desktop)**
- Make sure Node.js is installed (`node --version`)
- Verify the path to `mcp-bridge.mjs` in your Claude config is correct
- Restart Claude Desktop after changing the config

---

## Contributing

Pull requests welcome. If you add a new tool, follow the existing pattern in `Editor/Tools/` — each tool is a static method with a `[Tool]`-style registration in `MCPServer.cs`.

---

## License

[MIT](LICENSE) — use it however you want.
