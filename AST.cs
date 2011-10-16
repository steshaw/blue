//-----------------------------------------------------------------------------
// File: AST.cs
// The abstract syntax tree
// 
// All AST nodes derive from class Node for utility purposes (dumping, 
// source file mapping, etc).
//-----------------------------------------------------------------------------

#region Comment on AST node hierarchy
//-----------------------------------------------------------------------------
// Nodes fall into 3 categories: 
// 1) Decl - declare something
// 2) Statement - statements in program
// 3) Exp - part of an expression
//
// Each node may reference its corresponding derived class from SymEntry
// The AST is pure and does not contain any resolved information besides
// the symbol table entry. All resolved data is stored on the symbol entry.
// The parser creates the AST with strings for the identifiers.
// Then the semantic checking populates the SymbolTable and does a lookup
// on the string name and sets the SymEntry fields.
//  
// The node organization is:
// + Node - root class
//    + ProgramDecl - declare a program
//    + NamespaceDecl - declare a namespace
//    + ClassDecl - declare a class
//    + MemberDecl - member within a class
//       + MethodDecl
//       + FieldDecl 
//       + VarDecl
//           + LocalVarDecl - declare a local variable
//           + ParamVarDecl - declare a parameter
//    + PropertyDecl
//
//    + Statement
//       + ReturnStatement
//       + AssignStatement
//       + IfStatement
//       + LoopStatement
//          + WhileStatement
//          + DoStatement
//
//    + Exp - nodes that form expressions
//       + LiteralExp
//           + IntExp
//           + BoolExp
//           + StringExp
//           + CharExp
//           + DoubleExp
//       + BinaryExp
//       + UnaryExp       
//       + ObjExp
//          + SimpleObjExp - apply single identifier to ObjExp
//          + DotObjExp - single identifier
//          + MethodCallExp - evaluates to a method call
//          + NewObjExp - create new objects
//          + CastObjExp - cast an expression into an object
//
//    + TypeSig - store a type
//-----------------------------------------------------------------------------
#endregion

using System;
using System.Collections;
using System.Diagnostics;
using System.Xml;

using SymbolEngine;
using Blue.Public;



//-----------------------------------------------------------------------------
/// <summary>
/// <c>Identifier</c> is a helper class to associate a string with a <c>FileRange</c>.
/// All AST references to a string-data should refer to an Identifier.
/// </summary>
//-----------------------------------------------------------------------------
public class Identifier
{
    public Identifier(string stText)
        : this(stText, null)
    {    }
    public Identifier(string stText, FileRange location)
    {
        m_stText = stText;
        m_filerange = location;
    }
    
    protected string m_stText;
    protected FileRange m_filerange;
    
    public string Text
    {
        get { return m_stText; }
    }
    
    public FileRange Location
    {
        get { return m_filerange; }
    }
}

//-----------------------------------------------------------------------------
// The Abstract Syntax Tree
//-----------------------------------------------------------------------------
namespace AST
{
#region Node, the base class for all AST nodes
//-----------------------------------------------------------------------------
// Base AST node serves for utility purposes
//-----------------------------------------------------------------------------

/// <summary>
/// <c>Node</c> is the base class for all nodes in the Abstract Syntax tree.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>It is associated with a <c>FileRange</c> object to guarantee that all nodes 
/// can provide line number information. </item>
/// <item>It also provides some standard utility functions to serialize a subtree to
/// XML or strings.</item>
/// </list>
/// Nodes are split into the following major categories: 
/// Declarations, Members, Statements, Expressions
/// </remarks>
public abstract class Node
{    
#region Checks

// Utility to Dump to XML file
    public static void DumpTree(Node root, XmlWriter o)
    {
        o.WriteStartDocument();
        o.WriteStartElement("AST");
        root.Dump(o);
        o.WriteEndElement(); // AST
        o.WriteEndDocument();
        o.Close();
    }

// Dump as xml. XmlWriter must be opended & document must be started
// Will not close document.
    public abstract void Dump(XmlWriter o);
   
    // Debugging function only:
    // Debugging check to be done _after_ symbol resolution
    // That way, we have a symbol table that we can verify everything against.
    // Assert everything that we can possibly think of so that CodeGen
    // has a well-resolved tree.
    // If any of the asserts do fire, that means symbolic resolution is
    // making a mistake and should be fixed.
    public abstract  void DebugCheck(ISemanticResolver s);
    
    // These flags are used during the DebugCheck() to allow features
    // that haven't been implemented to pass.
    // remove these flags (set to false) as more stuff gets resolved
    public readonly static bool m_DbgAllowNoCLRType = false;
    
    
    // Debugging facility. Dump() spits out XML which can be really tough to read,
    // and it's not clear what it should include.
    // So we have a facility to spit it out as a string.
    static string GetAsSourceString(Node n)
    {
        //System.Text.StringBuilder sb = new System.Text.StringBuilder();    
        //n.ToSource(sb);
        //return sb.ToString();
        return "empty";
    }
    
    public static void DumpSourceToStream(Node n, System.IO.TextWriter t)
    {
        Console.WriteLine("*** Begin Source dump: [");
        
        System.CodeDom.Compiler.IndentedTextWriter i = new 
            System.CodeDom.Compiler.IndentedTextWriter(t, "    ");
        
                
        n.ToSource(i);
        
        Console.WriteLine("] *** End source dump");
        
        i.Close();        
        i = null;
        
    }
    
    // Dump the string to the console.
    public static void DumpSource(Node n)
    {        
        DumpSourceToStream(n, Console.Out);     
    }
    
    // This should look like source but inject a few extra characters
    // to make it clear how it was resolved.
    public virtual void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)
    {
        //sb.Append("['" + this.GetType().ToString() + "' not implemented]");
        sb.Write("['{0}' not implemented]", this.GetType().ToString());
    }
    
#endregion   

#region Linenumber info
    // Line information.
    protected FileRange m_filerange;
    public FileRange Location
    {
        get { return m_filerange; }        
    } 
    
    // Parser has to be able to set the line number information. 
    public void SetLocation(FileRange l)
    {
        m_filerange = l;
    }
    
#endregion Linenumber info    
 
    // Shortcut helper functions.
    static public void PrintError(SymbolError.SymbolErrorException e)
    {    
        Blue.Driver.StdErrorLog.PrintError(e);
    }
    static public void ThrowError(SymbolError.SymbolErrorException e)
    {    
        Blue.Driver.StdErrorLog.ThrowError(e);
    }    
}
#endregion

#region Node for a Compilation-Unit
//-----------------------------------------------------------------------------
// Top level program
//-----------------------------------------------------------------------------

/// <summary>
/// The root AST node for an entire multi-file program.
/// </summary>
/// <remarks>
/// A ProgramDecl node contains all global namespace decls.
/// </remarks>
public class ProgramDecl : Node
{
#region Construction
    public ProgramDecl(NamespaceDecl [] nGlobal)
    {
        Debug.Assert(nGlobal != null);
        
        m_nGlobal = nGlobal;        
    }
#endregion
            
#region Checks    
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("Program");        
        foreach(NamespaceDecl n in m_nGlobal)
            n.Dump(o);
        o.WriteEndElement();
    }

    // Debugging check
    public override void DebugCheck(ISemanticResolver s)
    {
        foreach(NamespaceDecl n in m_nGlobal)
            n.DebugCheck(s);
    }
#endregion    

#region Properties & Data
    // Source file has a single implicity global namespace
    NamespaceDecl [] m_nGlobal;
    public NamespaceDecl [] GlobalNamespaces
    {
        get { return m_nGlobal; }
    }


    // List of classes, created by flattening all classes in
    // all the namespaces.
    TypeDeclBase[] m_arClasses;
    public TypeDeclBase[] Classes
    {
        get { 
            return m_arClasses; 
        }
    }
    
#endregion
    
#region Other Functions    
    // Get a flat array of classes from the namespaces
    // The array is topologically sorted such that:
    // if (i < j) then T[i] does not depend on T[j]
    protected void CreateClassListFromNamespaces()
    {
        Debug.Assert(m_arClasses == null);
        
        ArrayList a = new ArrayList();
        
        foreach(NamespaceDecl n in m_nGlobal)
            n.ReportClasses(a);
        
        m_arClasses = new TypeDeclBase[a.Count];
        for(int i = 0; i < a.Count; i++)
            m_arClasses[i] = (TypeDeclBase) a[i];
    }
#endregion    
    
#region Resolution    
    public void ResolveNamespace(ISemanticResolver s, Scope scopeGlobal)
    {
    // First must do namespaces so that type stubs even
    // have a context.
        foreach(NamespaceDecl n in m_nGlobal)
            n.ResolveNamespace("", s, scopeGlobal);

    // Next, resolve type stubs so that using alias at least
    // has stub types to refer to
        ResolveTypeStubs(s);

    // Now resolve Using decls. Have to do this before we
    // try and use any types (since using decls will affect resolution)
        foreach(NamespaceDecl n in m_nGlobal)
            n.ResolveUsingDecls(s);
    }
    
    public void NotifyResolutionDone()
    {
    // Can't create the single class list until after
    // all symbols have been resolved
        CreateClassListFromNamespaces();
    }
    

    // Add stubs for all user types.
    public void ResolveTypeStubs(ISemanticResolver s)
    {
        foreach(NamespaceDecl n in m_nGlobal)
            n.ResolveTypesAsBlueStub(s);
    }

    // Resolve the types
    public void ResolveTypes(
        ISemanticResolver s,
        ICLRtypeProvider provider)
    {	       

    // Then go through and resolve them to CLR types.
        foreach(NamespaceDecl n in m_nGlobal)
            n.ResolveTypesAsCLR(s, provider);    
    }

    // Resolve the member declarations within a class.
    // Since these can refer to other classes, we must have
    // resolved all types before resolving any members
    public void ResolveMemberDecls(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        foreach(NamespaceDecl n in m_nGlobal)
            n.ResolveMemberDecls(s, provider);
    }
	
    // Resolve the bodies of methods
    public void ResolveBodies(ISemanticResolver s)
    {  
        foreach(NamespaceDecl n in m_nGlobal)
            n.ResolveBodies(s);
    }
#endregion
   
} // end class Program

#endregion

#region AST Nodes that go in a Namespace Decl
//-----------------------------------------------------------------------------
// Using directive
//-----------------------------------------------------------------------------
public class UsingDirective : Node
{
#region Construction
    // For alias
    public UsingDirective(string stAlias, Exp eNamespace)
    {
        Debug.Assert(stAlias != null);
        Debug.Assert(eNamespace != null);

        m_stAlias = stAlias;
        m_eNamespace = eNamespace;
    }

    // For search path
    public UsingDirective(Exp eNamespace)
    {
        Debug.Assert(eNamespace != null);

        m_stAlias = null;
        m_eNamespace = eNamespace;
    }
#endregion

#region Properties & Data
    // We have 2 types of using directives: Alias & Search.
    public bool IsAliasType
    {
        get { return m_stAlias != null; }
    }
    
    string m_stAlias;
    Exp m_eNamespace;
#endregion

#region Resolution
    // Lookup the symbol in this Using Alias/Directive.
    // Return symbol if found.
    // Return null if not found.
    public SymEntry LookupSymbol(string stText)
    {
        SymEntry sym = null;

        if (IsAliasType)
        {
            // For an alias, if the string name matches, then we already have the symbol
            // to return. No search needed.
            if (stText == m_stAlias)
            {   
                if (m_eNamespace is NamespaceExp)
                {
                    return ((NamespaceExp) m_eNamespace).Symbol;                
                }
                
                else if (m_eNamespace is TypeExp)
                {
                    return ((TypeExp) m_eNamespace).Symbol;
                }
                
                Debug.Assert(false);
            }
        } 
        else 
        {
            // Lookup in associated namespace            
            Scope scope = ((NamespaceExp) m_eNamespace).Symbol.ChildScope;
            sym = scope.LookupSymbolInThisScopeOnly(stText);

            // We specifically do not look in nested namespaces
            // See section 9.3.2 of the C# spec for details
            if (sym is NamespaceEntry)
                sym = null;
        }

        return sym;
    }

    public void Resolve(ISemanticResolver s, Scope scopeNamespace)
    {
        // Have to see what our namespace / class resolves to.
        //m_eNamespace.ResolveExpAsRight(s);
        Exp.ResolveExpAsRight(ref m_eNamespace, s);
        if (IsAliasType)
        {
            // Alias could be to either a class or namespace
            //Debug.Assert(m_eNamespace.SymbolMode != ObjExp.Mode.cExpEntry);
            Debug.Assert(m_eNamespace is TypeExp || m_eNamespace is NamespaceExp);
        } 
        else 
        {
            // Directives can only give us namespaces
            //Debug.Assert(m_eNamespace.SymbolMode == ObjExp.Mode.cNamespaceEntry);
            Debug.Assert(m_eNamespace is NamespaceExp);
        }
    }
#endregion
    
#region Checks
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("Using");
        if (m_stAlias != null)
            o.WriteAttributeString("alias", m_stAlias);
        m_eNamespace.Dump(o);
        o.WriteEndElement();
    }

// Debugging check
    public override void DebugCheck(ISemanticResolver s)
    {
    }
#endregion
}

