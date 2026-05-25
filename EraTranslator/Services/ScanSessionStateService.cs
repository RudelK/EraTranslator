using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EraTranslator.Services;

public sealed class ScanSessionStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string GetStateFilePath(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return string.Empty;
        }

        return Path.Combine(gameDirectory, ".era-translator", "last-scan-session.json");
    }

    public void Save(ScanSession session, string? stateRootDirectory = null)
    {
        var rootDirectory = string.IsNullOrWhiteSpace(stateRootDirectory) ? session.GameRoot : stateRootDirectory;
        var path = GetStateFilePath(rootDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var state = new ScanSessionState
        {
            GameRoot = session.GameRoot,
            JosaPackageInfo = new JosaSupportPackageInfoState
            {
                ErbExists = session.JosaPackageInfo.ErbExists,
                ErhExists = session.JosaPackageInfo.ErhExists,
                ErbPath = session.JosaPackageInfo.ErbPath,
                ErhPath = session.JosaPackageInfo.ErhPath,
                HasFunctionSignatures = session.JosaPackageInfo.HasFunctionSignatures,
                HasMacroDefines = session.JosaPackageInfo.HasMacroDefines,
                SupportsLBatchimRoroException = session.JosaPackageInfo.SupportsLBatchimRoroException,
                SupportsImplicitYiFallback = session.JosaPackageInfo.SupportsImplicitYiFallback,
                SupportsParticlePassThrough = session.JosaPackageInfo.SupportsParticlePassThrough,
                SupportsMacroDefines = session.JosaPackageInfo.SupportsMacroDefines,
                HasErhIncludeLinkage = session.JosaPackageInfo.HasErhIncludeLinkage,
                SupportedParticles = session.JosaPackageInfo.SupportedParticles.ToList(),
            },
            Documents = session.Documents.Values
                .Select(document => new SourceFileDocumentState
                {
                    DocumentId = document.DocumentId,
                    FullPath = document.FullPath,
                    RelativePath = document.RelativePath,
                    FileType = document.FileType,
                    OriginalText = document.OriginalText,
                    EncodingInfo = new DetectedEncodingInfoState
                    {
                        CodePage = document.EncodingInfo.Encoding.CodePage,
                        Name = document.EncodingInfo.Name,
                        Kind = document.EncodingInfo.Kind,
                        HasBom = document.EncodingInfo.HasBom,
                    },
                    NewLineSequence = document.NewLineSequence,
                    CsvKind = document.CsvKind,
                    Segments = document.Segments
                        .Select(segment => new TextSegmentState
                        {
                            SegmentId = segment.SegmentId,
                            DocumentId = segment.DocumentId,
                            SegmentType = segment.SegmentType,
                            AbsoluteStart = segment.AbsoluteStart,
                            Length = segment.Length,
                            LineNumber = segment.LineNumber,
                            OriginalText = segment.OriginalText,
                            FieldIndex = segment.FieldIndex,
                            SourceKey = segment.SourceKey,
                            CsvFieldRole = segment.CsvFieldRole,
                            SymbolNamespace = segment.SymbolNamespace,
                            OriginalSymbolKey = segment.OriginalSymbolKey,
                            IsReferenceBearingKey = segment.IsReferenceBearingKey,
                        })
                        .ToList(),
                    SymbolReferences = document.SymbolReferences
                        .Select(reference => new ErbSymbolReferenceState
                        {
                            DocumentId = reference.DocumentId,
                            Namespace = reference.Namespace,
                            Kind = reference.Kind,
                            ResolutionKind = reference.ResolutionKind,
                            OriginalKey = reference.OriginalKey,
                            VariableName = reference.VariableName,
                            ExpressionText = reference.ExpressionText,
                            AbsoluteStart = reference.AbsoluteStart,
                            Length = reference.Length,
                            LineNumber = reference.LineNumber,
                            CandidateKeys = reference.CandidateKeys.ToList(),
                        })
                        .ToList(),
                    VariableLiteralOccurrences = document.VariableLiteralOccurrences
                        .Select(occurrence => new ErbVariableLiteralOccurrenceState
                        {
                            DocumentId = occurrence.DocumentId,
                            VariableName = occurrence.VariableName,
                            LiteralValue = occurrence.LiteralValue,
                            AbsoluteStart = occurrence.AbsoluteStart,
                            Length = occurrence.Length,
                            LineNumber = occurrence.LineNumber,
                            IsExactValue = occurrence.IsExactValue,
                        })
                        .ToList(),
                    ScanWarnings = document.ScanWarnings.ToList(),
                    JosaAnalysis = new JosaDocumentAnalysisState
                    {
                        PatternCount = document.JosaAnalysis.PatternCount,
                        AutoConvertibleCount = document.JosaAnalysis.AutoConvertibleCount,
                        GenericFunctionCount = document.JosaAnalysis.GenericFunctionCount,
                        MacroPatternCount = document.JosaAnalysis.MacroPatternCount,
                        LegacyShorthandCount = document.JosaAnalysis.LegacyShorthandCount,
                        RequiresErh = document.JosaAnalysis.RequiresErh,
                        ErhLinked = document.JosaAnalysis.ErhLinked,
                        SyntaxType = document.JosaAnalysis.SyntaxType,
                        ErhLinkStatus = document.JosaAnalysis.ErhLinkStatus,
                        PackageCompatibilityStatus = document.JosaAnalysis.PackageCompatibilityStatus,
                    },
                })
                .ToList(),
            Items = session.Items
                .Select(item => new ExtractedTextItemState
                {
                    SegmentId = item.SegmentId,
                    DocumentId = item.DocumentId,
                    FileType = item.FileType,
                    RelativePath = item.RelativePath,
                    EncodingName = item.EncodingName,
                    SegmentType = item.SegmentType,
                    LineNumber = item.LineNumber,
                    OriginalText = item.OriginalText,
                    SourceKey = item.SourceKey,
                    FieldIndex = item.FieldIndex,
                    CsvFieldRole = item.CsvFieldRole,
                    SymbolNamespace = item.SymbolNamespace,
                    OriginalSymbolKey = item.OriginalSymbolKey,
                    IsReferenceBearingKey = item.IsReferenceBearingKey,
                    ReferenceImpactCount = item.ReferenceImpactCount,
                    RequiresReferenceRewrite = item.RequiresReferenceRewrite,
                    ReferenceResolutionStatus = item.ReferenceResolutionStatus,
                    WarningText = item.WarningText,
                })
                .ToList(),
            Metrics = new Dictionary<string, int>(session.Metrics),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    public ScanSession? Load(string gameDirectory)
    {
        var path = GetStateFilePath(gameDirectory);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<ScanSessionState>(json, JsonOptions);
            return state is null ? null : Restore(state);
        }
        catch
        {
            return null;
        }
    }

    public void Delete(string gameDirectory)
    {
        var path = GetStateFilePath(gameDirectory);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static ScanSession Restore(ScanSessionState state)
    {
        var analyzer = new SymbolReferenceAnalyzer();
        var session = new ScanSession
        {
            GameRoot = state.GameRoot,
            JosaPackageInfo = new JosaSupportPackageInfo
            {
                ErbExists = state.JosaPackageInfo.ErbExists,
                ErhExists = state.JosaPackageInfo.ErhExists,
                ErbPath = state.JosaPackageInfo.ErbPath,
                ErhPath = state.JosaPackageInfo.ErhPath,
                HasFunctionSignatures = state.JosaPackageInfo.HasFunctionSignatures,
                HasMacroDefines = state.JosaPackageInfo.HasMacroDefines,
                SupportsLBatchimRoroException = state.JosaPackageInfo.SupportsLBatchimRoroException,
                SupportsImplicitYiFallback = state.JosaPackageInfo.SupportsImplicitYiFallback,
                SupportsParticlePassThrough = state.JosaPackageInfo.SupportsParticlePassThrough,
                SupportsMacroDefines = state.JosaPackageInfo.SupportsMacroDefines,
                HasErhIncludeLinkage = state.JosaPackageInfo.HasErhIncludeLinkage,
                SupportedParticles = state.JosaPackageInfo.SupportedParticles,
            },
        };

        foreach (var documentState in state.Documents)
        {
            var document = new SourceFileDocument
            {
                DocumentId = documentState.DocumentId,
                FullPath = documentState.FullPath,
                RelativePath = documentState.RelativePath,
                FileType = documentState.FileType,
                OriginalText = documentState.OriginalText,
                EncodingInfo = new DetectedEncodingInfo
                {
                    Encoding = TryGetEncoding(documentState.EncodingInfo.CodePage),
                    Name = documentState.EncodingInfo.Name,
                    Kind = documentState.EncodingInfo.Kind,
                    HasBom = documentState.EncodingInfo.HasBom,
                },
                NewLineSequence = documentState.NewLineSequence,
                CsvKind = documentState.CsvKind,
                JosaAnalysis = new JosaDocumentAnalysis
                {
                    PatternCount = documentState.JosaAnalysis.PatternCount,
                    AutoConvertibleCount = documentState.JosaAnalysis.AutoConvertibleCount,
                    GenericFunctionCount = documentState.JosaAnalysis.GenericFunctionCount,
                    MacroPatternCount = documentState.JosaAnalysis.MacroPatternCount,
                    LegacyShorthandCount = documentState.JosaAnalysis.LegacyShorthandCount,
                    RequiresErh = documentState.JosaAnalysis.RequiresErh,
                    ErhLinked = documentState.JosaAnalysis.ErhLinked,
                    SyntaxType = documentState.JosaAnalysis.SyntaxType,
                    ErhLinkStatus = documentState.JosaAnalysis.ErhLinkStatus,
                    PackageCompatibilityStatus = documentState.JosaAnalysis.PackageCompatibilityStatus,
                },
            };

            document.Segments.AddRange(documentState.Segments.Select(segment => new TextSegment
            {
                SegmentId = segment.SegmentId,
                DocumentId = segment.DocumentId,
                SegmentType = segment.SegmentType,
                AbsoluteStart = segment.AbsoluteStart,
                Length = segment.Length,
                LineNumber = segment.LineNumber,
                OriginalText = segment.OriginalText,
                FieldIndex = segment.FieldIndex,
                SourceKey = segment.SourceKey,
                CsvFieldRole = segment.CsvFieldRole,
                SymbolNamespace = segment.SymbolNamespace,
                OriginalSymbolKey = segment.OriginalSymbolKey,
                IsReferenceBearingKey = segment.IsReferenceBearingKey,
            }));
            document.SymbolReferences.AddRange(documentState.SymbolReferences.Select(reference => new ErbSymbolReference
            {
                DocumentId = reference.DocumentId,
                Namespace = reference.Namespace,
                Kind = reference.Kind,
                ResolutionKind = reference.ResolutionKind,
                OriginalKey = reference.OriginalKey,
                VariableName = reference.VariableName,
                ExpressionText = reference.ExpressionText,
                AbsoluteStart = reference.AbsoluteStart,
                Length = reference.Length,
                LineNumber = reference.LineNumber,
                CandidateKeys = reference.CandidateKeys.ToList(),
            }));
            document.VariableLiteralOccurrences.AddRange(documentState.VariableLiteralOccurrences.Select(occurrence => new ErbVariableLiteralOccurrence
            {
                DocumentId = occurrence.DocumentId,
                VariableName = occurrence.VariableName,
                LiteralValue = occurrence.LiteralValue,
                AbsoluteStart = occurrence.AbsoluteStart,
                Length = occurrence.Length,
                LineNumber = occurrence.LineNumber,
                IsExactValue = occurrence.IsExactValue,
            }));
            document.ScanWarnings.AddRange(documentState.ScanWarnings);
            session.Documents[document.DocumentId] = document;
        }

        session.Items.AddRange(state.Items.Select(item => new ExtractedTextItem
        {
            SegmentId = item.SegmentId,
            DocumentId = item.DocumentId,
            FileType = item.FileType,
            RelativePath = item.RelativePath,
            EncodingName = item.EncodingName,
            SegmentType = item.SegmentType,
            LineNumber = item.LineNumber,
            OriginalText = item.OriginalText,
            SourceKey = item.SourceKey,
            FieldIndex = item.FieldIndex,
            CsvFieldRole = item.CsvFieldRole,
            SymbolNamespace = item.SymbolNamespace,
            OriginalSymbolKey = item.OriginalSymbolKey,
            IsReferenceBearingKey = item.IsReferenceBearingKey,
            ReferenceImpactCount = item.ReferenceImpactCount,
            RequiresReferenceRewrite = item.RequiresReferenceRewrite,
            ReferenceResolutionStatus = item.ReferenceResolutionStatus,
            WarningText = item.WarningText,
        }));

        foreach (var metric in state.Metrics)
        {
            session.Metrics[metric.Key] = metric.Value;
        }

        analyzer.Analyze(session);
        return session;
    }

    private static Encoding TryGetEncoding(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch
        {
            return new UTF8Encoding(true);
        }
    }
}
