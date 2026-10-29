using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Parse source as data. Never load, build or execute proposed upstream code.
var request = JsonSerializer.Deserialize<Request>(Console.In.ReadToEnd())!;
try {
    var shared = Snapshot.Read(request.Shared);
    var fork = Snapshot.Read(request.Fork);
    var incoming = Snapshot.Read(request.Incoming);
    var merged = Snapshot.Read(request.Merged);
    var protectedIds = new HashSet<string>();
    var references = new HashSet<string>();
    foreach (var (id, member) in fork.Members) {
        if (!shared.Members.TryGetValue(id, out var original) || original.Tokens != member.Tokens) {
            protectedIds.Add(id);
            references.UnionWith(member.References);
        }
    }
    foreach (var (id, member) in shared.Members) {
        if (!fork.Members.ContainsKey(id)) references.UnionWith(member.References);
    }
    // Protect transitive calls/data references too, including through other files.
    // Name matching is deliberately conservative across overloads and types.
    bool added;
    do {
        added = false;
        foreach (var (id, member) in fork.Members) {
            if (member.Names.Any(references.Contains) && protectedIds.Add(id)) {
                references.UnionWith(member.References);
                added = true;
            }
        }
    } while (added);
    var protectedNames = protectedIds.SelectMany(id => fork.Members[id].Names).ToHashSet();

    var reasons = new List<string>();
    foreach (var path in request.Paths) {
        if (!shared.Files.TryGetValue(path, out var before) || !incoming.Files.TryGetValue(path, out var after)
            || !fork.Files.ContainsKey(path) || !merged.Files.ContainsKey(path)) {
            reasons.Add($"{path}: source addition/removal needs review.");
            continue;
        }
        if (before.Shell != after.Shell || before.Directives != after.Directives
            || fork.Files[path].Shell != merged.Files[path].Shell
            || fork.Files[path].Directives != merged.Files[path].Directives) {
            reasons.Add($"{path}: imports, type declarations, attributes or preprocessor directives changed.");
        }
        if (before.NativeInterop || after.NativeInterop || fork.Files[path].NativeInterop || merged.Files[path].NativeInterop) {
            reasons.Add($"{path}: source imports native client structures; method-body changes need review.");
        }
        CheckMembers(shared, incoming, path, false);
        CheckMembers(fork, merged, path, true);
    }
    var eligible = fork.Members.Where(entry => entry.Value.Method && !entry.Value.Unsafe
        && !protectedIds.Contains(entry.Key) && !entry.Value.References.Any(protectedNames.Contains))
        .Select(entry => entry.Value.Label).Distinct().Order().ToArray();
    Console.Write(JsonSerializer.Serialize(new { reasons = reasons.Distinct(), protected_members = protectedIds.Count, eligible_members = eligible }));

    void CheckMembers(Snapshot before, Snapshot after, string path, bool checkProtection) {
        var conditionDataNames = fork.Members.Where(entry => entry.Key.StartsWith(path + "|", StringComparison.Ordinal)
            && (entry.Key.Contains("|FieldDeclaration|", StringComparison.Ordinal) || entry.Key.Contains("|PropertyDeclaration|", StringComparison.Ordinal)))
            .SelectMany(entry => entry.Value.Names).ToHashSet();
        foreach (var id in before.Members.Keys.Union(after.Members.Keys).Where(id => id.StartsWith(path + "|", StringComparison.Ordinal))) {
            if (!before.Members.TryGetValue(id, out var old) || !after.Members.TryGetValue(id, out var current)) {
                reasons.Add($"{path}: member added, removed or signature changed ({id.Split('|')[1]}).");
                continue;
            }
            if (old.Tokens == current.Tokens) continue;
            // Untouched conditions are an established routine category. Their
            // ordinary evaluation override may change, but contracts, unsafe
            // code and references to other protected members still may not.
            var routineCondition = request.RoutineConditions?.Contains(path) == true && old.ConditionEvaluation && current.ConditionEvaluation;
            if (checkProtection && protectedIds.Contains(id) && !routineCondition) {
                reasons.Add($"{path}: protected fork integration or transitive dependency changed ({old.Label}).");
            }
            if (old.Signature != current.Signature || !old.Method || !current.Method || old.Unsafe || current.Unsafe) {
                reasons.Add($"{path}: contract, saved data, initializer, constructor or native/unsafe member changed ({old.Label}).");
            }
            var protectedReferences = current.References.Concat(old.References).Where(name => protectedNames.Contains(name)
                && !(routineCondition && (name == "EvaluateCondition" || conditionDataNames.Contains(name)
                    || (name == "Get" && old.GetTokens > 0 && old.GetTokens == current.GetTokens && old.GetCalls == current.GetCalls)))).Distinct().ToArray();
            if (protectedReferences.Length != 0) {
                reasons.Add($"{path}: changed member references protected fork data/code ({old.Label}; references: {string.Join(", ", protectedReferences)}).");
            }
        }
    }
}
catch (Exception error) {
    Console.Write(JsonSerializer.Serialize(new { reasons = new[] { "Syntax review failed closed: " + error.Message } }));
}

