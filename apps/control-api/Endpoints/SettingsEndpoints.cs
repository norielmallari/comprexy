using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models;
using Comprexy.Application.Services.Settings;
using Comprexy.ControlApi.Contracts.Settings;
using Comprexy.Infrastructure.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Comprexy.ControlApi.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/comprexy")
            .WithTags("ComprexyOperatorSettings");

        group.MapGet("/settings", GetSettingsAsync);
        group.MapPut("/settings", PutSettingsAsync);

        return app;
    }

    private static async Task<IResult> GetSettingsAsync(
        IOperatorSettingsStore store,
        CancellationToken cancellationToken)
    {
        var (revision, json, updatedAt) = await store.GetAsync(cancellationToken);
        var settings = OperatorMutableSettingsJson.Parse(json);
        var dto = new OperatorSettingsResponseDto
        {
            Revision = revision,
            Settings = settings,
            UpdatedAt = updatedAt
        };

        return TypedResults.Ok(dto);
    }

    private static async Task<IResult> PutSettingsAsync(
        [FromBody] OperatorSettingsPutRequestDto body,
        HttpRequest httpRequest,
        IOperatorSettingsStore store,
        IOperatorSettingsOverlay overlay,
        OperatorSettingsChangeTokenSource changeTokens,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return TypedResults.BadRequest(new { error = "Body is required." });
        }

        long expectedRevision = body.Revision;
        if (httpRequest.Headers.TryGetValue("If-Match", out var ifMatchValues))
        {
            var raw = ifMatchValues.ToString().Trim().Trim('"');
            if (long.TryParse(raw, out var fromHeader))
            {
                expectedRevision = fromHeader;
            }
        }

        string settingsJson;
        try
        {
            // Round-trip through parse to reject unknown/secret keys, then serialize canonical JSON.
            var root = JsonSerializer.SerializeToElement(body.Settings, OperatorMutableSettingsJson.JsonOptions);
            OperatorMutableSettingsJson.RejectUnknownOrForbidden(root);
            var normalized = OperatorMutableSettingsJson.Parse(
                JsonSerializer.Serialize(body.Settings, OperatorMutableSettingsJson.JsonOptions));
            settingsJson = OperatorMutableSettingsJson.Serialize(normalized);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }
        catch (JsonException ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }

        var put = await store.TryPutAsync(expectedRevision, settingsJson, cancellationToken);
        if (put is null)
        {
            var (currentRevision, _, _) = await store.GetAsync(cancellationToken);
            return TypedResults.Conflict(new
            {
                error = "revision_conflict",
                currentRevision
            });
        }

        var (newRevision, updatedAt) = put.Value;
        if (overlay.TryUpdate(newRevision, settingsJson))
        {
            changeTokens.Signal();
        }

        return TypedResults.Ok(new OperatorSettingsResponseDto
        {
            Revision = newRevision,
            Settings = OperatorMutableSettingsJson.Parse(settingsJson),
            UpdatedAt = updatedAt
        });
    }
}
