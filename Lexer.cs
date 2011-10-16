//-----------------------------------------------------------------------------
// Hand coded lexer
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Diagnostics;
using System.Collections;

//using ErrorLog = Blue.Utilities.ErrorLog;
using ILexer = Blue.Public.ILexer;

namespace ManualParser
{
//-----------------------------------------------------------------------------
// Represent a position in the file
//-----------------------------------------------------------------------------
public struct CursorPos
{
    public int row;
    public int col;
}

//-----------------------------------------------------------------------------
// Token information
// Single base class that operates as a variant.
// @todo - this could probably be more efficient.
//-----------------------------------------------------------------------------

/// <summary>
/// A single lexeme from a source file. These are produced by a <see cref="Blue.Public.ILexer"/>
/// and consumed by a parser.
/// </summary>
public class Token
{
#region Enum of all the possible Token types
    public enum Type
    {
        cId,
    // Literals        
        cInt,
        cChar,
        cString,
        cBool,
        
        cClass,
        cInterface,
        cStruct,
        cEnum,
        cDelegate,
        cEvent,
        cOperator,

        cReturn,
        cNew,
        cIf,
        cSwitch,
        cElse,        
        cDo,
        cWhile,
        cFor,
        cForEach,
        cIn,
        cGoto,
        cBreak,
        cContinue,
        cDefault,
        cCase,
        
        cUsing,
        cNamespace,
        
        cNull,

        cTry,
        cCatch,
        cFinally,
        cThrow,

        cRef,
        cOut,
        cParams,
                
        cGet,
        cSet,
        //cValue,
        
        cAttrPublic,
        cAttrPrivate,
        cAttrProtected,
        cAttrStatic,
        cAttrVirtual,
        cAttrAbstract,
        cAttrOverride,
        cAttrInternal,
        cAttrSealed,
        cAttrReadOnly,
        cAttrConst,
        
        cLParen,
        cRParen,
        cLCurly,
        cRCurly,
        cLSquare,
        cRSquare,
        cLRSquare,  // includes int:Dimension
        cSemi,
        cComma,
        cDot,
        cAssign,
        cColon,
        cQuestion,
        
        // Boolean
        cAnd,
        cOr,
        cNot,
        
        // Bitwise
        cBitwiseAnd,
        cBitwiseAndEqual,
        cBitwiseOr,
        cBitwiseOrEqual,
        cBitwiseXor,
        cBitwiseXorEqual,
        
        cBitwiseNot,
        
        cShiftLeft,
        cShiftLeftEqual,
        cShiftRight,
        cShiftRightEqual,
        
        cPlus,
        cPlusPlus,
        cPlusEqual,
        cMinus,
        cMinusMinus,
        cMinusEqual,
        cMul,
        cMulEqual,
        cDiv,
        cDivEqual,
        cMod,
        cModEqual,
        
        cTypeOf,
        
        cEqu,
        cNeq,
        cGTE,
        cLTE,
        cGT,
        cLT,

        cIs,
        cAs,
        
        cError,
        cEOF,

    // The order here is important.
        MARKER_LastNormalToken,
        
        MARKER_FirstPreProcToken,
        
    // Preprocessor tokens.
        cPP_EOL,
        cPP_If,
        cPP_ElseIf,
        cPP_Else,
        cPP_Endif,
        cPP_Define,
        cPP_Undef,
        cPP_Region,
        cPP_EndRegion,
    }
#endregion

#region Checks            
    // Pretty print this token's type & content
    public override string ToString()
    {
        string stValue = null;
        
        switch(m_type)
        {
        case Type.cId:
            stValue = Text; break;
            
        case Type.cString:
            stValue = Text; break;
            
        case Type.cInt:
            stValue = m_nValue.ToString(); break;        
        
        case Type.cChar:
            stValue = "\'"  + (char) m_nValue + "\'"; break; // @todo - for escapes
            
        case Type.cBool:
            stValue = m_fValue.ToString(); break;            
            
        case Type.cError:
            stValue = "starting with:"+m_text; break;
        }
        
        if (stValue == null) {
            return m_type.ToString();
        } else {
            return m_type.ToString() + ":" + stValue;
        }
    }            
#endregion    
    
#region Constructors    
    // String data (for string literals & identifiers)
    public Token(Type type, bool fValue, FileRange pos)
    {   
        Debug.Assert(type == Type.cBool);
        m_filepos = pos;
        
        m_type = type;
        m_fValue = fValue;
        
    }
    
    // String data (for string literals & identifiers)
    public Token(Type type, string stText, FileRange pos)
    {   
        Debug.Assert(type == Type.cId || type == Type.cString);
        m_filepos = pos;
        
        m_type = type;
        m_text = stText;
    }
    
    // Int data (for integers)
    public Token(Type type, int nValue, FileRange pos)
    {   
        Debug.Assert(type == Type.cInt || type == Type.cLRSquare);
        m_filepos = pos;        
        
        m_type = type;
        m_nValue = nValue;
    }
    
    // For chars
    public Token(Type type, char chValue, FileRange pos)
    {
        Debug.Assert(type == Type.cChar);
        m_filepos = pos;
        
        m_type = type;
        m_nValue = (int) chValue;
    }
    
