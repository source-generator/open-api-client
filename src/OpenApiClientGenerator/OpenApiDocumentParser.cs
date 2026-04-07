using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenApiClientGenerator;

internal static class OpenApiDocumentParser
{
    public static OpenApiDocumentModel Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            var root = document.RootElement;
            EnsureObject(root, "$");

            if (!TryGetProperty(root, "paths", out var pathsElement))
            {
                throw new OpenApiParseException("The OpenAPI document must define a top-level 'paths' object.");
            }

            var model = new OpenApiDocumentModel();

            if (TryGetProperty(root, "components", out var componentsElement))
            {
                ParseComponents(model, componentsElement);
            }

            model.Paths = ParsePaths(pathsElement);
            return model;
        }
        catch (JsonException exception)
        {
            throw new OpenApiParseException($"Invalid JSON: {exception.Message}", exception);
        }
    }

    private static void ParseComponents(OpenApiDocumentModel model, JsonElement componentsElement)
    {
        EnsureObject(componentsElement, "$.components");

        if (TryGetProperty(componentsElement, "schemas", out var schemasElement))
        {
            foreach (var property in EnumerateObject(schemasElement, "$.components.schemas"))
            {
                model.Schemas[property.Name] = ParseSchema(property.Value, "$.components.schemas." + property.Name);
            }
        }

        if (TryGetProperty(componentsElement, "parameters", out var parametersElement))
        {
            foreach (var property in EnumerateObject(parametersElement, "$.components.parameters"))
            {
                model.Parameters[property.Name] = ParseParameter(property.Value, "$.components.parameters." + property.Name);
            }
        }

        if (TryGetProperty(componentsElement, "requestBodies", out var requestBodiesElement))
        {
            foreach (var property in EnumerateObject(requestBodiesElement, "$.components.requestBodies"))
            {
                model.RequestBodies[property.Name] = ParseRequestBody(property.Value, "$.components.requestBodies." + property.Name);
            }
        }

        if (TryGetProperty(componentsElement, "responses", out var responsesElement))
        {
            foreach (var property in EnumerateObject(responsesElement, "$.components.responses"))
            {
                model.Responses[property.Name] = ParseResponse(property.Value, "$.components.responses." + property.Name);
            }
        }
    }

    private static Dictionary<string, OpenApiPathModel> ParsePaths(JsonElement pathsElement)
    {
        var result = new Dictionary<string, OpenApiPathModel>(StringComparer.Ordinal);
        foreach (var pathProperty in EnumerateObject(pathsElement, "$.paths"))
        {
            var pathElement = pathProperty.Value;
            EnsureObject(pathElement, "$.paths." + pathProperty.Name);

            var pathModel = new OpenApiPathModel();

            if (TryGetProperty(pathElement, "parameters", out var pathParametersElement))
            {
                pathModel.Parameters.AddRange(ParseParametersArray(pathParametersElement, "$.paths." + pathProperty.Name + ".parameters"));
            }

            foreach (var operationProperty in pathElement.EnumerateObject())
            {
                var methodName = operationProperty.Name.ToLowerInvariant();
                if (!IsOperation(methodName))
                {
                    continue;
                }

                pathModel.Operations[methodName] = ParseOperation(
                    operationProperty.Value,
                    "$.paths." + pathProperty.Name + "." + operationProperty.Name);
            }

            result[pathProperty.Name] = pathModel;
        }

        return result;
    }

    private static OpenApiOperationModel ParseOperation(JsonElement element, string path)
    {
        EnsureObject(element, path);

        var model = new OpenApiOperationModel
        {
            OperationId = GetString(element, "operationId"),
        };

        if (TryGetProperty(element, "parameters", out var parametersElement))
        {
            model.Parameters.AddRange(ParseParametersArray(parametersElement, path + ".parameters"));
        }

        if (TryGetProperty(element, "requestBody", out var requestBodyElement))
        {
            model.RequestBody = ParseRequestBody(requestBodyElement, path + ".requestBody");
        }

        if (TryGetProperty(element, "responses", out var responsesElement))
        {
            foreach (var property in EnumerateObject(responsesElement, path + ".responses"))
            {
                model.Responses[property.Name] = ParseResponse(property.Value, path + ".responses." + property.Name);
            }
        }

        return model;
    }

    private static List<OpenApiParameterModel> ParseParametersArray(JsonElement element, string path)
    {
        EnsureArray(element, path);
        var result = new List<OpenApiParameterModel>();
        var index = 0;

        foreach (var item in element.EnumerateArray())
        {
            result.Add(ParseParameter(item, path + "[" + index + "]"));
            index++;
        }

        return result;
    }

    private static OpenApiParameterModel ParseParameter(JsonElement element, string path)
    {
        EnsureObject(element, path);

        if (TryGetProperty(element, "$ref", out var referenceElement))
        {
            return new OpenApiParameterModel
            {
                Reference = referenceElement.GetString(),
            };
        }

        var model = new OpenApiParameterModel
        {
            Name = GetRequiredString(element, "name", path),
            Location = GetRequiredString(element, "in", path),
            Required = GetBoolean(element, "required"),
        };

        if (TryGetProperty(element, "schema", out var schemaElement))
        {
            model.Schema = ParseSchema(schemaElement, path + ".schema");
        }

        return model;
    }

    private static OpenApiRequestBodyModel ParseRequestBody(JsonElement element, string path)
    {
        EnsureObject(element, path);

        if (TryGetProperty(element, "$ref", out var referenceElement))
        {
            return new OpenApiRequestBodyModel
            {
                Reference = referenceElement.GetString(),
            };
        }

        var model = new OpenApiRequestBodyModel
        {
            Required = GetBoolean(element, "required"),
        };

        if (TryGetProperty(element, "content", out var contentElement))
        {
            model.Content = ParseContent(contentElement, path + ".content");
        }

        return model;
    }

    private static OpenApiResponseModel ParseResponse(JsonElement element, string path)
    {
        EnsureObject(element, path);

        if (TryGetProperty(element, "$ref", out var referenceElement))
        {
            return new OpenApiResponseModel
            {
                Reference = referenceElement.GetString(),
            };
        }

        var model = new OpenApiResponseModel
        {
            Description = GetString(element, "description"),
        };

        if (TryGetProperty(element, "content", out var contentElement))
        {
            model.Content = ParseContent(contentElement, path + ".content");
        }

        return model;
    }

    private static Dictionary<string, OpenApiMediaTypeModel> ParseContent(JsonElement element, string path)
    {
        var result = new Dictionary<string, OpenApiMediaTypeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in EnumerateObject(element, path))
        {
            EnsureObject(property.Value, path + "." + property.Name);
            var mediaType = new OpenApiMediaTypeModel();

            if (TryGetProperty(property.Value, "schema", out var schemaElement))
            {
                mediaType.Schema = ParseSchema(schemaElement, path + "." + property.Name + ".schema");
            }

            result[property.Name] = mediaType;
        }

        return result;
    }

    private static OpenApiSchemaModel ParseSchema(JsonElement element, string path)
    {
        EnsureObject(element, path);

        if (TryGetProperty(element, "$ref", out var referenceElement))
        {
            return new OpenApiSchemaModel
            {
                Reference = referenceElement.GetString(),
            };
        }

        var model = new OpenApiSchemaModel
        {
            Type = GetString(element, "type"),
            Format = GetString(element, "format"),
            Nullable = GetBoolean(element, "nullable"),
        };

        if (TryGetProperty(element, "properties", out var propertiesElement))
        {
            foreach (var property in EnumerateObject(propertiesElement, path + ".properties"))
            {
                model.Properties[property.Name] = ParseSchema(property.Value, path + ".properties." + property.Name);
            }
        }

        if (TryGetProperty(element, "required", out var requiredElement))
        {
            EnsureArray(requiredElement, path + ".required");
            foreach (var item in requiredElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new OpenApiParseException("Schema 'required' entries must be strings at " + path + ".required.");
                }

                model.RequiredProperties.Add(item.GetString()!);
            }
        }

        if (TryGetProperty(element, "items", out var itemsElement))
        {
            model.Items = ParseSchema(itemsElement, path + ".items");
        }

        if (TryGetProperty(element, "additionalProperties", out var additionalPropertiesElement))
        {
            if (additionalPropertiesElement.ValueKind == JsonValueKind.Object)
            {
                model.AdditionalProperties = ParseSchema(additionalPropertiesElement, path + ".additionalProperties");
            }
        }

        if (TryGetProperty(element, "allOf", out var allOfElement))
        {
            model.AllOf.AddRange(ParseSchemaArray(allOfElement, path + ".allOf"));
        }

        if (TryGetProperty(element, "oneOf", out var oneOfElement))
        {
            model.OneOf.AddRange(ParseSchemaArray(oneOfElement, path + ".oneOf"));
        }

        if (TryGetProperty(element, "anyOf", out var anyOfElement))
        {
            model.AnyOf.AddRange(ParseSchemaArray(anyOfElement, path + ".anyOf"));
        }

        return model;
    }

    private static List<OpenApiSchemaModel> ParseSchemaArray(JsonElement element, string path)
    {
        EnsureArray(element, path);
        var result = new List<OpenApiSchemaModel>();
        var index = 0;

        foreach (var item in element.EnumerateArray())
        {
            result.Add(ParseSchema(item, path + "[" + index + "]"));
            index++;
        }

        return result;
    }

    private static IEnumerable<JsonProperty> EnumerateObject(JsonElement element, string path)
    {
        EnsureObject(element, path);
        return element.EnumerateObject();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
    {
        return element.TryGetProperty(name, out property);
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static string GetRequiredString(JsonElement element, string name, string path)
    {
        if (!TryGetProperty(element, name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new OpenApiParseException("Expected a string '" + name + "' property at " + path + ".");
        }

        return property.GetString()!;
    }

    private static bool GetBoolean(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.True;
    }

    private static void EnsureObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new OpenApiParseException("Expected an object at " + path + ".");
        }
    }

    private static void EnsureArray(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new OpenApiParseException("Expected an array at " + path + ".");
        }
    }

    private static bool IsOperation(string value)
    {
        return value == "get"
            || value == "post"
            || value == "put"
            || value == "patch"
            || value == "delete"
            || value == "head"
            || value == "options"
            || value == "trace";
    }
}

