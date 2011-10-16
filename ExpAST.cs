//-----------------------------------------------------------------------------
// File: ExpAST.cs
//
// Description: All Expression nodes in the AST
// See AST.cs for details
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Xml;

using SymbolEngine;
using Blue.Public;
using CodeGen = Blue.CodeGen;


namespace AST
{
#region Exp base class
//-----------------------------------------------------------------------------        
// General expression
//-----------------------------------------------------------------------------    

/// <summary>
/// Abstract base class for all expressions.
/// </summary>
/// <remarks>
/// <para>Provides virtual functions for Resolving as a left or right hand side expressions.
/// All expressions have an associated System.Type (the <see cref="Exp.CLRType"/> property).</para>
/// <para>When an expression is resolved, it can mutate itself into a new expression.The 
/// <see cref="Exp.ResolveExpAsLeft"/> and <see cref="Exp.ResolveExpAsRight"/> instance members
/// return a exp. If the expression was not mutated, they return the 'this' pointer. If a mutation
/// does occur, they could return a totally new, resolved, exp node. This is useful for code
/// transforms. (ex: constant-folding).</para>
/// </remarks>
public abstract class Exp : Node
{
    //.........................................................................
    // Semantic resolution. Resolve the exp as either a LHS or RHS value.
    //.........................................................................
    
    // Resolve the expression as a LHS value
    public static void ResolveExpAsLeft(ref Exp e, ISemanticResolver s)
    {
        e = e.ResolveExpAsLeft(s);
        Debug.Assert(e != null);
    }
    
    protected virtual Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        // Default impl - most expressions aren't valid on the LHS.
        //s.ThrowError_NotValidLHS(this.Location);
        ThrowError(SymbolError.NotValidLHS(this.Location));
        return null;
    }
   
    // Resolve expression as a RHS value. The exp node can totally change on us
    // (operator overloading, constant folding, etc), so we must pass as a ref
    public static void ResolveExpAsRight(ref Exp e, ISemanticResolver s)
    {
        // Debugging helper. Useful when we want to break when resolving 
        // a particular symbol.
        
        #if false   
             
        // Set lTarget to a symbol that we want to see resolved.
        FileRange lTarget = new FileRange("SemanticChecker.cs", 143, 39, 143, 43);
        if (lTarget.Equals(e.Location))
        {
            System.Diagnostics.Debugger.Break();
        }
        
        #endif
        e = e.ResolveExpAsRight(s);
        Debug.Assert(e != null);
    }
   
    // Helper to resolve the expression as RHS value.
    // Return the newly-resolved exp node. In most cases, that will just be
    // the old node, but in a resolved state. For things like operator overloading,
    // it will be a totally different node.
    // Note that we must expose this version because 'ref Exp' is not assignable
    // with any derived expression.
    protected abstract Exp ResolveExpAsRight(ISemanticResolver s);    

    //.........................................................................
    // Code generation
    // To codegen something like: 'a=b', we need to be able to do:
    // [a]lhs_pre   // emit code to load context for the assignment
    // [b]rhs       // emit code to load value to be assigned
    // [a]lhs_post  // emit code to actually store the results
    //
    // Most expressions will only need a rhs.
    //.........................................................................
        
    public virtual void GenerateAsLeftPre(CodeGen.EmitCodeGen gen)
    {
        Debug.Assert(false, "Didn't implement GenerateAsLeftPre");
    }    
    public virtual void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        Debug.Assert(false, "Didn't implement GenerateAsRight");
    }
    public virtual void GenerateAsLeftPost(CodeGen.EmitCodeGen gen)
    {
        Debug.Assert(false, "Didn't implement GenerateAsLeftPost");
    }
    
    
    //.........................................................................
    // Code Generation - Generate the address of this expression    
    //.........................................................................
    public virtual void GenerateAddrOf(CodeGen.EmitCodeGen gen)
    {
        // Can't get the addr of most expressions
        //Debug.Assert(false, "Didn't implemented GenerateAddrOf");
        // Default impl, evaluate as RHS, store in temp, gen addr of that.
        gen.GenerateAddrOf(this);
    }
    
    // @todo - not really working yet. We don't do constant folding.
    //.........................................................................
    // Simplifying an expression that we pass in. Pass out the simplified
    // expression (or the same thing we passed in if we can't simplify)
    //.........................................................................
    public static void TrySimplify(ref Exp e)
    {
        e = e.TrySimplify();
    }

    // @todo - not really working yet.
    // Actual workhorse,  Default behavior, can't simplify, so return the original
    protected virtual Exp TrySimplify()
    {
        return this;
    }
    
    // Return true iff the given expression is allowed to have a null type
    public static bool CanBeNullType(Exp e)
    {
        return (e is NullExp) || (e is IfExp) || (e is ArgExp) || (e is NamespaceExp);
    }
    
    // Determine the CLR Type of this expression
    // As a convenience, return the clr type to saves us from having to 
    // call CLRType
    public System.Type CalcCLRType(ISemanticResolver s)
    {
        Type tOld = m_clrType;
        m_clrType = CalcCLRTypeHelper(s);   
        Debug.Assert(m_clrType != null || Exp.CanBeNullType(this));
     
        // Assert that if we called this multiple times, the value hasn't changed on us
        Debug.Assert(((tOld == null)) || (tOld == m_clrType));

        // @todo - we don't resolve pointers yet....
        // The only place we can use them is in evaluating a MethodPtr for a delegate ctor.
        if (m_clrType == Type.GetType("System.IntPtr"))
        {
            Debug.Assert(this is MethodPtrExp);
            return m_clrType;
        }


        // @todo - move this into a place w/ better perf...
        // EnsureResolved
        if (m_clrType != null)
        {
            TypeEntry t = s.ResolveCLRTypeToBlueType(m_clrType);
            t.EnsureResolved(s);
        }

        return m_clrType;
    }

    // This gets called by CalcCLRType()
    // Derived classes override this and return the clr type    
    protected virtual Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        // This should be implemented
        Debug.Assert(false, "@todo - impl");
        return null;
    }
    
    public System.Type CLRType
    {
        get { return m_clrType; }
    }

    private System.Type m_clrType;
}
#endregion


//-----------------------------------------------------------------------------        
// Expression to represent an argument to a method call
//-----------------------------------------------------------------------------        

