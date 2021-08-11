﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Roslynator.CodeFixes;
using Roslynator.CSharp;
using Roslynator.Diagnostics;
using Roslynator.Documentation;
using Roslynator.FindSymbols;
using Roslynator.Spelling;
using static Roslynator.CommandLine.ParseHelpers;
using static Roslynator.Logger;

namespace Roslynator.CommandLine
{
    //TODO: banner/ruleset add, change, remove
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                ParserResult<object> parserResult = Parser.Default.ParseArguments<
                    MigrateCommandLineOptions,
#if DEBUG
                    AnalyzeAssemblyCommandLineOptions,
                    FindSymbolsCommandLineOptions,
                    SlnListCommandLineOptions,
                    ListVisualStudioCommandLineOptions,
                    GenerateSourceReferencesCommandLineOptions,
                    ListReferencesCommandLineOptions,
#endif
                    FixCommandLineOptions,
                    AnalyzeCommandLineOptions,
                    ListSymbolsCommandLineOptions,
                    FormatCommandLineOptions,
                    SpellcheckCommandLineOptions,
                    PhysicalLinesOfCodeCommandLineOptions,
                    LogicalLinesOfCodeCommandLineOptions,
                    GenerateDocCommandLineOptions,
                    GenerateDocRootCommandLineOptions
                    >(args);

                var verbosityParsed = false;

                parserResult.WithParsed<AbstractCommandLineOptions>(options =>
                {
                    var defaultVerbosity = Verbosity.Normal;

                    if (options.Verbosity == null
                        || TryParseVerbosity(options.Verbosity, out defaultVerbosity))
                    {
                        ConsoleOut.Verbosity = defaultVerbosity;

                        Verbosity fileLogVerbosity = defaultVerbosity;

                        if (options.FileLogVerbosity == null
                            || TryParseVerbosity(options.FileLogVerbosity, out fileLogVerbosity))
                        {
                            if (options.FileLog != null)
                            {
                                var fs = new FileStream(options.FileLog, FileMode.Create, FileAccess.Write, FileShare.Read);
                                var sw = new StreamWriter(fs, Encoding.UTF8, bufferSize: 4096, leaveOpen: false);
                                Out = new TextWriterWithVerbosity(sw) { Verbosity = fileLogVerbosity };
                            }

                            verbosityParsed = true;
                        }
                    }
                });

                if (!verbosityParsed)
                    return ExitCodes.Error;

                WriteLine(
                    $"Roslynator Command Line Tool version {typeof(Program).GetTypeInfo().Assembly.GetName().Version} "
                        + $"(Roslyn version {typeof(Accessibility).GetTypeInfo().Assembly.GetName().Version})",
                    Verbosity.Normal);

