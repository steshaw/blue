//-----------------------------------------------------------------------------
// EntryPoint for Blue Compiler
// See http://blogs.msdn.com/jmstall for issues.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using IO = System.IO;

using Blue.Public;
using Utilities = Blue.Utilities;
using CodeGen = Blue.CodeGen;
using ErrorLog = Blue.Utilities.ErrorLog;


// This is a dummy class just to serve as a tag for the XML docs
/// <summary>
/// Blue is a C# compiler written in C#.
/// </summary>
/// <remarks>
/// <para>Blue is an entirely managed C# implementation written 
/// in C# using the .NET runtime and Frameworks.</para>
/// <para>See help on <see cref="Blue.Driver"/> for more details on the implementation</para>
/// </remarks>
public class Help
{
public static void About() { }
}

namespace Blue
{  
#region Driver & helpers
    /// <summary>
    /// The <c>Driver</c> class houses the Main() function and 
    /// controls all other components.
    /// </summary>
    /// <remarks>
    /// The Driver class is the primary client for the interfaces in <see cref="Blue.Public"/>.
    /// It creates the components in the compilation pipeline.
    /// <list type="bullet">
    /// <item><see cref="ILexer"/> - convert source to a token stream </item>
    /// <item><see cref="IParser"/> - convert token stream into AST </item>
    /// <item><see cref="ISemanticChecker"/> - do symbol resolution on the AST </item>
    /// <item><see cref="ICodeGenDriver"/> - emit IL for a resolved-AST </item>    
    /// </list>
    /// It also creates the utility components:
    /// <list type="bullet">
    /// <item><see cref="IOptions"/> - register delegates to handle command line options</item>
    /// <item><see cref="IError"/> - unified error-reporting system for user-errors</item>
    /// <item><see cref="Log"/> - logging facility, mostly for debugging </item>
    /// </list>
    /// </remarks>
    public class Driver
    {
//-----------------------------------------------------------------------------
// Entry point for Blue
//-----------------------------------------------------------------------------
        public static int Main(string[] arstArgs)
        {        
            int iErrorValue = 1;
            
            Init(arstArgs);
            
            // Main work done here. This should not throw an exception.
            // It will print all the error information. When we return from here,
            // the binary should have been compiled, all error handling done, etc.
            
            iErrorValue = MainProxy(arstArgs);
            
            if (StdErrorLog.HasErrors())
                iErrorValue = 2;   
                
            Terminate(iErrorValue);
            
            return iErrorValue;
        }

//-----------------------------------------------------------------------------
// Shutdown
//-----------------------------------------------------------------------------
        static void Terminate(int iErrorValue)
        {
            Log.WriteLine(Log.LF.All, "Shutdown compiler, return value={0}",iErrorValue);
            Log.TerminateLogging();            
        }

//-----------------------------------------------------------------------------
// Initialization
//-----------------------------------------------------------------------------
        static void Init(string [] arstArgs)
        {
            PrintLogo();
            
            // Initialize logging
            Log.InitLogging(arstArgs);
            
            // Log startup info
            #if __BLUE__
                Log.WriteLine(Log.LF.All, "DOGFOOD version compiled by Blue.");            
            #else
                Log.WriteLine(Log.LF.All, "Normal version compiled by C#.");
            #endif
            Log.WriteLine(Log.LF.All, "Startup compiler, args={0}", Log.F(arstArgs));
            
            // Create the error logging facility
            m_ErrorLog = new Utilities.ErrorLog();
            
            // Create the option facility
            m_OptionMananager = new Utilities.Options();
            
            // Let each of the differen components register their option handlers
            AddDriverOptions();
            
            m_codegen = new CodeGen.EmitCodeGen(OptionManager);
        }
        