/// <summary>
/// Node for an argument in a method call
/// </summary>
/// <remarks>
/// An ArgExp represents each argument in a <see cref="MethodCallExp"/>.
/// <para><children>Children:<list type="bullet">
/// <item><see cref="EArgFlow"/> - specify if this is an 'in', 'out', or 'ref' parameter.</item>
/// <item><see cref="Exp"/> - specify the expression for this parameter</item>
/// </list></children></para>
/// </remarks>
public class ArgExp : Exp
{
    public ArgExp(AST.EArgFlow eFlow, Exp exp)
    {
        Debug.Assert(exp != null);        
        Debug.Assert(!(exp is ArgExp), "ArgExp should never wrap an ArgExp");
        m_exp = exp;
        m_eFlow = eFlow;
    }

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        m_exp.DebugCheck(s);
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ArgExp");
        o.WriteAttributeString("flow", m_eFlow.ToString());
        m_exp.Dump(o);
        o.WriteEndElement();
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("<{0}>", this.m_eFlow.ToString());
        this.m_exp.ToSource(sb);
    }
#endregion    
    
#region Properties & Data
    EArgFlow m_eFlow;
    public EArgFlow Flow
    {
        get { return m_eFlow; }
    }
    
    Exp m_exp;
    public Exp InnerExp
    {
        get { return m_exp; }
    }
#endregion

#region Resolution
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        // Get the type of our inner
        Type t = m_exp.CalcCLRType(s);
        
        // If we've already got a reference, then we're ok.
        // Also ok if we're null (since literals can't be passed as a ref/out)
        if ((t == null) || (t.IsByRef))
            return t;
        
        // Ref & Out params are actually type 'T&', not 'T'
        if (Flow == EArgFlow.cRef || Flow == EArgFlow.cOut)
        {
            t = s.GetRefToType(t);        
        }
        
        return t;
    }

    // Resolve the expression as a LHS value
    protected override Exp ResolveExpAsLeft(ISemanticResolver s)
    {
        Debug.Assert(m_eFlow == EArgFlow.cOut | m_eFlow == EArgFlow.cRef);
        ResolveExpAsLeft(ref m_exp, s);
        CalcCLRType(s);
        return this;
    }
   
    // Resolve the expression as RHS value.
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        //Debug.Assert(m_eFlow == EArgFlow.cIn | m_eFlow == EArgFlow.cRef);
        Exp.ResolveExpAsRight(ref m_exp, s);
        CalcCLRType(s);
        
        return this;
    }
#endregion

    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
    /*
        if (Flow == EArgFlow.cIn)
            m_exp.GenerateAsRight(gen);
        else
            m_exp.GenerateAddrOf(gen);            
    */
        gen.GenerateArg(this);            
    }

}


//-----------------------------------------------------------------------------        
// Expression that represents a type
// Should only be used as an intermediate node.
//-----------------------------------------------------------------------------        
public class TempTypeExp : Exp
{
    public TempTypeExp(TypeSig t)
    {
        m_tSig = t;
    }

    TypeSig m_tSig;
    public TypeSig TypeSigRec
    {
        get { return m_tSig; }
    }

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(false);
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        Debug.Assert(false);
    }

    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("<T>{0}", this.m_tSig.ToString());
    }
#endregion

#region Resolution
    // Semantic resolution
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        Debug.Assert(false);        
        return null;
    }    

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        Debug.Assert(false);
    }
    
    
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {        
        Debug.Assert(false);
        return null;
    }
#endregion
} // end TypeExp


#region Statement Expressions
/// <summary>
/// Abstract base class for expression that have sideeffects and can be used as statements.
/// </summary>
/// <remarks>
/// <para>Some expressions, like assignment, methodcalls, and the ++,-- operators
/// have sideeffects and so they can be used as a statement.</para>
/// <para>A StatementExp can be used directly as an expression, or it can be held in a 
/// <see cref="ExpStatement"/> to be used as a statement.</para>
/// </remarks>
public abstract class StatementExp : Exp
{   
    // Statement expressions can be generated as a statement or expression.
    // As an expression, they must leave a value on the top of the stack
    // As a statement, they don't leave a value on top (and hence may
    // be able to have a more efficient impl). 
    // Normal Generate() will generate as a RHS expression.    
    
    public virtual void GenerateAsStatement(CodeGen.EmitCodeGen gen)
    {        
    // Default impl generates as a RHS exp and just pops the value off    
        gen.GenerateAsStatement(this);
    }
    
    // Allow safe resolving on Statement Exp. Allowed to change to another StmtExp
    public static void ResolveExpAsRight(ref StatementExp se, ISemanticResolver s)
    {
        Exp e = se;
        Exp.ResolveExpAsRight(ref e, s);
        if (e is StatementExp)
            se = (StatementExp) e;
        else
            Debug.Assert(false, "Shouldn't have resolved to a non-stmtexp");            
            
        Debug.Assert(se != null);
    }
}

#if true
//-----------------------------------------------------------------------------
// Declares a temporary local var
// We must execute this as a statement to actually create the local,
// And then we can use the GetLocal() to get a LocalExp to refer to the temp.
//-----------------------------------------------------------------------------
public class DeclareLocalStmtExp : StatementExp
{
    public DeclareLocalStmtExp(System.Type t, ISemanticResolver s)
        : this(s.ResolveCLRTypeToBlueType(t))
    {           
    }
    
    public DeclareLocalStmtExp(TypeEntry t)    
    {
        LocalVarExpEntry l = new LocalVarExpEntry();
        l.m_strName = ".temp_local_" + m_count;
        m_count++;
        l.m_type = t;
        
        m_exp = new LocalExp(l);
    }

#region Properties & Data
    LocalExp m_exp;
    public LocalExp GetLocal()
    {
        return m_exp;
    }

#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {        
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("DeclareLocalStmtExp");
                
        o.WriteEndElement();
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {   
        sb.Write("<TempLocal>{0}", this.m_exp.Symbol.Name);   
    }
#endregion
    
#region Resolution
    // There's nothing to resolve here. No real action happens until
    // codegen...
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {           
        return this;
    }
    static int m_count = 0;
#endregion   

#region Generate
    // We should only do this as a Statement (since it produces side-effects)
    public override void GenerateAsStatement(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsStatement(this);
    }
#endregion
}

