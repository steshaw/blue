/***************************************************************************\
*
* File: Error.cs
*
* Description:
* Error.cs defines the error-interface for exposing compile errors to the
* End-Developer.
*
*
* History:
*  8/14/2001: JStall:       Created
*
* Copyright (C) 2001.  All rights reserved.
*
\***************************************************************************/

using System;
using System.Collections;
using System.Diagnostics;
using System.Text;



//-----------------------------------------------------------------------------
// File position - this is a lot of information to track, but VS can
// actually use it all!
//-----------------------------------------------------------------------------    

/// <summary>
/// A <c>FileRange</c> class is a logically-immutable object to track line number information.
/// It has a filename, a starting column and row, and an ending column and row. It only
/// represents constructs within a single file.
/// </summary>
public class FileRange
{
    // Describe a full filerange
    public FileRange(string stFilename, int rowStart, int colStart, int rowEnd, int colEnd)
    {
        Filename = stFilename;
        RowStart = rowStart;
        RowEnd = rowEnd;
        ColStart = colStart;
        ColEnd = colEnd;
    }
    
    // Use when we only have one character to build the filerange around
    public FileRange(string stFilename, int rowStartEnd, int colStartEnd)
    {
        Filename = stFilename;
        RowStart = rowStartEnd;
        RowEnd = rowStartEnd;
        ColStart = colStartEnd;
        ColEnd = colStartEnd;
    }
    
    // Used when we don't know anything
    public FileRange()
    {    
    }
    
    // Entry representing 
    public static FileRange NoSource = new FileRange();
    
    public int RowStart;
    public int RowEnd;
    public int ColStart;
    public int ColEnd;
    public string Filename;
    
    public override bool Equals(object o)
    {
        FileRange other = o as FileRange;
        if (other == null)
            return false;
        if (other == this)
            return true;
        if ((other.RowStart == this.RowStart) &&
            (other.RowEnd   == this.RowEnd) &&
            (other.ColStart == this.ColStart) &&
            (other.ColEnd   == this.ColEnd) &&
            (other.Filename == this.Filename))
            return true;
            
        return false;                    
    }
    public override int GetHashCode()
    {
        return this.Filename.GetHashCode() + 
            (this.RowStart + this.RowEnd) * 100 +
            (this.ColStart);
    }
    
    public bool IsValid()
    {
    // Verify well ordering
        return ((RowStart < RowEnd) || 
            (RowStart == RowEnd && ColStart < ColEnd));
    }
}

//-----------------------------------------------------------------------------
// Error mechanism in blue.
// All errors occurences are represented by an ErrorException. This forces
// a common bottleneck that all error handling must go through. 
// @todo - would be nice to centralize text.
// Usage:
// PrintError(Resolver.UndefinedSymbol(...));
//-----------------------------------------------------------------------------


/// <summary>
/// <c>ErrorException</c> is the base class for all errors resulting from bad user-input
/// (as opposed to errors resulting from bugs in blue). 
/// </summary>
/// <remarks>
/// Each subsystem derives its own type safe exception with its own set of error codes.
/// Use the <see cref="Blue.Public.IError"/> interface to print the errors.
/// <list type="bullet">
/// <item>
/// <see cref="ManualParser.LexerException"/> - Lexical errors. Usually resulting from 
/// missing End-of-file, unterminated constants, bad preprocessor structure.
/// </item>
/// <item>
/// <see cref="ManualParser.Parser.ParserErrorException"/> - Syntax errors. 
/// </item>
/// <item>
/// <see cref="SymbolError.SymbolErrorException"/> - Symantic errors. These are related to syntax
/// errors in, but in general, any error that can reasonably be determined by the parser is
/// a syntax error. 
/// </item>
/// </list>
/// </remarks>
public abstract class ErrorException : System.Exception
{
#region Public Constructors
    // Have typesafe ctors for each error-subsystem
    internal ErrorException(Blue.Driver.ErrorCodes e, FileRange location, string stMessage) 
        : this(ERGeneral(e), location, stMessage) { }
    
