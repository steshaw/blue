//-----------------------------------------------------------------------------
// Manual Recursive Descent parser 
// We can get away with this because the vast majority of 
// Blue's syntax is predictive. We always know what we're looking for.
// The only semi-complicated thing is arithmetic expressions.
//
// Look at the ILexer interface to know what the parser has to deal with here.
// The parser can only get the current token (which consumes that token),
// and peek at the next token (which doesn't consume the token).
//
// Generally, by using ILexer.Peek alot, we can see what's coming in the 
// token stream and know what to expect. Thus we can delegate to the proper
// parse function.
//-----------------------------------------------------------------------------

using System.Diagnostics;
using System.Collections;
using AST;
//using ErrorLog = Blue.Utilities.ErrorLog;
using Log = Blue.Log;
using IParser = Blue.Public.IParser;
using ILexer = Blue.Public.ILexer;

namespace ManualParser
{

#region Helper classes
#if false
//-----------------------------------------------------------------------------
// Explanation of how we do Expression parsing
//-----------------------------------------------------------------------------
Class to help parse a Left-Associative binary expression
E -> E + T | T
But that can't be parsed recursive descent because it's infinite recursion
So we transform it into:
E -> T1 E'
E' -> + T2 E' | {}
The transformation preserves meaning and can be done recursive descent.

Now this still produces a right-recursive tree, we have to create a left recursive tree.
So when we parse E', we pass in T1 from the parent. And then E' forms a BinaryOp
of T1+T2 and passes that into it's E'.
This builds a left-recursive AST. E' then propagates the tree up through return values.
#endif
#endregion

//-----------------------------------------------------------------------------
// Manual Parser
//-----------------------------------------------------------------------------
/// <summary>
/// The primary parser implementation. Convert a token stream for a source file 
/// obtained by an <see cref="Blue.Public.ILexer"/> into a parse tree.
/// Parse errors are handled by throwing <see cref="ManualParser.Parser.ParserErrorException"/>
/// </summary>
public class Parser : IParser
{
#region Construction
//-----------------------------------------------------------------------------
// Initialize with a lexer to provide the token stream
//-----------------------------------------------------------------------------
    public Parser(ILexer lexer)
    {
        m_lexer = lexer;        
    }
#endregion
    
    protected ILexer m_lexer;

#region Errors   
//-----------------------------------------------------------------------------
// When we get a parser error, we want to throw an exception
// This is really just a way to handle control flow within the parser. This
// exception should never get out of the parser.
//-----------------------------------------------------------------------------

    /// <summary>
    /// ParserErrorException represents parse errors due to bad user-input.
    /// </summary>
    private class ParserErrorException : ErrorException
    {
        internal ParserErrorException(Parser.Code c, FileRange location, string s) : 
            base (c, location, s)
        {
        // All Parser errors will come through this body.
        }
    }       
    
     
//-----------------------------------------------------------------------------
// Error handling
//-----------------------------------------------------------------------------

    /// <summary>
    /// Syntax error codes specific to Parser 
    /// </summary>    
    internal enum Code
    {
        cUnexpectedToken,           // we don't know what we wanted, but we didn't like what we got
        cExpectedDiffToken,         // we know what we wanted and didn't get it
        
       // cIllegalClassMemberDecl,  // We're trying to declare something illegal in our class
        cMissingRetType,            // something bad about the ctor declaration
        cBadLabelDef,               // supposed to define a label (really a misuse of a ':')
        cAccessorAlreadyDefined,    // Property accessor (get | set) already defined
        cMissingAccessor,           // Property/Indexer must have at least 1 accessor

        cNoAbstractCtor,            // Constructors can't be abstract
        cBadCtorChain,              // tried to chain to other than 'this' or 'base'
        cNoChainForStaticCtor,      // static ctors can't be chained,
        cNoBaseChainForStructs,     // Struct ctor decls can't chain to a base
        cNoDefaultCtorForStructs,   // structs can't have a default ctor
                
        cExpectedStatementExp,      // Expected a statement expression
        cBadForInitializer,         // the initializer in the for-loop is bad
        
        cDuplicateModifier,         // Modifiers can't be duplicated
        cIllegalModifiers,          // modifiers on something it shouldn't be on
        
        cNoCtorOnInterface,         // Intefaces can't define constructors
        
        cBadModsOnOps,              // Overloaded operators must be public & static
        cBadParamListOnOps,         // Bad parameter list for an overloaded operator
    }
    
    // Convenience helper so that we don't have to keep qualifying StdErrorLog.
    static private void ThrowError(ParserErrorException e)
    {
        Blue.Driver.StdErrorLog.ThrowError(e);
    }
    
    static private void PrintError(ParserErrorException e)
    {
        Blue.Driver.StdErrorLog.PrintError(e);
    }        
        
    // Perhaps the most general syntax error
    // We don't know what we wanted, but we didn't like what we got
    //protected void ThrowError_UnexpectedToken(Token tokenActual)
    ParserErrorException E_UnexpectedToken(Token tokenActual)
    {
        return new ParserErrorException(        
            Code.cUnexpectedToken, 
            tokenActual.Location,
            "Unexpected token '"+tokenActual.ToString() + "'"
        );    
    }
        
    // We expected a specific type of token and we got something else
    //protected void ThrowError_UnexpectedToken(Token tokenActual, Token.Type typeExpected)
    ParserErrorException E_UnexpectedToken(Token tokenActual, Token.Type typeExpected)
    {
        return new ParserErrorException(        
            Code.cExpectedDiffToken, 
            tokenActual.Location,
            "Expected '" + typeExpected.ToString() + "', but got token '"+tokenActual.ToString() + "'"
        );    
    }
    
    
    // We expected one token out of a possible set, but got something different
    //protected void ThrowError_UnexpectedToken(Token tokenActual, Token.Type [] arTypeExpected)
    ParserErrorException E_UnexpectedToken(Token tokenActual, Token.Type e1, Token.Type e2)
    {
        return E_UnexpectedToken(tokenActual, new Token.Type[] {e1, e2} );
    }
    
    ParserErrorException E_UnexpectedToken(Token tokenActual, Token.Type e1, Token.Type e2, Token.Type e3)
    {
        return E_UnexpectedToken(tokenActual, new Token.Type[] {e1, e2, e3} );
    }
        
    ParserErrorException E_UnexpectedToken(Token tokenActual, Token.Type [] arTypeExpected)
    {
        string stExpected = "";
        foreach(Token.Type t in arTypeExpected)
        {
            stExpected += t.ToString() + " ";            
        }
        
        return new ParserErrorException(        
            Code.cExpectedDiffToken, 
            tokenActual.Location,
            "Expected one of {" + stExpected + "}, but got token '"+tokenActual.ToString() + "'"
        );    
    }
 
    // Bad chain target for a constructor
    //protected void ThrowError_BadCtorChain(Identifier idName)
    ParserErrorException E_BadCtorChain(Identifier idName)
    {
        return new ParserErrorException(
            Code.cBadCtorChain,
            idName.Location,
            "Must specify either 'this' or 'base' in constructor chain, not '" + idName.Text + "'"
        );
    }

    // Static constructors can't be chained.
    ParserErrorException E_NoChainForStaticCtor(FileRange location)
    {
        return new ParserErrorException(
            Code.cNoChainForStaticCtor,
            location,
            "Static constructors can't chain to another constructor"
        );
    }

    // fIsGet - true if a 'get' accessor, false if a 'set accessor
    // idName - name of the culrprit property   
    //protected void ThrowError_AccessorAlreadyDefined(Identifier idName, bool fIsGet)
    ParserErrorException E_AccessorAlreadyDefined(Identifier idName, bool fIsGet)
    {
        return new ParserErrorException(
            Code.cAccessorAlreadyDefined,
            idName.Location,
            "Property '" + idName.Text + "' can't have multiple '" + (fIsGet ? "get" : "set") + "' accessors."
        );            
    }
    
    //protected void ThrowError_MissingAccessor(Identifier idName)
    ParserErrorException E_MissingAccessor(Identifier idName)
    {
        return new ParserErrorException(
            Code.cMissingAccessor,
            idName.Location,
            "Property or Indexer '" + idName.Text + "' must have at least 1 accessor."
        );
    }

    ParserErrorException E_NoAbstractCtor(Identifier idName)
    //protected void ThrowError_NoAbstractCtor(Identifier idName)
    {
        return new ParserErrorException(
            Code.cNoAbstractCtor,
            idName.Location,
            "Constructors can't be abstract and must have a body"
        );
    }
    
    // Check for the corresponding error
    protected void CheckError_UnexpectedToken(Token tokenActual, Token.Type [] arTypeExpected)
    {
        foreach(Token.Type t in arTypeExpected)
        {
            if (tokenActual.TokenType == t)
                return;
        }
        ThrowError(E_UnexpectedToken(tokenActual, arTypeExpected));
        //ThrowError_UnexpectedToken(tokenActual, arTypeExpected);
    }
    
    // Check that eActual only has the modifiers from a certain set (eAllowed),
    // If not, print an error listing all illegal modifiers
    protected void CheckError_LegalModifiers(
        FileRange location, 
        string stTargetHint,  // "class", "method", "property", "interface", etc...
        AST.Modifiers eActual,
        AST.Modifiers eAllowed
    )
    {
        if (eActual != eAllowed)
            ThrowError(
                new ParserErrorException(
                    Code.cIllegalModifiers,
                    location,
                    "Illegal modifiers on a '" + stTargetHint + "'"
                )
            );
    
    /*
        // Check if Actual has any bits that aren't set in Allowed
        AST.Modifiers.EFlags eDiff = eActual.Flags & ~eAllowed.Flags;
        
        // If no difference, then no error
        if (eDiff == Modifiers.EFlags.None)
            return;

        // We do have an error, so print out all flags                               
        AST.Modifiers modDiff = new Modifiers(eDiff);
        
        string st = modDiff.ToString();
        
        ThrowError(
            Code.cIllegalModifiers,
            location,
            "Modifiers '" + st + "' are not allowed on a '" + stTargetHint + "'"
        );
    */        
            
    }
     
    
    // Expected a statement expression
    //protected void ThrowError_ExpectedStatementExp(FileRange location)
    ParserErrorException E_ExpectedStatementExp(FileRange location)
    {   
        return new ParserErrorException(
            Code.cExpectedStatementExp,
            location,
            "Expected a statement expression"
        );
    }
    
    //protected void ThrowError_BadForLoopInit(FileRange location)
    ParserErrorException E_BadForLoopInit(FileRange location)
    {
        return new ParserErrorException(
            Code.cBadForInitializer,
            location,
            "The initializer in a for-loop must be either an expression-statement or a variable declaration");
    }
    
    // The given modifier appears multiple times. Modifiers can only appear once.
    /*
    protected void ThrowError_DuplicateModifier(FileRange location, AST.Modifiers.EFlags eFlag)
    {
        ThrowError(
            Code.cDuplicateModifier,
            location,
            "Duplicate '" + eFlag.ToString() + "' modifier.");
    }
    */
    
    //protected void ThrowError_NoBaseChainForStructs(FileRange location)
    ParserErrorException E_NoBaseChainForStructs(FileRange location)
    {
        return new ParserErrorException(
            Code.cNoBaseChainForStructs,
            location,
            "Constructors in Struct declarations can't chain to the base class");
    
    }
    
    //protected void ThrowError_NoCtorOnInterface(FileRange location)
    ParserErrorException E_NoCtorOnInterface(FileRange location)
    {
        return new ParserErrorException(
            Code.cNoCtorOnInterface,
            location,
            "Interfaces can't define constructors"
        );
    }
    
    //protected void ThrowError_NoDefaultCtorForStructs(FileRange location)
    ParserErrorException E_NoDefaultCtorForStructs(FileRange location)
    {
        return new ParserErrorException(
            Code.cNoDefaultCtorForStructs,
            location,
            "Structs can't have default constructors"
        );
    }
    
    ParserErrorException E_BadModsOnOps(FileRange location)
    {
        return new ParserErrorException(
            Code.cBadModsOnOps,
            location,
            "Overloaded operators must only have 'public' and 'static' modifiers"
        );
    }
    
