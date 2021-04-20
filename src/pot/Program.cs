using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using CommandLine;
using i18n.Core;
using i18n.Core.Abstractions;
using i18n.Core.Abstractions.Domain;
using i18n.Core.PortableObject;
using i18n.Core.Pot;
using i18n.Core.Pot.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Memory;

namespace pot
{
    [UsedImplicitly]
    internal class Options
    {
        [Option("show-source-context", Required = false, HelpText = "Append source context to references")]
        public bool ShowSourceContext { get; [UsedImplicitly] set; }

        [Option("web-config-path", Required = false, HelpText = "Path to web.config that contain i18n.* settings.")]
        public string WebConfigPath { get; [UsedImplicitly] set; }

        [Option("verbose", Required = false, HelpText = "Set output to verbose.")]
        public bool Verbose { get; [UsedImplicitly] set; }

        [Option("watch", Required = false, HelpText = "Automatically rebuild pot if any translatable files changes.")]
        public bool Watch { get; [UsedImplicitly] set; }

        [Option("watch-delay", Required = false, HelpText = "Delay between each build (throttling). Default value is 500 ms. ")]
        public int WatchDelay { get; [UsedImplicitly] set; } = 500;

        [Option("build-no-merge", Required = false, HelpText = "Prevent merging POT changes automatically into PO language files.")]
        public bool DontMergeOnBuild { get; [UsedImplicitly] set; }

        [Option("project", Required = false, HelpText = "Path to source files to project")]
        public string Project { get; [UsedImplicitly] set; }

        [Option("project-default-lang", Required = false, HelpText = "Language name of default pot file.")]
        public string ProjectDefaultLang { get; [UsedImplicitly] set; }

        [Option("project-output", Required = false, HelpText = "Path to write projected output files to.")]
        public string ProjectOutput { get; [UsedImplicitly] set; }

