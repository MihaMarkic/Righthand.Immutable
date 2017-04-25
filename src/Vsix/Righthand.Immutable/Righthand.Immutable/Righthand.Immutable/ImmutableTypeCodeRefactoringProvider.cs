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

            var constructors = typeDecl.ChildNodes().Where(cn => cn.Kind() == SyntaxKind.ConstructorDeclaration).Cast< ConstructorDeclarationSyntax>().ToArray();
            if (constructors.Length != 1)
            {
                return;
            }
            var constructor = constructors[0];
            if (constructor == null || constructor.ParameterList.Parameters.Count == 0)
            {
                return;
            }

            var action = CodeAction.Create("Implement immutable type", c => ImplementImmutableTypeAsync(context.Document, typeDecl, constructor, c));

            context.RegisterRefactoring(action);
        }

        private BlockSyntax CreateConstructorBody(IEnumerable<ParameterDefinition> parameters)
        {
            SyntaxList<StatementSyntax> statements = new SyntaxList<StatementSyntax>();
            foreach (var parameter in parameters)
            {
                var assignment =
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(Common.PascalCasing(parameter.Name)),
                                SyntaxFactory.IdentifierName(parameter.Name)));
                statements = statements.Add(assignment);
            }
            BlockSyntax newBody = SyntaxFactory.Block(statements);
            return newBody;
        }

        private SyntaxList<MemberDeclarationSyntax> CreateProperties(IEnumerable<ParameterDefinition> parameters)
        {
            var result = SyntaxFactory.List<MemberDeclarationSyntax>();
            foreach (var parameter in parameters)
            {
                string name = Common.PascalCasing(parameter.Name);
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

        /// <summary>
        /// Collects all custom members that are persistent.
        /// </summary>
        /// <param name="members"></param>
        /// <returns></returns>
        /// <remarks>All properties that have a getter body/no setter and all methods not named Clone.</remarks>
        private MemberDeclarationSyntax[] GetCustomMembers(SyntaxList<MemberDeclarationSyntax> members)
        {
            List<MemberDeclarationSyntax> result = new List<MemberDeclarationSyntax>(members.Count);
            foreach (var member in members)
            {
                switch(member.Kind())
                {
                    case SyntaxKind.PropertyDeclaration:
                        var propertyDeclaration = (PropertyDeclarationSyntax)member;
                        // include lambda gettters
                        if (propertyDeclaration.ExpressionBody != null)
                        {
                            result.Add(member);
                        }
                        else
                        {
                            var getter = propertyDeclaration.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)).SingleOrDefault();
                            bool isAbstract = propertyDeclaration.Modifiers.Any(m => m.Kind() == SyntaxKind.AbstractKeyword);
                            // include getters with body but only if no setter

                            if (isAbstract || (getter?.DescendantNodes().Any() ?? false))
                            {
                                var setter = propertyDeclaration.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)).SingleOrDefault();
                                if (setter == null)
                                {
                                    result.Add(member);
                                }
                            }
                        }
                        break;
                    case SyntaxKind.MethodDeclaration:
                        var methodDeclaration = (MethodDeclarationSyntax)member;
                        // include methods that aren't named Clone
                        if (!string.Equals(methodDeclaration.Identifier.Text, "Clone", System.StringComparison.Ordinal))
                        {
                            result.Add(member);
                        }
                        break;
                }
            }
            return result.ToArray();
        }

        private MethodDeclarationSyntax CreateCloneMethod(string typeName, IEnumerable<ParameterDefinition> parameters)
        {
            string arguments = string.Join(", ", parameters.Select(p => $"Param<{p.Type}>? {p.Name} = null"));
            string constructorArguments = string.Join(",\n", parameters.Select(p => p.Name)
                .Select(n => $"{n}.HasValue ? {n}.Value.Value : {Common.PascalCasing(n)}"));
            string code = $@"return new {typeName}({constructorArguments});";
            var methodArugmentsList = SyntaxFactory.ParseParameterList($"({arguments})");
            var x = SyntaxFactory.MethodDeclaration(SyntaxFactory.IdentifierName(typeName), "Clone")
                .WithParameterList(methodArugmentsList)
                .WithModifiers(SyntaxTokenList.Create(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement(code)));
            return x;
        }

        private ParameterDefinition[] CollectBaseTypeConstructorParameters(INamedTypeSymbol typeSymbol)
        {
            var baseType = typeSymbol.BaseType;
            if (baseType.SpecialType != SpecialType.System_Object && baseType.Constructors.Length == 1)
            {
                var baseTypeConstructor = baseType.Constructors[0];
                return baseTypeConstructor.Parameters.Select(p => new ParameterDefinition(true, SyntaxFactory.ParseTypeName(p.Type.ToString()), p.Name)).ToArray();
            }
            return null;
        }

        private async Task<Document> ImplementImmutableTypeAsync(Document document, TypeDeclarationSyntax typeDecl, ConstructorDeclarationSyntax constructor,
            CancellationToken cancellationToken)
        {
            var semanticModelTask = document.GetSemanticModelAsync(cancellationToken);
            var rootTask = document.GetSyntaxRootAsync();
            ClassDeclarationSyntax cds = typeDecl as ClassDeclarationSyntax;
            StructDeclarationSyntax sds = typeDecl as StructDeclarationSyntax;
            List<ParameterDefinition> parameters = new List<ParameterDefinition>();

            var semanticModel = await semanticModelTask;
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
            var baseTypeConstructorParameters = CollectBaseTypeConstructorParameters(typeSymbol);
            bool hasBaseType = baseTypeConstructorParameters != null;
            if (hasBaseType)
            {
                parameters.AddRange(baseTypeConstructorParameters);
            }
            var typeParametersQuery = from p in constructor.ParameterList.Parameters
                                      where !parameters.Where(pb => pb.IsDefinedInBaseType).Any(pb => string.Equals(p.Identifier.Text, pb.Name))
                                      let typeSyntax = SyntaxFactory.ParseTypeName(p.Type.ToString())
                                      select new ParameterDefinition(false, typeSyntax, p.Identifier.Text);
            parameters.InsertRange(0, typeParametersQuery);

            BlockSyntax newBody = CreateConstructorBody(parameters.Where(p => !p.IsDefinedInBaseType));
            var constructorParameters = SyntaxFactory.ParseParameterList($"({string.Join(", ", parameters.Select(p => p.Text))})");

            ConstructorDeclarationSyntax newConstructor;
            if (hasBaseType)
            {
                var argumentsText = parameters.Where(p => p.IsDefinedInBaseType).Select(p => p.Name);
                var argumentList = SyntaxFactory.ParseArgumentList($"({string.Join(", ", argumentsText)})");
                var initializer = SyntaxFactory.ConstructorInitializer(SyntaxKind.BaseConstructorInitializer, argumentList);
                newConstructor = constructor.WithBody(newBody)
                    .WithParameterList(constructorParameters)
                    .WithInitializer(initializer);
            }
            else
            {
                newConstructor = constructor.WithBody(newBody);
            }
            
            var newMembers = CreateProperties(parameters.Where(p => !p.IsDefinedInBaseType));
            newMembers = newMembers.Add(newConstructor);
            string typeIdentifierText = cds != null ? cds.Identifier.Text: sds.Identifier.Text;
            bool isTypeAbstract = typeDecl.Modifiers.Any(m => m.Kind() == SyntaxKind.AbstractKeyword);
            if (!isTypeAbstract)
            {
                var cloneMethod = CreateCloneMethod(typeIdentifierText, parameters);
                newMembers = newMembers.Add(cloneMethod);
            }
            var customMembers = GetCustomMembers(typeDecl.Members);
            newMembers = newMembers.AddRange(customMembers);
            CompilationUnitSyntax newRoot;
            var root = await rootTask;
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