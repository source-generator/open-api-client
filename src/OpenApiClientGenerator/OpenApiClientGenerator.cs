using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace OpenApiClientGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class OpenApiClientGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidMetadataDescriptor = new DiagnosticDescriptor(
        id: "OAC001",
        title: "Invalid OpenAPI generator metadata",
        messageFormat: "Additional file '{0}' must define both ClientNamespace and ClientName metadata",
        category: "OpenApiClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParseErrorDescriptor = new DiagnosticDescriptor(
        id: "OAC002",
        title: "OpenAPI parsing failed",
        messageFormat: "Failed to parse '{0}': {1}",
        category: "OpenApiClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenerationErrorDescriptor = new DiagnosticDescriptor(
        id: "OAC003",
        title: "OpenAPI generation failed",
        messageFormat: "Failed to generate client for '{0}': {1}",
        category: "OpenApiClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generationInputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => CreateGenerationResult(pair.Left, pair.Right, cancellationToken))
            .Where(static result => result is not null);

        context.RegisterSourceOutput(generationInputs, static (productionContext, result) =>
        {
            if (result is null)
            {
                return;
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (!string.IsNullOrWhiteSpace(result.Source))
            {
                productionContext.AddSource(result.HintName, SourceText.From(result.Source!, System.Text.Encoding.UTF8));
            }
        });
    }

    private static GeneratedClientResult? CreateGenerationResult(
        AdditionalText additionalText,
        AnalyzerConfigOptionsProvider optionsProvider,
        CancellationToken cancellationToken)
    {
        var options = optionsProvider.GetOptions(additionalText);
        var hasNamespace = TryGetAdditionalFileMetadata(options, "ClientNamespace", out var clientNamespace);
        var hasName = TryGetAdditionalFileMetadata(options, "ClientName", out var clientName);

        if (!hasNamespace && !hasName)
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<GeneratorDiagnostic>();

        if (string.IsNullOrWhiteSpace(clientNamespace) || string.IsNullOrWhiteSpace(clientName))
        {
            diagnostics.Add(new GeneratorDiagnostic(
                InvalidMetadataDescriptor,
                additionalText.Path));

            return new GeneratedClientResult(
                CreateHintName(clientNamespace, clientName),
                null,
                diagnostics.ToImmutable());
        }

        var text = additionalText.GetText(cancellationToken);
        if (text is null)
        {
            diagnostics.Add(new GeneratorDiagnostic(
                ParseErrorDescriptor,
                additionalText.Path,
                "the file could not be read"));

            return new GeneratedClientResult(
                CreateHintName(clientNamespace, clientName),
                null,
                diagnostics.ToImmutable());
        }

        try
        {
            var document = OpenApiDocumentParser.Parse(text.ToString());
            var source = new OpenApiClientEmitter(document, clientNamespace!, clientName!).Emit();

            return new GeneratedClientResult(
                CreateHintName(clientNamespace, clientName),
                source,
                diagnostics.ToImmutable());
        }
        catch (OpenApiParseException exception)
        {
            diagnostics.Add(new GeneratorDiagnostic(
                ParseErrorDescriptor,
                additionalText.Path,
                exception.Message));
        }
        catch (Exception exception)
        {
            diagnostics.Add(new GeneratorDiagnostic(
                GenerationErrorDescriptor,
                additionalText.Path,
                exception.Message));
        }

        return new GeneratedClientResult(
            CreateHintName(clientNamespace, clientName),
            null,
            diagnostics.ToImmutable());
    }

    private static string CreateHintName(string? clientNamespace, string? clientName)
    {
        return string.Concat(
            SanitizeHintPart(clientNamespace),
            ".",
            SanitizeHintPart(clientName),
            ".g.cs");
    }

    private static bool TryGetAdditionalFileMetadata(AnalyzerConfigOptions options, string metadataName, out string value)
    {
        if (options.TryGetValue("build_metadata.AdditionalFiles." + metadataName, out var candidate))
        {
            value = candidate ?? string.Empty;
            return true;
        }

        if (options.TryGetValue("build_metadata.additionalfiles." + metadataName, out candidate))
        {
            value = candidate ?? string.Empty;
            return true;
        }

        if (options.TryGetValue("build_metadata.AdditionalFiles." + metadataName.ToLowerInvariant(), out candidate))
        {
            value = candidate ?? string.Empty;
            return true;
        }

        if (options.TryGetValue("build_metadata.additionalfiles." + metadataName.ToLowerInvariant(), out candidate))
        {
            value = candidate ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string SanitizeHintPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "OpenApi";
        }

        var buffer = new List<char>(value!.Length);
        foreach (var character in value)
        {
            buffer.Add(char.IsLetterOrDigit(character) ? character : '_');
        }

        return new string(buffer.ToArray());
    }

    private sealed class GeneratedClientResult
    {
        public GeneratedClientResult(string hintName, string? source, ImmutableArray<GeneratorDiagnostic> diagnostics)
        {
            HintName = hintName;
            Source = source;
            Diagnostics = diagnostics;
        }

        public string HintName { get; }

        public string? Source { get; }

        public ImmutableArray<GeneratorDiagnostic> Diagnostics { get; }
    }

    private sealed class GeneratorDiagnostic
    {
        public GeneratorDiagnostic(DiagnosticDescriptor descriptor, params object[] messageArgs)
        {
            Descriptor = descriptor;
            MessageArgs = messageArgs;
        }

        public DiagnosticDescriptor Descriptor { get; }

        public object[] MessageArgs { get; }

        public Diagnostic ToDiagnostic()
        {
            return Diagnostic.Create(Descriptor, Location.None, MessageArgs);
        }
    }
}