    // For errors
    public Token(Type type, char ch1, int chPeek, FileRange pos)
    {   
        Debug.Assert(type == Type.cError);
        m_filepos = pos;
        
        m_type = type;
        if (chPeek == -1 || Lexer.IsWhitespace(chPeek))
        {
            m_text = "" + ch1;
        } else {
            m_text = "" + ch1 + (char) chPeek;
        }
        
        
    }
    
    // No data (for practically everything else)
    public Token(Type type, FileRange pos)
    {   
        m_filepos = pos;
        
        m_type = type;
        m_text = "";
    }
#endregion

#region Helpers
    public bool IsPreprocToken
    {
        get {
            return (this.m_type >= Token.Type.MARKER_FirstPreProcToken);
        }
    }
    

#endregion
    
#region Properties & Data        
    // Note where this lexeme exists (to propogate to AST for error info)
    protected FileRange m_filepos;
    public FileRange Location
    {
        get { return m_filepos; }
    }
    
    // What type of token are we?
    internal Type m_type;            
    public Type TokenType
    {
        get { return m_type; }
    }
        
    // Raw text of the token, 
    internal string m_text;
    public string Text
    {
        get { Debug.Assert(m_type == Type.cId || m_type == Type.cString); return m_text; }
    }
    
    // Get the identifier (text & location)
    public Identifier Id
    {
        get { 
            Debug.Assert(m_type == Type.cId);
            return new Identifier(m_text, m_filepos); 
        }
    }
    
    // Integer   
    internal int m_nValue;
    public int IntValue
    {
        get { Debug.Assert(m_type == Type.cInt); return m_nValue; }
    }
    
    // Chars
    public char CharValue
    {
        get { Debug.Assert(m_type == Type.cChar); return (char) m_nValue; }    
    }
    
    // Rank Specifier
    public int Dimension
    {
        get { Debug.Assert(m_type == Type.cLRSquare); return m_nValue; }        
    }
    
    // Boolean
    internal bool m_fValue;
    public bool BoolValue
    {
        get { Debug.Assert(m_type == Type.cBool); return m_fValue; }
    }
#endregion    
    
}

//-----------------------------------------------------------------------------
// Exceptions to help route errors.
// Used internally to manage control flow when we hit an error. 
// The lexer handles errors by passing a special error token to the outside
// world
//-----------------------------------------------------------------------------
internal class LexerException : ErrorException
{
    internal LexerException(Lexer.ErrorCode c, FileRange location, string stMessage) : 
        base(c, location, stMessage)
    {
    }
}

//-----------------------------------------------------------------------------
// Scanner to produce tokens
//-----------------------------------------------------------------------------

/// <summary>
/// A lexer to convert a <see cref="TextReader"/> into a stream of <see cref="Token"/> objects.
/// </summary>
/// <remarks>
/// <see cref="ILexer"/> implementation used by the <see cref="ManualParser.Parser"/> to parse a source file.
/// Also handles the preprocessor.
/// </remarks>
public class Lexer : ILexer
{   
#region Construction
    // Create a lexer on the given reader
    // The filename is only used to fill out the FileRange structures. (it
    // can be any string)
    public Lexer(string stFilenameHint, TextReader reader)
        : this(stFilenameHint, reader, null)
    {
    
    }
    