//-----------------------------------------------------------------------------
// Compound expressions
// useful in code transformations.
// Has a list of statement expressions that get generated. And then generates
// the last expression and uses that value.
//-----------------------------------------------------------------------------
public class CompoundStmtExp : StatementExp
{
    // <e0, e1, e2 .... en>
    // e_0 .. e_n-1 are StatementExp, that generate as Statements (to produce side effects)
    // e_n is a normal expression, generated as an expression, to produce a value
    public CompoundStmtExp(
        StatementExp [] eList,
        Exp eLast
    )
    {
        Debug.Assert(eList != null);
        Debug.Assert(eLast != null);
        
        m_eList = eList;
        m_eLast = eLast;
    }        
#region Properties & Data
    StatementExp [] m_eList;
    Exp m_eLast;
#endregion

#region Resolution
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        for(int i = 0; i < m_eList.Length; i++)
            StatementExp.ResolveExpAsRight(ref m_eList[i], s);

        Exp.ResolveExpAsRight(ref m_eLast, s);
            
        CalcCLRType(s);
                    
        return this;
    }
    
    // Type of an assignment is the type of the Right Side    
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_eLast.CLRType;
    }
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        foreach(Exp e in m_eList)
            e.DebugCheck(s);
        m_eLast.DebugCheck(s);
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("CompoundStmtExp");
    
        o.WriteStartElement("Statements");
        foreach(Exp e in m_eList)
            e.Dump(o);
        o.WriteEndElement();
        
        m_eLast.Dump(o);
                
        o.WriteEndElement();
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write('[');
        foreach(StatementExp e in m_eList)
        {
            e.ToSource(sb);
            sb.Write(',');
        }
        m_eLast.ToSource(sb);
        sb.Write(']');
    }
#endregion

#region CodeGen
    // For all CodeGen variations on a CompoundStatement, we want to 
    // first generate the StmtExp list to get the sideffects, and
    // then fall through to the last expression and use that
    // as the real CodeGen target.

    // Code generation as an expression
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        // Generate the StmtExp to get their sideffects
        foreach(StatementExp e in m_eList)
            e.GenerateAsStatement(gen);
                    
        m_eLast.GenerateAsRight(gen);
    }
    
    // We can only generate as a Statement if the last thing is also
    // a StatementeExp
    public override void GenerateAsStatement(CodeGen.EmitCodeGen gen)
    {
        // Generate the StmtExp to get their sideffects
        foreach(StatementExp e in m_eList)
            e.GenerateAsStatement(gen);
            
        Debug.Assert(m_eLast is StatementExp);
        StatementExp se = (StatementExp) m_eLast;
                            
        se.GenerateAsStatement(gen);
    }
    
    // Generate the address
    public override void GenerateAddrOf(CodeGen.EmitCodeGen gen)
    {        
        // Still generate all the StmtExp normally because we 
        // need their side-effects
        foreach(StatementExp e in m_eList)
            e.GenerateAsStatement(gen);
            
        // Use the address of the last expression.            
        m_eLast.GenerateAddrOf(gen);
    }
    
#endregion

}
#endif


#if false
//-----------------------------------------------------------------------------
// OpEqual exoressions: +=, *=, /=, -=, %=
//-----------------------------------------------------------------------------
public class OpEqualStmtExp : StatementExp
{
#region Construction
    public OpEqualStmtExp(Exp expLeft, Exp expRight, BinaryExp.BinaryOp op)
    {   
        Debug.Assert(false, "OpEqualStmtExp is obsolete"); //
        m_op = op;
        m_eLeft = expLeft;
        m_expRight = expRight;        
    }
#endregion    

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(Left != null);
        Debug.Assert(Right != null);
    
        Left.DebugCheck(s);
        Right.DebugCheck(s);            
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("OpEqualStmtExp");
    
        Left.Dump(o);
        Right.Dump(o);
                
        o.WriteEndElement();
    }
#endregion

#region Properties & Data
    BinaryExp.BinaryOp m_op;
    public BinaryExp.BinaryOp Op
    {
        get { return m_op; }
    }

    protected Exp m_eLeft;
    public Exp Left
    {
        get { return m_eLeft; }
    }
    
    protected Exp m_expRight;
    public Exp Right
    {
        get { return m_expRight; }
    }
#endregion

#region Resolution
    // Semantic resolution
    // "a X= b" is semantically equivalent to "a = a X b"
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        // Note that the left side ("a") of "a X= b" is both a 
        // Left & Right side value
        ResolveExpAsLeft(ref this.m_eLeft, s);
        ResolveExpAsRight(ref this.m_expRight, s);
       
        
        CalcCLRType(s);
    
        // Ensure type match
        s.EnsureAssignable(m_expRight, Left.CLRType);
        
        return this;
    }
    
    // Type of an assignment is the type of the Right Side    
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return Left.CLRType;
    }
#endregion

    // Code generation as an expression
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
    
    public override void GenerateAsStatement(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsStatement(this);
    }
}

#endif

//-----------------------------------------------------------------------------    
// Assignment statement
//-----------------------------------------------------------------------------    
public class AssignStmtExp : StatementExp
{
#region Construction
    public AssignStmtExp(Exp expLeft, Exp expRight)
    {
        Debug.Assert(expLeft != null);
        Debug.Assert(expRight != null);
    
        m_oeLeft = expLeft;
        m_expRight = expRight;
    
        //m_filerange = FileRange.Merge(expLeft.Location, expRight.Location);
    }
#endregion

#region Checks
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_oeLeft != null);
        Debug.Assert(m_expRight != null);
    
        m_oeLeft.DebugCheck(s);
        m_expRight.DebugCheck(s);
            
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("AssignStmtExp");
    
        m_oeLeft.Dump(o);
        m_expRight.Dump(o);
                
        o.WriteEndElement();
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        m_oeLeft.ToSource(sb);
        sb.Write("=");
        m_expRight.ToSource(sb);
    }
#endregion

