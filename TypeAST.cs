//-----------------------------------------------------------------------------    
// AST nodes for types
//-----------------------------------------------------------------------------    

using System;
using System.Diagnostics;
using System.Xml;

using SymbolEngine;
using Blue.Public;
using CodeGen = Blue.CodeGen;

namespace AST
{

#region Base class
//-----------------------------------------------------------------------------    
// Base TypeSig
// An AST wrapper for a TypeEntry symbol
// Represent any kind of Type (array, simple, reference, etc)
//-----------------------------------------------------------------------------    

/// <summary>
/// Abstract base class for ast nodes representing types.
/// </summary>
/// <remarks>
/// <para>The following nodes derive from TypeSig:<list type="bullet">
/// <item><see cref="ResolvedTypeSig"/>- a node not associated with source that directly wraps a symbol. </item>
/// <item><see cref="SimpleTypeSig"/> - a node representing a non-decorated type from source.</item>
/// <item><see cref="ArrayTypeSig"/> - a node representing an array type</item>
/// <item><see cref="RefTypeSig"/> - a node representing a reference to a type.</item>
/// </list></para>
/// <para>A TypeSig represents the usage of a type in the AST. A <see cref="ClassDecl"/> represents
/// the definition of a type in the AST. A <see cref="SymbolEngine.TypeEntry"/> is the symbol
/// representing the type.</para>
/// </remarks>
public abstract class TypeSig : Node
{
#region Virtuals - Symbols
    // From a Type, we want to be able to get the BLUE & CLR symbols
    public abstract TypeEntry BlueType
    {
        get;
    }
    
    public virtual Type CLRType
    {
        get {
            return BlueType.CLRType;
        }
    }
#endregion

#region Virtual - Getting Derived States    
    // Given a generic type, we need to be able to find out what it really is and
    // safely get it to a more derived state.
    // Derived classes can override with a more direct implementation.
    public virtual bool IsArray
    {
        get { 
            return (CLRType.IsArray); 
        }
    }
    
    // Assume that if this isn't overriden, we're not an array.
    public virtual ArrayTypeSig AsArraySig
    {
        get {            
            // We would like this, but the debugger will evaluate it in a watch window and die.
            // So we'll just let the null-ref exception warn us.
            Debug.Assert(false); // Not implemented
            return null;
        }
    }
#endregion        
    
#region Resolution    
    // Resolution
    public abstract void ResolveType(ISemanticResolver s);
#endregion    
    
#region Checks
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement(this.GetType().ToString());
        
        TypeEntry t = BlueType;
        o.WriteAttributeString("BlueType", t.ToString());
        
        System.Type t2 = CLRType;
        o.WriteAttributeString("CLRType", t2.ToString());
                
        o.WriteEndElement();
    }
    
    public override void DebugCheck(ISemanticResolver s)
    {        
        BlueType.DebugCheck(s);        
    }
    
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("<T>{0}", ToString());
    }
    
    public override string ToString()
    {
        if (BlueType == null)
            return "Unresolved";
        else if (CLRType == null)
            return this.BlueType.ToString();
        else                
            return this.CLRType.ToString();
    }
#endregion       
}

#endregion Base Class

//-----------------------------------------------------------------------------    
// Class to represent a flyweight around a CLR-type.
// @todo - If we expect this to handle ref types, then we just have to make it not
// derive from NonRefTypeSig.
//-----------------------------------------------------------------------------    
public class ResolvedTypeSig : NonRefTypeSig
{
#region Construction        
    // Expose constructor that takes an already resolved type
    // Very useful when we build/modify parts of the AST during resolution
    public ResolvedTypeSig(TypeEntry t)
    {
        Debug.Assert(!t.IsRef, "Don't expect ref types");
        Debug.Assert(t != null);
        m_type = t;
    }
    
    public ResolvedTypeSig(System.Type t, ISemanticResolver s)
    {
        Debug.Assert(!t.IsByRef, "Don't expect ref types");
        Debug.Assert(t != null);
        Debug.Assert(s != null);
        m_type = s.ResolveCLRTypeToBlueType(t);
    }
#endregion
    
    // Since we already have a symbol, we don't have anything to do
    // (symbols are resolved);
    public override void ResolveType(ISemanticResolver s)
    {   
    }

    public override ArrayTypeSig AsArraySig
    {
        get { 
            Debug.Assert(m_type.IsArray);
            Debug.Assert(false, "@todo - implement this");
            return null;
        }        
    }
    
#region Properties & Data
    // Get the resolved type
    protected TypeEntry m_type;
    public override TypeEntry BlueType
    {
        get { return m_type; }
    }
    
    public override Type CLRType
    {
        get { return m_type.CLRType; }
    }
#endregion
}


//-----------------------------------------------------------------------------    
// Common base class for types that are not references (includes Simple & Array)
//-----------------------------------------------------------------------------    
public abstract class NonRefTypeSig : TypeSig
{
    
}