    public Lexer(string stFilenameHint, TextReader reader, string[] stDefines)
    {
        m_reader = reader;
        
        // Set line number info
        m_stFilenameHint = stFilenameHint;
        m_row = 1;
        m_col = 1;
        m_fStartOfLine = true;
        
        m_fIsErrorMode = false;        
        
        InitPreprocessor(stDefines);
        
        
    }
#endregion

#region Static Construction
    // Fill out keyword hash. We only need one copy for all the lexers
    static Lexer()
    {   
        m_keywords["return"] = Token.Type.cReturn;
        m_keywords["class"] = Token.Type.cClass;
        m_keywords["interface"] = Token.Type.cInterface;
        m_keywords["struct"] = Token.Type.cStruct;
        m_keywords["enum"] = Token.Type.cEnum;
        m_keywords["delegate"] = Token.Type.cDelegate;
        m_keywords["event"] = Token.Type.cEvent;
        m_keywords["operator"] = Token.Type.cOperator;
        
        m_keywords["new"] = Token.Type.cNew;
        m_keywords["if"] = Token.Type.cIf;
        m_keywords["switch"] = Token.Type.cSwitch;
        m_keywords["else"] = Token.Type.cElse;
        
        m_keywords["using"] = Token.Type.cUsing;
        m_keywords["namespace"] = Token.Type.cNamespace;
        
        m_keywords["out"] = Token.Type.cOut;        
        m_keywords["ref"] = Token.Type.cRef;
        m_keywords["params"] = Token.Type.cParams;
        
        m_keywords["get"] = Token.Type.cGet;        
        m_keywords["set"] = Token.Type.cSet;
        //m_keywords["value"] = Token.Type.cValue; 
        
        m_keywords["do"] = Token.Type.cDo;
        m_keywords["while"] = Token.Type.cWhile;
        m_keywords["for"] = Token.Type.cFor;
        m_keywords["foreach"] = Token.Type.cForEach;
        m_keywords["in"] = Token.Type.cIn;
        
        m_keywords["goto"] = Token.Type.cGoto;
        m_keywords["break"] = Token.Type.cBreak;
        m_keywords["continue"] = Token.Type.cContinue;
        m_keywords["default"] = Token.Type.cDefault;
        m_keywords["case"] = Token.Type.cCase;

        m_keywords["is"] = Token.Type.cIs;
        m_keywords["as"] = Token.Type.cAs;


        m_keywords["try"] = Token.Type.cTry;
        m_keywords["catch"] = Token.Type.cCatch;
        m_keywords["finally"] = Token.Type.cFinally;
        m_keywords["throw"] = Token.Type.cThrow;

        // Literal keywords                
        m_keywords["true"] = Token.Type.cBool;        
        m_keywords["false"] = Token.Type.cBool;        
        m_keywords["null"] = Token.Type.cNull;
        
        // Modifiers
        m_keywords["public"] = Token.Type.cAttrPublic;
        m_keywords["private"] = Token.Type.cAttrPrivate;
        m_keywords["protected"] = Token.Type.cAttrProtected;
        m_keywords["static"] = Token.Type.cAttrStatic;        
        m_keywords["virtual"] = Token.Type.cAttrVirtual;
        m_keywords["abstract"] = Token.Type.cAttrAbstract;
        m_keywords["override"] = Token.Type.cAttrOverride;
        m_keywords["internal"] = Token.Type.cAttrInternal;
        m_keywords["sealed"] = Token.Type.cAttrSealed;
        m_keywords["readonly"] = Token.Type.cAttrReadOnly;
        m_keywords["const"] = Token.Type.cAttrConst;
        
        
        m_keywords["typeof"] = Token.Type.cTypeOf;
    
        // Preprocessor directives        
        m_keywords["#if"] = Token.Type.cPP_If;
        m_keywords["#elif"] = Token.Type.cPP_ElseIf;
        m_keywords["#else"] = Token.Type.cPP_Else;
        m_keywords["#endif"] = Token.Type.cPP_Endif;
        m_keywords["#define"] = Token.Type.cPP_Define;
        m_keywords["#undef"] = Token.Type.cPP_Undef;
        m_keywords["#region"] = Token.Type.cPP_Region;
        m_keywords["#endregion"] = Token.Type.cPP_EndRegion;
            
    }

    // If we find an identifier, we lookup in this table to see
    // if it's actually a keyword. If so, return the keyword (else return the id)     
    protected static Hashtable m_keywords = new Hashtable();
#endregion
    
    // Are we in error mode (in which case we always return EOF)
    bool m_fIsErrorMode;        
    string m_stFilenameHint;


#region Errors
    // Error codes. Mostly from preprocessor / bad EOF
    internal enum ErrorCode
    {
        cUnmatchedEndRegion,            // Missing a #region for this #endregion
        cMissingEndifBeforeEOF,
        cUnterminatedComment,
        cPreProcDirMustBeAtStartOfLine,
        cInvalidPreProcDir,
        cUnterminatedChar,
        cNoNewlineInString,
        cUnexpectedEOF,
        cUnrecognizedEscapeSequence,
        
    }
    
    // Main error hub for lexer
    internal void ThrowError(LexerException e)
    {
        Blue.Driver.StdErrorLog.ThrowError(e);
    }

    // We have a #region, but no matching #endregion    
    LexerException E_MissingEndRegion()
    {
        return new LexerException(ErrorCode.cUnmatchedEndRegion, CalcCurFileRange(), "Missing a #region for this #endregion.");
    }
    
    LexerException E_MissingEndifBeforeEOF()
    {
        return new LexerException(ErrorCode.cMissingEndifBeforeEOF, CalcCurFileRange(),
            "Expected #endif before end-of-file.");
    }
    
    LexerException E_UnterminatedComment()
    {
        return new LexerException(ErrorCode.cUnterminatedComment, CalcCurFileRange(),
            "Must terminate multi-line comment with '*/' before end-of-file.");
    }
        
    LexerException E_PreProcDirMustBeAtStartOfLine()
    {
        return new LexerException(ErrorCode.cPreProcDirMustBeAtStartOfLine, CalcCurFileRange(),
            "Preprocessor directives must be the first non-whitespace token in a line.");
    }
    
    LexerException E_InvalidPreProcDir(string stHint)
    {
        return new LexerException(ErrorCode.cInvalidPreProcDir, CalcCurFileRange(),
            "'" + stHint + "' is not a valid preprocessor directive.");        
    }

    LexerException E_UnterminatedChar()
    {
        return new LexerException(ErrorCode.cUnterminatedChar, CalcCurFileRange(),
            "Unterminated character constant.");
    }
    
    
    LexerException E_NoNewlineInString()
    {
        return new LexerException(ErrorCode.cNoNewlineInString, CalcCurFileRange(),
            "Can not have a newline in a string.");
    }
    
