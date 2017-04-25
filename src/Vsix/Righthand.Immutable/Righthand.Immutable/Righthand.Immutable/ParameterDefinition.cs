using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;

namespace Righthand.Immutable
{
    [DebuggerDisplay("{Type,nq} {Name,nq}")]  
    public struct ParameterDefinition
    {
        public bool IsDefinedInBaseType { get; }
        public TypeSyntax Type { get; }
        public string Name { get; }

        public ParameterDefinition(bool isDefinedInBaseType, TypeSyntax type, string name)
        {
            IsDefinedInBaseType = isDefinedInBaseType;
            Type = type;
            Name = name;
        }

        public string Text =>$"{Type} {Name}";
    }
}
