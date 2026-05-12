#!/usr/bin/env node
// Aggregator: multiplexes N Rhino MCP HTTP servers behind a single stdio MCP
// server. Each upstream's tools/prompts/resources are surfaced with the
// instance name as a prefix (e.g. A__RunCommand). This lets parallel agents
// each operate their own Rhino without shared state.

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import { SSEClientTransport } from "@modelcontextprotocol/sdk/client/sse.js";
import {
  ListToolsRequestSchema,
  CallToolRequestSchema,
  ListPromptsRequestSchema,
  GetPromptRequestSchema,
  ListResourcesRequestSchema,
  ListResourceTemplatesRequestSchema,
  ReadResourceRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

const SEPARATOR = "__";
const INSTANCE_NAME_RE = /^[A-Za-z0-9_-]{1,8}$/;
const RECONNECT_INTERVAL_MS = 5000;

function parseInstances(spec) {
  const entries = spec.split(",").map(s => s.trim()).filter(Boolean);
  if (entries.length === 0) throw new Error("No instances configured");
  const result = [];
  for (const entry of entries) {
    const eq = entry.indexOf("=");
    let name, url;
    if (eq === -1) {
      // Bare URL is only allowed when there's a single entry.
      if (entries.length !== 1) {
        throw new Error(`Instance "${entry}" must be in name=url form when configuring multiple instances`);
      }
      name = "Rhino";
      url = entry;
    } else {
      name = entry.slice(0, eq).trim();
      url = entry.slice(eq + 1).trim();
    }
    if (!INSTANCE_NAME_RE.test(name)) {
      throw new Error(`Invalid instance name "${name}" — must match ${INSTANCE_NAME_RE}`);
    }
    result.push({ name, url });
  }
  const seen = new Set();
  for (const { name } of result) {
    if (seen.has(name)) throw new Error(`Duplicate instance name: ${name}`);
    seen.add(name);
  }
  return result;
}

async function openClient(url) {
  const client = new Client(
    { name: "rhino-mcp-aggregator", version: "0.2.0" },
    { capabilities: {} }
  );
  try {
    await client.connect(new StreamableHTTPClientTransport(new URL(url)));
    return client;
  } catch {
    await client.connect(new SSEClientTransport(new URL(url)));
    return client;
  }
}

class Instance {
  constructor({ name, url }) {
    this.name = name;
    this.url = url;
    this.client = null;
    this.lastError = null;
    this.reconnectTimer = null;
  }

  async connect() {
    try {
      this.client = await openClient(this.url);
      this.lastError = null;
      this.client.onclose = () => this.handleDisconnect();
      this.client.onerror = (err) => { this.lastError = err?.message ?? String(err); };
    } catch (err) {
      this.client = null;
      this.lastError = err?.message ?? String(err);
      this.scheduleReconnect();
    }
  }

  handleDisconnect() {
    this.client = null;
    this.scheduleReconnect();
  }

  scheduleReconnect() {
    if (this.reconnectTimer) return;
    this.reconnectTimer = setTimeout(async () => {
      this.reconnectTimer = null;
      await this.connect();
    }, RECONNECT_INTERVAL_MS);
  }

  get up() { return this.client !== null; }
}

function splitPrefix(name) {
  const idx = name.indexOf(SEPARATOR);
  if (idx === -1) return null;
  return { prefix: name.slice(0, idx), rest: name.slice(idx + SEPARATOR.length) };
}

async function main() {
  const spec = process.argv[2];
  if (!spec) {
    console.error("rhino-mcp-platform: missing instances argument");
    process.exit(1);
  }

  const instances = new Map();
  for (const cfg of parseInstances(spec)) {
    instances.set(cfg.name, new Instance(cfg));
  }
  await Promise.all([...instances.values()].map(i => i.connect()));

  const server = new Server(
    { name: "rhino-mcp-platform", version: "0.2.0" },
    { capabilities: { tools: {}, prompts: {}, resources: {} } }
  );

  const route = (name) => {
    const split = splitPrefix(name);
    if (!split) throw new Error(`Tool/prompt/resource name "${name}" is missing an instance prefix (expected <name>${SEPARATOR}<...>)`);
    const inst = instances.get(split.prefix);
    if (!inst) throw new Error(`Unknown instance "${split.prefix}". Known: ${[...instances.keys()].join(", ")}`);
    if (!inst.up) throw new Error(`Instance "${split.prefix}" is unreachable: ${inst.lastError ?? "unknown error"}`);
    return { inst, name: split.rest };
  };

  server.setRequestHandler(ListToolsRequestSchema, async () => {
    const tools = [];
    for (const inst of instances.values()) {
      if (!inst.up) continue;
      try {
        const res = await inst.client.listTools();
        for (const t of res.tools) {
          tools.push({
            ...t,
            name: `${inst.name}${SEPARATOR}${t.name}`,
            description: `[${inst.name}] ${t.description ?? ""}`.trim(),
          });
        }
      } catch (err) {
        inst.lastError = err?.message ?? String(err);
      }
    }
    return { tools };
  });

  server.setRequestHandler(CallToolRequestSchema, async (req) => {
    const { inst, name } = route(req.params.name);
    return await inst.client.callTool({ name, arguments: req.params.arguments });
  });

  server.setRequestHandler(ListPromptsRequestSchema, async () => {
    const prompts = [];
    for (const inst of instances.values()) {
      if (!inst.up) continue;
      try {
        const res = await inst.client.listPrompts();
        for (const p of res.prompts) {
          prompts.push({
            ...p,
            name: `${inst.name}${SEPARATOR}${p.name}`,
            description: `[${inst.name}] ${p.description ?? ""}`.trim(),
          });
        }
      } catch (err) {
        inst.lastError = err?.message ?? String(err);
      }
    }
    return { prompts };
  });

  server.setRequestHandler(GetPromptRequestSchema, async (req) => {
    const { inst, name } = route(req.params.name);
    return await inst.client.getPrompt({ name, arguments: req.params.arguments });
  });

  server.setRequestHandler(ListResourcesRequestSchema, async () => {
    const resources = [];
    for (const inst of instances.values()) {
      if (!inst.up) continue;
      try {
        const res = await inst.client.listResources();
        for (const r of res.resources) {
          resources.push({
            ...r,
            uri: `rhino://${inst.name}/${encodeURIComponent(r.uri)}`,
            name: `${inst.name}${SEPARATOR}${r.name ?? r.uri}`,
          });
        }
      } catch (err) {
        inst.lastError = err?.message ?? String(err);
      }
    }
    return { resources };
  });

  server.setRequestHandler(ListResourceTemplatesRequestSchema, async () => {
    const resourceTemplates = [];
    for (const inst of instances.values()) {
      if (!inst.up) continue;
      try {
        const res = await inst.client.listResourceTemplates();
        for (const t of res.resourceTemplates ?? []) {
          resourceTemplates.push({
            ...t,
            uriTemplate: `rhino://${inst.name}/${t.uriTemplate}`,
            name: `${inst.name}${SEPARATOR}${t.name}`,
          });
        }
      } catch (err) {
        inst.lastError = err?.message ?? String(err);
      }
    }
    return { resourceTemplates };
  });

  server.setRequestHandler(ReadResourceRequestSchema, async (req) => {
    // URIs are rewritten as rhino://<instance>/<original-uri-encoded>
    const uri = req.params.uri;
    const match = /^rhino:\/\/([^/]+)\/(.+)$/.exec(uri);
    if (!match) throw new Error(`Resource URI "${uri}" is not in rhino://<instance>/<original> form`);
    const [, prefix, encoded] = match;
    const inst = instances.get(prefix);
    if (!inst) throw new Error(`Unknown instance "${prefix}"`);
    if (!inst.up) throw new Error(`Instance "${prefix}" is unreachable: ${inst.lastError ?? "unknown error"}`);
    return await inst.client.readResource({ uri: decodeURIComponent(encoded) });
  });

  await server.connect(new StdioServerTransport());
}

main().catch(err => {
  console.error("rhino-mcp-platform aggregator failed:", err);
  process.exit(1);
});
