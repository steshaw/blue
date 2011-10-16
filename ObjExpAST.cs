//-----------------------------------------------------------------------------
// File: ObjExpAST.cs
//
// Description: 
// all Exp nodes that are used to generate an expression evaluating to an 
// object
//
// See AST.cs for details
//
// Exp-> ObjExp -> <any derived class below>
//
// SimpleObjExp -> id                                       // a single identifier
// DotObjExp -> ObjExp '.' id                               // Field Access
// MethodCallExp -> id '(' param_list ')'                // MethodCall
// MethodCallExp -> ObjExp '.' id '(' param_list ')'     // MethodCall
// ArrayAccessExp -> ObjExp '[' int_exp ']'                    // array access
// CastObjExp -> '(' Type ')' Exp                           // Type Cast
// NewObjExp -> 'new' type '(' param_list ')'               // new - ctor
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Xml;

using SymbolEngine;
using Blue.Public;
using CodeGen = Blue.CodeGen;


namespace AST
{


#region Resolved Exp
//-----------------------------------------------------------------------------
// Expression that represents a namespace
//-----------------------------------------------------------------------------
public class NamespaceExp : Exp
{
    public NamespaceExp(
        SymbolEngine.NamespaceEntry symbol
    )
    {
        m_symbol = symbol;
    }
    
#region Properties & Data
    NamespaceEntry m_symbol;
    public NamespaceEntry Symbol
    {
        get { return m_symbol; }
    }
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_symbol != null);    
    }
    
    public override void Dump(XmlWriter o)
    {
        
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("<N>'{0}'", m_symbol.FullName);   
    }
#endregion

    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return null;
    }
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        return this;
    }
}

//-----------------------------------------------------------------------------
// Type, this is also temporary
//-----------------------------------------------------------------------------
class TypeExp : Exp
{
    public TypeExp(TypeEntry symbol)
    {
        m_symbol = symbol;
    }
    
    TypeEntry m_symbol;
    public TypeEntry Symbol
    {
        get { return m_symbol; }
    } 
    
#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_symbol != null);    
    }
    
    public override void Dump(XmlWriter o)
    {
        
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {   
        sb.Write("({0})", m_symbol.FullName);
    }
#endregion 

#region Resolution
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_symbol.CLRType;
    }
    
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        // We can only do the CLR types after we do them for the symbols.
        // But we may refer to a type before we have resolved it to a CLR symbol.
        // (such as when we specify a base class).
        if (m_symbol.CLRType != null)
            CalcCLRType(s);
        return this; 
    }
#endregion    
    
}


//-----------------------------------------------------------------------------
// Local var
//-----------------------------------------------------------------------------
public class LocalExp : Exp
{
    public LocalExp(
        SymbolEngine.LocalVarExpEntry symbol
    )
    {
        m_symbol = symbol;
    }
    
    LocalVarExpEntry m_symbol;
    public LocalVarExpEntry Symbol
    {
        get  { return m_symbol; }
    }  
    
#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_symbol != null);
    }
    
    public override void Dump(XmlWriter o)
    {
    
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("<L>{0}", m_symbol.Name);   
    }
#endregion   

#region Resolution
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_symbol.CLRType;
    }
    
    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        CalcCLRType(s);
        return this; 
    }
    
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        CalcCLRType(s);
        return this; 
    }
#endregion

#region Generate
    public override void GenerateAsLeftPre(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsLeftPre(this);
    }    
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
    public override void GenerateAsLeftPost(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsLeftPost(this);
    }
        
    public override void GenerateAddrOf(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAddrOf(this);
    }
#endregion
}

//-----------------------------------------------------------------------------
// Expression refering to a parameter
//-----------------------------------------------------------------------------
public class ParamExp : Exp
{
    public ParamExp(
        SymbolEngine.ParamVarExpEntry symbol
    )
    {
        m_symbol = symbol;
    }
    
    ParamVarExpEntry m_symbol;
    public ParamVarExpEntry Symbol
    {
        get  { return m_symbol; }
    }        
    
#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_symbol != null);
    }
    
    public override void Dump(XmlWriter o)
    {
    
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("<P>{0}", m_symbol.Name);   
    }
#endregion       


#region Resolution
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_symbol.CLRType;
    }
    
    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        CalcCLRType(s);
        return this; 
    }
    
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        CalcCLRType(s);
        return this; 
    }
#endregion

#region Generate
    public override void GenerateAsLeftPre(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsLeftPre(this);
    }    
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
    public override void GenerateAsLeftPost(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsLeftPost(this);
    }    
    
    public override void GenerateAddrOf(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAddrOf(this);
    }
#endregion

}

//-----------------------------------------------------------------------------
// For Properties
//-----------------------------------------------------------------------------
public class PropertyExp : Exp
{
    public PropertyExp(
        PropertyExpEntry symbol,
        Exp expInstance // null for statics
        )
    {
        Debug.Assert(symbol != null);
        m_symbol = symbol;
        m_expInstance = expInstance;
    }

#region Properties & Data
    SymbolEngine.PropertyExpEntry m_symbol;
    public PropertyExpEntry Symbol
    {
        get { return m_symbol; }
    }

    Exp m_expInstance;
    public Exp InstanceExp
    {
        get { return m_expInstance; }    
    }

    public bool IsStatic
    {
        get { return m_expInstance == null; }
    }
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_symbol != null);
        Debug.Assert(m_symbol.IsStatic == IsStatic);
    }

    public override void Dump(XmlWriter o)
    {

    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        if (m_expInstance == null)
            //sb.Write("[static]");
            sb.Write(m_symbol.SymbolClass.FullName);
        else
            m_expInstance.ToSource(sb);
                                
        sb.Write(".<prop>{0}", m_symbol.Name);   
    }
#endregion

#region Resolution
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_symbol.CLRType;
    }

    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        // Whoever is resolving against us will have to change us to a
        // Set method (because only they have access to the RHS).
       
        CalcCLRType(s);
        return this; 
    }

    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        //CalcCLRType(s);
        //return this; 
        // Transform into a get
        Exp eResolved = new MethodCallExp(
            this.InstanceExp, 
            Symbol.SymbolGet, 
            new ArgExp[0], 
            s
        );
        
        Exp.ResolveExpAsRight(ref eResolved, s);
        
        return eResolved;
    }

#endregion

#region Generate
// Properties don't generate code.
// They should be transformed into MethodCalls by that point 
#endregion

}

