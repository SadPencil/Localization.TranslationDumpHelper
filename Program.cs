using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Localization.TranslationDumpHelper
{
    class Program
    {
        static void Main(string[] args) => GeneralTryCatchCUI(() =>
        {
            ConsoleWriteColorLine("Warning: this program is naive and unreliable. Use it at your own risk!", ConsoleColor.Yellow);
            ConsoleWriteColorLine("Also, strings in condition compiling are ignored and I don't know how to deal with it.", ConsoleColor.Yellow);
            // initialize: find the MSBuild that comes with VS2017 or later
            _ = MSBuildLocator.RegisterDefaults();

            // constants
            string solutionDir;
#if DEBUG
            solutionDir = "D:\\SadPencil\\Documents\\GitHub\\xna-cncnet-client";
#else
            solutionDir = ".";
#endif
            string solutionName = @"DXClient.sln";
            string solutionFullPath = Path.Combine(solutionDir, solutionName);
            var projectsToBeAnalyzed = new HashSet<string>() { "DXMainClient", "ClientCore", "ClientGUI", "DTAConfig" };

            // perform syntax analysis
            ConsoleWriteColorLine($"====== Solution: {solutionName} ======", ConsoleColor.Green);
            var workspace = MSBuildWorkspace.Create();
            workspace.WorkspaceFailed += (sender, e) =>
            {
                ConsoleWriteColorLine(e.Diagnostic.ToString(), ConsoleColor.Red);
            };
            var solution = workspace.OpenSolutionAsync(solutionFullPath).Result;

            foreach (var project in solution.Projects)
            {
                if (!projectsToBeAnalyzed.Contains(project.Name))
                {
                    continue;
                }

                ConsoleWriteColorLine($"==== Project: {project.Name} [{project.DocumentIds.Count}] ====", ConsoleColor.Green);
                var compilation = project.GetCompilationAsync().Result;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    //var invocationSyntaxes = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>();
                    // https://stackoverflow.com/questions/43679690/with-roslyn-find-calling-method-from-string-literal-parameter
                    var invocationSyntaxes = tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>();
                    foreach (var s in invocationSyntaxes)
                    {
                        if (s == null) { continue; }
                        if (s.Kind() != SyntaxKind.SimpleMemberAccessExpression) { continue; }

                        if (s.Name.ToString() == "L10N")
                        {
                            var p = s.Parent as InvocationExpressionSyntax;
                            if (p == null) { continue; }
                            if (p.ArgumentList.Arguments.Count > 0)
                            {
                                var x = p.ArgumentList.Arguments[0];
                                Console.WriteLine($"{x}");

                                // https://stackoverflow.com/questions/35670115/how-to-use-roslyn-to-get-compile-time-constant-value
                                var semanticModel = compilation.GetSemanticModel(x.SyntaxTree);
                                Console.WriteLine($"++{semanticModel.GetConstantValue(x.Expression).Value?.ToString()}");

                            }


                        }
                    }

                }
            }

#if DEBUG
            // Program ends. This line will be removed soon.
            ConsoleWriteColorLine("Program ends. Hit Enter to exit.", ConsoleColor.Green);
            Console.ReadLine();
#endif
        });

        static bool PrintAndOmitError(Exception ex)
        {
            if (ex is AggregateException aggregateException)
            {
                aggregateException.Handle(exx => PrintAndOmitError(exx));
            }
            else
            {
                ConsoleWriteColorLine(ex.Message, ConsoleColor.Red);
                ConsoleWriteColorLine(ex.StackTrace, ConsoleColor.Red);
            }
            return true;
        }

        static void GeneralTryCatchCUI(Action action)
        {
#if DEBUG
            action.Invoke();
#else
            try
            {
                action.Invoke();
            }
#pragma warning disable CA1031 // Disable -- Do not catch general exception types
            catch (Exception ex)
            {
                PrintAndOmitError(ex);
            }
#pragma warning restore CA1031 // Disable --Do not catch general exception types
#endif
        }

        static void ConsoleWriteColorLine(string text, ConsoleColor color)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = oldColor;
        }
    }
}