//-----------------------------------------------------------------------------
// Declare a user namespace
// Note that a single namespace can be declared across multiple NamespaceDecls.
// Each source file is considered to be in the Global Namespace.
// Each namespace block can have its own set of using directives. (So even
// if 2 blocks refer to the same namespace, they can have different using
// directives)
//-----------------------------------------------------------------------------
public class NamespaceDecl : Node, SymbolEngine.ILookupController
{
#region Construction
    // Any of the array parameters may be null
    public NamespaceDecl(
        Identifier idName, // must be non-null
        UsingDirective [] arUsingDirectives,
        NamespaceDecl[] arNestedNamespaces,
        ClassDecl[] arClasses,
        TypeDeclBase[] arTypes
    )
    {
        m_idName = idName;
    
        m_arUsingDirectives = (arUsingDirectives == null) ? new UsingDirective [0] : arUsingDirectives;
        m_arNestedNamespaces = (arNestedNamespaces == null) ? new NamespaceDecl[0] : arNestedNamespaces; 
        //m_arClasses = (arClasses == null) ? new ClassDecl[0] : arClasses;
        Debug.Assert(arClasses == null);
        
        m_arTypes = (arTypes == null) ? new TypeDeclBase[0] : arTypes;
    }
    
#endregion    

#region Other Functions    
    // Recursively Create a flat list of classes in a program
    public void ReportClasses(ArrayList alClasses)
    {   
        // At each stop, we make sure all of our prerequisite types are added
        // before us. So it doesn't matter what order we add in here.        
        foreach(TypeDeclBase e in m_arTypes)
            e.ReportClass(alClasses);
            
        // Recursively add all classes from nested namespaces
        foreach(NamespaceDecl n in NestedNamespaces)
            n.ReportClasses(alClasses);
    }
#endregion
    
#region Properties & Data    
    Identifier m_idName;
    UsingDirective [] m_arUsingDirectives;
    NamespaceDecl[] m_arNestedNamespaces;
    //ClassDecl[] m_arClasses;
    //EnumDecl[] m_arEnums;
    
    // @todo -merge this with classes
    TypeDeclBase [] m_arTypes;
    
    UserNamespaceEntry m_symbol;
    
    public string Name
    {
        get { return m_idName.Text; }
    }
 
    public NamespaceDecl[] NestedNamespaces
    {
        get { return  m_arNestedNamespaces; }
    }
        
 
    Scope m_context;
    
#endregion        
    
#region Resolution    
    #region Impl ILookupController
    
    // @dogfood - make these explicit interface methods
    // Return null if not found.
    public SymEntry SmartLookup(string stIdentifier, Scope scope)
    {
        // Do a normal lookup of things defined directly in this namespace.
        SymEntry sym = scope.LookupSymbolInThisScopeOnly(stIdentifier);
        if (sym != null)         
            return sym;        
        
        // Look in the using directives associated with this namespace node.
        sym = LookupSymbolInUsingDirectives(stIdentifier);
        return sym;
    }   
        
    // Get a node responsible for this scope.
    // For imported types, this will be null
    public AST.Node OwnerNode { 
        get { return this; }
    }
    
    // Get a symbol responsible for this scope. 
    // This may be null. (If this is null, then OwnerNode should not be null).
    public SymEntry OwnerSymbol { 
        get { return this.m_symbol; }
    }
    
    public void DumpScope(Scope scope)
    {
        if (this.m_arUsingDirectives.Length > 0)
        {
            Console.Write("[has using...]");
        }
        return;
    }
    
    #endregion

    #if true
    // Check the using directives in this namespace block. 
    // Give precedence to an alias over a directive.
    // Return null if not found. 
    // Throw error if ambigious (ie, a symbol can be found refered from multiple
    // using directives)
    // If we haven't resolved all of our own using directives yet, then return null
    // (this is to prevent using directives from affecting each other)
    public SymEntry LookupSymbolInUsingDirectives(string stName)
    {
        if (!m_fResolvedUsing)
            return null;

        // @todo - resolve ambiguity
        SymEntry sym = null;
        foreach(UsingDirective u in this.m_arUsingDirectives)
        {
            sym = u.LookupSymbol(stName);
            if (sym != null)
                return sym;
        }

        return null;
    }
    #endif

    // Flag to note if our using decls have been resolved yet
    bool m_fResolvedUsing = false;

    // Before we can process the classes, we need to add all the namespaces
    public void ResolveNamespace(
        string stParentNamespace, 
        ISemanticResolver s,
        Scope scopeParent)
    {
        Debug.Assert(m_symbol == null);
        
        // We can have one namespaces spread across multiple blocks (NamespaceDecl).
        // All blocks share the same scope (something defined in any block is visible
        // to all blocks), but each block can have its own lexical parent & set
        // of using clause. 
#if true
        m_symbol = (UserNamespaceEntry) scopeParent.LookupSymbolInThisScopeOnly(Name);
        if (m_symbol == null)
        {
            // Create new namespace
            string stFullName;
            if (stParentNamespace == "")
                stFullName = this.Name;
            else 
                stFullName = stParentNamespace + "." + this.Name;

            m_symbol = new UserNamespaceEntry(this, stFullName);
            scopeParent.AddSymbol(m_symbol);                        
        }
        
        // The symbol has the scope with all the data. But each namespace decl creates
        // a proxy scope that links to the symbol's data (Because all blocks share that)
        // but has a tie to its own set of using clauses & lexical parent.
        m_context = m_symbol.ChildScope.CreateSharedScope(this, scopeParent);
        
        foreach(NamespaceDecl n in NestedNamespaces)
            n.ResolveNamespace(m_symbol.FullName, s, m_context);
#else        
        
        // Since we can have multiple disjoint namespace decls refer
        // to the same namespace, we have to check and see if this
        // symbol is already created.        
        m_symbol = (UserNamespaceEntry) s.GetCurrentScope().LookupSymbolInThisScopeOnly(Name);
        if (m_symbol == null) 
        {
            string stFullName;
            if (stParentNamespace == "")
                stFullName = this.Name;
            else 
                stFullName = stParentNamespace + "." + this.Name;

            m_symbol = new UserNamespaceEntry(this, stFullName);        
            s.GetCurrentScope().AddSymbol(m_symbol);
        }
        
        
        EnterNamespace(s);
        
        foreach(NamespaceDecl n in NestedNamespaces)
            n.ResolveNamespace(m_symbol.FullName, s, scopeParent);
            
        
        ExitNamespace(s);
#endif        
    }
/*    
    void EnterNamespace(ISemanticResolver s)
    {
        m_symbol.SetCurrentNode(this);
        s.PushScope(m_symbol.ChildScope);
    }
    
    void ExitNamespace(ISemanticResolver s)
    {
        m_symbol.SetCurrentNode(null);
        s.PopScope(m_symbol.ChildScope);
    }
*/    

    // Resolve the using declarations
    public void ResolveUsingDecls(ISemanticResolver s)
    {
        // All using declarations are resolved at global scope,
        // so don't enter / exit namespaces here
        //EnterNamespace(s);

        
        // Resolve all the using clauses in this namespace block
        Scope prev = s.SetCurrentContext(this.m_context);
        
        foreach(UsingDirective u in this.m_arUsingDirectives)
            u.Resolve(s, m_context);
            
        s.RestoreContext(prev);            
            
        // Recursively add all classes from nested namespaces
        foreach(NamespaceDecl n in NestedNamespaces)
            n.ResolveUsingDecls(s);

        //ExitNamespace(s);

        m_fResolvedUsing = true;
    }
    
    public void ResolveTypesAsBlueStub(
        ISemanticResolver s)
    {
        //EnterNamespace(s);
        
        // add all classes in this namespace
        {            
            foreach(TypeDeclBase e in m_arTypes)
                e.ResolveTypesAsBlueStub(s, this.m_symbol.FullName, m_context);
        }
            
        // Recursively add all classes from nested namespaces
        foreach(NamespaceDecl n in NestedNamespaces)
            n.ResolveTypesAsBlueStub(s);
            
        //ExitNamespace(s);
    }
    
    public void ResolveTypesAsCLR(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        // We shouldn't have to update the scope stack since
        // we should only be looking at resolved symbols

        Scope prev = s.SetCurrentContext(m_context);
        
        // add all classes in this namespace
        {   
            foreach(TypeDeclBase e in m_arTypes)
                e.ResolveTypesAsCLR(s, provider);
        }
        
        s.RestoreContext(prev);
            
        // Recursively add all classes from nested namespaces
        foreach(NamespaceDecl n in NestedNamespaces)
            n.ResolveTypesAsCLR(s, provider);
    }
	
    public void ResolveMemberDecls(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        // Do for all types in this namespace
        foreach(TypeDeclBase e in m_arTypes)
                e.ResolveMemberDecls(s, provider);
        
            
        // Recursively do all classes from nested namespaces
        foreach(NamespaceDecl n in NestedNamespaces)
            n.ResolveMemberDecls(s, provider);
        
    }

    // Resolve the bodies of methods
    public void ResolveBodies(ISemanticResolver s)
    {   
        foreach(TypeDeclBase e in m_arTypes)
            e.ResolveBodies(s);
        
            
        // Recursively add all classes from nested namespaces
        foreach(NamespaceDecl n in NestedNamespaces)
            n.ResolveBodies(s);
        
    }
#endregion    
    
#region Checks    
    // Dump the AST node contents to an xml file
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("Namespace");        
        o.WriteAttributeString("name", Name);
        
        o.WriteStartElement("UsingDirectives");
        foreach(UsingDirective n in m_arUsingDirectives)
            n.Dump(o);
        o.WriteEndElement();
        
        o.WriteStartElement("NestedNamespaces");
        foreach(NamespaceDecl n in m_arNestedNamespaces)
            n.Dump(o);
        o.WriteEndElement();
               
        /*                
        o.WriteStartElement("Classes");
        foreach(ClassDecl n in m_arClasses)
            n.Dump(o);
        o.WriteEndElement();
        */

        o.WriteStartElement("Types");
        foreach(TypeDeclBase e in m_arTypes)
            e.Dump(o);
        o.WriteEndElement();
        
        o.WriteEndElement();
    }

    // Debugging check
    public override void DebugCheck(ISemanticResolver s)
    {   
        //EnterNamespace(s);
        
        Debug.Assert(m_arUsingDirectives != null);
        Debug.Assert(m_arNestedNamespaces != null);
        //Debug.Assert(m_arClasses != null);
        
        /*
        foreach(ClassDecl c in m_arClasses)
        {
            c.DebugCheck(s);
        }
        */

        foreach(NamespaceDecl n in this.m_arNestedNamespaces)
        {
            n.DebugCheck(s);
        }
        
        foreach(TypeDeclBase e in m_arTypes)
            e.DebugCheck(s);

        foreach(UsingDirective u in this.m_arUsingDirectives)
        {
            u.DebugCheck(s);
        }

        //ExitNamespace(s);
    }
#endregion    
}


#region Base class for all type declarations
//-----------------------------------------------------------------------------
// Base for type declarations
//-----------------------------------------------------------------------------
public abstract class TypeDeclBase : Node
{    
#region Resolution
    // Resolution is the act of changing strings (in a parse tree) into symbols.
    // Symbols are associated with the appropriate object in the System.Reflection]
    // namespace.
    public abstract void ResolveTypesAsBlueStub(
        ISemanticResolver s,
        string stNamespace, // namespace that a type goes in. Includes nested classes.
        Scope scopeParent   // our Lexical parent's scope that we should add ourselves too.
    );
    
    public abstract void ResolveTypesAsCLR(
        ISemanticResolver s,
        ICLRtypeProvider provider
    );
    
    public abstract void ResolveMemberDecls(
        ISemanticResolver s,
        ICLRtypeProvider provider
    );
    
    public abstract void ResolveBodies(ISemanticResolver s);
    
    public abstract void ReportClass(ArrayList alClasses);
#endregion
    
#region Generate    
    // Generate the body for this type
    public abstract void GenerateType(Blue.CodeGen.EmitCodeGen gen);
    
    // Get the CLR type that this node represents. Useful for codegen.
    public abstract System.Type CLRType
    {
        get;
    }
#endregion
} // end TypeDeclBase

#endregion // Type Decl base





#region Delegate type declaration
//-----------------------------------------------------------------------------
// Delegates
//-----------------------------------------------------------------------------
public class DelegateDecl : TypeDeclBase
{
#region Construction    
    public DelegateDecl(
        Identifier      idName,
        TypeSig         tRetType,
        ParamVarDecl[]  arParams,        
        Modifiers       mods
    )
    {
        Debug.Assert(idName != null);
        Debug.Assert(tRetType != null);
        Debug.Assert(arParams != null);
                
        m_idName    = idName;
        m_tRetType  = tRetType;
        m_arParams  = arParams;
        m_mods      = mods;
        
        // Implied sealed
        m_mods.SetSealed();
    }    
#endregion Construction

#region Properties & Data
    Identifier      m_idName;    
    TypeSig         m_tRetType;
    ParamVarDecl[]  m_arParams;        
    Modifiers       m_mods;
    
    ClassDecl       m_nodeProxy;
    //TypeEntry       m_symbol;
#endregion

#region Resolution
    // Helper function
    // Given a symbol for a delegate, get the params for it
    static public System.Type [] GetParams(TypeEntry t)
    {
        // We can do this by looking up the invoke function
        MethodHeaderEntry header = t.LookupMethodHeader("Invoke");
        MethodExpEntry m = header.GetFirstMethod();
        
        Debug.Assert(header.GetNextMethod(m) == null, "Delegate should only have 1 Invoke() method");
        
        // Get parameters off this method
    
        System.Type [] al = m.ParamTypes(false);
        return al;
    }
    
    // Is this CLR type a delegate?
    public static bool IsDelegate(System.Type t)
    {
        // Determine by base type..
        return 
            (t != null) && 
            (t.BaseType == typeof(System.MulticastDelegate));
    }

