#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConsoleGraphicsEngine.CliHost {
	public sealed class PreprocessorSymbolOccurrence {
		public string Symbol { get; }
		public string RelativeFile { get; }
		public int Line { get; }
		public string DirectiveKind { get; }
		public string Expression { get; }

		public PreprocessorSymbolOccurrence(string symbol, string relativeFile, int line, string directiveKind, string expression) {
			Symbol = symbol; RelativeFile = relativeFile; Line = line; DirectiveKind = directiveKind; Expression = expression;
		}
	}

	public sealed class RoslynAnalysisReport {
		public List<string> Violations { get; } = new List<string>();
		public List<PreprocessorSymbolOccurrence> SymbolOccurrences { get; } = new List<PreprocessorSymbolOccurrence>();
		public int FilesAnalyzed { get; set; }
		public bool IsClean => Violations.Count == 0;
	}

	/// <summary>
	/// Roslyn syntax-tree static analyzer enforcing:
	/// 1. <c>EngineCore/</c> Unity-portability isolation (zero <c>Console</c>, <c>ConsoleKey</c>, <c>DllImport</c>, <c>Int128</c>/<c>UInt128</c>).
	/// 2. K&amp;R same-line opening braces and Unix <c>LF</c> line endings across all <c>.cs</c> files.
	/// 3. CLI entry point convention (<c>HelpText</c> &amp; <c>PrintHelp</c> declared before <c>Main</c>).
	/// 4. Preprocessor directive extraction and <c>PREPROCESSOR_SYMBOLS.md</c> documentation generation.
	/// </summary>
	public static class RoslynProjectAnalyzer {
		static readonly Dictionary<string, string> KnownSymbolDescriptions = new Dictionary<string, string>(StringComparer.Ordinal) {
			["DEBUG"] = "Standard debug build symbol. Enables deterministic 64-bit FNV-1a StateChecksum, script assert_checksum invariants, and Log output.",
			["KEEP_LOGS"] = "Opt-in symbol to retain ConsoleGraphicsEngine.Logging.Log file/sink output even in Release builds (adapted from ConsoleMrV.Log).",
			["UNITY_5_3_OR_NEWER"] = "Unity Engine compiler symbol (Unity 5.3+). Enables UnityEngineHost MonoBehaviour bridge and UnityNativeVectorBackend OnGUI rendering.",
			["UNITY_2017_1_OR_NEWER"] = "Unity Engine compiler symbol (Unity 2017.1+). Paired with UNITY_5_3_OR_NEWER to guard UnityEngine namespace references."
		};

		static readonly HashSet<string> ForbiddenEngineCoreIdentifiers = new HashSet<string>(StringComparer.Ordinal) {
			"Console", "ConsoleKey", "ConsoleKeyInfo", "ConsoleColor", "ConsoleModifiers",
			"DllImport", "DllImportAttribute", "Int128", "UInt128"
		};

		public static string LocateRepoRoot(string? startDir = null) {
			string dir = startDir ?? AppContext.BaseDirectory;
			while (!string.IsNullOrEmpty(dir)) {
				if (File.Exists(Path.Combine(dir, "ConsoleGraphicsApp.csproj"))) return dir;
				DirectoryInfo? parent = Directory.GetParent(dir);
				if (parent == null) break;
				dir = parent.FullName;
			}
			return Directory.GetCurrentDirectory();
		}

		public static RoslynAnalysisReport AnalyzeProject(string? repoRoot = null) {
			string root = repoRoot ?? LocateRepoRoot();
			var report = new RoslynAnalysisReport();

			string[] csFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
				.Where(p => !IsExcludedPath(root, p))
				.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
				.ToArray();

			// Parse with all conditional combinations so Roslyn parses code inside #if DEBUG and #if UNITY_5_3_OR_NEWER
			var parseDefault = new CSharpParseOptions(LanguageVersion.Latest);
			var parseDebugUnity = new CSharpParseOptions(LanguageVersion.Latest,
				preprocessorSymbols: new[] { "DEBUG", "KEEP_LOGS", "UNITY_5_3_OR_NEWER", "UNITY_2017_1_OR_NEWER" });

			foreach (string filePath in csFiles) {
				report.FilesAnalyzed++;
				string relPath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
				string sourceText = File.ReadAllText(filePath);

				if (sourceText.Contains("\r\n", StringComparison.Ordinal)) {
					report.Violations.Add($"{relPath}: Contains CRLF line endings (Unix LF required).");
				}

				SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceText, parseDebugUnity, filePath);
				SyntaxNode rootNode = tree.GetRoot();

				CheckKrBraces(tree, rootNode, relPath, report);
				CheckCliHelpAtTop(tree, rootNode, relPath, report);
				if (relPath.StartsWith("EngineCore/", StringComparison.OrdinalIgnoreCase)) {
					CheckEngineCoreIsolation(tree, rootNode, relPath, report);
				}

				ExtractPreprocessorSymbols(tree, rootNode, relPath, report);
			}

			return report;
		}

		static bool IsExcludedPath(string root, string fullPath) {
			string rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
			return rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
				   rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
				   rel.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase) ||
				   rel.StartsWith("local/", StringComparison.OrdinalIgnoreCase);
		}

		static void CheckKrBraces(SyntaxTree tree, SyntaxNode rootNode, string relPath, RoslynAnalysisReport report) {
			foreach (SyntaxToken token in rootNode.DescendantTokens()) {
				if (!token.IsKind(SyntaxKind.OpenBraceToken)) continue;
				SyntaxToken prev = token.GetPreviousToken();
				if (prev.IsKind(SyntaxKind.None)) continue;

				// In K&R style, '{' must sit on the exact same line as the token before it,
				// except for multi-line array/collection initializer elements (where previous token is ',' or '{').
				if (prev.IsKind(SyntaxKind.CommaToken) || prev.IsKind(SyntaxKind.OpenBraceToken)) continue;

				int braceLine = tree.GetLineSpan(token.Span).StartLinePosition.Line + 1;
				int prevLine = tree.GetLineSpan(prev.Span).EndLinePosition.Line + 1;
				if (braceLine != prevLine) {
					report.Violations.Add($"{relPath}:{braceLine}: Opening '{{' must be on the same line as '{prev.Text}' (line {prevLine}).");
				}
			}
		}

		static void CheckCliHelpAtTop(SyntaxTree tree, SyntaxNode rootNode, string relPath, RoslynAnalysisReport report) {
			foreach (ClassDeclarationSyntax cls in rootNode.DescendantNodes().OfType<ClassDeclarationSyntax>()) {
				MethodDeclarationSyntax? mainMethod = cls.Members.OfType<MethodDeclarationSyntax>()
					.FirstOrDefault(m => m.Identifier.Text == "Main");
				if (mainMethod == null) continue;

				int mainLine = tree.GetLineSpan(mainMethod.Span).StartLinePosition.Line + 1;
				MethodDeclarationSyntax? printHelp = cls.Members.OfType<MethodDeclarationSyntax>()
					.FirstOrDefault(m => m.Identifier.Text == "PrintHelp");

				if (printHelp == null) {
					report.Violations.Add($"{relPath}:{mainLine}: CLI entry class '{cls.Identifier.Text}' must define a PrintHelp() method.");
				} else {
					int helpLine = tree.GetLineSpan(printHelp.Span).StartLinePosition.Line + 1;
					if (helpLine > mainLine) {
						report.Violations.Add($"{relPath}:{helpLine}: PrintHelp() (line {helpLine}) must be declared above Main() (line {mainLine}).");
					}
				}
			}
		}

		static void CheckEngineCoreIsolation(SyntaxTree tree, SyntaxNode rootNode, string relPath, RoslynAnalysisReport report) {
			foreach (IdentifierNameSyntax id in rootNode.DescendantNodes().OfType<IdentifierNameSyntax>()) {
				string name = id.Identifier.Text;
				if (ForbiddenEngineCoreIdentifiers.Contains(name)) {
					int line = tree.GetLineSpan(id.Span).StartLinePosition.Line + 1;
					report.Violations.Add($"{relPath}:{line}: Forbidden non-portable identifier '{name}' in EngineCore/.");
				}
			}
		}

		static void ExtractPreprocessorSymbols(SyntaxTree tree, SyntaxNode rootNode, string relPath, RoslynAnalysisReport report) {
			foreach (SyntaxTrivia trivia in rootNode.DescendantTrivia(descendIntoTrivia: true)) {
				if (!trivia.HasStructure) continue;
				SyntaxNode? structure = trivia.GetStructure();
				if (structure is IfDirectiveTriviaSyntax ifDir) {
					RecordConditionSymbols(tree, ifDir.Condition, "#if", relPath, report);
				} else if (structure is ElifDirectiveTriviaSyntax elifDir) {
					RecordConditionSymbols(tree, elifDir.Condition, "#elif", relPath, report);
				} else if (structure is DefineDirectiveTriviaSyntax defDir) {
					int line = tree.GetLineSpan(defDir.Span).StartLinePosition.Line + 1;
					report.SymbolOccurrences.Add(new PreprocessorSymbolOccurrence(defDir.Name.Text, relPath, line, "#define", defDir.Name.Text));
				}
			}
		}

		static void RecordConditionSymbols(SyntaxTree tree, ExpressionSyntax condition, string kind, string relPath, RoslynAnalysisReport report) {
			int line = tree.GetLineSpan(condition.Span).StartLinePosition.Line + 1;
			string exprText = condition.ToString();
			foreach (IdentifierNameSyntax id in condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
				string sym = id.Identifier.Text;
				if (sym is "true" or "false") continue;
				report.SymbolOccurrences.Add(new PreprocessorSymbolOccurrence(sym, relPath, line, kind, exprText));
			}
		}

		public static string GeneratePreprocessorMarkdown(RoslynAnalysisReport report) {
			var sb = new StringBuilder();
			sb.Append("# Preprocessor Symbols Reference\n\n");
			sb.Append("Auto-discovered via Roslyn syntax-tree trivia analysis (`CliHost/RoslynProjectAnalyzer.cs`). ");
			sb.Append("Re-generate or verify anytime with `dotnet run -- --doc-symbols` or `dotnet run -- --test`.\n\n");

			sb.Append("## 1. Symbol Catalog & Purpose\n\n");
			sb.Append("| Symbol | Purpose & Build Behavior |\n");
			sb.Append("| :--- | :--- |\n");

			var grouped = report.SymbolOccurrences
				.GroupBy(o => o.Symbol, StringComparer.Ordinal)
				.OrderBy(g => g.Key, StringComparer.Ordinal)
				.ToList();

			foreach (var group in grouped) {
				string desc = KnownSymbolDescriptions.TryGetValue(group.Key, out string? known)
					? known
					: "Conditional compilation symbol.";
				sb.Append($"| `{group.Key}` | {desc} |\n");
			}

			sb.Append("\n## 2. Exact Source Locations\n\n");
			sb.Append("| Symbol | File | Line | Directive | Condition / Expression |\n");
			sb.Append("| :--- | :--- | :--- | :--- | :--- |\n");

			foreach (var occ in report.SymbolOccurrences.OrderBy(o => o.Symbol, StringComparer.Ordinal).ThenBy(o => o.RelativeFile, StringComparer.Ordinal).ThenBy(o => o.Line)) {
				sb.Append($"| `{occ.Symbol}` | `{occ.RelativeFile}` | {occ.Line} | `{occ.DirectiveKind}` | `{occ.Expression}` |\n");
			}

			return sb.ToString();
		}

		public static string WritePreprocessorMarkdownFile(string? repoRoot = null, string relativeOutputPath = "PREPROCESSOR_SYMBOLS.md") {
			string root = repoRoot ?? LocateRepoRoot();
			RoslynAnalysisReport report = AnalyzeProject(root);
			string md = GeneratePreprocessorMarkdown(report).Replace("\r\n", "\n");
			string outPath = Path.Combine(root, relativeOutputPath);
			File.WriteAllText(outPath, md, new UTF8Encoding(false));
			return outPath;
		}
	}
}
