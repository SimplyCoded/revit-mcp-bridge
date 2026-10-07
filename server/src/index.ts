import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

import * as modelQuery from "./tools/modelQuery.js";
import * as parameters from "./tools/parameters.js";
import * as sheets     from "./tools/sheets.js";
import * as modelInfo  from "./tools/modelInfo.js";
import * as elements   from "./tools/elements.js";

const toolModules = [modelQuery, parameters, sheets, modelInfo, elements];

const server = new Server(
  { name: "revit-mcp-bridge", version: "1.0.0" },
  { capabilities: { tools: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: toolModules.flatMap((m) => m.tools),
}));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args = {} } = request.params;
  const typedArgs = args as Record<string, unknown>;

  for (const mod of toolModules) {
    const result = await mod.handleCall(name, typedArgs);
    if (result !== undefined) return result;
  }

  return {
    isError: true,
    content: [{ type: "text", text: `Unknown tool: ${name}` }],
  };
});

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((err) => {
  process.stderr.write(`Fatal: ${err}\n`);
  process.exit(1);
});