    // Delegates are really just blessed Types.
    void CreateProxyType(ISemanticResolver s)
    {
        Debug.Assert(m_nodeProxy == null, "only create proxy once");
    // The delegate R F(A) (where R is a return type, and A is a parameter list)
    // Can be converted into the type:
    // sealed class F : System.MulticastDelegate {
    //      F(object, native int) { }
    //      BeginInvoke() { }
    //      EndInvoke() { }
    //      R Invoke(A) { }
    // }
        BlockStatement stmtEmpty = new BlockStatement(null, new Statement[0]);
        Modifiers modsPublic = new Modifiers();
        modsPublic.SetPublic();
        
        Modifiers modsVirtual = modsPublic;
        modsVirtual.SetVirtual();
        
    
        //System.Type tNativeInt = typeof(int);
        System.Type tNativeInt = Type.GetType("System.IntPtr");
    
        TypeEntry t_IAsyncResult = s.ResolveCLRTypeToBlueType(typeof(System.IAsyncResult));
        
        // Create the parameters for the BeginInvoke()
        ParamVarDecl [] paramBeginInvoke = new ParamVarDecl[m_arParams.Length + 2];
        m_arParams.CopyTo(paramBeginInvoke, 0);
        paramBeginInvoke[m_arParams.Length]= new ParamVarDecl(
            new Identifier("cb"), 
            new ResolvedTypeSig(typeof(System.AsyncCallback), s), 
            EArgFlow.cIn
        );
        
        paramBeginInvoke[m_arParams.Length + 1] = new ParamVarDecl(
            new Identifier("state"), 
            new ResolvedTypeSig(typeof(System.Object), s), 
            EArgFlow.cIn
        );
    
        m_nodeProxy = new ClassDecl(
            m_idName,
            new TypeSig[] {
                new ResolvedTypeSig(typeof(System.MulticastDelegate), s)
            },
            new MethodDecl[] {
            // Ctor
                new MethodDecl(
                    m_idName, null, new ParamVarDecl[] {
                        new ParamVarDecl(new Identifier("instance"),    new ResolvedTypeSig(typeof(object), s),  EArgFlow.cIn),
                        new ParamVarDecl(new Identifier("func"),        new ResolvedTypeSig(tNativeInt, s),  EArgFlow.cIn)
                    },
                    stmtEmpty, modsPublic
                ),
            // Invoke,
                new MethodDecl(
                    new Identifier("Invoke"),
                    this.m_tRetType,
                    this.m_arParams,
                    stmtEmpty,
                    modsVirtual),
                    
            // Begin Invoke
                new MethodDecl(
                    new Identifier("BeginInvoke"),
                    new ResolvedTypeSig(t_IAsyncResult),
                    paramBeginInvoke,
                    stmtEmpty,
                    modsVirtual),                    
            
            // End Invoke
                new MethodDecl(
                    new Identifier("EndInvoke"),
                    this.m_tRetType,
                    new ParamVarDecl[] {
                        new ParamVarDecl(new Identifier("result"), new ResolvedTypeSig(t_IAsyncResult), EArgFlow.cIn)
                    },
                    stmtEmpty,
                    modsVirtual)
                                            
            },
            new PropertyDecl[0],
            new FieldDecl[0],
            new EventDecl[0],
            new TypeDeclBase[0],
            m_mods,
            true); // isClass
            
    }

    // Resolution functions
    public override void ResolveTypesAsBlueStub(
        ISemanticResolver s,
        string stNamespace, // namespace that a type goes in. Includes nested classes.
        Scope scopeParent
    )
    {
        CreateProxyType(s);
        this.m_nodeProxy.ResolveTypesAsBlueStub(s, stNamespace, scopeParent);
        
        // We can go ahead and resolve the rest because we know that a Delegate
        // doesn't inherit / implement interfaces for other user-defined classes.
        //this.m_nodeProxy.ResolveTypesAsBlueLinks(s);
        //this.m_nodeProxy.ResolveTypesAsBlueFinal(s);
    }
    
    public override void ResolveTypesAsCLR(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        this.m_nodeProxy.ResolveTypesAsCLR(s, provider);
    }
    
    public override void ResolveMemberDecls(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        this.m_nodeProxy.ResolveMemberDecls(s, provider);
    }
    
    public override void ResolveBodies(ISemanticResolver s)
    {
        this.m_nodeProxy.ResolveBodies(s);
    }
        
    public override void ReportClass(ArrayList alClasses)
    {
        alClasses.Add(this.m_nodeProxy);
    }
    
    // Get the CLR type that this node represents. Useful for codegen.
    public override System.Type CLRType
    {
        get {
            return this.m_nodeProxy.CLRType;
        }
    }
#endregion Resolution 

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_nodeProxy != null);
        this.m_nodeProxy.DebugCheck(s);
    }
    
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("DelegateDecl");
        this.m_nodeProxy.Dump(o);
        o.WriteEndElement();
    }
#endregion Checks

#region Generate
    public override void GenerateType(Blue.CodeGen.EmitCodeGen gen)
    {
        this.m_nodeProxy.GenerateType(gen);
    }
#endregion Generate
}
#endregion

#region Enums type declaration
    
//-----------------------------------------------------------------------------
// Declare an enum
//-----------------------------------------------------------------------------
public class EnumDecl : TypeDeclBase
{
#region Construction
    public EnumDecl(Identifier idName, FieldDecl[] fields, Modifiers mods)
    {
        Debug.Assert(fields != null);
        m_fields = fields;
        m_idName = idName;
        m_mods = mods;
    }
#endregion

#region Properties & Data
    FieldDecl[] m_fields;
    public FieldDecl[] Fields
    {
        get { return m_fields; }
    }

    Identifier m_idName;
    public Identifier Name
    {
        get { return m_idName; }
    }

    EnumTypeEntry m_symbol;
    public EnumTypeEntry Symbol 
    {
        get { return m_symbol; }
    }
    
    Modifiers m_mods;
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        foreach(FieldDecl f in m_fields)
        {
            f.DebugCheck(s);
        }
    }
    
    public override void Dump(XmlWriter o)
    {
        foreach(FieldDecl f in m_fields)
        {
            f.Dump(o);
        }
    }
#endregion

#region Resolution
    // Semantic checking
    // Stubs can be resolved in any order.
    public override void ResolveTypesAsBlueStub(
        ISemanticResolver s,
        string stNamespace,
        Scope scopeParent
        )
    {
        // Get our name. Nested classes are separated with a '+'
        // Namespaces are separated with a '.'
        string stFullName = (stNamespace == "") ? 
            (Name.Text) :
            (stNamespace + "." + Name.Text);

        // Are we a nested class? 
        if (scopeParent.Node is ClassDecl)
        {
            Debug.Assert(stNamespace != "");
            stFullName = stNamespace + "+" + Name.Text;
        }                    

        // Create a stub sym entry to add to current scope
        m_symbol = new EnumTypeEntry(Name.Text, stFullName, s, m_mods, this, scopeParent);
        scopeParent.AddSymbol(m_symbol);
    }

    public override void ResolveTypesAsCLR(
        ISemanticResolver s,
        ICLRtypeProvider provider
        )
    {
        if (m_symbol.CLRType != null)
            return;
            
        m_symbol.SetCLRType(provider); // enum
                
        s.AddClrResolvedType(this.Symbol);
    }


    public override void ResolveMemberDecls(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        Scope scope = m_symbol.MemberScope;
        
        int iLastValue = -1;
        Scope prev = s.SetCurrentContext(scope);
        
        for(int i = 0; i < this.Fields.Length; i++)
        {
            FieldDecl f = this.Fields[i];
            Exp e = f.InitialExp;
            //Debug.Assert(e != null);

            // Evaluate e to a literal...
            //Exp.TrySimplify(ref e);
            int iValue;
            if (e != null)
            {
                IntExp e2 = e as IntExp;
                Debug.Assert(e2 != null, "Can only assign to an integer literal"); //@todo - legit error
                iValue = e2.Value;
            } 
            else 
            {
                if (i == 0)
                    iValue = 0;
                else
                    iValue = iLastValue + 1;
            }

            // Need to create an object of type 'ThisEnum' with integer value iValue;
            //Type tEnum = this.Symbol.CLRType;
            // errors
            //System.ComponentModel.TypeConverter c = new System.ComponentModel.TypeConverter();
            //object o2 = c.ConvertTo(iValue, tEnum);
            
            // errors
            // object oValue = System.Enum.ToObject(tEnum, iValue);
            iLastValue = iValue;
            f.ResolveMemberAsLiteral(this.m_symbol, s, iValue);

            LiteralFieldExpEntry l = (LiteralFieldExpEntry) f.Symbol;
            l.SetInfo(provider);
        }
        
        //s.PopScope(scope);
        s.RestoreContext(prev);
    }

    public override void ResolveBodies(ISemanticResolver s)
    {
        // @todo - set LiteralFieldExpEntry's Data here
    }
#endregion // Resolution

    // Add this type to the topological list of types
    bool m_fReported = false;
    public override void ReportClass(ArrayList alClasses)
    {
        if (m_fReported)
            return;
            
        m_fReported = true;
                
        // Emit forces us to CreateType() on outer classes 
        // before inner classes.
        TypeEntry tOuter = this.Symbol.GetContainingType();
        if (tOuter != null)
        {
            ClassDecl c = tOuter.Node;
            Debug.Assert(c != null); // containing type should not be imported
            c.ReportClass(alClasses);
        }
        
        alClasses.Add(this);
    }
    
    // Get the CLR type that this node represents. Useful for codegen.
    public override System.Type CLRType
    {
        get {
            return this.m_symbol.CLRType;
        }
    }

    public override void GenerateType(Blue.CodeGen.EmitCodeGen gen)
    {
        gen.GenerateEnum(this);
    }
}

#endregion // enums


#region Class type declaration
//-----------------------------------------------------------------------------
// Declare a class
// A class decl represents Classes, Interfaces, and Structs (value-types)
//-----------------------------------------------------------------------------
public class ClassDecl : TypeDeclBase
{
#region Construction        
    // For interface types
    public ClassDecl(
        Identifier idName,         
        TypeSig [] arSuper, // super class & implemented interfaces
        MethodDecl [] alMethods,
        PropertyDecl[] alProperties,
        Modifiers mods    
    ) 
    {        
        Debug.Assert(idName != null);
        Debug.Assert(alMethods != null);
        

        m_strName = idName.Text;
        
        m_arSuper = (arSuper == null) ? new TypeSig[0] : arSuper;

        m_alMethods = alMethods;
        m_alProperties = alProperties;
        m_alNestedTypes = m_sEmptyTypeList;
        
        // @todo - this is wrong
        m_filerange = idName.Location;

        //m_mods.FlagSetter = mods.Flags | Modifiers.EFlags.Abstract;
        m_mods = mods;
        m_mods.SetAbstract();
       
        m_genre = TypeEntry.Genre.cInterface;
    }

    // For non-interface types
    public ClassDecl(
        Identifier idName,         
        TypeSig [] arSuper, // super class & implemented interfaces
        MethodDecl [] alMethods,
        PropertyDecl[] alProperties,
        FieldDecl[] alFields,
        EventDecl [] alEvents,
        TypeDeclBase[] alNestedTypes,
        Modifiers mods,
        bool fIsClass // true for class, false for struct
    ) 
    {        
        Debug.Assert(idName != null);
        Debug.Assert(alMethods != null);
        Debug.Assert(alFields != null);
        Debug.Assert(alProperties != null);
        Debug.Assert(alEvents != null);


        m_strName = idName.Text;
        
        m_arSuper = (arSuper == null) ? new TypeSig[0] : arSuper;

        m_alNestedTypes     = (alNestedTypes == null) ?  m_sEmptyTypeList : alNestedTypes;
        m_alMethods         = alMethods;
        m_alProperties      = alProperties;
        m_alFields          = alFields;
        m_alEvents          = alEvents;
        
        // @todo - this is wrong
        m_filerange = idName.Location;

        if (!fIsClass) // structs are implicitly sealed.           
            mods.SetSealed();
            
        m_mods = mods;          
                    
        m_genre = fIsClass ? TypeEntry.Genre.cClass : TypeEntry.Genre.cStruct;
    }
#endregion

#region Properties & Data
    static TypeDeclBase [] m_sEmptyTypeList = new TypeDeclBase[0];
    
    readonly TypeEntry.Genre m_genre;
    public bool IsInterface 
    {        
        get { return m_genre == TypeEntry.Genre.cInterface; }
    }

    public bool IsClass
    {        
        get { return m_genre == TypeEntry.Genre.cClass; }
    }
    
    public bool IsStruct
    {
        get { return m_genre == TypeEntry.Genre.cStruct; }
    }
   
    MethodDecl m_nodeStaticInit;
    public MethodDecl StaticInitMethod
    {
        get {return m_nodeStaticInit; }
    }

    MethodDecl m_nodeInstanceInit;
    public MethodDecl InstanceInitMethod
    {
        get {return m_nodeInstanceInit; }
    }
    
    protected Modifiers m_mods;
    public Modifiers Mods
    {
        get { return m_mods; }
    }
    
    TypeSig [] m_arSuper; // super class & implemented interfaces
    
    public MethodDecl[] Methods
    {
        get { return m_alMethods; }
    }

    public string Name 
    {
        get { return m_strName; }
    }
   
    public PropertyDecl[] Properties
    {
        get { return m_alProperties; }
    }
    
    public FieldDecl[] Fields
    {
        get { return m_alFields; }
    }
    
    public EventDecl[] Events
    {
        get { return m_alEvents; }
    }