        // Add the options for the Driver.
        static void AddDriverOptions()            
        {
            OptionManager.AddHandler("xml", null, new OptionHandler(Driver.OptionX),
                "Generate XML debug information", 
                "Emit xml files for the parse tree and symbols.\n"+
                "The parse tree of all source files are dumped into a single xml file.\n" +
                "The following files are generated:\n"+
                "'X_parse.xml' - the parse tree before any resolution.\n"+
                "'X_post.xml'  - the parse tree after resolution. This may display most.\n"+
                "                attributes as well as show the changes in the tree during.\n"+
                "                resolution.\n"+
                "'X_sym.xml'   - a dump of the symbol table, including imported symbols.\n"
                );
                    
            OptionManager.AddHandler("dbg_quit", "_Q", new OptionHandler(Driver.OptionDbgQuit),
                "Debugging: Quit after stage (Lexer, Parser, Resolve)", 
                "[For debugging purposes.]\n"+
                "Quits after the specified compilation stage. Useful in conjuction with\n"+
                "the 'xml' option.\n"+
                "usage:\n"+
                "/_Q:Lexer - quit after scanning. Implicitly output the lexemes.\n"+
                "/_Q:Parser - quit after building the AST, before any resolution.\n"+
                "/_Q:Resolve - quit after symbol resolution, but before codegen.\n"
            );
                
            OptionManager.AddHandler("reference", "r", new OptionHandler(Driver.OptionAddReference),
                "Add assembly reference (/r:myassem)", 
                "Imports the meta data from the specified assembly\n" +
                "/r:XXX will first search the local directory for a file XXX; if not found it\n"+
                "will then search the GAC where XXX is the fully-qualified name; if not found it\n"+
                "will then search where XXX is the partial name.\n"+
                "ex: /r:System"
                );
                
            OptionManager.AddHandler("define", "d", new OptionHandler(Driver.OptionDefineSymbol),
                "Define conditional compilation symbols (/d:<symbol>)", 
                "Adds a symbol to control the preprocessor's #if <symbol> behavior.\n"+
                "This is equivalent to using a #define <symbol> in every source file.\n"+
                "The symbol __BLUE__ is always prefedined."+
                "ex:\n"+
                "/define:DEBUG\n"
                //"/d:Skip_First_thing,AnotherDefine,Third" @todo - allow this
                );
        }
        
#region Data
        // Option manager
        static Utilities.Options m_OptionMananager;
        static Public.IOptions OptionManager {
            get { return m_OptionMananager; }
        }
        
        
        // Codegen
        static ICodeGenDriver m_codegen;
        
        // Predefined symbols for preprocessor        
        static string [] m_defines;
        
