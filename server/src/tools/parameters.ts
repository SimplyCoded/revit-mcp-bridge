import type { Tool, CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { postToRevit } from "../client/revitClient.js";

const VALID_CATEGORIES = [
  "Structural Columns",
  "Structural Framing",
  "Structural Foundations",
  "Walls",
  "Floors",
  "Roofs",
  "Ceilings",
  "Doors",
  "Windows",
  "Stairs",
  "Railings",
  "Columns",
  "Beams",
  "Braces",
  "Slabs",
  "Footings",
  "Piles",
  "Rooms",
  "Areas",
  "Spaces",
  "MEP Fixtures",
  "Mechanical Equipment",
  "Electrical Equipment",
  "Plumbing Fixtures",
  "Pipe Fittings",
  "Duct Fittings",
  "Cable Tray Fittings",
  "Conduit Fittings",
  "Furniture",
  "Furniture Systems",
  "Specialty Equipment",
  "Casework",
  "Generic Models",
  "Mass",
  "Site",
  "Parking",
  "Entourage",
  "Planting",
  "Topography",
] as const;

export const tools: Tool[] = [
  {
    name: "validate_parameters",
    description:
      "Validate Revit parameter values for one or more elements against project rules. " +
      "Returns a list of validation results with pass/fail status and messages.",
    inputSchema: {
      type: "object",
      required: ["parameters"],
      properties: {
        parameters: {
          type: "array",
          description: "Array of parameter objects to validate.",
          items: {
            type: "object",
            required: ["elementId", "paramName", "value"],
            properties: {
              elementId: { type: "integer", description: "Revit element ID." },
              paramName: { type: "string",  description: "Parameter name (exact Revit name)." },
              value:     { type: "string",  description: "Value to validate." },
              category:  {
                type: "string",
                description: "Element category for rule matching.",
                enum: VALID_CATEGORIES,
              },
            },
          },
        },
      },
    },
  },
  {
    name: "apply_parameter_rules",
    description:
      "Apply rule-based auto-fill to Revit parameters. Reads project rules from ParameterEngine and " +
      "sets parameter values on matching elements. Only parameters in the allowed_write_params list are written.",
    inputSchema: {
      type: "object",
      required: ["category"],
      properties: {
        category: {
          type: "string",
          description: "Element category to apply rules to.",
          enum: VALID_CATEGORIES,
        },
        dryRun: {
          type: "boolean",
          description: "If true, returns what would be changed without writing anything (default false).",
        },
        elementIds: {
          type: "array",
          items: { type: "integer" },
          description: "Optional subset of element IDs to apply rules to. Applies to all if omitted.",
        },
      },
    },
  },
];

export async function handleCall(
  name: string,
  args: Record<string, unknown>
): Promise<CallToolResult | undefined> {
  if (name === "validate_parameters") return postToRevit("validate_parameters", args);
  if (name === "apply_parameter_rules") return postToRevit("apply_parameter_rules", args);
  return undefined;
}