    ParserErrorException E_BadParamListOnOps(FileRange location, string stHint)
    {
        return new ParserErrorException(
            Code.cBadParamListOnOps,
            location,
            "Bad parameter list for overloaded operator:" + stHint);   
    }
    
#endregion // Errors    

#region Public Methods for IParser



//-----------------------------------------------------------------------------
// Top level function
// Parse a compile-unit (single source file)
// A program may contain multiple source files
//
// ** rules **
// Program -> NamespaceBody
//-----------------------------------------------------------------------------    
    public NamespaceDecl ParseSourceFile()
    {   
        NamespaceDecl nGlobal = null;

        try
        {
            NamespaceDecl t = null;
            FileRange f = BeginRange();
            t = ParseNamespaceBody(new Identifier("", null));            
            ReadExpectedToken(Token.Type.cEOF); // this could throw an exception...
            t.SetLocation(EndRange(f));
            
            // Only once we know we can't get any errors do we set nGlobal to non-null
            nGlobal = t;
        }
        
        // Strip away parser errors. We must have called ThrowError() to throw
        // this exception, and so we know it's already been reported.
        catch(ParserErrorException)
        {            
        }
        
        // General catch to protect the parsing subsystem
        catch(System.Exception e)
        {
            Blue.Driver.PrintError_InternalError(e, "parse");            
        }   
                        
        return nGlobal;
    }
#endregion

#region Linenumber tracking
//-----------------------------------------------------------------------------
// Helpers to rack line numbers.
// BeginRange returns a filerange to pass to GetCurRange(). Together these 
// provide a FileRange to describe whatever is parsed between Begin & End.
// They can be nested.
//-----------------------------------------------------------------------------
    
    protected FileRange BeginRange()
    {
        Token t = m_lexer.PeekNextToken();
        
        FileRange f = new FileRange(t.Location.Filename,
            t.Location.RowStart, t.Location.ColStart,
            -1, -1);
            
        return f;            
    }
        
    protected FileRange EndRange(FileRange f)
    {   
        // Can't peek at the current token to decide where the end is because
        // it may be on another line.
        // So we store the last token read and use that.        
        f.RowEnd = m_tLastRead.Location.RowEnd;
        f.ColEnd = m_tLastRead.Location.ColEnd;
        return f;            
    }
    
    private Token m_tLastRead;

#endregion

#region Standard Helpers
//-----------------------------------------------------------------------------
// Helper to consume and throw away a token (useful after we peek at it)
//-----------------------------------------------------------------------------
    protected void ConsumeNextToken()
    {
        m_tLastRead = m_lexer.GetNextToken();
    }

//-----------------------------------------------------------------------------
// Helper to read an expected token and throw error if it's not found
//-----------------------------------------------------------------------------
    protected Token ReadExpectedToken(Token.Type tExpected)
    {
        Token tNext = m_lexer.GetNextToken();
        if (tNext.TokenType != tExpected)
        {            
            ThrowError(E_UnexpectedToken(tNext, tExpected));
        }
        m_tLastRead = tNext;
        return tNext;
    }
    
//-----------------------------------------------------------------------------
// Helper to read an identifier and return the string
// Will throw an error if we don't have an identifier.
//-----------------------------------------------------------------------------    
    protected Identifier ReadExpectedIdentifier()
    {
        Token t = ReadExpectedToken(Token.Type.cId);
        return new Identifier(t.Text, t.Location);        
    }

#endregion

#region Parse Declarations
//-----------------------------------------------------------------------------
// 3 sorts of Types.
//-----------------------------------------------------------------------------
    protected enum Genre
    {
        cInterface,
        cClass,
        cStruct
    }
    
//-----------------------------------------------------------------------------
// Parse a namespace.
//
// ** rules **
// Namespace -> 'namespace' id.id.id... '{' NamespaceBody '}'
//-----------------------------------------------------------------------------
    protected NamespaceDecl ParseNamespace()
    {        
        ReadExpectedToken(Token.Type.cNamespace);
        return ParseNamespaceHelper();
        
    }
    
    protected NamespaceDecl ParseNamespaceHelper()
    {   
        Token t;                      
        Identifier i = ReadExpectedIdentifier();
        
        
        t = m_lexer.PeekNextToken();
        if (t.TokenType == Token.Type.cDot)
        {
            ConsumeNextToken();
            NamespaceDecl nInner = ParseNamespaceHelper();
            return new NamespaceDecl(i, null, new NamespaceDecl[] { nInner}, null, null);
        }        
        
        FileRange f = this.BeginRange();
        ReadExpectedToken(Token.Type.cLCurly);        
        NamespaceDecl n = ParseNamespaceBody(i);
        ReadExpectedToken(Token.Type.cRCurly);
        n.SetLocation(this.EndRange(f));
        
        return n;
    }

//-----------------------------------------------------------------------------
// Parse a namespace body (since we're just parsing the body, we have to 
// pass in the name).
// 
// ** rules **
// NamespaceBody-> (UsingDirectives)? (Namespace | ClassDecl)*
//-----------------------------------------------------------------------------
    protected NamespaceDecl ParseNamespaceBody(Identifier idName)
    {
        UsingDirective[] arUsingNodes = ParseUsingDirectives();
                
        //ArrayList alClasses = new ArrayList();
        ArrayList alNestedNamespaces = new ArrayList();
        //ArrayList alEnums = new ArrayList();
        ArrayList alTypes = new ArrayList();

        while(true)
        {                        
            Modifiers mods = ParseModifiers();
            Token t = m_lexer.PeekNextToken();
             
            switch(t.TokenType)
            {
                case Token.Type.cClass:
                {
                    ClassDecl nodeClass = ParseClass(mods);
                    alTypes.Add(nodeClass);
                }
                    break;
                    
                case Token.Type.cStruct:
                {
                    ClassDecl nodeStruct = ParseStruct(mods);
                    alTypes.Add(nodeStruct);                
                }
                    break;                    

                case Token.Type.cInterface:
                {
                    ClassDecl nodeClass = ParseInterface(mods);
                    alTypes.Add(nodeClass);
                }
                    break;
            
                case Token.Type.cNamespace:
                {
                    NamespaceDecl nNested = ParseNamespace();
                    alNestedNamespaces.Add(nNested);
                }
                    break;

                case Token.Type.cEnum:
                {
                    EnumDecl e = ParseEnum(mods);
                    alTypes.Add(e);                    
                }
                    break;
                    
                case Token.Type.cDelegate:
                {
                    DelegateDecl d = ParseDelegate(mods);
                    alTypes.Add(d);
                }
                    break;

                default:
                    // Like to break out of the 'while' here, but a 'break' statement
                    // will just get out of the 'switch', not the while, so use a goto
                    goto Done;

            } // end switch
            
                
            t = m_lexer.PeekNextToken();
        } // end while
Done:
        NamespaceDecl [] arNamespaces = (NamespaceDecl []) alNestedNamespaces.ToArray(typeof(NamespaceDecl));
        TypeDeclBase [] arTypes = (TypeDeclBase []) alTypes.ToArray(typeof(TypeDeclBase));
                    
        return new NamespaceDecl(idName, arUsingNodes, arNamespaces, null, arTypes);    
    }

//-----------------------------------------------------------------------------
// Parse and return an array of using declarations 
// ** rules **
// UsingDirective -> 'using' id:name '=' id_list ';'      // for aliasing
// UsingDirective -> 'using' id_list ';'                  // for namespaces
//-----------------------------------------------------------------------------
    protected UsingDirective[] ParseUsingDirectives()
    {
        Token t;
                       
        t = m_lexer.PeekNextToken();

        ArrayList a = new ArrayList();

        while( t.TokenType == Token.Type.cUsing)
        {
            ConsumeNextToken();
            Exp o = ParseDottedIdList();

            UsingDirective node;

            FileRange f = this.BeginRange();        
            t = m_lexer.PeekNextToken();
            if (t.TokenType == Token.Type.cAssign)
            {
                ConsumeNextToken();
                // o had better be a single identifier
                SimpleObjExp s = o as SimpleObjExp;
                Debug.Assert(s != null);
                string stAlias = s.Name.Text;

                Exp o2 = ParseDottedIdList();
                node = new UsingDirective(stAlias, o2);
            } 
            else 
            {
                node = new UsingDirective(o);
            }
            ReadExpectedToken(Token.Type.cSemi);

            Debug.Assert(node != null);
            node.SetLocation(this.EndRange(f));
            a.Add(node);
            t = m_lexer.PeekNextToken();
        }


        UsingDirective[] arNodes = (UsingDirective[]) a.ToArray(typeof(UsingDirective));
        
        return arNodes;
    }

//-----------------------------------------------------------------------------
// Parse delegate declaration
// --> 'delegate' Type:rettype id:name '(' param_list ')' ';'
//-----------------------------------------------------------------------------
    protected DelegateDecl ParseDelegate(Modifiers mods)
    {
        ReadExpectedToken(Token.Type.cDelegate);
        TypeSig tRetType            = ParseTypeSig();
        Identifier idName           = ReadExpectedIdentifier();        
        ParamVarDecl [] arParams    = ParseParamList(); // includes '(' ... ')'
        ReadExpectedToken(Token.Type.cSemi);
    
        FileRange f = this.BeginRange();
        DelegateDecl node = new DelegateDecl(idName, tRetType, arParams, mods);
        node.SetLocation(this.EndRange(f));
        return node;
    }
    

//-----------------------------------------------------------------------------
// Parse enum declaration
// --> 'enum' id:name '{' enum_decl_list '}'
//-----------------------------------------------------------------------------
    protected EnumDecl ParseEnum(Modifiers modsEnums)
    {
        ReadExpectedToken(Token.Type.cEnum);

        Identifier idName = ReadExpectedIdentifier();
        FileRange f2 = this.BeginRange();
        ReadExpectedToken(Token.Type.cLCurly);
        
        ArrayList a = new ArrayList();

        // All enum fields are Static, Public, Literal
        // and have fieldtype set to type of the enum
        //Modifiers mods = new Modifiers(Modifiers.EFlags.Public | Modifiers.EFlags.Static);
        Modifiers mods = new Modifiers();
        mods.SetPublic();
        mods.SetStatic();
        
        TypeSig tSig = new SimpleTypeSig(new SimpleObjExp(idName));

        Identifier idPrev = null;

        Token t = m_lexer.PeekNextToken();
        while(t.TokenType != Token.Type.cRCurly)
        {
        // Parse fields
            Identifier id = ReadExpectedIdentifier();

            Exp expInit = null;

            t = m_lexer.PeekNextToken();
            if (t.TokenType == Token.Type.cAssign)
            {                
                ConsumeNextToken();
                expInit = ParseExp();
            } 
            else 
            {
#if false
                // If no explicit assignment, then we must create one
                // first field -> '=0'                
                if (idPrev == null)
                {
                    expInit = new IntExp(0, id.Location);
                } 

                // all other fields -> '= <prevfield>  + '1' '
                else 
                {
                    expInit = new BinaryExp(
                        new SimpleObjExp(idPrev),
                        new IntExp(1, id.Location),
                        BinaryExp.BinaryOp.cAdd);
                }
#endif
            }

            //EnumField e = new EnumField(id);
            FieldDecl e = new FieldDecl(id, tSig, mods, expInit);
            a.Add(e);
            

            // If no comma, then this had better be our last one
            t = m_lexer.PeekNextToken();
            if (t.TokenType != Token.Type.cComma)
            {
                break;
            }
            ReadExpectedToken(Token.Type.cComma);
            
            idPrev = id;
            t = m_lexer.PeekNextToken();
        } // while parsing fields
        
        ReadExpectedToken(Token.Type.cRCurly);

        // Convert array list to EnumField[]
        FieldDecl [] f = new FieldDecl[a.Count];
        for(int i = 0; i < f.Length; i++)
            f[i] = (FieldDecl) a[i];


        EnumDecl node = new EnumDecl(idName, f, modsEnums);
        node.SetLocation(this.EndRange(f2));
        
        return node;
        
            
    }




//-----------------------------------------------------------------------------
// Parse an interface
//
// ** rules **
// InterfaceDecl-> 'interface' id:name '{' body '}'
// InterfaceDecl-> 'interface' id:name ':' base_list '{' body '}'    
//-----------------------------------------------------------------------------
    protected ClassDecl ParseInterface(Modifiers modsInterface)
    {
        Token t;
        ReadExpectedToken(Token.Type.cInterface);

        Identifier idName = ReadExpectedIdentifier();
        TypeSig[] arBase = null;
        
        //if (!modsInterface.IsPublic)
            //modsInterface.FlagSetter |= Modifiers.EFlags.Private;
        if (modsInterface.VisibilityNotSet)
            modsInterface.SetPrivate();
            

        ArrayList alMethods = new ArrayList();
        ArrayList alProperties = new ArrayList();

        // Read list of base interfaces that we derive from
        t = m_lexer.PeekNextToken();
        if (t.TokenType == Token.Type.cColon)
        {
            ConsumeNextToken(); // ':'
            arBase = ParseIdNameList();
        }

        ReadExpectedToken(Token.Type.cLCurly);

        // Read members
        t = m_lexer.PeekNextToken();
        while(t.TokenType != Token.Type.cRCurly)
        {
            // member:
            // method -> rettype id:name '(' param_list ')' ';'
            // property -> rettype id:name '{' set ';'  get ';' '}'
            TypeSig rettype = ParseTypeSig(); 
            Identifier idMember = ReadExpectedIdentifier();
            
            
            t = m_lexer.PeekNextToken();
            
            // All interface members have these attributes
            /*
            AST.Modifiers mods = new AST.Modifiers(
                AST.Modifiers.EFlags.Abstract | 
                AST.Modifiers.EFlags.Virtual |
                AST.Modifiers.EFlags.Public
            );
            */
            Modifiers mods = new Modifiers();
            mods.SetAbstract();
            mods.SetVirtual();
            mods.SetPublic();
                
            // Method
            if (t.TokenType == Token.Type.cLParen)
            {            
                MemberDecl m = this.PartialParseMethodDecl(mods, rettype, idMember, Genre.cInterface);
                alMethods.Add(m);                
            }
            
            // Property
            else if (t.TokenType == Token.Type.cLCurly)
            {                                     
                PropertyDecl p = PartialParsePropertyDecl(mods, rettype, idMember);
                alProperties.Add(p);            
            } 
            
            // Indexer
            else if (t.TokenType == Token.Type.cLSquare)
            {
                PropertyDecl p = PartialParseIndexerDecl(mods, rettype, idMember);
                alProperties.Add(p);            
            }
            
            // Error
            else {
                //this.ThrowError_UnexpectedToken(t);   
                ThrowError(E_UnexpectedToken(t));
            }
                        
            t = m_lexer.PeekNextToken();
        }
        ReadExpectedToken(Token.Type.cRCurly); // '}'

        MethodDecl [] arMethods = this.MethodDeclFromArray(alMethods);
        PropertyDecl [] arProperties = this.PropertyDeclFromArray(alProperties);

        ClassDecl d = new ClassDecl(idName, arBase, arMethods, arProperties, modsInterface);

        return d;
    }


//-----------------------------------------------------------------------------
// Parse a class, reutrn a ClassDecl node
//
// ** Rules **
// ClassDecl -> 'class' id:name '{' membeddecl_list '}'
// ClassDecl -> 'class' id:name ':' id:Super '{' membeddecl_list '}'
//----------------------------------------------------------------------------- 
    protected ClassDecl ParseClass(Modifiers modsClass)
    {
        return ParseClassOrStruct(modsClass, true);
    }
    