#region Resolution
    
    // Semantic resolution.
    // This is where we check for Set-Property transformations (where an
    // assignment gets changed into a methodcall)
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        // Resolve the leftside of the operator                
        Exp.ResolveExpAsLeft(ref m_oeLeft, s);
        
        
        // Event transform  actually occurs in the assignment node.
        // A.e = A.e + d --> A.add_e(d)            
        // We have to do this before we resolve the RHS of the operator (Since we
        // can't resolve events as a RHS).
        if (m_oeLeft is EventExp)
        {   
            EventExp nodeEvent = (EventExp)m_oeLeft;
            EventExpEntry e = nodeEvent.Symbol;         
            
            // Here we just do some asserts.
            BinaryExp b = this.m_expRight as BinaryExp;
            Debug.Assert(b != null, "bad formed event +=,-=");  
         
            // By now, we know we have something of the form A = B + C   
            // Make sure that A=B. Since we resolved A as left, must resolve B as left too.
            Exp eTempLeft = b.Left;
            Exp.ResolveExpAsLeft(ref eTempLeft, s);
            Debug.Assert(eTempLeft is EventExp);
            Debug.Assert(Object.ReferenceEquals(((EventExp) eTempLeft).Symbol, e)); // symbols should be exact references
            
            // Resolve C (the delegate that we're adding to the event)
            Exp eTempRight = b.Right;
            Exp.ResolveExpAsRight(ref eTempRight, s);            
            Debug.Assert(AST.DelegateDecl.IsDelegate(eTempRight.CLRType), "Event only ops w/ delegates"); // @todo -legit/            
            
            Debug.Assert(b.Op == BinaryExp.BinaryOp.cAdd || b.Op == BinaryExp.BinaryOp.cSub);
                
            
            MethodExpEntry m2 = (b.Op == BinaryExp.BinaryOp.cAdd) ? e.AddMethod : e.RemoveMethod;
                
            Exp e2 = new MethodCallExp(
                nodeEvent.InstanceExp,
                m2,
                new ArgExp[] {
                    new ArgExp(EArgFlow.cIn, eTempRight)
                },
                s
            );
                    
            Exp.ResolveExpAsRight(ref e2, s);
            return e2;            
        }
        
        Exp.ResolveExpAsRight(ref m_expRight, s);
                        
        
        // Check for calling add_, remove on events
        // a.E += X 
        // a.E = a.E + X (parser transforms)
        // if E is a delegate, and RHS is structured like E + X
        // then transform to a.add_E(X) or a.remove_E(x)
        
        // @todo - use the EventInfo to get exact add / remove functions
        if (DelegateDecl.IsDelegate(m_oeLeft.CLRType))
        {   
            // Events can only exist on a class
            AST.FieldExp f = m_oeLeft as FieldExp;
            if (f == null)
                goto NotAnEvent;
                
            Exp eInstance = f.InstanceExp; // ok if static                
            
            BinaryExp rhs = m_expRight as BinaryExp;
            if (rhs == null)
                goto NotAnEvent;            
            
            // Check if RHS is a.E + X
            if ((rhs.Left != m_oeLeft) || (rhs.Right.CLRType != rhs.Left.CLRType))
                goto NotAnEvent;
            
                        
            string stEventName = f.Symbol.Name;
            string stOpName;
            if (rhs.Op == BinaryExp.BinaryOp.cAdd)
                stOpName = "add_" + stEventName;
            else if (rhs.Op == BinaryExp.BinaryOp.cSub)
                stOpName = "remove_" + stEventName;                        
            else
                goto NotAnEvent;                
             
            // a.add_E(X);    
            Exp e = new MethodCallExp(
                eInstance,
                new Identifier(stOpName),
                new ArgExp[] {
                    new ArgExp(EArgFlow.cIn, rhs.Right)
                }
            );            
            
            Exp.ResolveExpAsRight(ref e, s);
            e.SetLocation(this.Location);    
            return e;
                        
            
        NotAnEvent:
            ;            
        }
        
        // Check for set-indexer
        if (m_oeLeft is ArrayAccessExp)
        {
            ArrayAccessExp a = m_oeLeft as ArrayAccessExp;
            if (a.IsIndexer) 
            {
                // Leftside: get_Item(idx, value);
                System.Type [] alParams = new Type [] {
                    a.ExpIndex.CLRType,
                    m_expRight.CLRType
                };
            
                TypeEntry t = s.ResolveCLRTypeToBlueType(a.Left.CLRType);            
                MethodExpEntry m = t.LookupIndexer(a.Left.Location, s, alParams, true);
                
                Exp e = new MethodCallExp(
                    a.Left,
                    m,
                    new ArgExp[] { 
                        new ArgExp(EArgFlow.cIn, a.ExpIndex),
                        new ArgExp(EArgFlow.cIn, m_expRight) 
                    },
                    s);
                
                Exp.ResolveExpAsRight(ref e, s);
                e.SetLocation(this.Location);    
                return e;
            }        
        }
        
        // Check for transforming properties into MethodCalls
        if (m_oeLeft is PropertyExp)
        {
            PropertyExp p = (PropertyExp) m_oeLeft;
            
            Exp e = new MethodCallExp(
                p.InstanceExp, 
                p.Symbol.SymbolSet, 
                new ArgExp[] { 
                    new ArgExp(EArgFlow.cIn, m_expRight) 
                },
                s);
                
            Exp.ResolveExpAsRight(ref e, s);
            e.SetLocation(this.Location);
            return e;
        }
        
        CalcCLRType(s);
    
        // Ensure type match        
        s.EnsureAssignable(m_expRight, m_oeLeft.CLRType);        
        
        return this;
    }
    
    // Type of an assignment is the type of the Right Side    
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return m_oeLeft.CLRType;
    }
#endregion

    // Code generation as an expression
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
    
    public override void GenerateAsStatement(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsStatement(this);
    }
    
#region Properties & Data
    protected Exp m_oeLeft;
    public Exp Left
    {
        get { return m_oeLeft; }
    }
    
    protected Exp m_expRight;
    public Exp Right
    {
        get { return m_expRight; }
    }
#endregion    
}

//-----------------------------------------------------------------------------    
// Pre & Post inc / dec
//-----------------------------------------------------------------------------    
public class PrePostIncDecStmtExp : StatementExp
{
#region Construction
    public PrePostIncDecStmtExp(
        Exp e,
        bool fPre, bool fInc)
    {
        m_exp = e;
        m_fPre = fPre;
        m_fInc = fInc;
    }
#endregion    

#region Properties & Data
    Exp m_exp;
    public Exp Arg
    {
        get  { return m_exp; }
    }
    
    bool m_fPre;
    bool m_fInc;
    
