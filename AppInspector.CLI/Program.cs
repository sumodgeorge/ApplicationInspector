﻿//Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using Microsoft.ApplicationInspector.Logging;

namespace Microsoft.ApplicationInspector.CLI
{
    using CommandLine;
    using Commands;
    using ShellProgressBar;
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInspector.Common;
    using Microsoft.Extensions.Logging;
    using ILogger = Extensions.Logging.ILogger;

    public static class Program
    {
        private static ILoggerFactory loggerFactory = new LoggerFactory();

        /// <summary>
        /// CLI program entry point which defines command verbs and options to running
        /// </summary>
        /// <param name="args"></param>
        public static int Main(string[] args)
        {
            int finalResult = (int)Common.Utils.ExitCode.CriticalError;

            Common.Utils.CLIExecutionContext = true;//set manually at start from CLI
            Exception? exception = null;
            try
            {
                var argsResult = Parser.Default.ParseArguments<CLIAnalyzeCmdOptions,
                    CLITagDiffCmdOptions,
                    CLIExportTagsCmdOptions,
                    CLIVerifyRulesCmdOptions,
                    CLIPackRulesCmdOptions>(args)
                  .MapResult(
                    (CLIAnalyzeCmdOptions cliOptions) => VerifyOutputArgsRun(cliOptions),
                    (CLITagDiffCmdOptions cliOptions) => VerifyOutputArgsRun(cliOptions),
                    (CLIExportTagsCmdOptions cliOptions) => VerifyOutputArgsRun(cliOptions),
                    (CLIVerifyRulesCmdOptions cliOptions) => VerifyOutputArgsRun(cliOptions),
                    (CLIPackRulesCmdOptions cliOptions) => VerifyOutputArgsRun(cliOptions),
                    errs => (int)Common.Utils.ExitCode.CriticalError
                  );

                finalResult = argsResult;
            }
            catch (Exception e)
            {
                exception = e;
            }
            var logger = loggerFactory.CreateLogger("Program");
            if (exception is not null)
            {
                logger.LogError("Uncaught exception: {type}:{message}. {stackTrace}", exception.GetType().Name, exception.Message, exception.StackTrace);
            }
            loggerFactory.Dispose();
            return finalResult;
        }


        //idea is to check output args which are not applicable to NuGet callers before the command operation is run for max efficiency

        private static int VerifyOutputArgsRun(CLITagDiffCmdOptions options)
        {
            loggerFactory = options.GetLoggerFactory();

            CommonOutputChecks(options);
            return RunTagDiffCommand(options);
        }
        private static int VerifyOutputArgsRun(CLIExportTagsCmdOptions options)
        {
            loggerFactory = options.GetLoggerFactory();

            CommonOutputChecks(options);
            return RunExportTagsCommand(options);
        }

        private static int VerifyOutputArgsRun(CLIVerifyRulesCmdOptions options)
        {
            loggerFactory = options.GetLoggerFactory();

            CommonOutputChecks(options);
            return RunVerifyRulesCommand(options);
        }

        private static int VerifyOutputArgsRun(CLIPackRulesCmdOptions options)
        {
            loggerFactory = options.GetLoggerFactory();
            ILogger logger = loggerFactory.CreateLogger("Program");
            
            if (string.IsNullOrEmpty(options.OutputFilePath))
            {
                logger.LogError(MsgHelp.GetString(MsgHelp.ID.PACK_MISSING_OUTPUT_ARG));
                throw new OpException(MsgHelp.GetString(MsgHelp.ID.PACK_MISSING_OUTPUT_ARG));
            }
            else
            {
                CommonOutputChecks(options);
            }

            return RunPackRulesCommand(options);
        }

        private static int VerifyOutputArgsRun(CLIAnalyzeCmdOptions options)
        {
            loggerFactory = options.GetLoggerFactory();

            //analyze with html format limit checks
            if (options.OutputFileFormat == "html")
            {
                options.OutputFilePath ??= "output.html";
                string extensionCheck = Path.GetExtension(options.OutputFilePath);
                if (extensionCheck is not ".html" and not ".htm")
                {
                    loggerFactory.CreateLogger("Program").LogInformation(MsgHelp.GetString(MsgHelp.ID.ANALYZE_HTML_EXTENSION));
                }
            }
            if (CommonOutputChecks(options))
            {
                return RunAnalyzeCommand(options);
            }
            else
            {
                return (int)Utils.ExitCode.CriticalError;
            }
        }

