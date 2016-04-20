# ConsoleTools
I wanted to create a “single” class that could be added to a WinForm or Console application to allow for a rich user interaction typical with other command line tools. I wanted it to be as simple as “Copy Paste” to add this functionality to an existing project with little to no impact on the business classes within the code base. 
First, this been screaming to be exploded into a multiple independent classes for some time and already has begun to feel a bit ‘unwieldy’ at times but, I have been creating multiple single executable utilities lately that I just couldn’t bear to have additional external dependences on… so it remains and has been useful so far but should still be considered ‘Alpha’ software for the time being.
Another thing I am not “in love with” is right now Command Parser is subscribing to AppDomain.CurrentDomain.UnhandledException printing the exception to the console and exits the environment. Ultimately a fully internal exception flow that doesn’t infringe on the underlying application would likely be the best scenario. 
With all that said…
#CommandParser (Better Name Someday)
Allows string[] args to be passed to the parser with a flexible and generally overridable behaviors. 
The most simple implementation looks like this, var a = args.ParseOnly(); where the args are parsed into a list of “CmdGroup” objects that join switch identifiers, switch text and any arguments provided. 

Note: Arguments are not bound to a space barrier 
IE      -file c: \program files  = -file “c:\Program files”

This version allows for the standard slash ‘/’ or dash ‘-‘ switch identifiers though they are customizable via settings.
Provides property and class attributes to extend functionality with (Required fields, short names, descriptions, ect.) though they are not required for simple parsing configurations. 
Auto builds the help menu based on the type provided to the parser. (Accessible via -help, /help, -?, /? By default, but… they are also customizable. 
Contains default (optional) type converters for SecureString, Bool, FileInfo, Directory, and used standard conversions where applicable. Additionally, various collection conversions are provided as well for Queue, ConcurrentQueue, Stack, ConcurrentStack, ArrayList, List, Hashset, LinkedList, SortedSet, ConcurrentBag, BlockingCollection, BindingList and Aray by allowing a comma separator to be used at input. 
In the case of SecureString the default setting is to force “secure mode” IE ignoring passed parameters for that type and instead prompting the user for that data so it can be read char by char into a SecureString element. This also is overridable via the settings class, should you want a small village to burst into flames… 
The system can be setup via the CommandParser.Settings.PromptForMissingRequired property to prompt the end user at runtime for any forgotten required parameters with an optional timeout of 30 secs. 
Generally during the “missing prompt” the user can enter a string or securestring. If a failed conversion occurs the user will again be prompted for the correct data. Enums are currently configured to show the user allowed responses and can press the “highlighted” char to select it. They can also hit Esc and the parser will return the default value (or null) for that type.
#Advanced Scenario’s
There are multiple events subscriptions available to effect control on what happens in cases like parseException, HelpRequested, IgnoredParameter as well as a good degree of control via the Setting class (and nested classes)
Types can be used to model the parsing against and then a different type can have those parameters loaded into it. 
            var Reg = new RegClass();
            args.ParseAs(typeof (IInterface)).LoadInto(Reg);

A dynamic “cast” is available 
var d = args.ParseAs(typeof(AbstractClass));
var dynObj = d.ToDynamic();
// The line below will throw and Exception if MyInt was not passed in via command line
var myInt = dynObj.MyInt; 

#ConsoleHelper
Due to the amount of console interaction and impressions of end user expectations or “eye candy” like proper indenting, paging, prompts, color, text alignment, response options, etc. I ended up including this class ease and offload much of the ‘grunt’ work. Many of the random “that would be cool” features I added here are used in CommandParser but there are multiple that didn’t ‘fit’ but are available to you should you choose to utilize them.

If anyone finds this useful please drop me a line. Pulls are welcomed, particularly if you would like to contribute.