//-----------------------------------------------------------------------------    
// Simple type. May be class, interface, struct, enum.
// Get this from the parse tree.
//-----------------------------------------------------------------------------    
public class SimpleTypeSig : NonRefTypeSig
{
#region Construction
    public SimpleTypeSig(Exp e)
    {
        if (e != null)
            m_filerange = e.Location;
            
        m_oeValue = e;           
    }
#endregion    
    
#region Properties & Data   
    // SimpleType is never an array 
    public override bool IsArray
    {
        get { return false; }
    }
                   
    // This object is completely hidden    
    protected Exp m_oeValue;
    
    // Get the resolved type
    TypeEntry m_type;
    public override TypeEntry BlueType
    {
        get { return m_type; }
    }
#endregion
    
    // Semantic resolution
    public override void ResolveType(ISemanticResolver s)
    {
        if (m_type != null)
            return;
            
        Exp.ResolveExpAsRight(ref m_oeValue, s);
                
        Debug.Assert(m_oeValue is TypeExp);                
        m_type = ((TypeExp) m_oeValue).Symbol;
    }
    
#region Checks    
/*
    public override string ToString()
    {
        if (m_type != null)
            return "TypeSig:" + m_type.ToString();
        else
            return "TypeSig:<unresolved>";        
    }
*/    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("TypeSig");
                
        if (m_oeValue != null)
            m_oeValue.Dump(o);
                    
        o.WriteEndElement();
    }
    
    public override void DebugCheck(ISemanticResolver s)
    {        
        Debug.Assert(m_type != null);
        TypeEntry t = BlueType;
        
        if (t.IsRef)
            return;
        
        System.Type clrType = t.CLRType;
        
        // Make sure that our CLR type matches our TypeEntry
        if (clrType != null)
        {
            // @todo - Enums aren't entered into the hash.
            if (!(t is EnumTypeEntry))
            {
                TypeEntry t2 = s.ResolveCLRTypeToBlueType(clrType);
                Debug.Assert(t == t2);
            }
        }
        if (clrType == null)
        {   
            // Even now, the only way we can have no clr type is if we
            // are a user declared class
            Debug.Assert(t.Node != null);            
        }
                
    } // DebugCheck   
#endregion     
}

//-----------------------------------------------------------------------------    
// Array
//-----------------------------------------------------------------------------    
public class ArrayTypeSig : NonRefTypeSig
{
#region Construction
    // ArrayType of base T and given rank
    public ArrayTypeSig(NonRefTypeSig tElem, int dimension)
    {
        Debug.Assert(tElem != null);
        
        m_filerange = tElem.Location;
        m_cDimension = dimension;
        m_sigBase = tElem;
    }
    
    // Create an array node given an existing CLR array type
    public ArrayTypeSig(System.Type tArray, ISemanticResolver s)    
    {
        Debug.Assert(tArray != null);
        
        m_filerange = null;
        m_cDimension = tArray.GetArrayRank();        
        Debug.Assert(m_cDimension == 1, "@todo - only 1d arrays currently implemented");
        
        m_ArrayTypeRec = s.ResolveCLRTypeToBlueType(tArray).AsArrayType;
        m_sigBase = null; // left as null.
    }
    /*
    public ArrayTypeSig(ResolvedTypeSig tElem, int dimension)
    {
        Debug.Assert(tElem != null);
        
        m_filerange = tElem.Location;
        m_cDimension = dimension;
        
        Debug.Assert(!tElem.CLRType.IsByRef, "Can't make an array of references");
        m_sigBase = null;
        m_ArrayTypeRec = 
        Debug.Assert(m_ArrayTypeRec != null);
    }
    */
#endregion

#region Properties
    NonRefTypeSig m_sigBase;
/*
    public TypeSig ElemTypeNode
    {
        get { return m_sigBase; }
    }
*/
    public TypeEntry ElemType
    {
        get {
            if (m_ArrayTypeRec == null)
                return m_sigBase.BlueType;
            return m_ArrayTypeRec.ElemType;
        }
    }

    int m_cDimension;
    public int Dimension
    {
        get { return m_cDimension;}
    }

    protected ArrayTypeEntry m_ArrayTypeRec;
    public ArrayTypeEntry ArrayTypeRec
    {
        get { return m_ArrayTypeRec; }
    }

#endregion

    // Get the resolved type
    // Same as ArrayTypeRec, but not type safe
    public override TypeEntry BlueType
    {
        get { return m_ArrayTypeRec; }
    }
        
    // ArrayType is always an array 
    public override bool IsArray
    {
        get { return true; }
    }
    
    // trivially true..
    public override ArrayTypeSig AsArraySig
    {
        get { return this; }
    }
    
#region Checks

#if false            
    public override string ToString()
    {
        // First get the non-array type

        TypeSig t = m_sigBase;
        while(t.IsArray)
        t = t.AsArraySig.ElemType;

        string stBase = t.ToString();
            
        t = this;
        while(t.IsArray)
        {
            stBase = stBase + "[";
            for(int i = 0; i < t.AsArraySig.Dimension - 1; i++)
            stBase += ",";
            stBase += "]";
                    
            t = t.AsArraySig.ElemType;
        }
        
        return stBase;
    }
#endif        
        
