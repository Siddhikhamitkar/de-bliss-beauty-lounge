using DeBliss.Infrastructure;
using DeBliss.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

// The database check is what proves the API can actually reach SQL Server.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DeBlissDbContext>("database");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapHealthChecks("/health");

app.Run();
