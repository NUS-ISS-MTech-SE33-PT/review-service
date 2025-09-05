var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", (string message) => $"Hello from review service! you get '{message}'");
app.MapPost("/", (string message) => $"you post '{message}'");
app.MapPut("/", (string message) => $"you put '{message}'");
app.MapDelete("/", (string message) => $"you delete '{message}'");

app.Run();