    LexerException E_UnexpectedEOF()
    {
        return new LexerException(ErrorCode.cUnexpectedEOF, CalcCurFileRange(),
            "Unexpected EOF.");
    }
    
    LexerException E_UnrecognizedEscapeSequence(char ch)
    {
        return new LexerException(ErrorCode.cUnrecognizedEscapeSequence, CalcCurFileRange(),
            "Unrecognized escape sequence '\\" + ch + "'.");
    }
    

#endregion
            
#region Data for stream    
    // The lexer is really just a high level wrapper around the TextReader
    protected TextReader m_reader;
    
    // Used to track where in the file we are
    int m_row;
    int m_col;
    
    bool m_fStartOfLine; // are we the first token on a new line?
    
    // Wrappers around the TextReader to track line number info
    int Read()
    {
        int iCh = m_reader.Read();
        m_col++;
        if (iCh == '\n') {
            m_row++;
            m_col = 1;
            m_fStartOfLine = true;
        } 
        return iCh;
    }
    
    int Peek()
    {
        return m_reader.Peek();
    }
    
    string ReadLine()
    {
        // Reading a line will consume a '\n', thus bump us up.
        m_row++;
        m_col = 1;
        m_fStartOfLine = true;
        return m_reader.ReadLine();
    }
    
    // Cache this at the beginning of a lexeme
    protected CursorPos m_StartPos;
    protected FileRange CalcCurFileRange()
    {
        FileRange r = new FileRange();
        r.Filename = this.m_stFilenameHint;
        r.ColStart = m_StartPos.col;
        r.RowStart = m_StartPos.row;
        
        r.ColEnd = m_col;
        r.RowEnd = m_row;
        
        return r;        
    }
#endregion

#region Public Interface Methods
    // Get
    public Token GetNextToken()
    {
        if (m_tknNext != null)
        {
            Token t = m_tknNext;
            m_tknNext = null;
            return t;
        }
        
        return SafeGetNextToken();
    }
    
    // Peek
    public Token PeekNextToken()
    {
        if (m_tknNext == null)
            m_tknNext = SafeGetNextToken();
            
        return m_tknNext;
    }
    
    // For peeking, we remember the next token.
    protected Token m_tknNext = null;
    
    
    // Safe wrapper around GetNextToken
    // Catch exceptions and convert them to Error tokens
    private Token SafeGetNextToken()
    {
        // Once in error mode, we stay in error mode.
        
        Token t = null;        
        
        if (!m_fIsErrorMode)
        {
            try {
                // Do the real work.
                t = GetNextToken_PreprocessorFilter();
            }
            catch(ManualParser.LexerException)
            {
                m_fIsErrorMode = true;
                t = null;
            }
        }
        
        if (t == null)
            return new Token(Token.Type.cEOF, CalcCurFileRange());
        
        return t;
    }
#endregion
   
#region Helper Functions   
    // Helper funcs
    public static bool IsWhitespace(int iCh)
    {
        return iCh == 0x20 || iCh == '\t' || iCh == '\n' || iCh == '\r';
    }
    
    public static bool IsDigit(int iCh)
    {
        return iCh >= '0' && iCh <= '9';    
    }
    
    // Return -1 if not a hex digit, else return 0..15
    public static int AsHexDigit(int iCh)
    {
        if (iCh >= '0' && iCh <= '9')
            return iCh - '0';
        if (iCh >= 'A' && iCh <= 'F')
            return iCh - 'A' + 10;
        if (iCh >= 'a' && iCh <= 'f')
            return iCh - 'a' + 10;            
        return -1;            
    }
    
    public static bool IsFirstIdChar(int iCh)
    {   
        return (iCh == '_') || (iCh >= 'a' && iCh <= 'z') || (iCh >= 'A' && iCh <= 'Z');
    }
    
