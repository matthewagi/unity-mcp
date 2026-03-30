#!/usr/bin/env node

/**
 * MCP stdio server for Unity MCP.
 * Claude Desktop launches this as a child process (stdio transport).
 * Handles MCP protocol locally; forwards tools/list and tools/call to Unity HTTP server.
 */

import http from 'http';

const UNITY_HOST = 'localhost';
const UNITY_PORT = 9999;
const UNITY_PATH = '/mcp';

// ── MCP Protocol Constants ──────────────────────────────────────────

const SERVER_INFO = {
  name: 'unity-mcp',
  version: '1.0.0'
};

const CAPABILITIES = {
  tools: { listChanged: true }
};

const PROTOCOL_VERSION = '2024-11-05';

// ── stdio JSON-RPC Transport ────────────────────────────────────────

let buffer = '';

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  buffer += chunk;

  let newlineIdx;
  while ((newlineIdx = buffer.indexOf('\n')) !== -1) {
    const line = buffer.substring(0, newlineIdx).trim();
    buffer = buffer.substring(newlineIdx + 1);
    if (line && line.startsWith('{')) {
      handleMessage(line);
    }
  }
});

process.stdin.resume();

// ── Message Handler ─────────────────────────────────────────────────

function handleMessage(jsonStr) {
  let request;
  try {
    request = JSON.parse(jsonStr);
  } catch (e) {
    sendResponse({
      jsonrpc: '2.0',
      id: null,
      error: { code: -32700, message: 'Parse error', data: e.message }
    });
    return;
  }

  const method = request.method;
  const id = request.id; // may be undefined for notifications

  // ── Handle MCP protocol messages locally ──

  if (method === 'initialize') {
    sendResponse({
      jsonrpc: '2.0',
      id: id,
      result: {
        protocolVersion: PROTOCOL_VERSION,
        capabilities: CAPABILITIES,
        serverInfo: SERVER_INFO
      }
    });
    return;
  }

  if (method === 'notifications/initialized' || method === 'initialized') {
    // This is a notification — no response required
    return;
  }

  if (method === 'ping') {
    sendResponse({
      jsonrpc: '2.0',
      id: id,
      result: {}
    });
    return;
  }

  if (method === 'notifications/cancelled') {
    // Cancellation notification — no response
    return;
  }

  // ── Forward tools/list and tools/call to Unity ──

  if (method === 'tools/list' || method === 'tools/call') {
    forwardToUnity(request, id);
    return;
  }

  // ── Unknown method ──

  if (id !== undefined && id !== null) {
    sendResponse({
      jsonrpc: '2.0',
      id: id,
      error: { code: -32601, message: `Method not found: ${method}` }
    });
  }
}

// ── Forward to Unity HTTP Server ────────────────────────────────────

function forwardToUnity(request, requestId) {
  const body = JSON.stringify(request);

  const options = {
    hostname: UNITY_HOST,
    port: UNITY_PORT,
    path: UNITY_PATH,
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Content-Length': Buffer.byteLength(body)
    },
    timeout: 30000
  };

  const req = http.request(options, (res) => {
    let data = '';
    res.on('data', (chunk) => { data += chunk; });
    res.on('end', () => {
      // Parse Unity's response and ensure the id matches the request
      try {
        const parsed = JSON.parse(data);
        // Force the correct id from our request (Unity might return it wrong)
        parsed.id = requestId;
        sendResponse(parsed);
      } catch (e) {
        // If Unity returned invalid JSON, wrap it
        sendResponse({
          jsonrpc: '2.0',
          id: requestId,
          error: {
            code: -32000,
            message: 'Invalid response from Unity server',
            data: data.substring(0, 500)
          }
        });
      }
    });
  });

  req.on('error', (err) => {
    sendResponse({
      jsonrpc: '2.0',
      id: requestId,
      error: {
        code: -32000,
        message: `Cannot connect to Unity MCP server at localhost:${UNITY_PORT}. Make sure Unity is open and the MCP plugin is running.`,
        data: err.message
      }
    });
  });

  req.on('timeout', () => {
    req.destroy();
    sendResponse({
      jsonrpc: '2.0',
      id: requestId,
      error: {
        code: -32000,
        message: 'Unity MCP server request timed out after 30s'
      }
    });
  });

  req.write(body);
  req.end();
}

// ── Send Response via stdout ────────────────────────────────────────

function sendResponse(responseObj) {
  // Only send responses for requests (those with an id)
  // Never send responses for notifications
  if (responseObj.id === undefined) return;

  const json = JSON.stringify(responseObj);
  process.stdout.write(json + '\n');
}