//-----------------------------------------------------------------------------
// For MethodPointers (used for delegates)
//-----------------------------------------------------------------------------
public class MethodPtrExp : Exp
{
    public MethodPtrExp(
        MethodExpEntry symbol
    )
    {
        Debug.Assert(symbol != null);
        m_symbol = symbol;
    }        
    
#region Properties & Data
    MethodExpEntry m_symbol;
    public MethodExpEntry Symbol
    {
        get { return m_symbol; }
    }    
#endregion    

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_symbol != null);
    }
    
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("MethodPtrExp");
        string stName = TypeEntry.GetDecoratedParams(m_symbol);
        o.WriteAttributeString("name", stName);
        o.WriteEndElement();
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("<Mptr>'{0}'", m_symbol.PrettyDecoratedName);    
    }
#endregion

#region Resolution
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        // This is a pointer, and pointers are ints.
        // Also, we pass this to the delegate ctor, which expects an int
        return Type.GetType("System.IntPtr");
        //return typeof(int);
    }
            
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        CalcCLRType(s);
        return this; 
    }
#endregion

    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
}

//-----------------------------------------------------------------------------
// For events
// This is only a temporary node (like properties) and should be transformed
// into method calls.
//-----------------------------------------------------------------------------
public class EventExp : Exp
{
    public EventExp(
        EventExpEntry symbol,
        Exp expInstance // null for statics
        )
    {
        Debug.Assert(symbol != null);
        m_symbol        = symbol;
        m_expInstance   = expInstance;
    }
    
    EventExpEntry m_symbol;
    public EventExpEntry Symbol
    {
        get { return m_symbol; }
    }
    
    Exp m_expInstance;
    public Exp InstanceExp
    {
        get { return m_expInstance; }
    }
    
#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(false, "EventExp node should be replaced by end of resolution");
        Debug.Assert(m_symbol != null);        
    }
    
    public override void Dump(XmlWriter o)
    {
    
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("<E>{0}", m_symbol.Name);
    }
#endregion    
    
#region Resolution
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_symbol.EventType.CLRType;
    }
    
    // Ok to resolve as a LS thing (really only in += and -=).    
    // This node will get replaced with a method call anyways..
    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        CalcCLRType(s);
        return this; 
    }
    
    // See section 10.7 of the C# spec.
    // Outside of our class, we can only use an event as the LS in += and -=
    // However, within our class, we can use an event on the RS, like a delegate.
    // This is a little goofy. 
    // We can actually never use an event on the RS. When try to, we're actually
    // using a compiler generated delegate (that has the exact same name) instead.
    // However, we can't put both an event & a delegate in the same scope (because
    // the names would conflict). So the delegate has a different name than the event.
    // When the user tries to access an event as a RHS, we switch it to the delegate here.
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        FieldExpEntry f = m_symbol.Field;
        if (f == null)
        {   
            // Events aren't allowed as RHS expressions.
            ThrowError(SymbolError.NoEventOnRHS(this));
        }
        Exp e = new FieldExp(f, this.InstanceExp);
        Exp.ResolveExpAsRight(ref e, s);
        return e;
        
    }
#endregion    

}

//-----------------------------------------------------------------------------
// For fields
//-----------------------------------------------------------------------------
public class FieldExp : Exp
{
    public FieldExp(
        FieldExpEntry symbol,
        Exp expInstance // null for statics
    )
    {
        Debug.Assert(symbol != null);
        m_symbol = symbol;
        m_expInstance = expInstance;
    }
    
#region Properties & Data
    SymbolEngine.FieldExpEntry m_symbol;
    public FieldExpEntry Symbol
    {
        get { return m_symbol; }
    }
    
    Exp m_expInstance;
    public Exp InstanceExp
    {
        get { return m_expInstance; }    
    }
    
    public bool IsStatic
    {
        get { return m_expInstance == null; }
    }
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_symbol != null);
        Debug.Assert(m_symbol.IsStatic == IsStatic);
    }
    
    public override void Dump(XmlWriter o)
    {
    
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        if (m_expInstance == null)
            //sb.Write("[static]");
            sb.Write(m_symbol.SymbolClass.FullName);
        else
            m_expInstance.ToSource(sb);
                                
        sb.Write(".<field>{0}", m_symbol.Name);   
    }
#endregion

#region Resolution
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_symbol.CLRType;
    }
    
    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        CalcCLRType(s);
        return this; 
    }
    
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        CalcCLRType(s);
        return this; 
    }
    
#endregion

#region Generate
    public override void GenerateAsLeftPre(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsLeftPre(this);
    }    
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
    public override void GenerateAsLeftPost(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsLeftPost(this);
    }
        
    public override void GenerateAddrOf(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAddrOf(this);
    }
#endregion

}



#endregion



//-----------------------------------------------------------------------------    
// ObjExp -> id
// Resolve to another, more-specific, Node
//-----------------------------------------------------------------------------
public class SimpleObjExp : Exp
{
    public SimpleObjExp(Identifier id)
    {
        m_filerange = id.Location;
        m_strId = id;
    }
    
