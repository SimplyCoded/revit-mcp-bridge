import type { Tool, CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { postToRevit } from "../client/revitClient.js";

const VALID_CATEGORIES = [
  "OST_StructuralFraming",
  "OST_StructuralColumns",
  "OST_StructuralFoundation",
  "OST_Walls",
  "OST_Floors",
] as const;

export const tools: Tool[] = [
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
          enum: [...VALID_CATEGORIES],
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
          enum: [...VALID_CATEGORIES],
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
                  "if_then: if 'if_param' matches 'if_value' via 'if_operator' (case-insensitive), sets " +
                    "'set_param' to 'set_value'. Use 'TypeName' as if_param to match the element type name.",
              },
              target_param:   { type: "string", description: "Target parameter name (material_summary, wall_function_flag)." },
              exterior_value: { type: "string", description: "Value to set when wall is Exterior (wall_function_flag)." },
              interior_value: { type: "string", description: "Value to set when wall is not Exterior (wall_function_flag)." },
              if_param:    { type: "string", description: "Condition parameter. Use 'TypeName' for element type name, a BuiltInParameter enum name, or a custom param name (if_then)." },
              if_operator: { type: "string", enum: ["equals", "starts_with", "ends_with", "contains"], description: "Comparison operator. Defaults to 'equals' (if_then)." },
              if_value:    { type: "string", description: "Value to match against (case-insensitive) (if_then)." },
              set_param: { type: "string", description: "Parameter to set when condition matches (if_then)." },
              set_value: { type: "string", description: "Value to write when condition matches (if_then)." },
            },
          },
        },
      },
    },
  },
];

export async function handleCall(
  name: string,
  args: Record<string, unknown>
): Promise<CallToolResult | undefined> {
  if (name === "validate_parameters") {
    const category = args.category as string;
    const additional_params = args.additional_params as string[] | undefined;
    return postToRevit(
      "validate_parameters",
      { category, ...(additional_params ? { additional_params } : {}) },
      60000
    );
  }

  if (name === "apply_parameter_rules") {
    const category = args.category as string;
    const action = args.action as "suggest" | "auto-fill";
    const rules = args.rules as Record<string, unknown>[];
    const allowed_write_params = args.allowed_write_params as string[] | undefined;
    return postToRevit(
      "apply_parameter_rules",
      { category, action, rules, ...(allowed_write_params ? { allowed_write_params } : {}) },
      60000
    );
  }

  return undefined;
}