    public override void DebugCheck(ISemanticResolver s)
    {
        ElemType.DebugCheck(s);
        Debug.Assert(m_ArrayTypeRec != null);
            
        // Parallel data structures:
        // (ArrayTypeSig : TypeSig) as (ArrayTypeEntry : TypeEntry)
        // So verify the integrity there
        if (m_sigBase != null)
        {
            Debug.Assert(m_sigBase.BlueType == m_ArrayTypeRec.ElemType);
        }
                
    } // DebugCheck

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ArrayTypeSig");
        o.WriteAttributeString("Dimension", m_cDimension.ToString());
        o.WriteAttributeString("fulltype", ToString());
                    
        if (m_sigBase != null)
            m_sigBase.Dump(o);
                        
        o.WriteEndElement();
    }
#endregion
    // Semantic resolution
    public override void ResolveType(ISemanticResolver s)
    {
        if (m_ArrayTypeRec != null)
            return;
            
        m_sigBase.ResolveType(s);
        m_ArrayTypeRec = new ArrayTypeEntry(this, s);
    }
}

#region Reference Type
//-----------------------------------------------------------------------------    
// Reference
//-----------------------------------------------------------------------------    
public class RefTypeSig : TypeSig
{
#region Construction
    // Create around an existing type
    public RefTypeSig(NonRefTypeSig sig)        
    {
        Debug.Assert(sig != null);
        m_tElem = sig;
    }
#endregion

#region Properties & Data
    // We hold a reference to a non-reference type.
    // Can't have reference to references. (These ain't pointers!)
    NonRefTypeSig m_tElem;

    // Track our internal symbol
    RefTypeEntry m_tRefEntry;

    // Get the blue & clr symbols
    public override TypeEntry BlueType
    {
        get { return m_tRefEntry; }
    }
        
    public override Type CLRType
    {
        get { return m_tRefEntry.CLRType; }
    }    
    
    // References are pretty transparent. So For array checks, we just pass right
    // through to our element type.
    public override bool IsArray
    {
        get { 
            return m_tElem.IsArray; 
        }
    }
    
    public override ArrayTypeSig AsArraySig
    {
        get {
            return m_tElem.AsArraySig;
        }
    }
#endregion

    // Semantic resolution
    public override void ResolveType(ISemanticResolver s)
    {        
        if (m_tRefEntry != null)
            return;
            
        m_tElem.ResolveType(s);
        
        m_tRefEntry = new RefTypeEntry(m_tElem.BlueType, s);
    }
    
#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        base.DebugCheck(s);
        Debug.Assert(CLRType.IsByRef, "Ref type must wrap a ref");
        Debug.Assert(!m_tElem.BlueType.CLRType.IsByRef, "Can't have ref to ref");
    }
#endregion    
    
} // end RefTypeSig

#endregion Reference Type




#region Array Initializer

//-----------------------------------------------------------------------------    
// Array Initilizer - list of either expressions or nested ArrayInitliazers
// ArrayInitializers are not expressions, they're just part of the NewObjExp.
//-----------------------------------------------------------------------------    
public class ArrayInitializer : Node
{
    // Create an array initializer from the list
    public ArrayInitializer(System.Collections.ArrayList al)
    {
        m_list = new Node[al.Count];
        for(int i = 0; i < m_list.Length; i++)
            m_list[i] = (Node) al[i];
    }
    
    public ArrayInitializer(Node[] list)
    {
        m_list= list;
    }
    
    Node [] m_list;
    
    protected Node [] List
    {
        get { return m_list; }
    }
    
    public int Length
    {
        get { return m_list.Length; }
    }
    
    public Exp GetExpAt(int idx)
    {
        return (Exp) m_list[idx];
    }
    
#region checks
    public override void DebugCheck(ISemanticResolver s)    
    {
        foreach(Node n in List)
        {
            n.DebugCheck(s);
        }
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ArrayInitializer");
        
        foreach(Node n in List)
        {
            n.Dump(o); 
        }
                            
        o.WriteEndElement();
    }
#endregion

#region Resolution
    // Resolve this array initializer list.
    // Provide with the type that we expect each element in the list to be
    public void Resolve(ISemanticResolver s, TypeEntry tExpected)
    {
        // Each node must either be an expression or a nested array initializer list
        //foreach(Node n in List)
        for(int i = 0; i < m_list.Length; i++)
        {
            Node n = m_list[i]; 
            if (n is Exp)
            {
                Exp e = (Exp) n;
                Exp.ResolveExpAsRight(ref e, s);
                m_list[i] = e;
                
                s.EnsureAssignable(e, tExpected.CLRType);
            } 
            
            else if (n is ArrayInitializer)
            {
                Debug.Assert(tExpected.IsArray); // @todo -legit
                
                ArrayInitializer a = (ArrayInitializer) n;
                TypeEntry tNested = tExpected.AsArrayType.ElemType;
                
                a.Resolve(s, tNested);
            }
            
            else 
            {
                // Error
                Debug.Assert(false); // @todo - legit
            }
        
        }
    }


#endregion
}

#endregion ArrayInitializer


} // namespace AST