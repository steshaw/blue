//-----------------------------------------------------------------------------
//
// File: Options.cs
// Description:
// Options defines the Option class used to manage command-line parameters.
//
//
// History:
//  8/22/2001: JStall:       Created
//
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Collections;
using System.Diagnostics;

namespace Blue.Utilities
{

//-----------------------------------------------------------------------------
// Option handlers can throw this exception 
//-----------------------------------------------------------------------------
internal class OptionErrorException : System.Exception
{
    public OptionErrorException(string stHint) : base(stHint)
    {
    }
}

//-----------------------------------------------------------------------------
// The option class.
//-----------------------------------------------------------------------------
internal class Options : Blue.Public.IOptions
{

#region Construction

    /***************************************************************************\
    *
    * Options.Options
    *
    * Options() initializes a new Options object.
    *
    \***************************************************************************/

    internal 
    Options()
    {
        m_tblOptions        = new Hashtable();        
        m_fIsLocked         = false;
        
        // @dogfood - In delegate, we should be able to say 'Option_Help', not 'this.Option_Help'
        AddHandler(
            "help", "?", 
            new Blue.Public.OptionHandler(this.Option_Help), 
            "provide detailed help on a specific command", 
            "ex:\n"+
            "/help        - print a summary of all commands\n"+
            "/help:define - print detailed help for the 'define' command\n"+
            "/help:*      - print detailed help for all commands\n"
            );
    }

#endregion Construction

#region Error handling
    // The option manager doesn't recognize the following option
    static void PrintError_UnrecognizedOption(string stOption)
    {
        Blue.Driver.StdErrorLog.PrintError(
            new Blue.Driver.GeneralErrorException(
                Blue.Driver.ErrorCodes.cInternalError, 
                null,
                "The option '" + stOption + "' is not recognized"
            )
        );    
    }
    
    static void PrintError_CantOpenResponseFile(string stFilename, System.Exception e)
    {
        Blue.Driver.StdErrorLog.PrintError(
            new Blue.Driver.GeneralErrorException(
                Blue.Driver.ErrorCodes.cCantOpenResponseFile, 
                null,
                "Can't open response file '"+stFilename + "'. " + e.Message
            )
        );
    }
    
    // General error mechanism for options
    static void PrintError_OptionError(string stOption, string stParam, string stHint)
    {
        Blue.Driver.StdErrorLog.PrintError(
            new Blue.Driver.GeneralErrorException(
                Blue.Driver.ErrorCodes.cOptionError,
                null,
                "The option '" + stOption + "' has illegal parameter '" + stParam + "'."+ stHint
            )
        );
    }
    
#endregion

#region Implementation for IOption
    //-----------------------------------------------------------------------------
    // Adds a new handler for the specified option.
    //-----------------------------------------------------------------------------

    public void 
        AddHandler(
        string stOption,                    // Unique flag for Option
        string stShortcut,                  // Optional shortcut (can be null);
        Blue.Public.OptionHandler ha,       // Option Handler
        string stDescription,               // short 1-line description
        string stFullHelp                   // long description
        )        
    {
        Debug.Assert(!m_fIsLocked, "Can't add new optionhandler after we've locked:"+ stDescription);
        
        
        OptionInfo info = new OptionInfo(stOption, stShortcut, ha, stDescription, stFullHelp);
                
        
        if (m_tblOptions[stOption] == null)
        {               
            if (stShortcut != null)
            {
                Debug.Assert(m_tblOptions[stShortcut] == null);
                m_tblOptions[stShortcut] = info;
            }
            m_tblOptions[stOption] = info;            
            
        } 
        else 
        {
            // Since end users can't define option handlers, we should know about
            // any collisions during the debug phase.
            Debug.Assert(false, "Handler already defined");
        }
    }
#endregion

#region methods