    protected ClassDecl ParseStruct(Modifiers modsClass)
    {
        return ParseClassOrStruct(modsClass, false);
    }

    protected ClassDecl ParseClassOrStruct(Modifiers modsClass, bool fIsClass)
    {
        Token t;
        
        
        if (fIsClass)
            ReadExpectedToken(Token.Type.cClass);
        else 
            ReadExpectedToken(Token.Type.cStruct);
        
        // Get Name of the type                
        Identifier stClassName = ReadExpectedIdentifier();
        
        t = m_lexer.PeekNextToken();
        
        // Get base list
        TypeSig[] arBase = null;
        
        if (t.TokenType == Token.Type.cColon)
        {
            ConsumeNextToken();            
            arBase = ParseIdNameList();
        }

        ReadExpectedToken(Token.Type.cLCurly);
                
        ArrayList alMethods     = new ArrayList();
        ArrayList alFields      = new ArrayList();
        ArrayList alProperties  = new ArrayList();
        ArrayList alNestedTypes = new ArrayList();
        ArrayList alEvents      = new ArrayList();
        
        // Parse list of memberdecls
        // We peek at the first token. If it's a '}', then we're done parsing members,
        // Else Figure out what type of member this is and parse it.
        
        t = m_lexer.PeekNextToken();
        while (t.TokenType != Token.Type.cRCurly)
        {
            // mods -> <set of member modifiers, like public, protected, static, etc>
            // type -> <some type>
            // ctordecl   -> attrs      id '(' paramlist ')' '{' statementlist '}'
            // methoddecl -> attrs type id '(' paramlist ')' '{' statementlist '}'
            //            -> attrs type 'operator' op '(' paramlist ')' body
            // fielddecl  -> attrs type id ';'
            // propdecl   -> attrs type id '{' ... '}'            
            // typedecl   -> attrs 'enum' ...
            // eventdecl  -> attrs 'delegate' type id ';'
            
            // All members start with 'attrs type id'. (except ctor) So do those.
            Modifiers mods = ParseModifiers();
            
            // Make private a default
            if (mods.VisibilityNotSet)
                mods.SetPrivate();
                                                            
            // @todo - we need a clean way to decide if this is a ctor or a methoddecl
            Identifier tTempId = null;
            t = m_lexer.PeekNextToken();
            
            // Check events
            if (t.TokenType == Token.Type.cEvent)
            {
                EventDecl e = PartialParseEventDecl(mods);
                alEvents.Add(e);
                continue;
            }
            
                        
            // Check if this is a nested type
            if (t.TokenType == Token.Type.cEnum)
            {
                EnumDecl e = ParseEnum(mods);
                alNestedTypes.Add(e);
                
                t = m_lexer.PeekNextToken();               
                continue;
            }
                                    
            if (t.TokenType == Token.Type.cDelegate)
            {
                DelegateDecl d = ParseDelegate(mods);
                alNestedTypes.Add(d);
                t = m_lexer.PeekNextToken();
                continue;
            }
            
            if (t.TokenType == Token.Type.cClass)
            {
                ClassDecl d = ParseClass(mods);
                alNestedTypes.Add(d);
                t = m_lexer.PeekNextToken();
                continue;
            }
            
            if (t.TokenType == Token.Type.cStruct)
            {
                ClassDecl d = ParseStruct(mods);
                alNestedTypes.Add(d);
                t = m_lexer.PeekNextToken();
                continue;
            }
            
            
            // Not a nested type, so it's some other member (maybe a ctor)...
            if (t.TokenType == Token.Type.cId)
            {
                tTempId = t.Id;    
            } 
                
            TypeSig type = ParseTypeSig();
            
            Identifier stMemberName;                        
            // Ctor - has a '(' instead of another identifier for the member name
            t = m_lexer.PeekNextToken();
            if (t.TokenType == Token.Type.cLParen)
            {
                type = null; // ctor has a no return type
                stMemberName = tTempId;

                // If the ctor name doesn't match the class name, we have some error
                if (stMemberName.Text != stClassName.Text)
                {
                    ThrowError(new ParserErrorException(
                        Code.cMissingRetType, 
                        stMemberName.Location, 
                        "Missing a return type on a method"
                    ));
                }

            } else {
                // Check for overloaded operator
                if (t.TokenType == Token.Type.cOperator)
                {
                    MethodDecl m = this.PartialParseOverloadedOp(mods, type);
                    alMethods.Add(m);
                    
                    t = m_lexer.PeekNextToken();
                    continue;
                }
            
                // Not a ctor, so we can go ahead and read the identifier
                stMemberName = ReadExpectedIdentifier();
            }
            
            t = m_lexer.PeekNextToken();
            
            // MethodDecl. Has a '(' next
            if (t.TokenType == Token.Type.cLParen)
            {   
                MethodDecl m = PartialParseMethodDecl(mods, type, stMemberName, fIsClass ? Genre.cClass : Genre.cStruct);                
                alMethods.Add(m);
            } 
            
            // FieldDecl. Has a ';' (or possibly an '=') next
            else if ((t.TokenType == Token.Type.cSemi) || (t.TokenType == Token.Type.cAssign))
            {       
                Exp expInit = null;
                if (t.TokenType == Token.Type.cAssign)
                {
                    ConsumeNextToken();
                    expInit = ParseExp();
                }
                
                ReadExpectedToken(Token.Type.cSemi);
                
                FieldDecl f = new FieldDecl(stMemberName, type, mods, expInit);
                alFields.Add(f);
            } 
            
            // Property
            else if (t.TokenType == Token.Type.cLCurly)
            {
                PropertyDecl p = PartialParsePropertyDecl(mods, type, stMemberName);
                alProperties.Add(p);            
            }
            
            // Indexer
            else if (t.TokenType == Token.Type.cLSquare)
            {
                PropertyDecl p = PartialParseIndexerDecl(mods, type, stMemberName);
                alProperties.Add(p);            
            }
            
            // Syntax error
            else {
                ThrowError(E_UnexpectedToken(t));
            }
        
            t = m_lexer.PeekNextToken();
        } // end member decl list
        
        ReadExpectedToken(Token.Type.cRCurly);    
        
        MethodDecl [] arMethods = MethodDeclFromArray(alMethods);
        FieldDecl [] arFields = FieldDeclFromArray(alFields);
        PropertyDecl [] arProperties = PropertyDeclFromArray(alProperties);
        
        EventDecl [] arEvents = (EventDecl[]) alEvents.ToArray(typeof(EventDecl));
        
        TypeDeclBase[] arNestedTypes = new TypeDeclBase[alNestedTypes.Count];
        for(int i = 0; i < alNestedTypes.Count; i++)
            arNestedTypes[i] = (TypeDeclBase) alNestedTypes[i];
        
        return new ClassDecl(
            stClassName, 
            arBase, 
            arMethods,
            arProperties,
            arFields,
            arEvents,
            arNestedTypes,
            modsClass,
            fIsClass
        );
    }

#region Array conversions
//-----------------------------------------------------------------------------
// Helpes to convert from ArrayLists to normal arrays
// Have to do this until mscorlib works the bugs out of its array conversion
// stuff.
//-----------------------------------------------------------------------------
    protected ParamVarDecl[] ParamVarDeclFromArray(ArrayList alParams)
    {
        ParamVarDecl[] v = new ParamVarDecl[alParams.Count];
        for(int i = 0; i < alParams.Count; i++)            
            v[i] = (ParamVarDecl) alParams[i];
            
        return v;
    }
    
    protected LocalVarDecl[] LocalVarDeclFromArray(ArrayList alParams)
    {
        LocalVarDecl[] v = new LocalVarDecl[alParams.Count];
        for(int i = 0; i < alParams.Count; i++)            
            v[i] = (LocalVarDecl) alParams[i];
            
        return v;
    }
    
    protected FieldDecl[] FieldDeclFromArray(ArrayList alParams)
    {
        FieldDecl[] v = new FieldDecl[alParams.Count];
        for(int i = 0; i < alParams.Count; i++)            
            v[i] = (FieldDecl) alParams[i];
            
        return v;
    }
    
    protected PropertyDecl [] PropertyDeclFromArray(ArrayList alProperties)
    {
        PropertyDecl [] v = new PropertyDecl[alProperties.Count];
        for(int i = 0; i < alProperties.Count; i++)
            v[i] = (PropertyDecl) alProperties[i];
            
        return v;
    }
    
    protected Statement[] StatementFromArray(ArrayList alParams)
    {
        Statement[] v = new Statement[alParams.Count];
        for(int i = 0; i < alParams.Count; i++)            
            v[i] = (Statement) alParams[i];
            
        return v;
    }
    
