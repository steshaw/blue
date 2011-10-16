//-----------------------------------------------------------------------------
// Public Interfaces.
//
// These are the interfaces that connect to the main components of the 
// compiler, which the driver will use to manage everything.
//
// This doesn't contain all interfaces throughout the entire compiler, 
// it just needs centralizes the ones used by the driver.
//-----------------------------------------------------------------------------

using SymbolEngine;
using System.Xml;
using System.Reflection;

#if false
// We'd like to place this on the namespace, but C# won't let us.
/// <summary>
/// The Blue.Public namespace contains interfaces for each of the major subsystems
/// in the compiler.
/// </summary>
/// <remarks>
/// Exceptions should not be thrown across subsystem boundaries. Each subsystem
/// can have its own internal exception derived from <see cref="ErrorException"/>
/// used for internal control flow, but those exceptions should be caught and reported
/// to the Error subsystem.
/// </remarks>
#endif
namespace Blue
{
namespace Public
{
#region Lexing & Parsing
    //-----------------------------------------------------------------------------
    // Interace for lexing    
    //-----------------------------------------------------------------------------
    
    /// <summary>
    /// Interface provided by a lexer. This interface provides 1 token 
    /// of lookahead (via peek).
    /// </summary>
    /// <remarks>
    /// Used by an <see cref="Blue.Public.IParser"/> to convert the entire  
    /// text-based input into a token stream. 
    /// <para> The lexer handles EOF by returning the EOF token.
    /// The lexer handles normal errors by returning the Error token, and
    /// all subsequent calls will return EOF. </para>
    /// <para> All preprocessor directives are handled behind the ILexer interface.</para>
    /// <para> The primary implementation is <see cref="ManualParser.Lexer"/></para>
    /// </remarks>
    public interface ILexer
    {
        /// <summary>
        /// Consume the next token in the stream and return it
        /// </summary>        
        ManualParser.Token GetNextToken();

        /// <summary>
        /// Peek at the next token in the stream, but don't consume it (non-invasive)    
        /// </summary>                
        ManualParser.Token PeekNextToken();
    }
    
    //-----------------------------------------------------------------------------
    // Interface for parsers
    //-----------------------------------------------------------------------------
    
    /// <summary>
    /// The parser is responsible for providing the compiler with an AST to compile.    
    /// </summary>
    /// <remarks>
    /// The parser has no specific knowledge of the filesystem or locating source files.
    /// The primary implemenation of the IParser interface is <see cref="ManualParser.Parser"/>
    /// which uses an <see cref="Blue.Public.ILexer"/> to parse a token stream into an AST. 
    /// However, not all parser implementations would require a lexer. For example,
    /// a parser could be implemented to read in from XML.
    /// </remarks>
    public interface IParser
    {        
        /// <summary>
        /// Parse a single source file and return a namespace.
        /// </summary>
        /// <remarks>
        /// The driver must package all NamespaceDecl nodes into a single <see cref="AST.ProgramDecl"/>
        /// which can then be resolved (using <see cref="Blue.Public.ISemanticChecker"/>) and
        /// codegened (using <see cref="Blue.Public.ICodeGenDriver"/>)
        /// </remarks>
        AST.NamespaceDecl ParseSourceFile();
    }
#endregion

#region Symbol Resolution
    //-----------------------------------------------------------------------------
    // Interace used by driver to kick off Symbol Engine
    //-----------------------------------------------------------------------------
    
    /// <summary>
    /// This is the interface the Driver uses to kick off semantic checks.
    /// </summary>
    public interface ISemanticChecker
    {   
        // Traverse the AST and construct the symbol table
        // Return true if successful, else false
        bool DoCheck(
            AST.ProgramDecl p,
            ICLRtypeProvider provider,
            Assembly [] refs
            );
        
    
        // Dump the current symbol table through the XML writer    
        void DumpSymbolTable(XmlWriter o);    
    }
#endregion    
    
#region IProvider - link between Codegen & Symbolresolution    
    //-----------------------------------------------------------------------------
    // Interface implemented by somebody to provide CLR types
    // This interface is used by Symbol Resolution to put symbols in a form 
    // that CodeGen understands.
    //-----------------------------------------------------------------------------
    
    /// <summary>
    /// The ICLRTypeProvider interface provides metadata objects to the symbol engine.
    /// </summary>
    /// <remarks>
    /// The ICLRTypeProvider interface is obtained from the emitter (in <see cref="Blue.Public.ICodeGenDriver"/>)
    /// and used by the <see cref="Blue.Public.ISemanticChecker"/>
    /// </remarks>
    public interface ICLRtypeProvider
    {
        System.Type                     CreateCLRClass(SymbolEngine.TypeEntry symBlueType);
    
        System.Type                     CreateCLRArrayType(SymbolEngine.ArrayTypeEntry symBlueType);

        System.Type                     CreateCLREnumType(SymbolEngine.EnumTypeEntry symEnum);
    
        System.Reflection.MethodBase    CreateCLRMethod(SymbolEngine.MethodExpEntry symBlueMethod);
    
        System.Reflection.FieldInfo     CreateCLRField(SymbolEngine.FieldExpEntry symBlueField);

        System.Reflection.FieldInfo     CreateCLRLiteralField(SymbolEngine.LiteralFieldExpEntry sym);
    
        System.Reflection.PropertyInfo  CreateCLRProperty(SymbolEngine.PropertyExpEntry symProperty);
        
