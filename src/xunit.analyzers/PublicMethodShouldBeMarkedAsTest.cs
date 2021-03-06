﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Xunit.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PublicMethodShouldBeMarkedAsTest : XunitDiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
           ImmutableArray.Create(Descriptors.X1013_PublicMethodShouldBeMarkedAsTest);

        internal override void AnalyzeCompilation(CompilationStartAnalysisContext compilationStartContext, XunitContext xunitContext)
        {
            var taskType = compilationStartContext.Compilation.GetTypeByMetadataName(Constants.Types.SystemThreadingTasksTask);
            var interfacesToIgnore = new List<INamedTypeSymbol>
                {
                    compilationStartContext.Compilation.GetSpecialType(SpecialType.System_IDisposable),
                    compilationStartContext.Compilation.GetTypeByMetadataName(Constants.Types.XunitIAsyncLifetime),
                };

            compilationStartContext.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;

                if (type.TypeKind != TypeKind.Class ||
                    type.DeclaredAccessibility != Accessibility.Public)
                    return;

                var methodsToIgnore = interfacesToIgnore.Where(i => i != null && type.AllInterfaces.Contains(i))
                    .SelectMany(i => i.GetMembers())
                    .Select(m => type.FindImplementationForInterfaceMember(m))
                    .Where(s => s != null)
                    .ToList();

                var hasTestMethods = false;
                var violations = new List<IMethodSymbol>();
                foreach (var member in type.GetMembers().Where(m => m.Kind == SymbolKind.Method))
                {
                    symbolContext.CancellationToken.ThrowIfCancellationRequested();

                    var method = (IMethodSymbol)member;
                    if (method.MethodKind != MethodKind.Ordinary)
                        continue;

                    var isTestMethod = method.GetAttributes().ContainsAttributeType(xunitContext.FactAttributeType);
                    hasTestMethods = hasTestMethods || isTestMethod;

                    if (isTestMethod)
                        continue;

                    if (method.DeclaredAccessibility == Accessibility.Public &&
                        (method.ReturnsVoid || (taskType != null && method.ReturnType == taskType)))
                    {
                        var shouldIgnore = false;
                        while (!shouldIgnore || method.IsOverride)
                        {
                            if (methodsToIgnore.Any(m => method.Equals(m)))
                            {
                                shouldIgnore = true;
                            }
                            if (!method.IsOverride) break;
                            method = method.OverriddenMethod;
                        }
                        if (!shouldIgnore)
                            violations.Add(method);
                    }
                }

                if (hasTestMethods)
                {
                    foreach (var method in violations)
                    {
                        var testType = method.Parameters.Any() ? "Theory" : "Fact";
                        symbolContext.ReportDiagnostic(Diagnostic.Create(Descriptors.X1013_PublicMethodShouldBeMarkedAsTest,
                            method.Locations.First(),
                            method.Name, method.ContainingType.Name, testType));
                    }
                }
            }, SymbolKind.NamedType);
        }
    }
}
