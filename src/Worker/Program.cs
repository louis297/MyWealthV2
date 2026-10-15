using Hangfire;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructureServices();
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["reports"];
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}

app.MapDefaultEndpoints();

app.Run();
