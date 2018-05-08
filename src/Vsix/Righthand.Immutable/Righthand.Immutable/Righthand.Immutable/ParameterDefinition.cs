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
        public bool CanBeNull { get; }
        public ParameterDefinition(bool isDefinedInBaseType, TypeSyntax type, string name, bool canBeNull)
        {
            IsDefinedInBaseType = isDefinedInBaseType;
            Type = type;
            Name = name;
            CanBeNull = canBeNull;
        }

        public string Text =>$"{Type} {Name}";
    }
}
