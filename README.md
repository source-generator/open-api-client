# OpenAPI Client Source Generator

This repository contains a Roslyn source generator that reads an OpenAPI document from `AdditionalFiles` metadata and emits an `HttpClient`-based client plus DTOs.

The generator uses a custom JSON parser built on `System.Text.Json`. It supports:

- local component references such as `#/components/schemas/Pet`
- path, query, and header parameters
- JSON, text, and binary request or response bodies
- object DTO generation for `components.schemas`

## Usage

Reference the generator as an analyzer and attach one or more OpenAPI documents as additional files with `ClientNamespace` and `ClientName` metadata:

```xml
<ItemGroup>
  <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="ClientNamespace" />
  <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="ClientName" />

  <ProjectReference Include="..\src\OpenApiClientGenerator\OpenApiClientGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />

  <AdditionalFiles Include="OpenApi\petstore.json"
                   ClientNamespace="MyCompany.PetStore"
                   ClientName="PetStoreClient" />
  <AdditionalFiles Include="OpenApi\store.json"
                   ClientNamespace="MyCompany.PetStore"
                   ClientName="StoreClient" />
</ItemGroup>
```

`CompilerVisibleItemMetadata` is required so Roslyn exposes `ClientNamespace` and `ClientName` to `AnalyzerConfigOptionsProvider`.

The generated client expects an `HttpClient` instance. Configure `BaseAddress` on that client in your consuming application.

## Sample

The sample consumer project lives at `samples/SamplePetStore/SamplePetStore.csproj` and demonstrates generating multiple clients from multiple additional files.

## Tests

The generator tests live at `tests/OpenApiClientGenerator.Tests/OpenApiClientGenerator.Tests.csproj` and verify both single-file and multi-file generation behavior.