    public bool IsPre
    {
        get { return m_fPre; }
    }
    
    public bool IsInc
    {
        get { return m_fInc; }
    }
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(Arg != null);
    
        Arg.DebugCheck(s);            
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("PrePostIncDecStmtExp");
        o.WriteAttributeString("IsPre", IsPre.ToString());
        o.WriteAttributeString("IsInc", IsInc.ToString());
    
        Arg.Dump(o);       
                
        o.WriteEndElement();
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        string stOp = (IsInc) ? "++" : "--";
        if (IsPre)
            sb.Write(stOp);
        Arg.ToSource(sb);
        if (!IsPre)
            sb.Write(stOp);            
    }
    
#endregion

#region Resolution
    // Semantic resolution
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        // We have a problem. If our arg is a property, then we need to 
        // transform this x++ --> x = x + 1
        // But we can't always transform since we can overload the ++, -- operators.
        // So resolve our arg, see if it's a property, and then transform if it is.
        
        // Since we update the Arg, we must resolve it as a left value
        Exp.ResolveExpAsLeft(ref this.m_exp, s);
        
        // First check if we have an overload
        
        
        // Check if we're a property
        if (m_exp is PropertyExp)
        {
            Exp e = null;
            int iDelta = IsInc ? 1 : -1;
            
            e = new AssignStmtExp(
                m_exp, 
                new BinaryExp(
                    m_exp,
                    new IntExp(iDelta, this.m_filerange),
                    BinaryExp.BinaryOp.cAdd
                )
            );
            
            Exp.ResolveExpAsRight(ref e, s);
            return e;
        }
                
        CalcCLRType(s);
    
        // Ensure type match, unless we have an overload.
        s.EnsureAssignable(Arg, typeof(int));
        
        return this;
    }
    
    // Type of an assignment is the type of the Right Side    
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return Arg.CLRType;
    }
#endregion

    // Code generation as an expression
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
    
    public override void GenerateAsStatement(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAsStatement(this);
    }
}

#endregion // Statement Exp

#region ?: operator
//-----------------------------------------------------------------------------    
// Conditional Expression
//-----------------------------------------------------------------------------    
public class IfExp : Exp
{
#region Construction
    public IfExp(
        Exp expTest,
        Exp expTrue,
        Exp expFalse
    )
    {
        Debug.Assert(expTest != null);
        Debug.Assert(expTrue != null);
        Debug.Assert(expFalse != null);
        
        m_expTest = expTest;
        m_expTrue = expTrue;
        m_expFalse = expFalse;
    }
#endregion

#region Properties & Data
    Exp m_expTest;
    Exp m_expTrue;
    Exp m_expFalse;
    
    public Exp TestExp
    {
        get { return m_expTest;}
    }
    
    public Exp TrueExp
    {
        get { return m_expTrue; }
    }
    
    public Exp FalseExp
    {
        get { return m_expFalse; }
    }
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        m_expTest.DebugCheck(s);
        m_expTrue.DebugCheck(s);
        m_expFalse.DebugCheck(s);
    }

    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write('(');
        this.m_expTest.ToSource(sb);
        sb.Write('?');
        this.m_expTrue.ToSource(sb);
        sb.Write(':');
        this.m_expFalse.ToSource(sb);
        sb.Write(')');
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {    
        o.WriteStartElement("IFExp");
        
        o.WriteStartElement("Test Expression");
        m_expTest.Dump(o);
        o.WriteEndElement();
        
        o.WriteStartElement("True Expression");
        m_expTrue.Dump(o);
        o.WriteEndElement();
        
        o.WriteStartElement("False Expression");
        m_expFalse.Dump(o);
        o.WriteEndElement();
        
        o.WriteEndElement();
    }
#endregion
    // Semantic resolution
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        ResolveExpAsRight(ref this.m_expTest, s);
        ResolveExpAsRight(ref this.m_expTrue, s);
        ResolveExpAsRight(ref this.m_expFalse, s);
        
        s.EnsureAssignable(m_expTest, typeof(bool));
                
        CalcCLRType(s);
        return this;
    }

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
    
    public override void GenerateAddrOf(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAddrOf(this);
    }
    
    // typeof ?: operator.
    // Rules for determining type: Say you have (b ? t : f) where
    // t is type T, f is type F.
    // - If T ==F, type is T
    // - if implicit convert T -> F, then type is F
    // - if implicit convert F -> T, then type is T
    // - else error
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        Type t = m_expTrue.CLRType;
        Type f = m_expFalse.CLRType;
        
        // If both ops are null, then we'll be null too 
        if (t == null && f == null)
            return null;
  
        // If these are compound types, may not have reference equality,
        // but the IsAssignable tests will still pass and so we're ok.
        if (t == f)
            return t;
            
        if (TypeEntry.IsAssignable(t, f))
            return f;
            
        if (TypeEntry.IsAssignable(f, t))
            return t;
            
        // Else we have error
        //ThrowError_BadTypeIfExp(s, this); // won't return
        ThrowError(SymbolError.BadTypeIfExp(this));
             
        return null;
    }
}


#endregion

#region Operator Expressions (binary, unary)
//-----------------------------------------------------------------------------    
// Is boolean operator
//-----------------------------------------------------------------------------
public class IsExp : Exp
{
#region Construction
    public IsExp(
        Exp expTest,
        TypeSig tTarget
    )
    {
        Debug.Assert(expTest != null);
        Debug.Assert(tTarget != null);

        m_expTest = expTest;
        m_tTarget = tTarget;

        //m_filerange = FileRange.Merge(expTest.Location, tTarget.Location);
    }
#endregion

#region Properties & Data
    Exp m_expTest;
    public Exp Left
    {
        get { return m_expTest; }
    }

    TypeSig m_tTarget;
    public TypeEntry TargetType
    {
        get { return m_tTarget.BlueType; }
    }

    public override void DebugCheck(ISemanticResolver s)
    {
        m_expTest.DebugCheck(s);
        m_tTarget.DebugCheck(s);
    }
#endregion

#region Checks
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("IsExp");
        m_expTest.Dump(o);
        m_tTarget.Dump(o);
        o.WriteEndElement();
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write('(');
        m_expTest.ToSource(sb);
        sb.Write(" is ");
        m_tTarget.ToSource(sb);
        sb.Write(')');
    }