    public SimpleObjExp(string st)
    {
        m_filerange = null;
        m_strId = new Identifier(st, m_filerange);
    }
    
#region Properties & Data        
    Identifier m_strId;
    public Identifier Name
    {
        get { return m_strId; }
    }

    
#endregion
    
#region Checks        
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(false, "Temporary node still in Final pass");
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("SimpleObjExp");
        o.WriteAttributeString("id", m_strId.Text);        
        o.WriteEndElement();
    }
    
    public override string ToString()
    {
        return this.m_strId.Text;
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {        
        sb.Write("'{0}'", this.Name.Text);   
    }
#endregion    
    // m_type is only null if we're a namespace, in which case we have an ancestor
    // DotObjExp which will have the type
    
    // Resolve as a LHS
    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        Exp e = GetResolvedNode(s, false);        
        Debug.Assert(e != this);
        Exp.ResolveExpAsLeft(ref e, s); 
        e.SetLocation(this.Location);       
        return e;
    }
    
    // Resolve as a RHS
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {           
        Exp e = GetResolvedNode(s, true);        
        if (e != this)
            Exp.ResolveExpAsRight(ref e, s);
        e.SetLocation(this.Location);                    
        return e;
    }
    
    
    // An ObjExp is just a temporary node. But that's the best a Context-Free parse can
    // do. So now that we're building a symbol table, we can do a Context-Sensitive resolution
    // and figure out what type of node this really is.
    public Exp GetResolvedNode(ISemanticResolver s, bool fRight)
    {   
        Exp eResolved = null;
        
        // Lookup the symbol and determine what we are
        //SymEntry sym = s.LookupSymbol(this.m_strId, true);
        string stName = this.m_strId.Text;
        SymEntry sym = s.LookupSymbolWithContext(this.m_strId, false); // allow methods
        
        // Namespace
        if (sym is NamespaceEntry)
        {
            eResolved = new NamespaceExp(sym as NamespaceEntry);
        }
        
        // Local Variable
        else if (sym is LocalVarExpEntry)
        {
            eResolved = new LocalExp(sym as LocalVarExpEntry);
        }
        
        // Parameter
        else if (sym is ParamVarExpEntry)
        {
            eResolved = new ParamExp(sym as ParamVarExpEntry);   
        }
        
        // A type name
        else if (sym is TypeEntry)
        {
            eResolved = new TypeExp(sym as TypeEntry);
        }
        
        // A field (w/ an implied 'this' pointer)
        else if (sym is FieldExpEntry)
        {
            // When a single identifier resolves to a field, it can be either
            // an instance field with an implied 'this' ref, or a static field of this class.
            FieldExpEntry f = sym as FieldExpEntry;
            Exp expInstance = null;
            if (!f.IsStatic)
            {
                expInstance = new SimpleObjExp("this");
                Exp.ResolveExpAsRight(ref expInstance, s);
            }
            eResolved = new FieldExp(f, expInstance);
        }
        
        // An event (w/ an implied 'this' ptr)
        else if (sym is EventExpEntry)
        {
            EventExpEntry e = (EventExpEntry) sym;
            Exp expInstance = null;
            if (!e.Mods.IsStatic)
            {
                expInstance = new SimpleObjExp("this");
                Exp.ResolveExpAsRight(ref expInstance, s);
            }
            eResolved = new EventExp(e, expInstance);            
        }
        
        // A property (w/ an implied 'this' ptr).
        // Properties will eventually be converted into method calls.
        else if (sym is PropertyExpEntry)
        {
            PropertyExpEntry p = (PropertyExpEntry) sym;
            
            Exp expInstance = null;
            if (!p.IsStatic)            
            {
                expInstance = new SimpleObjExp("this");
                Exp.ResolveExpAsRight(ref expInstance, s);
            }
            
            eResolved = new PropertyExp(p, expInstance);
        }
        
        // Not recognized.
        else {
            if (stName == "this") // check a common error case...            
                Debug.Assert(false, "Can't access 'this'. Are we in a static?");
            
            if (sym == null)
            {
                MethodHeaderEntry h = s.GetCurrentClass().LookupMethodHeader(this.m_strId.Text);
                if (h != null)
                {                    
                    return this;
                }
                
                ThrowError(SymbolError.UndefinedSymbol(m_strId));
                //Debug.Assert(false, "Unknown name in SimpleObjExp:" + stName);
            }
            Debug.Assert(false, "Unknown symbol type:" + ((sym == null) ? "null" : sym.ToString()));
        }
        
        
        Debug.Assert(eResolved != null);        
        return eResolved;
    }
  
}

//-----------------------------------------------------------------------------    
// DotObjExp -> ObjExp '.' id
//-----------------------------------------------------------------------------    
public class DotObjExp : Exp
{
#region Construction
    public DotObjExp(Exp left, Identifier id)
    {
        m_left = left;
        m_strId = id;
        
        m_filerange = id.Location;
    }
#endregion

#region Properties & Data
    Exp m_left;
    public Exp LeftExp
    {
        get { return m_left; }
    }

    readonly Identifier m_strId;
    public Identifier Id
    { 
        get { return m_strId; }
    }        
    
#endregion    

#region Resolution
    // Resolve as a LHS
    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        Exp.ResolveExpAsRight(ref m_left, s);
        Exp e = GetResolvedNode(s);        
        Exp.ResolveExpAsLeft(ref e, s);
        e.SetLocation(this.Location);
        return e;
    }
    
    // Resolve as a RHS
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {           
        // E.i
        // First resolve the exp on the left of the dot.
        Exp.ResolveExpAsRight(ref m_left, s);
        
        // Now we can figure out what we ought to be.
        Exp e = GetResolvedNode(s);
        
        // If we changed nodes, then resolve the new one.
        // If we didn't change, then don't resolve again (else we have stack-overflow)
        if (e != this)
            Exp.ResolveExpAsRight(ref e, s);
            
        e.SetLocation(this.Location);
        return e;
    }
    
    
    // An ObjExp is just a temporary node. But that's the best a Context-Free parse can
    // do. So now that we're building a symbol table, we can do a Context-Sensitive resolution
    // and figure out what type of node this really is.
    public Exp GetResolvedNode(ISemanticResolver s)
    {
        string stText = this.m_strId.Text;
        // Left must already be resolved, then we resolve right in the context of left
        Debug.Assert(m_left != null);
        Exp eResolved = null;
                   
        // @todo, what if left is a NullExp?                       
        if (m_left is NamespaceExp)
        {
            // We're either a nested namespace or a class
            NamespaceEntry n = (m_left as NamespaceExp).Symbol;
            SymEntry sym = n.ChildScope.LookupSymbol(stText);
            if (sym is NamespaceEntry)
            {
                eResolved = new NamespaceExp(sym as NamespaceEntry);
            }
            else if (sym is TypeEntry)
            {
                eResolved = new TypeExp(sym as TypeEntry);
            } else {
                //ThrowError_UndefinedSymbolInNamespace(s, n, m_strId);            
                ThrowError(SymbolError.UndefinedSymbolInNamespace(n, m_strId));
            }
        }
        
        // Check for statics
        else if (m_left is TypeExp)
        {
            TypeEntry t = ((TypeExp) m_left).Symbol;
            t.EnsureResolved(s);
            SymEntry sym = t.MemberScope.LookupSymbol(stText);
            if (sym is FieldExpEntry)
            {
                Debug.Assert(((FieldExpEntry) sym).IsStatic);
                eResolved = new FieldExp(sym as FieldExpEntry, null); // static
            }
            
            else if (sym is PropertyExpEntry)
            {
                eResolved = new PropertyExp(sym as PropertyExpEntry, null);
            } 
            
            else if (sym is EventExpEntry)
            {
                eResolved = new EventExp(sym as EventExpEntry, null);
            }
            
            // Allow nested types
            else if (sym is TypeEntry)
            {
                eResolved = new TypeExp(sym as TypeEntry);
            }
            
            else 
            {
                // Must be a method. The node transform occurs higher up though.
                Debug.Assert((sym = t.LookupMethodHeader(stText)) != null);                
                eResolved = this;
            }
            
            if (eResolved == null) {
                //ThrowError_UndefinedSymbolInType(s, t, m_strId);
                ThrowError(SymbolError.UndefinedSymbolInType(t, m_strId));
            }
        }
        
        // m_left is a variable, and we're doing an instance member dereference
        else {
            TypeEntry t = null;
            
            t = s.ResolveCLRTypeToBlueType(this.m_left.CLRType);
            t.EnsureResolved(s);
                       
            Scope scope = t.MemberScope;
            
            // @todo - broken for an interface. IA : IB, scope for IA doesn't link to IB.
            SymEntry sym = scope.LookupSymbol(stText);
            if (sym is FieldExpEntry)
            {
                eResolved = new FieldExp(sym as FieldExpEntry, this.m_left);
            } 
            else if (sym is PropertyExpEntry)
            {
                eResolved = new PropertyExp(sym as PropertyExpEntry, this.m_left);
            }
            
            else if (sym is EventExpEntry)
            {
                eResolved = new EventExp(sym as EventExpEntry, this.m_left);            
            }
            
            else 
            {
                // Must be a method. The node transform occurs higher up though.
                sym = t.LookupMethodHeader(stText);
                if (sym != null)
                    eResolved = this;
            }
            
            if (eResolved == null) 
            {                
                ThrowError(SymbolError.UndefinedSymbolInType(t, m_strId));
            }
        }
        
        
        Debug.Assert(eResolved != null);
        return eResolved;
        
    }