    protected MethodDecl[] MethodDeclFromArray(ArrayList alParams)
    {
        MethodDecl[] v = new MethodDecl[alParams.Count];
        for(int i = 0; i < alParams.Count; i++)            
            v[i] = (MethodDecl) alParams[i];
            
        return v;
    }
#endregion
    
//-----------------------------------------------------------------------------
// Parse a parameter list (including opening & closing parens)
// -> '(' (''|'ref'|'out') typesig id ',' typesig id ',' ... ')'
//-----------------------------------------------------------------------------

// @todo - allow out,ref
    protected ParamVarDecl [] ParseParamList()
    {
        ReadExpectedToken(Token.Type.cLParen);

        // Read parameter list. Keep looping until we get the closing ')'
        // param-> Typesig id
        // paramlist-> <comma separated list of 'param'>
        ArrayList alParams = new ArrayList();
    
        Token t = m_lexer.PeekNextToken();
        
        if (t.TokenType == Token.Type.cRParen)
            ConsumeNextToken();
        
        bool fUsedParams = false;
        while (t.TokenType != Token.Type.cRParen)
        {
            Debug.Assert(!fUsedParams, "@todo - 'params' only allowed on last thing");
            t = m_lexer.PeekNextToken();
            // Check for flow modifier
            AST.EArgFlow eFlow = EArgFlow.cIn;
            if (t.TokenType == Token.Type.cRef)
                eFlow = EArgFlow.cRef;
            else if (t.TokenType == Token.Type.cOut)
                eFlow = EArgFlow.cOut;  
            
            // Allow 'params' modifier for a vararg on last parameter
            else if (t.TokenType == Token.Type.cParams)
            {
                fUsedParams = true;   
                ConsumeNextToken();
                t = m_lexer.PeekNextToken();
            }                              
                
            if (eFlow != EArgFlow.cIn) {
                ConsumeNextToken();                
            }
            
            
            // param-> Typesig id
            NonRefTypeSig type = ParseTypeSig();
            Identifier stName = ReadExpectedIdentifier();
        
            VarDecl nodeDecl = new ParamVarDecl(stName, type, eFlow);
            alParams.Add(nodeDecl);
        
            t = m_lexer.GetNextToken();
            
            CheckError_UnexpectedToken(t, new Token.Type [] { Token.Type.cComma, Token.Type.cRParen } );            
        }        
        
    
        ParamVarDecl [] arParams = ParamVarDeclFromArray(alParams);

        return arParams;
    }

//-----------------------------------------------------------------------------
// Partial parse an Indexer decl
//
// ** rules **
// IndexerDecl  -> mods type 'this' '[' param_list ']' '{' property_body '}'
//-----------------------------------------------------------------------------
    protected PropertyDecl PartialParseIndexerDecl(
        Modifiers mods, 
        TypeSig typeReturn, 
        Identifier stMemberName
    )
    {
        Debug.Assert(stMemberName.Text == "this");
        
        // @todo - Change name to 'Item'
        
        FileRange f = this.BeginRange();
        ReadExpectedToken(Token.Type.cLSquare);
        // @todo  - For now, we only support one parameter
        NonRefTypeSig t = this.ParseTypeSig();
        Identifier idParam = this.ReadExpectedIdentifier();   
             
        ReadExpectedToken(Token.Type.cRSquare);
                
        BlockStatement stmtGet;
        BlockStatement stmtSet;
                        
        bool fHasGet;
        bool fHasSet;
                        
        ParseAccessors(mods.IsAbstract, stMemberName,
            out stmtGet, out fHasGet,
            out stmtSet, out fHasSet);
        
        PropertyDecl p = new PropertyDecl(
            stMemberName, typeReturn,
            new ParamVarDecl(idParam, t, EArgFlow.cIn), 
            stmtGet, fHasGet, 
            stmtSet, fHasSet, 
            mods);
          
        p.SetLocation(this.EndRange(f));            
        return p;            
    }

//-----------------------------------------------------------------------------
// Partial parse an event
// ** rules **
// EventDecl -> mods 'event' type id ';'
//-----------------------------------------------------------------------------
    protected EventDecl PartialParseEventDecl(
        Modifiers mods)
    {
        
        FileRange f = this.BeginRange();
        ReadExpectedToken(Token.Type.cEvent);
        
        NonRefTypeSig t = ParseTypeSig();
        Identifier idName = ReadExpectedIdentifier();
        ReadExpectedToken(Token.Type.cSemi);


        EventDecl node = new EventDecl(idName, t, mods);            
        node.SetLocation(this.EndRange(f));
        
        return node;
    }        

//-----------------------------------------------------------------------------
// Partial parse a property decl.
// We've already parsed the modifiers, return type & member name, so
// pass those in as parameters. Start parsing at '{'
// Note that indexers are just properties that take parameters.
//
// ** rules **
// PropertyDecl -> mods type id '{' property_body '}'
//-----------------------------------------------------------------------------
    protected PropertyDecl PartialParsePropertyDecl(
        Modifiers mods, 
        TypeSig typeReturn, 
        Identifier stMemberName
    )
    {   
        // Note that properties can be abstract, in which case their bodyStmt is null.
        // Also note that both get & set have the same modifiers        
                
        FileRange f = this.BeginRange();
                        
        BlockStatement stmtGet;
        BlockStatement stmtSet;
                        
        bool fHasGet;
        bool fHasSet;
                        
        ParseAccessors(mods.IsAbstract, stMemberName,
            out stmtGet, out fHasGet,
            out stmtSet, out fHasSet);
                
        PropertyDecl p = new PropertyDecl(
            stMemberName, typeReturn, 
            stmtGet, fHasGet, 
            stmtSet, fHasSet, 
            mods); 
        
        p.SetLocation(this.EndRange(f));
        return p;
    }
    
//-----------------------------------------------------------------------------
// Parse accessors
//
// *** rules ***
// propertyBody -> 'get' block | 'set' block  | 
//                 'get' block 'set' block | 
//                 'set' block 'get' block
//
// For an abstract property:
// PropertyBody -> 'get' ';' | 'set' ';' | 
//                 'get' ';' 'set' ';' | 
//                 'set' ';' 'get' ';'
//-----------------------------------------------------------------------------    
    protected void ParseAccessors(
        bool fIsAbstract,
        Identifier stMemberName,
        out BlockStatement stmtGet,
        out bool fHasGet,
        out BlockStatement stmtSet,
        out bool fHasSet
    )
    {    
        // We've already parsed everything up until, but not including, the '{'
        ReadExpectedToken(Token.Type.cLCurly);
                
        stmtGet = null;
        stmtSet = null;
                        
        fHasGet = false;
        fHasSet = false;
        
        Token t  = m_lexer.GetNextToken();
        
        // Parse the get/set accesssors
        while(t.TokenType != Token.Type.cRCurly)
        {                            
            switch(t.TokenType)
            {
                case Token.Type.cGet:
                    if (fHasGet)
                        ThrowError(E_AccessorAlreadyDefined(stMemberName, true));
                    
                    fHasGet = true;
                    if (fIsAbstract)
                        ReadExpectedToken(Token.Type.cSemi);
                    else
                        stmtGet = ParseStatementBlock();
                    break;
                
                case Token.Type.cSet:
                    if (fHasSet)
                        ThrowError(E_AccessorAlreadyDefined(stMemberName, false));

                    fHasSet = true;
                    if (fIsAbstract)
                        ReadExpectedToken(Token.Type.cSemi);
                    else                        
                        stmtSet = ParseStatementBlock();
                    break;
                
                default: // error
                    //this.ThrowError_UnexpectedToken(t, new Token.Type [] { Token.Type.cGet, Token.Type.cSet } );
                    ThrowError(E_UnexpectedToken(t, Token.Type.cGet, Token.Type.cSet));
                    break;
            }
            
            t = m_lexer.GetNextToken();
        }
        
        // Already consumed the closing '}', so we're done. 
        // So just do some error checks & create the ast node
        
        if (!fHasGet && !fHasSet)
            ThrowError(E_MissingAccessor(stMemberName));
    }        
        

//-----------------------------------------------------------------------------
// Parse for overloaded operator
// -> mods type 'operator' op '(' paramlist ')' '{' statementlist '}'
//-----------------------------------------------------------------------------
    protected MethodDecl PartialParseOverloadedOp(    
        Modifiers mods, // must be public & static
        TypeSig typeReturn
    )
    {
        /*
        Modifiers modsLegal = new Modifiers(Modifiers.EFlags.Public | Modifiers.EFlags.Static);
        if (modsLegal.Flags != mods.Flags)
            ThrowError_BadModsOnOps(typeReturn.Location);
        */
        if (!mods.IsStatic || !mods.IsPublic)
            ThrowError(E_BadModsOnOps(typeReturn.Location));
        
            
        ReadExpectedToken(Token.Type.cOperator);
        
        // Get the operator
        Token tOp = m_lexer.GetNextToken();
        BinaryExp.BinaryOp op = ConvertToBinaryOp(tOp);
        
        // @todo - Check that it's a valid overloadable op...
        
        // Parse parameters. Expect 2, both values (not ref/out)
        ParamVarDecl [] arParams = ParseParamList();
        if (arParams.Length != 2)
            ThrowError(E_BadParamListOnOps(typeReturn.Location, "Must have 2 parameters"));

        
        for(int i = 0; i < arParams.Length; i++)
        {   
            if (arParams[i].Flow != EArgFlow.cIn)
                ThrowError(E_BadParamListOnOps(arParams[i].Location, "Parameter " + i + " can't be ref/out"));
        }
            
        
        // Read method body. This will include the '{' ... '}'    
        BlockStatement block = ParseStatementBlock();
        
        // Allocate the method decl
        MethodDecl nodeMethod = new MethodDecl(
            op, 
            typeReturn, 
            arParams, 
            block
        );
            
        return nodeMethod;            
        
    }

//-----------------------------------------------------------------------------
// Do a partial parse of the method decl (includes ctor), 
// Pass in the parameters that we've already parsed, which is everything 
// before the first '('. 
//
// ** Rules **
// methoddecl -> attrs type id '(' paramlist ')' '{' statementlist '}'
// methoddecl -> attrs type id '(' paramlist ')' ';'   // if abstract
//-----------------------------------------------------------------------------
    protected MethodDecl PartialParseMethodDecl(
        Modifiers mods, 
        TypeSig typeReturn,
        Identifier stMemberName,
        Genre genre // applies additional restrictions
    )
    {   
    // We should have already parsed the 'attrs type id'. So continue with param list
        
        ParamVarDecl [] arParams = ParseParamList();
        
        // Structs can't define a default ctor
        if ((genre == Genre.cStruct) && (typeReturn == null) && (arParams.Length == 0))
        {
            ThrowError(E_NoDefaultCtorForStructs(m_lexer.PeekNextToken().Location));
        }
        

        CtorChainStatement chain  = null;
    // If this is a constructor, then we can chain it
    // ctordecl -> mods type id '(' param_list ')' ':' (this|base) '(' param_list ')' '{' statementlist '}'
    // ctor can't be abstract / virtual. Can be static
        if (typeReturn == null)
        {   
            Token t = m_lexer.PeekNextToken();
            if (genre == Genre.cInterface)
            {
                ThrowError(E_NoCtorOnInterface(t.Location));
            }        
            
            if (t.TokenType == Token.Type.cColon)
            {                
                ConsumeNextToken();
                // Currently, 'base' & 'this' are just identifiers, not specific tokens
                Identifier id = this.ReadExpectedIdentifier();
                Exp [] arParams2 = ParseExpList();

                CtorChainStatement.ETarget eTarget = (CtorChainStatement.ETarget) (-1);
                
                if (id.Text == "this")
                    eTarget = CtorChainStatement.ETarget.cThis;
                else if (id.Text == "base")
                {
                    if (genre == Genre.cStruct)
                    {
                        ThrowError(E_NoBaseChainForStructs(id.Location));
                    }
                    eTarget = CtorChainStatement.ETarget.cBase;
                }
                else 
                {
                    ThrowError(E_BadCtorChain(id));
                }

                chain = new CtorChainStatement(eTarget, arParams2);
            } 
            else 
            {
                // If no explicit ctor chain, then we still have an implicit "base ()"
                // (except for static ctors, which can't be chained)
                if (!mods.IsStatic && (genre == Genre.cClass))                
                {                
                    chain = new CtorChainStatement();
                }
            }

            // Static ctors can't be chained
            if (chain != null)
            {
                if (mods.IsStatic)                
                {
                    ThrowError(E_NoChainForStaticCtor(stMemberName.Location));
                }
            }
        }


    // Parse body
        BlockStatement block = null;

        if (mods.IsAbstract)        
        {
        // For abstract methods, no body. Just end with a ';'
            ReadExpectedToken(Token.Type.cSemi);        
        } else 
        {        
        // Read method body. This will include the '{' ... '}'    
            block = ParseStatementBlock();
        }


        if (typeReturn == null)
        {
            if (mods.IsAbstract | mods.IsVirtual | (block == null))            
            {
                ThrowError(E_NoAbstractCtor(stMemberName));
            }
        }
        
        // Allocate the method decl
        MethodDecl nodeMethod = new MethodDecl(
            stMemberName, 
            typeReturn, 
            arParams, 
            block,
            mods);

        // If we have a chain, inject it into the statements
        if (chain != null)
        {
            nodeMethod.Body.InjectStatementAtHead(chain);
            chain.FinishInit(nodeMethod);
        }

        return nodeMethod;
    }
#endregion Parse Declarations

#region Parse Statements
//-----------------------------------------------------------------------------
// Parse a scope (includes statement list & declaration)
// The parser will separate out local declarations from statements and
// return 2 separate arrays.
// It will also convert constructs like "int x = 3" to "int x; x = 3"
//
// ** Rules ***
// BlockStatement-> '{' (decl | statement)* '}'
// statement->
//      ObjExp '.' id '(' explist ')' ';'   // method call
//      return [ exp ] ';'                  // return statement (w/ optional return expression)
//      BlockStatement                      // blocks can be nested
//      
// decl ->
//      TypeSig id ';'              // declare a local variable
//      TypeSig id '=' exp ';'      // declare a local and do an assignment.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Wrapper when we expect a statement
//-----------------------------------------------------------------------------
    protected Statement ParseStatement()
    {
        Statement s;
        LocalVarDecl v;
                
        ParseStatementOrLocal(out s, out v);        
        Debug.Assert(v == null);
        
        
        
        return s;
    }

//-----------------------------------------------------------------------------
// Helper to parse array decls.
// For arrays, leftmost [] is the outermost
// So X[][,,][,] is 1d of 3d of 2d of X
// Because this is left to right (and not right to left), we have to be
// stack based / recursive (instead of an iterative while)
// 
// sigElemType is the type of the non-array portion (X in the above example)
// Note that if this isn't an array type, we'll just return sigElemType
//-----------------------------------------------------------------------------
    NonRefTypeSig ParseOptionalArrayDecl(NonRefTypeSig sigElemType)
    {
        Token t = m_lexer.PeekNextToken();
        
        if (t.TokenType == Token.Type.cLRSquare)
        {
            ConsumeNextToken();
            
            int dim = t.Dimension;
            NonRefTypeSig sig = ParseOptionalArrayDecl(sigElemType);
            sig = new ArrayTypeSig(sig, dim);
            
            string stTest = sig.ToString();
            
            return sig;
            
        } else {
            return sigElemType;
        }
    }
        
//-----------------------------------------------------------------------------
// Parse a single statement or local var decl (we can't tell which yet)
// Note that may be statement may be a block (which is just fine)
// for a construct like 'int x = 3' (decl & assignment), we'll yeild
// both s & v. Else the out params will be null if we don't have them.
//-----------------------------------------------------------------------------
    protected void ParseStatementOrLocal(out Statement s, out LocalVarDecl v)
    {
        // Wrap the worker to ensure that we have proper line number info
        FileRange f = BeginRange();
        
        // Do the real work
        ParseStatementOrLocal_Helper(out s, out v);
        
        if (s != null)
        {
            s.SetLocation(EndRange(f));
        }
    }
    