#endregion    

#region Resolution
    // Semantic resolution
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        ResolveExpAsRight(ref m_expTest, s);
        m_tTarget.ResolveType(s);
        
        CalcCLRType(s);
        
        return this;
    }
        
    // 'Is' operator is always boolean
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {        
        return typeof(bool);
    }
#endregion
    
    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }


}

//-----------------------------------------------------------------------------    
// Binary operations
//-----------------------------------------------------------------------------        
public class BinaryExp : Exp
{
    // @dogfood - 'new String[]' not needed when we have an array of literals,
    private static string [] m_ops = new String[] {
        "+", "-", "*", "/", "%",
        "&&", "||",
        "==", "!=", "<", ">", "<=", ">=",
        "&", "|", "^",
        "<<", ">>"
    };
    
    public static string OpName(BinaryOp op)
    {
        return m_ops[(int) op];
    }
                    
    public enum BinaryOp
    {
        cAdd,
        cSub,
        cMul,
        cDiv,
        cMod,
        
        cAnd,
        cOr,
                
        cEqu,
        cNeq,
        cLT,
        cGT,
        cLE,
        cGE,
        
        cBitwiseAnd,
        cBitwiseOr,
        cBitwiseXor,
        
        cShiftLeft,
        cShiftRight,
        
        cInvalid,
    }
    
    public BinaryExp(Exp left, Exp right, BinaryOp op)
    {
        Debug.Assert(left != null);
        Debug.Assert(right != null);
        
        //m_filerange = FileRange.Merge(left.Location, right.Location);
        
        m_left = left;
        m_right = right;
        m_op = op;
    }

#region Properties & Data    
    protected Exp m_left;
    protected Exp m_right;
    protected BinaryOp m_op;

    public Exp Left
    {
        get { return m_left; }
    }            
    
    public Exp Right
    {
        get { return m_right; }
    }
    
    public BinaryOp Op
    {
        get { return m_op; }
    }
#endregion

#region Resolution
    // Try and simplify
    protected override Exp TrySimplify()
    {
        TrySimplify(ref m_left);
        TrySimplify(ref m_right);

        IntExp i1 = Left as IntExp;
        IntExp i2 = Right as IntExp;
        if (i1 != null && i2 != null)
        {
            int n1 = i1.Value;
            int n2 = i2.Value;
            switch(Op)
            {
                case BinaryOp.cAdd:
                    return new IntExp(n1 + n2, Location);
            }
        }

        // Can't simplify
        return this;
    }

        
    // Nothing to resolve for literal expressions
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {   
        // Always resolve our children     
        ResolveExpAsRight(ref m_left, s);
        ResolveExpAsRight(ref m_right, s);
        
        // If we don't match a predefined operator, then check for overloads
        if (!MatchesPredefinedOp(Op, this.Left.CLRType, this.Right.CLRType))
        {
            // Packagage Left & Right into parameters for a method call
            ArgExp [] args = new ArgExp [2] {
                new ArgExp(EArgFlow.cIn, m_left),
                new ArgExp(EArgFlow.cIn, m_right)
            };
                                    
            // Check for delegate combination
            // D operator+(D, D)
            // D operator-(D, D)            
            if (AST.DelegateDecl.IsDelegate(m_left.CLRType))
            {
                if (m_left.CLRType == m_right.CLRType)
                {
                    if (Op == BinaryOp.cAdd || Op == BinaryOp.cSub)
                    {
                        System.Type d = m_left.CLRType;
                        
                        // Translates to:
                        // op+ --> (D) System.Delegate.Combine(left, right)
                        // op- --> (D) System.Delegate.Remove(left, right)
                        
                        TypeEntry tDelegate = s.LookupSystemType("MulticastDelegate");
                        string stName = (Op == BinaryOp.cAdd) ? "Combine" : "Remove";
                        
                        bool dummy;
                        MethodExpEntry sym = tDelegate.LookupMethod(s, new Identifier(stName), 
                            new Type[] { d, d}, out dummy);
                        
                        
                        Exp call2 = new CastObjExp(
                            new ResolvedTypeSig(d, s),
                            new MethodCallExp(
                                null,sym, args, s)
                        );
                        
                        Exp.ResolveExpAsRight(ref call2, s);
                        return call2;                                
                    
                    }
                }
            
            } // end delgate op+ check
                                            
            // Check for System.String.Concat().
            // @todo - this should be able to compress an entire subtree, not just 2 args.
            // (ie, a+b+c -> String.Concat(a,b,c);
            // So we can't merge this into the SearchForOverload.
            // But for now we'll be lazy...
            if ((Op == BinaryOp.cAdd) && (Left.CLRType == typeof(string) || Right.CLRType == typeof(string)))
            {                
                Exp call2 = new MethodCallExp(                    
                        new DotObjExp(
                            new SimpleObjExp(
                                new Identifier("System", this.m_filerange)
                            ),
                            new Identifier("String", this.m_filerange)),
                        new Identifier("Concat", m_filerange),
                        args);
                call2.SetLocation(this.Location);
                Exp.ResolveExpAsRight(ref call2, s);
                        
                return call2;
            }
        
            MethodExpEntry  m = SearchForOverloadedOp(s);
            if (m == null && (Op == BinaryOp.cEqu || Op == BinaryOp.cNeq))
            {
                // If it's '==' or '!=', then it's ok if we didn't find 
                // an overload.
            } else 
            {
                // Couldn't find an overload, throw error
                if (m == null)
                {
                    //ThrowError_NoAcceptableOperator(s, this.Location, m_left.CLRType, m_right.CLRType, Op);
                    ThrowError(SymbolError.NoAcceptableOperator(this.Location, m_left.CLRType, m_right.CLRType, Op));
                }
                
                // Replace this node w/ the method call            
                MethodCallExp call = new MethodCallExp(null, m, args, s);
                call.SetLocation(this.Location);
                
                return call;
            }
        }
        
        
        CalcCLRType(s);
        
        return this;
    }
    
