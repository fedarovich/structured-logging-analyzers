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
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace StructuredLogging.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Rule0003CodeFixProvider)), Shared]
    public class Rule0003CodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(StructuredLoggingAnalyzer.Rule0003Id);

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
                    title: CodeFixResources.Rule0003CodeFixTitle,
                    createChangedDocument: c => ToStructuredLoggingAsync(context.Document, declaration, c),
                    equivalenceKey: nameof(CodeFixResources.Rule0003CodeFixTitle)),
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

                var index = methodSymbol.Parameters.IndexOf(parameter);
                if (index < 0 || index >= invocation.ArgumentList.Arguments.Count)
                    return false;

                argumentSyntax = invocation.ArgumentList.Arguments[index];
                return true;
            }

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

            var catchClause = invocation.FirstAncestorOrSelf<CatchClauseSyntax>();
            if (catchClause!.Declaration != null && catchClause.Declaration.Identifier.Kind() != SyntaxKind.None)
            {
                newInvocation = newInvocation.AddArgumentListArguments(
                    Argument(IdentifierName(catchClause!.Declaration.Identifier)));
            }
            else
            {
                newInvocation = newInvocation.AddArgumentListArguments(
                    Argument(IdentifierName("ex")));
            }

            var messageParameter = methodSymbol!.Parameters.FirstOrDefault(p => p.Name == "message");
            if (messageParameter != null && TryGetArgument(messageParameter, out var messageArgument))
            {
                newInvocation = newInvocation.AddArgumentListArguments(messageArgument);
            }

            if (invocation.ArgumentList.Arguments.Count >= methodSymbol.Parameters.Length)
            {
                newInvocation = newInvocation.AddArgumentListArguments(
                    invocation.ArgumentList.Arguments.Skip(methodSymbol.Parameters.Length - 1).ToArray());
            }

            var newCatchClause = catchClause.ReplaceNode(invocation, newInvocation);
            if (newCatchClause.Declaration == null)
            {
                newCatchClause = newCatchClause
                    .WithCatchKeyword(Token(newCatchClause.CatchKeyword.LeadingTrivia, SyntaxKind.CatchKeyword, TriviaList(Space)))
                    .WithDeclaration(CatchDeclaration(IdentifierName("Exception")).WithIdentifier(Identifier("ex")));
            }
            else if (newCatchClause.Declaration.Identifier.Kind() == SyntaxKind.None)
            {
                newCatchClause = newCatchClause
                    .WithDeclaration(newCatchClause.Declaration.WithIdentifier(Identifier("ex")));
            }
            
            var oldRoot = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = oldRoot.ReplaceNode(catchClause, newCatchClause);
            
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