    protected MethodDecl[] m_alMethods;
    protected FieldDecl[] m_alFields;
    protected PropertyDecl[] m_alProperties;
    protected EventDecl[] m_alEvents;
    
    protected TypeDeclBase[] m_alNestedTypes;
    public TypeDeclBase[] NestedTypes
    {
        get { return m_alNestedTypes; }
    }
    

    readonly string m_strName;
    protected TypeEntry m_symbol;
        
    public TypeEntry Symbol
    {
        get { return m_symbol; }
    }
#endregion

#region Checks
// Debugging check
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_strName != null);
    
        //Debug.Assert(IsClass ^ IsInterface); // mutually exclusive options
    
        // Verify symbol
        Debug.Assert(m_symbol != null);
        Debug.Assert(m_symbol.Node == this);        
        Debug.Assert(m_symbol.MemberScope != null); // this may be ok
        Debug.Assert(m_symbol.Name == m_strName);       

        Debug.Assert(m_symbol.CLRType != null);
        
        Debug.Assert(m_arSuper != null);
        
        // The only class without a super class is system.object.
        // But all user-defined classes must have some super class
        if (!IsInterface)
        {
            Debug.Assert(m_symbol.Super!= null);

            if (m_symbol.Super != null)
            {
                // If we inherit from a user-defined class, then 
                // check the AST
                /*
                if (IdSuper != null)
                {
                    Debug.Assert(IdSuper.Text == m_symbol.Super.Name);
                }
                */
            }            
        
            // Fields                
            foreach(FieldDecl f in m_alFields)
            {
                f.DebugCheck(s);
            }
        } 
        else 
        {
            // For interfaces, we don't extend a class
            Debug.Assert(m_symbol.Super == null);
        }
        
        // Methods
        foreach(MethodDecl m in m_alMethods)
        {
            m.DebugCheck(s);
        }
        
        // Properties
        foreach(PropertyDecl p in m_alProperties)
            p.DebugCheck(s);
            
        // Nested types
        foreach(TypeDeclBase t in m_alNestedTypes)
           t.DebugCheck(s);            
    }

// Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ClassDecl");
        
        o.WriteAttributeString("name", m_strName);
        if (m_symbol != null)
            o.WriteAttributeString("symbol", m_symbol.ToString());

        if (IsInterface)
            o.WriteAttributeString("IsInterface", "yes");
        if (IsClass)
            o.WriteAttributeString("IsClass", "yes");
        if (IsStruct)
            o.WriteAttributeString("IsStruct", "yes");
    
        foreach(TypeSig t in m_arSuper)
        {
            o.WriteAttributeString("super", t.ToString());
        }
        
        if (!IsInterface)
        {
            foreach(FieldDecl f in m_alFields)
            {
                f.Dump(o);
            }
        }
        
        foreach(MethodDecl m in m_alMethods)
        {
            m.Dump(o);
        }
        
        foreach(PropertyDecl p in m_alProperties)
            p.Dump(o);
            
        foreach(TypeDeclBase t in m_alNestedTypes)
            t.Dump(o);            
                                    
        o.WriteEndElement();
    }
#endregion    
    
#region Resolution

#region Topological Sort
    // Recursive One-time build a topological sort of classes.    
    bool m_fReported = false;
    public override void ReportClass(ArrayList alClasses)
    {
        // keep flag to avoid cycles
        if (m_fReported)
            return;
            
        m_fReported = true;

        // Add all of our dependencies before us
        if (this.IsClass && (this.Symbol.Super.Node != null))
            Symbol.Super.Node.ReportClass(alClasses);

        foreach(TypeEntry t in this.Symbol.BaseInterfaces)
        {
            if (t.Node != null)
            {
                t.Node.ReportClass(alClasses);
            }
        }
        
        // Emit forces us to CreateType() on outer classes 
        // before inner classes.
        TypeEntry tOuter = this.Symbol.GetContainingType();
        if (tOuter != null)
        {
            ClassDecl c = tOuter.Node;
            Debug.Assert(c != null); // containing type should not be imported
            c.ReportClass(alClasses);
        }
        
        
        
        #if true
        
        if (!IsInterface)
        {
            // @error - what if the value type is a nested class?
            // Any field that's a value type has to be done before us.
            foreach(FieldDecl f in this.Fields)
            {
                if (f.Symbol.FieldType.IsImported)
                    continue; // imported types don't have to be added.
                 
                #if true
                // Any field that's nested in the current type doesn't need to be added
                if (f.Symbol.FieldType.CLRType.DeclaringType == this.Symbol.CLRType)
                    continue;
                #endif                    
                    
                Type t = f.Symbol.CLRType;
                if (t.IsEnum)
                {
                    // If we imported the enum, it will import as a TypeEntry, but then we still
                    // don't have to add it.
                    EnumDecl e = ((EnumTypeEntry) f.Symbol.FieldType).Node;
                    if (e != null)
                    {
                        e.ReportClass(alClasses);                        
                    }                
                } 
                else if (t.IsValueType)
                {
                    if (f.Symbol.FieldType.Node != null)
                    {
                        f.Symbol.FieldType.Node.ReportClass(alClasses);
                    }
                }
            }
        }
        #endif

        // Now, add this class
        alClasses.Add(this);
                
        // Now that the outer type is added, make sure all nested classes
        // get added.        
        foreach(TypeDeclBase t in this.m_alNestedTypes)
        {
            t.ReportClass(alClasses);
        }
        
    }
    
    // Get the CLR type that this node represents. Useful for codegen.
    public override System.Type CLRType
    {
        get {
            return this.Symbol.CLRType;
        }
    }
#endregion    

#region Resolve Types as Blue Stub
    // Semantic checking
    // Stubs can be resolved in any order.
    public override void ResolveTypesAsBlueStub(
        ISemanticResolver s,
        string stNamespace,
        Scope scopeParent
        )
    {
        // Get our name. Nested classes are separated with a '+'
        // Namespaces are separated with a '.'
        string stFullName = (stNamespace == "") ? 
            (m_strName) :
            (stNamespace + "." + m_strName);

        // Are we a nested class? 
        if (scopeParent.Node is ClassDecl)
        {
            Debug.Assert(stNamespace != "");
            stFullName = stNamespace + "+" + m_strName;
        }                    

        // Create a stub sym entry to add to current scope
        m_symbol = new TypeEntry(stFullName, this, m_genre, scopeParent);
        scopeParent.AddSymbol(m_symbol);
        
        // Stub on nested types
        //s.PushScope(m_symbol.MemberScope);
        
        // Our context is the same as the scope on our symbol
        Scope context = m_symbol.MemberScope;
        Debug.Assert(context != null);
        
        s.SetCurrentClass(m_symbol);
                    
        foreach(TypeDeclBase t in this.m_alNestedTypes)
        {   
            t.ResolveTypesAsBlueStub(s, stFullName, context);
        }
        
        s.SetCurrentClass(null);
        //s.PopScope(m_symbol.MemberScope);
    }
#endregion

#region Resolve Type as CLR

    // Called after we've resolved as a blue type
    // Pair this type with a CLR type obtained via the Provider
    // Recursively call on all inherited types (base class & interfaces)
    public override void ResolveTypesAsCLR(
        ISemanticResolver s,
        ICLRtypeProvider provider
        )
    {
        // Since this can be called recursively, bail if we're already resolved.
        if (this.Symbol.IsInit)
            return;
        
        
        // S is already set to the context that we're evaluating in.
        Scope prev = s.SetCurrentContext(this.Symbol.MemberScope.LexicalParent);
        
        FixBaseTypes(s, provider); // this is recursive        
        CreateCLRType(s, provider);
        
        s.RestoreContext(prev);
    }

    // Now that all the types have been stubbed and established their context,
    // we can recursively run through and resolve all of our base types.
    void FixBaseTypes(
        ISemanticResolver s,  
        ICLRtypeProvider provider
    )
    {
        TypeEntry tSuper = null;
        
        BeginCheckCycle(s);
        
        ArrayList alInterfaces = new ArrayList();
        
        // Decide who our super class is and which interfaces we're inheriting
        foreach(TypeSig sig in this.m_arSuper)
        {
            sig.ResolveType(s);
            
            TypeEntry t = sig.BlueType;
            
            // Make sure this base type is resolved. Do this recursively
            ClassDecl c = t.Node;
            if (c != null)
            {
                c.ResolveTypesAsCLR(s, provider);
            }
            
            if (t.IsClass)
            {
                Debug.Assert(this.IsClass, "Only a class can have a super-class");
                
                if (tSuper != null)
                {
                    ThrowError(SymbolError.OnlySingleInheritence(this));                
                }
                tSuper = t;
            } else {
                // Both structs & interfaces can only derive from interfaces (not classes)
                if (!t.IsInterface)
                    ThrowError(SymbolError.MustDeriveFromInterface(this, t));
                alInterfaces.Add(t);
            }
        }
        
        TypeEntry [] tInterfaces = new TypeEntry[alInterfaces.Count];
        for(int i = 0; i < alInterfaces.Count; i++)
            tInterfaces[i] = (TypeEntry) alInterfaces[i];
        
        // If no super class is specified, then we use as follows:
        // 'Interface' has no super class,
        // 'Class'     has 'System.Object'
        // 'Struct'    has 'System.ValueType'        
        if (!IsInterface && (tSuper == null))
        {
            if (IsClass)
                tSuper = s.ResolveCLRTypeToBlueType(typeof(object));
            if (IsStruct)
                tSuper = s.ResolveCLRTypeToBlueType(typeof(System.ValueType));                
        }
        
        
        Debug.Assert(IsInterface ^ (tSuper != null));

        // Just to sanity check, make sure the symbol stub is still there
        #if DEBUG               
        TypeEntry sym = (TypeEntry) s.GetCurrentContext().LookupSymbolInThisScopeOnly(m_strName);        
        Debug.Assert(sym == m_symbol);
        #endif

        m_symbol.InitLinks(tSuper, tInterfaces);
                
        // Make sure all of our base types are resolved        
        foreach(TypeEntry t in this.Symbol.BaseInterfaces)
        {               
            t.EnsureResolved(s);
        }
            
        if (Symbol.Super != null)                   
            Symbol.Super.EnsureResolved(s);
                
        // Final call. Set super scope
        m_symbol.FinishInit();
        
        
        EndCheckCycle();
    }
    
    // Flag to help check for cyclical dependencies
    bool m_fIsResolving;
    
    protected void BeginCheckCycle(
        ISemanticResolver s
    )
    {
        // This may get called mutliple times (since we call it recursively,
        // and we may have multiple derived classes). But it shouldn't 
        // get called from itself (which would imply circular reference)
        if (m_fIsResolving)
        {
            ThrowError(SymbolError.CircularReference(this.Symbol));
            /*
            s.ThrowError(SemanticChecker.Code.cCircularReference,
                this.Location, "Type '" + this.Symbol.FullName + "' is in a circular reference");
            */                
        }
        m_fIsResolving = true;
    }
    
    protected void EndCheckCycle()
    {
        m_fIsResolving = false;
    }
    
        
    // Requires that all of our base types have been resolved.
    void CreateCLRType(
        ISemanticResolver s,
        ICLRtypeProvider provider)
    {
    // Should have already resolved as a blue type
        Debug.Assert(this.Symbol != null);

        BeginCheckCycle(s);

        // Now that all of our dependent classes have their clr type set,
        // we can go ahead and set ours.
        // Note that a derived class may have already set ours, so we have
        // to check
        if (Symbol.CLRType == null)
        {
            // Get the clr type and then update the symbol engine
            this.Symbol.SetCLRType(provider);
            s.AddClrResolvedType(this.Symbol);
        }
        Debug.Assert(this.Symbol.CLRType != null);

        // Nested classes
        foreach(TypeDeclBase t in this.m_alNestedTypes)
        {
            t.ResolveTypesAsCLR(s, provider);        
        }

        EndCheckCycle();
    }

#endregion // resolution