    // Search for an overloaded operator. Return the symbol for the method if found.
    // Return null if not found.
    MethodExpEntry SearchForOverloadedOp(ISemanticResolver s)
    {
        Type [] alParams = new Type[2];
        alParams[0] = m_left.CLRType;
        alParams[1] = m_right.CLRType;
        
        
        
        string stName = MethodDecl.GetOpOverloadedName(this.Op);
        
        MethodExpEntry sym;
        
        if (m_left.CLRType != null)
        {
            TypeEntry t1 = s.ResolveCLRTypeToBlueType(m_left.CLRType);
            sym = t1.LookupOverloadedOperator(s, stName, alParams);
            if (sym != null)
                return sym;
        }
        
        if (m_right.CLRType != null)
        {
            TypeEntry t2 = s.ResolveCLRTypeToBlueType(m_right.CLRType);
            sym = t2.LookupOverloadedOperator(s, stName, alParams);
            if (sym != null)
                return sym;
        }
                
        return null; 
    }
    
    
    // Helpers for detecting if we match predefined ops
    // Return true if we're any integral type, else false.
    static public bool IsNumber(System.Type t)
    {
        return t == typeof(int) || t == typeof(char);
    }
    static public bool IsBool(System.Type t)
    {
        return t == typeof(bool);
    }
    static public bool IsInteger(System.Type t)
    {
        return t == typeof(int) || t == typeof(char);
    }
    static public bool IsEnum(System.Type t)
    {
        return (t != null) && t.IsEnum;
    }
        
    // Return true if our param types let us match a predefined operator.
    // Else return false (which implies that we must match against an 
    // overloaded operator)
    // @todo - combine w/ CalcCLRTypeHelper()
    static public bool MatchesPredefinedOp(BinaryOp op, Type clrLeft, Type clrRight)
    {
        // For arithmetic ops, only predefined if both args are numbers
        if (op == BinaryOp.cAdd || op == BinaryOp.cMul || op == BinaryOp.cSub || 
            op == BinaryOp.cDiv || op == BinaryOp.cMod ||
            op == BinaryOp.cShiftLeft || op == BinaryOp.cShiftRight)
        {
            return IsNumber(clrLeft) && IsNumber(clrRight);                
        }
        
        if (op == BinaryOp.cEqu || op == BinaryOp.cNeq)
        {
            if (IsNumber(clrLeft) && IsNumber(clrRight)) 
                return true;
            if (IsBool(clrLeft) && IsBool(clrRight))
                return true;
            if (IsEnum(clrLeft) && IsEnum(clrRight))
                return true;                
            return false;                
        }
        
        // Bitwise operators work on either bool or integers
        if (op == BinaryOp.cBitwiseAnd || op == BinaryOp.cBitwiseOr || op == BinaryOp.cBitwiseXor)
        {
            if (IsInteger(clrLeft) && IsInteger(clrRight)) 
                return true;
            if (IsBool(clrLeft) && IsBool(clrRight))
                return true;
            if (IsEnum(clrLeft) && IsEnum(clrRight)) // @todo -only if flags attribute specified
                return true;                
            return false;  
        }
                
        // Relational ops only work on ints
        if (op == BinaryOp.cLT || op == BinaryOp.cGT ||
            op == BinaryOp.cLE || op == BinaryOp.cGE)
        {
            if (IsNumber(clrLeft) && IsNumber(clrRight))
                return true;
            if (IsEnum(clrLeft) && IsEnum(clrRight))
                return true;                
        }
        
        // These ops can't be overloaded, so they had better
        // match a default type
        if (op == BinaryOp.cAnd || op == BinaryOp.cOr)
        {
            return true;        
        }
        
        return false;
    }
    
    // Determine the CLR type of this expression
    // If we're a predefined op, then this is given by the C# spec.
    // If we're an overloaded op, then this is the return type of the method
    // that we resolve to.
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        
        
        switch(this.Op)
        {   
            // Special case for string.concat
            case BinaryOp.cAdd: 
                if (Left.CLRType == typeof(string) || Right.CLRType == typeof(string))
                    return typeof(string);
                return typeof(int);
            
            case BinaryOp.cSub: return typeof(int);
            case BinaryOp.cMul: return typeof(int);
            case BinaryOp.cDiv: return typeof(int);
            case BinaryOp.cMod: return typeof(int);
        
            case BinaryOp.cAnd: return typeof(bool);
            case BinaryOp.cOr: return typeof(bool);
                    
            case BinaryOp.cEqu: return typeof(bool);
            case BinaryOp.cNeq: return typeof(bool);
            case BinaryOp.cLT: return typeof(bool);
            case BinaryOp.cGT: return typeof(bool);
            case BinaryOp.cLE: return typeof(bool);
            case BinaryOp.cGE: return typeof(bool);
            
            case BinaryOp.cBitwiseAnd:  return Left.CLRType;
            case BinaryOp.cBitwiseOr:   return Left.CLRType;
            case BinaryOp.cBitwiseXor:  return Left.CLRType;
            
            case BinaryOp.cShiftLeft:   return Left.CLRType;
            case BinaryOp.cShiftRight:  return Left.CLRType;
        }

        Debug.Assert(false, "Unknown binary op:"+this.Op.ToString());

        return null;
    }
#endregion    
    
    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }    
    
#region Checks    
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_left != null);
        Debug.Assert(m_right != null);
        
        CalcCLRType(s);
                
        m_left.DebugCheck(s);
        m_right.DebugCheck(s);
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("BinaryExp");
        o.WriteAttributeString("op", m_op.ToString());
        m_left.Dump(o);
        m_right.Dump(o);
        o.WriteEndElement();
    }   
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write('(');
        m_left.ToSource(sb);
        sb.Write(OpName(this.m_op));
        m_right.ToSource(sb);
        sb.Write(')');        
    }
#endregion    
}

//-----------------------------------------------------------------------------    
// Unary operations
//-----------------------------------------------------------------------------        
public class UnaryExp : Exp
{
    public enum UnaryOp
    {
        cNegate,
        cNot,
        cPreInc,
        cPostInc,
        cPreDec,
        cPostDec
    }

    public UnaryExp(Exp left, UnaryOp op)
    {
        Debug.Assert(left != null);
            
        m_filerange = left.Location;
    
        m_left = left;        
        m_op = op;
    }

    protected Exp m_left;    
    protected UnaryOp m_op;

    public Exp Left
    {
        get { return m_left; }
    }            
    
    public UnaryOp Op
    {
        get { return m_op; }
    }
    
    // Nothing to resolve for literal expressions
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        ResolveExpAsRight(ref m_left, s);

        CalcCLRType(s);
        
