var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDistributedMemoryCache();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var app = builder.Build();
app.MapControllers();
app.Run();