#region Resolve Members
    
    // Make sure that all of our base types are resolved.
    void FixBaseMemberDecls(ISemanticResolver s, ICLRtypeProvider provider)
    {   
        if (this.IsClass)
        {
            if (this.Symbol.Super.Node != null)
                this.Symbol.Super.Node.ResolveMemberDecls(s, provider);
        }
                
        foreach(TypeEntry t in this.Symbol.BaseInterfaces)
        {
            if (t.Node != null)
                t.Node.ResolveMemberDecls(s, provider);
        }
    }

    // Resolve all the fields in this type. Only class/struct should call
    // this. 
    void FixFields(ISemanticResolver s, ICLRtypeProvider provider)
    {
        Debug.Assert(!IsInterface);
        
        int cInstance = 0;
        int cStatic = 0;

        foreach(FieldDecl f in m_alFields)
        {
            f.ResolveMember(m_symbol, s, provider);
            //f.Symbol.SetInfo(provider);

            if (f.InitialExp != null)
                if (f.Mods.IsStatic) 
                    cStatic++; 
                else 
                    cInstance++;
        }


        Statement [] stmtStatic = new Statement[cStatic];
        Statement [] stmtInstance = new Statement[cInstance];

        cStatic = 0;
        cInstance = 0;
        // Fields can have assignments. Make 2 helper functions to do
        // assignment for static & instance fields
        foreach(FieldDecl f in m_alFields)
        {
            if (f.InitialExp != null)
            {           
                Statement stmt = new ExpStatement(new AssignStmtExp(
                    new SimpleObjExp(new Identifier(f.Name, f.Location)), f.InitialExp));

                if (f.Mods.IsStatic)
                {
                    stmtStatic[cStatic] = stmt;
                    cStatic++;
                } 
                else 
                {
                    if (IsStruct)
                    {
                        //ThrowError_NoFieldInitForStructs(s, f);
                        ThrowError(SymbolError.NoFieldInitForStructs(f));
                    }
                    
                    stmtInstance[cInstance] = stmt;
                    cInstance ++;
                }
            } // end has initializer expression
        }

        Debug.Assert(cStatic == stmtStatic.Length);
        Debug.Assert(cInstance == stmtInstance.Length);

        // Create methods to initialize the static & instance fields.
        // Then the ctors can call these methods
        if (cStatic != 0)
        {
            Modifiers mods = new Modifiers();
            mods.SetStatic();
            mods.SetPrivate();
            
            m_nodeStaticInit = new MethodDecl(
                new Identifier(".StaticInit", this.Location), 
                new ResolvedTypeSig(typeof(void), s),                    
                new ParamVarDecl[0],
                new BlockStatement(null, stmtStatic),                     
                mods
                );

            //AddMethodToList(m_nodeStaticInit);
        } 
        if (cInstance != 0)
        {   
            Modifiers mods = new Modifiers();
            mods.SetPrivate();
            
            m_nodeInstanceInit = new MethodDecl(
                new Identifier(".InstanceInit", this.Location),                     
                new ResolvedTypeSig(typeof(void), s),
                new ParamVarDecl[0],
                new BlockStatement(null, stmtInstance),                     
                mods
                );
            AddMethodToList(m_nodeInstanceInit);
        }        
    } // end fields
    
    // Debug only, make sure that we don't add methods after we start resolving them.
    #if DEBUG
    bool m_fLockMethodList;
    #endif
    
    void FixMethods(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {        
        #if DEBUG
        Debug.Assert(!m_fLockMethodList);
        m_fLockMethodList = true;
        #endif
        
        // Resolve all methods, do some inheritence checks too:
        // - if a class has any abstract members, it must be abstract        
        bool fHasAbstractMember = false;
        foreach(MethodDecl m in m_alMethods)
        {
            // set provider=null since we have to tweak members based off inheritence checks
            m.ResolveMember(m_symbol, s, null);            
            if (m.Mods.IsAbstract)
                fHasAbstractMember = true;
        }
        
        Debug.Assert(!fHasAbstractMember || !this.IsStruct, "Only classes / interfaces can have abstract members");
        
        // If we have any abstract members, then the class must be abstract
        if (fHasAbstractMember && !this.Mods.IsAbstract)            
            PrintError(SymbolError.ClassMustBeAbstract(this));
                        
        
        // Do some checks that will modify how we create the CLR type.
        if (!IsInterface)
        {            
            // Make sure that we implement all methods from all the interfaces
            // that we inherit from
            foreach(TypeEntry t in this.Symbol.BaseInterfaces)
                EnsureMethodsImplemented(s, t);
        }
                
        foreach(MethodDecl m in m_alMethods)
        {            
            m.Symbol.SetInfo(provider);
            // If a method is 'override', an exact match must exist in a super clas that:
            //      a) is marked 'override' also
            //      b) is marked 'virtual'
            if (m.Mods.IsOverride)
            {
                TypeEntry t = this.Symbol.Super;
                                
                MethodExpEntry mSuper = t.LookupExactMethod(m.Symbol); // search super classes
                if (mSuper == null)                    
                {                    
                    PrintError(SymbolError.NoMethodToOverride(m.Symbol));
                }                
                                
                // Further checks if we found an method that matched sigs
                else
                {
                    // Get the CLR representaitons for this & the super
                    System.Reflection.MethodBase i1 = m.Symbol.Info;
                    System.Reflection.MethodBase i2 = mSuper.Info;                    
                    
                    // if final, error
                    if (i2.IsFinal)
                    {   
                        PrintError(SymbolError.CantOverrideFinal(m.Symbol, mSuper));
                    }
                        
                    // If !virtual, error
                    if (!i2.IsVirtual)
                    {   
                        PrintError(SymbolError.CantOverrideNonVirtual(m.Symbol, mSuper));
                    }
                    
                    // Check same visibility access                                        
                    bool fMatch = 
                        (i1.IsPublic == i2.IsPublic) &&
                        (i1.IsPrivate == i2.IsPrivate) &&
                        (i1.IsFamily == i2.IsFamily) &&
                        (i1.IsAssembly == i2.IsAssembly);
                    if (!fMatch)
                    {   
                        PrintError(SymbolError.VisibilityMismatch(m.Symbol));
                    }                        
                }                
            } // IsOverride
        } // foreach Method
    }
    
    void FixCtors(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        // Add default ctor
        bool fFoundCtor = m_symbol.HasMethodHeader(Name);
            
        // Note that structs don't have a default ctor, but can have other ctors                        
        // But add a default ctor for classes if we don't have one.
        if (!fFoundCtor && IsClass)
        {                
            CtorChainStatement stmtChain = new CtorChainStatement();
            Modifiers mods = new Modifiers();
            mods.SetPublic();

            MethodDecl mdecl = new MethodDecl(
                new Identifier(Name, this.Location), 
                null, 
                null, 
                new BlockStatement(new LocalVarDecl[0], new Statement[] { stmtChain }), 
                //new AST.Modifiers(AST.Modifiers.EFlags.Public)
                mods
                );

            stmtChain.FinishInit(mdecl);
                
            mdecl.ResolveMember(m_symbol,s, null);
            mdecl.Symbol.SetInfo(provider);
                
            // Add to the end of the m_alMethods array so that we get codegen'ed!
            AddMethodToList(mdecl);
                
            Debug.Assert(m_symbol.HasMethodHeader(Name));
        }

        // @todo - perhaps we could just make the static initializer a static-ctor..
        // If we don't have a static ctor, but we do have static data, then add
        // a static ctor
        if (m_nodeStaticInit != null) 
        {
            bool fFoundStaticCtor = false;
            foreach(MethodDecl m in m_alMethods)
            {
                if (m.Mods.IsStatic && m.IsCtor)
                {
                    fFoundStaticCtor = true;
                    break;
                }
            }

            if (!fFoundStaticCtor)
            {
                Modifiers mods = new Modifiers();
                mods.SetStatic();
                mods.SetPublic();
                    
                MethodDecl mdecl2 = new MethodDecl(
                    new Identifier(Name, this.Location),
                    null,
                    null,
                    new BlockStatement(null, new Statement[]{}),
                    //new Modifiers(AST.Modifiers.EFlags.Static | Modifiers.EFlags.Public)
                    mods
                    );
                mdecl2.ResolveMember(m_symbol, s, null);
                mdecl2.Symbol.SetInfo(provider);
                AddMethodToList(mdecl2);
            }
        } // end check static ctor
    } // fix ctors
    
    // Resolve all events in the class
    void FixEvents(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        if (this.IsStruct)
            return;
        
        if (Events == null)
            return;
            
        foreach(EventDecl e in this.Events)
        {
            e.ResolveMember(m_symbol, s, provider);          
        }            
    }
    
    // Called after all the methods are decled.
    void PostFixEvents(
        ISemanticResolver s,
        ICLRtypeProvider provider
        )
    {   
        #if DEBUG
        Debug.Assert(m_fLockMethodList, "Don't PostResolve Events until after methods");
        #endif     
        
        if (Events == null)
            return;
            
        foreach(EventDecl e in this.Events)
        {
            e.PostResolveMember(s);
            //  e.FieldSymbol.SetInfo(provider);
        }            
    }
            
    // Resolve the members (fields & methods)
    public override void ResolveMemberDecls(
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {   
        // If we've already been resolved, then skip
        if (this.Symbol.MemberScope.IsLocked)
            return;
            
        // We need our base types to have resolved members so that we can 
        // do inheritence checks.            
        FixBaseMemberDecls(s, provider);    
        
        // Setup our context to do evaluation
        Scope prev = s.SetCurrentContext(m_symbol.MemberScope);
        s.SetCurrentClass(m_symbol);            
                
        // Add our members to our scope
        {
            // Do events first because they generate methods & fields
            FixEvents(s, provider);
        
            if (!IsInterface)
                this.FixFields(s, provider);
            
            // Resolve properties before methods, since properties create methods
            foreach(PropertyDecl p in m_alProperties)
            {
                p.ResolveMember(m_symbol, s, provider);                            
            }
            
            // This can change both the normal methods as well as accessor methods on properties.
            FixMethods(s, provider);
            
            // Set property symbols after we've touched up the methods.
            foreach(PropertyDecl p in m_alProperties)
            {
                p.Symbol.SetInfo(provider);
            }
            
            PostFixEvents(s, provider);
                  
            // Nested types
            foreach(TypeDeclBase t in this.m_alNestedTypes)
            {
                t.ResolveMemberDecls(s, provider);        
            }              
                   
                    
            // If we have no ctor at all, then add a default one.
            // (default ctor - takes no parameters, chains to base's default ctor)
            if (!IsInterface)
                FixCtors(s, provider);
            
            // We're done adding to the scope, so lock it.
            m_symbol.MemberScope.LockScope();
        }
        
        s.SetCurrentClass(null);        
        s.RestoreContext(prev);
    }
            
            
    // @todo - this is close, but wrong. See the C# specs on "Interface Mapping" for details.
                
    // Make sure that this class implements all methods on the interfaces it inherits.
    // Since an interface may inherit interfaces, we have to call recursively on the 
    // interface tree to make sure we get them all.
    // Since properties are converted to methods, this will catch those too.
    void EnsureMethodsImplemented(ISemanticResolver s, TypeEntry tInterface)
    {
        Debug.Assert(tInterface.IsInterface);
        
        // Enforce that the type we're searching has indeed been fully populated.
        // Can't let anything slip through the cracks by adding a method after
        // we do this check...
        Debug.Assert(tInterface.MemberScope.IsLocked);
        
        string stMethod;
        TypeEntry t = this.Symbol;
        // For each method in this interface, look it up in the class to 
        // make sure it's implemented
        foreach(SymEntry sym in tInterface.MemberScope)
        {
            if (!(sym is MethodExpEntry))
                continue;
            MethodExpEntry mInterface = sym as MethodExpEntry;
            
            #if DEBUG
            stMethod = mInterface.PrettyDecoratedName; // helpful for debugging
            #endif
            
            MethodExpEntry m = t.LookupInterfaceMethod(mInterface, false);
            if (m == null)
            {   
                // @todo - you're an idiot. Lookup in the C# spec and do it right.
                // @todo - This comment below is garbage. 
                // There's one crazy little case where an error is too conservative:
                // IA               IA.f()
                // CA : IA          CA.f()
                // CA2 : CA, IA2
                // CA2 does not define f(), but it inherits CA.f(). Normally this is 
                // not ok, but since CA implements IA, it is.
                // So if we're missing, check for it in our super class
                
                // @todo - do we have to make sure that CA inherits from IA?
                m = t.LookupInterfaceMethod(mInterface, true);
                
                if (m == null)
                    ThrowError(SymbolError.MissingInterfaceMethod(this.Location, mInterface, t));
            } else {
                if (!m.Node.Mods.IsPublic)                                                     
                    ThrowError(SymbolError.IMethodMustBePublic(this.Location, mInterface, t));
                    
                // If there's no 'virtual' / 'abstract' on an interface method, 
                // then it's implicitily virtual & sealed.
                if (!m.Node.Mods.IsVirtual && !m.Node.Mods.IsAbstract)
                {
                    m.Node.MarkAsVirtual();
                    m.Node.MarkAsFinal();
                }    
            }
        }
        
        // Ensure that we implement all the methods that the interface inherits
        foreach(TypeEntry tBase in tInterface.BaseInterfaces)
            EnsureMethodsImplemented(s, tBase);
    }
    
    
    // Helper to add automatically generated methods to the method list
    // Useful for properties, default ctors, and events
    public void AddMethodToList(MethodDecl m)
    {
        #if DEBUG
        // Ctor can skip this check since we explicitly resolve it and it can't
        // have any conflicting inheritence checks
        if (!m.IsCtor)
        {
            Debug.Assert(!m_fLockMethodList, "Can't add members after we start resolving");
        }
        
        // Make sure method isn't already in scope
        foreach(MethodDecl m2 in m_alMethods)
        {            
            Debug.Assert(m != m2, "Method '" + m.Name + "' already added");    
            Debug.Assert(m.Name != m2.Name, "Method name '" + m.Name + "' already added. Shouldn't add overload");        
        }
        #endif
    
        MethodDecl [] temp = new MethodDecl[m_alMethods.Length + 1];
        temp[0] = m;
        m_alMethods.CopyTo(temp, 1);
        m_alMethods = temp;
    }
#endregion Resolve decls

#region Resolve Bodies    
// Resolve the bodies of methods
    public override void ResolveBodies(ISemanticResolver s)
    {
        Scope scope = m_symbol.MemberScope;
        
        //s.PushScope(scope);
        Scope prev = s.SetCurrentContext(scope);
        s.SetCurrentClass(m_symbol);
         
        foreach(MethodDecl m in m_alMethods)
        {
            m.ResolveBodies(s);
        }
        
        foreach(PropertyDecl p in m_alProperties)
        {
            p.ResolveBodies(s);
        }
        
        // Nested
        foreach(TypeDeclBase t in this.m_alNestedTypes)
        {
            t.ResolveBodies(s);
        }
        
        s.SetCurrentClass(null);
        //s.PopScope(scope);
        s.RestoreContext(prev);
    }
#endregion Resolve Bodies

#endregion Resolution
    
    
    public override void GenerateType(Blue.CodeGen.EmitCodeGen gen)
    {
        gen.GenerateClass(this);
    }
    
}
#endregion // Class

#endregion

#region AST nodes for Class Members (Fields, Properties & Methods)
//-----------------------------------------------------------------------------	
// Declare a member of a class
// @todo - should this be split?
//-----------------------------------------------------------------------------
public abstract class MemberDecl : Node
{ 
// Resolve the member given a symbol for the class we're defined in, a resolver, 
// and a provider
    public abstract void ResolveMember(
        TypeEntry symDefiningClass, 
        ISemanticResolver s, 
        ICLRtypeProvider provider
    );
    
// Only a derived class can set    
    protected Modifiers m_mods;
    public Modifiers Mods
    {
        get { return m_mods; }
    }
}

//-----------------------------------------------------------------------------
// Modifiers for all language constructs.
// @todo - have different types of modifiers for each component?
// We don't expose the internal flags. Rather we force anyonet go through
// Setter methods so that we can verify the flags are always valid.
// (ex, Can't set public & private together).
//-----------------------------------------------------------------------------
public struct Modifiers
{   
#region Constructors
    // Ctor to set the modifiers based off an imported method
    public Modifiers(System.Reflection.MethodBase m)
    {   
        // We must do this assignment to make this instance 'fully assigned'.
        // Unless an instance of a struct is 'fully assigned', we can't call
        // methods on it.
        
        // @dogfood - '0' as an integer is implicitly assignable to an enum. Shouldn't have to cast.
        this.m_attrs = (AST.Modifiers.EFlags) 0;
            
        // Inheritence
        if (m.IsAbstract)
        {
            Debug.Assert(m.IsVirtual);
            this.SetAbstract();            
        }
        
        // BlueModifiers can be 'virtual abstract' or 'override abstract'
        // Note that 'abstract' --> 'virtual abstract'
        if (m.IsVirtual)
        {
            if ((m.Attributes & System.Reflection.MethodAttributes.NewSlot) != 0)
                this.SetVirtual();
            else 
                this.SetOverride();
        }
    
        if (m.IsStatic)
            this.SetStatic();
    
        if (m.IsFinal)
            this.SetSealed();
        
        // Visibility            
        if (m.IsPublic)
            this.SetPublic();
    
        if (m.IsPrivate)
            this.SetPrivate();
        
        if (m.IsFamily)
            this.SetProtected();

        if (m.IsAssembly)
            this.SetInternal();                                                            
        
    
    }
#endregion
// Attributes on members of a class
    //[FlagsAttribute()]
    enum EFlags {
        None        = 0x00000000,
        
    // Access        
        Public          = 0x00000001,
        Private         = 0x00000002,
        Protected       = 0x00000003,
        Internal        = 0x00000004,
        
        VisibilityMask  = 0x0000000F,
        
    // Static vs Instance (default);        
        Static      = 0x00000010,
        
    // Inheritence        
        Virtual     = 0x00000020,
        Abstract    = 0x00000040,
        Override    = 0x00000080,
        Sealed      = 0x00000100,
        
        Const       = 0x00000200,
        ReadOnly    = 0x00000400,
        New         = 0x00000800,
    }
    
    // Set in derived ctor
    private EFlags m_attrs;

#region Helpers
    public static bool operator==(Modifiers a, Modifiers b)
    {
        return a.m_attrs == b.m_attrs;
    }
    
    public static bool operator!=(Modifiers a, Modifiers b)
    {
        return a.m_attrs != b.m_attrs;
    }
    
    public override int GetHashCode()
    {
        // Negate this just in case anyone had the crazy idea of 
        // using the hashcode to get the flag bits ...
        return - ((int) m_attrs);
    }
    
    public override bool Equals(object o)
    {
        if (!(o is Modifiers)) return false;
        return ((Modifiers) o) == this;
    }
    
    // Convert to a string.     
    public override string ToString()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        
        if (this.IsPublic)      sb.Append("public ");
        if (this.IsProtected)   sb.Append("protected ");
        if (this.IsPrivate)     sb.Append("private ");
        if (this.IsInternal)    sb.Append("internal ");
        
        if (this.IsStatic)      sb.Append("static ");
        if (this.IsVirtual)     sb.Append("virtual ");
        if (this.IsOverride)    sb.Append("override ");
        if (this.IsAbstract)    sb.Append("abstract ");
        
        if (this.IsConst)       sb.Append("const ");
        if (this.IsReadOnly)    sb.Append("readonly ");
        if (this.IsNew)         sb.Append("new ");
        
        if (this.IsSealed)      sb.Append("sealed");
        
        return sb.ToString();
    }
#endregion

#region Visibility   
    // All visibility options are mutually-exclusive
    public bool IsPublic
    {
        get { return (m_attrs & EFlags.VisibilityMask) == EFlags.Public; }
    }
    
    public bool IsProtected
    {
        get { return (m_attrs & EFlags.VisibilityMask) == EFlags.Protected; }
    }
    
    public bool IsPrivate
    {
        get { return (m_attrs & EFlags.VisibilityMask) == EFlags.Private; }
    }
    
    public bool IsInternal
    {
        get { return (m_attrs & EFlags.VisibilityMask) == EFlags.Internal; }
    }
    
    public bool VisibilityNotSet
    {
        get { return (m_attrs & EFlags.VisibilityMask) == 0; }
    }
#endregion 

#region Set Visibility
    internal void SetPublic()
    {
        Debug.Assert(VisibilityNotSet);
        m_attrs |= EFlags.Public;
    }
    
    internal void SetProtected()
    {
        Debug.Assert(VisibilityNotSet);
        m_attrs |= EFlags.Protected;
    }
    
    internal void SetPrivate()
    {
        Debug.Assert(VisibilityNotSet);
        m_attrs |= EFlags.Private;
    }
    
    internal void SetInternal()
    {
        Debug.Assert(VisibilityNotSet);
        m_attrs |= EFlags.Internal;
    }
#endregion

    // Static is exclusive with (Virtual / Override) members 
    public bool IsStatic
    {
        get { return (m_attrs & EFlags.Static) != 0; }
    }
    
    internal void SetStatic()
    {
        Debug.Assert(!IsVirtual && !IsOverride);
        m_attrs |= EFlags.Static;
    }
#region Inheritence    
    // Virtual is exclusive with Override
    public bool IsVirtual
    {
        get { return (m_attrs & EFlags.Virtual) != 0; }
    }
    
    // Exclusive with Virtual
    public bool IsOverride
    {
        get { return (m_attrs & EFlags.Override) != 0; }
    }

    // Exclusive with Sealed, Static; only valid with (Virtual or Override)
    public bool IsAbstract
    {
        get { return (m_attrs & EFlags.Abstract) != 0; }
    }
    
    // Sealed only applies to Types. Exclusive with Abstract
    public bool IsSealed
    {
        get { return (m_attrs & EFlags.Sealed) != 0; }
    }
    
    public bool IsNew
    {
        get { return (m_attrs & EFlags.New) != 0; }        
    }
    
    public bool IsReadOnly
    {
        get { return (m_attrs & EFlags.ReadOnly) != 0; }
    }
    
    public bool IsConst
    {
        get { return (m_attrs & EFlags.Const) != 0; }
    }   
    
#endregion  

#region Set Inheritence
    internal void SetVirtual()
    {
        Debug.Assert(!IsOverride && !IsStatic && !IsSealed);
        m_attrs |= EFlags.Virtual;
    }
    
    internal void SetOverride()
    {
        Debug.Assert(!IsVirtual && !IsStatic && !IsSealed);
        m_attrs |= EFlags.Override;
    }
    
    internal void SetAbstract()
    {
        Debug.Assert(!IsSealed);
        m_attrs |= EFlags.Abstract;   
    }
    
    internal void SetSealed()
    {
        Debug.Assert(!IsAbstract);
        m_attrs |= EFlags.Sealed;
    }
    
    internal void SetNew()
    {
        m_attrs |= EFlags.New;
    }
    
    internal void SetConst()
    {
        m_attrs |= EFlags.Const;
    }
    
    internal void SetReadOnly()
    {
        m_attrs |= EFlags.ReadOnly;
    }
#endregion  
}