        // Expose the error log publicly
        static Utilities.ErrorLog m_ErrorLog;
        //static public Utilities.ErrorLog StdErrorLog
        static public Blue.Public.IError StdErrorLog
        {
            get { return m_ErrorLog; }
        }

#endregion

//-----------------------------------------------------------------------------
// Print the startup info
//-----------------------------------------------------------------------------
        static void PrintLogo()
        {
            Console.Write("Blue Sample Compiler v1.0");            
#if __BLUE__
            Console.WriteLine(" [Dogfood]");
#else
            Console.WriteLine(" [normal]");
#endif      
            Console.WriteLine("by Mike Stall (http://blogs.msdn.com/jmstall)");
            Console.WriteLine("\n");
        }

//-----------------------------------------------------------------------------
// Primary Worker. This drives the different stages.
// Returns the error code for all expected end-user errors.
// Unexpected errors (bugs / holes in the compiler) will throw an exception. 
//-----------------------------------------------------------------------------
        static int MainWorker(string[] arstSourceFiles)
        {   
            if (arstSourceFiles == null)
            {
                PrintError_NoSourceFiles();                        
                return 1;
            }
                                                    
                                        
            // Check if debugging the lexer. Just do first file
            // @todo - should we do all files?
            if (ShouldDebugQuit(EStage.cLexer))
            {
                string stDefault = arstSourceFiles[0];
                System.IO.StreamReader reader = new System.IO.StreamReader(stDefault);
                ILexer lex = new ManualParser.Lexer(stDefault, reader);

                return TestLexer(lex);
            }
               
            //                
            // Lex & Parse all source files. 
            // It doesn't matter which order files are parsed in
            //
            AST.ProgramDecl root = ParseAllFiles(arstSourceFiles);
            if (root == null)
                return 1;
                    
            if (ShouldDebugQuit(EStage.cParser))
                return 0;
                    
                            
            //                
            // Symantic checks:                     
            //
            
            // Must startup codegen so that it can assign clr types
            m_codegen.BeginOutput(arstSourceFiles);
            
            System.Reflection.Assembly [] refs = LoadImportedAssemblies();
            if (StdErrorLog.HasErrors())
                return 19;
            
            ICLRtypeProvider provider = m_codegen.GetProvider(refs);
                                    
            ISemanticChecker check = new SymbolEngine.SemanticChecker();
            bool fCheckOk = check.DoCheck(root, provider, refs);
                    
            if (!fCheckOk)
            {
                return 2;
            }
            
            // Dump parse tree after resolution        
            if (s_fGenerateXML)
            {
                string stOutput = IO.Path.GetFileNameWithoutExtension(arstSourceFiles[0]);
                
                
                System.Xml.XmlWriter oSym = new System.Xml.XmlTextWriter(stOutput + "_sym.xml", null);
                check.DumpSymbolTable(oSym);
            
                System.Xml.XmlWriter oPost = new System.Xml.XmlTextWriter(stOutput + "_post.xml", null);
                AST.Node.DumpTree(root, oPost);
            }
                
            if (ShouldDebugQuit(EStage.cSymbolResolution))
                return 0;

            //
            // Codegen
            //     
            
            m_codegen.DoCodeGen(root);
            
            m_codegen.EndOutput();
        
            // If we make it this far, we have no errors.
            return 0;
        }
        
//-----------------------------------------------------------------------------
// Main wrapper. 
// This doesn't worry about startup/shutdown.
// It wraps the worker, protects for exceptions, and handles errors at the
// top level.
//-----------------------------------------------------------------------------       
        static int MainProxy(string[] arstArgs)
        {            
            // Signify error. Clear error if we make it all the way through
            int iErrorValue = 1;
            
            
            
            // Process the command line. This will call all Option Handlers and
            // produce an array of files to compile. It may also produce errors
            string[] arstSourceFiles = null;
            bool f = m_OptionMananager.ProcessCommandLine(arstArgs, out arstSourceFiles);
            if (!f)
                return 0;
                    
            try
            {                
                // If the options are bad, then print errors & quit now.
                if (StdErrorLog.HasErrors())
                    return 1;
            
                        
                // This may throw an exception. If it does, it's an internal
                // error in Blue.
                iErrorValue = MainWorker(arstSourceFiles);
            }
            catch(Exception e)
            {
                // Catch the exception before we print the error log so
                // that the log includes it.
                PrintError_InternalError(e);
                iErrorValue = 8;
            }
            finally
            {
                m_ErrorLog.PrintFinal();                    
            }
                        
            return iErrorValue;
        } // end Driver.Main


//-----------------------------------------------------------------------------
// Parse multiple source files into a single program.
// The Parser only understands Lexers (as opposed to filenames), so we can't
// give the parser the string array of source files. But we don't want to 
// create the lexers all at once and pass them to the parser either.
// Return null on errors.
//-----------------------------------------------------------------------------
        private static AST.ProgramDecl ParseAllFiles(
            string [] arstSourceFiles
        )
        {
            //AST.ProgramDecl root = null;
            AST.NamespaceDecl [] arFiles = new AST.NamespaceDecl[arstSourceFiles.Length];
            
            bool fHasErrors = false;

            int iFile = 0;
            foreach(string stSourceFile in arstSourceFiles)
            {                        
                string stShort    = IO.Path.GetFileNameWithoutExtension(stSourceFile);

                // Get a lexer for this file                
                System.IO.StreamReader reader = null;
                
                try{
                    reader = new System.IO.StreamReader(stSourceFile);
                } catch(System.Exception e)
                {
                    reader = null;
                    PrintError_CantFindSourceFile(stSourceFile, e);
                    fHasErrors = true;
                }
                
                if (reader == null)
                {                    
                    arFiles[iFile] = null;                    
                } else {
                    ILexer lex = new ManualParser.Lexer(stSourceFile, reader, m_defines);

                    // Parse this file
                    IParser p = new ManualParser.Parser(lex);
                    Debug.Assert(p != null);
                                          
                    // Return null on errors. Continue to try and parse the other source
                    // files so that we can catch as many errors as possible, but 
                    // we won't resolve / codegen anything if it has parse errors.
                    Log.WriteLine(Log.LF.Parser, "Parsing source file:" + stSourceFile);
                    AST.NamespaceDecl nodeFile = p.ParseSourceFile();
                        
                    if (nodeFile == null) 
                    {
                        fHasErrors = true;
                    }

                    // Spit this out even if we have errors; it may be partially helpful.                        
                    if (s_fGenerateXML)
                    {
                        System.Xml.XmlWriter oParse = new System.Xml.XmlTextWriter(stShort + "_parse.xml", null);
                        AST.Node.DumpTree(nodeFile, oParse);
                    }
                    
                    arFiles[iFile] = nodeFile;
                }

            // Add to program                
                iFile++;
            }

            if (fHasErrors)
                return null;
                
            return new AST.ProgramDecl(arFiles);
        }

#region Option Handlers
//-----------------------------------------------------------------------------
// Handle -X option: Show XML
//-----------------------------------------------------------------------------
        private static void 
        OptionX(
            string stOption)
        {
            s_fGenerateXML = true;
            
            if (stOption != "")
                throw new Blue.Utilities.OptionErrorException("No parameter expected");
        }