                return parserResult.MapResult(
#if DEBUG
                    (AnalyzeAssemblyCommandLineOptions options) => AnalyzeAssembly(options),
                    (FindSymbolsCommandLineOptions options) => FindSymbolsAsync(options).Result,
                    (SlnListCommandLineOptions options) => SlnListAsync(options).Result,
                    (ListVisualStudioCommandLineOptions options) => ListVisualStudio(options),
                    (GenerateSourceReferencesCommandLineOptions options) => GenerateSourceReferencesAsync(options).Result,
                    (ListReferencesCommandLineOptions options) => ListReferencesAsync(options).Result,
#endif
                    (FixCommandLineOptions options) => FixAsync(options).Result,
                    (AnalyzeCommandLineOptions options) => AnalyzeAsync(options).Result,
                    (ListSymbolsCommandLineOptions options) => ListSymbolsAsync(options).Result,
                    (FormatCommandLineOptions options) => FormatAsync(options).Result,
                    (SpellcheckCommandLineOptions options) => SpellcheckAsync(options).Result,
                    (PhysicalLinesOfCodeCommandLineOptions options) => PhysicalLinesOfCodeAsync(options).Result,
                    (LogicalLinesOfCodeCommandLineOptions options) => LogicalLinesOrCodeAsync(options).Result,
                    (GenerateDocCommandLineOptions options) => GenerateDocAsync(options).Result,
                    (GenerateDocRootCommandLineOptions options) => GenerateDocRootAsync(options).Result,
                    (MigrateCommandLineOptions options) => Migrate(options),
                    _ => ExitCodes.Error);
            }
            catch (Exception ex) when (ex is AggregateException
                || ex is FileNotFoundException
                || ex is InvalidOperationException)
            {
                WriteError(ex);
            }
            finally
            {
                Out?.Dispose();
                Out = null;
            }

            return ExitCodes.Error;
        }

        private static async Task<int> FixAsync(FixCommandLineOptions options)
        {
            if (!options.TryParseDiagnosticSeverity(CodeFixerOptions.Default.SeverityLevel, out DiagnosticSeverity severityLevel))
                return ExitCodes.Error;

            if (!TryParseKeyValuePairs(options.DiagnosticFixMap, out List<KeyValuePair<string, string>> diagnosticFixMap))
                return ExitCodes.Error;

            if (!TryParseKeyValuePairs(options.DiagnosticFixerMap, out List<KeyValuePair<string, string>> diagnosticFixerMap))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnum(options.FixScope, OptionNames.FixScope, out FixAllScope fixAllScope, FixAllScope.Project))
                return ExitCodes.Error;

            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new FixCommand(
                options: options,
                severityLevel: severityLevel,
                diagnosticFixMap: diagnosticFixMap,
                diagnosticFixerMap: diagnosticFixerMap,
                fixAllScope: fixAllScope,
                projectFilter: projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static async Task<int> AnalyzeAsync(AnalyzeCommandLineOptions options)
        {
            if (!options.TryParseDiagnosticSeverity(CodeAnalyzerOptions.Default.SeverityLevel, out DiagnosticSeverity severityLevel))
                return ExitCodes.Error;

            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new AnalyzeCommand(options, severityLevel, projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static int AnalyzeAssembly(AnalyzeAssemblyCommandLineOptions options)
        {
            string language = null;

            if (options.Language != null
                && !TryParseLanguage(options.Language, out language))
            {
                return ExitCodes.Error;
            }

            var command = new AnalyzeAssemblyCommand(language);

            CommandStatus status = command.Execute(options);

            return GetExitCode(status);
        }

        private static async Task<int> FindSymbolsAsync(FindSymbolsCommandLineOptions options)
        {
            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.SymbolGroups, OptionNames.SymbolGroups, out SymbolGroupFilter symbolGroups, SymbolFinderOptions.Default.SymbolGroups))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.Visibility, OptionNames.Visibility, out VisibilityFilter visibility, SymbolFinderOptions.Default.Visibility))
                return ExitCodes.Error;

            if (!TryParseMetadataNames(options.WithAttributes, out ImmutableArray<MetadataName> withAttributes))
                return ExitCodes.Error;

            if (!TryParseMetadataNames(options.WithoutAttributes, out ImmutableArray<MetadataName> withoutAttributes))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.WithFlags, OptionNames.WithFlags, out SymbolFlags withFlags, SymbolFlags.None))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.WithoutFlags, OptionNames.WithoutFlags, out SymbolFlags withoutFlags, SymbolFlags.None))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            ImmutableArray<SymbolFilterRule>.Builder rules = ImmutableArray.CreateBuilder<SymbolFilterRule>();

            if (withAttributes.Any())
                rules.Add(new WithAttributeFilterRule(withAttributes));

            if (withoutAttributes.Any())
                rules.Add(new WithoutAttributeFilterRule(withoutAttributes));

            if (withFlags != SymbolFlags.None)
                rules.AddRange(SymbolFilterRuleFactory.FromFlags(withFlags));

            if (withoutFlags != SymbolFlags.None)
                rules.AddRange(SymbolFilterRuleFactory.FromFlags(withoutFlags, invert: true));

            var symbolFinderOptions = new SymbolFinderOptions(
                visibility: visibility,
                symbolGroups: symbolGroups,
                rules: rules,
                ignoreGeneratedCode: options.IgnoreGeneratedCode,
                unusedOnly: options.UnusedOnly);

            var command = new FindSymbolsCommand(
                options: options,
                symbolFinderOptions: symbolFinderOptions,
                projectFilter: projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static async Task<int> ListSymbolsAsync(ListSymbolsCommandLineOptions options)
        {
            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnum(options.Depth, OptionNames.Depth, out DocumentationDepth depth, DocumentationDepth.Member))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.WrapList, OptionNames.WrapList, out WrapListOptions wrapListOptions))
                return ExitCodes.Error;

            if (!TryParseMetadataNames(options.IgnoredAttributes, out ImmutableArray<MetadataName> ignoredAttributes))
                return ExitCodes.Error;

            if (!TryParseMetadataNames(options.IgnoredSymbols, out ImmutableArray<MetadataName> ignoredSymbols))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.IgnoredParts, OptionNames.IgnoredParts, out SymbolDefinitionPartFilter ignoredParts))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnum(options.Layout, OptionNames.Layout, out SymbolDefinitionListLayout layout, SymbolDefinitionListLayout.NamespaceList))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.Visibility, OptionNames.Visibility, out VisibilityFilter visibilityFilter, SymbolFilterOptions.Default.Visibility))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            ImmutableArray<SymbolFilterRule> rules = (ignoredSymbols.Any())
                ? ImmutableArray.Create<SymbolFilterRule>(new IgnoredNameSymbolFilterRule(ignoredSymbols))
                : ImmutableArray<SymbolFilterRule>.Empty;

            ImmutableArray<AttributeFilterRule> attributeRules = ImmutableArray.Create<AttributeFilterRule>(
                IgnoredAttributeNameFilterRule.Default,
                new IgnoredAttributeNameFilterRule(ignoredAttributes));

            var symbolFilterOptions = new SymbolFilterOptions(
                visibility: visibilityFilter,
                symbolGroups: GetSymbolGroupFilter(),
                rules: rules,
                attributeRules: attributeRules);

            var command = new ListSymbolsCommand(
                options: options,
                symbolFilterOptions: symbolFilterOptions,
                wrapListOptions: wrapListOptions,
                layout: layout,
                ignoredParts: ignoredParts,
                projectFilter: projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);

            SymbolGroupFilter GetSymbolGroupFilter()
            {
                switch (depth)
                {
                    case DocumentationDepth.Member:
                        return SymbolGroupFilter.TypeOrMember;
                    case DocumentationDepth.Type:
                        return SymbolGroupFilter.Type;
                    case DocumentationDepth.Namespace:
                        return SymbolGroupFilter.None;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        private static async Task<int> FormatAsync(FormatCommandLineOptions options)
        {
            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            string endOfLine = options.EndOfLine;

            if (endOfLine != null
                && endOfLine != "lf"
                && endOfLine != "crlf")
            {
                WriteLine($"Unknown end of line '{endOfLine}'.", Verbosity.Quiet);
                return ExitCodes.Error;
            }

            var command = new FormatCommand(options, projectFilter);

            IEnumerable<string> properties = options.Properties;

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, properties);

            return GetExitCode(status);
        }

        private static async Task<int> SpellcheckAsync(SpellcheckCommandLineOptions options)
        {
            if (!TryParseOptionValueAsEnumFlags(options.Scope, OptionNames.Scope, out SpellingScopeFilter scopeFilter, SpellingScopeFilter.All))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.IgnoredScope, OptionNames.IgnoredScope, out SpellingScopeFilter ignoredScopeFilter, SpellingScopeFilter.None))
                return ExitCodes.Error;

            scopeFilter &= ~ignoredScopeFilter;

            if (!TryParseOptionValueAsEnum(options.Visibility, OptionNames.Visibility, out Visibility visibility))
                return ExitCodes.Error;

            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!ParseHelpers.TryEnsureFullPath(options.Words, out ImmutableArray<string> wordListPaths))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            WordListLoaderResult loaderResult = WordListLoader.Load(
                wordListPaths,
                options.MinWordLength,
                (options.CaseSensitive) ? WordListLoadOptions.None : WordListLoadOptions.IgnoreCase);

            var data = new SpellingData(loaderResult.List, loaderResult.CaseSensitiveList, loaderResult.FixList);

            var command = new SpellcheckCommand(options, projectFilter, data, visibility, scopeFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static async Task<int> SlnListAsync(SlnListCommandLineOptions options)
        {
            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new SlnListCommand(options, projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static int ListVisualStudio(ListVisualStudioCommandLineOptions options)
        {
            var command = new ListVisualStudioCommand(options);

            CommandStatus status = command.Execute();

            return GetExitCode(status);
        }

        private static async Task<int> PhysicalLinesOfCodeAsync(PhysicalLinesOfCodeCommandLineOptions options)
        {
            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new PhysicalLinesOfCodeCommand(options, projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static async Task<int> LogicalLinesOrCodeAsync(LogicalLinesOfCodeCommandLineOptions options)
        {
            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new LogicalLinesOfCodeCommand(options, projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static async Task<int> GenerateDocAsync(GenerateDocCommandLineOptions options)
        {
            if (options.MaxDerivedTypes < 0)
            {
                WriteLine("Maximum number of derived items must be equal or greater than 0.", Verbosity.Quiet);
                return ExitCodes.Error;
            }

            if (!TryParseOptionValueAsEnum(options.Depth, OptionNames.Depth, out DocumentationDepth depth, DocumentationOptions.Default.Depth))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.IgnoredRootParts, OptionNames.IgnoredRootParts, out RootDocumentationParts ignoredRootParts, DocumentationOptions.Default.IgnoredRootParts))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.IgnoredNamespaceParts, OptionNames.IgnoredNamespaceParts, out NamespaceDocumentationParts ignoredNamespaceParts, DocumentationOptions.Default.IgnoredNamespaceParts))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.IgnoredTypeParts, OptionNames.IgnoredTypeParts, out TypeDocumentationParts ignoredTypeParts, DocumentationOptions.Default.IgnoredTypeParts))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.IgnoredMemberParts, OptionNames.IgnoredMemberParts, out MemberDocumentationParts ignoredMemberParts, DocumentationOptions.Default.IgnoredMemberParts))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.IncludeContainingNamespace, OptionNames.IncludeContainingNamespace, out IncludeContainingNamespaceFilter includeContainingNamespaceFilter, DocumentationOptions.Default.IncludeContainingNamespaceFilter))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.OmitMemberParts, OptionNames.OmitMemberParts, out OmitMemberParts omitMemberParts, OmitMemberParts.None))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnum(options.Visibility, OptionNames.Visibility, out Visibility visibility))
                return ExitCodes.Error;

            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Path, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new GenerateDocCommand(
                options,
                depth,
                ignoredRootParts,
                ignoredNamespaceParts,
                ignoredTypeParts,
                ignoredMemberParts,
                omitMemberParts,
                includeContainingNamespaceFilter,
                visibility,
                projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static async Task<int> GenerateDocRootAsync(GenerateDocRootCommandLineOptions options)
        {
            if (!TryParseOptionValueAsEnumFlags(options.IncludeContainingNamespace, OptionNames.IncludeContainingNamespace, out IncludeContainingNamespaceFilter includeContainingNamespaceFilter, DocumentationOptions.Default.IncludeContainingNamespaceFilter))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnum(options.Visibility, OptionNames.Visibility, out Visibility visibility))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnum(options.Depth, OptionNames.Depth, out DocumentationDepth depth, DocumentationOptions.Default.Depth))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.IgnoredParts, OptionNames.IgnoredRootParts, out RootDocumentationParts ignoredParts, DocumentationOptions.Default.IgnoredRootParts))
                return ExitCodes.Error;

            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Path, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new GenerateDocRootCommand(
                options,
                depth,
                ignoredParts,
                includeContainingNamespaceFilter: includeContainingNamespaceFilter,
                visibility,
                projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static async Task<int> GenerateSourceReferencesAsync(GenerateSourceReferencesCommandLineOptions options)
        {
            if (!TryParseOptionValueAsEnum(options.Depth, OptionNames.Depth, out DocumentationDepth depth, DocumentationOptions.Default.Depth))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnum(options.Visibility, OptionNames.Visibility, out Visibility visibility))
                return ExitCodes.Error;

            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Path, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new GenerateSourceReferencesCommand(
                options,
                depth,
                visibility,
                projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static int Migrate(MigrateCommandLineOptions options)
        {
            if (!string.Equals(options.Identifier, "roslynator.analyzers", StringComparison.Ordinal))
            {
                WriteLine($"Unknown identifier '{options.Identifier}'.", Verbosity.Quiet);
                return ExitCodes.Error;
            }

            if (!TryParsePaths(options.Path, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            if (!TryParseVersion(options.Version, out Version version))
                return ExitCodes.Error;

            version = new Version(
                version.Major,
                version.Minor,
                Math.Max(version.Build, 0));

            if (version != Versions.Version_3_0_0)
            {
                WriteLine($"Unknown target version '{version}'.", Verbosity.Quiet);
                return ExitCodes.Error;
            }

            var command = new MigrateCommand(
                paths,
                options.Identifier,
                version,
                options.DryRun);

            CommandStatus status = command.Execute();

            return GetExitCode(status);
        }

        private static async Task<int> ListReferencesAsync(ListReferencesCommandLineOptions options)
        {
            if (!TryParseOptionValueAsEnum(options.Display, OptionNames.Display, out MetadataReferenceDisplay display, MetadataReferenceDisplay.Path))
                return ExitCodes.Error;

            if (!TryParseOptionValueAsEnumFlags(options.Type, OptionNames.Type, out MetadataReferenceFilter metadataReferenceFilter, MetadataReferenceFilter.Dll | MetadataReferenceFilter.Project))
                return ExitCodes.Error;

            if (!options.TryGetProjectFilter(out ProjectFilter projectFilter))
                return ExitCodes.Error;

            if (!TryParsePaths(options.Paths, out ImmutableArray<string> paths))
                return ExitCodes.Error;

            var command = new ListReferencesCommand(
                options,
                display,
                metadataReferenceFilter,
                projectFilter);

            CommandStatus status = await command.ExecuteAsync(paths, options.MSBuildPath, options.Properties);

            return GetExitCode(status);
        }

        private static bool TryParsePaths(string value, out ImmutableArray<string> paths)
        {
            return TryParsePaths(ImmutableArray.Create(value), out paths);
        }

        private static bool TryParsePaths(IEnumerable<string> values, out ImmutableArray<string> paths)
        {
            paths = ImmutableArray<string>.Empty;

            if (values.Any())
            {
                if (!TryEnsureFullPath(values, out ImmutableArray<string> paths2))
                    return false;

                paths = paths.AddRange(paths2);
            }

            if (Console.IsInputRedirected)
            {
                if (!TryEnsureFullPath(
                    ConsoleHelpers.ReadRedirectedInputAsLines().Where(f => !string.IsNullOrEmpty(f)),
                    out ImmutableArray<string> paths2))
                {
                    return false;
                }

                paths = paths.AddRange(paths2);
            }

            if (!paths.IsEmpty)
                return true;

            string directoryPath = Environment.CurrentDirectory;

            if (!TryFindFile(Directory.EnumerateFiles(directoryPath, "*.sln", SearchOption.TopDirectoryOnly), out string solutionPath))
            {
                WriteLine($"Multiple MSBuild solution files found in '{directoryPath}'", Verbosity.Quiet);
                return false;
            }

            if (!TryFindFile(
                Directory.EnumerateFiles(directoryPath, "*.*proj", SearchOption.TopDirectoryOnly)
                    .Where(f => !string.Equals(".xproj", Path.GetExtension(f), StringComparison.OrdinalIgnoreCase)),
                out string projectPath))
            {
                WriteLine($"Multiple MSBuild projects files found in '{directoryPath}'", Verbosity.Quiet);
                return false;
            }

            if (solutionPath != null)
            {
                if (projectPath != null)
                {
                    WriteLine($"Both MSBuild project file and solution file found in '{directoryPath}'", Verbosity.Quiet);
                    return false;
                }

                paths = ImmutableArray.Create(solutionPath);
                return true;
            }
            else if (projectPath != null)
            {
                paths = ImmutableArray.Create(projectPath);
                return true;
            }
            else
            {
                WriteLine($"Could not find MSBuild project or solution file in '{directoryPath}'", Verbosity.Quiet);
                return false;
            }

            static bool TryFindFile(IEnumerable<string> paths, out string result)
            {
                using (IEnumerator<string> en = paths.GetEnumerator())
                {
                    if (en.MoveNext())
                    {
                        string path = en.Current;

                        if (en.MoveNext())
                        {
                            result = null;
                            return false;
                        }

                        result = path;
                    }
                    else
                    {
                        result = null;
                    }
                }

                return true;
            }
        }

        private static int GetExitCode(CommandStatus status)
        {
            switch (status)
            {
                case CommandStatus.Success:
                    return ExitCodes.Success;
                case CommandStatus.NotSuccess:
                    return ExitCodes.NotSuccess;
                case CommandStatus.Fail:
                case CommandStatus.Canceled:
                    return ExitCodes.Error;
                default:
                    throw new InvalidOperationException($"Unknown enum value '{status}'.");
            }
        }

        private static class ExitCodes
        {
            public const int Success = 0;
            public const int NotSuccess = 1;
            public const int Error = 2;
        }
    }
}
