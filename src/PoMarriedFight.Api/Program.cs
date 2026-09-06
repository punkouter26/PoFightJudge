// T01 stub: the host grows feature by feature (T03+). Kept minimal so the solution compiles end to end.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

/// <summary>Exposed for WebApplicationFactory in tests.</summary>
public partial class Program;