        private static bool s_fGenerateXML = false;
        
//-----------------------------------------------------------------------------
// Test bed for the lexer
//-----------------------------------------------------------------------------        
        private static int TestLexer(ILexer lex)
        {
            ManualParser.Token t;
            
            Console.WriteLine("Lexer test:");
            do
            {
                t = lex.GetNextToken();
                Console.WriteLine(t.ToString());                
            } while (t.TokenType != ManualParser.Token.Type.cEOF);
            
            return 0;
        }

//-----------------------------------------------------------------------------
// Define symbols for preprocessor
// This may occur multiple times.
//-----------------------------------------------------------------------------
        private static void 
        OptionDefineSymbol(
            string stSymbols
        )
        {
            // stSymbols is a comma separated list of strings
            string [] stArgs = stSymbols.Trim(' ').Split(' ');
            
            if (m_defines == null)
            {
                m_defines = stArgs;
                return;
            } else {
                string [] stTemp = new string[stArgs.Length + m_defines.Length];                
                m_defines.CopyTo(stTemp, 0);
                stArgs.CopyTo(stTemp, m_defines.Length);
                
                m_defines = stTemp;
            }
        }            

//-----------------------------------------------------------------------------
// Add an assembly reference
//-----------------------------------------------------------------------------
        private static void 
        OptionAddReference(
            string stOption
        )
        {
            m_stAsmRefs.Add(stOption);
        }
        
        private static System.Collections.Specialized.StringCollection m_stAsmRefs = 
            new System.Collections.Specialized.StringCollection();
         
        // Return an array of assembly references. 
        // Goes through references set by cmd line and loads them to get real Assemblies
        // Throw an error if there's a problem   
        //
        // We need to be able to handle:
        // /r:System.dll
        private static System.Reflection.Assembly [] LoadImportedAssemblies()
        {
            if (m_AsmRefs != null && m_AsmRefs.Length == m_stAsmRefs.Count)
                return m_AsmRefs;
                
            string stRuntimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();                
                
            m_AsmRefs = new System.Reflection.Assembly[m_stAsmRefs.Count];
            for(int i = 0; i < m_AsmRefs.Length; i++)
            {
                string stFilename = m_stAsmRefs[i];
                
                // We have 3 load functions:
                // LoadFrom(string stFilename) - load the file
                // Load(string stAssemblyName) - load the assembly given the _fullname_,checks the gac
                // LoadWithPartialName(string stName) - checks gac. Load's best match

                System.Reflection.Assembly a = null;
                
                // Check a local dir. Ex: /r:MyThing.dll
                try{                    
                    a = System.Reflection.Assembly.LoadFrom(stFilename);                    
                } 
                catch(System.IO.FileNotFoundException)
                {
                    a = null;
                }
                
                // Checks partial name (includes gac), ex: /r:System
                if (a == null)
                {   
                    try
                    {
                        a = System.Reflection.Assembly.LoadWithPartialName(stFilename);                        
                    }
                    catch(System.IO.FileNotFoundException)
                    {
                        a = null;
                    }
                }
                
                // Check runtime dir, ex: /r:System.dll
                if (a == null)
                {
                    try {
                        a = System.Reflection.Assembly.LoadFrom(stRuntimeDir + "\\" + stFilename);
                    }
                    catch(System.IO.FileNotFoundException)
                    {                
                        a = null;                
                    }
                }

                // @todo - include fusion log?
                if (a == null)
                {
                    PrintError_CantFindAssembly(stFilename);
                }
                                
                m_AsmRefs[i] = a;
            }
            
            return m_AsmRefs;
        }
        