#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(false, "Temporary node still in Final pass");
    }
    
    public override void Dump(XmlWriter o)
    {
        
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        this.m_left.ToSource(sb);
        sb.Write(".'{0}'", this.Id.Text);   
    }
#endregion    
    
}


//-----------------------------------------------------------------------------
//Method Call exp
//MethodCall-> LValue '.' id '(' paramlist ')' 
//MethodCall-> id '(' paramlist ')' // no LValue, implied 'this'
//-----------------------------------------------------------------------------
public class MethodCallExp : StatementExp
{
#region Construction
    public MethodCallExp(
        Exp e, // may be null,
        Identifier id,
        ArgExp [] arParams
    )
    {   
    // m_objExp may be null _until_ we resolve this. And then it's either
    // going to the implied 'this' ptr, a global func or a static func.
        m_objExp = e;
        m_idName = id;
        m_arParams = (arParams == null) ? new ArgExp[0] : arParams;
        
        
        // @todo - set in parser
        m_filerange = id.Location;        
    }
    
    // Use this when we already have a static method to call
    // and we already have the symbols
    public MethodCallExp(
        Exp eInstance, // null if static       
        MethodExpEntry  symMethod,
        ArgExp [] arParams,
        ISemanticResolver s
    )
    {
        this.m_idName = new Identifier(symMethod.Name);
        m_arParams = arParams;
        m_symbol = symMethod;
        
        // Spoof Left
        if (eInstance == null)
        {
            //m_objExp = new SimpleObjExp(symMethod.SymbolClass.Name);
            m_objExp = new TypeExp(symMethod.SymbolClass);
        }
        else
            m_objExp = eInstance;
            
        Exp.ResolveExpAsRight(ref m_objExp, s);

        // Resolve args, just in case
        foreach(ArgExp eArg in arParams)
        {
            Exp e = eArg;
            Exp.ResolveExpAsRight(ref e, s);
            Debug.Assert(e == eArg);
        }   
        
        //ResolveAsExpEntry(m_symbol, s);
        CalcCLRType(s);
    }
    
#endregion

#region Checks
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(Symbol != null);
        Debug.Assert(m_symbol.RetType.CLRType == base.CalcCLRType(s));
        Debug.Assert(m_symbol.m_strName == this.m_idName.Text);
                
        Debug.Assert(m_symbol.Info != null);
        
        //Debug.Assert(m_mode == ObjExp.Mode.cExpEntry);
                
        m_objExp.DebugCheck(s);
        
        foreach(ArgExp e in ParamExps)
        {
            e.DebugCheck(s);
        }
                        
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        #if true
        if (this.m_objExp == null)        
            sb.Write(this.Symbol.SymbolClass.FullName);
        else
            m_objExp.ToSource(sb);
                
        sb.Write('.');
        sb.Write(this.Symbol.Name);
        #endif
        
        sb.Write('(');
        bool fFirst = true;
        foreach(ArgExp a in m_arParams)
        {
            if (!fFirst)
                sb.Write(',');
            a.ToSource(sb);
            
            fFirst = false;
        }
        sb.Write(')');
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("MethodCallExp");
        o.WriteAttributeString("name", m_idName.Text);
            
        if (m_symbol != null)
            o.WriteAttributeString("symbol", m_symbol.ToString());
    
        if (m_objExp != null)
            m_objExp.Dump(o);
        
        o.WriteStartElement("parameters");    
        foreach(ArgExp e in m_arParams)
        {
            e.Dump(o);
        }
        o.WriteEndElement();
                
        o.WriteEndElement();
    }        
#endregion Checks

#region Properties & Data
    Exp m_objExp;    
    public Exp LeftObjExp
    {
        get { return m_objExp; }
    }

    ArgExp [] m_arParams;
    
    //readonly string m_strName;
    readonly Identifier m_idName;
    
    public MethodExpEntry m_symbol;

    public ArgExp [] ParamExps
    {
        get { return m_arParams; }
    }

    public MethodExpEntry Symbol
    {
        get { return m_symbol; }
    }
    
    //bool m_fIsVarArg;
    /*
    public bool IsVarArg
    {
        get { return m_fIsVarArg; }
    }*/

    // Should codegen make this a static call or an instance call?
    public bool IsStaticCall
    {
        get { return m_symbol.IsStatic; }
    }
    
    bool m_fIsNotPolymorphic;
    public bool IsNotPolymorphic
    {
        get { return m_fIsNotPolymorphic; }
    }
#endregion

