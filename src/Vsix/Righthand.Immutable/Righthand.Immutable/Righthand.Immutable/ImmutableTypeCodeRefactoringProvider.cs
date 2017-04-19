using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Righthand.Immutable
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(ImmutableTypeCodeRefactoringProvider)), Shared]
    internal class ImmutableTypeCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // Find the node at the selection.
            var node = root.FindNode(context.Span);

            var typeDecl = node as TypeDeclarationSyntax;
            if (typeDecl == null)
            {
                return;
            }

            if (!(node is ClassDeclarationSyntax) && !(node is StructDeclarationSyntax))
            {
                return;
            }

            var constructor = (ConstructorDeclarationSyntax)typeDecl.ChildNodes().SingleOrDefault(cn => cn.Kind() == SyntaxKind.ConstructorDeclaration);
            if (constructor == null || constructor.ParameterList.Parameters.Count == 0)
            {
                return;
            }

            var action = CodeAction.Create("Implement immutable type", c => ImplementImmutableTypeAsync(context.Document, typeDecl, constructor, c));

            context.RegisterRefactoring(action);
        }

        private string PascalCasing(string name)
        {
            return char.ToUpper(name[0]) + name.Substring(1);
        }

        private BlockSyntax CreateConstructorBody(IEnumerable<ParameterSyntax> parameters)
        {
            SyntaxList<StatementSyntax> statements = new SyntaxList<StatementSyntax>();
            foreach (var parameter in parameters)
            {
                var assignment =
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(PascalCasing(parameter.Identifier.Text)),
                                SyntaxFactory.IdentifierName(parameter.Identifier.Text)));
                statements = statements.Add(assignment);
            }
            BlockSyntax newBody = SyntaxFactory.Block(statements);
            return newBody;
        }

        private SyntaxList<MemberDeclarationSyntax> CreateProperties(IEnumerable<ParameterSyntax> parameters)
        {
            var result = SyntaxFactory.List<MemberDeclarationSyntax>();
            foreach (var parameter in parameters)
            {
                string name = PascalCasing(parameter.Identifier.Text);
                var newProperty = SyntaxFactory.PropertyDeclaration(parameter.Type, name)
                    .WithAccessorList(
                        SyntaxFactory.AccessorList(
                            SyntaxFactory.List(
                                new[]{
                                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                                }
                            )
                        )
                    )
                    .WithModifiers(SyntaxTokenList.Create(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));
                result = result.Add(newProperty);
            }
            return result;
        }

        private MethodDeclarationSyntax CreateCloneMethod(string typeName, IEnumerable<ParameterSyntax> parameters)
        {
            string arguments = string.Join(", ", parameters.Select(p => $"Param<{p.Type}>? {p.Identifier.Text} = null"));
            string constructorArguments = string.Join(",\n", parameters.Select(p => p.Identifier.Text)
                .Select(n => $"{n}.HasValue ? {n}.Value.Value : {PascalCasing(n)}"));
            string code = $@"return new {typeName}({constructorArguments});";
            var methodArugmentsList = SyntaxFactory.ParseParameterList($"({arguments})");
            var x = SyntaxFactory.MethodDeclaration(SyntaxFactory.IdentifierName(typeName), "Clone")
                .WithParameterList(methodArugmentsList)
                .WithModifiers(SyntaxTokenList.Create(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement(code)));
            return x;
        }

        private async Task<Document> ImplementImmutableTypeAsync(Document document, TypeDeclarationSyntax typeDecl, ConstructorDeclarationSyntax constructor,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync();
            ClassDeclarationSyntax cds = typeDecl as ClassDeclarationSyntax;
            StructDeclarationSyntax sds = typeDecl as StructDeclarationSyntax;

            BlockSyntax newBody = CreateConstructorBody(constructor.ParameterList.Parameters);
            var newConstructor = constructor.WithBody(newBody);
            var newMembers = CreateProperties(constructor.ParameterList.Parameters);
            newMembers = newMembers.Add(newConstructor);
            string typeIdentifierText = cds != null ? cds.Identifier.Text: sds.Identifier.Text;
            var cloneMethod = CreateCloneMethod(typeIdentifierText, constructor.ParameterList.Parameters);
            newMembers = newMembers.Add(cloneMethod);
            CompilationUnitSyntax newRoot;
            if (cds != null)
            {
                newRoot = (CompilationUnitSyntax)root.ReplaceNode(typeDecl, cds.WithMembers(newMembers));
            }
            else
            {
                newRoot = (CompilationUnitSyntax)root.ReplaceNode(typeDecl, sds.WithMembers(newMembers));
            }
            bool hasImmutableNamespace = newRoot.Usings
                .Where(u => u.Name.Kind() == SyntaxKind.QualifiedName)
                .Where(n => n.Name.ToFullString() == "Righthand.Immutable").Any();
            if (!hasImmutableNamespace)
            {
                newRoot = newRoot.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Righthand.Immutable")));
            }

            var newDocument = document.WithSyntaxRoot(newRoot);

            return newDocument;
        }
    }
}