#region Header

// Developer(s) : rleonard
//                        
// Modified : 04-16-2016 9:34 PM
// Created : 04-16-2016 2:04 PM
// Solution : ConsoleApplication4
// Project : ConsoleApplication4
// File : CommandParser.cs

#endregion

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using static ConsoleTools.ConsoleHelper;
using Timer = System.Timers.Timer;

namespace ConsoleTools
{
    internal static class CommandParser
    {
        #region Constructors

        static CommandParser()
        {
            IgnoredCmdGroupsList = new List<CmdGroup>();
            ErrorsList = new List<Exception>();

            Setup();
            AttachConsole();
        }

        #endregion

        #region Static Fields and Constants

        private static List<CmdGroup> IgnoredCmdGroupsList;

        #endregion

        #region Properties & Indexers

        /// <summary>
        /// Gets any errors the Parser encountered. 
        /// </summary>
        /// <value>The errors.</value>
        public static IEnumerable<Exception> Errors => ErrorsList?.Distinct().ToList();

        /// <summary>
        ///     Gets a value indicating whether [help was requested].
        /// </summary>
        /// <value><c>true</c> if [help was requested]; otherwise, <c>false</c>.</value>
        public static bool HelpWasRequested { get; private set; }

        /// <summary>
        ///     Gets the ignored command groups.
        /// </summary>
        /// <value>The ignored command groups.</value>
        public static IEnumerable<CmdGroup> IgnoredCmdGroups => IgnoredCmdGroupsList?.Distinct().ToList();

        private static Dictionary<Type, TypeConverter> CustomTypeConverters { get; set; }

        /// <summary>
        ///     Retrieves any parsing errors
        /// </summary>
        /// <value>The errors.</value>
        private static List<Exception> ErrorsList { get; set; }

        private static bool HaveLoadedTypeConverters { get; set; } = false;

        private static Dictionary<Type, TypeConverter> InternalTypeConverters { get; set; }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Adds the custom type converter.
        /// </summary>
        /// <param name="typeToConvert">The type to convert.</param>
        /// <param name="typeConverter">The type converter.</param>
        [DebuggerStepThrough]
        public static void AddCustomTypeConverter(Type typeToConvert, TypeConverter typeConverter)
        {
            CustomTypeConverters.Add(typeToConvert, typeConverter);
        }

        /// <summary>
        ///     Loads dictionary the into the provided object.
        /// </summary>
        /// <param name="dictionary">The dictionary.</param>
        /// <param name="obj">The object.</param>
        [DebuggerStepThrough]
        public static void LoadInto(this Dictionary<string, dynamic> dictionary, object obj)
        {
            if (dictionary == null) return;

            foreach (
                var propertyInfo in
                    obj.GetProperties()
                        .Where(propertyInfo => dictionary.ContainsKey(propertyInfo.Name.ToLowerInvariant())))
            {
                propertyInfo.SetValue(obj, dictionary[propertyInfo.Name.ToLowerInvariant()]);
            }
        }

        /// <summary>
        ///     Parses the specified arguments into the provided obj.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="args">The arguments.</param>
        /// <param name="obj">The object.</param>
        /// <returns>T.</returns>
        [DebuggerStepThrough]
        public static T Parse<T>(this string[] args, T obj) where T : class, new()
        {
            if (obj == null)
            {
                var cstor = typeof(T).GetConstructor(Type.EmptyTypes);
                if (cstor == null)
                    throw new TypeInitializationException(typeof(T).FullName,
                        new Exception("Cannot find parameterless constructor."));
                obj = Activator.CreateInstance<T>();
            }

            //if (obj is Type)
            //{
            //    var tObj = obj as Type;
            //    args.ParseAs(tObj).LoadInto(obj);
            //}
            //else
            args.ParseAs(typeof(T)).LoadInto(obj);


            return obj;
        }

        /// <summary>
        ///     Parses the provided arguments as the given type.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <param name="type">The type.</param>
        /// <returns>Dictionary&lt;System.String, dynamic&gt;.</returns>
        [DebuggerStepThrough]
        public static Dictionary<string, dynamic> ParseAs(this string[] args, Type type)
        {
            Setup();

            var returnDict = new Dictionary<string, dynamic>();
            var cmdGroups = args.ParseOnly();
            CheckForHelpRequest(cmdGroups);

            var properties = type.GetProperties();
            var usedGroups = new List<CmdGroup>();

            foreach (var propertyInfo in properties)
            {
                var att = propertyInfo.GetPropertyAttribute();
                if (att == null && Settings.AllowAllProperties == false)
                    continue;

                var name = propertyInfo.Name.ToLowerInvariant();
                var cmdGroup =
                    cmdGroups.FirstOrDefault(c => c.Switch == name || att?.ShortName?.ToLowerInvariant() == c.Switch);

                if (cmdGroup == null)
                {
                    if (propertyInfo.PropertyType == typeof(SecureString) && !HelpWasRequested)
                    {
                        returnDict.Add(name, GetSecureStringFromConsole(propertyInfo));
                    }
                    else if (att?.Required == true)
                    {
                        if (Settings.PromptForMissingRequired)
                        {
                            var success = false;
                            while (!success)
                            {
                                try
                                {
                                    returnDict.Add(name, GetStringFromConsole(propertyInfo));
                                    success = true;
                                }
                                catch (Exception e)
                                {
                                    WriteLine(ConsoleColor.Red, e.Message);
                                }
                            }
                        }
                        else
                        {
                            Write(ConsoleColor.Red, "\n** ERROR: ");
                            WriteLine(
                                $"{Settings.CmdGroupSettings.SwitchIdentifiers.First()}{propertyInfo.Name} is a required parameter.\n");

                            if (Settings.HelpSettings.ShowHelpOnErrors)
                                ExitWithHelp(type);
                            else
                                ExitWithException(
                                    new ArgumentNullException(propertyInfo.Name, $"{propertyInfo.Name} is required."),
                                    propertyInfo.PropertyType);
                        }
                    }
                    else if (att?.DefaultValue != null)
                    {
                        returnDict.Add(name, att.DefaultValue);
                    }
                }
                else
                {
                    try
                    {
                        dynamic value = null;

                        if (propertyInfo.PropertyType == typeof(SecureString) && Settings.ForceSecure)
                            value = GetSecureStringFromConsole(propertyInfo);
                        else
                            value = Get(cmdGroup.Argument, propertyInfo.PropertyType);

                        if (value == null)
                            continue;

                        returnDict.Add(name, value);
                        usedGroups.Add(cmdGroup);
                    }
                    catch (Exception e)
                    {
                        var parseException = new ParsingException(cmdGroup.Argument, propertyInfo, e);
                        var exceptionArgs = new ParsingErrorArgs(cmdGroup.Argument, propertyInfo, parseException);
                        OnParsingError(exceptionArgs);

                        if (exceptionArgs.Handled)
                        {
                            returnDict.Add(name, exceptionArgs.CorrectedValue);
                            usedGroups.Add(cmdGroup);
                        }
                        else
                        {
                            ErrorsList.Add(parseException);
                        }
                    }
                }
            }

            IgnoredCmdGroupsList.AddRange(cmdGroups.Except(usedGroups));
            ProcessIgnoredCmdGroups();

            if (HelpWasRequested)
                ExitWithHelp(type);

            return returnDict;
        }

        /// <summary>
        ///     Parses the arguments into groups
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <returns>List&lt;CmdGroup&gt;.</returns>
        [DebuggerStepThrough]
        public static List<CmdGroup> ParseOnly(this string[] args)
        {
            args = args.ToLower();
            var returnList = new List<CmdGroup>();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.IsSwitch()) continue;

                var cmdGroup = GetCmdGroup(arg);

                for (var j = i + 1; j < args.Length; j++)
                {
                    if (!args[j].IsSwitch())
                    {
                        if (string.IsNullOrEmpty(cmdGroup.Argument))
                            cmdGroup.Argument = args[j];
                        else
                            cmdGroup.Argument += " " + args[j];
                        i = j;
                    }
                    else
                        break;
                }

                cmdGroup.Index = returnList.Count + 1;

                if (returnList.Any(cg => cg.Switch == cmdGroup.Switch))
                    if (Settings.ThrowOnMultipleSwitchUse)
                        ExitWithException(
                            new MultipleSwitchUseException(
                                $"Switch \"{cmdGroup.Switch}\" used multiple times, switches cannot be used more than once."));
                    else
                        IgnoredCmdGroupsList.Add(cmdGroup);

                returnList.Add(cmdGroup);
            }