        [Option("project-force", Required = false, HelpText = "Force overwriting of output files in project output.")]
        public bool ProjectForce { get; [UsedImplicitly] set; }

    }

    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal static class Program
    {
        static readonly object BuildLock = new object();
        static bool _isBuilding;
        static DateTime? _lastBuildDate;
        static string _assemblyVersionStr;

        public static void Main(string[] args)
        {
            _assemblyVersionStr = typeof(Program)
                                       .Assembly
                                       .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                       ?.InformationalVersion
                                       ?? "0.0.0";

            Environment.ExitCode = 1;

            Parser.Default
                .ParseArguments<Options>(args)
                .WithParsed(options =>
                {
                    try
                    {
                        Environment.ExitCode = Run(options);
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine("ERROR: {0}", exception.Message);
                        if (exception.InnerException == null)
                        {
                            return;
                        }
                        while (exception.InnerException != null)
                        {
                            exception = exception.InnerException;
                        }
                        Console.WriteLine("Error (InnerException): {0}", exception.Message);
                    }
                });
        }

        static int Run(Options options)
        {
            ReferenceContext.ShowSourceContext = options.ShowSourceContext;

            string projectDirectory;
            string webConfigFilename;

            if (options.WebConfigPath != null)
            {
                if (options.WebConfigPath.LastIndexOf("Web.config", StringComparison.OrdinalIgnoreCase) == -1)
                {
                    projectDirectory = Path.GetFullPath(options.WebConfigPath);
                    webConfigFilename = Path.Combine(projectDirectory, "Web.config");
                }
                else
                {
                    projectDirectory = Path.GetFullPath(Path.GetDirectoryName(options.WebConfigPath)!);
                    webConfigFilename = options.WebConfigPath;
                }
            }
            else
            {
                projectDirectory = Directory.GetCurrentDirectory();
                webConfigFilename = Path.Combine(projectDirectory, "Web.config");
            }

            if (options.Verbose)
            {
                Console.WriteLine($"Project directory: {projectDirectory}");
                Console.WriteLine($"Web.config filename: {webConfigFilename}");
            }

            if (options.Watch)
            {
                return Watch(options, projectDirectory, webConfigFilename, () => Build(options, projectDirectory, webConfigFilename, options.WatchDelay));
            }

            if (options.Project != null)
            {
                return Project(options, projectDirectory, webConfigFilename, () => Build(options, projectDirectory, webConfigFilename, options.WatchDelay));
            }

            Build(options, projectDirectory, webConfigFilename);

            return 0;
        }

        static int Watch(Options options, string projectDirectory, string webConfigFilename, Action onChangeAction)
        {
            var settingsProvider = (ISettingsProvider)new SettingsProvider(projectDirectory);
            settingsProvider.PopulateFromWebConfig(webConfigFilename);
            var settings = new I18NLocalizationOptions(settingsProvider);
            var watchers = new List<FileSystemWatcher>();

            var filters = settings.WhiteList.Where(x => x.StartsWith("*.")).ToList();

            Console.WriteLine($"Watching directories: {string.Join(", ", settings.DirectoriesToScan)}. Filters: {string.Join(", ", filters)}");

            try
            {
                foreach (var directory in settings.DirectoriesToScan)
                {
                    if (!Directory.Exists(directory))
                    {
                        Console.Error.WriteLine($"Watch directory does not exist: {directory}");
                        return 1;
                    }

                    var watcher = new FileSystemWatcher
                    {
                        Path = directory,
                        NotifyFilter = NotifyFilters.LastWrite
                                       | NotifyFilters.FileName
                                       | NotifyFilters.DirectoryName,
                        IncludeSubdirectories = true
                    };

                    foreach (var filter in filters)
                    {
                        watcher.Filters.Add(filter);
                    }

                    watcher.Changed += (sender, args) =>
                    {
                        onChangeAction.Invoke();
                    };

                    watcher.Created += (sender, args) =>
                    {
                        onChangeAction.Invoke();
                    };

                    watcher.Renamed += (sender, args) =>
                    {
                        onChangeAction.Invoke();
                    };

                    watcher.Deleted += (sender, args) =>
                    {
                        onChangeAction.Invoke();
                    };

                    watcher.EnableRaisingEvents = true;

                    watchers.Add(watcher);
                }
            }
            finally
            {
                Console.ReadLine();

                foreach (var watcher in watchers)
                {
                    watcher.Dispose();
                }
            }

            return 0;
        }

        static int Project(Options options, string projectDirectory, string webConfigFilename, Action onChangeAction)
        {
            var settingsProvider = (ISettingsProvider)new SettingsProvider(projectDirectory);
            settingsProvider.PopulateFromWebConfig(webConfigFilename);
            var settings = new I18NLocalizationOptions(settingsProvider);
            var watchers = new List<FileSystemWatcher>();

            var filters = settings.WhiteList.Where(x => x.StartsWith("*.")).ToList();

            Console.WriteLine($"Projecting directories: {string.Join(", ", settings.DirectoriesToScan)}. Filters: {string.Join(", ", filters)}");

            var repository = new PoTranslationRepository(settings, _assemblyVersionStr);

            if (string.IsNullOrWhiteSpace(options.ProjectDefaultLang))
            {
                Console.Error.WriteLine($"Default culture not specified. Use --project-default-lang");
                return 1;
            }
            CultureInfo defaultCulture = CultureInfo.GetCultureInfo(options.ProjectDefaultLang);
            if (defaultCulture == null)
            {
                Console.Error.WriteLine($"Default culture not found: {options.ProjectDefaultLang}");
                return 1;
            }
            string[] langs = new string[] { defaultCulture.Name }.Union(repository.GetAvailableLanguages().Select(x => x.LanguageShortTag)).ToArray();
            Console.WriteLine($"Using default language for project: {defaultCulture.Name}");
            Console.WriteLine($"Found languages for project: {string.Join(", ", langs)}");
            Console.WriteLine($"Projecting with locales from: {settings.LocaleDirectory}");

            ILocalizationManager localizationManager = new LocalizationManager(
                new IPluralRuleProvider[] { new DefaultPluralRuleProvider() },
                new PortableObjectFilesTranslationsProvider(new DirectoryPoFileLocationProvider(settings.LocaleDirectory, defaultCulture.Name)),
                new MemoryCache(new MemoryCacheOptions()),
                new DefaultNuggetReplacer()
                );


            try
            {
                var directory = Path.GetFullPath(options.Project);

                if (!Directory.Exists(directory))
                {
                    Console.Error.WriteLine($"Directory does not exist: {directory}");
                    return 1;
                }

                string outputDirectory;

                if (options.ProjectOutput != null)
                {
                    outputDirectory = Path.GetFullPath(Path.GetDirectoryName(options.ProjectOutput)!);
                }
                else
                {
                    outputDirectory = Path.Combine(directory, "projected");
                }

                if (!Directory.Exists(outputDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDirectory);
                        if (options.Verbose) Console.Out.WriteLine($"Directory created: {outputDirectory} ");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Directory {outputDirectory} could not be created: {ex.Message}");
                        return 1;
                    }
                }

                if (options.Verbose) Console.Out.WriteLine($"Projecting into directory: {outputDirectory} ");

                try
                {
                    foreach (var lang in langs)
                    {
                        Console.Out.WriteLine($"Projecting language: {lang}");
                        Process(options, lang, localizationManager, directory, outputDirectory);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return 1;
                }
                //foreach (var subdirectory in Directory.GetDirectories(directory))
                //{
                //    if (Path.GetFullPath(subdirectory) != outputDirectory && Path.GetFileName(subdirectory).StartsWith("_"))
                //    {
                //        var copyOutputDirectory = Path.Combine(outputDirectory, $"{Path.GetFileName(subdirectory)}");

                //        if (!Directory.Exists(copyOutputDirectory))
                //        {
                //            try
                //            {
                //                Directory.CreateDirectory(copyOutputDirectory);
                //                if (options.Verbose) Console.Out.WriteLine($"Directory created: {copyOutputDirectory} ");
                //            }
                //            catch (Exception ex)
                //            {
                //                Console.Error.WriteLine($"Directory {copyOutputDirectory} could not be created: {ex.Message}");
                //                return 1;
                //            }
                //        }

                //        foreach (var file in Directory.GetFiles(subdirectory))
                //        {
                //            if (options.Verbose) Console.Out.WriteLine($"Copying file: {file}");

                //            var outputFile = Path.Combine(copyOutputDirectory, Path.GetFileName(file));

                //            if (options.Verbose) Console.Out.WriteLine($"Writing output: {outputFile}");
                //            if (File.Exists(outputFile) && !options.ProjectForce)
                //            {
                //                Console.Error.WriteLine($"File {outputFile} already exists. Use --project-force to overwrite.");
                //                return 1;
                //            }
                //            File.Copy(file, outputFile);
                //        }
                //    }
                //}
            }
            finally
            {

            }

            return 0;
        }

        private static void Process(Options options, string lang, ILocalizationManager localizationManager, string directory, string outputDirectory)
        {
            foreach (var subdirectory in Directory.GetDirectories(directory))
            {
                if (Path.GetFullPath(subdirectory) != outputDirectory)
                {
                    if (Path.GetFileName(subdirectory).StartsWith("_"))
                    {
                        if (options.Verbose) Console.Out.WriteLine($"Projecting a sub directory starting with _: {subdirectory}");
                        Process(options, lang, localizationManager, Path.GetFullPath(subdirectory), Path.Combine(outputDirectory, Path.GetFileName(subdirectory)));
                    }
                    else
                    {
                        if (options.Verbose) Console.Out.WriteLine($"Projecting directory: {subdirectory}");

                        var langOutputDirectory = Path.Combine(outputDirectory, $"{lang}-{Path.GetFileName(subdirectory)}");

                        if (!Directory.Exists(langOutputDirectory))
                        {
                            try
                            {
                                Directory.CreateDirectory(langOutputDirectory);
                                if (options.Verbose) Console.Out.WriteLine($"Directory created: {langOutputDirectory} ");
                            }
                            catch (Exception ex)
                            {
                                throw new ApplicationException($"Directory {langOutputDirectory} could not be created: {ex.Message}");
                            }
                        }

                        foreach (var file in Directory.GetFiles(subdirectory))
                        {
                            if (options.Verbose) Console.Out.WriteLine($"Projecting file: {file}");

                            var translatedFileContents = localizationManager.Translate(CultureInfo.GetCultureInfo(lang), File.ReadAllText(file));

                            var outputFile = Path.Combine(langOutputDirectory, Path.GetFileName(file));

                            if (options.Verbose) Console.Out.WriteLine($"Writing output: {outputFile}");
                            if (File.Exists(outputFile) && !options.ProjectForce)
                            {

                                throw new ApplicationException($"File {outputFile} already exists. Use --project-force to overwrite.");
                            }
                            File.WriteAllText(outputFile, translatedFileContents);
                        }

                    }
                }

            }

        }

        static void Build(Options options, string projectDirectory, string webConfigFilename, int buildDelayMilliseconds = -1)
        {
            lock (BuildLock)
            {
                if (_isBuilding)
                {
                    return;
                }

                if (buildDelayMilliseconds > 0
                    && _lastBuildDate.HasValue
                    && DateTime.Now - _lastBuildDate.Value < TimeSpan.FromMilliseconds(buildDelayMilliseconds))
                {
                    return;
                }

                _isBuilding = true;
            }

            try
            {
                var sw = new Stopwatch();
                sw.Restart();

                var settingsProvider = (ISettingsProvider)new SettingsProvider(projectDirectory);
                settingsProvider.PopulateFromWebConfig(webConfigFilename);

                var settings = new I18NLocalizationOptions(settingsProvider);
                var repository = new PoTranslationRepository(settings, _assemblyVersionStr);
                var nuggetFinder = new FileNuggetFinder(settings);

                var items = nuggetFinder.ParseAll();
                if (repository.SaveTemplate(items))
                {
                    if (!options.DontMergeOnBuild)
                    {
                        var merger = new TranslationMerger(repository);
                        merger.MergeAllTranslation(items);
                    }
                }

                sw.Stop();

                Console.WriteLine($"Build operation completed in {sw.Elapsed.TotalSeconds:F} seconds.");
            }
            finally
            {
                lock (BuildLock)
                {
                    _isBuilding = false;
                    _lastBuildDate = DateTime.Now;
                }
            }
        }

    }
}