#region Resolution
    // The type of a method expression is its return type.
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return this.Symbol.CLRType;
    }
    
    // Semantic resolution
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {   
        // Only resolve once.     
        if (m_symbol != null)
            return this;
            
        // First, resolve our parameters (because of overloading)
        // We need to know the URT types for our parameters
        // in order to resolve between overloaded operators
        
        Type [] alParamTypes = new Type[m_arParams.Length];
        
        
        for(int i = 0; i < m_arParams.Length; i++)        
        {
            Exp e = m_arParams[i];
            ResolveExpAsRight(ref e, s);
            Debug.Assert(e == m_arParams[i]);
            
            Type tParam = e.CLRType;
            
            //if ((tParam !=null) && tParam.IsByRef)
            //    tParam = tParam.GetElementType();
            
            alParamTypes[i] = tParam;
            //Debug.Assert(alParamTypes[i] != null);
            
        }   
        
        TypeEntry tCur = s.GetCurrentClass();    
        TypeEntry tLeft = null; // Type to lookup in   
        
        // Is this a 'base' access?
        // Convert to the real type and set a non-virtual flag
        if (m_objExp is SimpleObjExp)
        {
            SimpleObjExp e = m_objExp as SimpleObjExp;
            if (e.Name.Text == "base")
            {
                // Set the scope that we lookup in.
                tLeft = tCur.Super;
                
                // Still need to resolve the expression.
                m_objExp = new SimpleObjExp("this");               
                                               
                m_fIsNotPolymorphic = true;
            }
        }
        
#if true
        // See if we have a delegate here
        Exp eDelegate = null;
        if (m_objExp == null)
        {
            Exp e = new SimpleObjExp(m_idName);
            Exp.ResolveExpAsRight(ref e, s);
            if (!(e is SimpleObjExp))                
                eDelegate = e;
        } else {
            // If it's an interface, then we know we can't have a delegate field on it, 
            // so short-circuit now. 
            Exp.ResolveExpAsRight(ref m_objExp, s);
            if (!m_objExp.CLRType.IsInterface)
            {                
                Exp e = new DotObjExp(m_objExp, m_idName);
                Exp.ResolveExpAsRight(ref e, s);
                if (!(e is DotObjExp))                
                    eDelegate = e;        
            }
        }

        if (eDelegate != null)
        {
            if (!DelegateDecl.IsDelegate(eDelegate.CLRType))
            {
                //Debug.Assert(false, "@todo - " + m_strName + " is not a delegate or function"); // @todo - legit
                // Just fall through for now, method resolution will decide if this is a valid function
            } else 
            {            
                Exp e = new MethodCallExp(
                    eDelegate, 
                    new Identifier("Invoke"), 
                    this.m_arParams
                );
                
                Exp.ResolveExpAsRight(ref e, s);
                return e;        
            }
        }        
#endif    
        // No delegate, carry on with a normal function call
                        
        // If there's no objexp, then the function is a method
        // of the current class. 
        // make it either a 'this' or a static call
        if (m_objExp == null)
        {   
            // Lookup
            bool fIsVarArgDummy;
            MethodExpEntry sym = tCur.LookupMethod(s, m_idName, alParamTypes, out fIsVarArgDummy);
            
            if (sym.IsStatic)
            {                
                m_objExp = new TypeExp(tCur);
            } else {
                m_objExp = new SimpleObjExp("this");
            }
        }
        
        // Need to Lookup m_strName in m_objExp's scope (inherited scope)
        Exp.ResolveExpAsRight(ref m_objExp, s);
                    
                
        // Get type of of left side object
        // This call can either be a field on a variable
        // or a static method on a class
        
        bool fIsStaticMember = false;
        
        // If we don't yet know what TypeEntry this methodcall is on, then figure
        // it out based off the expression
        if (tLeft == null)
        {
            if (m_objExp is TypeExp)
            {
                fIsStaticMember = true;
                tLeft = ((TypeExp) m_objExp).Symbol;
            } else {
                fIsStaticMember = false;
                tLeft = s.ResolveCLRTypeToBlueType(m_objExp.CLRType);
            }
        }
        
        // Here's the big lookup. This will jump through all sorts of hoops to match
        // parameters, search base classes, do implied conversions, varargs, 
        // deal with abstract, etc.
        bool fIsVarArg;
        m_symbol = tLeft.LookupMethod(s, m_idName, alParamTypes, out fIsVarArg);
        Debug.Assert(m_symbol != null);
        
        if (m_fIsNotPolymorphic)
        {
            // of the form 'base.X(....)'
            if (m_symbol.IsStatic)
                ThrowError(SymbolError.BaseAccessCantBeStatic(this.Location, m_symbol)); // @todo - PrintError?
        } else {
            // normal method call
            /*
            if (fIsStaticMember && !m_symbol.IsStatic)
                ThrowError(SymbolError.ExpectInstanceMember(this.Location)); // @todo - PrintError?
            else if (!fIsStaticMember && m_symbol.IsStatic)                
                ThrowError(SymbolError.ExpectStaticMember(this.Location)); // @todo - PrintError?
            */
            Debug.Assert(fIsStaticMember == m_symbol.IsStatic, "@todo - user error. Mismatch between static & instance members on line.");
        }
        
        
        // If we have a vararg, then transform it 
        if (fIsVarArg)
        {
        // Create the array
            int cDecl = m_symbol.ParamCount;
            int cCall = this.ParamExps.Length;
            
            ArrayTypeSig tSig = new ArrayTypeSig(m_symbol.ParamCLRType(cDecl - 1), s);
            
            Node [] list = new Node[cCall - cDecl + 1];
            for(int i = 0; i < list.Length; i++)
            {
                list[i] = this.ParamExps[i + cDecl - 1];
            }
            
            Exp eArray = new NewArrayObjExp(
                tSig,
                new ArrayInitializer(
                    list
                )
            );
            
            Exp.ResolveExpAsRight(ref eArray, s);
            
        // Change the parameters to use the array    
            ArgExp [] arParams = new ArgExp[cDecl];
            for(int i = 0; i < cDecl - 1; i++)
                arParams[i] = m_arParams[i];
            arParams[cDecl - 1] = new ArgExp(EArgFlow.cIn, eArray);
            
            m_arParams = arParams;                            
        } // end vararg transformation
                
        this.CalcCLRType(s);
   
        return this;
    }
#endregion
    
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {        
        gen.Generate(this);      
    }
    
    public override void GenerateAsStatement(CodeGen.EmitCodeGen gen)
    {        
        // Default impl generates as a RHS exp and just pops the value off    
        gen.GenerateAsStatement(this);
    }
}

#region Cast Expressions (TypeCasting & As operator)
//-----------------------------------------------------------------------------
// Type Cast any expression into a specific type
// TypeCast -> '(' Type ')' Exp                           
// AsExp -> Exp as Type
//-----------------------------------------------------------------------------

public class AsExp : BaseCastObjExp
{
    public AsExp(
        TypeSig tTargetType,
        Exp expSource
        ) 
        : base(tTargetType, expSource)
    {
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write('(');
        this.m_expSource.ToSource(sb);
        sb.Write(" as {0}", this.m_tTargetType.ToString());
        sb.Write(')');
    }

    // Resolve
    // The 'as' operator can only be used on a Reference type (not a struct)
    // So let the base class resolve, and then just add the error check here
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        Exp e = base.ResolveExpAsRight(s);
                
        if (this.SourceExp.CLRType.IsValueType)            
            ThrowError(SymbolError.AsOpOnlyOnRefTypes(this.Location));
        
        return e;
    }

    // Do codegen
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {        
        gen.Generate(this);
    }
}