    public static bool IsIdChar(int iCh)
    {
        return IsFirstIdChar(iCh) || IsDigit(iCh);
    }
#endregion

#region Preprocessor Layer
//-----------------------------------------------------------------------------
// The preprocessor works as a middle layer around GetNextTokenWorker()
// If manages a small symbol table (for #define / #undef) as well 
// as conditionals (#if,#elif,#else,#endif) and strips away #region/#endregion
// Most of the errors that can occur in the lexer are in the preprocessor
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Construction. Supply an optional list of predefined symbols
//-----------------------------------------------------------------------------
    protected void InitPreprocessor(string [] stDefines)
    {
        m_tblPreprocSymbols = new Hashtable();
        
        // Always add this as a predefined symbol
        AddSymbol("__BLUE__");
        
        if (stDefines != null)
        {
            foreach(string s in stDefines)
                AddSymbol(s);
        }
    }   

#region Preprocessor Filter
//-----------------------------------------------------------------------------
// When we're skipping over text (in a false branch of an #if), the text
// doesn't have to lex properly. But we still have to recognize nested #if, 
// and the closing #endif, as well as an EOF. 
// So we have a modified lexer to lexer stuff in dead code.
// Note that this lexer must preserve the expression after an #elsif
// This lexer is also #if..#endif nest aware
//-----------------------------------------------------------------------------
    protected Token.Type GetNextDeadToken()
    {
        int iRowBefore = m_row;
        
        int cIfDepth = 0;
        string st;
        do
        {
        // Does this line start with a preprocessor directive?
        // If so, handle it delicately so that we can read the expression afterwards
            SkipWhiteSpace();
#if true
            int iCh;
            do {
                iCh = Read();
            } while (iCh == '\n');
            
            // Skip past opening whitespace
            while(iCh == ' ' || iCh == '\t')
                iCh = Read();
                
            
            if (iCh == '#')
            {                
                // Note that we don't want to call GetNextTokenWorker() because
                // it may throw an exception, and we just want to check for
                // preproc directives. So do it manually.
                if (Lexer.IsFirstIdChar(Peek()))
                {                    
                    st = ReadPreProcSymbol();
                    
                    switch (st)
                    {
                        // Handle nested #if...#endif
                        case "if":
                            cIfDepth++;
                            break;
                        
                        case "endif":
                            if (cIfDepth == 0)
                                return Token.Type.cPP_Endif;
                            cIfDepth--;
                                                   
                            break;                        
                            
                        case "elif": return Token.Type.cPP_ElseIf;
                        case "else": return Token.Type.cPP_Else;
                            
                    }
                }
                // Ignore it
#endif                            
            }
            
            // Discard the rest of the line and try again    
            st = ReadLine(); // null on EOF
        } while(st != null);
        
        return Token.Type.cEOF;
        
    }
    

//-----------------------------------------------------------------------------
// Skip to the next #endif, being nest aware (ie, skip over nested #if..#endif)
//-----------------------------------------------------------------------------
    protected void SkipToEndif()
    {    
        int iRowBefore = m_row;
        Token.Type tt;
        do
        {               
            // Just eat everything in our path...
            tt = GetNextDeadToken();
            if (tt == Token.Type.cEOF)                 
                ThrowError(E_MissingEndifBeforeEOF());
                
        } while (tt != Token.Type.cPP_Endif);
    }
    
//-----------------------------------------------------------------------------
// Skip for the #if on false. We skip to: #elif, #else, #endif
// and then act accordingly. Return which token we land on.
// Must be nest aware.
//-----------------------------------------------------------------------------    
    protected Token.Type SkipWhenIfIsFalse()    
    {   
        int iRowBefore = m_row;  
                
        do
        {
            Token.Type tt = GetNextDeadToken();
            if (tt == Token.Type.cEOF)                 
                ThrowError(E_MissingEndifBeforeEOF());
            
            // If we hit a #elif/#else/#endif on the top level, return it
            
            if ((tt == Token.Type.cPP_Else) || 
                (tt == Token.Type.cPP_ElseIf) ||
                (tt == Token.Type.cPP_Endif))
                    return tt;                  
        } while(true);
    }
    
//-----------------------------------------------------------------------------
// Preprocessor filter. Intercepts the token stream to do pre-processing.
//-----------------------------------------------------------------------------    
    int m_cRegionDepth = 0;
    
    protected Token GetNextToken_PreprocessorFilter()
    {
        do
        {
            Token t = GetNextTokenWorker();
            
            // If this isn't a preprocesor token, we can just pass it straight through
            if (!t.IsPreprocToken)
                return t;
                
            switch(t.TokenType)
            {
            // Control flow
            // '#if' exp
            case Token.Type.cPP_If:
            {         
// @dogfood - need to implement a 'goto case X', and then remove this label.
PREPROC_IF:            
                bool f = this.ReadBooleanExp();
                EnsureAtEOL();
                if (f)
                {
                    // If the (exp) is true, then we just ignore the #if, and
                    // continue returning tokens
                    break;
                }
                else 
                {                    
                    // If the (exp) is false, then we have to stategically skip.
                    Token.Type tt = SkipWhenIfIsFalse();
                    // For #else, #endif, resume normally
                    
                    // but for #elif we have to evaluate the expression.
                    // This means our lexer had better have left us off before the start of the expression
                    // So at this point, #elsif behaves just like #if, so we go back to the start
                    // of the case.
                    if (tt == Token.Type.cPP_ElseIf)
                    {
                        //goto case Token.Type.cPP_If;
                        goto PREPROC_IF;
                    }
                }                    
            }
                break;
                
            // If we're hitting a #else/#elif live, then we must have just executed
            // an #if, so we can safely skip to the #endif
            // '#elif' exp
            // '#else'            
            case Token.Type.cPP_ElseIf:
            case Token.Type.cPP_Else:
                EnsureAtEOL();
                
                SkipToEndif();
                break;

            // If we hit the #endif live, we can ignore it. (We just stay live).
            // #endif is really only useful to terminate skipping.
            // '#endif'            
            case Token.Type.cPP_Endif:
                EnsureAtEOL();
                break;
                
            // Modify the lexer's set of defined symbols            
            // '#define' symbol
            case Token.Type.cPP_Define:
            {
                string st = this.ReadPreProcSymbol();
                AddSymbol(st);
                EnsureAtEOL();
            }
                break;
                
            // '#undef' symbol
            case Token.Type.cPP_Undef:
            {
                string st = this.ReadPreProcSymbol();
                EnsureAtEOL();
                RemoveSymbol(st);            
            }
                break;
            
            // Regions - really just cosmetic
            // '#region' implicit comment to eol
            case Token.Type.cPP_Region:
                ConsumeRestOfLine();
                m_cRegionDepth++;
                break;
                
            // '#endregion' implicit comment to eol            
            case Token.Type.cPP_EndRegion:
                ConsumeRestOfLine();
                m_cRegionDepth--;
                if (m_cRegionDepth < 0)
                {                    
                    ThrowError(E_MissingEndRegion());
                }
                    
                break;            
            
            }
            
        } while(true);
            
    }
#endregion

#region Preprocessor Symbols
//-----------------------------------------------------------------------------
// Manage Symbols for preprocessor
//-----------------------------------------------------------------------------    
    protected Hashtable m_tblPreprocSymbols;
    void AddSymbol(string st)
    {
        m_tblPreprocSymbols.Add(st, null); // don't care what the value is.
    }
    