internal sealed class OpenApiDocumentModel
{
    public Dictionary<string, OpenApiSchemaModel> Schemas { get; } = new Dictionary<string, OpenApiSchemaModel>(StringComparer.Ordinal);

    public Dictionary<string, OpenApiParameterModel> Parameters { get; } = new Dictionary<string, OpenApiParameterModel>(StringComparer.Ordinal);

    public Dictionary<string, OpenApiRequestBodyModel> RequestBodies { get; } = new Dictionary<string, OpenApiRequestBodyModel>(StringComparer.Ordinal);

    public Dictionary<string, OpenApiResponseModel> Responses { get; } = new Dictionary<string, OpenApiResponseModel>(StringComparer.Ordinal);

    public Dictionary<string, OpenApiPathModel> Paths { get; set; } = new Dictionary<string, OpenApiPathModel>(StringComparer.Ordinal);

    public OpenApiSchemaModel ResolveSchema(OpenApiSchemaModel schema)
    {
        return schema.Reference is null ? schema : ResolveSchemaReference(schema.Reference);
    }

    public OpenApiSchemaModel ResolveSchemaReference(string reference)
    {
        var name = GetComponentName(reference, "#/components/schemas/");
        if (!Schemas.TryGetValue(name, out var schema))
        {
            throw new OpenApiParseException("Unresolved schema reference '" + reference + "'.");
        }

        return schema;
    }

