using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace OpenApiClientGenerator.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void GeneratesHttpClientClientAndModelsFromAdditionalFile()
    {
        var result = RunGenerator(
            """
            using System.Net.Http;
            using SamplePetStore.Clients;

            public sealed class Consumer
            {
                public object Create(HttpClient httpClient)
                {
                    return new PetStoreClient(httpClient);
                }
            }
            """,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PetStoreApi.json")),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = "SamplePetStore.Clients",
                ["build_metadata.AdditionalFiles.ClientName"] = "PetStoreClient",
            });

        Assert.Empty(result.DriverDiagnostics);
        Assert.Empty(result.ErrorDiagnostics);
        Assert.Single(result.GeneratedSources);

        var generatedSource = result.GeneratedSources[0];
        Assert.Contains("public sealed partial class PetStoreClient", generatedSource);
        Assert.Contains("public async Task<Pet?> GetPetByIdAsync(long petId, CancellationToken cancellationToken = default)", generatedSource);
        Assert.Contains("public async Task<Pet?> CreatePetAsync(CreatePetRequest body, CancellationToken cancellationToken = default)", generatedSource);
        Assert.Contains("public sealed partial class Pet", generatedSource);
        Assert.Contains("public sealed partial class CreatePetRequest", generatedSource);
    }

    [Fact]
    public void AcceptsLowerCaseAdditionalFileMetadataKeys()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            """
            {
              "openapi": "3.0.1",
              "paths": {
                "/values": {
                  "get": {
                    "responses": {
                      "200": {
                        "description": "ok",
                        "content": {
                          "application/json": {
                            "schema": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.additionalfiles.clientnamespace"] = "LowerCase.Clients",
                ["build_metadata.additionalfiles.clientname"] = "LowerClient",
            });

        Assert.Empty(result.DriverDiagnostics);
        Assert.Empty(result.ErrorDiagnostics);
        Assert.Single(result.GeneratedSources);
        Assert.Contains("namespace LowerCase.Clients", result.GeneratedSources[0]);
        Assert.Contains("public sealed partial class LowerClient", result.GeneratedSources[0]);
    }

    [Fact]
    public void HintNameUsesFullClientNamespaceAndClientName()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            """
            {
              "openapi": "3.0.1",
              "paths": {}
            }
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = "Contoso.PetStore.Clients",
                ["build_metadata.AdditionalFiles.ClientName"] = "PetStoreClient",
            });

        Assert.Empty(result.DriverDiagnostics);
        Assert.Empty(result.ErrorDiagnostics);
        Assert.Equal("Contoso_PetStore_Clients.PetStoreClient.g.cs", Assert.Single(result.GeneratedHintNames));
    }

    [Fact]
    public void GeneratesSourcesForMultipleAdditionalFiles()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            CreateAdditionalFile(
                path: "PetsApi.json",
                additionalFileText:
                """
                {
                  "openapi": "3.0.1",
                  "paths": {
                    "/pets": {
                      "get": {
                        "responses": {
                          "200": {
                            "description": "ok",
                            "content": {
                              "application/json": {
                                "schema": { "type": "string" }
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
                """,
                clientNamespace: "Multi.Pets.Clients",
                clientName: "PetsClient"),
            CreateAdditionalFile(
                path: "OrdersApi.json",
                additionalFileText:
                """
                {
                  "openapi": "3.0.1",
                  "paths": {
                    "/orders": {
                      "get": {
                        "responses": {
                          "200": {
                            "description": "ok",
                            "content": {
                              "application/json": {
                                "schema": { "type": "string" }
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
                """,
                clientNamespace: "Multi.Orders.Clients",
                clientName: "OrdersClient"));

        Assert.Empty(result.DriverDiagnostics);
        Assert.Empty(result.ErrorDiagnostics);
        Assert.Equal(2, result.GeneratedSources.Count);
        Assert.Contains("namespace Multi.Pets.Clients", result.GeneratedSources[0] + result.GeneratedSources[1]);
        Assert.Contains("namespace Multi.Orders.Clients", result.GeneratedSources[0] + result.GeneratedSources[1]);
        Assert.Contains("Multi_Pets_Clients.PetsClient.g.cs", result.GeneratedHintNames);
        Assert.Contains("Multi_Orders_Clients.OrdersClient.g.cs", result.GeneratedHintNames);
    }

    [Fact]
    public void IgnoresAdditionalFilesWithoutGeneratorMetadata()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            """
            {
              "openapi": "3.0.1",
              "paths": {}
            }
            """);

        Assert.Empty(result.DriverDiagnostics);
        Assert.Empty(result.GeneratedSources);
        Assert.Empty(result.ErrorDiagnostics);
    }

    [Fact]
    public void ReportsDiagnosticWhenMetadataIsIncomplete()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            """
            {
              "openapi": "3.0.1",
              "paths": {}
            }
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = "Only.Namespace",
            });

        var diagnostic = Assert.Single(result.DriverDiagnostics);
        Assert.Equal("OAC001", diagnostic.Id);
        Assert.Contains("ClientNamespace and ClientName", diagnostic.GetMessage());
    }

    [Fact]
    public void ReportsDiagnosticWhenAdditionalFileCannotBeRead()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            additionalText: new NullAdditionalText("Unreadable.json"),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = "Unreadable.Clients",
                ["build_metadata.AdditionalFiles.ClientName"] = "UnreadableClient",
            });

        var diagnostic = Assert.Single(result.DriverDiagnostics);
        Assert.Equal("OAC002", diagnostic.Id);
        Assert.Contains("could not be read", diagnostic.GetMessage());
    }

    [Fact]
    public void ReportsDiagnosticForInvalidJson()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            "{ not valid json",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = "Broken.Clients",
                ["build_metadata.AdditionalFiles.ClientName"] = "BrokenClient",
            });

        var diagnostic = Assert.Single(result.DriverDiagnostics);
        Assert.Equal("OAC002", diagnostic.Id);
        Assert.Contains("Invalid JSON", diagnostic.GetMessage());
    }

    [Fact]
    public void ReportsDiagnosticForMissingPathsObject()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            """
            {
              "openapi": "3.0.1"
            }
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = "Broken.Clients",
                ["build_metadata.AdditionalFiles.ClientName"] = "BrokenClient",
            });

        var diagnostic = Assert.Single(result.DriverDiagnostics);
        Assert.Equal("OAC002", diagnostic.Id);
        Assert.Contains("top-level 'paths' object", diagnostic.GetMessage());
    }

    [Fact]
    public void ReportsDiagnosticForUnresolvedReferences()
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            """
            {
              "openapi": "3.0.1",
              "paths": {
                "/pets": {
                  "get": {
                    "responses": {
                      "200": {
                        "description": "ok",
                        "content": {
                          "application/json": {
                            "schema": {
                              "$ref": "#/components/schemas/Missing"
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = "Broken.Clients",
                ["build_metadata.AdditionalFiles.ClientName"] = "BrokenClient",
            });

        var diagnostic = Assert.Single(result.DriverDiagnostics);
        Assert.Equal("OAC002", diagnostic.Id);
        Assert.Contains("Unresolved schema reference", diagnostic.GetMessage());
    }

    [Fact]
    public void EmitsQueryHeaderBodyAndResponseVariants()
    {
        var generatedSource = GenerateSource(
            ComplexApiJson,
            "Complex.Clients",
            "ComplexClient");

        Assert.Contains("public async Task<string?> GetReportsByReportIdAsync(Guid reportId, bool includeDeleted, IEnumerable<string>? tags = null, string? xTenantId = null, CancellationToken cancellationToken = default)", generatedSource);
        Assert.Contains("if (tags is not null)", generatedSource);
        Assert.Contains("foreach (var item in tags)", generatedSource);
        Assert.Contains("AddQueryParameter(queryParameters, \"includeDeleted\", includeDeleted);", generatedSource);
        Assert.Contains("request.Headers.TryAddWithoutValidation(\"x-tenant-id\", FormatUriValue(xTenantId));", generatedSource);
        Assert.Contains("request.Content = new StringContent(body, Encoding.UTF8, \"text/plain\");", generatedSource);
        Assert.Contains("request.Content = new ByteArrayContent(body);", generatedSource);
        Assert.Contains("request.Content.Headers.ContentType = new MediaTypeHeaderValue(\"application/octet-stream\");", generatedSource);
        Assert.Contains("return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);", generatedSource);
        Assert.Contains("return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);", generatedSource);
        Assert.Contains("public async Task DeleteReportsByReportIdAsync(Guid reportId, CancellationToken cancellationToken = default)", generatedSource);
    }

    [Fact]
    public void UsesFallbackMethodNamesAndAvoidsBodyParameterNameCollisions()
    {
        var generatedSource = GenerateSource(
            """
            {
              "openapi": "3.0.1",
              "paths": {
                "/widgets/{body}": {
                  "post": {
                    "parameters": [
                      {
                        "name": "body",
                        "in": "path",
                        "required": true,
                        "schema": { "type": "string" }
                      }
                    ],
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "properties": {
                              "name": { "type": "string" }
                            }
                          }
                        }
                      }
                    },
                    "responses": {
                      "201": {
                        "description": "created",
                        "content": {
                          "application/json": {
                            "schema": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """,
            "Fallback.Clients",
            "FallbackClient");

        Assert.Contains("public async Task<string?> PostWidgetsByBodyAsync(string body, JsonElement requestBody, CancellationToken cancellationToken = default)", generatedSource);
        Assert.Contains("request.Content = JsonContent.Create(requestBody, options: _serializerOptions);", generatedSource);
    }

    [Fact]
    public void EmitsFlattenedObjectModelsAndTypeMappings()
    {
        var generatedSource = GenerateSource(
            TypeMappingApiJson,
            "Mapping.Clients",
            "MappingClient");

        Assert.Contains("public sealed partial class DerivedPet", generatedSource);
        Assert.Contains("public string Name { get; set; } = default!;", generatedSource);
        Assert.Contains("public DateTime? BirthDate { get; set; }", generatedSource);
        Assert.Contains("public DateTimeOffset? LastVisit { get; set; }", generatedSource);
        Assert.Contains("public Guid? ExternalId { get; set; }", generatedSource);
        Assert.Contains("public byte[] Payload { get; set; } = default!;", generatedSource);
        Assert.Contains("public decimal? Weight { get; set; }", generatedSource);
        Assert.Contains("public double? Score { get; set; }", generatedSource);
        Assert.Contains("public float? Ratio { get; set; }", generatedSource);
        Assert.Contains("public bool? IsHealthy { get; set; }", generatedSource);
        Assert.Contains("public List<string>? Tags { get; set; }", generatedSource);
        Assert.Contains("public Dictionary<string, int>? Attributes { get; set; }", generatedSource);
        Assert.DoesNotContain("public sealed partial class FlexibleMap", generatedSource);
        Assert.DoesNotContain("public sealed partial class Variant", generatedSource);
    }

    [Fact]
    public void ParserResolvesLocalComponentReferences()
    {
        var document = global::OpenApiClientGenerator.OpenApiDocumentParser.Parse(
            """
            {
              "openapi": "3.0.1",
              "paths": {
                "/pets/{petId}": {
                  "parameters": [
                    { "$ref": "#/components/parameters/PetId" }
                  ],
                  "get": {
                    "requestBody": { "$ref": "#/components/requestBodies/PetBody" },
                    "responses": {
                      "200": { "$ref": "#/components/responses/PetResponse" }
                    }
                  }
                }
              },
              "components": {
                "parameters": {
                  "PetId": {
                    "name": "petId",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "integer", "format": "int64" }
                  }
                },
                "requestBodies": {
                  "PetBody": {
                    "required": true,
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/Pet" }
                      }
                    }
                  }
                },
                "responses": {
                  "PetResponse": {
                    "description": "ok",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/Pet" }
                      }
                    }
                  }
                },
                "schemas": {
                  "Pet": {
                    "type": "object",
                    "properties": {
                      "id": { "type": "integer" }
                    }
                  }
                }
              }
            }
            """);

        var path = Assert.Single(document.Paths);
        var operation = Assert.Single(path.Value.Operations);
        var resolvedParameter = document.ResolveParameter(path.Value.Parameters[0]);
        var resolvedRequestBody = document.ResolveRequestBody(operation.Value.RequestBody!);
        var resolvedResponse = document.ResolveResponse(operation.Value.Responses["200"]);

        Assert.Equal("petId", resolvedParameter.Name);
        Assert.True(resolvedRequestBody.Required);
        Assert.Equal("ok", resolvedResponse.Description);
        Assert.Same(document.Schemas["Pet"], document.ResolveSchemaReference("#/components/schemas/Pet"));
    }

    [Fact]
    public void ParserRejectsUnsupportedReferenceKinds()
    {
        var document = global::OpenApiClientGenerator.OpenApiDocumentParser.Parse(
            """
            {
              "openapi": "3.0.1",
              "paths": {},
              "components": {
                "schemas": {
                  "Pet": { "type": "object" }
                }
              }
            }
            """);

        var exception = Assert.Throws<global::OpenApiClientGenerator.OpenApiParseException>(
            () => document.ResolveSchemaReference("https://example.test/schemas/Pet"));

        Assert.Contains("Only local component references are supported", exception.Message);
    }

    private static string GenerateSource(string additionalFileText, string clientNamespace, string clientName)
    {
        var result = RunGenerator(
            "public sealed class Consumer { }",
            additionalFileText,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = clientNamespace,
                ["build_metadata.AdditionalFiles.ClientName"] = clientName,
            });

        Assert.Empty(result.DriverDiagnostics);
        Assert.Empty(result.ErrorDiagnostics);
        return Assert.Single(result.GeneratedSources);
    }

    private static GeneratorRunResult RunGenerator(
        string consumerSource,
        string additionalFileText,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return RunGenerator(
            consumerSource,
            new InMemoryAdditionalText("OpenApi.json", additionalFileText),
            metadata);
    }

    private static GeneratorRunResult RunGenerator(
        string consumerSource,
        params AdditionalFileInput[] additionalFiles)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(consumerSource, Encoding.UTF8));
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new global::OpenApiClientGenerator.OpenApiClientGenerator();
        var metadataByPath = additionalFiles.ToDictionary(
            static file => file.AdditionalText.Path,
            static file => file.Metadata,
            StringComparer.Ordinal);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { generator.AsSourceGenerator() },
            additionalTexts: additionalFiles.Select(static file => file.AdditionalText).ToArray(),
            parseOptions: (CSharpParseOptions)syntaxTree.Options,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>(), metadataByPath));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var driverDiagnostics);
        var runResult = driver.GetRunResult();

        return new GeneratorRunResult(
            driverDiagnostics,
            outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList(),
            outputCompilation.SyntaxTrees.Skip(1).Select(static tree => tree.ToString()).ToList(),
            runResult.Results.SelectMany(static result => result.GeneratedSources).Select(static source => source.HintName).ToList());
    }

    private static GeneratorRunResult RunGenerator(
        string consumerSource,
        AdditionalText additionalText,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return RunGenerator(
            consumerSource,
            new AdditionalFileInput(additionalText, metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    private sealed class GeneratorRunResult
    {
        public GeneratorRunResult(
            ImmutableArray<Diagnostic> driverDiagnostics,
            IReadOnlyList<Diagnostic> errorDiagnostics,
            IReadOnlyList<string> generatedSources,
            IReadOnlyList<string> generatedHintNames)
        {
            DriverDiagnostics = driverDiagnostics;
            ErrorDiagnostics = errorDiagnostics;
            GeneratedSources = generatedSources;
            GeneratedHintNames = generatedHintNames;
        }

        public ImmutableArray<Diagnostic> DriverDiagnostics { get; }

        public IReadOnlyList<Diagnostic> ErrorDiagnostics { get; }

        public IReadOnlyList<string> GeneratedSources { get; }

        public IReadOnlyList<string> GeneratedHintNames { get; }
    }

    private static AdditionalFileInput CreateAdditionalFile(
        string path,
        string additionalFileText,
        string clientNamespace,
        string clientName)
    {
        return new AdditionalFileInput(
            new InMemoryAdditionalText(path, additionalFileText),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.ClientNamespace"] = clientNamespace,
                ["build_metadata.AdditionalFiles.ClientName"] = clientName,
            });
    }

    private sealed class AdditionalFileInput
    {
        public AdditionalFileInput(AdditionalText additionalText, IReadOnlyDictionary<string, string> metadata)
        {
            AdditionalText = additionalText;
            Metadata = metadata;
        }

        public AdditionalText AdditionalText { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return _text;
        }
    }

    private sealed class NullAdditionalText : AdditionalText
    {
        public NullAdditionalText(string path)
        {
            Path = path;
        }

        public override string Path { get; }

        public override SourceText? GetText(CancellationToken cancellationToken = default)
        {
            return null;
        }
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions;
        private readonly IReadOnlyDictionary<string, AnalyzerConfigOptions> _additionalFileOptions;

        public TestAnalyzerConfigOptionsProvider(
            IReadOnlyDictionary<string, string> globalValues,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? additionalFileValuesByPath = null)
        {
            _globalOptions = new TestAnalyzerConfigOptions(globalValues);
            _additionalFileOptions = (additionalFileValuesByPath ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal))
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => (AnalyzerConfigOptions)new TestAnalyzerConfigOptions(pair.Value),
                    StringComparer.Ordinal);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            if (_additionalFileOptions.TryGetValue(textFile.Path, out var options))
            {
                return options;
            }

            return _globalOptions;
        }
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value)
        {
            return _values.TryGetValue(key, out value!);
        }
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        Assert.False(string.IsNullOrWhiteSpace(trustedPlatformAssemblies));

        return trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
    }

    private const string ComplexApiJson =
        """
        {
          "openapi": "3.0.1",
          "paths": {
            "/reports/{reportId}": {
              "parameters": [
                {
                  "name": "reportId",
                  "in": "path",
                  "required": true,
                  "schema": { "type": "string", "format": "uuid" }
                }
              ],
              "get": {
                "parameters": [
                  {
                    "name": "tags",
                    "in": "query",
                    "schema": {
                      "type": "array",
                      "items": { "type": "string" }
                    }
                  },
                  {
                    "name": "includeDeleted",
                    "in": "query",
                    "required": true,
                    "schema": { "type": "boolean" }
                  },
                  {
                    "name": "x-tenant-id",
                    "in": "header",
                    "schema": { "type": "string" }
                  }
                ],
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "text/plain": {
                        "schema": { "type": "string" }
                      }
                    }
                  }
                }
              },
              "put": {
                "requestBody": {
                  "content": {
                    "text/plain": {
                      "schema": { "type": "string" }
                    }
                  }
                },
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": {
                        "schema": { "type": "string" }
                      }
                    }
                  }
                }
              },
              "patch": {
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/octet-stream": {
                      "schema": { "type": "string", "format": "binary" }
                    }
                  }
                },
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/octet-stream": {
                        "schema": { "type": "string", "format": "binary" }
                      }
                    }
                  }
                }
              },
              "delete": {
                "responses": {
                  "204": {
                    "description": "deleted"
                  }
                }
              }
            }
          }
        }
        """;

    private const string TypeMappingApiJson =
        """
        {
          "openapi": "3.0.1",
          "paths": {
            "/pets": {
              "get": {
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/DerivedPet" }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "BasePet": {
                "type": "object",
                "required": [ "name", "payload" ],
                "properties": {
                  "name": { "type": "string" },
                  "payload": { "type": "string", "format": "byte" },
                  "birthDate": { "type": "string", "format": "date" },
                  "lastVisit": { "type": "string", "format": "date-time" },
                  "externalId": { "type": "string", "format": "uuid" }
                }
              },
              "DerivedPet": {
                "allOf": [
                  { "$ref": "#/components/schemas/BasePet" },
                  {
                    "type": "object",
                    "properties": {
                      "weight": { "type": "number" },
                      "score": { "type": "number", "format": "double" },
                      "ratio": { "type": "number", "format": "float" },
                      "isHealthy": { "type": "boolean" },
                      "tags": {
                        "type": "array",
                        "items": { "type": "string" }
                      },
                      "attributes": {
                        "type": "object",
                        "additionalProperties": { "type": "integer" }
                      }
                    }
                  }
                ]
              },
              "FlexibleMap": {
                "type": "object",
                "additionalProperties": { "type": "integer" }
              },
              "Variant": {
                "oneOf": [
                  { "type": "string" },
                  { "type": "integer" }
                ]
              }
            }
          }
        }
        """;
}