    void RemoveSymbol(string st)
    {
        m_tblPreprocSymbols.Remove(st);
    }
    
    bool IsSymbolDefined(string st)
    {
        return m_tblPreprocSymbols.ContainsKey(st);
    }
#endregion    
    
#region Preprocessor Parsing helpers    
//-----------------------------------------------------------------------------
// The preprocessor needs a super-mini parser in it.
//-----------------------------------------------------------------------------    
    
    // Skip past whitespace on this line (not including newling
    void SkipWhiteSpace()
    {
        int iCh = Peek();
        while (IsWhitespace(iCh) && (iCh != '\n'))
        {
            Read();
            iCh = Peek();
        }
    }
    
    // For #if, #elif
    bool ReadBooleanExp()
    {
        string st = ReadPreProcSymbol();
        if (st == "true")
            return true;
        if (st == "false")  
            return false;
        return IsSymbolDefined(st);
    }
    
    // Read an expected preproc symbol, return as a string.
    string ReadPreProcSymbol()
    {
        SkipWhiteSpace();
        
        int iCh = Peek();
        Debug.Assert(IsFirstIdChar(iCh));
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append((char) iCh);
        Read();
                
        iCh = Peek();
        while(IsIdChar(iCh))
        {            
            sb.Append((char) iCh);
            Read(); // consume                
            iCh = Peek();
        }
        
        return sb.ToString();
    }
    
    // Make sure that we're at the end of the line (and that we don't have any extra tokens
    // Throw exception if not.
    void EnsureAtEOL()
    {
        // @todo - skip comments?
        SkipWhiteSpace();
        Debug.Assert(Peek() == '\n');
    }
    