            return returnList;
        }

        /// <summary>
        /// Parses the provided arguments to a static class type.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <param name="t">The t.</param>
        /// <exception cref="ArgumentException">Only static classes can be used with this method.</exception>
        [DebuggerStepThrough]

        public static void ParseStatic(this string[] args, Type t = null)
        {
            if (t == null || !t.IsSealed || !t.IsAbstract)
                throw new ArgumentException("Only static classes can be used with this method.");

            ParseAs(args, t).LoadInto(t);
            return;
        }

        /// <summary>
        ///     Exposes the dictionary as a dynamic object
        /// </summary>
        /// <param name="dictionary">The dictionary.</param>
        /// <returns>dynamic.</returns>
        [DebuggerStepThrough]
        public static dynamic ToDynamic(this Dictionary<string, dynamic> dictionary)
        {
            return new DynObject(dictionary);
        }

        #endregion

        #region Private Methods

        private static void CheckForHelpRequest(List<CmdGroup> cmdGroups)
        {
            // Help Requested?
            var help =
                cmdGroups.FirstOrDefault(
                    group =>
                        Settings.HelpSettings.HelpRequestString.Any(s => s.ToUpper().Equals(group.Switch.ToUpper())));

            if (help != null)
            {
                HelpWasRequested = true;
                cmdGroups.Remove(help);
            }
        }

        private static void ExitWithHelp(Type type)
        {
            if (Settings.HelpSettings.AllowDefaultHelp)
                PrintHelp(type);

            if (HelpRequested != null)
                OnHelpRequested();
            else
            {
                PauseIfDebugger();
                Environment.Exit(411);
            }
        }

        private static dynamic GetStringFromConsole(PropertyInfo propertyInfo)
        {
            var consoleResponse =
                new ConsoleResponse(
                    $"Please enter required field {{{propertyInfo.Name.ToSpaced()}}} [{propertyInfo.PropertyType.Name.ToSpaced()}]");

            if (propertyInfo.PropertyType.IsEnum)
                consoleResponse.AutoCompleteList.AddRange(Enum.GetNames(propertyInfo.PropertyType));

            var sucess = Settings.PromptForMissingTimeoutEnabled ? consoleResponse.GetInput(30000) : consoleResponse.GetInput();

            return sucess ? Get(consoleResponse.Response.String, propertyInfo.PropertyType) : null;
        }

        private static SecureString GetSecureStringFromConsole(PropertyInfo propertyInfo)
        {
            var consoleResponse = new ConsoleResponse($"Please enter required field {{{propertyInfo.Name.ToSpaced()}}}");

            var sucess = Settings.PromptForMissingTimeoutEnabled ? consoleResponse.GetInput(30000, true) : consoleResponse.GetInput(true);

            return sucess ? consoleResponse.Response.SecureString : null;
        }

        private static void PauseIfDebugger()
        {
#if DEBUG
            if (!Debugger.IsAttached) return;
            WriteLine();
            WriteLineCentered(new ConsoleText("*** Debugger Attached ***", ConsoleColor.Yellow));
            WriteLineCentered(new ConsoleText("Press any key to continue", ConsoleColor.White));
            Console.ReadKey(true);
#endif
        }

        private static void ProcessIgnoredCmdGroups()
        {
            if (IgnoredParameters != null)
                OnInvalidParameters();

            if (Settings.PrintIgnoredCmdGroup)
            {
                // var txtColor = Settings.ThrowOnIgnoredCmdGroup ? ConsoleColor.Red : ConsoleColor.Yellow;
                var txtColor = ConsoleColor.Red;

                WriteLine(txtColor, "Invalid Parameters: ");
                foreach (var cmdGroup in IgnoredCmdGroups)
                {
                    WriteLine(txtColor, $"\t{cmdGroup}");
                }
            }

            if (Settings.ThrowOnIgnoredCmdGroup)
                ExitWithException(new IgnoredParameterException(IgnoredCmdGroups.ToArray()));
        }

        private static void Setup()
        {
            HaveLoadedTypeConverters = false;


            CustomTypeConverters = new Dictionary<Type, TypeConverter>();
            InternalTypeConverters = new Dictionary<Type, TypeConverter>();

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private static SecureString ToSecureString(this IEnumerable<char> value)
        {
            if (value == null) throw new ArgumentNullException("value");

            using (var secured = new SecureString())
            {
                var charArray = value.ToArray();
                foreach (var t in charArray)
                {
                    secured.AppendChar(t);
                }

                secured.MakeReadOnly();
                return secured;
            }
        }

        private static string ToSpaced(this IEnumerable<char> chars)
        {
            var sb = new StringBuilder();
            foreach (var c in chars)
            {
                if (char.IsUpper(c))
                    sb.Append(" " + c);
                else
                    sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        #endregion

        #region Nested type: CmdClassAttribute

        /// <summary>
        ///     Class CommandLineClassAttribute used on classes that would like to accept command line parameters.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class)]
        public class CmdClassAttribute : Attribute
        {
            #region Properties & Indexers

            public string HelpText { get; set; }

            #endregion
        }

        #endregion

        #region Nested type: CmdGroup

        public class CmdGroup
        {
            #region Properties & Indexers

            public string Argument { get; set; }

            public int Index { get; set; }

            public string Switch { get; set; }

            public char SwitchIdentifier { get; set; }

            #endregion

            /// <summary>
            ///     Returns a string that represents the current object.
            /// </summary>
            /// <returns>
            ///     A string that represents the current object.
            /// </returns>
            public override string ToString()
            {
                if (string.IsNullOrEmpty(Argument))
                    return SwitchIdentifier + Switch;
                return SwitchIdentifier + Switch + " " + Argument;
            }

            #region Equality Operators

            /// <summary>
            ///     Determines whether the specified object is equal to the current object.
            /// </summary>
            /// <returns>
            ///     true if the specified object  is equal to the current object; otherwise, false.
            /// </returns>
            /// <param name="obj">The object to compare with the current object. </param>
            public override bool Equals(object obj)
            {
                if (obj is CmdGroup)
                    return ((CmdGroup)obj).Equals(this);
                return false;
            }

            private bool Equals(CmdGroup other)
            {
                return string.Equals(Argument, other.Argument) && Index == other.Index &&
                       string.Equals(Switch, other.Switch) && SwitchIdentifier == other.SwitchIdentifier;
            }

            /// <summary>
            ///     Serves as the default hash function.
            /// </summary>
            /// <returns>
            ///     A hash code for the current object.
            /// </returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (Argument != null ? Argument.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ Index;
                    hashCode = (hashCode * 397) ^ (Switch != null ? Switch.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ SwitchIdentifier.GetHashCode();
                    return hashCode;
                }
            }

            #endregion
        }

        #endregion

        #region Nested type: CmdPropertyAttribute

        /// <summary>
        ///     Class CommandLineParam used to identify a property that should be used as a command line parameter.
        /// </summary>
        [AttributeUsage(AttributeTargets.Property)]
        public class CmdPropertyAttribute : Attribute
        {
            #region Properties & Indexers

            /// <summary>
            ///     Gets or sets the default value.
            /// </summary>
            /// <value>The default value.</value>
            public object DefaultValue { get; set; }

            /// <summary>
            ///     Gets the description.
            /// </summary>
            /// <value>The description.</value>
            public string Description { get; set; }

            /// <summary>
            ///     Gets or sets the example text.
            /// </summary>
            /// <value>The example text.</value>
            public string ExampleText { get; set; }

            /// <summary>
            ///     Gets or sets a value indicating whether this <see cref="CmdPropertyAttribute" /> is required.
            /// </summary>
            /// <value><c>true</c> if required; otherwise, <c>false</c>.</value>
            public bool Required { get; set; }

            /// <summary>
            ///     Gets the short name for the command.
            /// </summary>
            /// <value>The short name of the command.</value>
            public string ShortName { get; set; }

            #endregion
        }

        #endregion

        #region Nested type: DynObject

        public sealed class DynObject : DynamicObject
        {
            #region Constructors

            public DynObject(Dictionary<string, dynamic> properties)
            {
                _properties = properties;
            }

            public DynObject()
            {
                _properties = new Dictionary<string, dynamic>();
            }

            #endregion

            #region Fields

            private readonly Dictionary<string, dynamic> _properties;

            #endregion

            #region Properties & Indexers

            public dynamic this[string key]
            {
                get
                {
                    return _properties.ContainsKey(key.ToLowerInvariant()) ? _properties[key.ToLowerInvariant()] : null;
                }

                set
                {
                    if (_properties.ContainsKey(key.ToLowerInvariant()))
                        _properties[key.ToLowerInvariant()] = value;
                    else _properties.Add(key.ToLowerInvariant(), value);
                }
            }

            #endregion

            public override IEnumerable<string> GetDynamicMemberNames()
            {
                return _properties.Keys;
            }

            public override bool TryGetMember(GetMemberBinder binder, out dynamic result)
            {
                if (_properties == null)
                {
                    result = null;
                    return false;
                }

                if (_properties.ContainsKey(binder.Name.ToLowerInvariant()))
                    return _properties.TryGetValue(binder.Name.ToLowerInvariant(), out result);
                else
                {
                    result = null;
                    return false;
                }
            }

            public override bool TrySetMember(SetMemberBinder binder, object value)
            {
                if (_properties.ContainsKey(binder.Name))
                {
                    _properties[binder.Name] = value;
                    return true;
                }
                else
                    return false;
            }
        }

        #endregion

        #region Nested type: MultipleSwitchUseException
        [Serializable]
        private class MultipleSwitchUseException : Exception
        {
            #region Constructors

            //
            // For guidelines regarding the creation of new exception types, see
            //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
            // and
            //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
            //

            public MultipleSwitchUseException() { }
            public MultipleSwitchUseException(string message) : base(message) { }
            public MultipleSwitchUseException(string message, Exception inner) : base(message, inner) { }

            protected MultipleSwitchUseException(
                SerializationInfo info,
                StreamingContext context) : base(info, context)
            { }

            #endregion
        }

        #endregion

        #region Nested type: NativeMethods

        /// <summary>
        ///     Class NativeMethods.
        /// </summary>
        private static class NativeMethods
        {
            #region Static Fields and Constants

            /// <summary>
            ///     Identifies the console of the parent of the current process as the console to be attached.
            /// </summary>
            private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

            /// <summary>
            ///     calling process is already attached to a console
            /// </summary>
            private const int ERROR_ACCESS_DENIED = 5;

            #endregion

            /// <summary>
            ///     Allocate a console if application started from within windows GUI.
            ///     Detects the presence of an existing console associated with the application and
            ///     attaches itself to it if available.
            /// </summary>
            /// <exception cref="System.Exception">Console Allocation Failed</exception>
            internal static void AllocateConsole()
            {
                //
                // the following should only be used in a non-console application type (C#)
                // (since a console is allocated/attached already when you define a console app)
                //
                if (!AttachConsole(ATTACH_PARENT_PROCESS) && Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED)
                {
                    // A console was not allocated, so we need to make one.
                    if (AllocConsole() == -1)
                    {
                        // MessageBox.Show("A console could not be allocated, sorry!");
                        ExitWithException(new Exception("Console allocation failed."));
                        // throw new Exception("Console Allocation Failed");
                    }
                    else
                    {
                        Console.WriteLine("Is Attached, press a key...");
                        Console.ReadKey(true);
                        // you now may use the Console.xxx functions from .NET framework
                        // and they will work as normal
                    }
                }
            }

            // http://msdn.microsoft.com/en-us/library/ms683150(VS.85).aspx
            /// <summary>
            ///     Detaches the calling process from its console.
            /// </summary>
            /// <returns>nonzero if the function succeeds; otherwise, zero.</returns>
            /// <remarks>
            ///     If the calling process is not already attached to a console,
            ///     the error code returned is ERROR_INVALID_PARAMETER (87).
            /// </remarks>
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern int FreeConsole();

            // http://msdn.microsoft.com/en-us/library/ms681944(VS.85).aspx
            /// <summary>
            ///     Allocates a new console for the calling process.
            /// </summary>
            /// <returns>nonzero if the function succeeds; otherwise, zero.</returns>
            /// <remarks>
            ///     A process can be associated with only one console,
            ///     so the function fails if the calling process already has a console.
            /// </remarks>
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern int AllocConsole();

            /// <summary>
            ///     Attaches the console.
            /// </summary>
            /// <param name="dwProcessId">The dw process identifier.</param>
            /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool AttachConsole(uint dwProcessId);
        }

        #endregion

        #region Nested type: Settings

        public static class Settings
        {
            #region Static Fields and Constants

            internal static Mode MyMode;

            #endregion

            #region Properties & Indexers            
            /// <summary>
            /// Gets or sets the allow all properties.
            /// If True, any public property in the type provided will be accepted via the command line switches
            /// If False, only properties that are decorated by the <see cref="CmdPropertyAttribute"/> will be allowed by command line swithes
            /// </summary>
            /// <value>The allow all properties.</value>
            public static bool AllowAllProperties { get; set; } = false;
            /// <summary>
            /// Gets or sets the force secure.
            /// If True, and <see cref="SecureString"/> properties passed initally will be ignored.
            /// see <see cref="CmdPropertyAttribute"/> for the setting Forced to require the user to provide the password in a secure manner. 
            /// </summary>
            /// <value>The force secure.</value>
            public static bool ForceSecure { get; set; } = true;

            /// <summary>
            /// Gets or sets the list separator uses to determin collection items
            /// </summary>
            /// <value>The list separator.</value>
            public static char[] ListSeparator { get; set; } = { ',' };
            /// <summary>
            /// Gets or sets the print ignored command group.
            /// True, Warns the user with a Console ooutput if a parameter was ignored. 
            /// </summary>
            /// <value>The print ignored command group.</value>
            public static bool PrintIgnoredCmdGroup { get; set; } = false;
            /// <summary>
            /// Gets or sets the prompt for missing required.
            /// True, Prompts the user to provide and missing properties that are required via the <see cref="CmdPropertyAttribute"/>
            /// </summary>
            /// <value>The prompt for missing required.</value>
            public static bool PromptForMissingRequired { get; set; } = false;
            /// <summary>
            /// Gets or sets the prompt for missing timeout enabled.
            /// True, waits for a maximum of 30 seconds for a user to respond before moving forward. 
            /// False waits indefinitlly for the user to respond. 
            /// </summary>
            /// <value>The prompt for missing timeout enabled.</value>
            public static bool PromptForMissingTimeoutEnabled { get; set; } = true;
            /// <summary>
            /// Gets or sets the throw on ignored command group.
            /// True throws a <see cref="IgnoredParameterException"/> when a parameter is ignored.
            /// False ignores the occurance and simply adds the parameter to the <see cref="CommandParser.IgnoredCmdGroups"/> list.
            /// </summary>
            /// <value>The throw on ignored command group.</value>
            public static bool ThrowOnIgnoredCmdGroup { get; set; } = false;

            /// <summary>
            ///     <para>Indicates whether an exception should be thrown if a switch is used multiple times.</para>
            ///     <para> If false, switches that were previously defined will be ignored. IE First Wins</para>
            /// </summary>
            /// <value><c>true</c> if [throw on multiple switch use]; otherwise, <c>false</c>.</value>
            public static bool ThrowOnMultipleSwitchUse { get; set; } = false;
            /// <summary>
            /// Gets or sets the throw on parse exception.
            /// True, throws a <see cref="ParsingException"/> if a parsing failure occures. 
            /// False add parameter to <see cref="CommandParser.Errors"/> list.
            /// </summary>
            /// <value>The throw on parse exception.</value>
            public static bool ThrowOnParseException { get; set; } = false;

            /// <summary>
            /// Gets or sets the treat bool presence as true.
            /// True allows a switch that is passed for a <see cref="bool"/> property to be assumed true unless T/F is specifically noted. 
            /// False reqires an explsit declaration of T/F as a argument to a passed parameter. 
            /// </summary>
            /// <value>The treat bool presence as true.</value>
            public static bool TreatBoolPresenceAsTrue { get; set; } = true;
            /// <summary>
            /// Gets or sets the use internal type converters.
            /// True allow the use of the the parsers own internal type converters.
            /// False disallows the use of the parsers internal type converters.
            /// </summary>
            /// <value>The use internal type converters.</value>
            public static bool UseInternalTypeConverters { get; set; } = true;

            #endregion

            #region Nested type: CmdGroupSettings

            public static class CmdGroupSettings
            {
                #region Static Fields and Constants

                private static readonly char[] SwitchIdentifiersDefault = { '/', '-' };
                private static char[] _switchIdentifierOverride;

                #endregion

                #region Properties & Indexers                
                /// <summary>
                /// Gets or sets the switch identifier override.
                /// </summary>
                /// <value>The switch identifier override.</value>
                public static char[] SwitchIdentifierOverride

                {
                    get { return _switchIdentifierOverride; }
                    set { _switchIdentifierOverride = value.ToLower(); }
                }

                internal static IEnumerable<char> SwitchIdentifiers
                {
                    get
                    {
                        if (_switchIdentifierOverride != null && _switchIdentifierOverride.Any())
                            return _switchIdentifierOverride;
                        return SwitchIdentifiersDefault;
                    }
                }

                #endregion
            }

            #endregion

            #region Nested type: HelpSettings

            public static class HelpSettings
            {
                #region Static Fields and Constants

                private static readonly string[] HelpRequestStringsDefault = { "?", "help" };
                private static string[] _helpRequestStringsOverride;

                #endregion

                #region Properties & Indexers                
                /// <summary>
                /// Gets or sets the allow default help.
                /// </summary>
                /// <value>The allow default help.</value>
                public static bool AllowDefaultHelp { get; set; } = true;
                /// <summary>
                /// Gets or sets the help request strings override.
                /// </summary>
                /// <value>The help request strings override.</value>
                public static string[] HelpRequestStringsOverride
                {
                    get { return _helpRequestStringsOverride.ToLower(); }
                    set { _helpRequestStringsOverride = value; }
                }
                /// <summary>
                /// Gets or sets the show all possible enum values.
                /// </summary>
                /// <value>The show all possible enum values.</value>
                public static bool ShowAllPossibleEnumValues { get; set; } = true;
                /// <summary>
                /// Gets or sets the show all properties. This is applicable in conjunction with the <see cref="Settings.AllowAllProperties"/> property.
                /// </summary>
                /// <value>The show all properties.</value>
                public static bool ShowAllProperties { get; set; } = true;
                /// <summary>
                /// Gets or sets the show help on errors. Also respects the <see cref="AllowDefaultHelp"/>
                /// </summary>
                /// <value>The show help on errors.</value>
                public static bool ShowHelpOnErrors { get; set; } = true;

                internal static IEnumerable<string> HelpRequestString
                {
                    get
                    {
                        if (HelpRequestStringsOverride != null && HelpRequestStringsOverride.Any())
                            return HelpRequestStringsOverride;
                        return HelpRequestStringsDefault;
                    }
                }

                #endregion
            }

            #endregion

            #region Nested type: Mode

            internal enum Mode
            {
                Console,
                GUI
            }

            #endregion
        }

        #endregion

        #region Nested type: TypeSystem

        private static class TypeSystem
        {
            internal static Type GetElementType(Type seqType)
            {
                var ienum = FindIEnumerable(seqType);
                if (ienum == null) return seqType;
                return ienum.GetGenericArguments()[0];
            }

            private static Type FindIEnumerable(Type seqType)
            {
                if (seqType == null || seqType == typeof(string))
                    return null;
                if (seqType.IsArray)
                    return typeof(IEnumerable<>).MakeGenericType(seqType.GetElementType());
                if (seqType.IsGenericType)
                {
                    foreach (var arg in seqType.GetGenericArguments())
                    {
                        var ienum = typeof(IEnumerable<>).MakeGenericType(arg);
                        if (ienum.IsAssignableFrom(seqType))
                        {
                            return ienum;
                        }
                    }
                }
                var ifaces = seqType.GetInterfaces();
                if (ifaces != null && ifaces.Length > 0)
                {
                    foreach (var iface in ifaces)
                    {
                        var ienum = FindIEnumerable(iface);
                        if (ienum != null) return ienum;
                    }
                }
                if (seqType.BaseType != null && seqType.BaseType != typeof(object))
                {
                    return FindIEnumerable(seqType.BaseType);
                }
                return null;
            }
        }

        #endregion

        #region Private Methods

        private static void AddCollectionConverter(CmdCollectionConverterBase collectionConverter)
        {
            foreach (var t in collectionConverter.TypesICanConvert)
            {
                InternalTypeConverters.Add(t, collectionConverter);
            }
        }

        private static void AttachConsole()
        {
            try
            {
                var BS = Console.WindowWidth;
                Settings.MyMode = Settings.Mode.Console;
            }
            catch
            {
                Settings.MyMode = Settings.Mode.GUI;
                NativeMethods.AllocateConsole();
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ExitWithException((Exception)e.ExceptionObject);
        }

        private static string DefaultValueString(this Type propType)
        {
            var typeName = TypeSystem.GetElementType(propType);

            //// Provided default value
            var propertyAttribute = propType.GetPropertyAttribute();
            if (propertyAttribute?.DefaultValue != null)
                return propertyAttribute.DefaultValue.ToString();

            if (propType == typeof(string))
                return "abc";

            if (propType == typeof(bool))
                return "T | F";

            if (propType == typeof(int) || propType == typeof(long) || propType == typeof(short))
                return "123";

            if (propType == typeof(decimal) || propType == typeof(double) || propType == typeof(float))
                return "12.34";

            if (propType == typeof(byte))
                return "1 | 0";

            if (propType == typeof(char))
                return "x";

            if (propType == typeof(DateTime))
                return DateTime.Now.ToString("MM-dd-yy h:mm tt");

            if (typeof(ICollection).IsAssignableFrom(propType))
            {
                var p = DefaultValueString(TypeSystem.GetElementType(propType));
                return $"{p}, ...";
            }

            if (Settings.HelpSettings.ShowAllPossibleEnumValues)
                if (propType.IsEnum)
                    return string.Join(" | ", Enum.GetNames(propType));

            if (propType == typeof(FileInfo))
                return @"C:\temp\test.txt";

            if (propType == typeof(DriveInfo))
                return @"C:\";

            if (propType.IsValueType)
                return Activator.CreateInstance(propType).ToString();

            return typeName.ToString();
        }

        [DebuggerStepThrough]
        [DebuggerHidden]
        private static void ExitWithException(Exception ex, Type passedObject = null)
        {
            if ((Settings.HelpSettings.AllowDefaultHelp || Settings.HelpSettings.ShowHelpOnErrors) &&
                passedObject != null)
                PrintHelp(passedObject);

            ErrorsList.Add(ex);

            WriteLine(ConsoleColor.Red, ex.Message);
            WriteLine(ConsoleColor.Red, ex.StackTrace);
            WriteLine("Exiting...");
            PauseIfDebugger();

            ReleaseConsole();
            Environment.Exit(-1);
        }


        [DebuggerStepThrough]
        private static dynamic Get(string input, Type type)
        {
            LoadTypeConverters();

            var converter = TypeDescriptor.GetConverter(type);
            if (converter.CanConvertTo(type))
                return converter.ConvertTo(null, CultureInfo.CurrentCulture, input, type);

            converter = TypeDescriptor.GetConverter(type);
            if (converter.CanConvertFrom(typeof(string)))
                return converter.ConvertFrom(null, CultureInfo.CurrentCulture, input);

            if (type.GenericTypeArguments.Length > 0 || InternalTypeConverters.Any(c => c.Value.CanConvertTo(type)))
            {
                var asd = InternalTypeConverters.FirstOrDefault(c => c.Value.CanConvertTo(type));

                var key = asd.Key;
                var myConverter = asd.Value;

                if (key != null && myConverter != null)
                    return myConverter.ConvertTo(null, CultureInfo.CurrentCulture, input, type);
            }

            return Convert.ChangeType(input, type);
        }

        private static IEnumerable<dynamic> GetChildrenOf(Type type)
        {
            return type.Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(type) && !t.IsAbstract)
                .Select(t => Activator.CreateInstance(t));
        }

        private static CmdClassAttribute GetClassAttribute(this Type type)
        {
            var attributes = type.GetCustomAttributes(false);
            var desiredAtt = attributes.FirstOrDefault(a => a.GetType() == typeof(CmdClassAttribute));
            return desiredAtt as CmdClassAttribute;
        }

        private static CmdGroup GetCmdGroup(this string input)
        {
            if (input == null) return null;
            var switchChar = Settings.CmdGroupSettings.SwitchIdentifiers.FirstOrDefault(sw => sw.Equals(input[0]));

            return new CmdGroup
            {
                SwitchIdentifier = switchChar,
                Switch = input.Remove(0, 1),
                Argument = string.Empty
            };
        }

        private static IEnumerable<dynamic> GetGenericChildrenOf(Type type)
        {
            return from x in Assembly.GetExecutingAssembly().GetTypes()
                   let y = x.BaseType
                   where !x.IsAbstract && !x.IsInterface &&
                         y != null && y.IsGenericType &&
                         y.GetGenericTypeDefinition() == type
                   select Activator.CreateInstance(x);
        }

        private static IEnumerable<PropertyInfo> GetProperties(this object obj)
        {
            var type = obj as Type;
            return type?.GetProperties().Where(info => info.CanRead && info.CanWrite) ??
                   obj.GetType().GetProperties().Where(info => info.CanRead && info.CanWrite);
        }

        private static CmdPropertyAttribute GetPropertyAttribute(this PropertyInfo prop)
        {
            var attributes = prop.GetCustomAttributes();
            var desiredAtt = attributes.FirstOrDefault(a => a.GetType() == typeof(CmdPropertyAttribute));
            return desiredAtt as CmdPropertyAttribute;
        }

        private static CmdPropertyAttribute GetPropertyAttribute(this Type type)
        {
            var attributes = type.GetCustomAttributes(true);
            var desiredAtt = attributes.FirstOrDefault(a => a.GetType() == typeof(CmdPropertyAttribute));
            return desiredAtt as CmdPropertyAttribute;
        }

        private static bool IsSwitch(this string input)
        {
            return !string.IsNullOrEmpty(input) && Settings.CmdGroupSettings.SwitchIdentifiers.Any(c => input[0] == c);
        }

        private static void LoadTypeConverters()
        {
            if (HaveLoadedTypeConverters) return;

            if (Settings.UseInternalTypeConverters)
            {
                foreach (var collectionConverters in GetChildrenOf(typeof(CmdCollectionConverterBase)))
                    AddCollectionConverter(collectionConverters);

                foreach (var converter in GetGenericChildrenOf(typeof(StringConverterBase<>)))
                    InternalTypeConverters.Add(converter.TypeIConvert, converter);

                foreach (var converter in InternalTypeConverters)
                {
                    TypeDescriptor.AddAttributes(converter.Key,
                        new TypeConverterAttribute(converter.Value.GetType()));
                }
            }

            foreach (var converter in CustomTypeConverters)
            {
                TypeDescriptor.AddAttributes(converter.Key, new TypeConverterAttribute(converter.Value.GetType()));
            }

            HaveLoadedTypeConverters = true;
        }

        private static void PrintHelp(Type passedType)
        {
            var entryAssembly = Assembly.GetEntryAssembly();

            var descriptionAttribute = entryAssembly
                .GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false)
                .OfType<AssemblyDescriptionAttribute>()
                .FirstOrDefault();

            if (descriptionAttribute != null)
                WriteLine(descriptionAttribute.Description);

            var location = Path.GetFileNameWithoutExtension(entryAssembly.Location) + " ";
            Write(location);
            Indent = location.Length;

            var propertiesToInclude = new List<PropertyInfo>();
            var switchString = Settings.CmdGroupSettings.SwitchIdentifiers.First();
            foreach (var prop in passedType.GetProperties().Where(p => p.CanRead && p.CanWrite))
            {
                var att = prop.GetPropertyAttribute();
                if (prop.PropertyType == typeof(SecureString)) continue;
                if (!Settings.AllowAllProperties && att == null) continue;
                if (!Settings.HelpSettings.ShowAllProperties && att == null) continue;

                propertiesToInclude.Add(prop);

                Write(ConsoleColor.White, string.IsNullOrEmpty(att?.ExampleText)
                    ? $"{switchString}{prop.ShortName()}:[{prop.PropertyType.DefaultValueString()}] "
                    : $"{switchString}{prop.ShortName()}:[{att.ExampleText}] ");
            }

            const int propIndent = 2;
            const int descIndent = 6;

            Indent = 0;
            NextLine();
            if (propertiesToInclude.Any())
            {
                WriteLine();
                var maxPropNameLength = propertiesToInclude.Max(p => p.ShortName().Length);
                foreach (var prop in propertiesToInclude)
                {
                    Indent = propIndent;
                    Write(ConsoleColor.White, $"{Settings.CmdGroupSettings.SwitchIdentifiers.First()}{prop.ShortName()}");

                    Indent = maxPropNameLength + descIndent;
                    var att = prop.GetPropertyAttribute();
                    if (att != null && string.IsNullOrEmpty(att.Description) == false)
                    {
                        Write(att.Description);
                    }
                    Indent = 0;
                    Write("\n");
                }

                Indent = propIndent;
                var helpText = Settings.HelpSettings.HelpRequestString.First();
                Write(Settings.CmdGroupSettings.SwitchIdentifiers.First() + helpText);
                Indent = maxPropNameLength + descIndent;
                Write("To display this menu.\n");
                Indent = 0;
            }

            WriteLine();
            var classAtt = passedType.GetClassAttribute();
            if (classAtt != null && string.IsNullOrEmpty(classAtt.HelpText) == false)
            {
                Indent = 2;
                WriteLine(ConsoleColor.Cyan, classAtt.HelpText);
                Indent = 0;
            }
        }

        private static void ReleaseConsole()
        {
            if (Settings.MyMode == Settings.Mode.GUI)
                NativeMethods.FreeConsole();
        }

        private static string ShortName(this PropertyInfo property)
        {
            var dd = property.GetPropertyAttribute();
            return !string.IsNullOrEmpty(dd?.ShortName) ? dd.ShortName : property.Name;
        }

        private static string[] ToLower(this IEnumerable<string> args)
        {
            return args?.Select(s => s.ToLowerInvariant()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

        private static char[] ToLower(this IEnumerable<char> args)
        {
            return args?.Select(char.ToLowerInvariant).ToArray();
        }

        #endregion

        #region Type Converters

        // ReSharper disable UnusedMember.Global
        private class SecureStringConverter : StringConverterBase<SecureString>
        {
            protected override bool Convert(ITypeDescriptorContext context, CultureInfo culture, string value,
                out SecureString newValue)
            {
                newValue = value.ToSecureString();
                return true;
            }
        }

        private class CmdBoolConverter : StringConverterBase<bool>
        {
            protected override bool Convert(ITypeDescriptorContext context, CultureInfo culture, string value,
                out bool newValue)
            {
                switch (value.ToLower())
                {
                    case "false":
                    case "f":
                    case "no":
                    case "n":
                    case "0":
                        newValue = false;
                        return true;
                    case "true":
                    case "t":
                    case "yes":
                    case "y":
                    case "1":
                        newValue = true;
                        return true;
                    default:
                        if (Settings.TreatBoolPresenceAsTrue)
                        {
                            newValue = true;
                            return true;
                        }
                        newValue = false;
                        return false;
                }
            }
        }

        private class FileInfoConverter : StringConverterBase<FileInfo>
        {
            protected override bool Convert(ITypeDescriptorContext context, CultureInfo culture, string value,
                out FileInfo newValue)
            {
                newValue = new FileInfo(value);
                return true;
            }
        }

        private class DirectoryInfoConverter : StringConverterBase<DirectoryInfo>
        {
            protected override bool Convert(ITypeDescriptorContext context, CultureInfo culture, string value,
                out DirectoryInfo newValue)
            {
                newValue = new DirectoryInfo(value);
                return true;
            }
        }

        private class CmdQueueConverter : CmdCollectionConverterBase
        {
            #region Properties & Indexers

            public override IEnumerable<Type> TypesICanConvert
            {
                get { return new List<Type> { typeof(Queue<>), typeof(ConcurrentQueue<>) }; }
            }

            #endregion

            protected override object Convert(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType)
            {
                dynamic obj = Activator.CreateInstance(destinationType);
                foreach (
                    var s in
                        value.SplitInput()
                            .Select(s => ChangeGenericType(s, destinationType.GenericTypeArguments[0])))
                {
                    obj.Enqueue(s);
                }
                return obj;
            }
        }

        private class CmdStackConverter : CmdCollectionConverterBase
        {
            #region Properties & Indexers

            public override IEnumerable<Type> TypesICanConvert
            {
                get { return new List<Type> { typeof(Stack<>), typeof(ConcurrentStack<>) }; }
            }

            #endregion

            protected override object Convert(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType)
            {
                dynamic obj = Activator.CreateInstance(destinationType);
                foreach (
                    var s in
                        value.SplitInput()
                            .Select(s => ChangeGenericType(s, destinationType.GenericTypeArguments[0])))
                {
                    obj.Push(s);
                }
                return obj;
            }
        }

        private class CmdListConverter : CmdCollectionConverterBase
        {
            #region Properties & Indexers

            public override IEnumerable<Type> TypesICanConvert
            {
                get
                {
                    return new List<Type>
                    {
                        typeof (ArrayList),
                        typeof (List<>),
                        typeof (HashSet<>),
                        typeof (LinkedList<>),
                        typeof (SortedSet<>),
                        typeof (ConcurrentBag<>),
                        typeof (BlockingCollection<>),
                        typeof (BindingList<>)
                    };
                }
            }

            #endregion

            protected override object Convert(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType)
            {
                dynamic obj = Activator.CreateInstance(destinationType);

                if (destinationType.IsGenericType)
                    foreach (
                        var s in
                            value.SplitInput()
                                .Select(s => ChangeGenericType(s, destinationType.GenericTypeArguments[0])))
                    {
                        if (destinationType.GetGenericTypeDefinition() == typeof(LinkedList<>))
                            obj.AddLast(s);
                        else
                            obj.Add(s);
                    }
                else
                {
                    foreach (var s in SplitString(value.ToString()))
                    {
                        obj.Add(s);
                    }
                }
                return obj;
            }
        }

        private class CmdArrayConverter : CmdCollectionConverterBase
        {
            #region Properties & Indexers

            public override IEnumerable<Type> TypesICanConvert
            {
                get { return new List<Type> { typeof(Array) }; }
            }

            #endregion

            protected override object Convert(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType)
            {
                var args = value.SplitInput();
                // var args = SplitString(value.ToString());

                //var newArgs = new List<string>();
                //if (typeof (int).IsAssignableFrom(destinationType.GetElementType()))
                //    foreach (var s in args.Where(s => value.ToString().Contains("-")))
                //    {
                //        newArgs.AddRange(MakeIntRange(s));
                //    }
                //else newArgs = args.ToList();


                dynamic array = Array.CreateInstance(destinationType.GetElementType(), args.Count);
                for (var i = 0; i < args.Count; i++)
                {
                    array[i] = ChangeGenericType(args[i], destinationType.GetElementType());
                }
                return array;
            }
        }

        private static List<string> SplitInput(this object value)
        {
            var v = value.ToString().Split(Settings.ListSeparator);

            var list = new List<string>();
            foreach (var s in v)
            {
                if (!s.Contains("-"))
                    list.Add(s);
                else
                {
                    list.AddRange(MakeIntRange(s));
                }
            }
            return list;
        }


        private static string[] MakeIntRange(string input)
        {
            var result = input.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (result.Length < 2) return result; //.Cast<int>();
            if (result.All(r => r.All(char.IsDigit)) == false || result.Length > 2)
                ExitWithException(new ArgumentException($"Connot convert {input} to IEnumerable<int>"));

            var intResult = result.Select(r => Int32.Parse(r)).ToArray();
            return Enumerable.Range(intResult[0], (intResult[1] - intResult[0]) + 1).Select(s => s.ToString()).ToArray();
        }

        private static bool IsNumericType(this Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }

        // ReSharper restore UnusedMember.Global

        private abstract class CmdCollectionConverterBase : CollectionConverter
        {
            #region Properties & Indexers

            public abstract IEnumerable<Type> TypesICanConvert { get; }

            #endregion

            /// <summary>
            ///     Returns whether this converter can convert an object of the given type to the type of this converter, using the
            ///     specified context.
            /// </summary>
            /// <returns>
            ///     true if this converter can perform the conversion; otherwise, false.
            /// </returns>
            /// <param name="context">An <see cref="T:System.ComponentModel.ITypeDescriptorContext" /> that provides a format context. </param>
            /// <param name="sourceType">A <see cref="T:System.Type" /> that represents the type you want to convert from. </param>
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
            }

            /// <summary>
            ///     Returns whether this converter can convert the object to the specified type, using the specified context.
            /// </summary>
            /// <returns>
            ///     true if this converter can perform the conversion; otherwise, false.
            /// </returns>
            /// <param name="context">An <see cref="T:System.ComponentModel.ITypeDescriptorContext" /> that provides a format context. </param>
            /// <param name="destinationType">A <see cref="T:System.Type" /> that represents the type you want to convert to. </param>
            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                return TypesICanConvert.Any(t => IsSubclassOfRawGeneric(t, destinationType)) ||
                       base.CanConvertTo(context, destinationType);
            }

            /// <summary>
            ///     Converts the given value object to the specified destination type.
            /// </summary>
            /// <returns>
            ///     An <see cref="T:System.Object" /> that represents the converted value.
            /// </returns>
            /// <param name="context">An <see cref="T:System.ComponentModel.ITypeDescriptorContext" /> that provides a format context. </param>
            /// <param name="culture">The culture to which <paramref name="value" /> will be converted.</param>
            /// <param name="value">
            ///     The <see cref="T:System.Object" /> to convert. This parameter must inherit from
            ///     <see cref="T:System.Collections.ICollection" />.
            /// </param>
            /// <param name="destinationType">The <see cref="T:System.Type" /> to convert the value to. </param>
            /// <exception cref="T:System.ArgumentNullException"><paramref name="destinationType" /> is null. </exception>
            /// <exception cref="T:System.NotSupportedException">The conversion cannot be performed. </exception>
            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType)
            {
                if (TypesICanConvert.Any(t => IsSubclassOfRawGeneric(t, destinationType)))
                    return Convert(context, culture, value, destinationType);
                else return base.ConvertTo(context, culture, value, destinationType);
            }


            protected abstract object Convert(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType);

            internal static dynamic ChangeGenericType(object obj, Type type)
            {
                return Get(obj.ToString().TrimStart(), type);
            }

            internal static string[] SplitString(string input)
            {
                return input.Split(Settings.ListSeparator);
            }

            private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
            {
                while (toCheck != null && toCheck != typeof(object))
                {
                    var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                    if (generic == cur)
                    {
                        return true;
                    }
                    toCheck = toCheck.BaseType;
                }
                return false;
            }
        }

        private abstract class StringConverterBase<T> : TypeConverter // where T:class,new()
        {
            #region Properties & Indexers

            public Type TypeIConvert
            {
                get { return typeof(T); }
            }

            #endregion

            /// <summary>
            ///     Returns whether this converter can convert an object of the given type to the type of this converter, using the
            ///     specified context.
            /// </summary>
            /// <returns>
            ///     true if this converter can perform the conversion; otherwise, false.
            /// </returns>
            /// <param name="context">An <see cref="T:System.ComponentModel.ITypeDescriptorContext" /> that provides a format context. </param>
            /// <param name="sourceType">A <see cref="T:System.Type" /> that represents the type you want to convert from. </param>
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
            }

            /// <summary>
            ///     Returns whether this converter can convert the object to the specified type, using the specified context.
            /// </summary>
            /// <returns>
            ///     true if this converter can perform the conversion; otherwise, false.
            /// </returns>
            /// <param name="context">An <see cref="T:System.ComponentModel.ITypeDescriptorContext" /> that provides a format context. </param>
            /// <param name="destinationType">A <see cref="T:System.Type" /> that represents the type you want to convert to. </param>
            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                return destinationType == typeof(T) || base.CanConvertTo(context, destinationType);
            }

            /// <summary>
            ///     Converts the given object to the type of this converter, using the specified context and culture information.
            /// </summary>
            /// <returns>
            ///     An <see cref="T:System.Object" /> that represents the converted value.
            /// </returns>
            /// <param name="context">An <see cref="T:System.ComponentModel.ITypeDescriptorContext" /> that provides a format context. </param>
            /// <param name="culture">The <see cref="T:System.Globalization.CultureInfo" /> to use as the current culture. </param>
            /// <param name="value">The <see cref="T:System.Object" /> to convert. </param>
            /// <exception cref="T:System.NotSupportedException">The conversion cannot be performed. </exception>
            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                T returnValue;

                return Convert(context, culture, (string)value, out returnValue)
                    ? returnValue
                    : base.ConvertFrom(context, culture, value);
            }

            /// <summary>
            ///     Converts the given value object to the specified type, using the specified context and culture information.
            /// </summary>
            /// <returns>
            ///     An <see cref="T:System.Object" /> that represents the converted value.
            /// </returns>
            /// <param name="context">An <see cref="T:System.ComponentModel.ITypeDescriptorContext" /> that provides a format context. </param>
            /// <param name="culture">
            ///     A <see cref="T:System.Globalization.CultureInfo" />. If null is passed, the current culture is
            ///     assumed.
            /// </param>
            /// <param name="value">The <see cref="T:System.Object" /> to convert. </param>
            /// <param name="destinationType">The <see cref="T:System.Type" /> to convert the <paramref name="value" /> parameter to. </param>
            /// <exception cref="T:System.ArgumentNullException">The <paramref name="destinationType" /> parameter is null. </exception>
            /// <exception cref="T:System.NotSupportedException">The conversion cannot be performed. </exception>
            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType)
            {
                try
                {
                    T returnValue;
                    Convert(context, culture, (string)value, out returnValue);
                    return returnValue;
                }
                catch (Exception)
                {
                    return base.ConvertTo(context, culture, value, destinationType);
                }
            }

            protected abstract bool Convert(ITypeDescriptorContext context, CultureInfo culture, string value,
                out T newValue);
        }

        #endregion

        #region Event Methods

        /// <summary>
        ///     Occurs when [help requested].
        /// </summary>
        public static event EventHandler HelpRequested;

        /// <summary>
        ///     Occurs when [ignored parameters].
        /// </summary>
        public static event IgnoredParameterDelegate IgnoredParameters;

        /// <summary>
        ///     Occurs when [parsing error].
        /// </summary>
        public static event ParsingErrorDelegate ParsingError;

        #region Delegates

        /// <summary>
        ///     Delegate IgnoredParameterDelegate
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        public delegate void IgnoredParameterDelegate(object sender, IgnoredParameterArgs e);

        /// <summary>
        ///     Delegate ParsingErrorDelegate
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        public delegate void ParsingErrorDelegate(object sender, ParsingErrorArgs e);

        #endregion

        #region EventArgs

        #region Nested type: IgnoredParameterArgs

        public class IgnoredParameterArgs : EventArgs
        {
            #region Constructors

            public IgnoredParameterArgs(CmdGroup[] invalidCmdGroups)
            {
                InvalidCmdGroups = invalidCmdGroups;
            }

            #endregion

            #region Properties & Indexers

            public CmdGroup[] InvalidCmdGroups { get; private set; }

            #endregion
        }

        #endregion

        #region Nested type: ParsingErrorArgs

        public class ParsingErrorArgs : EventArgs
        {
            #region Constructors

            public ParsingErrorArgs(string input, PropertyInfo property, Exception exception)
            {
                Input = input;
                ParseException = exception;
                PropertyBeingParsed = property;
            }

            #endregion

            #region Fields

            private object _correctedValue;

            #endregion

            #region Properties & Indexers

            public object CorrectedValue
            {
                get { return _correctedValue; }
                set
                {
                    Handled = true;
                    _correctedValue = value;
                }
            }

            public string Input { get; private set; }
            public Exception ParseException { get; private set; }
            public PropertyInfo PropertyBeingParsed { get; private set; }
            internal bool Handled { get; private set; }

            #endregion
        }

        #endregion

        #endregion

        private static void OnHelpRequested()
        {
            HelpRequested?.Invoke(null, EventArgs.Empty);
        }

        private static void OnInvalidParameters()
        {
            IgnoredParameters?.Invoke(null, new IgnoredParameterArgs(IgnoredCmdGroups.ToArray()));
        }

        private static void OnParsingError(ParsingErrorArgs e)
        {
            ParsingError?.Invoke(null, e);

            if (!e.Handled && Settings.ThrowOnParseException)
                ExitWithException(new ParsingException(e.Input, e.PropertyBeingParsed, e.ParseException));
        }

        #endregion

        #region Exceptions

        #region Nested type: IgnoredParameterException

        /// <summary>
        ///     Class IgnoredParameterException.
        /// </summary>
        /// <seealso cref="System.Exception" />
        [Serializable]
        public class IgnoredParameterException : Exception
        {
            #region Constructors

            /// <summary>
            ///     Initializes a new instance of the <see cref="IgnoredParameterException" /> class.
            /// </summary>
            /// <param name="invalidCmdGroups">The invalid command groups.</param>
            public IgnoredParameterException(CmdGroup[] invalidCmdGroups)
            {
                InvalidCmdGroups = invalidCmdGroups;
            }

            #endregion

            #region Properties & Indexers

            /// <summary>
            ///     Gets the invalid command groups.
            /// </summary>
            /// <value>The invalid command groups.</value>
            public CmdGroup[] InvalidCmdGroups { get; private set; }

            /// <summary>
            ///     Gets a message that describes the current exception.
            /// </summary>
            /// <returns>
            ///     The error message that explains the reason for the exception, or an empty string ("").
            /// </returns>
            public override string Message
            {
                get
                {
                    var sb = new StringBuilder();
                    foreach (var cmdGroup in InvalidCmdGroups)
                    {
                        sb.AppendLine($"Group {cmdGroup.Switch} {cmdGroup.Argument} not recognised.");
                    }
                    return sb.ToString().TrimEnd();
                }
            }

            #endregion
        }

        #endregion

        #region Nested type: ParsingException

        /// <summary>
        ///     Class ParsingException.
        /// </summary>
        /// <seealso cref="System.Exception" />
        [Serializable]
        public class ParsingException : Exception
        {
            #region Constructors

            public ParsingException(string input, PropertyInfo propertyBeingParsed, Exception innerException)
                : base($"Cannot parse [{propertyBeingParsed.Name}] from input \"{input.Trim()}\"", innerException)
            {
                Input = input;
                PropertyBeingParsed = propertyBeingParsed;
            }

            #endregion

            #region Properties & Indexers

            /// <summary>
            ///     Gets the input.
            /// </summary>
            /// <value>The input.</value>
            public string Input { get; }

            /// <summary>
            ///     Gets the property being parsed.
            /// </summary>
            /// <value>The property being parsed.</value>
            public PropertyInfo PropertyBeingParsed { get; }

            #endregion

            /// <summary>
            ///     Determines whether the specified object is equal to the current object.
            /// </summary>
            /// <returns>
            ///     true if the specified object  is equal to the current object; otherwise, false.
            /// </returns>
            /// <param name="obj">The object to compare with the current object. </param>
            public override bool Equals(object obj)
            {
                if (obj is ParsingException)
                    return Equals((ParsingException)obj);
                return false;
            }

            /// <summary>
            ///     Serves as the default hash function.
            /// </summary>
            /// <returns>
            ///     A hash code for the current object.
            /// </returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Input?.GetHashCode() ?? 0) * 397) ^ (PropertyBeingParsed?.GetHashCode() ?? 0);
                }
            }

            public override string ToString()
            {
                return $"Cannot parse {PropertyBeingParsed.Name} from input {Input}";
            }

            private bool Equals(ParsingException other)
            {
                return string.Equals(Input, other.Input) && Equals(PropertyBeingParsed, other.PropertyBeingParsed) &&
                       InnerException.Message.Equals(other.InnerException.Message);
            }
        }

        #endregion

        #endregion
    }

    /// <summary>
    ///     Contains various methods to ease console application development
    /// </summary>
    internal static class ConsoleHelper
    {
        #region Constructors

        static ConsoleHelper()
        {
            InternalConsoleWriter = new ConsoleWriter();
        }

        #endregion

        #region Static Fields and Constants

        private static readonly ConsoleWriter InternalConsoleWriter;

        #endregion

        #region Properties & Indexers

        public static bool EnablePaging { get; set; }

        public static int Indent
        {
            get { return InternalConsoleWriter.Indent; }
            set { InternalConsoleWriter.Indent = value; }
        }

        public static ConsoleText PagingText { get; set; } = "-- Press any key to continue --";

        #endregion

        #region Public Methods

        /// <summary>
        ///     Draws a horizontal line.
        /// </summary>
        /// <param name="lineChar">The line character.</param>
        /// <param name="color">The color.</param>
        /// <param name="length">The length.</param>
        public static void HorizontalLine(char lineChar = '-', ConsoleColor? color = null, int length = -1)
        {
            if (length == -1)
                length = Console.WindowWidth - Indent;

            for (var i = 0; i < length; i++)
            {
                if (color == null)
                    Write(lineChar.ToString());
                else
                    Write(color.Value, lineChar.ToString());
            }
        }

        /// <summary>
        ///     Moves to the next line.
        /// </summary>
        public static void NextLine()
        {
            if (Console.CursorTop + 1 >= Console.BufferHeight)
                Console.Write("\n");
            else
                Console.SetCursorPosition(Indent, Console.CursorTop + 1);
        }

        /// <summary>
        ///     Starts a spinner with the specified prompt.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <param name="workerAction">The worker action.</param>
        public static void Spinner(ConsoleText prompt, Action workerAction)
        {
            Spinner(prompt, new List<ConsoleText> { " ", ".", "..", "..." },
                new ConsoleText("=> Complete\n") { ForegroundColor = ConsoleColor.Yellow }, workerAction);
        }

        /// <summary>
        ///     Starts a spinner with the specified prompt.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <param name="slides">The slides.</param>
        /// <param name="completionText">The completion text.</param>
        /// <param name="workerAction">The worker action.</param>
        public static void Spinner(string prompt, IList<string> slides, string completionText, Action workerAction)
        {
            var ctSlides = slides.Select(slide => new ConsoleText(slide)).ToList();
            Spinner(new ConsoleText(prompt), ctSlides,
                new ConsoleText(completionText) { ForegroundColor = ConsoleColor.Yellow }, workerAction);
        }

        /// <summary>
        ///     Starts a spinner with the specified prompt.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <param name="slides">The slides.</param>
        /// <param name="completionText">The completion text.</param>
        /// <param name="workerAction">The worker action.</param>
        public static void Spinner(ConsoleText prompt, IList<ConsoleText> slides, ConsoleText completionText,
            Action workerAction)
        {
            if (!String.IsNullOrEmpty(prompt.Text))
                Write(prompt);

            var task = Task.Factory.StartNew(workerAction);
            var maxSlideLength = slides.Max(s => s.Text.Length);
            Console.CursorVisible = false;

            while (!task.IsCompleted)
            {
                foreach (var slide in slides)
                {
                    slide.Text = slide.Text.PadRight(maxSlideLength);

                    Thread.Sleep(100);
                    Write(slide);
                    Console.SetCursorPosition(Console.CursorLeft - slide.Text.Length, Console.CursorTop);
                    if (task.IsCompleted) break;
                }
            }

            Console.CursorVisible = true;
            Write(completionText);
            Console.SetCursorPosition(Indent, Console.CursorTop + 1);
            task.GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Starts a dashed spinner with the specified prompt.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <param name="workerAction">The worker action.</param>
        public static void SpinnerDashes(string prompt, Action workerAction)
        {
            Spinner(new ConsoleText(prompt), new List<ConsoleText> { "-", "\\", "|", "/" },
                new ConsoleText("=> Complete") { ForegroundColor = ConsoleColor.Yellow }, workerAction);
        }

        /// <summary>
        ///     Starts a period spinner with the specified prompt.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <param name="workerAction">The worker action.</param>
        public static void SpinnerDots(string prompt, Action workerAction)
        {
            Spinner(new ConsoleText(prompt), new List<ConsoleText> { " ", ".", "..", "..." },
                new ConsoleText("=> Complete\n") { ForegroundColor = ConsoleColor.Yellow }, workerAction);
        }

        /// <summary>
        ///     Draws a vertical line
        /// </summary>
        /// <param name="lineChar">The line character.</param>
        /// <param name="color">The color.</param>
        public static void VerticalLine(char lineChar = '|', ConsoleColor? color = null)
        {
            var horizontal = Console.CursorLeft;
            var vertical = Console.CursorTop;

            for (var i = Console.CursorTop; i < Console.WindowHeight - 1; i++)
            {
                Console.SetCursorPosition(horizontal, i);
                if (color == null)
                    Write(lineChar.ToString());
                else Write(color.Value, lineChar.ToString());
            }

            Console.SetCursorPosition(horizontal + 1, vertical);
        }

        /// <summary>
        ///     Writes the input vertically.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="args">The arguments.</param>
        public static void WriteVertical(string text, params object[] args)
        {
            var horizontal = Console.CursorLeft;
            var vertical = Console.CursorTop;
            var i = vertical;
            var y = horizontal;

            text = String.Format(text, args);
            text = text.Replace("\t", "    ");
            var lines = text.Split('\n', '\r');

            foreach (var line in lines)
            {
                Console.SetCursorPosition(y, vertical);
                foreach (var c in line)
                {
                    Write(c.ToString());
                    i = i + 1;
                    Console.SetCursorPosition(y, i);
                }

                i = vertical;
                y = y + 1;
            }
        }

        /// <summary>
        ///     Writes the input as a vertical line.
        /// </summary>
        /// <param name="text">The text.</param>
        public static void WriteVerticalLine(string text)
        {
            var vertical = Console.CursorTop;
            WriteVertical(text);
            Console.SetCursorPosition(Console.CursorLeft + 1, vertical);
        }

        /// <summary>
        ///     Erases the line.
        /// </summary>
        public static void EraseLine()
        {
            InternalConsoleWriter.EraseLine();
        }

        #endregion

        #region Nested type: AutoComplete

        public class AutoComplete
        {
            #region Constructors

            public AutoComplete(IEnumerable<string> options, AutoCompleteMode? mode = null)
            {
                if (mode != null)
                    Mode = mode.Value;
                TotalOptions = options.ToList();
            }

            #endregion

            #region AutoCompleteMode enum

            public enum AutoCompleteMode
            {
                StartWith,
                Contains
            }

            #endregion

            #region Properties & Indexers

            public AutoCompleteMode Mode { get; set; }
            private List<string> CurrentOptions { get; set; }
            private IEnumerator<string> Enumerator { get; set; }
            private string SearchString { get; set; }

            private List<string> TotalOptions { get; set; }

            #endregion

            public string Next(string searchString)
            {
                if (searchString.Equals(SearchString))
                    return CurrentOptions == null ? null : GetNext();

                SearchString = searchString;

                CurrentOptions = Mode == AutoCompleteMode.StartWith
                    ? TotalOptions.Where(s => s.ToLower().StartsWith(searchString.ToLower())).ToList()
                    : TotalOptions.Where(s => s.ToLower().Contains(searchString.ToLower())).ToList();

                Enumerator = CurrentOptions.GetEnumerator();

                return CurrentOptions == null ? null : GetNext();
            }

            private string GetNext()
            {
                if (!CurrentOptions.Any()) return null;

                if (Enumerator.MoveNext())
                    return Enumerator.Current;

                Enumerator.Reset();
                return GetNext();
            }
        }

        #endregion

        #region Nested type: ConsoleResponse

        public class ConsoleResponse:IDisposable
        {
            #region Constructors

            public ConsoleResponse(ConsoleText prompt)
            {
                Prompt = prompt;
            }

            public ConsoleResponse(ConsoleText prompt, IEnumerable<char> allowChars) : this(prompt)
            {
                AllowedChars = allowChars.ToList();
            }

            public ConsoleResponse(ConsoleText prompt, IEnumerable<string> autoCompleteOptions,
                AutoComplete.AutoCompleteMode? autoCompleteMode) : this(prompt)
            {
                AutoCompleteList = autoCompleteOptions.ToList();

                if (autoCompleteMode.HasValue)
                    AutoCompleteMode = autoCompleteMode.Value;
            }

            public ConsoleResponse(ConsoleText prompt, IEnumerable<string> autoCompleteOptions,
                AutoComplete.AutoCompleteMode? autoCompleteMode, IEnumerable<char> allowedChars)
                : this(prompt, autoCompleteOptions, AutoComplete.AutoCompleteMode.Contains)
            {
                AllowedChars = allowedChars.ToList();
            }

            #endregion

            #region ConsoleResponseType enum

            public enum ConsoleResponseType
            {
                Error,
                Complete,
                Canceled,
                TimedOut
            }

            #endregion

            #region Fields

            public readonly List<char> AllowedChars = new List<char>();

            private bool _timeOutExpired;

            private Timer _timeOutTimer;

            #endregion

            #region Properties & Indexers

            public List<string> AutoCompleteList { get; set; } = new List<string>();
            public AutoComplete.AutoCompleteMode AutoCompleteMode { get; set; }

            public ConsoleText Prompt { get; set; }
            public OptionalySecureText Response { get; private set; }
            public ConsoleResponseType ResponseType { get; private set; }
            public bool AutoCompleteOptionsForced { get; set; } = true;
            #endregion

            public bool GetInput(bool secure = false)
            {
                return GetInput(-1, secure);
            }

            private Dictionary<char, string> AutoCompleteEntries
            {
                get
                {
                    var returnDict = new Dictionary<char, string>();
                    if (AutoCompleteList == null || AutoCompleteList.Any() == false)
                        return returnDict;

                    foreach (var item in AutoCompleteList)
                    {
                     //   var used = false;
                        for (int i = 0; i < item.Length; i++)
                        {
                            var c = char.ToLower(item[i]);
                            if (returnDict.ContainsKey(c)) continue;
                            returnDict.Add(c, item);
                    //        used = true;
                            break;
                        }
                        //Todo Should note/throw here really
                        //if(!used)
                        //    throw new Exception($"Couldn't use option {item}, could not find unique char.");

                    }

                    return returnDict;
                }
            }

            private string ReplaceFirst(string text, string search, string replace)
            {
                if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search) || string.IsNullOrEmpty(replace))
                    return null;

                var pos = text.ToLower().IndexOf(search.ToLower());
                if (pos < 0)
                {
                    return text;
                }
                return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
            }

            public bool GetInput(double timeout, bool secure = false)
            {
                NextLine();
                NextLine();

                Indent = 1;

                if (!string.IsNullOrEmpty(Prompt))
                    WriteLine(Prompt + ": ");
                else
                    Prompt = string.Empty;

                var autoCompleteEntries = AutoCompleteEntries;

                if (secure == false && AutoCompleteList?.Any() == true && AutoCompleteOptionsForced)
                {
                    AllowedChars.Clear();
                    foreach (var s in autoCompleteEntries)
                    {
                        var ss = ReplaceFirst(s.Value, s.Key.ToString(), $"[{char.ToUpperInvariant(s.Key)}]");
                        Write(ConsoleColor.Yellow, $" {ss}  ");
                        AllowedChars.Add(s.Key);
                    }

                    WriteLine();
                }

                Write(ConsoleColor.White, " > ");
                Indent = 4;

                var complete = false;
                string preGuess = null;
                var autoComplete = new AutoComplete(AutoCompleteList, AutoCompleteMode);

                var input = new OptionalySecureText(secure);

                SetupTimeout(timeout);

                while (!complete && _timeOutExpired == false)
                {
                    WaitForInput();

                    if (_timeOutExpired)
                    {
                        WriteLine(ConsoleColor.Yellow, "Timed Out");
                        return false;
                    }

                    var key = Console.ReadKey(true);

                    if (secure == false &&
                        AutoCompleteList?.Any() == true &&
                        AutoCompleteOptionsForced && AllowedChars.Any(c => c == key.KeyChar))
                    {
                        if (key.Key == ConsoleKey.Escape)
                            return false;

                        input.Append(autoCompleteEntries[char.ToLower(key.KeyChar)]);
                        Response = input;
                        Write(key.KeyChar.ToString());
                        return true;
                    }

                    _timeOutTimer?.Stop();
                    switch (key.Key)
                    {
                        case ConsoleKey.Escape:
                            complete = true;
                            input.Clear();
                            ResponseType = ConsoleResponseType.Canceled;
                            return false;
                        case ConsoleKey.Enter:
                            ResponseType = ConsoleResponseType.Complete;
                            complete = true;
                            break;
                        case ConsoleKey.Backspace:
                            if (input.Length > 0)
                                input.Remove(input.Length - 1);

                            Console.Write("\b \b");
                            preGuess = null;
                            break;
                        case ConsoleKey.Tab:
                            if (AutoCompleteOptionsForced) break;
                            if (input.Mode == OptionalySecureText.SecureMode.Insecure && AutoCompleteList.Any())
                            {
                                if (preGuess == null)
                                    preGuess = input.String;
                                else
                                {
                                    input.Clear();
                                    input.Append(preGuess);
                                }

                                var guess = autoComplete.Next(preGuess);

                                if (guess != null)
                                {
                                    EraseLine();
                                    Console.Write(guess);
                                    input.Clear();
                                    input.Append(guess);
                                }
                            }
                            break;
                        case ConsoleKey.LeftArrow:
                        case ConsoleKey.RightArrow:
                        case ConsoleKey.UpArrow:
                        case ConsoleKey.DownArrow:
                            break;
                        default:
                            if (AllowedChars.Any())
                            {
                                if (AllowedChars.Any(c => c == key.KeyChar))
                                {
                                    input.Append(key.KeyChar);
                                    Console.Write(input.Mode == OptionalySecureText.SecureMode.Insecure
                                        ? key.KeyChar
                                        : '*');
                                }
                            }
                            else
                            {
                                input.Append(key.KeyChar);
                                Console.Write(input.Mode == OptionalySecureText.SecureMode.Insecure ? key.KeyChar : '*');
                            }
                            preGuess = null;
                            break;
                    }
                }

                Indent = 0;
                Response = input;
                return true;
            }

            private void SetupTimeout(double timeout)
            {
                if (!(timeout > 0)) return;

                _timeOutTimer = new Timer(timeout);

                ElapsedEventHandler handler = null;
                handler = (a, b) =>
                {
                    _timeOutTimer.Enabled = false;
                    _timeOutExpired = true;
                    _timeOutTimer.Elapsed -= handler;
                    ResponseType = ConsoleResponseType.TimedOut;
                };

                _timeOutTimer.Elapsed += handler;
                _timeOutTimer.Start();
            }

            private void WaitForInput()
            {
                while (!Console.KeyAvailable && _timeOutExpired == false)
                    Thread.Sleep(50);
            }

            #region Predefined Char Ranges

            public static IEnumerable<char> NumericChars
                => new[] { '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '-' };

            public static IEnumerable<char> DecimalChars
            {
                get
                {
                    var allowed = new List<char>() { '.' };
                    allowed.AddRange(NumericChars);
                    return allowed;
                }
            }

            public static IEnumerable<char> AlphaChars
                => "abcdefghijklmnopqrstuvwxyz";

            #endregion

            /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
            public void Dispose()
            {
               _timeOutTimer.Dispose();
            }
        }

        #endregion

        #region Nested type: OptionalySecureText

        public class OptionalySecureText
        {
            #region Constructors

            public OptionalySecureText(SecureMode mode)
            {
                Mode = mode;
                switch (mode)
                {
                    case SecureMode.Secure:
                        SecureString = new SecureString();
                        break;
                    default:
                        _stringBuilder = new StringBuilder();
                        break;
                }
            }

            public OptionalySecureText(bool secure = false)
            {
                switch (secure)
                {
                    case true:
                        Mode = SecureMode.Secure;
                        SecureString = new SecureString();
                        break;
                    default:
                        Mode = SecureMode.Insecure;
                        _stringBuilder = new StringBuilder();
                        break;
                }
            }

            #endregion

            #region SecureMode enum

            public enum SecureMode
            {
                Insecure,
                Secure
            }

            #endregion

            #region Fields

            private readonly StringBuilder _stringBuilder;

            #endregion

            #region Properties & Indexers

            public int Length
            {
                get
                {
                    switch (Mode)
                    {
                        case SecureMode.Secure:
                            return SecureString.Length;
                        default:
                            return _stringBuilder.Length;
                    }
                }
            }

            public SecureMode Mode { get; private set; }
            public SecureString SecureString { get; private set; }
            public string String => Mode == SecureMode.Insecure ? _stringBuilder.ToString() : null;

            #endregion

            public void Append(char c)
            {
                switch (Mode)
                {
                    case SecureMode.Secure:
                        SecureString.AppendChar(c);
                        break;
                    default:
                        _stringBuilder.Append(c);
                        break;
                }
            }

            public void Append(IEnumerable<char> chars)
            {
                switch (Mode)
                {
                    case SecureMode.Secure:
                        foreach (var c in chars)
                        {
                            SecureString.AppendChar(c);
                        }
                        break;
                    default:
                        _stringBuilder.Append(chars);
                        break;
                }
            }

            public void Clear()
            {
                switch (Mode)
                {
                    case SecureMode.Secure:
                        SecureString.Clear();
                        break;
                    default:
                        _stringBuilder.Clear();
                        break;
                }
            }

            public void Remove(int index)
            {
                switch (Mode)
                {
                    case SecureMode.Secure:
                        SecureString.RemoveAt(index);
                        break;
                    default:
                        _stringBuilder.Remove(index, 1);
                        break;
                }
            }

            public static OptionalySecureText operator +(OptionalySecureText sct, char c)
            {
                var OptionSecure = sct;
                OptionSecure.Append(c);
                return OptionSecure;
            }

            public static OptionalySecureText operator +(OptionalySecureText sct, IEnumerable<char> chars)
            {
                var OptionSecure = sct;
                OptionSecure.Append(chars);
                return OptionSecure;
            }

            public static explicit operator OptionalySecureText(string input)
            {
                var ost = new OptionalySecureText(SecureMode.Insecure);
                ost.Append(input);
                return ost;
            }

            public static explicit operator OptionalySecureText(SecureString input)
            {
                var ost = new OptionalySecureText(SecureMode.Secure) { SecureString = input };
                return ost;
            }
        }

        #endregion

        #region Nested type: ConsoleText

        public class ConsoleText
        {
            #region Constructors

            public ConsoleText()
            {
                ForegroundColor = Console.ForegroundColor;
                BackgroundColor = Console.BackgroundColor;
            }

            public ConsoleText(string text) : this()
            {
                Text = text;
            }

            public ConsoleText(string text, ConsoleColor foregroundColor) : this()
            {
                Text = text;
                ForegroundColor = foregroundColor;
            }

            public ConsoleText(string text, ConsoleColor foregroundColor, ConsoleColor backgroundColor) : this()
            {
                Text = text;
                ForegroundColor = foregroundColor;
                BackgroundColor = backgroundColor;
            }

            #endregion

            #region Properties & Indexers

            public ConsoleColor BackgroundColor { get; set; }

            public ConsoleColor ForegroundColor { get; set; }
            public string Text { get; set; }

            #endregion

            /// <summary>
            ///     Returns a string that represents the current object.
            /// </summary>
            /// <returns>
            ///     A string that represents the current object.
            /// </returns>
            public override string ToString()
            {
                return Text;
            }

            public static implicit operator ConsoleText(string input)
            {
                return new ConsoleText(input);
            }

            public static implicit operator string(ConsoleText input)
            {
                return input.ToString();
            }
        }

        #endregion

        #region Nested type: ConsoleWriter

        private class ConsoleWriter : TextWriter
        {
            #region Constructors

            public ConsoleWriter()
            {
                _mOldConsole = Console.Out;
                Console.SetOut(this);
            }

            #endregion

            #region Fields

            private int _indent;

            private bool _mDoIndent;
            private readonly TextWriter _mOldConsole;

            private int lastLine = 0;

            #endregion

            #region Properties & Indexers

            public override Encoding Encoding => _mOldConsole.Encoding;

            public int Indent
            {
                get { return _indent; }
                set
                {
                    Console.SetCursorPosition(value, Console.CursorTop);
                    _indent = value;
                }
            }

            private int LineRemaining
            {
                get { return Console.WindowWidth - Console.CursorLeft; }
            }

            #endregion

            /// <summary>
            ///     Writes a formatted string to the text string or stream, using the same semantics as the
            ///     <see cref="M:System.String.Format(System.String,System.Object)" /> method.
            /// </summary>
            /// <param name="format">A composite format string (see Remarks). </param>
            /// <param name="arg0">The object to format and write. </param>
            /// <exception cref="T:System.ArgumentNullException"><paramref name="format" /> is null. </exception>
            /// <exception cref="T:System.ObjectDisposedException">The <see cref="T:System.IO.TextWriter" /> is closed. </exception>
            /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
            /// <exception cref="T:System.FormatException">
            ///     <paramref name="format" /> is not a valid composite format string.-or- The
            ///     index of a format item is less than 0 (zero), or greater than or equal to the number of objects to be formatted
            ///     (which, for this method overload, is one).
            /// </exception>
            public override void Write(string format)
            {
                var wordSplits = format.Split(new[] { ' ' });

                if (LineRemaining < format.Length)
                    for (var i = 0; i < wordSplits.Length; i++)
                    {
                        var wordSplit = wordSplits[i];
                        if (LineRemaining < wordSplit.Length)
                            base.Write("\n");

                        if (i == wordSplits.Length - 1)
                            base.Write(wordSplit);
                        else base.Write(wordSplit + " ");
                    }
                else base.Write(format);
            }

            /// <summary>
            ///     Writes the specified char.
            /// </summary>
            /// <param name="c">The ch.</param>
            public override void Write(char c)
            {
                CheckPaging();

                if (Console.CursorLeft <= Indent)
                    _mDoIndent = true;

                if (_mDoIndent || Console.CursorLeft < Indent)
                {
                    _mDoIndent = false;
                    var start = Console.CursorLeft;

                    for (var i = start; i < Indent; ++i)
                        Console.SetCursorPosition(Console.CursorLeft + 1, Console.CursorTop);
                    //_mOldConsole.Write(" ");
                }
                _mOldConsole.Write(c);
                if (c == '\n') _mDoIndent = true;
            }

            internal void EraseLine()
            {

                var origX = Console.CursorTop;
                Console.CursorVisible = false;

                for (var i = 0; i < Console.WindowWidth - Indent; i++)
                {
                    Console.CursorLeft = i;
                    Console.Write(" ");
                }

                Console.CursorVisible = true;
                Console.SetCursorPosition(0, origX);

                //for (var i = 0; i < Console.WindowWidth - Indent; i++)
                //{
                //    Console.SetCursorPosition(i, Console.CursorTop);
                //    Console.Write(" ");
                //}
                //Console.SetCursorPosition(0, Console.CursorTop);
            }

            private void CheckPaging()
            {
                if (lastLine == Console.CursorTop) return;
                if (!EnablePaging || Console.CursorTop == 0) return;
                if ((Console.CursorTop) % (Console.WindowHeight - 1) != 0) return;

                Console.SetCursorPosition(GetCenteredTextStartingPoint(PagingText), Console.CursorTop);

                EnablePaging = false;

                var origColor = Console.ForegroundColor;
                var origBackColor = Console.BackgroundColor;

                Console.ForegroundColor = PagingText.ForegroundColor;
                Console.BackgroundColor = PagingText.BackgroundColor;
                base.Write(PagingText);
                Console.ForegroundColor = origColor;
                Console.BackgroundColor = origBackColor;

                EnablePaging = true;

                Console.CursorVisible = false;
                Console.ReadKey(true);
                Console.CursorVisible = true;

                lastLine = Console.CursorTop;
                EraseLine();
            }
        }

        #endregion

        #region ReadLine

        public static string ReadLine()
        {
            return ReadLine(ConsoleColor.White, null);
        }

        public static string ReadLine(string prompt, params object[] args)
        {
            return ReadLine(ConsoleColor.White, prompt, args);
        }

        public static string ReadLine(ConsoleColor consoleColor, string prompt, params object[] args)
        {
            try
            {
                if (!String.IsNullOrEmpty(prompt))
                    Write(consoleColor, prompt, args);


                return Console.ReadLine();
            }
            finally
            {
                // Console.CursorVisible = true;
            }
        }

        #endregion

        #region ReadKey

        public static ConsoleKeyInfo ReadKey()
        {
            return ReadKey(ConsoleColor.White, null);
        }

        public static ConsoleKeyInfo ReadKey(string prompt, params object[] args)
        {
            return ReadKey(ConsoleColor.White, prompt, args);
        }

        public static ConsoleKeyInfo ReadKey(ConsoleColor consoleColor, string prompt, params object[] args)
        {
            try
            {
                if (!String.IsNullOrEmpty(prompt))
                    Write(consoleColor, prompt, args);

                Console.CursorVisible = false;
                return Console.ReadKey(true);
            }
            finally
            {
                Console.CursorVisible = true;
            }
        }

        public static void ReadKey(ConsoleKey consoleKey)
        {
            ReadKeys(new[] { consoleKey }, ConsoleColor.White, null);
        }

        public static void ReadKey(ConsoleKey consoleKey, string prompt, params object[] args)
        {
            ReadKeys(new[] { consoleKey }, ConsoleColor.White, prompt, args);
        }

        public static void ReadKey(ConsoleKey consoleKey, ConsoleColor consoleColor, string prompt,
            params object[] args)
        {
            ReadKeys(new[] { consoleKey }, consoleColor, prompt, args);
        }

        #endregion

        #region ReadKeys

        public static void ReadKeys(IList<ConsoleKey> consoleKeys)
        {
            ReadKeys(consoleKeys, ConsoleColor.White, null);
        }

        public static void ReadKeys(IList<ConsoleKey> consoleKeys, string prompt, params object[] args)
        {
            ReadKeys(consoleKeys, ConsoleColor.White, prompt, args);
        }

        public static void ReadKeys(IList<ConsoleKey> consoleKeys, ConsoleColor consoleColor, string prompt,
            params object[] args)
        {
            try
            {
                Console.CursorVisible = false;

                if (!String.IsNullOrEmpty(prompt))
                    Write(consoleColor, prompt, args);

                while (consoleKeys.Any(key => Console.ReadKey(true).Key != key)) { }
            }
            finally
            {
                Console.CursorVisible = true;
            }
        }

        #endregion

        #region Write

        public static void Write(string message, params object[] args)
        {
            Write(ConsoleColor.White, message, args);
        }

        public static void Write(ConsoleText consoleText)
        {
            var origColor = Console.ForegroundColor;
            var origBColor = Console.BackgroundColor;

            Console.BackgroundColor = consoleText.BackgroundColor;
            Console.ForegroundColor = consoleText.ForegroundColor;
            Console.Write(consoleText);

            Console.BackgroundColor = origBColor;
            Console.ForegroundColor = origColor;
        }

        public static void Write(ConsoleColor color, string message, params object[] args)
        {
            if (string.IsNullOrEmpty(message) == false && (args == null || args.Any() == false))
            {
                message = message.Replace("{", "{{");
                message = message.Replace("}", "}}");
            }

            if (string.IsNullOrEmpty(message)) message = string.Empty;

            Write(new ConsoleText { Text = string.Format(message, args), ForegroundColor = color });
        }

        #endregion

        #region WriteLine

        private static int GetCenteredTextStartingPoint(string text)
        {
            var offset = Indent;
            if (Console.CursorLeft > offset)
                offset = Console.CursorLeft;

            return (Console.WindowWidth + offset - text.Length) / 2;
        }

        public static void WriteLineCentered(ConsoleText consoleText)
        {
            var origOffset = Indent;
            Indent = 0;
            Console.SetCursorPosition(GetCenteredTextStartingPoint(consoleText), Console.CursorTop);
            WriteLine(consoleText);
            Indent = origOffset;
        }

        public static void WriteLineCentered(string message, params object[] args)
        {
            Console.SetCursorPosition(GetCenteredTextStartingPoint(String.Format(message, args)), Console.CursorTop);
            WriteLine(message);
        }

        public static void WriteLine()
        {
            NextLine();
            // Write("\n");
        }

        public static void WriteLine(ConsoleText consoleText)
        {
            consoleText.Text = consoleText.Text; // + "\n";
            Write(consoleText);
            NextLine();
        }

        public static void WriteLine(string message, params object[] args)
        {
            Write(ConsoleColor.White, message, args);
            NextLine();
        }

        public static void WriteLine(ConsoleColor color, string message, params object[] args)
        {
            Write(color, message, args);
            NextLine();
        }

        #endregion
    }
}