using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ManyConsole.Internal;

namespace ManyConsole
{
    public class ConsoleCommandDispatcher
    {
        public static int DispatchCommand(ConsoleCommand command, string[] arguments, TextWriter consoleOut)
        {
            return DispatchCommand(new [] {command}, arguments, consoleOut);
        }

        public static int DispatchCommand(IEnumerable<ConsoleCommand> commands, string[] arguments, TextWriter consoleOut, bool skipExeInExpectedUsage = false)
        {
            ConsoleCommand selectedCommand = null;

            TextWriter console = consoleOut;

            IEnumerable<ConsoleCommand> consoleCommands = commands as ConsoleCommand[] ?? commands.ToArray();
            foreach (var command in consoleCommands)
            {
                ValidateConsoleCommand(command);
            }

            try
            {
                List<string> remainingArguments;

                if (consoleCommands.Count() == 1)
                {
                    selectedCommand = consoleCommands.First();

                    if (arguments.Any() && (arguments.First().ToLower() == selectedCommand.Command.ToLower()))
                    {
                        remainingArguments = selectedCommand.GetActualOptions().Parse(arguments.Skip(1));
                    }
                    else
                    {
                        remainingArguments = selectedCommand.GetActualOptions().Parse(arguments);
                    }
                }
                else
                {
                    if (!arguments.Any())
                        throw new ConsoleHelpAsException("No arguments specified.");

                    if (arguments[0].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                    {
                        selectedCommand = GetMatchingCommand(consoleCommands, arguments.Skip(1).FirstOrDefault());

                        if (selectedCommand == null)
                            ConsoleHelp.ShowSummaryOfCommands(consoleCommands, console);
                        else
                            ConsoleHelp.ShowCommandHelp(selectedCommand, console, skipExeInExpectedUsage);

                        return -1;
                    }

                    selectedCommand = GetMatchingCommand(consoleCommands, arguments.First());

                    if (selectedCommand == null)
                        throw new ConsoleHelpAsException("Command name not recognized.");

                    remainingArguments = selectedCommand.GetActualOptions().Parse(arguments.Skip(1));
                }

                selectedCommand.CheckRequiredArguments();

                CheckRemainingArguments(remainingArguments, selectedCommand.RemainingArgumentsCount);

                var preResult = selectedCommand.OverrideAfterHandlingArgumentsBeforeRun(remainingArguments.ToArray());

                if (preResult.HasValue)
                    return preResult.Value;

                ConsoleHelp.ShowParsedCommand(selectedCommand, console);

                return selectedCommand.Run(remainingArguments.ToArray());
            }
            catch (ConsoleHelpAsException e)
            {
                return DealWithException(e, console, skipExeInExpectedUsage, selectedCommand, consoleCommands);
            }
            catch (NDesk.Options.OptionException e)
            {
                return DealWithException(e, console, skipExeInExpectedUsage, selectedCommand, consoleCommands);
            }
        }

        private static int DealWithException(Exception e, TextWriter console, bool skipExeInExpectedUsage, ConsoleCommand selectedCommand, IEnumerable<ConsoleCommand> commands)
        {
            if (selectedCommand != null)
            {
                console.WriteLine();
                console.WriteLine(e.Message);
                ConsoleHelp.ShowCommandHelp(selectedCommand, console, skipExeInExpectedUsage);
            }
            else
            {
                ConsoleHelp.ShowSummaryOfCommands(commands, console);
            }

            return -1;
        }
  
        private static ConsoleCommand GetMatchingCommand(IEnumerable<ConsoleCommand> command, string name)
        {
            return command.FirstOrDefault(c => c.Command.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateConsoleCommand(ConsoleCommand command)
        {
            if (string.IsNullOrEmpty(command.Command))
            {
                throw new InvalidOperationException(
                    $"Command {command.GetType().Name} did not call IsCommand in its constructor to indicate its name and description.");
            }
        }

        private static void CheckRemainingArguments(List<string> remainingArguments, int? parametersRequiredAfterOptions)
        {
            if (parametersRequiredAfterOptions.HasValue)
                ConsoleUtil.VerifyNumberOfArguments(remainingArguments.ToArray(),
                    parametersRequiredAfterOptions.Value);
        }

        public static IEnumerable<ConsoleCommand> FindCommandsInSameAssemblyAs(Type typeInSameAssembly)
        {
            if (typeInSameAssembly == null)
                throw new ArgumentNullException(nameof(typeInSameAssembly));

            return FindCommandsInAssembly(typeInSameAssembly.Assembly);
        }

        public static IEnumerable<ConsoleCommand> FindCommandsInAllLoadedAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies().SelectMany(FindCommandsInAssembly);
        }

        public static IEnumerable<ConsoleCommand> FindCommandsInAssembly(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            var commandTypes = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(ConsoleCommand)))
                .Where(t => !t.IsAbstract)
                .OrderBy(t => t.FullName);

            List<ConsoleCommand> result = new List<ConsoleCommand>();

            foreach(var commandType in commandTypes)
            {
                var constructor = commandType.GetConstructor(new Type[] { });

                if (constructor == null)
                    continue;

                result.Add((ConsoleCommand)constructor.Invoke(new object[] { }));
            }

            return result;
        }
    }
}