        return this;
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("UnaryExp");
        o.WriteAttributeString("op", m_op.ToString());
        m_left.Dump(o);
        o.WriteEndElement();
    }         

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);        
    }

    // Determine the CLR type of this expression
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        switch(m_op)
        {
            case UnaryOp.cNegate:
                return typeof(int);
            case UnaryOp.cNot:
                return typeof(bool);

            case UnaryOp.cPreInc:
                return typeof(int);

            case UnaryOp.cPostInc:
                return typeof(int);

            case UnaryOp.cPreDec:
                return typeof(int);

            case UnaryOp.cPostDec:
                return typeof(int);
        
            default:
                Debug.Assert(false, "Illegal unary operator:" + m_op.ToString());
                return null;
        }
        
    }
  
#region Checks    
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        CalcCLRType(s);  
        m_left.DebugCheck(s);
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        char ch = '?';
        switch(m_op)
        {
            case UnaryOp.cNegate: ch = '-'; break;
            case UnaryOp.cNot: ch = '!'; break;
        }
        sb.Write(ch);
        this.Left.ToSource(sb);
    }
#endregion    
}

#endregion

#region Literal Expressions
//-----------------------------------------------------------------------------    
// Literals - constant expressions
//-----------------------------------------------------------------------------        

/// <summary>
/// Abstract base class for all literals (ints, strings, chars, null, etc)
/// </summary>
public abstract class LiteralExp : Exp
{    
    public LiteralExp()
    {
#if DEBUG
        m_fResolved = false;
#endif
    }

    // Nothing to resolve for literal expressions
    protected override Exp ResolveExpAsRight(ISemanticResolver s)
    {
        CalcCLRType(s);
#if DEBUG
        m_fResolved = true;
#endif
        return this;
    }
    
    
    public override void DebugCheck(ISemanticResolver s)
    {   
        Debug.Assert(Exp.CanBeNullType(this) || this.CLRType != null);
#if DEBUG
        Debug.Assert(m_fResolved);
#endif      
    }

// We're supposed to resolve all expressions. But if we forget to resolve
// a literal, things will still conveniently work out, and so we won't notice it.
// But if we don't resolve a literal, then we could have missed any expression.
// So add a debug-only check here to make sure literals are resolved.
#if DEBUG
    bool m_fResolved;
#endif

    // Generating the address of a literal is ok. We just have to
    // create a temporary local to provide backing storage
    public override void GenerateAddrOf(CodeGen.EmitCodeGen gen)
    {
        gen.GenerateAddrOf(this);
    }        
}

//-----------------------------------------------------------------------------
// Literal expression
//-----------------------------------------------------------------------------
/// <summary>
/// Node representing the 'null' keyword literal.
/// </summary>
public class NullExp : LiteralExp
{
    public NullExp(FileRange location) 
    {
        m_filerange = location;
    }

    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return null;
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("NullExp");        
        o.WriteEndElement();
    }        
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("null");
    }

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }  
    
     
}

//-----------------------------------------------------------------------------        
// literal integer    
//-----------------------------------------------------------------------------        

/// <summary>
/// Node representing an System.Int32
/// </summary>
public class IntExp : LiteralExp
{
    public IntExp(int i, FileRange location) 
    {
        m_filerange = location;
        m_value = i;        
    }
    
    // Determine the CLR type of the expression
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {        
        return typeof(int);
    }
    
    int m_value;
    
    public int Value
    {
        get { return m_value; }
    }    
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("IntExp");
        o.WriteAttributeString("value", m_value.ToString());                        
        o.WriteEndElement();
    }   
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("{0}i", m_value);
    }     

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
}

//-----------------------------------------------------------------------------
// literal boolean
//-----------------------------------------------------------------------------
public class StringExp : LiteralExp
{
    public StringExp(string s, FileRange location) 
    {
        m_filerange = location;
        m_stValue = s;        
    }

    // Determine the CLR type of the expression
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {        
        return typeof(string);
    }
    
    string m_stValue;

    public string
    Value
    {
        get 
        { 
            return m_stValue; 
        }
    }    

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("StringExp");
        o.WriteAttributeString("value", Value);
        o.WriteEndElement();
    }        
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("@\"{0}\"", m_stValue);
    }

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
}

//-----------------------------------------------------------------------------
// literal boolean
//-----------------------------------------------------------------------------
public class BoolExp : LiteralExp
{
    public BoolExp(bool f, FileRange location) 
    {
        m_filerange = location;
        m_fValue = f;        
    }

    // Determine the CLR type of the expression
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return typeof(bool);
    }

    bool m_fValue;

    public bool
    Value
    {
        get 
        { 
            return m_fValue; 
        }
    }    

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("BoolExp");
        o.WriteAttributeString("value", m_fValue ? "true" : "false");                        
        o.WriteEndElement();
    }        

    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("{0}b", m_fValue ? "true" : "false");
    }

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
 
}

#if false
//-----------------------------------------------------------------------------
// literal double precision floating point number
//-----------------------------------------------------------------------------
public class DoubleExp : LiteralExp
{
    public DoubleExp(double d, FileRange location) 
    {
        m_filerange = location;
        m_dValue = d;        
    }

    // Determine the CLR type of the expression
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {        
        return typeof(double);        
    }

    double m_dValue;

    public double
    Value
    {
        get 
        { 
            return m_dValue; 
        }
    }    

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("DoubleExp");
        o.WriteAttributeString("value", m_dValue.ToString());
        o.WriteEndElement();
    }        

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
  
}
#endif

//-----------------------------------------------------------------------------
// literal Char
//-----------------------------------------------------------------------------
public class CharExp : LiteralExp
{
    public CharExp(char ch, FileRange location) 
    {
        m_filerange = location;
        m_chValue = ch;        
    }

    // Determine the CLR type of the expression
    protected override Type CalcCLRTypeHelper(ISemanticResolver s)
    {
        return typeof(char);
    }

    char m_chValue;

    public char Value
    {
        get 
        { 
            return m_chValue; 
        }
    }    

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("CharExp");
        o.WriteAttributeString("value", m_chValue.ToString());
        o.WriteEndElement();
    }        
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("'{0},{1}'", m_chValue, (int) m_chValue);
    }

    // Code generation
    public override void GenerateAsRight(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }

}
#endregion

} // namespace AST
