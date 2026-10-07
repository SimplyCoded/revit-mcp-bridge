using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using RevitMcpPlugin.Commands;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitMcpPlugin
{
    public class App : IExternalApplication
    {
        private static HttpListener? _httpListener;
        private static ExternalEvent? _externalEvent;
        private static McpCommandHandler? _handler;
        private static bool _isRunning = false;

        public static ConcurrentQueue<(string RequestId, string CommandJson)> CommandQueue = new();
        public static ConcurrentDictionary<string, string> CommandResults = new();

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string tabName = "MCP Bridge";
                try { application.CreateRibbonTab(tabName); } catch { }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Tools");
                PushButtonData buttonData = new PushButtonData(
                    "HelloWorld", "Hello\nWorld",
                    System.Reflection.Assembly.GetExecutingAssembly().Location,
                    "RevitMcpPlugin.HelloWorldCommand");
                panel.AddItem(buttonData);

                _authToken = LoadAuthToken();
                if (_authToken == null)
                {
                    RevitTaskDialog.Show("MCP Bridge — Startup Error",
                        "Plugin not started: config.json was not found, could not be read, " +
                        "or authToken is still set to the default/example value.\n\n" +
                        "Create config.json with a strong unique secret and restart Revit.");
                    return Result.Failed;
                }

                _handler = new McpCommandHandler();
                _externalEvent = ExternalEvent.Create(_handler);
                StartHttpServer();

                RevitTaskDialog.Show("MCP Bridge", "Revit MCP Bridge Plugin Loaded Successfully!");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                RevitTaskDialog.Show("MCP Bridge Error", "Failed to load plugin: " + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _isRunning = false;
            _httpListener?.Stop();
            _httpListener?.Close();
            return Result.Succeeded;
        }

        private static string? _authToken;

        private static string? LoadAuthToken()
        {
            const string ExampleToken = "change-me-to-a-strong-random-secret";
            const string OldDefault   = "revit-mcp-secret-2025";

            string? dir = Path.GetDirectoryName(typeof(App).Assembly.Location);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "config.json");
                if (File.Exists(candidate))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                        if (doc.RootElement.TryGetProperty("authToken", out var t) &&
                            t.GetString() is string token &&
                            !string.IsNullOrWhiteSpace(token) &&
                            token != ExampleToken &&
                            token != OldDefault)
                            return token;
                    }
                    catch { }
                    return null;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private const long MAX_REQUEST_SIZE      = 1024 * 1024;
        private const int  MaxQueueDepth         = 10;
        private const int  MaxConcurrentRequests = 3;
        private static int _activeRequests       = 0;

        private void StartHttpServer()
        {
            _isRunning = true;
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://127.0.0.1:8080/revit/");
            _httpListener.Start();

            Task.Run(async () =>
            {
                while (_isRunning)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _ = ProcessRequest(context);
                    }
                    catch (Exception ex)
                    {
                        if (_isRunning) LogError("HTTP Listener Error: " + ex.Message);
                    }
                }
            });
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            try
            {
                LogRequest(context.Request);

                if (context.Request.HttpMethod != "POST")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    return;
                }

                int active = Interlocked.Increment(ref _activeRequests);
                if (active > MaxConcurrentRequests || App.CommandQueue.Count >= MaxQueueDepth)
                {
                    context.Response.StatusCode = 429;
                    await WriteResponse(context, "{\"error\":\"Too many requests\"}");
                    return;
                }

                string? token = context.Request.Headers["X-Revit-MCP-Token"];
                if (token != _authToken)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    await WriteResponse(context, "{\"error\":\"Unauthorized: Invalid or missing token\"}");
                    return;
                }

                if (context.Request.ContentLength64 > MAX_REQUEST_SIZE)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                    await WriteResponse(context, "{\"error\":\"Request too large\"}");
                    return;
                }

                using var reader = new StreamReader(
                    context.Request.InputStream, context.Request.ContentEncoding);
                string commandJson = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(commandJson) || !commandJson.Contains("\"command\""))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteResponse(context, "{\"error\":\"Invalid JSON: command field required\"}");
                    return;
                }

                string requestId = Guid.NewGuid().ToString();
                CommandQueue.Enqueue((requestId, commandJson));
                _externalEvent?.Raise();

                int timeout = 300;
                while (!CommandResults.ContainsKey(requestId) && timeout > 0)
                {
                    await Task.Delay(100);
                    timeout--;
                }

                if (CommandResults.TryRemove(requestId, out var result) && result is not null)
                {
                    await WriteResponse(context, result);
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    await WriteResponse(context, "{\"error\":\"Timed out waiting for Revit response\"}");
                }
            }
            catch (Exception ex)
            {
                LogError("ProcessRequest error: " + ex.ToString());
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteResponse(context, "{\"error\":\"Internal server error\"}");
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
                context.Response.Close();
            }
        }

        private async Task WriteResponse(HttpListenerContext context, string content)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private void LogRequest(HttpListenerRequest request) =>
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] {request.HttpMethod} {request.Url} from {request.RemoteEndPoint}");

        private void LogError(string message) =>
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] ERROR: {message}");
    }

    [Transaction(TransactionMode.Manual)]
    public class HelloWorldCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            RevitTaskDialog.Show("Hello World", "Revit MCP Bridge is active and listening on 127.0.0.1:8080");
            return Result.Succeeded;
        }
    }

    public class McpCommandHandler : IExternalEventHandler
    {
        private static readonly HashSet<string> AllowedCommands = new()
        {
            // Core
            "get_project_info",
            "validate_parameters",
            "apply_parameter_rules",
            "say_hello",
            // Sheets
            "get_sheets",
            "get_title_blocks",
            "create_sheet",
            "duplicate_sheet",
            // Phase 1 — model query
            "get_current_view_info",
            "get_current_view_elements",
            "get_selected_elements",
            "get_available_family_types",
            "analyze_model_statistics",
            "get_material_quantities",
            "ai_element_filter",
            // Phase 2 — creation
            "create_level",
            "create_grid",
            "create_line_based_element",
            "create_point_based_element",
            "create_room",
            "create_surface_based_element",
            "create_structural_framing_system",
            // Phase 2 — modification
            "delete_element",
            "operate_element",
            "color_elements",
            "create_dimensions",
            "tag_all_walls",
            "tag_all_rooms",
        };

        public void Execute(UIApplication app)
        {
            Document? doc = app.ActiveUIDocument?.Document;

            while (App.CommandQueue.TryDequeue(out var queued))
            {
                string result;
                try
                {
                    using var jsonDoc = JsonDocument.Parse(queued.CommandJson);
                    var root = jsonDoc.RootElement;

                    if (!root.TryGetProperty("command", out var cmdElem))
                    {
                        App.CommandResults.TryAdd(queued.RequestId, "{\"error\":\"No command specified\"}");
                        continue;
                    }

                    string cmdName = cmdElem.GetString() ?? "";

                    if (!AllowedCommands.Contains(cmdName))
                    {
                        App.CommandResults.TryAdd(queued.RequestId,
                            $"{{\"error\":\"Unknown command: {cmdName}\"}}");
                        continue;
                    }

                    if (doc == null)
                    {
                        App.CommandResults.TryAdd(queued.RequestId,
                            "{\"error\":\"No active Revit document open\"}");
                        continue;
                    }

                    root.TryGetProperty("args", out var args);

                    result = cmdName switch
                    {
                        // Core
                        "get_project_info"      => GetProjectInfo(doc),
                        "validate_parameters"   => ParameterEngine.ValidateParameters(doc, args),
                        "apply_parameter_rules" => ParameterEngine.ApplyParameterRules(doc, args),
                        "say_hello"             => HandleSayHello(app, doc),
                        // Sheets
                        "get_sheets"      => GetSheets(doc),
                        "get_title_blocks"=> GetTitleBlocks(doc),
                        "create_sheet"    => CreateSheet(doc, args),
                        "duplicate_sheet" => DuplicateSheet(doc, args),
                        // Phase 1 — model query
                        "get_current_view_info"      => ModelInfoCommands.GetCurrentViewInfo(app, doc),
                        "get_current_view_elements"  => ModelInfoCommands.GetCurrentViewElements(app, doc, args),
                        "get_selected_elements"      => ModelInfoCommands.GetSelectedElements(app, doc),
                        "get_available_family_types" => ModelInfoCommands.GetAvailableFamilyTypes(doc, args),
                        "analyze_model_statistics"   => ModelInfoCommands.AnalyzeModelStatistics(doc),
                        "get_material_quantities"    => ModelInfoCommands.GetMaterialQuantities(doc, args),
                        "ai_element_filter"          => ModelInfoCommands.AiElementFilter(doc, args),
                        // Phase 2 — creation
                        "create_level"                      => ElementCommands.CreateLevel(doc, args),
                        "create_grid"                       => ElementCommands.CreateGrid(doc, args),
                        "create_line_based_element"         => ElementCommands.CreateLineBasedElement(doc, args),
                        "create_point_based_element"        => ElementCommands.CreatePointBasedElement(doc, args),
                        "create_room"                       => ElementCommands.CreateRoom(doc, args),
                        "create_surface_based_element"      => ElementCommands.CreateSurfaceBasedElement(doc, args),
                        "create_structural_framing_system"  => ElementCommands.CreateStructuralFramingSystem(doc, args),
                        // Phase 2 — modification
                        "delete_element"    => ElementCommands.DeleteElement(doc, args),
                        "operate_element"   => ElementCommands.OperateElement(doc, args),
                        "color_elements"    => ElementCommands.ColorElements(app, doc, args),
                        "create_dimensions" => ElementCommands.CreateDimensions(app, doc, args),
                        "tag_all_walls"     => ElementCommands.TagAllWalls(app, doc, args),
                        "tag_all_rooms"     => ElementCommands.TagAllRooms(app, doc, args),
                        _                   => "{\"error\":\"Unhandled command\"}",
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCP Handler] {ex}");
                    result = "{\"error\":\"Command execution failed\"}";
                }

                App.CommandResults.TryAdd(queued.RequestId, result);
            }
        }

        public string GetName() => "MCP Bridge Command Handler";

        private static string GetProjectInfo(Document doc)
        {
            string name = doc.ProjectInformation?.Name ?? "Unnamed Project";
            string escaped = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"{{\"projectName\":\"{escaped}\",\"status\":\"Success\"}}";
        }

        private static string HandleSayHello(UIApplication app, Document doc)
        {
            RevitTaskDialog.Show("MCP Bridge", "Hello from Claude!");
            string version = app.Application.VersionNumber;
            string title = doc.Title ?? "No Document";
            title = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"{{\"status\":\"ok\",\"revit_version\":\"{version}\",\"project_title\":\"{title}\"}}";
        }

        private static string GetSheets(Document doc)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s =>
                {
                    var tb = new FilteredElementCollector(doc, s.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .OfClass(typeof(FamilyInstance))
                        .FirstOrDefault();
                    return new
                    {
                        id            = s.Id.Value,
                        sheetNumber   = s.SheetNumber,
                        sheetName     = s.Name,
                        titleBlockId  = tb?.Id.Value ?? -1,
                        viewportCount = s.GetAllViewports().Count,
                    };
                })
                .ToList();
            return JsonSerializer.Serialize(new { sheets });
        }

        private static string GetTitleBlocks(Document doc)
        {
            var titleBlockTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .Select(t => new
                {
                    id         = t.Id.Value,
                    familyName = t.FamilyName,
                    typeName   = t.Name,
                })
                .ToList();
            return JsonSerializer.Serialize(new { titleBlockTypes });
        }

        private static string CreateSheet(Document doc, JsonElement args)
        {
            string sheetNumber = args.TryGetProperty("sheetNumber", out var sn) ? sn.GetString() ?? "" : "";
            string sheetName   = args.TryGetProperty("sheetName",   out var nm) ? nm.GetString() ?? "" : "";
            var titleBlockTypeId = args.TryGetProperty("titleBlockTypeId", out var tbId)
                ? new ElementId((long)tbId.GetInt32()) : ElementId.InvalidElementId;

            using var tx = new Transaction(doc, "MCP: Create Sheet");
            tx.Start();
            var sheet = ViewSheet.Create(doc, titleBlockTypeId);
            sheet.SheetNumber = sheetNumber;
            sheet.Name        = sheetName;
            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status      = "Success",
                id          = sheet.Id.Value,
                sheetNumber = sheet.SheetNumber,
                sheetName   = sheet.Name,
            });
        }

        private static string DuplicateSheet(Document doc, JsonElement args)
        {
            int sourceSheetId     = args.TryGetProperty("sourceSheetId",   out var sid) ? sid.GetInt32()    : -1;
            string newSheetNumber = args.TryGetProperty("newSheetNumber",  out var nsn) ? nsn.GetString() ?? "" : "";
            string newSheetName   = args.TryGetProperty("newSheetName",    out var nn)  ? nn.GetString()  ?? "" : "";

            if (sourceSheetId < 0) return "{\"error\":\"sourceSheetId is required\"}";

            if (doc.GetElement(new ElementId((long)sourceSheetId)) is not ViewSheet sourceSheet)
                return "{\"error\":\"Source sheet not found\"}";

            var tbInstance = new FilteredElementCollector(doc, sourceSheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .FirstOrDefault();
            var titleBlockTypeId = tbInstance?.GetTypeId() ?? ElementId.InvalidElementId;

            int viewportsCopied  = 0;
            int viewportsSkipped = 0;
            var skippedViews     = new List<string>();

            using var tx = new Transaction(doc, "MCP: Duplicate Sheet");
            tx.Start();

            var newSheet = ViewSheet.Create(doc, titleBlockTypeId);
            newSheet.SheetNumber = newSheetNumber;
            newSheet.Name        = newSheetName;

            foreach (var vpId in sourceSheet.GetAllViewports())
            {
                if (doc.GetElement(vpId) is not Viewport vp) continue;
                if (doc.GetElement(vp.ViewId) is not Autodesk.Revit.DB.View view) continue;

                if (!view.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
                {
                    skippedViews.Add(view.Name);
                    viewportsSkipped++;
                    continue;
                }

                try
                {
                    var dupId = view.Duplicate(ViewDuplicateOption.Duplicate);
                    Viewport.Create(doc, newSheet.Id, dupId, vp.GetBoxCenter());
                    viewportsCopied++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCP DuplicateSheet] viewport skip: {ex.Message}");
                    skippedViews.Add(view.Name);
                    viewportsSkipped++;
                }
            }

            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status          = "Success",
                id              = newSheet.Id.Value,
                sheetNumber     = newSheet.SheetNumber,
                sheetName       = newSheet.Name,
                viewportsCopied,
                viewportsSkipped,
                skippedViews,
            });
        }
    }
}