    // Do the real work
    protected void ParseStatementOrLocal_Helper(out Statement s, out LocalVarDecl v)
    {   
        s = null;
        v = null;
        
        // For each statement, we know which type based off the first token.
        // Expect for an identifier, in which case it could be a few things.
        Token t = m_lexer.PeekNextToken();
        
        #if false
        // Skip past any ';' (as empty statements)
        while(t.TokenType == Token.Type.cSemi)
        {
            ConsumeNextToken();
            t = m_lexer.PeekNextToken();            
        }
        #endif
                       
        if (IsStartOfExp(t))
        {
            FileRange f = BeginRange();
            
            // This could be either an expression or a type
            Exp e = ParseExp();
            t = m_lexer.PeekNextToken();

            
            // Case 1 - Var declaration:
            // If an identifier follows, then we just read a type and this is
            // a var declaration:
            // Type id ';'
            // Type id '=' exp ';'
            if (t.TokenType == Token.Type.cId)
            {
                TypeSig tSig  = this.ConvertExpToType(e);

                Identifier id = ReadExpectedIdentifier();
                
                v = new LocalVarDecl(id, tSig);
                                
                // Check for optional assignment (if there's an '=' after the name)
                Token t3 = m_lexer.PeekNextToken();
                if (t3.TokenType == Token.Type.cAssign)
                {
                    ConsumeNextToken();                     // '='
                    Exp eRHS = ParseExp();                  // exp                
                    ReadExpectedToken(Token.Type.cSemi);    // ';'
                    
                    SimpleObjExp oleft = new SimpleObjExp(id);
                    StatementExp se = new AssignStmtExp(oleft, eRHS);
                    s = new ExpStatement(se);
                    
                    se.SetLocation(EndRange(f));
                } else {                
                    ReadExpectedToken(Token.Type.cSemi);    // ';'
                }


                
                return;
            } // end decl case

            // Case 2 - label declaration
            else if (t.TokenType == Token.Type.cColon)
            {                
                SimpleObjExp o2 = e as SimpleObjExp;
                if (o2 != null)
                {
                    ConsumeNextToken(); // ':'
                    s = new LabelStatement(o2.Name);
                    return; // skip reading a ';'
                } 
                
                ThrowError(new ParserErrorException(Code.cBadLabelDef, t.Location, 
                    "Bad label definition (labels must be a single identifier)"));                                
            } // end case for label decls
                        
            // Expect a StatementExp
            else if (t.TokenType == Token.Type.cSemi) {
                ReadExpectedToken(Token.Type.cSemi);
                
                // Else we must be a StatementExp
                StatementExp se = e as StatementExp;
                if (se == null)
                    //this.ThrowError_ExpectedStatementExp(e.Location);
                    ThrowError(E_ExpectedStatementExp(e.Location));
                
                se.SetLocation(EndRange(f));
                s = new ExpStatement(se);            
                return;
            }
    
            ThrowError(E_UnexpectedToken(t));
        } // end start of expressions
        
        switch(t.TokenType)
        {
        // Empty statement
        case Token.Type.cSemi:
            ConsumeNextToken();
            s = new EmptyStatement();
            break;

        // Return -> 'return' ';'
        //         | 'return' exp ';'
        case Token.Type.cReturn:
            {
                ConsumeNextToken();
                
                t = m_lexer.PeekNextToken();
                Exp e = null;
                if (t.TokenType != Token.Type.cSemi) 
                {
                    e = ParseExp();                    
                }
                ReadExpectedToken(Token.Type.cSemi);
        
                s = new ReturnStatement(e);                
            }        
            break;
            
        // Note that the semi colons are included inthe stmt            
        // IfSmt -> 'if' '(' exp ')' stmt:then 
        // IfSmt -> 'if' '(' exp ')' stmt:then 'else' stmt:else
        case Token.Type.cIf:
        {
            ConsumeNextToken(); // 'if'
            ReadExpectedToken(Token.Type.cLParen);
            Exp exp = ParseExp();            
            ReadExpectedToken(Token.Type.cRParen);
            
            Statement sThen = ParseStatement();
            Statement sElse = null;
            
            Token t2 = m_lexer.PeekNextToken();
            if (t2.TokenType == Token.Type.cElse) 
            {
                ConsumeNextToken(); // 'else'
                sElse = ParseStatement();                
            }
            
            s = new IfStatement(exp, sThen, sElse);        
        }
            break;
            
        case Token.Type.cSwitch:
            s = ParseSwitchStatement();
            break;            
        
        // Throw an expression
        // ThrowStmt -> 'throw' objexp    
        case Token.Type.cThrow:
        {
            ConsumeNextToken(); // 'throw'
            Exp oe = null;
            if (m_lexer.PeekNextToken().TokenType != Token.Type.cSemi)
            {
                oe = ParseExp();            
            }
            ReadExpectedToken(Token.Type.cSemi);
            
            s = new ThrowStatement(oe);
        }
            break;
        
        // try-catch-finally
        case Token.Type.cTry:
            s = ParseTryCatchFinallyStatement();
            break;
        
        // while loop
        // 'while' '(' exp ')' stmt            
        case Token.Type.cWhile:
        {
            ConsumeNextToken(); // 'while'
            ReadExpectedToken(Token.Type.cLParen);

            Exp e = ParseExp();

            ReadExpectedToken(Token.Type.cRParen);

            Statement body = ParseStatement();

            s = new WhileStatement(e, body);


        }
            break;

        // do loop
        // 'do' stmt 'while' '(' exp ')' ';'
        case Token.Type.cDo:
        {
            ConsumeNextToken(); // 'do'
            Statement body = ParseStatement();
            ReadExpectedToken(Token.Type.cWhile);

            ReadExpectedToken(Token.Type.cLParen);
            Exp e = ParseExp();
            ReadExpectedToken(Token.Type.cRParen);

            ReadExpectedToken(Token.Type.cSemi);

            s = new DoStatement(e, body);

        }
            break;

        // goto
        // 'goto' id:label ';'
        case Token.Type.cGoto:
        {
            ConsumeNextToken();                             // 'goto'
            Identifier id = ReadExpectedIdentifier();       // id:label
            ReadExpectedToken(Token.Type.cSemi);            // ';'

            s = new GotoStatement(id);
        }
            break;

        // break
        // 'break' ';'
        case Token.Type.cBreak:
            ConsumeNextToken();
            ReadExpectedToken(Token.Type.cSemi);
            s = new BreakStatement();
            break;

        // Continue
        // 'continue' ';'
        case Token.Type.cContinue:
            ConsumeNextToken();
            ReadExpectedToken(Token.Type.cSemi);
            s = new ContinueStatement();
            break;
            
        // For-loop            
        case Token.Type.cFor:
            s = ParseForStatement();
            break;

        // For-each
        // -> 'foreach' '(' Type id 'in' exp:collection ')' stmt
        case Token.Type.cForEach:
            s = ParseForeachStatement();
            break;
            
        // BlockStatement - can be nested inside each other
        // start with a  '{', no terminating semicolon
        case Token.Type.cLCurly:
            {
                s = ParseStatementBlock();                
            }
            break;
            
        default:
            ThrowError(E_UnexpectedToken(t)); // unrecognized statement
            break;
        
        } // end switch
        
        // Must have come up with something
        Debug.Assert(s != null || v != null);    
    }

//-----------------------------------------------------------------------------
// Parse a Switch-case statement
// --> 'switch' '(' exp ')' '{' body '}'
// body -> section +
//-----------------------------------------------------------------------------
    protected Statement ParseSwitchStatement()
    {
        ReadExpectedToken(Token.Type.cSwitch);
        
        ReadExpectedToken(Token.Type.cLParen);
        Exp expTest = ParseExp();
        ReadExpectedToken(Token.Type.cRParen);
        
        ReadExpectedToken(Token.Type.cLCurly);
    
        // Parse sections
        ArrayList al = new ArrayList();
        
        Token t = m_lexer.PeekNextToken();
        while(t.TokenType != Token.Type.cRCurly)
        {
            SwitchSection section = ParseSwitchSection();    
            al.Add(section);        
            t = m_lexer.PeekNextToken();
        }
        ReadExpectedToken(Token.Type.cRCurly);
    
        
        SwitchSection [] sections = new SwitchSection[al.Count];
        for(int i = 0; i < sections.Length; i++)
            sections[i] = (SwitchSection) al[i];
            
        Statement s = new SwitchStatement(expTest, sections);
        
        return s;
    }

//-----------------------------------------------------------------------------    
// section -> ('case' exp ':')+ statement
//           |'default' ':' statement    
//-----------------------------------------------------------------------------
    protected SwitchSection ParseSwitchSection()
    {
        Token t = m_lexer.PeekNextToken();
        SwitchSection c;
        
        // Handle 'default' label
        if (t.TokenType == Token.Type.cDefault)
        {
            ConsumeNextToken();
            ReadExpectedToken(Token.Type.cColon);
            //Statement s = ParseStatement();
            Statement s = ParseStatementList();
            c = new SwitchSection(s);
        }
        
        else {
            // Handle 'case' label
            ArrayList al = new ArrayList();
            
            while (t.TokenType == Token.Type.cCase)
            {
                ConsumeNextToken();
                Exp e = ParseExp();
                ReadExpectedToken(Token.Type.cColon);
                al.Add(e);
                t = m_lexer.PeekNextToken();
            }
            
            //Statement stmt = ParseStatement();
            Statement s = ParseStatementList();
            
            Exp [] eList = new Exp[al.Count];
            for(int i = 0; i < eList.Length; i++)
                eList[i] = (Exp) al[i];
            
            c = new SwitchSection(eList, s);
        }
        return c;
    }

//-----------------------------------------------------------------------------
// For-each
// -> 'foreach' '(' Type id 'in' exp:collection ')' stmt
//-----------------------------------------------------------------------------
    protected Statement ParseForeachStatement()
    {
        ReadExpectedToken(Token.Type.cForEach);
        ReadExpectedToken(Token.Type.cLParen);
        
        TypeSig t = this.ParseTypeSig();
        
        Identifier id = ReadExpectedIdentifier();
        
        ReadExpectedToken(Token.Type.cIn);
        
        Exp e = ParseExp();
        
        ReadExpectedToken(Token.Type.cRParen);
        
        Statement stmt = this.ParseStatement();
        
        return new ForeachStatement(
            new LocalVarDecl(id, t),
            e,
            stmt);    
    }
    
//-----------------------------------------------------------------------------
// Parse a for-loop
// --> 'for' '(' StmtExp:init ';' Exp:test ';' StmtExp:next ')' stmt ';'
// --> 'for' '(' Type StmtExp:init '=' exp ';' Exp:test ';' StmtExp:next ')' stmt ';'
// 
// If we declare a var in the initializer, then we return a BlockStatement
// else we return a ForStatement
//-----------------------------------------------------------------------------
    protected Statement ParseForStatement()
    {
        ReadExpectedToken(Token.Type.cFor);
        ReadExpectedToken(Token.Type.cLParen);
        
        // Initializer - could either be a StmtExp or a var declaration
        // Note that the var is local to the body statement of the loop,
        // not to anyone outside the loop
        
        //StatementExp seInit = ParseStatementExp();        
        //ReadExpectedToken(Token.Type.cSemi);
        Statement sInit;
        LocalVarDecl vInit;
        ParseStatementOrLocal(out sInit, out vInit);
        
        ExpStatement es = sInit as ExpStatement;
        if (es == null)
            ThrowError(E_BadForLoopInit(sInit.Location));
            
        StatementExp seInit = es.StmtExp;
        
        
        // Test expression
        Exp eTest = ParseExp();
        ReadExpectedToken(Token.Type.cSemi);
        
        // Update expression
        StatementExp seNext = ParseStatementExp();
        ReadExpectedToken(Token.Type.cRParen);

        // Get body        
        Statement stmtBody = ParseStatement();
        
        
        Statement stmt = new ForStatement(seInit, eTest, seNext, stmtBody);
        
        // If we have a var decl, then put the for-loop in a block-statement
        // and add the var to that block statement
        if (vInit != null)
        {            
            stmt = new BlockStatement(new LocalVarDecl[] { vInit }, new Statement[] { stmt });
        }
        
        
        return stmt;
    }
    