record Request(Dictionary<string, string> Shared, Dictionary<string, string> Fork,
    Dictionary<string, string> Incoming, Dictionary<string, string> Merged, string[] Paths, string[]? RoutineConditions = null);
record Member(string Label, string Tokens, string Signature, string[] Names, string[] References, bool Method, bool Unsafe, bool ConditionEvaluation, string GetCalls, int GetTokens);
record FileShape(string Shell, string Directives, bool NativeInterop);

sealed class Snapshot {
    public Dictionary<string, Member> Members { get; } = [];
    public Dictionary<string, FileShape> Files { get; } = [];

    private static string Tokens(IEnumerable<SyntaxToken> tokens) => string.Join('\u001f', tokens.Select(t => $"{t.RawKind}:{t.Text}"));

    public static Snapshot Read(Dictionary<string, string> sources) {
        var result = new Snapshot();
        foreach (var (path, source) in sources) {
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) throw new InvalidOperationException("Cannot parse " + path);
            var root = tree.GetRoot();
            var members = root.DescendantNodes().OfType<MemberDeclarationSyntax>()
                .Where(m => m is not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax).ToArray();
            var shell = root.DescendantTokens().Where(t => !members.Any(m => m.Span.Contains(t.Span)));
            var directives = root.DescendantTrivia(descendIntoTrivia: true)
                .Where(t => t.GetStructure() is DirectiveTriviaSyntax || t.IsKind(SyntaxKind.DisabledTextTrivia)).Select(t => t.ToString());
            var nativeInterop = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Any(u => u.Name?.DescendantTokens().Any(t => t.ValueText is "FFXIVClientStructs" or "InteropServices") == true)
                || root.DescendantNodes().OfType<AttributeSyntax>()
                    .Any(a => a.Name.DescendantTokens().Any(t => t.ValueText is "DllImport" or "LibraryImport" or "UnmanagedCallersOnly"));
            result.Files.Add(path, new FileShape(Tokens(shell), string.Join('\n', directives), nativeInterop));
            foreach (var node in members) {
                string[] names = node switch {
                    MethodDeclarationSyntax m => [m.Identifier.ValueText],
                    ConstructorDeclarationSyntax c => [c.Identifier.ValueText],
                    PropertyDeclarationSyntax p => [p.Identifier.ValueText],
                    FieldDeclarationSyntax f => f.Declaration.Variables.Select(v => v.Identifier.ValueText).ToArray(),
                    EventFieldDeclarationSyntax e => e.Declaration.Variables.Select(v => v.Identifier.ValueText).ToArray(),
                    _ => [node.Kind().ToString()],
                };
                var ancestors = node.Ancestors().Reverse().Select(a => a switch {
                    BaseNamespaceDeclarationSyntax n => n.Name.ToString(),
                    BaseTypeDeclarationSyntax t => t.Identifier.ValueText,
                    _ => null,
                }).Where(n => n is not null);
                var label = string.Join('.', ancestors.Concat(names));
                var signatureNode = node is MethodDeclarationSyntax method
                    ? method.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default) : node;
                var signature = Tokens(signatureNode.DescendantTokens());
                var key = path + "|" + label + "|" + node.Kind() + "|" + signature;
                var references = node.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText).Distinct().ToArray();
                var unsafeMember = node.DescendantTokens().Concat(node.Ancestors().OfType<TypeDeclarationSyntax>().SelectMany(t => t.Modifiers))
                    .Any(t => t.IsKind(SyntaxKind.UnsafeKeyword));
                var conditionEvaluation = node is MethodDeclarationSyntax evaluation && evaluation.Identifier.ValueText == "EvaluateCondition"
                    && evaluation.Modifiers.Any(t => t.IsKind(SyntaxKind.OverrideKeyword)) && evaluation.ParameterList.Parameters.Count == 0;
                var getCalls = node.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(call => call.Expression switch {
                    MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText == "Get",
                    MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText == "Get",
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "Get",
                    _ => false,
                }).Select(call => Tokens(call.DescendantTokens()));
                var getTokens = node.DescendantTokens().Count(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == "Get");
                if (!result.Members.TryAdd(key, new Member(label, Tokens(node.DescendantTokens()), signature, names, references, node is MethodDeclarationSyntax, unsafeMember, conditionEvaluation, string.Join('\n', getCalls), getTokens))) {
                    throw new InvalidOperationException("Ambiguous member in " + path);
                }
            }
        }
        return result;
    }
}
