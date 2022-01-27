using Microsoft.Build.Locator;
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
            solutionDir = "../../..";
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

            Dictionary<string, string> translations = new Dictionary<string, string>();
            HashSet<string> duplicatedLabels = new HashSet<string>();

            foreach (var project in solution.Projects)
            {
                if (!projectsToBeAnalyzed.Contains(project.Name))
                {
                    continue;
                }

                ConsoleWriteColorLine($"==== Project: {project.Name} [{project.DocumentIds.Count}] ====", ConsoleColor.Green);
                string test = "\" +  \"";
                ReplaceStringRegex(ref test, new Regex("\"\\s*\\+\\s*\\\"", RegexOptions.CultureInvariant), "");//test
                var compilation = project.GetCompilationAsync().Result;
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var invocationSyntaxes = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>();
                    foreach (var syntax in invocationSyntaxes)
                    {
                        // Warning: the implementation is naive and unreliable. Use it at your own risk!

                        string syntaxText = syntax.ToString();
                        if (syntaxText.Contains(".L10N("))
                        {
                            while (true)
                            {
                                bool replaced = false;
                                //replaced |= ReplaceString(ref syntaxText, "  ", " ");
                                replaced |= ReplaceString(ref syntaxText, "\r\n", "\n");
                                //replaced |= ReplaceString(ref syntaxText, "\\\n", "");
                                replaced |= ReplaceString(ref syntaxText, "+\n", "+");
                                replaced |= ReplaceString(ref syntaxText, "(\n", "(");
                                replaced |= ReplaceString(ref syntaxText, "Environment.NewLine", "\"\\n\"");
                                //replaced |= ReplaceString(ref syntaxText, "\" + \"", "");
                                replaced |= ReplaceStringRegex(ref syntaxText, new Regex("\"\\s*\\+\\s*\\\"", RegexOptions.CultureInvariant), "");
                                if (!replaced) break;
                            }

                            if (new Regex("^\\(\"[\\s\\S]*\"\\).L10N\\(\"[\\s\\S]*\"\\)$", RegexOptions.CultureInvariant).IsMatch(syntaxText))
                            {
                                Debug.Assert(syntaxText.First() == '(');
                                syntaxText = syntaxText.Substring(1);
                                ReplaceString(ref syntaxText, ").L10N(", ".L10N(");
                            }

                            List<string> validStrings = new List<string>();
                            if (new Regex("^\"[\\s\\S]*\".L10N\\(\"[\\s\\S]*\"\\)$", RegexOptions.CultureInvariant).IsMatch(syntaxText))
                            {
                                validStrings.Add(syntaxText);
                            }
                            else
                            {
                                //non greedy
                                bool found = false;
                                var matches = new Regex("\"[\\s\\S]*?\".L10N\\(\"[\\s\\S]*\"\\)", RegexOptions.CultureInvariant).Matches(syntaxText).Cast<Match>().ToList();
                                if (matches.Count > 0)
                                {
                                    found = true;
                                    foreach (Match match in matches) validStrings.Add(match.Value);
                                }

                                matches = new Regex("\\(\"[\\s\\S]*?\"\\).L10N\\(\"[\\s\\S]*\"\\)", RegexOptions.CultureInvariant).Matches(syntaxText).Cast<Match>().ToList();
                                if (matches.Count > 0)
                                {
                                    found = true;
                                    foreach (Match match in matches)
                                    {
                                        string value = match.Value;
                                        Debug.Assert((new Regex("^\\(\"[\\s\\S]*\"\\).L10N\\(\"[\\s\\S]*\"\\)$", RegexOptions.CultureInvariant).IsMatch(value)));
                                        Debug.Assert(value.First() == '(');
                                        value = value.Substring(1);
                                        ReplaceString(ref value, ").L10N(", ".L10N(");
                                        validStrings.Add(value);
                                    }
                                }

                                if (!found)
                                {
                                    ConsoleWriteColorLine("Warning: unrecognized string below.", ConsoleColor.Yellow);
                                    ConsoleWriteColorLine(syntaxText, ConsoleColor.Cyan);
                                }

                            }
                            foreach (var text in validStrings)
                            {
                                Debug.Assert(new Regex("^\"[\\s\\S]*\".L10N\\(\"[\\s\\S]*\"\\)$", RegexOptions.CultureInvariant).IsMatch(text));
                                {
                                    var split = text.Split(new string[] { ".L10N(" }, StringSplitOptions.None);
                                    if (split.Length != 2) break;
                                    Debug.Assert(split[0].First() == '\"');
                                    Debug.Assert(split[0].Last() == '\"');
                                    Debug.Assert(split[1].First() == '\"');
                                    Debug.Assert(split[1].Last() == ')');

                                    for (var i = 0; i < split.Length; i++)
                                    {
                                        ReplaceString(ref split[i], "\\n", "@@");
                                        ReplaceString(ref split[i], "\\\"", "\"");
                                        split[i] = split[i].Substring(1);
                                        split[i] = split[i].Substring(0, split[i].Length - 1);
                                        if (i == 1)
                                        {
                                            split[i] = split[i].Substring(0, split[i].Length - 1);
                                        }
                                    }
                                    if (translations.ContainsKey(split[1]))
                                    {
                                        if (string.CompareOrdinal(split[0], translations[split[1]]) != 0)
                                        {
                                            _ = duplicatedLabels.Add(split[1]);
                                            ConsoleWriteColorLine($"Warning: label {split[1]} is defined more than once with different values.", ConsoleColor.Yellow);
                                            ConsoleWriteColorLine("1 - " + translations[split[1]], ConsoleColor.Cyan);
                                            ConsoleWriteColorLine("2 - " + split[0], ConsoleColor.Cyan);
                                            if (!split[0].Contains("\"") && (split[0].Length > translations[split[1]].Length || translations[split[1]].Contains("\"")))
                                                translations[split[1]] = split[0];
                                            ConsoleWriteColorLine("> - " + translations[split[1]], ConsoleColor.Cyan);
                                        }
                                        else
                                        {
                                            // do nothing
                                        }
                                    }
                                    else
                                    {
                                        translations.Add(split[1], split[0]);
                                    }

                                }


                            }


                        }
                    }
                }
            }
            foreach (var kv in translations)
            {
                Console.WriteLine($"{kv.Key}={kv.Value}");
            }
            if (duplicatedLabels.Count > 0)
            {
                ConsoleWriteColorLine("Warning: the follow labels might be defined more than once with different values. Please fix or ignore it.", ConsoleColor.Yellow);
                foreach (var label in duplicatedLabels)
                {
                    Console.WriteLine(label);
                }
            }

#if DEBUG
            // Program ends. This line will be removed soon.
            ConsoleWriteColorLine("Program ends. Hit Enter to exit.", ConsoleColor.Green);
            Console.ReadLine();
#endif
        });

        static bool ReplaceString(ref string text, string oldValue, string newValue)
        {
            if (!text.Contains(oldValue)) return false;
            text = text.Replace(oldValue, newValue);
            return true;
        }

        static bool ReplaceStringRegex(ref string text, Regex pattern, string newValue)
        {
            if (!pattern.IsMatch(text)) return false;
            text = pattern.Replace(text, newValue);
            return true;
        }
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