    protected StatementExp ParseStatementExp()
    {
        Exp e = ParseExp();
        StatementExp se = e as StatementExp;
        if (se == null)
        {
            ThrowError(E_ExpectedStatementExp(e.Location));
        }
        return se;
    }

//-----------------------------------------------------------------------------
// Parse a catch handler
// CatchHandler-> 'catch' '(' TypeSig [id] ')' BlockStmt
// CatchHandler-> 'catch' BlockStmt                         // general catch
//-----------------------------------------------------------------------------
    protected CatchHandler ParseCatchHandler()
    {
        ReadExpectedToken(Token.Type.cCatch);
        
        Token t = m_lexer.PeekNextToken();
        
        TypeSig sig = null;
        Identifier id = null;
            
        if (t.TokenType == Token.Type.cLParen)
        {
            ReadExpectedToken(Token.Type.cLParen);
            
            sig = ParseTypeSig();
            
            t = m_lexer.PeekNextToken();
        
            if (t.TokenType == Token.Type.cId)
            {
                id = ReadExpectedIdentifier();            
            }
            ReadExpectedToken(Token.Type.cRParen);
        }
        
        BlockStatement s = ParseStatementBlock();
        
        return new CatchHandler(sig, id, s);
    }

//-----------------------------------------------------------------------------
// Parse a try-catch-finally statement
// -> 'try' block 'finally' block
// -> 'try' block <'catch' '(' TypeSig id ')' block>+ ['finally' block]?
//-----------------------------------------------------------------------------
    protected TryStatement ParseTryCatchFinallyStatement()
    {
        ReadExpectedToken(Token.Type.cTry);
        
        BlockStatement stmtTry = ParseStatementBlock();
        
        Token t2 = m_lexer.PeekNextToken();
        ArrayList a = new ArrayList();
        while(t2.TokenType == Token.Type.cCatch)
        {            
            CatchHandler c = ParseCatchHandler();
            a.Add(c);
            t2 = m_lexer.PeekNextToken();
        }
        
        CatchHandler [] arCatch = new CatchHandler[a.Count];
        for(int i = 0; i < a.Count; i++) 
            arCatch[i] = (CatchHandler) a[i];
        
        BlockStatement stmtFinally = null;
        if (t2.TokenType == Token.Type.cFinally)
        {
            ConsumeNextToken();
            stmtFinally = ParseStatementBlock();            
        }
        
        return new TryStatement(stmtTry, arCatch, stmtFinally);
    }
    
//-----------------------------------------------------------------------------
// Parse a statement block
// block -> '{' statement_list '}'
//-----------------------------------------------------------------------------
    protected BlockStatement ParseStatementBlock()
    {
        ReadExpectedToken(Token.Type.cLCurly);
        
        BlockStatement s = ParseStatementList();
        
        ReadExpectedToken(Token.Type.cRCurly);
        
        return s;
    }
//-----------------------------------------------------------------------------
// list -> stmt stmt ...
//-----------------------------------------------------------------------------
    protected BlockStatement ParseStatementList()
    {   
        ArrayList alLocals = new ArrayList();
        ArrayList alStatement = new ArrayList();
        
    // Statement block begins with a '{'
        //ReadExpectedToken(Token.Type.cLCurly);
        
    // Read list of statements and local decls    
        Token t = m_lexer.PeekNextToken();
        
        
        // Keep reading statements until we find a closing '}'
        //while (t.TokenType != Token.Type.cRCurly)
        while (
            (t.TokenType != Token.Type.cRCurly) && 
            (t.TokenType != Token.Type.cCase) && 
            (t.TokenType != Token.Type.cDefault))
        {                        
            Statement s;
            LocalVarDecl v;
            ParseStatementOrLocal(out s, out v);
            
            if (s != null)
                alStatement.Add(s);            
            if (v != null)
                alLocals.Add(v);
            
            t = m_lexer.PeekNextToken();
        } // end while
                
        //ReadExpectedToken(Token.Type.cRCurly);
                   
        LocalVarDecl [] arLocals = LocalVarDeclFromArray(alLocals);
        Statement[] arStmt = StatementFromArray(alStatement);
                
        BlockStatement block = new BlockStatement(arLocals, arStmt);
        return block;
    }
#endregion Parse Statements

#region Parse Expressions    
   
//-----------------------------------------------------------------------------
// Parse an arbitrarily complex expression
// Use recursive descent
// We have to following precedence groups (from lowest to highest)
// 1) &&, ||
// 2) ==, !=, <=, >=, <, >
// 3) -, +
// 4) *, /, %
// 5) !, - (unary)
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Here's how we can parse expressions using recursive descent.
// First, think about the grammar: (where '+' is any op)
// This grammar enforces precedence & is unambiguous.
// Exp -> E1 '+' Exp | E1
// E1 -> E2 '+' E1 | E2
// E2 -> E3 '+' E2 | E3
// En -> En+1 '+' En | En+1
// En+1 -> Eatom | (Exp)
// 
// Where Exp is the term for an entire complex expression,
// and Eatom is the term for the smallest expression containing no 
// subexpressions. (atoms include literals like ints & strings, as well
// as ObjExp like identifiers & method calls)
//
// The trick is that for every rule, we can left-factor so that:
// E1 -> E2 '+' E1 | E2
// Becomes: 
// E1 -> E2 ('+' E1)?
// Then to parse this, we just always read the E2, and then we check the next
// token. If it's an operator we're looking for, we consume it and then read
// another E1.
//-----------------------------------------------------------------------------
    
    // Wrapper to ensure we have line number info
    protected Exp ParseExp()
    {
        FileRange f = BeginRange();
        Exp e = ParseExp_Worker();
        e.SetLocation(EndRange(f));
        
        return e;
    }
    
    // Group 0) assignment (=, +=, etc) & conditional ?:
    protected Exp ParseExp_Worker()
    {        
        Exp T = ParseExp1();
        
        Token t = m_lexer.PeekNextToken();
        
        // Assignment
        if (t.TokenType == Token.Type.cAssign)
        {
            ConsumeNextToken();
            Exp E = ParseExp();
            
            AssignStmtExp s = new AssignStmtExp(T, E);
            return s;
        } 
        
        // ?: operator --> E ? E : E
        if (t.TokenType == Token.Type.cQuestion)
        {
            ConsumeNextToken();   
            Exp eTrue = ParseExp();
            ReadExpectedToken(Token.Type.cColon);
            Exp eFalse = ParseExp();
            
            return new IfExp(T, eTrue, eFalse);
        } 
        
        else {            
            // Look for +=,-=,*=,/=,%=
            AST.BinaryExp.BinaryOp op = BinaryExp.BinaryOp.cEqu; // set to dummy
            switch(t.TokenType)
            {
            // Arithmetic
            case Token.Type.cPlusEqual:
                op = BinaryExp.BinaryOp.cAdd;
                break;
            case Token.Type.cMinusEqual:
                op = BinaryExp.BinaryOp.cSub;
                break;
            case Token.Type.cMulEqual:
                op = BinaryExp.BinaryOp.cMul;
                break;
            case Token.Type.cDivEqual:
                op = BinaryExp.BinaryOp.cDiv;
                break;
            case Token.Type.cModEqual:
                op = BinaryExp.BinaryOp.cMod;
                break;          
                
            // Bitwise
            case Token.Type.cBitwiseAndEqual:
                op = BinaryExp.BinaryOp.cBitwiseAnd;
                break;
                
            case Token.Type.cBitwiseOrEqual:
                op = BinaryExp.BinaryOp.cBitwiseOr;
                break;
                
            case Token.Type.cBitwiseXorEqual:
                op = BinaryExp.BinaryOp.cBitwiseXor;
                break;
                
            // Shift
            case Token.Type.cShiftLeftEqual:
                op = BinaryExp.BinaryOp.cShiftLeft;
                break;
                
            case Token.Type.cShiftRightEqual:
                op = BinaryExp.BinaryOp.cShiftRight;
                break;                
            }
            
            if (op != BinaryExp.BinaryOp.cEqu)
            {
                ConsumeNextToken();
                Exp E = ParseExp();                
                //return new OpEqualStmtExp(T, E, op);
                // Transform 'a X= b' -> 'a = (a X b)'
                // This makes things much more consistent.
                AST.AssignStmtExp assign = new AssignStmtExp(
                    T,
                    new BinaryExp(
                        T,
                        E,
                        op)
                    );
                return assign;
            }
        }
        
        Debug.Assert(T != null);
        return T;
    }    
    
    // Helper for parsing it as left-associative. We get in our parent's left
    // exp, and we return the left-associative parse tree.
    private Exp ParseExp1_rest(Exp expParentLeft)
    {   
        // E' -> && T E'
        //       || T E'
        //       {}    
                
        Token t = m_lexer.PeekNextToken();
        if (t.TokenType == Token.Type.cAnd || t.TokenType == Token.Type.cOr)
        {
            ConsumeNextToken();            
                        
            BinaryExp.BinaryOp op = ConvertToBinaryOp(t);
            Exp expRight = ParseExp2();            

            Exp expChildLeft = new BinaryExp(expParentLeft, expRight, op);
            return ParseExp1_rest(expChildLeft);                        
        } 
        else 
        {            
            return expParentLeft;
        }
    }
    
    // 1) &&, ||
    protected Exp ParseExp1()
    {
        // E -> T E'
        // E' -> && T E'
        //       || T E'
        //       {}
        Exp eLeft = ParseExp2();
        Exp e = ParseExp1_rest(eLeft);
        
        // E-> T++ | T--
        // Post Inc & Dec, x++, x--
        Token t = m_lexer.PeekNextToken();
        if (t.TokenType == Token.Type.cPlusPlus)
        {
            ConsumeNextToken();
            return new PrePostIncDecStmtExp(e, false, true);
        } 
        else if (t.TokenType == Token.Type.cMinusMinus)
        {
            ConsumeNextToken();
            return new PrePostIncDecStmtExp(e, false, false);
        }
        
        Debug.Assert(e != null);
        return e;
    }
   
    // Bitwise |, ^, &
    protected Exp ParseExp2()
    {
        Exp T = ParseExp3();
        
        Token t = m_lexer.PeekNextToken();
        if ((t.TokenType == Token.Type.cBitwiseOr)||
            (t.TokenType == Token.Type.cBitwiseXor) ||
            (t.TokenType == Token.Type.cBitwiseAnd))
        {
            ConsumeNextToken();
            Exp E = ParseExp2(); // here's the recursion
            
            BinaryExp.BinaryOp op = ConvertToBinaryOp(t);                        
            return new BinaryExp(T, E, op);
        }        
        
        Debug.Assert(T != null);
        return T;    
    }
    