    public OpenApiParameterModel ResolveParameter(OpenApiParameterModel parameter)
    {
        return parameter.Reference is null ? parameter : ResolveParameterReference(parameter.Reference);
    }

    public OpenApiParameterModel ResolveParameterReference(string reference)
    {
        var name = GetComponentName(reference, "#/components/parameters/");
        if (!Parameters.TryGetValue(name, out var parameter))
        {
            throw new OpenApiParseException("Unresolved parameter reference '" + reference + "'.");
        }

        return parameter;
    }

    public OpenApiRequestBodyModel ResolveRequestBody(OpenApiRequestBodyModel requestBody)
    {
        return requestBody.Reference is null ? requestBody : ResolveRequestBodyReference(requestBody.Reference);
    }

    public OpenApiRequestBodyModel ResolveRequestBodyReference(string reference)
    {
        var name = GetComponentName(reference, "#/components/requestBodies/");
        if (!RequestBodies.TryGetValue(name, out var requestBody))
        {
            throw new OpenApiParseException("Unresolved request body reference '" + reference + "'.");
        }

        return requestBody;
    }

    public OpenApiResponseModel ResolveResponse(OpenApiResponseModel response)
    {
        return response.Reference is null ? response : ResolveResponseReference(response.Reference);
    }

    public OpenApiResponseModel ResolveResponseReference(string reference)
    {
        var name = GetComponentName(reference, "#/components/responses/");
        if (!Responses.TryGetValue(name, out var response))
        {
            throw new OpenApiParseException("Unresolved response reference '" + reference + "'.");
        }

        return response;
    }

