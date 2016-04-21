using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using ConsoleTools;
using static ConsoleTools.ConsoleHelper;

namespace Console_Tools
{
    public static class Program
    {
        static void Main(string[] args)
        {
            if (!args.Any())
                args = "/int 123 -bool T -string This is my string".Split(' ');

            CommandParser.Settings.AllowAllProperties = true;
            CommandParser.Settings.PromptForMissingRequired = true;

            // Parses to CmdGroup Objects
            //var a = args.ParseOnly();

            // ParsedAs parses arguments based on a given type
            //var b = args.ParseAs(typeof(IInterface));
            //var c = args.ParseAs(typeof(MyStruct));
            //var d = args.ParseAs(typeof(AbstractClass));

            // args.Parse(T) parses the argument as T and loads arguments into type
            //var e = args.Parse(new RegClass());

            Spinner("Parsing Parameters ", () =>
            {
                args.ParseStatic(typeof(StaticClass));
            });

            // Converts the output os a ParseAs to a dynamic object 
            ////var dynObj = d.ToDynamic();
            ////var myInt = dynObj.Int;

            if (!CommandParser.HelpWasRequested)
            {
                WriteLine(ConsoleColor.Green, $"Thanks! {StaticClass.String}");
                SomeEyeCandy();
            }
        }
        
        private static void SomeEyeCandy()
        {
            EnablePaging = true;
            VerticalLine();
            Indent = 2;

            Spinner("Working!! ", new List<ConsoleText>()
            {
                new ConsoleText("0",ConsoleColor.DarkGray),
                new ConsoleText("0",ConsoleColor.Gray),
                new ConsoleText("0",ConsoleColor.Green),
                new ConsoleText("0",ConsoleColor.Yellow),
                new ConsoleText("0",ConsoleColor.White)
            }, "All Done \n",
                () => { Thread.Sleep(3000); });

            for (var i = 0; i < 100; i++)
            {
                WriteLine($"Test {i}");
            }

            WriteVertical("This is a test");
            WriteLineCentered(new ConsoleText("This is another test", ConsoleColor.Red));
            SpinnerDashes("Spin!!! ", () => { Thread.Sleep(1800); });
            Spinner("This line will be erased ",new[] {"5","4","3","2", "1"},"Now!",()=> {Thread.Sleep(1100);});
            EraseLine();
            Indent = 0;
            HorizontalLine();

            ConsoleResponse Name = new ConsoleResponse("What is your name?");
            Name.GetInput();

            Write($"Hello {Name.Response.String}");

            EnablePaging = false;
        }

        private struct MyStruct 
        {
            public string String { get; set; }
            public int Int { get; set; }
            public bool Bool { get; set; }
        }

        private interface IInterface
        {
            [CommandParser.CmdProperty(Description = "This is my string")]
            string String { get; set; }

            [CommandParser.CmdProperty(ShortName = "I", DefaultValue = 45, Description = "My Init value")]
            int Int { get; set; }

            bool Bool { get; set; }
        }

        private class RegClass 
        {
            [CommandParser.CmdProperty(Required = true)]
            public ConsoleColor NotProvided { get; set; }

            [CommandParser.CmdProperty(Description = "Da Password")]
            public SecureString Password { get; set; }

            public string String { get; set; }
            public int Int { get; set; }
            public bool Bool { get; set; }
        }

        private abstract class AbstractClass 
        {
            public abstract string String { get; set; }
            public abstract int Int { get; set; }
            public abstract bool Bool { get; set; }
        }

        private static class StaticClass
        {
            public static string String { get; set; }
            public static int Int { get; set; }
            public static bool Bool { get; set; }
        }
    }
}