    #region OptionInfo class
    // Class to bundle all the option-information together for each option
    // Immutable.
    class OptionInfo
    {
        // Constructor
        public OptionInfo(
            string stOption, 
            string stShortcut,
            Blue.Public.OptionHandler ha,
            string stDescription,
            string stFullHelp
        )            
        {
            Debug.Assert(stOption != null, "Must specify option");
            Debug.Assert(ha != null, "Must have valid handler");
            Debug.Assert(stDescription != null, "Must have valid description");
                        
            m_stOption = stOption;
            m_stShortcut = stShortcut; // can be null
            m_ha = ha;
            m_stDescription = stDescription;
            m_stFullHelp = (stFullHelp == null) ? stDescription : stFullHelp;
        }
    
        // Data
        readonly string m_stOption;                    // Unique flag for Option
        readonly string m_stShortcut;                  // Optional shortcut (can be null);
        readonly Blue.Public.OptionHandler m_ha;                   // Option Handler
        readonly string m_stDescription;               // short 1-line description
        readonly string m_stFullHelp;                   // long description        
        
        // Properties
        public string Option { get { return m_stOption; } }
        public string Shortcut { get { return m_stShortcut; } }
        public Blue.Public.OptionHandler Handler { get { return m_ha; } }
        public string ShortHelp { get { return m_stDescription; } }
        public string FullHelp { get  { return m_stFullHelp; } }        
    }
    #endregion
    

    //-------------------------------------------------------------------------
    // Dispatch a response file. This will parse the file and call DispatchOption    
    //-------------------------------------------------------------------------
    internal void DispatchResponseFile(string stFilename, ArrayList alFiles)
    {
        //System.IO.TextReader r = new System.IO.StreamReader(stFilename);
        
        TextReader input = null;
        
        try
        {
            input = File.OpenText(stFilename);
        }
        catch(Exception e)
        {
            Options.PrintError_CantOpenResponseFile(stFilename, e);
            return;
        }

        Debug.Assert(input != null);            
            
        string line;
        while((line = input.ReadLine())!= null)
        {
            // Skip comments and blank lines
            if (line.Length <= 1)                
                continue;
            if (line[0] == '#')
                continue;            
                
            // Process
            string [] stArgs = line.Trim(' ').Split(' '); // split by spaces
            foreach(string stArg in stArgs)
                DispatchOption(stArg, alFiles);                
        }
        
    }

    //-------------------------------------------------------------------------    
    // Dispatch a single option
    //-------------------------------------------------------------------------    
    internal void DispatchOption(string stArg, ArrayList alFiles)
    {
        int cch = stArg.Length;
        if (cch >= 2)
        {
            // Repsonse file
            if (stArg[0] == '@')
            {
                 DispatchResponseFile(stArg.Substring(1), alFiles);       
            }
                    
            // Normal option, connected to an OptionHandler
            else if (stArg[0] == '/')
            {
                string stParam = "";
                int i = stArg.IndexOf(':');
                if (i > -1)
                    stParam = stArg.Substring(i + 1);
                                
                string stOption = (i == -1) ? stArg : stArg.Substring(0, i);
                
                OptionInfo info = m_tblOptions[stOption.Substring(1)] as OptionInfo;
                                
                if (info != null)
                {    
                    Blue.Public.OptionHandler handler = info.Handler;
                
                    try
                    {                
                        handler(stParam);                 
                    }
                    catch(OptionErrorException e)
                    {
                        PrintError_OptionError(stOption, stParam, e.Message);
                    }
                } else {                    
                    PrintError_UnrecognizedOption(stOption); 
                }
            }
                    
            // Filename
            else
            {
                alFiles.Add(stArg);
            }
        } else {
        // @todo - we're just ignoring the others, is that ok?
        }
    }

    //-------------------------------------------------------------------------    
    // Options.ProcessCommandLine
    //
    // ProcessCommandLine() process the given command-line parameters, calling
    // registered OptionHandlers for given compiler options and building a list
    // of source files to process.    
    //
    // Return false if an option did useful work (and so the driver can exit)
    // Return true if the options just set properties and so we want to go
    //     ahead and compile.
    //-------------------------------------------------------------------------

