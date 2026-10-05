using DeBliss.Api.Common;
using DeBliss.Api.DevData;
using DeBliss.Api.Security;
using DeBliss.Application;
using DeBliss.Infrastructure;
using DeBliss.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDeblissOpenApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDeblissSecurity(builder.Configuration);

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

    // Demo scenario for the calendar workspace: only in Development and only
    // while the staff table is empty, so production data is never overwritten.
    await DevDataSeeder.SeedAsync(app);
    await DevAuthSeeder.SeedAsync(app);
}

app.UseHttpsRedirection();

// Authentication before authorization before the endpoints they guard.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

app.Run();

/// <summary>Entry point exposed so integration tests can host this exact pipeline.</summary>
public partial class Program;
