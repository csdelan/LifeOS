using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LifeOs.Api.Hosting;

/// <summary>
/// Shapes the OpenAPI document the web client's types are generated from
/// (<c>web/src/api/openapi.json</c>, code review CR9), so it is the same wherever it is made
/// and says what the API actually sends.
/// </summary>
internal static class WebClientDocument
{
    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            // Named for the API, not the host that serves it, and with no server URL: the test
            // that checks the committed document runs on a random port.
            document.Info.Title = "LifeOS API";
            document.Servers = [];
            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, _) =>
        {
            // The web JSON options read a number sent as a string, so the default document types
            // every number as "integer or string". The API always writes a number.
            if (schema.Type is { } type
                && (type & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0
                && (type & JsonSchemaType.String) != 0)
            {
                schema.Type = type & ~JsonSchemaType.String;
                schema.Pattern = null;
            }

            // The API writes every property of a response, nulls included, so one that can't be
            // null is always there and the generated type needn't make it optional. A request may
            // leave any property out (it reads as its default), so request schemas keep theirs
            // optional.
            if (context.ParameterDescription is null
                && context.JsonPropertyInfo is null
                && context.JsonTypeInfo.Kind == JsonTypeInfoKind.Object
                && schema.Properties is { Count: > 0 })
            {
                foreach (var property in context.JsonTypeInfo.Properties)
                {
                    if (!property.IsGetNullable && schema.Properties.ContainsKey(property.Name))
                    {
                        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
                        schema.Required.Add(property.Name);
                    }
                }
            }

            return Task.CompletedTask;
        });
    }
}