    internal bool
    ProcessCommandLine(
        string[] arstArgs,                  // Command-line arguments
        out string[] arstSourceFiles)       // Input files to process
    {
        
        int cArgs           = arstArgs.Length;
        ArrayList alFiles   = new ArrayList();
        arstSourceFiles     = null;

        // Once we process the command line, we're locked and we shouldn't add
        // any new handlers.
        Debug.Assert(!m_fIsLocked, "Only call ProcessCommandLine once");
        m_fIsLocked =true;

        bool fSuccess = false;
                        
        // Check all of the arguments, notifying components as appropriate.        
        // The help handler (/?, /Help) is just a normal registered handler 
        // that can be invoked here.
        for (int idx = 0; idx < cArgs; idx++)
        {
            string stArg = arstArgs[idx];
            DispatchOption(stArg, alFiles);
        }

        // If one of the handlers did useful work already, then we don't need to
        // invoke the compiler.
        if (m_fDidUsefulWork)
            return false;

        //
        // Return the list of files to process.
        //

        if (alFiles.Count > 0)
        {
            arstSourceFiles = (string[]) alFiles.ToArray(typeof(string));
            Debug.Assert(arstSourceFiles != null);
        }

        fSuccess = true;
        
        

        return fSuccess;
    }

    //-----------------------------------------------------------------------------
    // If stOption is a valid command, Provide detailed help.
    // If stOption = "*" then show details on all commands
    // Else print the summary.
    //-----------------------------------------------------------------------------
    private void Option_Help(string stOption)
    {
        // The help command is considered 'useful work'.
        // That means that we may invoke the compiler just using the help command,
        // and we won't get an error for not specifying source files.
        m_fDidUsefulWork = true;
        
        if (stOption == "")
        {
            DisplayHelpSummary();
            return;
        }
        
        Console.WriteLine("Help on help:");
        
        // Print on all commands
        if (stOption == "*")
        {
            foreach(DictionaryEntry x in m_tblOptions)
            {
                OptionInfo info = (OptionInfo) x.Value;
                string stKey = (string) x.Key;
                if (info.Option != stKey)
                    continue; // skip over shortcuts
                PrintDetailedHelp(info);
            }
            return;
        } else {
            // Print on the specified command
            OptionInfo info = (OptionInfo) m_tblOptions[stOption];
            if (info == null)
            {
                Console.WriteLine("'" + stOption + "' is not a command.");
                DisplayHelpSummary();
                return;
            }
            
            PrintDetailedHelp(info);
        }
    }
    
    // Print detailed help on the specific option
    private void PrintDetailedHelp(OptionInfo info)
    {
        Console.WriteLine("command /{0}:", info.Option);
        if (info.Shortcut != null)
            Console.WriteLine("shortcut:/{0}", info.Shortcut);
        Console.WriteLine("short help:{0}", info.ShortHelp);
        Console.WriteLine(info.FullHelp);
        Console.WriteLine();
    }
    
    //-----------------------------------------------------------------------------
    // Display a complete list of all registered handlers.
    //-----------------------------------------------------------------------------
    private void 
    DisplayHelpSummary()
    {
        
        Console.WriteLine("BLUE COMPILER OPTIONS");
        Console.WriteLine();
                
        foreach(DictionaryEntry x in m_tblOptions)
        {
            OptionInfo info = (OptionInfo) x.Value;
            string stKey = (string) x.Key;
            if (info.Option != stKey)
                continue; // skip over shortcuts
            
            string stShortcut = "";
            if (info.Shortcut != null)
            {
                stShortcut = "(/" + info.Shortcut + ")";
            }
            Console.WriteLine("/{0,-10}{1} {2}", info.Option, info.ShortHelp, stShortcut);
        }
        
        // also print help for response files
        Console.WriteLine("{0, -10} specify a response file", "@<file>");
        
        // Print some other general tips
        Console.WriteLine("Use /help:help for more details.");
    }

#endregion Implementation

   
#region Data

    private Hashtable   m_tblOptions;
    private bool        m_fDidUsefulWork;
    
    // For debugging use. Make sure we don't add options after we start processing.
    private bool        m_fIsLocked;
#endregion Data

} // class Options

} // namespace Blue.Util
