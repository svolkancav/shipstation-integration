using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
using ShipStation.Integration;
using ShipStation.Integration.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddShipStation(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;

    // Surface the upstream status code instead of collapsing every integration
    // failure into a 500 — a caller retrying on 429 needs to see the 429.
    var problem = error switch
    {
        ShipStationApiException api => new ProblemDetails
        {
            Status = (int)(api.StatusCode ?? System.Net.HttpStatusCode.BadGateway),
            Title = "ShipStation rejected the request",
            Detail = api.Message
        },
        OperationCanceledException => new ProblemDetails
        {
            Status = StatusCodes.Status499ClientClosedRequest,
            Title = "Request cancelled"
        },
        _ => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Unexpected error"
        }
    };

    context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(problem);
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();

await app.RunAsync();

namespace ShipStation.Integration.Api
{
    public sealed class Program;
}