#region Event declaration
//-----------------------------------------------------------------------------
// Events are fields
//-----------------------------------------------------------------------------
public class EventDecl : MemberDecl
{
    public EventDecl(
        Identifier idName,
        NonRefTypeSig tType,
        Modifiers mods
    )
    {
        Debug.Assert(idName != null);
        Debug.Assert(tType != null);
        
        m_idName    = idName;
        m_tType     = tType;
        m_mods      = mods;    
    }              
    
#region Properties & Data
    Identifier m_idName;
    public Identifier Name
    {
        get { return m_idName; }
    }
    
    NonRefTypeSig m_tType;
    public NonRefTypeSig EventType
    {
        get { return m_tType; }   
    }    
    
    EventExpEntry m_symbol;
    public EventExpEntry Symbol
    {
        get { return m_symbol; }
    }
    /*
    FieldExpEntry m_field;
    public FieldExpEntry FieldSymbol 
    {
        get { return m_field; }
    }
    */
    
    MethodDecl m_addMethod;
    MethodDecl m_removeMethod;
    
#endregion    

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        m_tType.DebugCheck(s);
    }
    
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("EventDecl");
        m_tType.Dump(o);
        o.WriteEndElement();
    }
#endregion

#region Resolution    
    
    // Called after member decls have been resolved
    public void PostResolveMember(
        ISemanticResolver s
    )
    {
        Debug.Assert(m_symbol != null);
        Debug.Assert(m_addMethod.Symbol != null);
        Debug.Assert(m_removeMethod.Symbol != null);
        
        m_symbol.SetAddMethod(m_addMethod.Symbol);
        m_symbol.SetRemoveMethod(m_removeMethod.Symbol);        
    }

    // Resolve the events
    public override void ResolveMember(
        TypeEntry symDefiningClass, 
        ISemanticResolver s,
        ICLRtypeProvider provider        
        )
    {
        m_tType.ResolveType(s);
        
        m_symbol = new EventExpEntry(symDefiningClass, this);
        s.GetCurrentContext().AddSymbol(m_symbol);
        
        // If an event has no handlers
        if (m_addMethod == null && m_addMethod == null)
        {
            FixDefaultHandlers(symDefiningClass, s, provider);        
        }
        
        
        Debug.Assert(m_addMethod != null && m_removeMethod != null);
        Debug.Assert(m_addMethod.Symbol == null); // shouldn't have resolved handlers yet
        Debug.Assert(m_removeMethod.Symbol == null); 
        
        // The handlers should be treated just like normal methods..
        ClassDecl c = symDefiningClass.Node;
        c.AddMethodToList(m_addMethod);
        c.AddMethodToList(m_removeMethod);
        
               
    }
    
    // Return a created add/remove default handler.
    // A default event handler uses a private delegate.
    #if false                     
        // Code for default handler
        void <stHandlerName>(D thing) {
            <idDelegate> =  (D) System.MulticastDelegate.Combine(<idDelegate>, thing);
        }  
    #endif
    MethodDecl CreateHandler(string stHandlerName, string stAction, Identifier idDelegate, ISemanticResolver s)    
    {
        Debug.Assert(stAction == "Combine" || stAction == "Remove", "'" + stAction + "' is bad");
        
        // Create the default Add & Remove handlers
        TypeSig tVoid = new ResolvedTypeSig(typeof(void), s);             
                        
        DotObjExp e_System_MulticastDelegate = new DotObjExp(
            new SimpleObjExp("System"),
            new Identifier("MulticastDelegate")
            );
            
        Identifier idX = new Identifier("thing");
        SimpleObjExp expX = new SimpleObjExp(idX);
        
        SimpleObjExp expDelegate = new SimpleObjExp(idDelegate);
                
        return new MethodDecl(
            new Identifier(stHandlerName),
            tVoid,
            new ParamVarDecl[] {
                new ParamVarDecl(idX, m_tType, EArgFlow.cIn)
            },
            new BlockStatement(
                new LocalVarDecl[0],
                new Statement[] {
                    new ExpStatement(
                        new AssignStmtExp(
                            expDelegate,
                            new CastObjExp(
                                m_tType, // type of delegate that 
                                new MethodCallExp(
                                    e_System_MulticastDelegate,
                                    new Identifier(stAction),                                 
                                    new ArgExp[] {
                                        new ArgExp(EArgFlow.cIn, expDelegate),
                                        new ArgExp(EArgFlow.cIn, expX)                                
                                    }
                                ) // end method call
                            ) // cast
                        ) // assignment exp
                    ) // assignment statement
                } // end Statement body
            ),
            this.Mods
        );
    }
    
    // If no add/remove handlers are specified, then the compiler (that's us!) supplies
    // default add & remove that operate on a delegate
    void FixDefaultHandlers(
        TypeEntry symDefiningClass,
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        
        Modifiers modsDelegate = new Modifiers();
        modsDelegate.SetPrivate();
        if (this.Mods.IsStatic)
            modsDelegate.SetStatic();
        
        string stEventName = m_idName.Text;    
        Identifier idDelegate = new Identifier("." + stEventName, m_idName.Location);            
        
        // For default handlers, the compiler makes a private field of a delegate
        // to provide storage for the event.
        FieldDecl f = new FieldDecl(idDelegate, m_tType, modsDelegate, null);
        f.ResolveMember(symDefiningClass, s, provider);        
        m_symbol.SetDefaultField(f.Symbol);
          
        m_addMethod = this.CreateHandler("add_" + stEventName, "Combine", idDelegate, s);
        m_removeMethod = this.CreateHandler("remove_" + stEventName, "Remove", idDelegate, s);
                    

    }
    
