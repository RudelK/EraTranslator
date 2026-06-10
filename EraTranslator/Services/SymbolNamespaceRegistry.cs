namespace EraTranslator.Services;

public sealed class SymbolNamespaceRegistry
{
    private readonly Dictionary<string, string> _canonicalNamespaces = new(StringComparer.OrdinalIgnoreCase);

    public SymbolNamespaceRegistry(IEnumerable<string>? dynamicNamespaces = null)
    {
        foreach (var builtInNamespace in ErbSyntaxCatalog.BuiltInNamespaces)
        {
            AddNamespace(builtInNamespace);
        }

        if (dynamicNamespaces is not null)
        {
            foreach (var dynamicNamespace in dynamicNamespaces)
            {
                AddNamespace(dynamicNamespace);
            }
        }

        OrderedNamespaces = _canonicalNamespaces.Values
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(@namespace => @namespace.Length)
            .ThenBy(@namespace => @namespace, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<string> OrderedNamespaces { get; }

    public bool TryResolveNamespace(string value, out string canonicalNamespace)
    {
        return _canonicalNamespaces.TryGetValue(CanonicalizeNamespace(value), out canonicalNamespace!);
    }

    public string ResolveFileNamespace(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        if (string.Equals(fileName, "GameBase.csv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "VariableSize.csv", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (ErbSyntaxCatalog.BuiltInFileNamespaceByFileName.TryGetValue(fileName, out var builtInNamespace))
        {
            return builtInNamespace;
        }

        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".cvs", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".erd", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var stem = StripErdDimensionSuffix(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrWhiteSpace(stem) || stem.StartsWith('_'))
        {
            return string.Empty;
        }

        return CanonicalizeNamespace(stem);
    }

    public static SymbolNamespaceRegistry CreateFromRelativePaths(IEnumerable<string> relativePaths)
    {
        var dynamicNamespaces = relativePaths
            .Select(path => Default.ResolveFileNamespace(path))
            .Where(@namespace => !string.IsNullOrWhiteSpace(@namespace));
        return new SymbolNamespaceRegistry(dynamicNamespaces);
    }

    public static SymbolNamespaceRegistry CreateFromDocuments(IEnumerable<Models.SourceFileDocument> documents)
    {
        return CreateFromRelativePaths(documents
            .Where(document => DocumentFileTypes.IsCsvLike(document.FileType))
            .Select(document => document.RelativePath));
    }

    public static SymbolNamespaceRegistry CreateFromReferenceMaps(
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string>? stringLookupRenameMap = null)
    {
        var namespaces = renameMap.Keys.Select(key => key.Namespace);
        if (stringLookupRenameMap is not null)
        {
            namespaces = namespaces.Concat(stringLookupRenameMap.Keys.Select(key => key.Namespace));
        }

        return new SymbolNamespaceRegistry(namespaces);
    }

    public static string CanonicalizeNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var buffer = new char[trimmed.Length];
        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            buffer[index] = character is >= 'a' and <= 'z'
                ? char.ToUpperInvariant(character)
                : character;
        }

        return new string(buffer);
    }

    public static SymbolNamespaceRegistry Default { get; } = new();

    private void AddNamespace(string value)
    {
        var canonicalNamespace = CanonicalizeNamespace(value);
        if (string.IsNullOrWhiteSpace(canonicalNamespace))
        {
            return;
        }

        _canonicalNamespaces.TryAdd(canonicalNamespace, canonicalNamespace);
    }

    private static string StripErdDimensionSuffix(string stem)
    {
        var atIndex = stem.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == stem.Length - 1)
        {
            return stem;
        }

        return stem[(atIndex + 1)..].All(char.IsDigit)
            ? stem[..atIndex]
            : stem;
    }
}