        private static System.Reflection.Assembly [] m_AsmRefs;

//-----------------------------------------------------------------------------
// Option to allow us to quit after certain stages
// (used for debugging purposes)
//-----------------------------------------------------------------------------
        private static void 
            OptionDbgQuit(
            string stOption)
        {
            s_fIsQuitAfterStage = true;
            
            if (stOption == "Lexer")
            {   
                s_eQuitAfterStage = EStage.cLexer;
            } else if (stOption == "Parser")
            {
                s_eQuitAfterStage = EStage.cParser;
            } else if (stOption == "Resolve")
            {
                s_eQuitAfterStage = EStage.cSymbolResolution;
            } else {
                throw new Blue.Utilities.OptionErrorException("Expected 'Lexer', 'Parser', or 'Resolve'");
            }
        }
        
        private static bool s_fIsQuitAfterStage;
        private static EStage s_eQuitAfterStage;
        
        private static bool ShouldDebugQuit(EStage eCurrentStage)
        {
            return s_fIsQuitAfterStage && (s_eQuitAfterStage == eCurrentStage);
        }
        
        private enum EStage {
            cLexer,
            cParser,
            cSymbolResolution,
            cCodeGen,
        }

#endregion

#region Errors        
//-----------------------------------------------------------------------------
// Error handling
//-----------------------------------------------------------------------------        

    internal enum ErrorCodes
    {
        cInternalError,     // Last resort; something bad happened...
        cNoSourceFiles,     // no source specified on command line
        cMissingAssembly,   // can't load an assembly
        cMissingSourceFile, // can't load a source file        
        
        cUnrecognizedOption,    // Specified option is unrecognized
        cCantOpenResponseFile,  // The response file can't be found
        cOptionError,           // General option error
        
    }
    
    internal class GeneralErrorException : ErrorException
    {
        internal GeneralErrorException(ErrorCodes c, FileRange location, string s) : 
            base (c, location, s)
        {
            // All General errors will come through this body.
        }
    }    
    
    // Internal error at some unknown phase. This is really just a top-level
    // catch right before an exception would go unhandled.
    // Pass in the culprit exception.
    protected static void PrintError_InternalError(System.Exception e)
    {    
        m_ErrorLog.PrintError(new GeneralErrorException(
            ErrorCodes.cInternalError, null,
            "Internal error:" + e.GetType().ToString() + "," + e.Message
        ));
        
        string stStackTrace = e.StackTrace;
        Console.WriteLine("Stack Trace:\n{0}", stStackTrace);            
        
        Debug.Assert(false, "Should never get an Internal Error");
    }
    
    // Each sub-system should protect itself with this.
    public static void PrintError_InternalError(System.Exception e, string stPhase)
    {
        m_ErrorLog.PrintError(new GeneralErrorException(
            ErrorCodes.cInternalError, null,
            "Internal error during '"+stPhase+"' phase:" +  e.GetType().ToString() + "," + e.Message
        ));
            
        string stStackTrace = e.StackTrace;
        Console.WriteLine("Stack Trace for internal error:\n{0}", stStackTrace);
        Debug.Assert(false, "Should never get an Internal Error"); 
    }

    public static void PrintError_NoSourceFiles()
    {
        m_ErrorLog.PrintError(new GeneralErrorException(
            ErrorCodes.cNoSourceFiles, null, "No source files"
        ));
    }
    
    public static void PrintError_CantFindAssembly(string stName)
    {
        m_ErrorLog.PrintError(new GeneralErrorException(
            ErrorCodes.cMissingAssembly, null, 
            "Can't find assembly '"+stName + "'"
        ));
    }
    
    public static void PrintError_CantFindSourceFile(string stFilename, System.Exception e)
    {
        m_ErrorLog.PrintError(new GeneralErrorException(        
            ErrorCodes.cInternalError, null,
            "Can't open source file '" + stFilename + "'." + e.Message
        ));
    }
         
#endregion        
        
    } // end class Driver


#endregion Driver

#region Logging Facility
//-----------------------------------------------------------------------------        
// Error logging
// 
//-----------------------------------------------------------------------------        
       
