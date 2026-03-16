using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
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

                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch
                {
                    // Tab may already exist
                }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Tools");

                PushButtonData buttonData = new PushButtonData(
                    "HelloWorld",
                    "Hello\nWorld",
                    System.Reflection.Assembly.GetExecutingAssembly().Location,
                    "RevitMcpPlugin.HelloWorldCommand"
                );

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

        private static string? _authToken; // set in OnStartup; null means plugin failed to load

        // Returns the token string, or null if config is missing, unreadable, or uses the
        // example/default value. Caller must refuse to start if null is returned.
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
                    catch { /* fall through */ }
                    return null; // file found but token invalid — do not fall through
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null; // no config.json found
        }
        private const long MAX_REQUEST_SIZE = 1024 * 1024; // 1MB limit

        private const int MaxQueueDepth        = 10;
        private const int MaxConcurrentRequests = 3;
        private static int _activeRequests      = 0;

        private void StartHttpServer()
        {
            _isRunning = true;
            _httpListener = new HttpListener();
            // Bind strictly to 127.0.0.1 for local-only development
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

                // 1. Concurrency guard — increment first, then validate
                int active = Interlocked.Increment(ref _activeRequests);
                if (active > MaxConcurrentRequests || App.CommandQueue.Count >= MaxQueueDepth)
                {
                    // decrement happens in finally
                    context.Response.StatusCode = 429;
                    await WriteResponse(context, "{\"error\":\"Too many requests\"}");
                    return;
                }

                // 2. Check Auth Token
                string? token = context.Request.Headers["X-Revit-MCP-Token"];
                if (token != _authToken)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    await WriteResponse(context, "{\"error\":\"Unauthorized: Invalid or missing token\"}");
                    return;
                }

                // 2. Check Request Size
                if (context.Request.ContentLength64 > MAX_REQUEST_SIZE)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                    await WriteResponse(context, "{\"error\":\"Request too large\"}");
                    return;
                }

                using var reader = new StreamReader(
                    context.Request.InputStream,
                    context.Request.ContentEncoding
                );

                string commandJson = await reader.ReadToEndAsync();
                
                // 3. Simple Validation (More robust JSON parsing recommended)
                if (string.IsNullOrEmpty(commandJson) || !commandJson.Contains("\"command\""))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteResponse(context, "{\"error\":\"Invalid JSON: 'command' field required\"}");
                    return;
                }

                string requestId = Guid.NewGuid().ToString();
                CommandQueue.Enqueue((requestId, commandJson));
                _externalEvent?.Raise();

                int timeout = 300; // 30 seconds (300 * 100ms) — allows time for large-model checks
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

        private void LogRequest(HttpListenerRequest request)
        {
            // Simple logging to Debug output (viewable in DebugView or VS)
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] {request.HttpMethod} {request.Url} from {request.RemoteEndPoint}");
        }

        private void LogError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] ERROR: {message}");
        }
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
            "get_project_info",
            "validate_parameters",
            "apply_parameter_rules",
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
                        App.CommandResults.TryAdd(queued.RequestId, $"{{\"error\":\"Unknown command: {cmdName}\"}}");
                        continue;
                    }

                    if (doc == null)
                    {
                        App.CommandResults.TryAdd(queued.RequestId, "{\"error\":\"No active Revit document open\"}");
                        continue;
                    }

                    root.TryGetProperty("args", out var args); // default JsonElement if absent

                    result = cmdName switch
                    {
                        "get_project_info"      => GetProjectInfo(doc),
                        "validate_parameters"   => ParameterEngine.ValidateParameters(doc, args),
                        "apply_parameter_rules" => ParameterEngine.ApplyParameterRules(doc, args),
                        _                       => "{\"error\":\"Unhandled command\"}",
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
    }
}