public class CastObjExp : BaseCastObjExp
{
    public CastObjExp(
        TypeSig tTargetType,
        Exp expSource
        ) 
        : base(tTargetType, expSource)
    {
    }


    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("(({0}) ", m_tTargetType.ToString());        
        this.m_expSource.ToSource(sb);                
        sb.Write(')');
    }
    
    // Do codegen
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {        
        gen.Generate(this);
    }
}
    
// Base class for both TypeCasting & As operator
public abstract class BaseCastObjExp : Exp
{
    public BaseCastObjExp(
        TypeSig tTargetType,
        Exp expSource
        )
    {
        m_tTargetType = tTargetType;
        m_expSource = expSource;
        
        Debug.Assert(m_tTargetType != null);
        Debug.Assert(m_expSource != null);
        
        // @todo - have parser resolve this
        //m_filerange = FileRange.Merge(expSource.Location, m_tTargetType.Location);
    }
    
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_tTargetType != null);
        m_tTargetType.DebugCheck(s);
        
        Debug.Assert(m_expSource != null);
        m_expSource.DebugCheck(s);    
                
        //Debug.Assert(m_tTargetType.TypeRec == base.TypeRec);
    }
    
    // Get the type that we want to cast too
    protected TypeSig m_tTargetType;
    public TypeSig TargetType
    {   
        get { return m_tTargetType; }
    }
    
    // The source expression whos type will get converted
    protected Exp m_expSource;
    public Exp SourceExp
    {
        get { return m_expSource; }
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement(this.GetType().ToString());
        o.WriteAttributeString("targettype", TargetType.ToString());
    
        m_expSource.Dump(o);
                
        o.WriteEndElement();
    } 
        
    // Resolve
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        ResolveExpAsRight(ref m_expSource, s);
        m_tTargetType.ResolveType(s);
        
        // Optimizing Check - if we're casting to the type of the source exp,
        // then remove the cast. ex: '(T) t --> t'
        if (m_tTargetType.CLRType == m_expSource.CLRType)
        {
            return m_expSource;
        }
        
        
        //ResolveAsExpEntry(m_tTargetType.TypeRec, s);
        CalcCLRType(s);   
        return this;
    }
    
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_tTargetType.BlueType.CLRType;
    }
} // end cast expression

#endregion

#region New Expressions
//-----------------------------------------------------------------------------
// Allocate an object and invoke the proper constructor
// NewObjExp -> 'new' type '(' exp_list ')'
//-----------------------------------------------------------------------------
public class NewObjExp : Exp
{
    public NewObjExp(
        TypeSig tType,
        Exp [] arParams
    )
    {
        Debug.Assert(tType != null);
        Debug.Assert(arParams != null);
        
        m_tType = tType;
        m_arParams = arParams;
        
        // @todo- this is wrong
        m_filerange = tType.Location;                
    }
    
#region Checks    
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
    /*
        Debug.Assert(m_tType != null);
        m_tType.DebugCheck(s);
        
        Debug.Assert(m_tType.TypeRec == TypeRec);
        
        Debug.Assert(m_arParams != null);
        foreach(Exp e in m_arParams)
        {
            e.DebugCheck(s);
        }
        
        if (m_tType.TypeRec.IsStruct && (m_arParams.Length == 0))
        {
            // structs can't have a parameterless ctor
        } else {
            Debug.Assert(SymbolCtor != null);
            Debug.Assert(SymbolCtor.IsCtor || 
                (SymbolCtor.SymbolClass.IsStruct && (SymbolCtor.RetType.CLRType == typeof(void)))
            );
            
            Debug.Assert(!SymbolCtor.IsStatic); // new can't call a static ctor
        
            // Name of ctor must match name of the type we're creating
            Debug.Assert(SymbolCtor.Name == m_tType.TypeRec.Name);
        }
    */   
        
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {   
        sb.Write("new {0}", this.ElemType.ToString());
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("NewObjExp");
        o.WriteAttributeString("targettype", ElemType.ToString());
        if (SymbolCtor != null)
            o.WriteAttributeString("ctor", SymbolCtor.ToString());
    
    
        o.WriteStartElement("parameters");            
        foreach(Exp e in Params)
        {
            e.Dump(o);
        }
        o.WriteEndElement();
                
        o.WriteEndElement();
    } 
#endregion

#region Properties & Data    
    // The type of object that we're allocating
    protected TypeSig m_tType;
    public TypeSig ElemType
    {
        get { return m_tType; }
    }
    
    // The parameter list for the constructor call
    protected Exp[] m_arParams;
    public Exp[] Params
    {
        get { return m_arParams; }
    }
    
    // To make codegen's like easier, semantic checking
    // figures out what constructor operator new should
    // invoke
    protected MethodExpEntry m_symCtor;
    public MethodExpEntry SymbolCtor
    {
        get { return m_symCtor; }
    }
#endregion
    
    // Do codegen
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
    
#region Resolution    
    // Resolve
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        // Resolve the type we're allocating        
        m_tType.ResolveType(s);
                               
        // One major exception for creating a new delegate, of the form:
        // new T(E.i)
        //if (m_tType.CLRType.BaseType == typeof(System.MulticastDelegate))
        if (DelegateDecl.IsDelegate(m_tType.CLRType))
        {
            //Debug.Assert(false, "@todo - Impl Delegates in New");
            if (Params.Length != 1)
            {
                // When this is resolved, we actually have 2 params (not just one).
                // Make sure we've already resolved this. 
                return this;           
            }        
            
            Exp.ResolveExpAsRight(ref m_arParams[0], s);
            DotObjExp e = m_arParams[0] as DotObjExp;
            Debug.Assert(e != null);
            
            Exp expInstance = null;
            TypeEntry tLeft = null;
            
            if (e.LeftExp is TypeExp)
            {
                // Static
                expInstance = new NullExp(Location);
                tLeft = ((TypeExp) e.LeftExp).Symbol;
            } else {
                // Instance
                expInstance = e.LeftExp;
                tLeft = s.ResolveCLRTypeToBlueType(e.LeftExp.CLRType);
            }
            
            // Use the parameter list off the delegate type to discern for overloads.            
            System.Type [] alDelegateParams = DelegateDecl.GetParams(m_tType.BlueType);
            
            // Lookup what function we're passing to the delegate.
            
            bool fIsOut;
            MethodExpEntry m = tLeft.LookupMethod(s, e.Id, alDelegateParams, out fIsOut);
            Debug.Assert(!fIsOut, "@todo - can't have a delegate reference a vararg function");
            
            // Change parameters
            m_arParams = new Exp [] {
                expInstance,
                new MethodPtrExp(m)
            };
        }
        
