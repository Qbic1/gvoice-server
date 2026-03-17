using GVoice.API.Hubs;
using GVoice.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<XmlChatHistoryService>();

// Configure CORS

var allowedOrigins = builder.Configuration
    .GetRequiredSection("Cors:AllowedOrigins")
    .Get<string[]>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(allowedOrigins!)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
    });
}

app.UseCors("AllowAngular");

app.MapGet("/rooms", () => SignalingHub.GetRooms());

app.MapPost("/admin/verify", (AdminVerifyRequest request, IConfiguration config) =>
{
    var adminPassword = config["AdminPassword"];
    return request.Password == adminPassword ? Results.Ok() : Results.Unauthorized();
});

app.MapHub<SignalingHub>("/hub/signaling");

app.Run();

public record AdminVerifyRequest(string Password);
