using System;
using System.Security;
using ConsoleTools;

namespace Console_Tools
{
    class Program
    {
        static void Main(string[] args)
        {
            CommandParser.Settings.AllowAllProperties = true;
            CommandParser.Settings.PromptForMissingRequired = true;
            CommandParser.Settings.PromptForMissingTimeoutEnabled = false;
            CommandParser.Settings.ThrowOnMultipleSwitchUse = true;

            var a = args.ParseOnly();
            var b = args.ParseAs(typeof(IInterface));
            var c = args.ParseAs(typeof(MyStruct));
            var d = args.ParseAs(typeof(AbstractClass));

            var e = args.Parse(new RegClass());

            //var dynObj = d.ToDynamic();
            //var myInt = dynObj.Int;
        }


        public struct MyStruct : IInterface
        {
            public string String { get; set; }
            public int Int { get; set; }
            public bool Bool { get; set; }
        }

        private interface IInterface
        {
            [CommandParser.CmdProperty(Description = "No Idea")]
            string String { get; set; }

            [CommandParser.CmdProperty(ShortName = "I", DefaultValue = 45, Description = "My Init")]
            int Int { get; set; }

            bool Bool { get; set; }
        }

        private class RegClass : IInterface
        {
            [CommandParser.CmdProperty(Required = true)]
            public ConsoleColor NotProvided { get; set; }

            [CommandParser.CmdProperty]
            public SecureString Password { get; set; }

            public string String { get; set; }
            public int Int { get; set; }
            public bool Bool { get; set; }
        }

        private abstract class AbstractClass : IInterface
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