    // Used when we don't care what's after us.
    void ConsumeRestOfLine()
    {
        ReadLine();
    }
#endregion
    
#endregion    
    
//-----------------------------------------------------------------------------
// Do the real work to get the next token
// Returns preprocessor tokens. 
//-----------------------------------------------------------------------------
    protected Token GetNextTokenWorker()
    {   
        int iCh;
               
        do
        {      
            // Record position of start of the lexeme after we've skipped whitespace
            m_StartPos.col = m_col;
            m_StartPos.row = m_row;
                                  
            iCh = Read();
        
            if (IsWhitespace(iCh))
                continue;
                
#region Comments & Division operators        
            // Check for comments
            if (iCh == '/') 
            {
                // Found eol comment, read until eol                    
                if (Peek() == '/') 
                {                
                    do
                    {
                        iCh = Read();                        
                        if (iCh == -1)
                        {                
                            return new Token(Token.Type.cEOF, CalcCurFileRange());
                        }
                    } while (iCh != '\n');
                    
                    continue;
                }  
                                
                // Multiline comment
                // Goes between /* .... */
                else if (Peek() == '*') {
                    Read(); // consume the '*'
                    
                    do 
                    {
                        iCh = Read();
                        if (iCh == -1)
                        {
                            //Debug.Assert(false, "Multiline comment terminated be EOF");
                            //return new Token(Token.Type.cEOF, CalcCurFileRange());
                            ThrowError(E_UnterminatedComment());
                        }
                    } while ((iCh != '*') || (Peek() != '/'));
                    
                    Read(); // consume the '/'
                    continue;
                }   
                
                else if (Peek() == '=')
                {
                    Read(); // consume '='
                    // DivisionEqual symbol
                    m_fStartOfLine = false;
                    return new Token(Token.Type.cDivEqual, CalcCurFileRange());
                }
                         
                else 
                {
                    // Division symbol
                    m_fStartOfLine = false;
                    return new Token(Token.Type.cDiv, CalcCurFileRange());
                }
            }
#endregion

            // Check EOF
            if (iCh == -1)
            {                
                return new Token(Token.Type.cEOF, CalcCurFileRange());
            }

#region Preprocessor tokens
            if (iCh == '#')
            {
                if (!m_fStartOfLine)
                {
                    // @todo - fix this error
                    //Debug.Assert(false, "Preprocessor directives only allowed at start of new line");                
                    ThrowError(E_PreProcDirMustBeAtStartOfLine());                    
                }
                
                // Read the whole preprocessor directive in
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("#");
                iCh = Peek();
                while(IsIdChar(iCh))
                {
                    Read(); // consume
                    sb.Append((char) iCh);
                    iCh = Peek();
                }
                
                string st = sb.ToString();
                
                object o = m_keywords[st];
                if (o == null)
                {
                    //Debug.Assert(false, "Preproc directive '" + st + "' not valid");
                    ThrowError(E_InvalidPreProcDir(st));
                }
                
                Token.Type e = (Token.Type) o;
                
                // Preprocessor directives have syntax:
                // #if <exp>
                // #elif <exp>
                // #define <symbol>
                // #undef <symbol>
                return new Token(e, CalcCurFileRange());                
            }
#endregion

            // Since we've skipped past all whitespace & comments, after here, we're
            // no longer at the start of the line.
            m_fStartOfLine = false;

                        
#region Identifiers (including 'true' & 'false')         
            // If start of an identifier?
            if (IsFirstIdChar(iCh))
            {
                string stId = "" + (char) iCh;
                
                iCh = Peek();
                while(IsIdChar(iCh))
                {
                    stId +=(char) iCh;
                    Read(); // consume
                    
                    iCh = Peek();
                }
                
                // Lookup for keyword (or bool)
                object o = m_keywords[stId];
                if (o != null)
                {
                    Token.Type e = (Token.Type) o;
                    
                    if (e == Token.Type.cBool) 
                    {                        
                        return new Token(e, (stId == "true"), CalcCurFileRange());
                    } else {
                        return new Token(e, CalcCurFileRange());
                    }
                }
                                                   
                // Return identifier
                return new Token(Token.Type.cId, stId, CalcCurFileRange());
            }
#endregion            
          
#region Characters            
            // Look for characters (inbetween single quotes)
            if (iCh == '\'')
            {
                char val = ReadFormattedChar();
                iCh = Read();
                if (iCh != '\'')
                    //Debug.Assert(false, "Unterminated character constant"); // @todo -legit
                    ThrowError(E_UnterminatedChar());
            
                return new Token(Token.Type.cChar, val, CalcCurFileRange());
            }
#endregion
            
#region String Literals            
            // Look for string literal            
            if (iCh == '\"')
            {
                System.Text.StringBuilder bld = new System.Text.StringBuilder();
                
                while(Peek() != '\"')
                {
                    if (Peek() == '\n')
                    {
                        //Debug.Assert(false, "Can't have newling in string literal"); // @todo- legit
                        ThrowError(E_NoNewlineInString());
                    }
                    
                    iCh = ReadFormattedChar();                    
                    bld.Append((char) iCh);
                }
                this.Read(); // eat closing quote.
            
                string stLiteral = bld.ToString();
                return new Token(Token.Type.cString, stLiteral, CalcCurFileRange());
            }
#endregion

#region Integers            
            // Ints
            // As Hex: 0xAAAAAAAA, 0XAAAAAAAA            
            if (iCh == '0')
            {
                if (Peek() == 'X' || Peek() == 'x')
                {
                    Read(); // consume 'x' or 'X'
                    int val = 0;
                                        
                    int d;
                    while((d = AsHexDigit(Peek())) != -1)
                    {
                        val = (val * 16 + d);
                        Read();
                    }
                    
                    return new Token(Token.Type.cInt, val, CalcCurFileRange());
                }            
            }
            int fSign = 1;
            /*
             * Lexer can't resolve negatives because '-1' could be '-' '1'.
            if (iCh == '-' && IsDigit(Peek()))
            {
                fSign = -1;                
                iCh = Read();
                // fall through to normal int case
            }
            */
            if (IsDigit(iCh))
            {
                int val = iCh - '0';
                 
                iCh = Peek();
                while(IsDigit(iCh))
                {
                    val = (val * 10) + (iCh - '0');
                    Read(); // consume                    
                    iCh = Peek();
                }
            
                return new Token(Token.Type.cInt, val * fSign, CalcCurFileRange());
            }
            Debug.Assert(fSign == 1); // if this isn't 1, then negative case didn't fall through
#endregion            
            
#region Operators             
            Token.Type type = Token.Type.cEOF;
            int i2 = Peek();
            
            switch (iCh)
            {
            case ':':
                type = Token.Type.cColon; break;

            case '?':
                type = Token.Type.cQuestion; break;                

            case '[':
            {
                int cDim = 1; // dimension starts at 1
                while(i2 == ',')
                {
                    cDim++;
                    Read();
                    i2 = Peek();
                }
                if (i2 == ']') {
                    Read();                    
                    return new Token(Token.Type.cLRSquare,cDim, CalcCurFileRange());
                } else {
                    if (cDim != 1)
                        break; // error.
                }
            }
                type = Token.Type.cLSquare; break;
                
            case ']':
                type = Token.Type.cRSquare; break;
                    
            case  '{':
                type = Token.Type.cLCurly; break;
            
            case  '}':
                type = Token.Type.cRCurly; break;
             
            case  '(':
                type = Token.Type.cLParen; break;
             
            case  ')':
                type = Token.Type.cRParen; break;
             
            case  ';':
                type = Token.Type.cSemi; break;
             
            case  ',':
                type = Token.Type.cComma; break;
             
            case  '.':
                type = Token.Type.cDot; break;
             
            case  '=':               
                if (i2 == '=') {
                    Read();
                    type = Token.Type.cEqu;
                } else 
                    type = Token.Type.cAssign;
            
                break;
            
            case '!':
                if (i2 == '=')
                {
                    Read();
                    type = Token.Type.cNeq;
                } else 
                    type = Token.Type.cNot;
                break;
            
            case '<': // <, <=, <<, <<=
                if (i2 == '=')
                {
                    Read();    
                    type = Token.Type.cLTE;
                } else if (i2 == '<')
                {
                    Read();
                    i2 = Peek();
                    if (i2 == '=')
                    {
                        Read();
                        type = Token.Type.cShiftLeftEqual;                         
                    } else
                        type = Token.Type.cShiftLeft;
                } else 
                    type = Token.Type.cLT;
                break;

                    
            case '>': // >, >=, >>, >>=
                if (i2 == '=')
                {
                    Read();    
                    type = Token.Type.cGTE;
                } 
                else if (i2 == '>')
                {
                    Read();
                    i2 = Peek();
                    if (i2 == '=')
                    {
                        Read();
                        type = Token.Type.cShiftRightEqual;                         
                    } 
                    else
                        type = Token.Type.cShiftRight;
                } 
                else 
                    type = Token.Type.cGT;              
                break;    

            case '&': // &, &=, &&
                if (i2 == '&')
                {
                    Read();    
                    type = Token.Type.cAnd;
                } else if (i2 == '=')
                {
                    Read();
                    type = Token.Type.cBitwiseAndEqual;
                } else
                    type = Token.Type.cBitwiseAnd;
                break;  
                
            case '|': // |, |=, ||
                if (i2 == '|')
                {
                    Read();    
                    type = Token.Type.cOr;
                } else if (i2 == '=')
                {
                    Read();
                    type = Token.Type.cBitwiseOrEqual;                
                } else 
                    type = Token.Type.cBitwiseOr;
                break;            
                      
            case '^': // ^, ^=                
                if (i2 == '=')
                {
                    Read();
                    type = Token.Type.cBitwiseXorEqual;                
                } 
                else 
                    type = Token.Type.cBitwiseXor;
                break;                 
            
            case  '+':         
                if (i2 == '+')
                {
                    Read();
                    type = Token.Type.cPlusPlus;
                } else if (i2 == '=')
                {
                      Read();
                      type = Token.Type.cPlusEqual;
                } else 
                    type = Token.Type.cPlus; 

                break;
            
            case  '-':            
                if (i2 == '-')
                {
                    Read();
                    type = Token.Type.cMinusMinus;
                } else if (i2 == '=')
                {
                    Read();
                    type = Token.Type.cMinusEqual;
                } 
                else 
                    type = Token.Type.cMinus; 

                break;
            
            case  '*':
                if (i2 == '=')
                {
                    Read();
                    type = Token.Type.cMulEqual;
                } else
                    type = Token.Type.cMul; 
                break;
                        
            case  '%':
                if (i2 == '=')
                {
                    Read();
                    type = Token.Type.cModEqual;
                } 
                else
                    type = Token.Type.cMod; 
                break;
            
            
            }
            if (type != Token.Type.cEOF)
                return new Token(type, CalcCurFileRange());
#endregion        
               
            // Here's an error
            m_fIsErrorMode = true;
            return new Token(Token.Type.cError, (char) iCh, Peek(), CalcCurFileRange());
        } while(true); // break out of this loop by returning a token
        
        
        
        
    } // End GetNextTokenWorker()


    // Read a character, checking for escapes
    char ReadFormattedChar()
    {               
        int iCh = this.Read();
        if (iCh == -1)
        {
            //Debug.Assert(false, "Unexpected EOF"); // @todo - legit   
            ThrowError(E_UnexpectedEOF()); // in character or string
        }
        
        if (iCh == '\\')
        {
            int iCh2 = Read();
            switch(iCh2)
            {
            case '\'':  iCh = '\''; break;
            case '\"':  iCh = '\"'; break;
            case '\\':  iCh = '\\'; break;
            case '0':   iCh = '\0'; break;
            case 'a':   iCh = '\a'; break;
            case 'b':   iCh = '\b'; break;
            case 'f':   iCh = '\f'; break;
            case 'n':   iCh = '\n'; break;
            case 'r':   iCh = '\r'; break;
            case 't':   iCh = '\t'; break;
            case 'v':   iCh = '\v'; break;
            case -1:
                ThrowError(E_UnexpectedEOF()); break;
            default:                
                ThrowError(E_UnrecognizedEscapeSequence((char) iCh));
                break;                     
            }
        }
        
        return (char) iCh;
    } // end of ReadChar()
    
}



} // end namespace ManualParser