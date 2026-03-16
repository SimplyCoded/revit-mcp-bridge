import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import axios from "axios";
import { readFileSync } from "fs";
import { resolve } from "path";

function loadConfig(): Record<string, string> {
  try {
    const configPath = resolve(__dirname, "../../config.json");
    return JSON.parse(readFileSync(configPath, "utf-8"));
  } catch {
    return {};
  }
}

const config = loadConfig();

// 1. Initialize the MCP Server
const server = new Server(
  {
    name: "revit-mcp-bridge",
    version: "1.0.0",
  },
  {
    capabilities: {
      tools: {},
    },
  }
);

const REVIT_PLUGIN_URL = config.revitPluginUrl ?? "http://127.0.0.1:8080/revit/";
const AUTH_TOKEN = config.authToken ?? "revit-mcp-secret-2025";

const VALID_CATEGORIES = [
  "OST_StructuralFraming",
  "OST_StructuralColumns",
  "OST_StructuralFoundation",
  "OST_Walls",
  "OST_Floors",
] as const;

// 2. Define Available Tools
server.setRequestHandler(ListToolsRequestSchema, async () => {
  return {
    tools: [
      {
        name: "get_revit_project_info",
        description: "Get basic information about the currently open Revit project (e.g., project name).",
        inputSchema: {
          type: "object",
          properties: {},
        },
      },
      {
        name: "validate_parameters",
        description:
          "Validate required parameters on structural Revit elements. " +
          "Returns a health report listing which elements have missing or empty parameters. " +
          "Always checks the core built-in schema (Mark, Comments, Structural Usage where applicable). " +
          "Pass additional_params to also check project-specific or client-specific shared parameters (Psets).",
        inputSchema: {
          type: "object",
          required: ["category"],
          properties: {
            category: {
              type: "string",
              enum: VALID_CATEGORIES,
              description: "Revit element category to validate.",
            },
            additional_params: {
              type: "array",
              items: { type: "string" },
              description:
                "Optional list of custom or shared parameter names to also check " +
                "(e.g. ['SBB_AssetID', 'SBB_MaintenanceZone']). " +
                "Use the exact parameter name as it appears in Revit.",
            },
          },
        },
      },
      {
        name: "apply_parameter_rules",
        description:
          "Apply rule-based logic to derive and fill parameter values on structural elements. " +
          "'suggest' returns proposed changes without writing to the model. " +
          "'auto-fill' writes the changes directly to the Revit document. " +
          "Supported rule types: material_summary, wall_function_flag, if_then.",
        inputSchema: {
          type: "object",
          required: ["category", "action", "rules"],
          properties: {
            category: {
              type: "string",
              enum: VALID_CATEGORIES,
            },
            action: {
              type: "string",
              enum: ["suggest", "auto-fill"],
              description:
                "'suggest' — returns proposed changes, nothing written. " +
                "'auto-fill' — writes changes to the Revit document immediately.",
            },
            allowed_write_params: {
              type: "array",
              items: { type: "string" },
              description:
                "Required when action is 'auto-fill'. Explicit list of parameter names that " +
                "rules are permitted to write (e.g. ['Material_Summary', 'IsExterior']). " +
                "Any rule targeting a parameter not in this list will be skipped.",
            },
            rules: {
              type: "array",
              description: "List of rules to apply. Each rule must have a 'type' property.",
              items: {
                type: "object",
                required: ["type"],
                properties: {
                  type: {
                    type: "string",
                    enum: ["material_summary", "wall_function_flag", "if_then"],
                    description:
                      "material_summary: collects material names from the element and writes them " +
                        "to 'target_param' (default: 'Material_Summary'). " +
                      "wall_function_flag: sets 'target_param' (default: 'IsExterior') to " +
                        "'exterior_value'/'interior_value' based on the wall type function. " +
                      "if_then: if 'if_param' equals 'if_value' (case-insensitive), sets " +
                        "'set_param' to 'set_value'.",
                  },
                  target_param:   { type: "string", description: "Target parameter name (material_summary, wall_function_flag)." },
                  exterior_value: { type: "string", description: "Value to set when wall is Exterior (wall_function_flag)." },
                  interior_value: { type: "string", description: "Value to set when wall is not Exterior (wall_function_flag)." },
                  if_param:  { type: "string", description: "Condition parameter name or BuiltInParameter enum name (if_then)." },
                  if_value:  { type: "string", description: "Expected value to match (if_then)." },
                  set_param: { type: "string", description: "Parameter to set when condition matches (if_then)." },
                  set_value: { type: "string", description: "Value to write when condition matches (if_then)." },
                },
              },
            },
          },
        },
      },
    ],
  };
});

// 3. Handle Tool Execution
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  // Helper: POST a command to the Revit plugin and return the MCP response
  async function callRevit(command: string, revitArgs: object, timeoutMs = 15000) {
    try {
      const response = await axios.post(
        REVIT_PLUGIN_URL,
        { command, args: revitArgs },
        {
          headers: {
            "X-Revit-MCP-Token": AUTH_TOKEN,
            "Content-Type": "application/json",
          },
          timeout: timeoutMs,
        }
      );
      return {
        content: [{ type: "text" as const, text: JSON.stringify(response.data, null, 2) }],
      };
    } catch (error: any) {
      return {
        content: [{ type: "text" as const, text: `Error communicating with Revit: ${error.message}` }],
        isError: true,
      };
    }
  }

  if (name === "get_revit_project_info") {
    return callRevit("get_project_info", {});
  }

  if (name === "validate_parameters") {
    const { category, additional_params } = args as {
      category: string;
      additional_params?: string[];
    };
    return callRevit(
      "validate_parameters",
      { category, ...(additional_params ? { additional_params } : {}) },
      60000  // 60s — large models may have thousands of elements
    );
  }

  if (name === "apply_parameter_rules") {
    const { category, action, rules, allowed_write_params } = args as {
      category: string;
      action: "suggest" | "auto-fill";
      rules: object[];
      allowed_write_params?: string[];
    };
    return callRevit(
      "apply_parameter_rules",
      { category, action, rules, ...(allowed_write_params ? { allowed_write_params } : {}) },
      60000  // 60s — auto-fill writes a transaction, give it room
    );
  }

  throw new Error(`Tool not found: ${name}`);
});

// 4. Start the Server (using stdio transport)
async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("Revit MCP Bridge Server running on stdio");
}

main().catch((error) => {
  console.error("Fatal error in main():", error);
  process.exit(1);
});