    // Group 2) ==, !=, <=, >=, <, >
    protected Exp ParseExp3()
    {
        // E -> T | T + E        
        Exp T = ParseExp4();
        
        Token t = m_lexer.PeekNextToken();
        if ((t.TokenType == Token.Type.cEqu) ||
            (t.TokenType == Token.Type.cNeq) ||
            (t.TokenType == Token.Type.cLT) ||
            (t.TokenType == Token.Type.cLTE) ||
            (t.TokenType == Token.Type.cGT) ||
            (t.TokenType == Token.Type.cGTE))            
        {
            ConsumeNextToken();
            Exp E = ParseExp3(); // here's the recursion
            
            BinaryExp.BinaryOp op = ConvertToBinaryOp(t);                        
            return new BinaryExp(T, E, op);
        }        
        
        // 'Is' and 'As' operators
        if (t.TokenType == Token.Type.cIs)
        {
            ConsumeNextToken();
            TypeSig type = ParseTypeSig();

            return new IsExp(T, type);
        }
        // Check for 'as' operator
        // E -> E 'as' typesig
        
        if (t.TokenType == Token.Type.cAs)
        {
            ConsumeNextToken();

            TypeSig type = ParseTypeSig();
            return new AsExp(type, T);            
        }
        
        Debug.Assert(T != null);
        return T;
    }
    
    // Shifts
    protected Exp ParseExp4()
    {
        // E -> T | T << E
        Exp T = ParseExp5();
        
        Token t = m_lexer.PeekNextToken();
        if (t.TokenType == Token.Type.cShiftLeft || t.TokenType == Token.Type.cShiftRight)
        {
            ConsumeNextToken();
            Exp E = ParseExp4(); // here's the recursion
            
            BinaryExp.BinaryOp op = ConvertToBinaryOp(t);
            return new BinaryExp(T, E, op);
        }        
        
        Debug.Assert(T != null);
        return T;
    
    }
    
    
    // Helper for parsing it as left-associative. We get in our parent's left
    // exp, and we return the left-associative parse tree.
    private Exp ParseExp5_rest(Exp expParentLeft)
    {   
        // E' -> + T E'
        //       - T E'
        //       {}    
                
        Token t = m_lexer.PeekNextToken();
        if (t.TokenType == Token.Type.cPlus || t.TokenType == Token.Type.cMinus)
        {
            ConsumeNextToken();            
                        
            BinaryExp.BinaryOp op = ConvertToBinaryOp(t);
            Exp expRight = ParseExp6();            

            Exp expChildLeft = new BinaryExp(expParentLeft, expRight, op);
            return ParseExp5_rest(expChildLeft);                        
        } else {            
            return expParentLeft;
        }
    }
    
    // 3) -, +
    protected Exp ParseExp5()
    {
        // E -> T E'
        // E' -> + T E'
        //       - T E'
        //       {}    
#if true                       
        Exp eLeft = ParseExp6();
        Exp e = ParseExp5_rest(eLeft);
        
        return e;
        
#else   
        // Error - this is Right associative. We need left associtive
        // E -> T + E | T            
        Exp T = ParseExp6();
        
        Token t = m_lexer.PeekNextToken();
        if (t.TokenType == Token.Type.cPlus || t.TokenType == Token.Type.cMinus)
        {
            ConsumeNextToken();
            Exp E = ParseExp5(); // here's the recursion
            
            BinaryExp.BinaryOp op = ConvertToBinaryOp(t);                        
            return new BinaryExp(T, E, op);
        }        
        
        Debug.Assert(T != null);
        return T;
#endif        
    }
    
    // 4) *, /, %
    protected Exp ParseExp6()
    {
        // E -> T | T + E        
        Exp T = ParseExp7();
        
        Token t = m_lexer.PeekNextToken();
        if ((t.TokenType == Token.Type.cMul) ||
            (t.TokenType == Token.Type.cDiv) ||
            (t.TokenType == Token.Type.cMod))
        {
            ConsumeNextToken();
            Exp E = ParseExp6(); // here's the recursion
            
            BinaryExp.BinaryOp op = ConvertToBinaryOp(t);                        
            return new BinaryExp(T, E, op);
        }        
        
        Debug.Assert(T != null);
        return T;
    }
     
    
    private Exp ParseExp7()
    {
        return ParseUnaryExp();            
    }
    
//-----------------------------------------------------------------------------
// Unary expressions
// E->! E
// E->- E
// E-> -- E
// E-> ++ E
// E-> typeof(type)
// E->EAtom;
//-----------------------------------------------------------------------------    
    private Exp ParseUnaryExp()
    {
        Token t= m_lexer.PeekNextToken();
        if (t.TokenType == Token.Type.cNot)
        {
            ConsumeNextToken();
            Exp e = ParseUnaryExp();
            return new UnaryExp(e, UnaryExp.UnaryOp.cNot);     
        }
        
        if (t.TokenType == Token.Type.cMinus)
        {
            ConsumeNextToken();
            Exp e = ParseUnaryExp();
            return new UnaryExp(e, UnaryExp.UnaryOp.cNegate);
        }
        
        // Check pre inc & dec
        if (t.TokenType == Token.Type.cPlusPlus)
        {
            ConsumeNextToken();
            Exp e = ParseExp();
            return new PrePostIncDecStmtExp(e, true, true);
        }
        
        if (t.TokenType == Token.Type.cMinusMinus)
        {
            ConsumeNextToken();
            Exp e = ParseExp();
            return new PrePostIncDecStmtExp(e, true, false);
        }
        
        if (t.TokenType == Token.Type.cTypeOf)
        {
            ConsumeNextToken();
            ReadExpectedToken(Token.Type.cLParen);
            TypeSig sig = ParseTypeSig();
            ReadExpectedToken(Token.Type.cRParen);
            
            return new TypeOfExp(sig);
        
        }
        
        //return ParseExpAtom();    
        return ParsePrimaryExp();
    }

//-----------------------------------------------------------------------------
// E -> E . i
// E -> E . i (...)
// E -> E [ E]
//-----------------------------------------------------------------------------
    protected Exp ParsePrimaryExp()
    {
    
        Exp eFinal = ParseExpAtom();
        
        // Now, since ObjExp are left-linear, we can actually parse them recursively
        // We parsed the base case, so we just keep iterating through deciding
        // which rule to apply. eFinal contains the root of the ast we're building
        
        Token t;
        while(true)
        {
            t = m_lexer.PeekNextToken();
            
            // If next char is '.', then we're either doing:
            // E -> E . i
            // E -> E . i (...)
            if (t.TokenType == Token.Type.cDot)
            {
                ConsumeNextToken(); // eat the dot
                
                Identifier stId = ReadExpectedIdentifier();
                Token t2 = m_lexer.PeekNextToken();
                
                // MethodCall - if next character is a '('
                // E -> E . i (...)
                if (t2.TokenType == Token.Type.cLParen)
                {
                    ArgExp [] arParams = ParseArgList();
                    eFinal = new MethodCallExp(eFinal, stId, arParams);                    
                    continue;
                } 
                // Dot operator - for all other cases
                // E -> E . i
                else 
                { 
                    eFinal = new DotObjExp(eFinal, stId);
                    continue;
                }            
            }
            
            // If next char is a '[', then this is an array access
            // E -> E [ E ]
            else if (t.TokenType == Token.Type.cLSquare) 
            {
                ConsumeNextToken();                
                Exp eIdx = ParseExp();                
                ReadExpectedToken(Token.Type.cRSquare);
                
                eFinal = new ArrayAccessExp(eFinal, eIdx);
                continue;
            } 
            
            // If we got to here, then we're done so break out of loop
            break;
            
        } // end while
    
        
        // @hack
        // Since expressions can be types (ie, that's how we parse a TypeCast)
        // Check if this is an array type
        if (t.TokenType == Token.Type.cLRSquare)
        {
            NonRefTypeSig sigElemType = new SimpleTypeSig(eFinal);
            TypeSig tSig = ParseOptionalArrayDecl(sigElemType);
            return new TempTypeExp(tSig);
        }
        
        return eFinal;
    }
    
//-----------------------------------------------------------------------------
// Parse a single atom of an expression
// Atom expressions are the basic building blocks of expressions and
// don't contain any operators in them.
// Atoms are combined together to form more complex expressions.
// One caveat:
// Type casting & parenthesis look really similar...
//-----------------------------------------------------------------------------
    protected Exp ParseExpAtom()
    {
        // Parse a single term
        Token t = m_lexer.PeekNextToken();
                        
        // Either expression in parenthesis
        // could be a typecast
        if (t.TokenType == Token.Type.cLParen)
        {
            ConsumeNextToken();
            Exp e = ParseExp();
            ReadExpectedToken(Token.Type.cRParen);

        // Typecast if the next token is in the first set of an expression
        // --> (Type) exp
            t = m_lexer.PeekNextToken();
            if (IsStartOfExp(t))
            {
                TypeSig tSig = ConvertExpToType(e);                
                Exp eSource = this.ParsePrimaryExp();

                return new CastObjExp(tSig, eSource);
            }

            return e;        
        }
        
        // Check for 'new'
        if (t.TokenType == Token.Type.cNew)
            return ParseNewExp();
        
        
        // Check for identifier or methodcall
        // E -> i
        // E -> i ( ... )
        if (t.TokenType == Token.Type.cId)
        {
            Identifier id = ReadExpectedIdentifier();
            Token t2 = m_lexer.PeekNextToken();
            
            // if next char after id is a '(', then this is a method call
            // with an implied 'this' pointer on the left side
            if (t2.TokenType == Token.Type.cLParen)
            {
                ArgExp [] arParams = ParseArgList();
                MethodCallExp m = new MethodCallExp(null, id, arParams);
                return m;
            } 
            else 
            {
                return new SimpleObjExp(id);
            }
        } 
        
        // Check for literals
        if (t.TokenType == Token.Type.cNull)
        {
            ConsumeNextToken();
            return new NullExp(t.Location);
        }
        
        if (t.TokenType == Token.Type.cString)
        {
            ConsumeNextToken();
            return new StringExp(t.Text, t.Location);
        }
        
        if (t.TokenType == Token.Type.cInt)
        {
            ConsumeNextToken();            
            return new IntExp(t.IntValue, t.Location);
        }
        
        if (t.TokenType == Token.Type.cChar)
        {
            ConsumeNextToken();
            return new CharExp(t.CharValue, t.Location);
        }
        
        if (t.TokenType == Token.Type.cBool)
        {
            ConsumeNextToken();
            return new BoolExp(t.BoolValue, t.Location);
        }
                
        ThrowError(E_UnexpectedToken(t));
        return null;
    }
    
//-----------------------------------------------------------------------------
// Parse a new Expression (either array or non-array)
// -> 'new' id_list '(' exp, exp ... ')'
//
// -> 'new' id_list '[x]' '[]'* array_init?
// -> 'new' id_list '[]'* array_init
// 
// array_init-> '{' exp, exp, exp, ...'}'
//-----------------------------------------------------------------------------
    protected Exp ParseNewExp()
    {                   
        ReadExpectedToken(Token.Type.cNew);
                   
        // A typesig would allow [] to be part of the type
        // An id_list doesn't.
        Exp oe = ParseDottedIdList();
        SimpleTypeSig type = new SimpleTypeSig(oe);
        
        Token t2 = m_lexer.PeekNextToken();
        
        // If next is a '(', then this is a ctor call (and not an array)
        if (t2.TokenType == Token.Type.cLParen)
        {                
            Exp [] eList = ParseExpList();
            Exp e = new NewObjExp(type, eList);
            return e;
        } 
        
        // Array case
        if (t2.TokenType == Token.Type.cLRSquare || t2.TokenType == Token.Type.cLSquare)
        {            
                            
            ArrayTypeSig typeArray = null;
            ArrayInitializer init = null;
                            
            // dynamic size explicitly in rank specifier
            // -> 'new' id_list '[x]' '[]'* array_init?                
            if (t2.TokenType == Token.Type.cLSquare)
            {                    
                Exp [] eList = ParseExpList(Token.Type.cLSquare, Token.Type.cRSquare); // includes [ and ]
                                
                int dim = eList.Length;
                                            
                NonRefTypeSig s = ParseOptionalArrayDecl(type);
                typeArray = new ArrayTypeSig(s, dim);
                
                if (m_lexer.PeekNextToken().TokenType == Token.Type.cLCurly)
                {
                    init = this.ParseArrayInitList();
                }
                
                Exp e = new NewArrayObjExp(typeArray,eList,init);
                return e;
            }
            
            // static size implicitly from array_initializer
            // -> 'new' id_list '[]'* array_init
            else if (t2.TokenType == Token.Type.cLRSquare)
            {
                NonRefTypeSig t = ParseOptionalArrayDecl(type);
                typeArray = t.AsArraySig;
                init = this.ParseArrayInitList();
                
                Exp e = new NewArrayObjExp(typeArray,init);
                return e;
            }
        } 
        
        
        ThrowError(E_UnexpectedToken(t2));
        return null;
    }    
    
//-----------------------------------------------------------------------------
// Parse an expected array initializer list
//
// ** rules **
// ArrayInit -> '{' '}' | '{' vl '}'
// vl -> v | v vl
// v->exp | ArrayInit
//-----------------------------------------------------------------------------
    ArrayInitializer ParseArrayInitList()
    {
        ReadExpectedToken(Token.Type.cLCurly);
        
        ArrayList al = new ArrayList();
        
        Token t = m_lexer.PeekNextToken();
            
        Node n = null;                
        // List of either nested lists or expressions                
        while(t.TokenType != Token.Type.cRCurly)
        {
            if (n != null)
                ReadExpectedToken(Token.Type.cComma);

#if false            
// @todo - Nested array initializers are only used for multi-dimensional arrays.
            if (t.TokenType == Token.Type.cLCurly)
            {
                n = ParseArrayInitList();
            } else 
#endif            
            {
                n = ParseExp();
            }
        
            al.Add(n);
        
            t = m_lexer.PeekNextToken();
        }
        
        ReadExpectedToken(Token.Type.cRCurly);
        
        return new ArrayInitializer(al);
    }
    
    
    //-----------------------------------------------------------------------------
    // Helper for parsing. Convert a token to an operator
    //-----------------------------------------------------------------------------
    public AST.BinaryExp.BinaryOp ConvertToBinaryOp(Token t)
    {
        switch(t.TokenType)
        {
            case Token.Type.cAnd:      return BinaryExp.BinaryOp.cAnd;
            case Token.Type.cOr:       return BinaryExp.BinaryOp.cOr;
        
            case Token.Type.cPlus:     return BinaryExp.BinaryOp.cAdd;
            case Token.Type.cMinus:    return BinaryExp.BinaryOp.cSub;
            case Token.Type.cMul:      return BinaryExp.BinaryOp.cMul;
            case Token.Type.cDiv:      return BinaryExp.BinaryOp.cDiv;
            case Token.Type.cMod:      return BinaryExp.BinaryOp.cMod;
                        
            case Token.Type.cEqu:      return BinaryExp.BinaryOp.cEqu;
            case Token.Type.cNeq:      return BinaryExp.BinaryOp.cNeq;
            case Token.Type.cGTE:      return BinaryExp.BinaryOp.cGE;
            case Token.Type.cLTE:      return BinaryExp.BinaryOp.cLE;
            case Token.Type.cGT:       return BinaryExp.BinaryOp.cGT;
            case Token.Type.cLT:       return BinaryExp.BinaryOp.cLT;
        
            case Token.Type.cBitwiseAnd:    return BinaryExp.BinaryOp.cBitwiseAnd;
            case Token.Type.cBitwiseOr:     return BinaryExp.BinaryOp.cBitwiseOr;
            case Token.Type.cBitwiseXor:    return BinaryExp.BinaryOp.cBitwiseXor;
        
            case Token.Type.cShiftLeft:     return BinaryExp.BinaryOp.cShiftLeft;
            case Token.Type.cShiftRight:    return BinaryExp.BinaryOp.cShiftRight;
        
            default:
                ThrowError(E_UnexpectedToken(t));
                break;                
        }
        
        Debug.Assert(false);
        return BinaryExp.BinaryOp.cAdd; // dummy
    }
    
#endregion Parse Expressions

#region Misc Helpers
//-----------------------------------------------------------------------------
// Parse a list of identifiers separated by dots
// 
// ** rules **
// id ( '.' id )*
//-----------------------------------------------------------------------------
    protected Exp ParseDottedIdList()
    {
        Identifier stId = ReadExpectedIdentifier();        
        Exp o = new SimpleObjExp(stId);
        
        Token t = m_lexer.PeekNextToken();        
        while (t.TokenType == Token.Type.cDot)
        {
            ConsumeNextToken();
            
            stId = ReadExpectedIdentifier();
            o = new DotObjExp(o, stId);
                        
            t = m_lexer.PeekNextToken();
        }  
        
        return o;       
    
    }


//-----------------------------------------------------------------------------
// Parse a parameter list (including opening & closing parens)
// paramlist-> param ',' param ',' ...
// param-> (''|'ref'|'out') exp
//-----------------------------------------------------------------------------
    protected ArgExp[] ParseArgList()
    {
        ArrayList al = new ArrayList();
        
        ReadExpectedToken(Token.Type.cLParen);
        
        // Keep parsing expressions until we hit the closing ')'
        Token t = m_lexer.PeekNextToken();
        
        if (t.TokenType == Token.Type.cRParen)
            ConsumeNextToken();
            
        while(t.TokenType != Token.Type.cRParen) 
        {
            t = m_lexer.PeekNextToken();
            // Parse an expression and add it to the list
            EArgFlow eFlow = EArgFlow.cIn;
            if (t.TokenType == Token.Type.cOut)
                eFlow = EArgFlow.cOut;
            if (t.TokenType == Token.Type.cRef)
                eFlow = EArgFlow.cRef;
            
            if (eFlow != EArgFlow.cIn)
            {
                ConsumeNextToken();                
            }
            
            Exp e = ParseExp();
            
            e = new ArgExp(eFlow, e);
            al.Add(e);
            
            // Skip past the comma (or read the closing ')' )            
            t = m_lexer.GetNextToken();
                        
            CheckError_UnexpectedToken(t, new Token.Type [] { Token.Type.cComma, Token.Type.cRParen } );
        }
        
        
        // Convert to real array
        ArgExp[] a = new ArgExp[al.Count];
        for(int i = 0; i < al.Count; i++)
            a[i] = (ArgExp) al[i];
        return a;   
    }

//-----------------------------------------------------------------------------
// Parse an expression list (including opening & closing parens)
// Allows an empty list
//
// ** rules **
// explist -> '(' exp ',' exp ',' ... ')'
//-----------------------------------------------------------------------------
    protected Exp [] ParseExpList()
    {
        return ParseExpList(Token.Type.cLParen, Token.Type.cRParen);
    }
    