    public string GetSchemaComponentName(string reference)
    {
        return GetComponentName(reference, "#/components/schemas/");
    }

    private static string GetComponentName(string reference, string prefix)
    {
        if (reference.StartsWith(prefix, StringComparison.Ordinal))
        {
            return reference.Substring(prefix.Length);
        }

        throw new OpenApiParseException("Only local component references are supported. Found '" + reference + "'.");
    }
}

internal sealed class OpenApiPathModel
{
    public List<OpenApiParameterModel> Parameters { get; } = new List<OpenApiParameterModel>();

    public Dictionary<string, OpenApiOperationModel> Operations { get; } = new Dictionary<string, OpenApiOperationModel>(StringComparer.OrdinalIgnoreCase);
}

internal sealed class OpenApiOperationModel
{
    public string? OperationId { get; set; }

    public List<OpenApiParameterModel> Parameters { get; } = new List<OpenApiParameterModel>();

    public OpenApiRequestBodyModel? RequestBody { get; set; }

    public Dictionary<string, OpenApiResponseModel> Responses { get; } = new Dictionary<string, OpenApiResponseModel>(StringComparer.OrdinalIgnoreCase);
}

internal sealed class OpenApiParameterModel
{
    public string? Reference { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public bool Required { get; set; }

    public OpenApiSchemaModel? Schema { get; set; }
}

internal sealed class OpenApiRequestBodyModel
{
    public string? Reference { get; set; }

    public bool Required { get; set; }

    public Dictionary<string, OpenApiMediaTypeModel> Content { get; set; } = new Dictionary<string, OpenApiMediaTypeModel>(StringComparer.OrdinalIgnoreCase);
}

internal sealed class OpenApiResponseModel
{
    public string? Reference { get; set; }

    public string? Description { get; set; }

    public Dictionary<string, OpenApiMediaTypeModel> Content { get; set; } = new Dictionary<string, OpenApiMediaTypeModel>(StringComparer.OrdinalIgnoreCase);
}

internal sealed class OpenApiMediaTypeModel
{
    public OpenApiSchemaModel? Schema { get; set; }
}

internal sealed class OpenApiSchemaModel
{
    public string? Reference { get; set; }

    public string? Type { get; set; }

    public string? Format { get; set; }

    public bool Nullable { get; set; }

    public Dictionary<string, OpenApiSchemaModel> Properties { get; } = new Dictionary<string, OpenApiSchemaModel>(StringComparer.Ordinal);

    public HashSet<string> RequiredProperties { get; } = new HashSet<string>(StringComparer.Ordinal);

    public OpenApiSchemaModel? Items { get; set; }

    public OpenApiSchemaModel? AdditionalProperties { get; set; }

    public List<OpenApiSchemaModel> AllOf { get; } = new List<OpenApiSchemaModel>();

    public List<OpenApiSchemaModel> OneOf { get; } = new List<OpenApiSchemaModel>();

    public List<OpenApiSchemaModel> AnyOf { get; } = new List<OpenApiSchemaModel>();
}

internal sealed class OpenApiParseException : Exception
{
    public OpenApiParseException(string message)
        : base(message)
    {
    }

    public OpenApiParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