        /// <summary>
        /// Checks that either output filepath is valid or console verbosity is not visible to ensure
        /// some output can be achieved...other command specific inputs that are relevant to both CLI
        /// and NuGet callers are checked by the commands themselves
        /// </summary>
        /// <param name="options"></param>
        private static bool CommonOutputChecks(CLICommandOptions options)
        {
            //validate requested format
            string fileFormatArg = options.OutputFileFormat;
            string[] validFormats =
            {
                "html",
                "text",
                "json",
                "sarif"
            };
            var logger = loggerFactory.CreateLogger("Program");
            string[] checkFormats;
            if (options is CLIAnalyzeCmdOptions cliAnalyzeOptions)
            {
                checkFormats = validFormats;
                fileFormatArg = cliAnalyzeOptions.OutputFileFormat;
            }
            else if (options is CLIPackRulesCmdOptions cliPackRulesOptions)
            {
                checkFormats = validFormats.Skip(2).Take(1).ToArray();
                fileFormatArg = cliPackRulesOptions.OutputFileFormat;
            }
            else
            {
                checkFormats = validFormats.Skip(1).Take(2).ToArray();
            }

            bool isValidFormat = checkFormats.Any(v => v.Equals(fileFormatArg.ToLower()));
            if (!isValidFormat)
            {
                logger.LogError(MsgHelp.GetString(MsgHelp.ID.CMD_INVALID_ARG_VALUE), "-f");
                return false;
            }

            if (!string.IsNullOrEmpty(options.OutputFilePath) && !CanWritePath(options.OutputFilePath))
            {
                logger.LogError(MsgHelp.GetString(MsgHelp.ID.CMD_INVALID_LOG_PATH), options.OutputFilePath);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ensure output file path can be written to
        /// </summary>
        /// <param name="filePath"></param>
        private static bool CanWritePath(string filePath)
        {
            try
            {
                File.WriteAllText(filePath, "");//verify ability to write to location
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }



        /// <summary>
        /// This method gets a logging factory with Console disabled when the progress bar is enabled. Does not change the original options.
        /// </summary>
        /// <param name="cliOptions">The original options.</param>
        /// <returns>A logging factory with adjusted settings.</returns>
        private static ILoggerFactory GetAdjustedFactory(CLIAnalyzeCmdOptions cliOptions)
        {
            return new LogOptions()
            {
                ConsoleVerbosityLevel = cliOptions.ConsoleVerbosityLevel,
                LogFileLevel = cliOptions.LogFileLevel,
                LogFilePath = cliOptions.LogFilePath,
                DisableConsoleOutput = !cliOptions.NoShowProgressBar || cliOptions.DisableConsoleOutput
            }.GetLoggerFactory();
        }

        private static (bool initialSuccess, bool backupSuccess, string? backupLocation) TryWriteResults(ResultsWriter writer, AnalyzeResult analyzeResult, CLIAnalyzeCmdOptions cliOptions, ILogger logger)
        {
            try
            {
                writer.Write(analyzeResult, cliOptions);
                return (true, false, null);
            }
            catch (Exception e)
            {
                logger.LogError("Exception hit while writing. {Type} : {Message}", e.GetType().Name,
                    e.Message);
                analyzeResult.ResultCode = AnalyzeResult.ExitCode.CriticalError;
                // If HTML 
                if (cliOptions.OutputFileFormat.Equals("html",StringComparison.InvariantCultureIgnoreCase))
                {
                    string alternatepath = "backupOutput.json";
                    var optionsCopy = cliOptions with {OutputFileFormat = "json", OutputFilePath = alternatepath};
                    try
                    {
                        writer.Write(analyzeResult, optionsCopy);
                        logger.LogInformation("Failed to write HTML report. Exported JSON instead at {AlternatePath}", alternatepath);
                        return (false, true, alternatepath);
                    }
                    catch (Exception e2)
                    {
                        logger.LogError("Exception hit while writing backup of result object. {Type} : {Message}", e2.GetType().Name,
                            e2.Message);
                    }
                }
            }
            return (false, false, null);
        }

        private static int RunAnalyzeCommand(CLIAnalyzeCmdOptions cliOptions)
        {
            // If the user manually specified -1 this means they also don't even want the snippet in sarif, so we respect that option
            // Otherwise if we are outputting sarif we don't use any of the context information so we set context to 0, if not sarif leave it alone.
            bool isSarif = cliOptions.OutputFileFormat.Equals("sarif", StringComparison.InvariantCultureIgnoreCase);
            int numContextLines = cliOptions.ContextLines == -1 ? cliOptions.ContextLines : isSarif ? 0 : cliOptions.ContextLines;
            // tagsOnly isn't compatible with sarif output, we choose to prioritize the choice of sarif.
            bool tagsOnly = !isSarif && cliOptions.TagsOnly;
            var logger = cliOptions.GetLoggerFactory().CreateLogger("Program");
            if (!cliOptions.NoShowProgressBar)
            {
                if (string.IsNullOrEmpty(cliOptions.LogFilePath))
                {
                    logger.LogInformation("Progress bar is enabled so console output will be suppressed. No LogFilePath has been configured so you will not receive log messages");
                }
                else
                {
                    logger.LogInformation("Progress bar is enabled so console output will be suppressed. For log messages check the log at {LogPath}", cliOptions.LogFilePath);
                }
            }
            ILoggerFactory adjustedFactory = GetAdjustedFactory(cliOptions);
            AnalyzeCommand command = new(new AnalyzeOptions()
            {
                SourcePath = cliOptions.SourcePath ?? Array.Empty<string>(),
                CustomRulesPath = cliOptions.CustomRulesPath ?? "",
                CustomCommentsPath = cliOptions.CustomCommentsPath,
                CustomLanguagesPath = cliOptions.CustomLanguagesPath,
                IgnoreDefaultRules = cliOptions.IgnoreDefaultRules,
                ConfidenceFilters = cliOptions.ConfidenceFilters,
                FilePathExclusions = cliOptions.FilePathExclusions,
                SingleThread = cliOptions.SingleThread,
                NoShowProgress = cliOptions.NoShowProgressBar,
                FileTimeOut = cliOptions.FileTimeOut,
                ProcessingTimeOut = cliOptions.ProcessingTimeOut,
                ContextLines = numContextLines,
                ScanUnknownTypes = cliOptions.ScanUnknownTypes,
                TagsOnly = tagsOnly,
                NoFileMetadata = cliOptions.NoFileMetadata,
                AllowAllTagsInBuildFiles = cliOptions.AllowAllTagsInBuildFiles,
                MaxNumMatchesPerTag = cliOptions.MaxNumMatchesPerTag,
                DisableCrawlArchives = cliOptions.DisableArchiveCrawling,
                EnumeratingTimeout = cliOptions.EnumeratingTimeout
            }, adjustedFactory);

            AnalyzeResult analyzeResult = command.GetResult();

            ResultsWriter writer = new(loggerFactory);
            if (cliOptions.NoShowProgressBar)
            {
                TryWriteResults(writer, analyzeResult, cliOptions, logger);
            }
            else
            {
                ProgressBarOptions outputWriterBarOptions = new()
                {
                    ForegroundColor = ConsoleColor.Yellow,
                    ForegroundColorDone = ConsoleColor.DarkGreen,
                    BackgroundColor = ConsoleColor.DarkGray,
                    ForegroundColorError = ConsoleColor.Red,
                    BackgroundCharacter = '\u2593',
                    DisableBottomPercentage = true
                };

                using (IndeterminateProgressBar pbar = new("Writing Result Files.", outputWriterBarOptions))
                {
                    var done = false;
                    (bool initialSuccess, bool backupSuccess, string? alternatePath) = (false, false, null);
                    _ = Task.Factory.StartNew(() =>
                    {
                        (initialSuccess, backupSuccess, alternatePath) = TryWriteResults(writer, analyzeResult, cliOptions, adjustedFactory.CreateLogger("RunAnalyzeCommand"));
                        done = true;
                    });
                    
                    while (!done)
                    {
                        Thread.Sleep(100);
                    }
                    
                    pbar.ObservedError = !initialSuccess;
                    pbar.Message = !initialSuccess ? 
                        (backupSuccess ? $"Failed to write results. Wrote backup results to {alternatePath}." : "Failed to write Results and failed to write Backup Results.") : $"Results written to {cliOptions.OutputFilePath}.";
                    pbar.Finished();
                }
                Console.Write(Environment.NewLine);
            }

            return (int)analyzeResult.ResultCode;
        }

        private static int RunTagDiffCommand(CLITagDiffCmdOptions cliOptions)
        {
            TagDiffCommand command = new(new TagDiffOptions()
            {
                SourcePath1 = cliOptions.SourcePath1,
                SourcePath2 = cliOptions.SourcePath2,
                CustomRulesPath = cliOptions.CustomRulesPath,
                CustomCommentsPath = cliOptions.CustomCommentsPath,
                CustomLanguagesPath = cliOptions.CustomLanguagesPath,
                IgnoreDefaultRules = cliOptions.IgnoreDefaultRules,
                FilePathExclusions = cliOptions.FilePathExclusions,
                TestType = cliOptions.TestType,
                ConfidenceFilters = cliOptions.ConfidenceFilters,
                SeverityFilters = cliOptions.SeverityFilters,
                FileTimeOut = cliOptions.FileTimeOut,
                ProcessingTimeOut = cliOptions.ProcessingTimeOut,
                ScanUnknownTypes = cliOptions.ScanUnknownTypes,
                SingleThread = cliOptions.SingleThread,
            }, loggerFactory);

            TagDiffResult tagDiffResult = command.GetResult();

            ResultsWriter writer = new(loggerFactory);
            writer.Write(tagDiffResult, cliOptions);

            return (int)tagDiffResult.ResultCode;
        }
        
        private static int RunExportTagsCommand(CLIExportTagsCmdOptions cliOptions)
        {
            ExportTagsCommand command = new(new ExportTagsOptions()
            {
                IgnoreDefaultRules = cliOptions.IgnoreDefaultRules,
                CustomRulesPath = cliOptions.CustomRulesPath,
            }, loggerFactory);

            ExportTagsResult exportTagsResult = command.GetResult();

            ResultsWriter writer = new(loggerFactory);
            writer.Write(exportTagsResult, cliOptions);

            return (int)exportTagsResult.ResultCode;
        }

        private static int RunVerifyRulesCommand(CLIVerifyRulesCmdOptions cliOptions)
        {
            VerifyRulesCommand command = new(new VerifyRulesOptions()
            {
                VerifyDefaultRules = cliOptions.VerifyDefaultRules,
                CustomRulesPath = cliOptions.CustomRulesPath,
                CustomCommentsPath = cliOptions.CustomCommentsPath,
                CustomLanguagesPath = cliOptions.CustomLanguagesPath,
                Failfast = cliOptions.Failfast,
            }, loggerFactory);

            VerifyRulesResult exportTagsResult = command.GetResult();

            ResultsWriter writer = new(loggerFactory);
            writer.Write(exportTagsResult, cliOptions);

            return (int)exportTagsResult.ResultCode;
        }

        private static int RunPackRulesCommand(CLIPackRulesCmdOptions cliOptions)
        {
            PackRulesCommand command = new(new PackRulesOptions()
            {
                CustomRulesPath = cliOptions.CustomRulesPath,
                CustomCommentsPath = cliOptions.CustomCommentsPath,
                CustomLanguagesPath = cliOptions.CustomLanguagesPath,
                PackEmbeddedRules = cliOptions.PackEmbeddedRules
            }, loggerFactory);

            PackRulesResult exportTagsResult = command.GetResult();

            ResultsWriter writer = new(loggerFactory);
            writer.Write(exportTagsResult, cliOptions);

            return (int)exportTagsResult.ResultCode;
        }

    }
}