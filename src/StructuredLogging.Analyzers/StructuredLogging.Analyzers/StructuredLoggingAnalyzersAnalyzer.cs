using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace StructuredLogging.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class StructuredLoggingAnalyzersAnalyzer : DiagnosticAnalyzer
    {
        public const string Rule0001Id = "SLOG0001";
        public const string Rule0002Id = "SLOG0002";
        public const string Rule0003Id = "SLOG0003";
        public const string Rule0004Id = "SLOG0004";

        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule0001 = new (Rule0001Id, 
            new LocalizableResourceString(nameof(Resources.Rule0001Title), Resources.ResourceManager, typeof(Resources)),
            new LocalizableResourceString(nameof(Resources.Rule0001MessageFormat), Resources.ResourceManager, typeof(Resources)), 
            Category, 
            DiagnosticSeverity.Warning, 
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor Rule0002 = new(Rule0002Id,
            new LocalizableResourceString(nameof(Resources.Rule0002Title), Resources.ResourceManager, typeof(Resources)),
            new LocalizableResourceString(nameof(Resources.Rule0002MessageFormat), Resources.ResourceManager, typeof(Resources)),
            Category,
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor Rule0003 = new(Rule0003Id,
            new LocalizableResourceString(nameof(Resources.Rule0003Title), Resources.ResourceManager, typeof(Resources)),
            new LocalizableResourceString(nameof(Resources.Rule0003MessageFormat), Resources.ResourceManager, typeof(Resources)),
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor Rule0004 = new(Rule0004Id,
            new LocalizableResourceString(nameof(Resources.Rule0004Title), Resources.ResourceManager, typeof(Resources)),
            new LocalizableResourceString(nameof(Resources.Rule0004MessageFormat), Resources.ResourceManager, typeof(Resources)),
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule0001, Rule0002, Rule0003, Rule0004);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            //context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.SimpleMemberAccessExpression);
        }

        private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            //var invocation = (InvocationExpressionSyntax) context.Node;
            //if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            //    || !memberAccess.Name.Identifier.Text.StartsWith("Log"))
            //    return;

            var memberAccess = (MemberAccessExpressionSyntax) context.Node;
            if (!memberAccess.Name.Identifier.Text.StartsWith("Log"))
                return;
            
            if (context.SemanticModel.GetSymbolInfo(memberAccess).Symbol is not IMethodSymbol { IsGenericMethod: false } methodSymbol)
                return;
            
            if (context.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger") is not { } loggerSymbol)
                return;

            if ((methodSymbol.ReceiverType as INamedTypeSymbol)?.IsSubtypeOf(loggerSymbol) != true)
                return;

            var invocation = memberAccess.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            var namedArguments = invocation.ArgumentList.Arguments
                .Where(arg => arg.NameColon != null)
                .ToDictionary(arg => arg.NameColon.Name.Identifier.Text);

            var messageArgument = namedArguments.TryGetValue("message", out var msgArg)
                ? msgArg : invocation.ArgumentList.Arguments[
                    methodSymbol.Parameters.IndexOf(methodSymbol.Parameters.Single(p => p.Name == "message"))];
            
            if (messageArgument.Expression is InterpolatedStringExpressionSyntax)
            {
                var diagnostic = Diagnostic.Create(Rule0001, invocation.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
            else if (messageArgument.Expression.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>().Any())
            {
                var diagnostic = Diagnostic.Create(Rule0004, invocation.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }

            if (!methodSymbol.Parameters.Any(x => x.Name == "eventId"))
            {
                var diagnostic = Diagnostic.Create(Rule0002, invocation.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }

            var catchClause = memberAccess.FirstAncestorOrSelf<CatchClauseSyntax>();
            if (catchClause != null && !methodSymbol.Parameters.Any(x => x.Name == "exception"))
            {
                var diagnostic = Diagnostic.Create(Rule0003, invocation.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
