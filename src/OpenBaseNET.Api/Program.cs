using OpenBaseNET.Api;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure ConnectionStrings:Default using User Secrets or environment variables.");

builder.Services.AddPersistence(connectionString);
builder.Services.AddScoped<CreateCustomer>();
builder.Services.AddScoped<GetCustomer>();
builder.Services.AddScoped<ListCustomers>();
builder.Services.AddScoped<UpdateCustomer>();
builder.Services.AddScoped<DeleteCustomer>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();
app.UseExceptionHandler();
app.MapControllers();
app.Run();

public partial class Program;