    internal ErrorException(ManualParser.Lexer.ErrorCode e, FileRange location, string stMessage) 
        : this(ERLexer(e), location, stMessage) { }
    
    internal ErrorException(ManualParser.Parser.Code e, FileRange location, string stMessage) 
        : this(ERParser(e), location, stMessage) { }
        
    internal ErrorException(SymbolError.Code e, FileRange location, string stMessage) 
        : this(ERResolve(e), location, stMessage) { }
        
    internal ErrorException(Blue.CodeGen.EmitCodeGen.ErrorCodes e, FileRange location, string stMessage) 
        : this(ERCodeGen(e), location, stMessage) { }
#endregion

#region Error Ranges    
    // Error Range
    // Convert the different error sub-system codes to a single integer range    
    internal static int ERGeneral(Blue.Driver.ErrorCodes e)
    {
        return ((int) e) + 1000;
    }
    
    internal static int ERLexer(ManualParser.Lexer.ErrorCode e)
    {
        return ((int) e) + 2000;
    }
    
    internal static int ERParser(ManualParser.Parser.Code e)
    {
        return ((int) e) + 2100;
    }
           
    internal static int ERResolve(SymbolError.Code e)
    {
        return ((int) e) + 3000;
    }
    
    internal static int ERCodeGen(Blue.CodeGen.EmitCodeGen.ErrorCodes e)
    {
        return ((int) e) + 5000;
    }
#endregion

#region Private Constructors
    // Constructor used once we've translated an error-enum into
    // a raw integer
    private ErrorException(
        int iCode,
        FileRange location,
        string stMessage)
    {
        m_iCode     = iCode;
        m_location  = location;
        m_stMessage = stMessage;
        
        // Debugging facility
        #if false
        int iBreakOnErrorCode = 0;
        if (iCode == iBreakOnErrorCode)
        {
            Console.WriteLine("UserBreakpoint on error code {0}", iCode);
            System.Diagnostics.Debugger.Break();        
        }        
        #endif
    }
#endregion

#region Properties & Data    
    int         m_iCode;
    FileRange   m_location;
    string      m_stMessage;
    
    public int Code
    {
        get { return m_iCode; }
    }
    
    public FileRange Location
    {
        get { return m_location; }
    }
    
    public override string Message
    {
        get { return m_stMessage; }
    }
        
#endregion    
    
}

namespace Blue.Utilities
{

/***************************************************************************\
*****************************************************************************
*
* class ErrorLog
*
* ErrorLog provides a consistent interface for exposing compile errors and 
* warnings to the End-Developer.
*
*****************************************************************************
\***************************************************************************/

public class ErrorLog : Blue.Public.IError
{
#region Enums
    public enum Severity
    {
        Error       = 0,
        Warning     = 1,
        Count       = 2
    }

#endregion Enums


#region Construction

    static ErrorLog()
    {
        s_arstSeverity = new string[] {
            "Error",
            "Warning"
        };

        s_arstSeveritySingular = new string[] {
            "error",
            "warning"
        };

        s_arstSeverityPlural = new string[] {
            "errors",
            "warnings"
        };
              
    }


    /***************************************************************************\
    *
    * ErrorLog.ErrorLog
    *
    * ErrorLog() initializes standard information.
    *
    \***************************************************************************/

    public ErrorLog()
    {
        m_arcOccurences = new int[(int) Severity.Count];
    }

#endregion Construction


#region Public Methods
//-----------------------------------------------------------------------------
// Blue has a single unified error subsystem, with error codes & checking
// partitioned across the different sub-systems.
//
// All users errors (ie, resulting from incorrect source file) must create
// a ErrorException object and send it through PrintError.
// This ensures standard error reporting & tracking.
//
// Note that this does mean that we have to create an exception object for
// every single error. This is ok because errors are rare. It is more 
// important to have strong (robust & flexible) error handling then fast
// handling. 
//-----------------------------------------------------------------------------
    