        System.Reflection.EventInfo     CreateCLREvent(SymbolEngine.EventExpEntry symEvent);
    
        System.Type                     CreateCLRReferenceType(System.Type t);
    }
#endregion 

#region CodeGen
    //-----------------------------------------------------------------------------
    // Interface to expose CodeGen to the Main driver
    //-----------------------------------------------------------------------------
    
    /// <summary>
    /// This is the interface the driver uses to control CodeGeneration
    /// </summary>
    interface ICodeGenDriver
    {
        /// <summary>
        /// Begin codegen. Creates the assembly. 
        /// </summary>        
        /// <param name="stFilenames">All files that are being compiled. This is used to determine
        /// a default name as well as associate files when building the .pdb. </param>        
        void BeginOutput(string [] stFilenames);
    
        /// <summary>
        /// Given an array of the assemblies that we reference (not including mscorlib)
        /// return a CLRTypeProvider for symbol resolution to use.
        /// </summary>        
        ICLRtypeProvider GetProvider(Assembly [] assemblyRefs);
    
        /// <summary>
        /// Given the root of the program, walk the AST and emit the IL. 
        /// </summary>
        /// <param name="root">The root of an AST for the entire program</param>        
        void DoCodeGen(AST.ProgramDecl root);
    
        /// <summary>
        /// The driver calls this when it is done with Codegen. 
        /// Save the assembly
        /// </summary>
        void EndOutput();
    }
#endregion

#region Options
    
//-----------------------------------------------------------------------------    
// Other subsystems can add a delegate to get invoked to handle options. 
//-----------------------------------------------------------------------------

/// <summary>
/// An OptionHandler delegate gets invoked by the option subsystem when it
/// parses the command line parameters.
/// </summary>
/// <remarks>
/// Handlers are registered by calling <see cref="Blue.Public.IOptions.AddHandler"/>.
/// The <paramref name="stOptionParams"/> is the parameter from the command line.
/// </remarks>
public delegate void OptionHandler(string stOptionParams);
    
//-----------------------------------------------------------------------------
// Expose option subsystem to others
//-----------------------------------------------------------------------------

/// <summary>
/// Subsystems use the IOption interface to add delegates to handle the 
/// different command line switches.
/// </summary>
public interface IOptions
{    
    /// <summary>
    /// Add a handler for a command line option.
    /// </summary>
    /// <param name="stOption">The long name of the option.</param>
    /// <param name="stShortcut">A shortcut name for the option.</param>
    /// <param name="ha">A <see cref="OptionHandler"/> delegate to be invoked to handle this option</param>
    /// <param name="stDescription">A short 1-line, description of this option.</param>
    /// <param name="stFullHelp">A full description of the option including examples and details.</param>
    void AddHandler(
        string stOption,                    // Unique flag for Option
        string stShortcut,                  // Optional shortcut (can be null);
        OptionHandler ha,                   // Option Handler
        string stDescription,               // short 1-line description
        string stFullHelp                   // long description
    );
}
#endregion

#region Error Logging

//-----------------------------------------------------------------------------
// Public interface to support error logging for the subsystems.
//-----------------------------------------------------------------------------

/// <summary>
/// The IError interface provides all subsytems with a unified ability to
/// report errors resulting from bad user-input (not internal compiler bugs).
/// </summary>
/// <remarks>
/// The IError interface is exposed to all components because anyone can
/// produce an error. 
/// <para> IError is built around the <see cref="ErrorException"/>
/// The rationale is that it is an implementation detail whether an error should
/// be thrown or just printed. By making everything an exception, clients of IError
/// can easily switch between Print and Throw as their implementaiton changes. </para>
/// </remarks>
public interface IError
{
    /// <summary>
    /// Print an error.
    /// </summary>
    /// <remarks>
    /// *Every* single user-error comes through here.
    /// This does not include internal errors and exceptions (such as File-not-found).
    /// However, if those exceptions impact the user, then they are converted to a
    /// ErrorException and then they come through here.
    /// </remarks>
    /// <param name="e">The error.</param>
    void PrintError(ErrorException e);
    
    /// <summary> 
    /// Print a warning.   
    /// </summary>
    /// <remarks>
    /// The cousin to PrintError.
    /// *Every* single user-warning comes through here.        
    /// Note that there is no ThrowWarning because a warning, by nature,
    /// should only be informative and not require a change in flow-control.
    /// </remarks>
    /// <param name="e">The error.</param>
    void PrintWarning(ErrorException e);
    
    /// <summary>
    /// call <see cref="IError.PrintError"/> and then throw the exception.
    /// </summary>
    /// <remarks>
    /// ThrowError is a convenience function. Many times a user error is an 
    /// exceptional case that requires major control flow. We use exception
    /// handling for that. This lets us avoid having to check for error cases 
    /// all over the place.    
    /// </remarks>    
    /// <param name="e">The error.</param>
    void ThrowError(ErrorException e);
    
    /// <summary>
    /// This can be used by the Driver to change control flow in response to errors.
    /// </summary>
    /// <remarks>
    /// Note that this is a check of errors since startup, not errors since last called.
    /// So once it returns true, it will always return true.
    /// </remarks>
    /// <returns>Return true if we've had any errors since startup, false if we've had no errors.</returns>
    bool HasErrors();    
}
#endregion
}
} // end namespace
