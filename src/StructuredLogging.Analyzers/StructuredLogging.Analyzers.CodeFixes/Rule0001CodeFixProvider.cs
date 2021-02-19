using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace StructuredLogging.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Rule0001CodeFixProvider)), Shared]
    public class Rule0001CodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(StructuredLoggingAnalyzer.Rule0001Id);

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root!.FindToken(diagnosticSpan.Start).Parent!.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.Rule0001CodeFixTitle,
                    createChangedDocument: c => ToStructuredLoggingAsync(context.Document, declaration, c),
                    equivalenceKey: nameof(CodeFixResources.Rule0001CodeFixTitle)),
                diagnostic);
        }

        private async Task<Document> ToStructuredLoggingAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var methodSymbol = (IMethodSymbol) semanticModel.GetSymbolInfo(invocation.Expression).Symbol;

            var namedArguments = invocation.ArgumentList.Arguments
                .Where(arg => arg.NameColon != null)
                .ToDictionary(arg => arg.NameColon.Name.Identifier.Text);

            bool TryGetArgument(IParameterSymbol parameter, out ArgumentSyntax argumentSyntax)
            {
                if (namedArguments.TryGetValue(parameter.Name, out argumentSyntax))
                    return true;

                argumentSyntax = invocation.ArgumentList.Arguments[parameter.Ordinal];
                return true;
            }

            var messageParameter = methodSymbol!.Parameters.FirstOrDefault(p => p.Name == "message");
            if (messageParameter == null
                || !TryGetArgument(messageParameter, out var messageArgument)
                || messageArgument.Expression is not InterpolatedStringExpressionSyntax interpolatedString)
                return document;

            var newInvocation = invocation.WithArgumentList(ArgumentList());

            var logLevelParameter = methodSymbol!.Parameters.FirstOrDefault(p => p.Name == "logLevel");
            if (logLevelParameter != null && TryGetArgument(logLevelParameter, out var logLevelArgument))
            {
                newInvocation = newInvocation.AddArgumentListArguments(logLevelArgument);
            }

            var eventIdParameter = methodSymbol!.Parameters.FirstOrDefault(p => p.Name == "eventId");
            if (eventIdParameter != null && TryGetArgument(eventIdParameter, out var eventIdArgument))
            {
                newInvocation = newInvocation.AddArgumentListArguments(eventIdArgument);
            }

            var exceptionParameter = methodSymbol!.Parameters.FirstOrDefault(p => p.Name == "exception");
            if (exceptionParameter != null && TryGetArgument(exceptionParameter, out var exceptionArgument))
            {
                newInvocation = newInvocation.AddArgumentListArguments(exceptionArgument);
            }

            var builder = new StringBuilder();
            var structuredArguments = new List<ArgumentSyntax>();
            int paramCounter = 0;
            foreach (var part in interpolatedString.Contents)
            {
                switch (part)
                {
                    case InterpolatedStringTextSyntax text:
                        builder.Append(text);
                        break;
                    case InterpolationSyntax interpolation:
                        structuredArguments.Add(Argument(interpolation.Expression));
                        var name = interpolation.Expression
                            .DescendantNodesAndSelf()
                            .OfType<IdentifierNameSyntax>()
                            .LastOrDefault()?.Identifier.Text ?? $"param{++paramCounter}";
                        builder.Append("{").Append(name).Append("}");
                        break;
                }
            }

            newInvocation = newInvocation.AddArgumentListArguments(Argument(
                LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(builder.ToString()))));
            
            if (structuredArguments.Any())
            {
                newInvocation = newInvocation.AddArgumentListArguments(structuredArguments.ToArray());
            }

            var oldRoot = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = oldRoot.ReplaceNode(invocation, newInvocation);
            
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
