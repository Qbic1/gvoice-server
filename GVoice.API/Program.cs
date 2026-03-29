using GVoice.API.Hubs;
using GVoice.API.Services;
using GVoice.API.Services.Implementations;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
});
builder.Services.AddSingleton<XmlChatHistoryService>();
builder.Services.AddSingleton<IRoomService, RoomService>();
builder.Services.AddSingleton<IParticipantService, ParticipantService>();

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

app.MapGet("/rooms", (IRoomService roomService) => roomService.Get().Select(r => new { r.Id, r.Name, ParticipantCount = r.Participants.Count }));

app.MapGet("/rooms/{roomId}/participants", (string roomId, IRoomService roomService) => Results.Ok(roomService.GetParticipants(roomId)));

app.MapPost("/admin/verify", (AdminVerifyRequest request, IConfiguration config) =>
{
    var adminPassword = config["AdminPassword"];
    return request.Password == adminPassword ? Results.Ok() : Results.Unauthorized();
});

app.MapHub<SignalingHub>("/hub/signaling");

app.Run();

public record AdminVerifyRequest(string Password);