#endregion
}


#endregion Events

//-----------------------------------------------------------------------------
// Fields
//-----------------------------------------------------------------------------
public class FieldDecl : MemberDecl
{
#region Construction
    public FieldDecl(
        Identifier idName, 
        TypeSig tType,
        Modifiers mods,
        Exp exp // optional, may be null
    )
    {
        m_stName = idName.Text;
        m_tType = tType;        
        m_mods = mods;
        m_expInit = exp;

        Debug.Assert(m_tType != null);
        
        //m_filerange = FileRange.Merge(tType.Location, idName.Location);
    }
#endregion

#region Checks    
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_stName != null);
        
        Debug.Assert(m_tType != null);
        m_tType.DebugCheck(s);
                
    // Validate symbol
        Debug.Assert(Symbol != null);        
        Debug.Assert(Symbol.Name == m_stName);
        Debug.Assert(Symbol.Node == this);
        Debug.Assert(Symbol.m_type == m_tType.BlueType);
        
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("FieldDecl");
        o.WriteAttributeString("name", m_stName);
                
        if (m_symbol != null)             
            o.WriteAttributeString("symbol", m_symbol.ToString());                   
        
        m_tType.Dump(o);
                   
        o.WriteEndElement();
    }   
#endregion 

#region Properties & Data    
    private TypeSig m_tType;
    public TypeSig FieldTypeSig
    {
        get { return m_tType; }    
    }
               
    readonly string m_stName;
    public string Name
    {
        get { return m_stName; }
    }
    
    protected FieldExpEntry m_symbol;
    public FieldExpEntry Symbol
    {
        get { return m_symbol; }
    }

    protected Exp m_expInit;
    public Exp InitialExp
    {
        get { return m_expInit; }
    }
#endregion    

    // Resolve member
    public override void ResolveMember(
        TypeEntry symDefiningClass, 
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        m_tType.ResolveType(s);
        TypeEntry t = m_tType.BlueType;
        
        m_symbol = new FieldExpEntry(m_stName, t, symDefiningClass, this);
        s.GetCurrentContext().AddSymbol(m_symbol);
        m_symbol.SetInfo(provider);
    }

    public void ResolveMemberAsLiteral(TypeEntry symDefiningClass, ISemanticResolver s, object o)
    {
        //f.FieldTypeSig.ResolveType(s);

        m_tType.ResolveType(s);
        TypeEntry t = m_tType.BlueType;

        m_symbol = new LiteralFieldExpEntry(Name, t, symDefiningClass, this);                    
        LiteralFieldExpEntry l = (LiteralFieldExpEntry) m_symbol;
        l.Data = o;

        s.GetCurrentContext().AddSymbol(m_symbol);
    }
}

//-----------------------------------------------------------------------------    
// Declare a property
// Properties are like smart fields
//-----------------------------------------------------------------------------    

public class PropertyDecl : MemberDecl
{
#region Construction
    // Name & type can't be null.
    // Must have at least Get or  Set be non-null
    public PropertyDecl(
        Identifier idName, TypeSig tType,        
        BlockStatement stmtGet, bool fHasGet,
        BlockStatement stmtSet, bool fHasSet,
        Modifiers mods
        ) : 
        this(
            idName, tType, 
            null,
            stmtGet, fHasGet,
            stmtSet, fHasSet,
            mods)            
    {
    
    }
    public PropertyDecl(
        Identifier idName, TypeSig tType,
        ParamVarDecl param, // optional param, may be null
        BlockStatement stmtGet, bool fHasGet,
        BlockStatement stmtSet, bool fHasSet,
        Modifiers mods
    )
    {
        m_stmtGet = stmtGet;
        m_stmtSet = stmtSet;
        
        m_idName = idName;
        m_tType = tType;
        
        Debug.Assert(idName != null);
        Debug.Assert(tType != null);
        Debug.Assert(fHasGet || fHasSet);
                
        m_mods = mods;
        
        //m_expParam = expParam;
                
        // Spoof bodies. Note that we just pass the attributes (static, virtual)
        // right to the new methods. Also, if we're abstract, the XXXStmt will be null
        // and the methods will just deal with that too.
        if (fHasGet)
        {
            Debug.Assert(mods.IsAbstract ^ (stmtGet != null));
        
            // T get_XXX();
            Identifier idName2 = new Identifier("get_"+Name.Text, m_idName.Location);
            m_declGet = new MethodDecl(
                idName2,
                m_tType,
                (param == null) ? 
                    new ParamVarDecl[0] : 
                    new ParamVarDecl[] { param }, 
                GetStmt, 
                mods
            );
        }
        
        if (fHasSet)
        {
            Debug.Assert(mods.IsAbstract ^ (stmtSet != null));
            
            // void set_XXX(T value);
            Identifier idP1 = new Identifier("value", new FileRange());
            
            ParamVarDecl T = new ParamVarDecl(idP1, (NonRefTypeSig) m_tType, EArgFlow.cIn);
            
            ParamVarDecl [] p = (param == null) ? 
                new ParamVarDecl[] { T } :
                new ParamVarDecl[] { param, T };
            
            Identifier idName2 = new Identifier("set_"+Name.Text, m_idName.Location);
            AST.TypeSig tVoid = new SimpleTypeSig(new SimpleObjExp(new Identifier("void", new FileRange())));
            //AST.TypeSig tVoid = new ResolvedTypeSig(typeof(void), s);
            m_declSet = new MethodDecl(idName2, tVoid,p, SetStmt, mods);
        }        
    }
#endregion

#region Data    
    BlockStatement m_stmtGet; // these may be null.
    BlockStatement m_stmtSet;
    TypeSig m_tType;
    Identifier m_idName;
    SymbolEngine.PropertyExpEntry m_symbol;
    
    // Interal references to hold spoofed bodies for accessors
    MethodDecl m_declGet;
    MethodDecl m_declSet;
    
    
    //Exp m_expParam;    
#endregion    
    
#region Properties    
    public SymbolEngine.PropertyExpEntry Symbol
    {
        get { return m_symbol; }
    }
    
    public Identifier Name
    {
        get { return m_idName; }
    }
    
    public TypeEntry BlueType
    {
        get { return m_tType.BlueType; }
    }
    
    public BlockStatement GetStmt
    {
        get { return m_stmtGet; }
    }
    
    public BlockStatement SetStmt
    {
        get { return m_stmtSet; }
    }
    
    public bool HasGet
    {
        get { return m_declGet != null; }
    }
    
    public bool HasSet
    {
        get { return m_declSet != null; }
    }
      
#endregion    

    // Resolve this
    public override void ResolveMember(
        TypeEntry symDefiningClass, 
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        m_tType.ResolveType(s);
        
    // Spoof bodies
                
        if (HasGet)
        {                        
            m_declGet.ResolveMember(symDefiningClass, s, null);
            m_declGet.Symbol.IsSpecialName = true;
        }
        if (HasSet)
        {   
            m_declSet.ResolveMember(symDefiningClass, s, null);
            m_declSet.Symbol.IsSpecialName = true;
        }
    
    // Create a symbol for the property
        m_symbol = new SymbolEngine.PropertyExpEntry(symDefiningClass, this, m_declGet, m_declSet);
        
        s.GetCurrentContext().AddSymbol(m_symbol);
    }
    
    // Context sensitive
    public void ResolveBodies(ISemanticResolver s)
    {
        // Resolve each accessor in the same context as the property
        if (m_declSet != null)
            m_declSet.ResolveBodies(s);
            
        if (m_declGet != null)
            m_declGet.ResolveBodies(s);
    }


#region Checks    
    public override void DebugCheck(ISemanticResolver s)
    {
                
    }
    
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("PropertyDecl");
        o.WriteAttributeString("name", this.m_idName.Text);
        
        if (GetStmt != null)
        {
            o.WriteStartElement("get");
            GetStmt.Dump(o);
            o.WriteEndElement();
        }
        
        if (SetStmt != null)
        {
            o.WriteStartElement("set");
            SetStmt.Dump(o);
            o.WriteEndElement();
        }
        
        o.WriteEndElement();        
    }
#endregion   

    public void Generate(Blue.CodeGen.EmitCodeGen gen)
    {   
        if (!Mods.IsAbstract)
        {
            if (m_declSet != null)
                gen.Generate(m_declSet);            
                
            if (m_declGet != null)
                gen.Generate(m_declGet);
        }            
    }        
}

//-----------------------------------------------------------------------------    
// Declare a Method / Constructor
// - iif we're a ctor, then we have no return type
// - iif we're abstract (includes everything on an interface) then we have
// no bodystatement
//-----------------------------------------------------------------------------
public class MethodDecl : MemberDecl, ILookupController
{    
#region Construction

    // For members on an interface
    public
    MethodDecl(
        Identifier idName,        
        TypeSig tRetType,
        ParamVarDecl[] arParams
    )
    {
        //m_strName       = idName.Text;
        m_idName        = idName;
        m_tRetType      = tRetType;
        
        m_mods          =   new Modifiers();
        m_mods.SetAbstract();
        m_mods.SetVirtual();
        m_mods.SetPublic(); 
        
        m_arParams      = (arParams != null) ? arParams : new ParamVarDecl[0];        
        m_stmtBody      = null;

        Debug.Assert(m_idName != null);
        Debug.Assert(m_arParams != null);        
        
        // @todo - this is wrong
        m_filerange = idName.Location;
    }

    // For normal methods
    public
    MethodDecl(
        Identifier idName,
        TypeSig tRetType,            
        ParamVarDecl[] arParams,
        BlockStatement stmtBody,
        Modifiers mods
        )
    {
        //m_strName       = idName.Text;
        m_idName        = idName;
        m_tRetType      = tRetType;
        
        m_mods          = mods;
        if (m_mods.IsAbstract && !m_mods.IsOverride)
            m_mods.SetVirtual();
        
        m_arParams      = (arParams != null) ? arParams : new ParamVarDecl[0];        
        m_stmtBody      = stmtBody;

        Debug.Assert(m_idName != null);        
        Debug.Assert((m_stmtBody != null) ^ mods.IsAbstract);
        Debug.Assert(m_arParams != null);        
        
        // @todo - this is wrong
        m_filerange = idName.Location;
    }
    
    // For overloaded operators
    // No modifiers needed since op overloading must be public & static.
    // Have a special constructor so that we can set the IsOp flag and
    // get a safe string name. 
    public MethodDecl(
        BinaryExp.BinaryOp op,
        TypeSig tRetType,
        ParamVarDecl[] arParams,
        BlockStatement stmtBody
    )
    {
        m_fIsOpOverload = true;
        string strName  = GetOpOverloadedName(op);
        m_idName        = new Identifier(strName, tRetType.Location);
        m_tRetType      = tRetType;
        m_arParams      = (arParams != null) ? arParams : new ParamVarDecl[0];        
        m_stmtBody      = stmtBody;
        
        //m_mods          = new Modifiers(Modifiers.EFlags.Public | Modifiers.EFlags.Static);
        m_mods = new Modifiers();
        m_mods.SetPublic();
        m_mods.SetStatic();
        
        Debug.Assert(m_idName != null);        
        Debug.Assert(m_stmtBody != null);
        Debug.Assert(m_arParams != null);
    }
    
#endregion    
    
#region ILookupController
    public SymEntry SmartLookup(string stIdentifier, Scope scope)
    {
        return scope.LookupSymbolInThisScopeOnly(stIdentifier);
    }
    
    // Get a node responsible for this scope.
    // For imported types, this will be null
    public AST.Node OwnerNode { 
        get { return this; }
    }
    
    // Get a symbol responsible for this scope. 
    // This may be null. (If this is null, then OwnerNode should not be null).
    public SymEntry OwnerSymbol { 
        get { return this.Symbol; }
    }
    
    // For debugging purposes. Used by DumpTree();
    public void DumpScope(Scope scope)
    {
        return;
    }
#endregion
    
    
#region Checks

    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("{0}", this.Mods.ToString());
        if (IsCtor)
        {
            sb.Write("(ctor) ");
        }
        else {
            sb.Write("{0} ", this.m_tRetType.ToString());
        }
        sb.WriteLine(this.Name);
                
        // Params
        sb.WriteLine("(");
        sb.Indent++;
        bool fFirst = true;
        foreach(ParamVarDecl p in m_arParams)
        {
            if (!fFirst)
                sb.WriteLine(',');
            p.ToSource(sb);
            fFirst = false;
        }
        sb.WriteLine();
        
        sb.Indent--;
        sb.WriteLine(")");
        
        // Body        
        this.m_stmtBody.ToSource(sb);        
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("MethodDecl");
        o.WriteAttributeString("name", Name);
        
        if (m_symbol != null)
            o.WriteAttributeString("symbol", m_symbol.ToString());
        
        if (IsCtor)
        {
            o.WriteAttributeString("IsCtor", "yes");
        } 
        else 
        {
            o.WriteAttributeString("rettype", m_tRetType.ToString());
        }
        
        foreach(ParamVarDecl param in m_arParams)
        {
            param.Dump(o);
        }

        if (!Mods.IsAbstract)
        {
            m_stmtBody.Dump(o);
        }
                          
        o.WriteEndElement();
    }
    
