using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Text;
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
            if (constructor == null || (constructor.ParameterList.Parameters.Count == 0 && typeDecl.BaseList == null))
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

        private SyntaxList<MemberDeclarationSyntax> CreateProperties(IEnumerable<ParameterDefinition> parameters, Dictionary<string, SyntaxTriviaList> leadingTrivia)
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
                if (leadingTrivia.TryGetValue(name, out var lt))
                {
                    newProperty = newProperty.WithLeadingTrivia(lt);
                }
                result = result.Add(newProperty);
            }
            return result;
        }

        static Dictionary<string, SyntaxTriviaList> GetNonCustomTrivia(SyntaxList<MemberDeclarationSyntax> members)
        {
            var query = from m in members
                        where m.Kind() == SyntaxKind.PropertyDeclaration
                        let p = (PropertyDeclarationSyntax)m
                        where !IsPropertyCustom(p) && p.HasLeadingTrivia
                        select p;
            return query.ToDictionary(p => p.Identifier.Text, p => p.GetLeadingTrivia());
        }

        static bool IsPropertyCustom(PropertyDeclarationSyntax propertyDeclaration)
        {
            
            if (propertyDeclaration.ExpressionBody != null)
            {
                return true;
            }
            else
            {
                bool isStatic = propertyDeclaration.Modifiers.Any(m => m.Kind() == SyntaxKind.StaticKeyword);
                if (isStatic)
                {
                    return true;
                }
                var getter = propertyDeclaration.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)).SingleOrDefault();
                bool isAbstract = propertyDeclaration.Modifiers.Any(m => m.Kind() == SyntaxKind.AbstractKeyword);
                // include getters with body but only if no setter

                if (isAbstract || (getter?.DescendantNodes().Any() ?? false))
                {
                    var setter = propertyDeclaration.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)).SingleOrDefault();
                    if (setter == null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        /// <summary>
        /// Collects all custom members that are persistent.
        /// </summary>
        /// <param name="members"></param>
        /// <returns></returns>
        /// <remarks>All properties that have a getter body/no setter and all methods not named Clone, Equals or GetHashCode.</remarks>
        MemberDeclarationSyntax[] GetCustomMembers(SyntaxList<MemberDeclarationSyntax> members)
        {
            List<MemberDeclarationSyntax> result = new List<MemberDeclarationSyntax>(members.Count);
            foreach (var member in members)
            {
                switch(member.Kind())
                {
                    case SyntaxKind.PropertyDeclaration:
                        var propertyDeclaration = (PropertyDeclarationSyntax)member;
                        // include lambda getters
                        if (IsPropertyCustom(propertyDeclaration))
                        {
                            result.Add(member);
                        }
                        break;
                    case SyntaxKind.MethodDeclaration:
                        var methodDeclaration = (MethodDeclarationSyntax)member;
                        // include methods that aren't named Clone, Equals and GetHashCode
                        if (!string.Equals(methodDeclaration.Identifier.Text, "Clone", System.StringComparison.Ordinal)
                            && !string.Equals(methodDeclaration.Identifier.Text, "Equals", System.StringComparison.Ordinal)
                            && !string.Equals(methodDeclaration.Identifier.Text, "GetHashCode", System.StringComparison.Ordinal))
                        {
                            result.Add(member);
                        }
                        break;
                }
            }
            return result.ToArray();
        }

        MethodDeclarationSyntax CreateCloneMethod(string typeName, IEnumerable<ParameterDefinition> parameters)
        {
            string arguments = string.Join(", ", parameters.Select(p => $"Param<{p.Type}>? {p.Name} = null"));
            string constructorArguments = string.Join(",\n\t\t\t\t", parameters.Select(p => p.Name)
                .Select(n => $"{n}.HasValue ? {n}.Value.Value : {Common.PascalCasing(n)}"));
            string code = $@"return new {typeName}({constructorArguments});";
            var methodArgumentsList = SyntaxFactory.ParseParameterList($"({arguments})");
            var x = SyntaxFactory.MethodDeclaration(SyntaxFactory.IdentifierName(typeName), "Clone")
                .WithParameterList(methodArgumentsList)
                .WithModifiers(SyntaxTokenList.Create(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement(code)));
            return x;
        }

        MethodDeclarationSyntax CreateEqualsMethod(string typeName, IEnumerable<ParameterDefinition> parameters)
        {
            string compare = string.Join(" && ", parameters.Select(p => $"Equals({Common.PascalCasing(p.Name)}, o.{Common.PascalCasing(p.Name)})"));
            string code =
$@"         if (obj == null || GetType() != obj.GetType()) return false;
            var o = ({typeName})obj;
            return {compare};
";
            var x = SyntaxFactory.MethodDeclaration(SyntaxFactory.IdentifierName("bool"), "Equals")
               .WithParameterList(SyntaxFactory.ParseParameterList("(object obj)"))
               .WithModifiers(SyntaxTokenList.Create(SyntaxFactory.Token(SyntaxKind.PublicKeyword)).Add(SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
               .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement(code)));
            return x;
        }
        MethodDeclarationSyntax CreateGetHashMethod(bool isDerived, IEnumerable<ParameterDefinition> parameters)
        {
            string compare = string.Join(" && ", parameters.Select(p => $"Equals({Common.PascalCasing(p.Name)}, o.{Common.PascalCasing(p.Name)})"));
            StringBuilder codeBuilder = new StringBuilder();
            codeBuilder.AppendLine("unchecked\n\t\t\t{");
            string initialValue = isDerived ? "base.GetHashCode()" : "23";
            codeBuilder.AppendLine($"\t\t\t\tint hash = {initialValue};");
            foreach (var p in parameters)
            {
                string name = Common.PascalCasing(p.Name);
                if (p.CanBeNull)
                {
                    codeBuilder.AppendLine($"\t\t\t\thash = hash * 37 + ({name} != null ? {name}.GetHashCode() : 0);");
                }
                else
                {
                    codeBuilder.AppendLine($"\t\t\t\thash = hash * 37 + {name}.GetHashCode();");
                }
            }
            codeBuilder.AppendLine("\t\t\t\treturn hash;");
            codeBuilder.AppendLine("\t\t\t}");
            string code = codeBuilder.ToString();
            var x = SyntaxFactory.MethodDeclaration(SyntaxFactory.IdentifierName("int"), "GetHashCode")
               .WithParameterList(SyntaxFactory.ParseParameterList("()"))
               .WithModifiers(SyntaxTokenList.Create(SyntaxFactory.Token(SyntaxKind.PublicKeyword)).Add(SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
               .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement(code)));
            return x;
        }

        private ParameterDefinition[] CollectBaseTypeConstructorParameters(INamedTypeSymbol typeSymbol)
        {
            var baseType = typeSymbol.BaseType;
            if (baseType.SpecialType != SpecialType.System_Object && baseType.Constructors.Length == 1)
            {
                var baseTypeConstructor = baseType.Constructors[0];
                return baseTypeConstructor.Parameters.Select(
                    p => 
                    {
                        return new ParameterDefinition(true, SyntaxFactory.ParseTypeName(p.Type.ToString()), p.Name, CanTypeBeNull(p.Type));
                    }).ToArray();
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
                                      let sp = semanticModel.GetDeclaredSymbol(p)
                                      let typeSyntax = SyntaxFactory.ParseTypeName(p.Type.ToString())
                                      select new ParameterDefinition(false, typeSyntax, p.Identifier.Text, CanTypeBeNull(sp.Type));
            var typeParameters = typeParametersQuery.ToArray();
            parameters.InsertRange(0, typeParameters);

            BlockSyntax newBody = CreateConstructorBody(parameters.Where(p => !p.IsDefinedInBaseType));
            var constructorParameters = SyntaxFactory.ParseParameterList($"({string.Join(", ", parameters.Select(p => p.Text))})");

            ConstructorDeclarationSyntax newConstructor;
            // implements call to base constructor only for classes
            if (hasBaseType && cds != null)
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

            var persistentPropertiesLeadingTrivia = GetNonCustomTrivia(typeDecl.Members);
            var newMembers = CreateProperties(parameters.Where(p => !p.IsDefinedInBaseType), persistentPropertiesLeadingTrivia);
            newMembers = newMembers.Add(newConstructor);
            // gets superclass between struct and class ti
            var typeIdentifier = cds != null ? cds : (TypeDeclarationSyntax)sds;
            string typeIdentifierText = ConstructFullTypeName(typeIdentifier);
            bool isTypeAbstract = typeDecl.Modifiers.Any(m => m.Kind() == SyntaxKind.AbstractKeyword);
            if (!isTypeAbstract)
            {
                var cloneMethod = CreateCloneMethod(typeIdentifierText, parameters);
                newMembers = newMembers.Add(cloneMethod);
            }
            if (parameters.Count > 0)
            {
                var equalsMethod = CreateEqualsMethod(typeIdentifierText, parameters);
                newMembers = newMembers.Add(equalsMethod);
            }
            if (typeParameters.Length > 0)
            {
                var getHashCodeMethod = CreateGetHashMethod(hasBaseType, typeParameters);
                newMembers = newMembers.Add(getHashCodeMethod);
            }
            // Collects custom members that are persisted
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
        static string ConstructFullTypeName(TypeDeclarationSyntax tds)
        {
            //typeIdentifier.TypeParameterList.Parameters[0].Identifier
            var typeParams = tds.TypeParameterList?.Parameters;
            string typeParamsText = "";
            if (typeParams?.Count > 0)
            {
                typeParamsText = $"<{string.Join(", ", typeParams.Value.Select(p => p.Identifier.Text))}>";
            }
            return $"{tds.Identifier.Text}{typeParamsText}";
        }
        static bool CanTypeBeNull(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol namedType)
            {
                switch (namedType.TypeKind)
                {
                    case TypeKind.Struct:
                        return namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T;
                    case TypeKind.Enum:
                        return false;
                }
            }
            // generic types
            else if (type is ITypeSymbol typed)
            {
                switch (typed.TypeKind)
                {
                    case TypeKind.TypeParameter:
                        return typed.IsReferenceType;
                }
            }
            return true;
        }
    }
}