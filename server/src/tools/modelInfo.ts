import type { Tool, CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { postToRevit } from "../client/revitClient.js";

export const tools: Tool[] = [
  {
    name: "get_current_view_info",
    description:
      "Get information about the currently active Revit view: name, type (FloorPlan, Section, 3D, etc.), scale, detail level, and associated level if applicable.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "get_current_view_elements",
    description:
      "Get elements visible in the currently active Revit view. " +
      "Optionally filter by category. Returns up to maxElements elements (default 500).",
    inputSchema: {
      type: "object",
      properties: {
        category: {
          type: "string",
          description: "Optional category name to filter, e.g. 'Walls', 'Doors', 'Structural Framing'.",
        },
        maxElements: {
          type: "integer",
          description: "Maximum elements to return (default 500).",
        },
      },
    },
  },
  {
    name: "get_selected_elements",
    description: "Get the elements currently selected in Revit. Returns ID, name, category, family, and type for each.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "get_available_family_types",
    description:
      "List all family types (FamilySymbol) loaded in the project. " +
      "Optionally filter by category. Use the returned IDs with create_* tools.",
    inputSchema: {
      type: "object",
      properties: {
        category: {
          type: "string",
          description: "Optional category filter, e.g. 'Structural Framing', 'Doors', 'Windows'.",
        },
      },
    },
  },
  {
    name: "analyze_model_statistics",
    description:
      "Analyze model complexity: total element count, family/type counts, view and sheet counts, level list, and the top 20 categories by element count.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "get_material_quantities",
    description:
      "Get material volume and area quantities for elements in a given category. " +
      "category is required to keep the query scoped and fast.",
    inputSchema: {
      type: "object",
      required: ["category"],
      properties: {
        category: {
          type: "string",
          description: "Category to query, e.g. 'Walls', 'Floors', 'Structural Framing'.",
        },
      },
    },
  },
  {
    name: "ai_element_filter",
    description:
      "Filter elements in the model by category and/or a parameter value match. " +
      "The AI (Claude) constructs the filter criteria; this tool executes them against the model. " +
      "Returns matching element IDs, names, categories, and type names.",
    inputSchema: {
      type: "object",
      properties: {
        category: {
          type: "string",
          description: "Category name to filter by, e.g. 'Walls'.",
        },
        paramName: {
          type: "string",
          description: "Parameter name to match against (exact name as it appears in Revit).",
        },
        paramValue: {
          type: "string",
          description: "Value to match (case-insensitive substring match).",
        },
        maxResults: {
          type: "integer",
          description: "Maximum results to return (default 200).",
        },
      },
    },
  },
];

export async function handleCall(
  name: string,
  args: Record<string, unknown>
): Promise<CallToolResult | undefined> {
  if (name === "get_current_view_info") return postToRevit("get_current_view_info", {});
  if (name === "get_selected_elements") return postToRevit("get_selected_elements", {});
  if (name === "analyze_model_statistics") return postToRevit("analyze_model_statistics", {});

  if (name === "get_current_view_elements") {
    const { category, maxElements } = args as { category?: string; maxElements?: number };
    return postToRevit("get_current_view_elements", {
      ...(category    !== undefined ? { category }    : {}),
      ...(maxElements !== undefined ? { maxElements } : {}),
    }, 30000);
  }

  if (name === "get_available_family_types") {
    const { category } = args as { category?: string };
    return postToRevit("get_available_family_types", {
      ...(category !== undefined ? { category } : {}),
    });
  }

  if (name === "get_material_quantities") {
    const { category } = args as { category: string };
    return postToRevit("get_material_quantities", { category }, 60000);
  }

  if (name === "ai_element_filter") {
    const { category, paramName, paramValue, maxResults } = args as {
      category?: string;
      paramName?: string;
      paramValue?: string;
      maxResults?: number;
    };
    return postToRevit("ai_element_filter", {
      ...(category   !== undefined ? { category }   : {}),
      ...(paramName  !== undefined ? { paramName }  : {}),
      ...(paramValue !== undefined ? { paramValue } : {}),
      ...(maxResults !== undefined ? { maxResults } : {}),
    }, 30000);
  }

  return undefined;
}