    // General expression list parser. Allows arbitrary Open & Close
    // tokens.
    protected Exp [] ParseExpList(Token.Type tOpen, Token.Type tClose)
    {        
        ArrayList al = new ArrayList();
        
        ReadExpectedToken(tOpen);
        
        // Keep parsing expressions until we hit the closing ')'
        Token t = m_lexer.PeekNextToken();
        
        if (t.TokenType == tClose)
            ConsumeNextToken();
            
        while(t.TokenType != tClose) 
        {
            // Parse an expression and add it to the list
            Exp e = ParseExp();
            al.Add(e);
            
            // Skip past the comma (or read the closing ')' )            
            t = m_lexer.GetNextToken();
                        
            CheckError_UnexpectedToken(t, new Token.Type [] { Token.Type.cComma, tClose } );
        }
        
        
        // Convert to real array
        Exp[] a = new Exp[al.Count];
        for(int i = 0; i < al.Count; i++)
            a[i] = (Exp) al[i];
        return a;                
    }

    //-----------------------------------------------------------------------------
    // return if a token is the start of an expression
    //-----------------------------------------------------------------------------
    protected bool IsStartOfExp(Token t)
    {
        switch(t.TokenType)
        {
            case Token.Type.cId: return true;
            case Token.Type.cInt: return true;
            case Token.Type.cString: return true;
            case Token.Type.cChar: return true;
            
            case Token.Type.cLParen: return true;
            case Token.Type.cPlusPlus: return true;
            case Token.Type.cMinusMinus: return true;
            
        }
        return false;
    }
  
#endregion Misc Helpers 

#region Parse Types
//-----------------------------------------------------------------------------
// Parse a Type Sig
//
// ** rules **
// TypeSig -> id ('.' id)* '[ ','* ]'*
//-----------------------------------------------------------------------------
    protected NonRefTypeSig ParseTypeSig()
    {
    // Currently, we implement this by parsing ObjExpressions. That's easier
    // for us, but may let us parse illegal things. That's ok. The TypeSig
    // container class along with semantic checking will still give us the
    // expected error control.
        NonRefTypeSig sig = null;
    
        Identifier stId = ReadExpectedIdentifier();        
        Exp o = new SimpleObjExp(stId);
        
        Token t = m_lexer.PeekNextToken();        
        while (t.TokenType == Token.Type.cDot)
        {
            ConsumeNextToken();
        
            stId = ReadExpectedIdentifier();
            o = new DotObjExp(o, stId);
                        
            t = m_lexer.PeekNextToken();
        }  
        
        sig = new SimpleTypeSig(o);
        
    // Check for arrays
        while (t.TokenType == Token.Type.cLRSquare)
        {   
            sig = new ArrayTypeSig(sig, 1);
            
            ConsumeNextToken();            
            t = m_lexer.PeekNextToken();
        }          
                
        return sig;
    }
    
    //-----------------------------------------------------------------------------
    // Convert an expression into a type.
    // Either e is a TypeExp,
    // or e is an i.i.i... (DottedObjExp & SimpleObjExp)
    // Throw if we can't convert
    //-----------------------------------------------------------------------------
    protected TypeSig ConvertExpToType(Exp e)
    {
        if (e is TempTypeExp)
        {
            TempTypeExp t = e as TempTypeExp;
            return t.TypeSigRec;
        }

        // Verify this is a dotted expression list        
        Exp o = e;
        while(o is DotObjExp)
        {
            o = (o as DotObjExp).LeftExp;
        }
        if (o is SimpleObjExp)
        {
            Exp o2 = e;
            return new SimpleTypeSig(o2);
        }

        // Error
        Debug.Assert(false); // @legit - expected type
        return null;
    }    
    
    //-----------------------------------------------------------------------------
    // Parse a comma separated list of identifiers
    // expect at least 1 entry
    //-----------------------------------------------------------------------------
    protected TypeSig[] ParseIdNameList()
    {
        ArrayList a = new ArrayList();

        Token t;

        while(true)
        {
            Exp o = ParseDottedIdList();
            a.Add(o);

            t = m_lexer.PeekNextToken();
            if (t.TokenType == Token.Type.cComma)
            {
                ConsumeNextToken();
                continue;
            }
            break;
        }

        TypeSig[] olist = new TypeSig[a.Count];
        for(int i = 0; i < a.Count; i++)        
        {
            olist[i] = new SimpleTypeSig((Exp) a[i]);
        }
        
        return olist;
    }    
    
#endregion

#region Parse Modifiers
//-----------------------------------------------------------------------------
// Parse Member attributes and return the bit flag
//
// ** rules **
// MemberAttr -> <any subset of {public, static, etc } >
//-----------------------------------------------------------------------------
    protected Modifiers ParseModifiers()
    {        
        AST.Modifiers mods = new Modifiers();
                
        while(true) {
            Token t = m_lexer.PeekNextToken();                        
            switch(t.TokenType)
            {   
                case Token.Type.cAttrPublic:                     
                     mods.SetPublic(); break;
                
                case Token.Type.cAttrProtected:                    
                    mods.SetProtected(); break;
                    
                case Token.Type.cAttrPrivate:                    
                    mods.SetPrivate(); break;                    

                case Token.Type.cAttrStatic:                                
                    mods.SetStatic(); break;

                case Token.Type.cAttrAbstract:                                
                    mods.SetAbstract(); break;

                case Token.Type.cAttrVirtual:                                
                    mods.SetVirtual(); break;
                    
                case Token.Type.cAttrOverride:
                    mods.SetOverride(); break;
                    
                case Token.Type.cAttrInternal:                    
                    mods.SetInternal(); break;
            
                case Token.Type.cAttrReadOnly:                    
                    mods.SetReadOnly(); break;
            
                case Token.Type.cNew:
                    mods.SetNew(); break;                    
                                    
                case Token.Type.cAttrSealed:                    
                    mods.SetSealed(); break;

            // Return once we encounter something that's not a modifier
                default:
                {
                    return mods;
                }
                    
            }
                
            ConsumeNextToken();        
        }
        
        // We exit once we find a token that's not a modifier (or we find a duplicate modifier)
    }
#endregion Parse Modifiers
  
} // end class Parser

} // end namespace ManualParser