        // Resolve all parameters                
        System.Type [] alParamTypes = new Type[Params.Length];
        for(int i = 0; i < this.m_arParams.Length; i++)
        {
            Exp.ResolveExpAsRight(ref m_arParams[i], s);
            
            alParamTypes[i] = m_arParams[i].CLRType;
            //Debug.Assert(alParamTypes[i] != null);            
        }
        
        

        // Now we're back to normal...
        
                                
        // Figure out what constructor we're calling    
        if (m_tType.BlueType.IsStruct && (alParamTypes.Length == 0))
        {
            // Structs have no default constructor
            
        } else {    
            // Else resolve the ctor
            bool fIsVarArg;
            m_symCtor = m_tType.BlueType.LookupMethod(
                s, 
                new Identifier(m_tType.BlueType.Name, this.Location), 
                alParamTypes, 
                out fIsVarArg);
        }
        
        //(m_tType.TypeRec, s);    
        CalcCLRType(s);
        
        return this;
    }
    
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_tType.BlueType.CLRType;
    }
#endregion    
}

//-----------------------------------------------------------------------------
// New array
// Expression List - specifies size of array, eval at runtime
// Initilizer List - specifies intializer list
//-----------------------------------------------------------------------------
public class NewArrayObjExp : Exp
{
    // Implicit size from init list
    public NewArrayObjExp(
        ArrayTypeSig tArrayType,    // includes rank specifiers
        ArrayInitializer aInit      // mandatory
    )
    {
        Debug.Assert(tArrayType != null);
        Debug.Assert(aInit != null);
        
        m_tFullType = tArrayType;
        this.m_arExpList = null;
        this.m_ArrayInit = aInit;

        // @todo - this is wrong
        m_filerange = tArrayType.Location;    
    }
    
    public NewArrayObjExp(
        ArrayTypeSig tArrayType,    // full array type (includes rank)
        Exp [] arExpSize,           // rank to allocate, eval at runtime
        ArrayInitializer aInit      // optional initilizer list        
    )
    {
        Debug.Assert(tArrayType != null);
        Debug.Assert(arExpSize != null);
        
        m_tFullType = tArrayType;
        this.m_arExpList = arExpSize;
        this.m_ArrayInit = aInit;
        
        // @todo - this is wrong
        m_filerange = tArrayType.Location;
    }        
    
    
  
#region Checks
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {        
        Debug.Assert(this.CLRType.IsArray);
        //Debug.Assert(TypeRec.AsArrayType.ElemType == ElemType.TypeRec);
        
        if (HasInitializerList) {
            ArrayInit.DebugCheck(s);
        }

        if (DimensionExpList != null)
        {
            foreach(Exp e in DimensionExpList)
            {
                e.DebugCheck(s);
            }
        }
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {    
        o.WriteStartElement("NewArrayObjExp");
        o.WriteAttributeString("elementtype", ElemType.ToString());
        

        if (HasInitializerList)
        {
            ArrayInit.Dump(o);
        }
            
        o.WriteEndElement();
        
    } 
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("new {0}[{1}]", this.ElemType.ToString(), this.DimensionExpList.Length);
    }
#endregion    

#region Properties & Data    
    // The array type that this expression will evaluate to
    protected ArrayTypeSig m_tFullType; 
    
    // The type of object that we're allocating
    public TypeEntry ElemType
    {
        get { return m_tFullType.ElemType; }
    }

    // Array Initializer
    ArrayInitializer m_ArrayInit;
    public ArrayInitializer ArrayInit
    {
        get { return m_ArrayInit; }
    }
/*    
    // A block statement that has the code needed to 
    // initialize the array for the Initializer list
    BlockStatement m_stmtInit;
    public BlockStatement InitStmt
    {
        get { return m_stmtInit; }
    }
  */  
    // Return true if we have an init list, else false
    public bool HasInitializerList
    {
        get { return m_ArrayInit != null; }
    }

    // Expression list for the size to allocate
    protected Exp[] m_arExpList;
    public Exp[] DimensionExpList
    {
        get { return m_arExpList; }
    }
    
    ArrayTypeEntry m_symbol;
    public ArrayTypeEntry ArraySymbol    
    {
        get { return m_symbol; }
    }
    
#endregion
                
    // Do codegen
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {        
        gen.Generate(this);
    }

#region Resolution
    // Resolve
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        if (m_symbol != null)
            return this;
            
        // Resolve the type we're allocating        
        m_tFullType.ResolveType(s);
        Debug.Assert(this.ElemType != null);
        
        // Resolve the initializer list        
        if (HasInitializerList) 
        {
            // If we specified a length, it'd better match the intializer list length.
            if (DimensionExpList != null)
            {
                Debug.Assert(this.DimensionExpList.Length == 1, "@todo -multidimensional arrays");
                Exp e = DimensionExpList[0];
                // e must be a compile time constant who's value matches the ArrayInit length
                IntExp eInt = e as IntExp;
                
                if (eInt == null)
                    ThrowError(SymbolError.MustBeCompileTimeConstant(e));
                
                if (eInt.Value != m_ArrayInit.Length)
                    ThrowError(SymbolError.NewArrayBoundsMismatch(this));
            }
        
            m_ArrayInit.Resolve(s, this.ElemType);
            
            // The ability to not specifiy a dimension list is just syntactic sugar.
            // So if we still don't have it, we'd better fill it in based of the array-init list.
            if (DimensionExpList == null)
            {
                m_arExpList = new Exp[] { new IntExp(m_ArrayInit.Length, this.Location) };                
            }        
        }
        
        Debug.Assert(DimensionExpList != null);                
        for(int i = 0; i < this.m_arExpList.Length; i++)
        {
            ResolveExpAsRight(ref m_arExpList[i], s);
        }
        

        m_symbol = new ArrayTypeEntry(m_tFullType, s);
        CalcCLRType(s);
                
        // Transform an initializer list into an CompoundExpression:
        // new T[] { e0, e1, ... en} 
        // <DeclareTemp(x), x = new T[], x[0]=e0, ... x[n]=en, x>
        if (HasInitializerList)
        {
            DeclareLocalStmtExp declare_x = new DeclareLocalStmtExp(this.ArraySymbol); 
                
            LocalExp x = declare_x.GetLocal();
            
            StatementExp [] list = new StatementExp[m_ArrayInit.Length + 2];
            list[0] = declare_x;
            list[1] = new AssignStmtExp(x, this);
            for(int i = 0; i < m_ArrayInit.Length; i++)
                list[i + 2] = new AssignStmtExp(
                    new ArrayAccessExp(
                        x,
                        new IntExp(i, null)
                    ),
                    m_ArrayInit.GetExpAt(i)
                );
            
            // Strip the ArrayInitializer off this node.    
            m_ArrayInit = null;
                            
            StatementExp c = new CompoundStmtExp(list, x);
            
            StatementExp.ResolveExpAsRight(ref c, s);
            
            return c;
        } // end has Initializer
                