    /// <summary>
    /// The <c>Log</c> class allows all other subsystems to emit log messages.
    /// This is mostly a debugging feature.
    /// </summary>
    /// <remarks>
    /// Activated by command line arguments (independent of the IOption system).
    /// See <see cref="InitLogging"/> for details.
    /// </remarks>
    public class Log
    {   
        /// <summary> Control what gets logged. </summary>
        /// <remarks>
        /// Each member get log to is own <see cref="System.IO.TextWriter"/>.
        /// The buffer may go to the console, a file, or nothing.
        /// </remarks>
        public enum LF
        {
            /// <summary>Verbose information of use to an end-user.</summary>
            Verbose,            
            
            /// <summary> Information from the parser </summary>
            Parser,
            
            /// <summary> Information during resolution </summary>
            Resolve,
            
            /// <summary> Information during codegen </summary>
            CodeGen,
            
            /// <summary> All information </summary>
            All,
            Invalid
        }


        
        //static System.IO.TextWriter m_out;
        static System.IO.TextWriter []  m_out;
    
        static System.IO.TextWriter GetStream(LF e)
        {
            return m_out[(int) e]; 
        }
        
        static void SetStream(LF e, System.IO.TextWriter w)
        {
            if (w == null)
            {
                w = System.IO.TextWriter.Null;
            }
            m_out[(int) e] = w;
        }
        
        // Create the 'all' stream
        static void CreateAllStream()
        {
            // @todo - create a Multi-cast stream of all the others
            SetStream(LF.All, GetStream(LF.CodeGen));
        }
                  
        /// <summary> Initialize the logging system. </summary>
        /// <param name="stArgs">The command line arguments.</param>
        /// <remarks>
        /// Do a prelim sweep of command line args to set streams.
        /// We can't use the ErrorHandling subsystem or the Options Subsystem because
        /// logging is a more fundamental subsystem that the others depend on.
        /// <para>Activate via:
        /// '-l:XXXXX' where XXXXX is {Codegen, Verbose, Parser, Resolve }
        /// Can specify multiple switches</para>
        /// </remarks>
        public static void InitLogging(string [] stArgs)
        {
            //m_out = new System.IO.StreamWriter("blue.log");
            
            // Set all streams to null by default
            m_out = new System.IO.TextWriter[((int) LF.All) + 1];
            for(int i = 0; i < m_out.Length; i++)
                m_out[i] = System.IO.TextWriter.Null;
            
            
            // Check command line switches
            for(int i = 0; i < stArgs.Length; i++)            
            {
                string st = stArgs[i];
                
                if (st.Length < 3)
                    continue;
                    
                if (st[0] != '-' && st[0] != '/')
                    continue;
                    
                if (st[1] != 'l') 
                    continue;
                if (st[2] != ':')
                    continue;
                    
                string stSub = st.Substring(3);
                
                LF lf = LF.Invalid;
                if (stSub == "Codegen") lf = LF.CodeGen;
                else if (stSub == "Verbose") lf = LF.Verbose;
                else if (stSub == "Parser") lf = LF.Parser;
                else if (stSub == "Resolve") lf = LF.Resolve;
                
                if (lf != LF.Invalid)                
                    SetStream(lf, Console.Out);
                    
                // Clear option so that it doesn't get confused with a real option
                stArgs[i] = "";
            }
            //SetStream(LF.Verbose, Console.Out);
            //SetStream(LF.CodeGen, Console.Out);
            
            CreateAllStream();
            
            WriteLine(LF.All, "Logging services initialized");
            
        }
        
        public static void TerminateLogging()
        {
            WriteLine(LF.All, "Terminating Logging services");
            for(int i = 0; i < m_out.Length; i++)
            {
                m_out[i].Close();
                m_out[i] = null;
            }
        }
        
        
        
        // Helper to flatten an array into a single string
        public static string F(params object [] args)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append('{');
            for(int i = 0; i < args.Length; i++)
            {
                if (i != 0)
                    sb.Append(',');                
                sb.Append(args[i]);
            }
            sb.Append('}');
            
            return sb.ToString();
        }
    #region WriteLines        
        public static void WriteLine(LF e, string stFormat)
        {            
            GetStream(e).WriteLine(stFormat);
        }
        public static void WriteLine(LF e, string stFormat, params object [] arg)
        {
            GetStream(e).WriteLine(stFormat, arg);
        }
    #endregion
   
    }
#endregion Logging Facility

} // end namespace Blue