    public void PrintError(ErrorException e)
    {
        Debug.Assert(e != null);
        
        // *Every* single user-error comes through here.
        // This does not include internal errors & exceptions (such as File-not-found).
        // However, if those exceptions impact the user, then they are converted to a
        // ErrorException and then they come through here.
        PrettyPrintError(e.Code, e.Location, e.Message);
    }
    
    public void PrintWarning(ErrorException e)
    {
        Debug.Assert(e != null);
        
        // The cousin to PrintError.
        // *Every* single user-warning comes through here.        
        // Note that there is no ThrowWarning because a warning, by nature,
        // should only be informative and not require a change in flow-control.
        PrettyPrintError(e.Code, e.Location, e.Message);
    }
    
    // ThrowError is a convenience function. Many times a user error is an 
    // exceptional case that requires major control flow. We use exception
    // handling for that. This lets us aboid having to check for error cases 
    // all over the place.    
    //
    // If an error is more of a casual observation that doesn't really get in our 
    // way but will ultimately fail CodeGen, then use PrintError instead of ThrowError.
    public void ThrowError(ErrorException e)
    {
        PrintError(e);
        throw e;
    }
        
    // Return true if we've had any errors, false if we've had no errors.
    // This can be used by the Driver to change control flow in response to errors.    
    public bool HasErrors()
    {
        return m_arcOccurences[(int) Severity.Error] != 0;
    }
#endregion

#region Methods
    
    /***************************************************************************\
    *
    * ErrorLog.PrintFinal
    *
    * PrintFinal() is used at the end of compilation to display the accumulated
    * build results.
    *
    \***************************************************************************/

    public void PrintFinal()
    {
        // @dogfood - allow 'const'
        /*const*/ int idxError      = (int) Severity.Error;
        /*const*/ int idxWarning    = (int) Severity.Warning;

        int cErrors             = m_arcOccurences[idxError];
        int cWarnings           = m_arcOccurences[idxWarning];

        string stError          = (cErrors == 1 ? s_arstSeveritySingular[idxError] : s_arstSeverityPlural[idxError]);
        string stWarning        = (cWarnings == 1 ? s_arstSeveritySingular[idxWarning] : s_arstSeverityPlural[idxWarning]);

        Console.WriteLine(string.Format("Build complete -- {0} {1}, {2} {3}", 
                cErrors, stError, cWarnings, stWarning));
    }


    /***************************************************************************\
    *
    * ErrorLog.Print
    *
    * Print() displays the given error or warning.
    *
    \***************************************************************************/

       
    private void PrettyPrintError(int iErrorNum, FileRange location, string stText)
    {
        PrettyPrint(iErrorNum, location, Severity.Error, stText);
    }
    
    private void PrettyPrintWarning(int iErrorNum, FileRange location, string stText)
    {
        PrettyPrint(iErrorNum, location, Severity.Warning, stText);
    }
    
    private void PrettyPrint(int iErrorNum, FileRange location, Severity idxSeverity, string stText)
    {
        StringBuilder sb = new StringBuilder();
        if (location != null)
        {
            sb.AppendFormat("{0}({1},{2}): {5} B{3}:{4}", 
                location.Filename, location.RowStart, location.ColStart,
                iErrorNum, stText,
                s_arstSeverity[(int) idxSeverity]);
        } else {
            sb.AppendFormat("{0} B{1}:{2}",                
                s_arstSeverity[(int) idxSeverity],
                iErrorNum, stText);        
        }
        
        Console.WriteLine(sb.ToString());
        
        m_arcOccurences[(int) idxSeverity]++;
    }

#endregion Methods


#region Data
    private static string[]     s_arstSeverity;
    private static string[]     s_arstSeveritySingular;
    private static string[]     s_arstSeverityPlural;

    private int[]               m_arcOccurences;

#endregion Data

} // class ErrorLog

} // namespace Blue.Utilities