//-----------------------------------------------------------------------------
// Debugging check
//-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        //Node.DumpSource(this);
    
    
        Debug.Assert(m_idName != null);        
        Debug.Assert(IsCtor || (m_tRetType != null));
                
        Debug.Assert(Mods.IsAbstract == (Body == null));

        Debug.Assert(Symbol != null);
        Debug.Assert(Symbol.Node == this);
        Debug.Assert(m_DbgAllowNoCLRType || (Symbol.Info != null));
        
        Debug.Assert(Symbol.SymbolClass != null);
        Debug.Assert(Symbol.m_scope != null);
        
        
        
        
        if (IsCtor)
        {
            // static ctors are renamed, but non-statics should have same name as class
            if (!m_mods.IsStatic)
                Debug.Assert(Symbol.SymbolClass.Name == this.Name);            
        } else {
            m_tRetType.DebugCheck(s);   
            Debug.Assert(Symbol.RetType == m_tRetType.BlueType);                        
        }
        
        Debug.Assert(Params != null);              
                                
        Type [] alParams = new Type[Params.Length];        
        int i = 0;  
        
        foreach (ParamVarDecl p in Params)
        {   
            p.DebugCheck(s);
            alParams[i] = p.Symbol.m_type.CLRType;
            i++;
        }
        
        // Verify that this method is actually in the scope that it thinks it is
        // Note that the lookup may assert, and we don't want asserts nested in asserts
        // so store it into a local
        bool f;
        MethodExpEntry m  = Symbol.SymbolClass.LookupMethod(s, m_idName, alParams, out f);
        Debug.Assert(m == this.Symbol);
        
        if (!Mods.IsAbstract)
        {
            Body.DebugCheck(s);
        }
    }
 #endregion

#region Resolution
    // Get the name for operloaded binary-op
    // return null for an illegal op
    static internal string GetOpOverloadedName(BinaryExp.BinaryOp op)
    {
        switch(op)
        {
        case BinaryExp.BinaryOp.cAdd:   return "op_Addition";
        case BinaryExp.BinaryOp.cMul:   return "op_Multiply";
        case BinaryExp.BinaryOp.cDiv:   return "op_Division";
        case BinaryExp.BinaryOp.cSub:   return "op_Subtraction";
        
        
        case BinaryExp.BinaryOp.cEqu:   return "op_Equality";
        case BinaryExp.BinaryOp.cNeq:   return "op_Inequality";
        
        default:
            return null;
        }
    }

    // Semantic checking
    public override void ResolveMember(
        TypeEntry symDefiningClass, 
        ISemanticResolver s,
        ICLRtypeProvider provider
    )
    {
        //TypeEntry tRetType = null;
        if (!IsCtor)
        {
            m_tRetType.ResolveType(s);
            //tRetType = m_tRetType.TypeRec;
        } 
        else 
        {            
            ClassDecl nodeClass = symDefiningClass.Node;
            if (Mods.IsStatic)
            {
                // Checks on static ctors
                if (this.Params.Length != 0)
                {
                    //s.ThrowError_NoParamsOnStaticCtor(this);
                    ThrowError(SymbolError.NoParamsOnStaticCtor(this));
                }

                // Rename to avoid a naming collision w/ a default ctor.
                // Since no one will call a static ctor (and codegen ignores ctor names),
                // we can pick anything here.
                //this.m_strName = "$static_ctor";
                this.m_idName = new Identifier("$static_ctor", m_idName.Location);

                // Is there a static field initializer to chain to?
                if (nodeClass.StaticInitMethod != null)
                {
                    /*                           
                    Statement stmt = new ExpStatement(
                        new MethodCallExp(
                        null,
                        new Identifier(nodeClass.StaticInitMethod.Name, nodeClass.StaticInitMethod.Location),
                        new ArgExp[0]
                        ));
                    */
                    // If there are Static, ReadOnly fields, then they must be assigned directly
                    // in a constructor, not in a function called by the constructor.
                    Statement stmt = nodeClass.StaticInitMethod.Body;
                    Body.InjectStatementAtHead(stmt);                    
                }
            } // static ctor
            else 
            {
                if (nodeClass.InstanceInitMethod != null)
                {
                    // Chain to an instance-field initializer if we don't chain to
                    // a this() ctor.
                    CtorChainStatement chain = (this.Body.Statements[0]) as CtorChainStatement;
                    Debug.Assert(chain != null);
                    
                    if (chain.TargetType == CtorChainStatement.ETarget.cBase)
                    {       
                        /*                 
                        Statement stmt = new MethodCallStatement(
                            new MethodCallExp(
                                null,
                                new Identifier(nodeClass.InstanceInitMethod.Name, nodeClass.InstanceInitMethod.Location),
                                new ArgExp[0]
                            ));
                        */
                        // PEVerify barfs if we try to do an instance method call in the ctor,
                        // so just inject the raw method body. It's just a bunch of assigns anyway,
                        // so we're ok.
                        Statement stmt = nodeClass.InstanceInitMethod.Body;
                            
                        Body.InjectStatementAtHead(stmt);
                    }
                }
            } // instance ctor
        }
                       
        // Add new sym entry to our parent class's scope
        MethodExpEntry m = new MethodExpEntry(
            Name, 
            this, 
            symDefiningClass, 
            IsCtor ? null : m_tRetType.BlueType
        );
        
        m.m_scope = new Scope(
            "method: " + symDefiningClass.m_strName + "." + Name, 
            this,
            symDefiningClass.MemberScope);
        
        m_symbol = m;
        
        //s.PushScope(m.m_scope);
        Scope prev = s.SetCurrentContext(m.m_scope);
        
        // resolve params (because we'll need them for overloading)
        // Add param 0 for 'this' (if not static)
        // For structs, "this" is a reference, for classes, it's a value.
        if (!Mods.IsStatic)
        {
            ParamVarExpEntry e = new ParamVarExpEntry();
            e.m_strName = "this";
            
            TypeEntry t = m_symbol.SymbolClass;
            if (t.IsStruct)
            {
                t = new RefTypeEntry(t, s);
            }
            e.m_type = t; // 'this' is the type of the containg class   
            e.CodeGenSlot = 0;
            s.GetCurrentContext().AddSymbol(e);
        }
        
        // do rest of the params
        foreach(ParamVarDecl param in m_arParams)
        {
            param.ResolveVarDecl(s);
        }

        //s.PopScope(m.m_scope);
        s.RestoreContext(prev);
                
        symDefiningClass.AddMethodToScope(m);
    }
    
    // Resolve the bodies of methods
    public void ResolveBodies(ISemanticResolver s)
    {
        if (Mods.IsAbstract)
            return;

        Scope scope = m_symbol.m_scope;
        
        //s.PushScope(scope);
        Scope prev = s.SetCurrentContext(scope);
        s.SetCurrentMethod(m_symbol);        
        
        m_stmtBody.ResolveStatement(s);

        // Do a second pass, for goto statements to resolve against labels.        
        m_stmtBody.ResolveStatement2(s);
   
        s.SetCurrentMethod(null);
        //s.PopScope(scope);
        s.RestoreContext(prev);
    
    }
    
    // Since Modifiers are value types, we only pass a copy out,
    // so noone else can change them. So we expose a method here to
    // mark methods that are implied virtual as explicitly virtual.
    // (ex: method inherited from an interface)
    internal void MarkAsVirtual()
    {
        m_mods.SetVirtual();        
    }
    internal void MarkAsFinal()
    {
        m_mods.SetSealed();
    }
#endregion
    
#region Properties and Data
    public bool IsCtor
    {
        get { return m_tRetType == null; }
    }
     
    // Is this an overloaded operator?
    bool m_fIsOpOverload;
    public bool IsOpOverload
    {
        get { return m_fIsOpOverload; }
    }
                
    // Return type for this method
    // Only valid if we're not a constructor
    private TypeSig m_tRetType;
    
    Identifier m_idName;                
    //String m_strName;
    protected MethodExpEntry m_symbol;
    
    // Locals and statements are in the block statement
    // Only non-null if !IsAbstract
    protected BlockStatement m_stmtBody;
            
    private ParamVarDecl[] m_arParams;
    
    public MethodExpEntry Symbol
    {
        get { return m_symbol; }
    }
       
    public string Name
    {
        //get { return m_strName; }
        get { return m_idName.Text; }
    }
       
    public BlockStatement Body
    {
        get { return m_stmtBody; }
    }
       
    public ParamVarDecl[] Params
    {
        get { return m_arParams; }
    }
#endregion
    
} // end MethodDecl

#endregion

#region AST Nodes for Simple Variables (Locals & Parameters)
//-----------------------------------------------------------------------------
// Variable declaration
// Common base class for Locals & Parameters
//-----------------------------------------------------------------------------
public abstract class VarDecl : Node
{   
    public VarDecl(Identifier idName, TypeSig tType)
    {
        m_tType = tType;
        m_stName = idName.Text;
        
        //m_filerange = FileRange.Merge(tType.Location, idName.Location);
    }


    protected TypeSig m_tType;
    public TypeSig Sig
    {
        get { return m_tType; }
    }
    
    readonly string m_stName;
        
    // Properties
    // Note, we don't expose m_oeType because users should get the resolved
    // type from the symbol.
    
    // Get the unqualified name of the local
    public string 
    Name
    {
        get 
        { 
            return m_stName; 
        }
    }
    
    // Get the resolved symbol for this local. Will be null if
    // we haven't done semantic checks yet
    public VarExpEntry Symbol
    {
        get { return GetSymbol(); }
    }
    
    protected abstract VarExpEntry GetSymbol();
        
    // Semantic Checking
    // - Create a symbol entry for this local
    // - Resolve the ObjExp to a type
    internal void ResolveVarDecl(ISemanticResolver s)    
    {    
                
    // Convert our Object Expression into a TypeEntry
    // Note that at this point, all defined & imported types should be added
    // So if this is null, it's undefined.        
        m_tType.ResolveType(s);
                
        TypeEntry t = m_tType.BlueType;
        Debug.Assert(t != null);
        
    // Create a symbol entry        
        VarExpEntry sym = Symbol;
        sym.m_strName = m_stName;
        sym.m_type = t;
        s.GetCurrentContext().AddSymbol(sym);
    }
    
#region Checks    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        VarExpEntry sym = Symbol;
     
        o.WriteStartElement("LocalDecl");
        o.WriteAttributeString("name", m_stName);
        
        if (sym != null)
            o.WriteAttributeString("symbol", sym.ToString());
                            
        DumpDerivedAttributes(o);                            
                            
        if (m_tType != null)
            m_tType.Dump(o);                            
                                        
        o.WriteEndElement();
    }   
    
    // Hook for derived classes to add attributes in dump
    protected abstract void DumpDerivedAttributes(XmlWriter o);
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("{0} {1}", this.m_tType.ToString(), this.m_stName);    
    }
#endregion
} // end LocalDecl


//-----------------------------------------------------------------------------
// Local variable (a var declared within the method)
//-----------------------------------------------------------------------------
public class LocalVarDecl : VarDecl
{
    public LocalVarDecl(Identifier idName, TypeSig tType) : base(idName, tType)
    {
        m_symbol = new LocalVarExpEntry();
    }
    
    protected override void DumpDerivedAttributes(XmlWriter o)
    {
        o.WriteAttributeString("vartype", "local");
    }
    
    protected LocalVarExpEntry m_symbol;
    
    protected override VarExpEntry GetSymbol()
    {
        return m_symbol;
    }
    
    // Typesafe version of the Symbol property
    public LocalVarExpEntry LocalSymbol
    {
        get { return m_symbol; }
    }
    
//-----------------------------------------------------------------------------
// Debugging check
//-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(Symbol != null);
        Debug.Assert(LocalSymbol == Symbol);
        Debug.Assert(m_tType != null);
        
        m_tType.DebugCheck(s);
        Debug.Assert(m_tType.BlueType == LocalSymbol.m_type);
        Debug.Assert(LocalSymbol.Name == Name);
    }
}

//-----------------------------------------------------------------------------
// Local parameters
//-----------------------------------------------------------------------------

// Which way can the parameter flow from the callsite into the method decl?
public enum EArgFlow
{
    cIn,    // callsite provides value to declsite
    cOut,   // declsite provides value to callsite
    cRef    // callsite provides value declsite, declsite updates value for callsite
}

public class ParamVarDecl : VarDecl
{
    public ParamVarDecl(Identifier idName, NonRefTypeSig tType, EArgFlow eFlow) : base(idName, tType)
    {
        m_eFlow = eFlow;
        
        // Update tType to include the Ref/Out
        if (m_eFlow == EArgFlow.cOut || m_eFlow == EArgFlow.cRef)
        {
            this.m_tType = new RefTypeSig(tType);
        }
        
        m_symbol = new ParamVarExpEntry();
    }
    
#region Properties & Data    
    EArgFlow m_eFlow;
    public EArgFlow Flow 
    {
        get  { return m_eFlow; }
    }

    protected ParamVarExpEntry m_symbol;    
    protected override VarExpEntry GetSymbol()
    {
        return m_symbol;
    }
    
    // Typesafe version of the Symbol property
    public ParamVarExpEntry ParamSymbol
    {
        get { return m_symbol; }
    }
#endregion

#region Checks    
    protected override void DumpDerivedAttributes(XmlWriter o)
    {
        o.WriteAttributeString("vartype", "param");
        o.WriteAttributeString("flow", m_eFlow.ToString());
    }
    
//-----------------------------------------------------------------------------
// Debugging check
//-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(Symbol != null);
        Debug.Assert(ParamSymbol == Symbol);
        Debug.Assert(m_tType != null);
        
        m_tType.DebugCheck(s);
        Debug.Assert(m_tType.BlueType == ParamSymbol.m_type);
        Debug.Assert(ParamSymbol.Name == Name);
                
    }    
#endregion    
}

#endregion

} // end AST namespace
