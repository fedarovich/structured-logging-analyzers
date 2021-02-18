using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace StructuredLogging.Analyzers
{
    internal static class Extensions
    {
        internal static bool IsSubtypeOf(this INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            var parent = type;
            do
            {
                if (SymbolEqualityComparer.Default.Equals(parent, baseType))
                    return true;

                parent = parent.BaseType;
            } while (parent != null);

            if (baseType.TypeKind == TypeKind.Interface)
            {
                foreach (var @interface in type.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(@interface, baseType))
                        return true;
                }
            }

            return false;
        }
    }
}