        return this;
    }
        
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_symbol.CLRType;
    }
#endregion    
}
#endregion // New

//-----------------------------------------------------------------------------
// Array access
// May be an actual array access, or an indexer overload
// If this is an indexer, we expect to be transformed into methodcalls
// before we get to codegen
//-----------------------------------------------------------------------------
public class ArrayAccessExp : Exp
{
    public ArrayAccessExp(
        Exp expLeft,
        Exp expIndex
        )
    {
        Debug.Assert(expLeft != null);
        Debug.Assert(expIndex != null);
    
        m_oeLeft = expLeft;
        m_expIndex = expIndex;
    
        // @todo- this is wrong
        m_filerange = m_oeLeft.Location;                
    }

#region Checks
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        m_oeLeft.DebugCheck(s);
        m_expIndex.DebugCheck(s);
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ArrayAccessExp");
        m_oeLeft.Dump(o);
        m_expIndex.Dump(o);                    
        o.WriteEndElement();
    } 
    
#endregion

#region Properties & Data
    // Array object on the left side to be dereferenced
    protected Exp m_oeLeft;
    public Exp Left
    {
        get { return m_oeLeft; }
    }
    
    // Expression to evaluate to integer index
    protected Exp m_expIndex;
    public Exp ExpIndex
    {
        get { return m_expIndex; }
    }
    
    // Is this ArrayAccess actually an indexer?
    bool m_fIsIndexer;
    public bool IsIndexer
    {
        get  {return m_fIsIndexer; }
    }
    
    // Get the element type as a CLR type
    // If we're an indexer, this will be the return type on the property
    public System.Type CLRElemType
    {            
        get { 
            // ...
            // ...
            // There is a bug in .NET where references are not properly stripped, so
            // we have to be a little more round-about here.
            System.Type tLeft = Left.CLRType;
            string stLeft = tLeft.ToString();
            
            System.Type tNoRef;
            if (tLeft.IsByRef) 
                tNoRef = tLeft.GetElementType();
            else
                tNoRef = tLeft;
                
            string stNoRef = tNoRef.ToString();                
                
            Debug.Assert(!tNoRef.IsByRef);                
            Debug.Assert(tNoRef.IsArray);
            
            System.Type tElem = tNoRef.GetElementType();
            string stElem = tElem.ToString();

            Debug.Assert(!tElem.IsByRef); // Can't be a ref, could still be an array.
            return  tElem;
        }
    }
#endregion       

#region Generate        
    // Do codegen
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {   
        Debug.Assert(!IsIndexer, "Don't codegen Indexers as Array Access");
        gen.Generate(this);
    }
    
    public override void GenerateAsLeftPre(CodeGen.EmitCodeGen gen)
    {        
        Debug.Assert(!IsIndexer, "Don't codegen Indexers as Array Access");
        gen.GenerateAsLeftPre(this);
    }
    
    public override void GenerateAsLeftPost(CodeGen.EmitCodeGen gen)
    {   
        Debug.Assert(!IsIndexer, "Don't codegen Indexers as Array Access");     
        gen.GenerateAsLeftPost(this);
    }
    
    public override void GenerateAddrOf(CodeGen.EmitCodeGen gen)
    {
        Debug.Assert(!IsIndexer, "Don't codegen Indexers as Array Access");
        gen.GenerateAddrOf(this);
    }
#endregion

#region Resolve
    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        return ResolveInternal(s, true);
    }
    
    // Resolve
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        return ResolveInternal(s, false);
    }
    
    // Internal helper. Since the left & right cases are close enough
    // we want to merge them into a function.
    private Exp ResolveInternal(ISemanticResolver s, bool fIsLeft)
    {
        ResolveExpAsRight(ref m_oeLeft, s);
        ResolveExpAsRight(ref m_expIndex, s);
        
    // @todo - check that m_expIndex is an integer
    
    // Check for indexers:
    // If the Left is not an array, then we must be an indexer.
    // Strip references, So T[]& --> T[]
        System.Type t = m_oeLeft.CLRType;
        if (t.IsByRef)
            t = t.GetElementType();
            
        if (!t.IsArray)
        {            
            m_fIsIndexer = true;
            
            
            // If we're the leftside, we have a problem. We don't know the exp on the RS,
            // so we don't have a full signature, so we don't know what we're supposed to
            // change too. So just leave it that we're an indexer and let our parent
            // in the AST resolve us.
            // But this also means that we don't have a good thing to set our CLR type too.
            // So we just don't call CalcCLRType(). That's ok since our parent will drop
            // this node immediately anyways.
            if (fIsLeft)
            {
                return this;
            }
            
            // Rightside: get_Item(idx);
            System.Type [] alParams = new Type [] {
                this.ExpIndex.CLRType
            };
            
            TypeEntry tLeft = s.ResolveCLRTypeToBlueType(m_oeLeft.CLRType);            
            MethodExpEntry m = tLeft.LookupIndexer(m_oeLeft.Location, s, alParams, fIsLeft);
            
            
            Exp e = new MethodCallExp(
                this.Left,
                m,
                new ArgExp[] {
                    new ArgExp(EArgFlow.cIn, ExpIndex)
                },
                s);
                
            Exp.ResolveExpAsRight(ref e, s);
            return e;
            
        }
    
    
    
    
        CalcCLRType(s);
        
        return this;
    }
    
    // Type of an array access is the type of the element we return
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return CLRElemType;
    }
#endregion        
}   
    
    
#region Typeof Expression
//-----------------------------------------------------------------------------
// Typeof
//-----------------------------------------------------------------------------
public class TypeOfExp : Exp
{
    public TypeOfExp(
        TypeSig sig
    )
    {
        m_sig = sig;    
    }       
#region Properties & Data
    TypeSig m_sig;
    public TypeSig Sig
    {
        get { return m_sig; }
    }
#endregion    

#region Checks
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        m_sig.DebugCheck(s);
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("TypeOfExp");
        m_sig.Dump(o);
        o.WriteEndElement();
    } 
    
#endregion

#region Resolution
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        m_sig.ResolveType(s);
                        
        // Type
        //TypeEntry t = s.LookupSystemType("Type");
        CalcCLRType(s);
        
        return this;
    }
    
    // A typeof() exp always returns a System.Type object
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return typeof(System.Type);
    }
#endregion

#region Generate
    // Do codegen
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {   
        gen.Generate(this);
    }
#endregion

}

#endregion    
} // end namespace AST