using DeBliss.Api.Common;
using DeBliss.Application;
using DeBliss.Infrastructure;
using DeBliss.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();

// Every failure leaves the API as an RFC 9457 problem document: the registered
// handler translates the Application layer's exceptions, and ProblemDetails
// covers everything else.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// The database check is what proves the API can actually reach SQL Server.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DeBlissDbContext>("database");

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.MapHealthChecks("/health");

app.Run();

/// <summary>Entry point exposed so integration tests can host this exact pipeline.</summary>
public partial class Program;
