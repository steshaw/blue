//-----------------------------------------------------------------------------
// File: StatementAST.cs
//
// Description: All Statement nodes in the AST
// See AST.cs for details
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Xml;

using CodeGen = Blue.CodeGen;
using Utilities = Blue.Utilities;

using Blue.Public;
using SymbolEngine;

namespace AST
{
//-----------------------------------------------------------------------------
// Statement base class
//-----------------------------------------------------------------------------

/// <summary>
/// Abstract base class for all statements.
/// </summary>
public abstract class Statement : Node
{

    // Semantic resolution
    public abstract void ResolveStatement(ISemanticResolver s);
    public virtual void ResolveStatement2(ISemanticResolver s) { }

    // Code generation
    public abstract void Generate(CodeGen.EmitCodeGen gen);
}

#region Empty Statement
//-----------------------------------------------------------------------------        
// Empty statement
//-----------------------------------------------------------------------------        
public class EmptyStatement : Statement
{
    public EmptyStatement() 
    {
    }
    
    public override void ResolveStatement(ISemanticResolver s)
    {
    }
    
    public override void Generate(CodeGen.EmitCodeGen gen)
    {
        // @todo - should we emit a nop?
    }    
    
#region Checks    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.WriteLine("; // empty");
    }
    public override void Dump(XmlWriter o)
    {        
    }
    
    public override void DebugCheck(ISemanticResolver s)
    {
    }
#endregion    

}
#endregion

#region BlockStatement
//-----------------------------------------------------------------------------        
// Block statement
// Statements inside '{' .... '}'
// Can have their own statements & optionally their own little scope
//-----------------------------------------------------------------------------        
public class BlockStatement : Statement
{
#region Construction
    public BlockStatement(
        LocalVarDecl[] arLocals, // can be null if we have no locals
        Statement[] arStatements // never null
    )
    {
        // If we have any locals in this block statement, then we
        // also need to have a scope to hold them.
        if (arLocals == null)
        {
            m_arLocals = new LocalVarDecl[0];
        } else {
            m_arLocals = arLocals;
            // @todo - decorate name w/ filerange?
            //m_scopeLocals = new Scope("block_scope", null);
        }
        
        // May have an empty statement block, so arStatements.Length could be 0
        // but at least expect an array.
        Debug.Assert(arStatements != null);
        m_arStatements = arStatements;
        
        // @todo - fix this
        if (arStatements.Length == 0)
            this.m_filerange = new FileRange();
        else 
            this.m_filerange = arStatements[0].Location;
    }
#endregion

    // Inject a statement at the beginning
    // Must resolve after doing this.
    public void InjectStatementAtHead(Statement sNew)
    {   
        Statement[] ar2 = new Statement[this.Statements.Length + 1];
        ar2[0] = sNew;
        for(int i = 0; i < Statements.Length; i++)        
            ar2[i + 1] = Statements[i];
        
        this.m_arStatements = ar2;
    }
    
    public void InjectLocalVar(LocalVarDecl l)
    {
        //m_scopeLocals = new Scope("block_scope", null);
        LocalVarDecl[] ar2 = new LocalVarDecl[Locals.Length + 1];
        ar2[0] = l;
        for(int i = 0; i < Locals.Length; i++)        
            ar2[i + 1] = Locals[i];
        m_arLocals = ar2;
    }

#region Checks
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_arLocals != null);
        Debug.Assert(m_arStatements != null);
        
        foreach(LocalVarDecl v in m_arLocals)
        {
            v.DebugCheck(s);
        }
        
        foreach(Statement st in m_arStatements)
        {
            st.DebugCheck(s);        
        }
    }

    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.WriteLine('{');
        sb.Indent++;
                
        foreach(LocalVarDecl v in Locals)
        {
            v.ToSource(sb);
            sb.WriteLine();
        }
        foreach(Statement s in Statements)
        {
            s.ToSource(sb); // these should already add '\n'            
        }
        sb.Indent--;
        sb.WriteLine('}');
        
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("BlockStatement");
        
        
        foreach(LocalVarDecl v in Locals)
        {
            v.Dump(o);
        }
                
        foreach(Statement s in Statements)
        {
            s.Dump(o);
        }
                    
        o.WriteEndElement();
    }
#endregion
    
#region Properties & Data
    Scope m_scopeLocals;

    protected LocalVarDecl [] m_arLocals;
    protected Statement[] m_arStatements;
    
    public LocalVarDecl [] Locals
    {
        get { return m_arLocals; }
    }
    
    public Statement[] Statements
    {
        get { return m_arStatements; }
    }
#endregion


#region Resolution   
    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {        
        Scope prev = null;
        if (this.m_arLocals.Length != 0)
        {
            //s.PushScope(m_scopeLocals);
            m_scopeLocals = new Scope("block_scope", null, s.GetCurrentContext());
            prev = s.SetCurrentContext(m_scopeLocals);
        }
            
        foreach(LocalVarDecl v in Locals)
        {
            v.ResolveVarDecl(s);
        }
                
        foreach(Statement stmt in Statements)
        {
            stmt.ResolveStatement(s);
        }

        if (m_scopeLocals != null)
            //s.PopScope(m_scopeLocals);
            s.RestoreContext(prev);
            
    }

    public override void ResolveStatement2(ISemanticResolver s)
    {
        Scope prev = null;
        if (m_scopeLocals != null)
            //s.PushScope(m_scopeLocals);
            prev = s.SetCurrentContext(m_scopeLocals);

        foreach(Statement stmt in Statements)
        {
            stmt.ResolveStatement2(s);
        }

        if (m_scopeLocals != null)
            //s.PopScope(m_scopeLocals);
            s.RestoreContext(prev);
    }
#endregion

    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {
        foreach(Statement stmt in Statements)
        {
            stmt.Generate(gen);
        }
    }
}
#endregion

#region EH statements    
//-----------------------------------------------------------------------------        
// Catch-handler, used in try block
//-----------------------------------------------------------------------------        
public class CatchHandler
{    
    public CatchHandler(
        TypeSig type,           // type we're catching (must derived from System.Exception)
        Identifier idName,       // optional (can be null) name for local var to store exception
        BlockStatement stmtBody // handler body (non-null)
    ) 
    {         
        Debug.Assert(stmtBody != null);
        
        // General catch blocks just becomes a System.Exception
        if (type == null)
        {        
            m_type = new SimpleTypeSig(new DotObjExp(
                new SimpleObjExp(new Identifier("System", null)),
                new Identifier("Exception", null)
                ));
        
            
        } else {     
            m_type = type;
        }
        m_idVarName = idName;
        m_body = stmtBody;
    }
    
#region Properties & Data    
    TypeSig m_type;
    public TypeEntry CatchType
    {
        get { return m_type.BlueType; }
    }
    
    Identifier m_idVarName;
    public Identifier IdVarName
    {
        get { return m_idVarName; }
    }
    
    LocalVarDecl m_var;
    public LocalVarDecl CatchVarDecl
    {
        get {return m_var; }
    }
    
        
    BlockStatement m_body;
    public BlockStatement Body
    {
        get { return m_body; }
    }       
#endregion

#region Checks    
    public void Dump(XmlWriter o)
    {
        o.WriteStartElement("Catch");
        if (IdVarName != null)
            o.WriteAttributeString("varname", IdVarName.Text);
            
        if (m_type != null)
            this.m_type.Dump(o);
        Body.Dump(o);        
        o.WriteEndElement();
    }
    
    public void DebugCheck(ISemanticResolver s)
    {
        CatchType.DebugCheck(s);
        Body.DebugCheck(s);
    }
#endregion
   
#region Resolution    
    public void ResolveHandler2(ISemanticResolver s)
    {
        m_body.ResolveStatement2(s);
    }
    
    public void ResolveHandler(ISemanticResolver s)
    {           
        // Catch blocks can declare an identifier        
        if (IdVarName != null)
        {
            m_var = new LocalVarDecl(IdVarName, m_type);
            Body.InjectLocalVar(m_var);
        }
        
        this.m_type.ResolveType(s);
        Body.ResolveStatement(s);
        
        // Catch type must be of type System.Exception
        if (m_var != null)
        {
            s.EnsureAssignable(m_var.Symbol.m_type.CLRType, typeof(System.Exception), IdVarName.Location);
        }
        
        // Catch type must be of type System.Exception
        //TypeEntry tSystem_Exception =s.ResolveCLRTypeToBlueType(typeof(System.Exception));
        //s.EnsureDerivedType(tSystem_Exception, m_type.TypeRec, new FileRange());
        
    }
#endregion    
}

//-----------------------------------------------------------------------------        
// Try-Catch-Finally
// -> 'try' block 'finally' block
// -> 'try' block <'catch' '(' TypeSig id ')' block>+ ['finally' block]?
//-----------------------------------------------------------------------------      
public class TryStatement : Statement
{
// Must have a finally or at least one Catch (or both)
    public TryStatement(
        BlockStatement stmtTry,     // never null
        CatchHandler [] arCatch,    // can be null
        BlockStatement stmtFinally  // can be null        
    )
    {
        Debug.Assert(stmtTry != null); // always have a try block        
        Debug.Assert((arCatch != null && arCatch.Length > 0) || stmtFinally != null);
                
        m_stmtTry = stmtTry;
        m_arCatchHandlers = (arCatch == null) ? new CatchHandler[0] : arCatch;        
        m_stmtFinally = stmtFinally;
        
        // @todo - this is wrong
        m_filerange = stmtTry.Location;
    }

#region Properties & Data    
    protected BlockStatement m_stmtTry;
    public BlockStatement TryStmt
    {
        get { return m_stmtTry; }
    }
    
    protected CatchHandler [] m_arCatchHandlers;
    public CatchHandler [] CatchHandlers
    {
        get { return m_arCatchHandlers; }
    }
    
    
    protected BlockStatement m_stmtFinally;
    public BlockStatement FinallyStmt
    {
        get { return m_stmtFinally; }
    }
#endregion

#region Checks    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("TryStatement");
        
        o.WriteStartElement("try_block");
        m_stmtTry.Dump(o);
        o.WriteEndElement();
        
        
        o.WriteStartElement("catch_handlers");
        foreach(CatchHandler c in m_arCatchHandlers)
        {
            c.Dump(o);
        }
        o.WriteEndElement();
                
        if (m_stmtFinally != null)
        {
            o.WriteStartElement("finally_block");
            m_stmtFinally.Dump(o);
            o.WriteEndElement();
        }
        
        o.WriteEndElement();        
    }
        
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        m_stmtTry.DebugCheck(s);
        
        if (m_stmtFinally != null)
            m_stmtFinally.DebugCheck(s);
            
        foreach(CatchHandler c in CatchHandlers)
        {
            c.DebugCheck(s);
        }
    }
#endregion

#region Resolution
    
    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        m_stmtTry.ResolveStatement(s);
        
        for(int i = 0; i < CatchHandlers.Length; i++)
        {        
            CatchHandler c = CatchHandlers[i];
            c.ResolveHandler(s);
            
        // For each catch handler, make sure that it's type doesn't shadow
        // a previous handler.
            for(int j = 0; j < i; j++)
            {        
                CatchHandler cPrev = CatchHandlers[j];
            
                if (s.IsDerivedType(cPrev.CatchType, c.CatchType))
                {
                    //ThrowError_ShadowCatchHandlers(s, this.Location, c.CatchType.CLRType, cPrev.CatchType.CLRType);
                    ThrowError(SymbolError.ShadowCatchHandlers(this.Location, c.CatchType.CLRType, cPrev.CatchType.CLRType));
                }            
            }            
        } // end for
        
        if (m_stmtFinally != null)
            m_stmtFinally.ResolveStatement(s);
    }
    
    public override void ResolveStatement2(ISemanticResolver s)
    {
        m_stmtTry.ResolveStatement2(s);
        
        foreach(CatchHandler c in CatchHandlers)
        {                   
            c.ResolveHandler2(s);
        }
            
        if (m_stmtFinally != null)
            m_stmtFinally.ResolveStatement2(s);
        
    }
#endregion
    
    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
}

//-----------------------------------------------------------------------------        
// Throw
// -> 'throw' 'obj expression'
//-----------------------------------------------------------------------------        
public class ThrowStatement : Statement
{
    public ThrowStatement(
        Exp oeException // can be null for a rethrow
    )
    {        
        m_oeException = oeException;
    }
    
#region Properties & Data    
    Exp m_oeException;
    public Exp ExceptionExp
    {
        get {return m_oeException; }
    }
#endregion    
    
#region Checks    
    public override void DebugCheck(ISemanticResolver s)
    {
        if (m_oeException != null)
            m_oeException.DebugCheck(s);
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ThrowStatement");
        if (m_oeException != null)
            ExceptionExp.Dump(o);
        o.WriteEndElement();        
    }
#endregion    
    
    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        if (m_oeException != null)
        {
            Exp.ResolveExpAsRight(ref m_oeException, s);
            //Debug.Assert(m_oeException.SymbolMode == ObjExp.Mode.cExpEntry);
            
            // Must derive from System.Exception            
            s.EnsureAssignable(m_oeException, typeof(System.Exception));
        }
    }
    
    
    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
}

#endregion

#region Control Flow statements    
#region Switch Statement
//-----------------------------------------------------------------------------
// Switch statement
//-----------------------------------------------------------------------------
public class SwitchStatement : Statement
{
    public SwitchStatement(
        Exp expTest,
        SwitchSection [] sections
    )
    {
        m_expTest = expTest;
        m_sections = sections;
        
        m_proxy = null;
    }


#region Properties & Data
    Exp m_expTest;
    public Exp ExpTest
    {
        get { return m_expTest; }
    }
    
    SwitchSection [] m_sections;
    public SwitchSection[] Sections
    {
        get { return m_sections; }
    }
    
    Statement m_proxy;
    public Statement ResolvedStatement
    {
        get { return m_proxy; }
    }
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {   
        if (m_proxy != null)
            m_proxy.DebugCheck(s);
        else
        {            
            m_expTest.DebugCheck(s);                
            foreach(SwitchSection c in Sections)
                c.DebugCheck(s);
        }
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("SwitchStatement");
        if (m_proxy != null)
            m_proxy.Dump(o);                            
        o.WriteEndElement();
    }
#endregion

#region Resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        if (m_proxy != null)
            return;
            
        // Resolve by converting into a giant if-statement
#if false
Transform:
    switch(e)
    {
        case A1:
        case A2:
            S_A;
        case B1:
            SB;
        default:
            S_Default;        
    }
    
Into:
    do {
        T x = [e];
        if ((x == A1) || (x == A2))
            S_A;
        if ((x == B1))
            S_B;
        S_Default;    
    } while(false);   
#endif  
        Exp.ResolveExpAsRight(ref m_expTest, s);

        TypeSig T = new ResolvedTypeSig(m_expTest.CLRType, s);
        LocalVarDecl declare_x = new LocalVarDecl(
            new Identifier(".temp", this.Location),
            T);
            
        declare_x.ResolveVarDecl(s);            
        LocalExp x = new LocalExp(declare_x.LocalSymbol);
                
        Statement [] sList = new Statement[m_sections.Length + 1];
        // T x = [e];
        sList[0] = new ExpStatement(
            new AssignStmtExp(x, this.m_expTest)
        );
                
        // @todo - this assumes that 'default' is last.                
        int iSection = 0;
        foreach(SwitchSection c in this.m_sections)
        {
            Statement t;
            if (c.IsDefaultCase)
            {
                t = c.StmtBody;
            } else {
                // Create ((x == A1) || (x == A2))
                Exp [] labels = c.CaseExps;            
                Exp e2 = null;
                for(int i = 0; i < labels.Length; i++)
                {
                    Exp e = new BinaryExp(
                        x,
                        labels[i],
                        BinaryExp.BinaryOp.cEqu
                    );
                    
                    if (e2 == null)
                        e2 = e;
                    else                    
                        e2 = new BinaryExp(e, e2, BinaryExp.BinaryOp.cOr);
                }
                // Create if () S_A else
            
                t = new IfStatement(e2, c.StmtBody, null);
            }
                        
            sList[iSection + 1] = t;
            iSection++;
        } // end for each section
        
        
        m_proxy = new BlockStatement(
            new LocalVarDecl[] { declare_x},
            sList
        );
        
        // This is pure evil. We drop the whole thing in an do..while(false)
        // loop so that we can catch the 'break' statements in the switchsections. 
        m_proxy = new DoStatement(new BoolExp(false, Location), m_proxy);
        
        m_proxy.ResolveStatement(s);
    }        
    
    public override void ResolveStatement2(ISemanticResolver s)
    {
        m_proxy.ResolveStatement2(s);
    }
#endregion
    public override void Generate(CodeGen.EmitCodeGen gen)
    {
        m_proxy.Generate(gen);
        //gen.Generate(this);
    }

}

// Each switch section has a set of expressions, and then a Statement
// to execute if the expressions are true.
public class SwitchSection
{
#region Construction
    // Default, has no expressions
    public SwitchSection(        
        Statement stmt
    )
    {
        m_CaseExp = null;
        m_stmt = stmt;
    }
    
    // Arbitrary number of cases
    public SwitchSection(
        Exp [] expTests,
        Statement stmt
    )
    {
        Debug.Assert(expTests != null);
        Debug.Assert(expTests.Length != 0);
        m_CaseExp = expTests;
        m_stmt = stmt;
    }
#endregion

#region Checks
    public void DebugCheck(ISemanticResolver s)
    {
        if (!this.IsDefaultCase)
        {
            foreach(Exp e in m_CaseExp)
                e.DebugCheck(s);
        }
            
        StmtBody.DebugCheck(s);    
    }
#endregion

#region Properties & Data
    Exp [] m_CaseExp;
    public Exp[] CaseExps
    {
        get { return m_CaseExp; }
    }
    
    Statement m_stmt;
    public Statement StmtBody
    {
        get { return m_stmt; }
    }
    
    public bool IsDefaultCase
    {
        get { return m_CaseExp == null; }
    }
#endregion


}


#endregion Switch Statement

//-----------------------------------------------------------------------------        
// If-then-else statement
//-----------------------------------------------------------------------------        
public class IfStatement : Statement
{
    public IfStatement(
        Exp exp, // must be non-null
        Statement stmtThen,  // must be non-null
        Statement stmtElse // null if no else-clause
    )
    {
        Debug.Assert(exp != null);
                
        m_exp = exp;
        m_stmtThen = stmtThen;
        m_stmtElse = stmtElse;
    }
    
    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_exp != null);
        Debug.Assert(m_stmtThen != null);
        
        m_exp.DebugCheck(s);
        System.Type clrType = m_exp.CalcCLRType(s);
        Debug.Assert((clrType == typeof(bool)) || 
            (clrType.IsByRef && (clrType.GetElementType() == typeof(bool))));
        
        m_stmtThen.DebugCheck(s);
        
        if (m_stmtElse != null)
            m_stmtElse.DebugCheck(s);
    }
    
    
    protected Exp m_exp;
    public Exp TestExp
    {
        get { return m_exp; }         
    }

    protected Statement m_stmtThen;
    public Statement ThenStmt
    {
        get { return m_stmtThen; }
    }

    protected Statement m_stmtElse;
    public Statement ElseStmt
    {
        get { return m_stmtElse; }
    }

#region Checks    
    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("If");
        
        m_exp.Dump(o);
        
        o.WriteStartElement("then_clause");
        m_stmtThen.Dump(o);
        o.WriteEndElement();        
        
        if (m_stmtElse != null)
        {
            o.WriteStartElement("else_clause");
            m_stmtElse.Dump(o);
            o.WriteEndElement();
        }        
        
        o.WriteEndElement(); // if
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)        
    {
        sb.Write("if (");
        this.m_exp.ToSource(sb);
        sb.WriteLine(")");
        
        sb.Indent++;
        m_stmtThen.ToSource(sb);
        sb.Indent--;
        
        if (m_stmtElse != null)
        {
            sb.WriteLine("else");
            sb.Indent++;
            m_stmtElse.ToSource(sb);
            sb.Indent--;
        }
        
    }
#endregion    
    
    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        Exp.ResolveExpAsRight(ref m_exp, s);
        m_stmtThen.ResolveStatement(s);
        if (m_stmtElse != null)
        {   
            m_stmtElse.ResolveStatement(s);
        }
    }

    public override void ResolveStatement2(ISemanticResolver s)
    {
        m_stmtThen.ResolveStatement2(s);
        if (m_stmtElse != null)
        {   
            m_stmtElse.ResolveStatement2(s);
        }
    }
    
    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }
}

#region Loop Statements    
//-----------------------------------------------------------------------------
// Loop Statement 
//-----------------------------------------------------------------------------
    public abstract class LoopStatement : Statement
    {
        public LoopStatement(
            Exp expTest,
            Statement stmtBody
            )
        {
            Debug.Assert(expTest != null);
            Debug.Assert(stmtBody != null);

            m_expTest = expTest;
            m_stmtBody = stmtBody;
        }

        // Test to make sure that break & continue are in a loop
    static int m_loopcount = 0;
    public static bool IsInsideLoop()  
    {
        return m_loopcount != 0;
    }

    protected Exp m_expTest;
    public Exp TestExp
    {
        get { return m_expTest; }
    }

    protected Statement m_stmtBody;        
    public Statement BodyStmt
    {
        get { return m_stmtBody; }
    }


    // Debug checking
    public override void DebugCheck(ISemanticResolver s)
    {
        TestExp.DebugCheck(s);
        BodyStmt.DebugCheck(s);
    }

    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {        
        Exp.ResolveExpAsRight(ref this.m_expTest, s);

        m_loopcount++;
        BodyStmt.ResolveStatement(s);
        m_loopcount--;

        //s.EnsureDerivedType(s.ResolveCLRTypeToBlueType(typeof(bool)), TestExp);
        s.EnsureAssignable(TestExp, typeof(bool));
    }

    public override void ResolveStatement2(ISemanticResolver s)
    {
        m_loopcount++;
        BodyStmt.ResolveStatement2(s);
        m_loopcount--;
    }
} // end LoopStatement

//-----------------------------------------------------------------------------
// Do Statement 
//-----------------------------------------------------------------------------
public class DoStatement : LoopStatement
{
    public DoStatement(
        Exp expTest,
        Statement stmtBody
    ) 
        : base(expTest, stmtBody)
    {        
    }
        
    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("do");
        TestExp.Dump(o);
        BodyStmt.Dump(o);
        o.WriteEndElement();
    }
    
    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {   
        gen.Generate(this);
    }
}

//-----------------------------------------------------------------------------
// For statement. Has 2 flavors - variable initialed & no var initialized
//-----------------------------------------------------------------------------
public class ForStatement : LoopStatement
{
#region Construction
    public ForStatement(
        StatementExp eInit,
        Exp expTest,
        StatementExp eNext,
        Statement stmtBody)
    : base(expTest, stmtBody)        
    {
        m_sexpInit = eInit;        
        m_sexpNext = eNext;
    }
#endregion

#region Properties & Data
    StatementExp m_sexpInit;
    public StatementExp InitExp
    {   
        get { return m_sexpInit; }
    }
    
    StatementExp m_sexpNext;
    public StatementExp NextExp
    {
        get { return m_sexpNext; }
    }    
#endregion    

#region Checks
    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("for");
        InitExp.Dump(o);
        TestExp.Dump(o);
        NextExp.Dump(o);
        BodyStmt.Dump(o);
        o.WriteEndElement();
    }
    
    // Debug checking
    public override void DebugCheck(ISemanticResolver s)
    {
        InitExp.DebugCheck(s);
        NextExp.DebugCheck(s);
        
        // Call to Loop.DebugCheck();
        base.DebugCheck(s);
    }
#endregion
    
#region Resolution    
    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {        
        Exp e = m_sexpInit;
        Exp.ResolveExpAsRight(ref e, s);
        Debug.Assert(e == m_sexpInit); // @todo - fix this
        
        e = m_sexpNext;
        Exp.ResolveExpAsRight(ref e, s);
        Debug.Assert(e == m_sexpNext); // @todo -fix this
        
        base.ResolveStatement(s);
    }    
#endregion     
    
    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {   
        gen.Generate(this);
    }
}

//-----------------------------------------------------------------------------
// Foreach statement
// foreach (Type t in Exp) Body
// Resolves to either a For statement or a while statement
//-----------------------------------------------------------------------------
public class ForeachStatement : Statement
{
    public ForeachStatement(
        LocalVarDecl var,
        Exp expCollection,
        Statement body
    )
    {
        Debug.Assert(var != null);
        Debug.Assert(expCollection != null);
        
        m_var = var;
        m_expCollection = expCollection;
        m_stmtBody = body;
    }
    
#region Properties & Data
    Statement m_stmtResolved;
    public Statement ResolvedStmt
    {
        get { return m_stmtResolved; }
    }
    
    LocalVarDecl m_var;
    Exp m_expCollection;
    Statement m_stmtBody;

#endregion

#region Checks
    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("foreach");
        m_var.Dump(o);
        m_expCollection.Dump(o);
        m_stmtBody.Dump(o);
        if (m_stmtResolved != null)
        {
            o.WriteStartElement("ResolvedStatement");
            m_stmtResolved.Dump(o);
            o.WriteEndElement();
        }
        o.WriteEndElement();
    }      
    
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_stmtResolved != null);
        m_stmtResolved.DebugCheck(s);
        
    /*
        m_var.DebugCheck(s);
        m_expCollection.DebugCheck(s);
        m_stmtBody.DebugCheck(s);
    */        
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        sb.Write("foreach(");
        this.m_var.ToSource(sb);
        sb.Write(" in ");
        this.m_expCollection.ToSource(sb);
        sb.WriteLine(')');
        this.m_stmtBody.ToSource(sb);
        
    }
#endregion

    // Code generation, just pass on to our resolved statement
    public override void Generate(CodeGen.EmitCodeGen gen)
    {           
        m_stmtResolved.Generate(gen);
    }

#region Resolution
    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        // If collection is an array, then we resolve to a for-loop around the array
        Exp.ResolveExpAsRight(ref m_expCollection, s);
                
        if (m_expCollection.CLRType.IsArray)
        {
            ResolveAsArray(s);
        } else {
            ResolveAsIEnumerator(s);
        }
        
        Debug.Assert(m_stmtResolved != null);
        m_stmtResolved.ResolveStatement(s);
    }
    
    public override void ResolveStatement2(ISemanticResolver s)
    {
        // By now, we have the resolved statement, so just go through
        // to it on the 2nd pass.
        m_stmtResolved.ResolveStatement2(s);
    }

    // We expect to resolve this as an array. If The collection is an 
    // array type, we should be able to do that without any problems.           
    void ResolveAsArray(ISemanticResolver s)
    {
#if false
// foreach(T t in C) [Body];
// Array resolves as:
{
    int i;
    T t;    
    for(i = 0; i < C.Length; i++)
    {
        t = A[i];
        [Body]
    }        
}
#endif
        Identifier idI = new Identifier(".temp", this.Location);
        SimpleObjExp expI = new SimpleObjExp(idI);
        
        Identifier idT = new Identifier(this.m_var.Name, Location);
        
        // Build an AST for the resolved statement
        m_stmtResolved = new BlockStatement(
            new LocalVarDecl[] { 
                new LocalVarDecl(
                    idI, 
                    //new TypeSig(new SimpleObjExp(new Identifier("int", Location)))
                    new ResolvedTypeSig(typeof(int), s)
                ),
                this.m_var
            },
            new Statement[] {
                new ForStatement(
                    new AssignStmtExp(
                        expI,
                        new IntExp(0, this.Location)
                    ),
                    new BinaryExp(
                        expI,
                        new DotObjExp(
                            m_expCollection,
                            new Identifier("Length", Location)
                        ),
                        BinaryExp.BinaryOp.cLT
                    ),
                    new PrePostIncDecStmtExp(expI, false, true),
                    new BlockStatement(
                        null,
                        new Statement[] {
                            new ExpStatement(
                                new AssignStmtExp(
                                    new SimpleObjExp(idT),
                                    new ArrayAccessExp(m_expCollection, expI)
                                )
                            ),
                            this.m_stmtBody
                        }
                    )
                )
            }                        
        ); // end BlockStatement
            
    } // End resolve as array
    
    // We expect to resolve using IEnumerator. This has several conditions,
    // and so we have to do some error checking.
    void ResolveAsIEnumerator(ISemanticResolver s)
    {
#if false
// foreach(T t in C) [Body];
// Enumeration resolves as:
{
    E e;
    T t;
    
    e = C.GetEnumerator();
    while(e.MoveNext())
    {
        t = (T) e.Current;
        [Body]
    }
}
#endif
        // First must find the GetEnumerator() method
        bool dummy;
        MethodExpEntry mGetEnumerator = 
            s.ResolveCLRTypeToBlueType(m_expCollection.CLRType).
            LookupMethod(s, new Identifier("GetEnumerator"), new Type[0], out dummy);

        Identifier idEnum = new Identifier(".Temp", Location);
        SimpleObjExp expEnum = new SimpleObjExp(idEnum);
        
        Identifier idT = new Identifier(this.m_var.Name, Location);
        
        // Build an AST for the resolved statement
        m_stmtResolved = new BlockStatement(
            new LocalVarDecl[] {
                new LocalVarDecl(
                    idEnum,
                    new ResolvedTypeSig(mGetEnumerator.RetType)
                ),
                this.m_var
            },
            new Statement[] {
                new ExpStatement(
                    new AssignStmtExp(
                        expEnum,
                        new MethodCallExp(m_expCollection, new Identifier("GetEnumerator", Location), new ArgExp[0])
                    )
                ),
                new WhileStatement(
                    new MethodCallExp(
                        expEnum,
                        new Identifier("MoveNext", Location),
                        new ArgExp[0]
                    ),
                    new BlockStatement(
                        null,
                        new Statement[]
                        {
                            new ExpStatement(
                                new AssignStmtExp(
                                    new SimpleObjExp(idT),
                                    new AST.CastObjExp(
                                        this.m_var.Sig,
                                        new DotObjExp(expEnum, new Identifier("Current", Location))
                                    )
                                )
                            ),                                
                            this.m_stmtBody
                        }
                    )
                ) // end while
            } // end Statement[]
        );            
    
    
    } // end resolve as Enumerator

#endregion    
        
}

//-----------------------------------------------------------------------------
// While Statement 
//-----------------------------------------------------------------------------
public class WhileStatement : LoopStatement
{
    public WhileStatement(
        Exp expTest,
        Statement stmtBody
    ) : base(expTest, stmtBody)    
    {        
    }
        
    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("while");
        TestExp.Dump(o);
        BodyStmt.Dump(o);
        o.WriteEndElement();
    }
    
    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {   
        gen.Generate(this);
    }
}
#endregion Loop Statements

#region Jump statements    
//-----------------------------------------------------------------------------
// Statement 
//-----------------------------------------------------------------------------
public class LabelStatement : Statement
{
    public LabelStatement(Identifier label)
    {
        Debug.Assert(label != null);
        m_label = label;
    }

    protected Identifier m_label;
    public Identifier LabelId
    {
        get { return m_label; }
    }

    protected LabelEntry m_symbol;
    public LabelEntry Symbol
    {
        get { return m_symbol; }
    }
    
    // Debug checking
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_symbol != null);
    }

    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("LabelStatement");
        o.WriteAttributeString("name", m_label.Text);
        o.WriteEndElement();
    }

    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
    // Label must have not been defined.
        m_symbol = (LabelEntry) s.LookupSymbol(s.GetCurrentContext(), m_label, false);
        if (m_symbol != null)
        {
        // Error, label already defined.
            ThrowError(SymbolError.LabelAlreadyDefined(m_label.Text, m_label.Location, m_symbol.Node.LabelId.Location));
        /*
            s.ThrowError(SymbolEngine.SemanticChecker.Code.cLabelAlreadyDefined, 
                m_label.Location, 
                "Label '" + m_label.Text + "' is already defined at '"+ 
                m_symbol.Node.LabelId.Location + "' in the current scope");
        */                
        } 
        
        m_symbol = new LabelEntry(m_label.Text, this);
        s.GetCurrentContext().AddSymbol(m_symbol);
                
    }

    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {   
        gen.Generate(this);
    }
}

//-----------------------------------------------------------------------------
// Statement 
//-----------------------------------------------------------------------------
public class GotoStatement : Statement
{
    public GotoStatement(Identifier label)
    {
        Debug.Assert(label != null);
        m_label = label;
    }

    protected Identifier m_label;
    public Identifier LabelId
    {
        get { return m_label; }
    }

    protected LabelEntry m_symbol;
    public LabelEntry Symbol
    {
        get { return m_symbol; }
    }
    
    // Debug checking
    public override void DebugCheck(ISemanticResolver s)
    {   
        Debug.Assert(m_symbol != null);
    }

    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("GotoStatement");
        o.WriteAttributeString("name", m_label.Text);
        o.WriteEndElement();
    }

    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        // Can't do anything on first pass
    }

    public override void ResolveStatement2(ISemanticResolver s)
    {       
    // Since we're doing this on a second pass, all labels must have been added
        m_symbol = (LabelEntry) s.EnsureSymbolType(
            s.LookupSymbolWithContext(m_label, true), 
            typeof(LabelEntry), 
            m_label.Location);
    }


    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {   
        gen.Generate(this);
    }
}


//-----------------------------------------------------------------------------
// Continue Statement 
//-----------------------------------------------------------------------------
public class ContinueStatement : Statement
{
    public ContinueStatement()
    {        
    }

    // Debug checking
    public override void DebugCheck(ISemanticResolver s)
    {   
    }

    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ContinueStatement");       
        o.WriteEndElement();
    }

    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {        
        if (!LoopStatement.IsInsideLoop())
        {
            ThrowError(SymbolError.MustBeInsideLoop(this));            
        }
    }

    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {   
        gen.Generate(this);        
    }
}

//-----------------------------------------------------------------------------
// Break Statement 
//-----------------------------------------------------------------------------
public class BreakStatement : Statement
{
    public BreakStatement()
    {        
    }
    
    // Debug checking
    public override void DebugCheck(ISemanticResolver s)
    {   
    }

    // Dump
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("BreakStatement");       
        o.WriteEndElement();
    }

    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        if (!LoopStatement.IsInsideLoop())
        {
            ThrowError(SymbolError.MustBeInsideLoop(this));
        /*
            s.ThrowError(SemanticChecker.Code.cMustBeInsideLoop,
                new FileRange(), 
                "'break' must occur inside a control block (do, while, for, switch)"
                );
        */                
        }
    }

    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {   
        gen.Generate(this);
    }
}

//-----------------------------------------------------------------------------    
// Return statement
// May (or maynot) have a return expression
//-----------------------------------------------------------------------------        
public class ReturnStatement : Statement
{
    public ReturnStatement(Exp e)
    {
        m_exp = e;
    
        // @todo - this is wrong
        if (e != null)
        {
            m_filerange = e.Location;
        } 
        else 
        {
            m_filerange = new FileRange();
        }
    }

    Exp m_exp;    

    //-----------------------------------------------------------------------------
    // Debugging check
    //-----------------------------------------------------------------------------
    public override void DebugCheck(ISemanticResolver s)
    {
        // Expression is optional
        if (m_exp != null)
        {
            m_exp.DebugCheck(s);
        
            // In resolve, we already verified that exp matches the function return type.        
        }
    }

    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        if (m_exp ==null)
            sb.WriteLine("return;");
        else {           
            sb.Write("return ");
            m_exp.ToSource(sb);
            sb.WriteLine(';');            
        }        
    }

    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ReturnStatement");
    
        if (m_exp != null)
            m_exp.Dump(o);
                
        o.WriteEndElement();
    }

    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        MethodExpEntry m = s.GetCurrentMethod();
        if (m_exp != null) 
        {
            Exp.ResolveExpAsRight(ref m_exp, s);
        
            // Ensure that expression we're returning matches the method's
            // return type            
            //s.EnsureDerivedType(m.RetType, m_exp);
            s.EnsureAssignable(m_exp, m.RetType.CLRType);
        } 
        else 
        {
            // If we're not returning an expression, then our return type should be void
            if (m.RetType.CLRType != typeof(void))
            {
                ThrowError(SymbolError.NoReturnTypeExpected(this));
                /*
                s.ThrowError(SemanticChecker.Code.cNoReturnTypeExpected,
                    this.Location, 
                    "Functions with void return type can't return an expression"
                    );
                */
            }        
        }
    }

    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {
        gen.Generate(this);
    }

    public Exp
        Expression
    {
        get
        {
            // This may return null since not all return statements have expressions.
            return m_exp;
        }
    }
}
    

#endregion // Jump statements    
#endregion // Control Flow Statements
    
//-----------------------------------------------------------------------------    
// Expression Statement - statements that just execute expressions
// with sideffects
//-----------------------------------------------------------------------------    

/// <summary>
/// Statement to hold an <see cref="StatementExp"/>.
/// </summary>
/// <remarks>
/// <para><children>Children<list type="bullet"><item>
/// <see cref="StatementExp"/> - the expression to be evaluated as a statement
/// </item></list></children></para>
/// </remarks>
public class ExpStatement : Statement
{
#region Construction
    public ExpStatement(StatementExp sexp)
    {
        Debug.Assert(sexp != null);
        m_sexp = sexp;
    }
#endregion    

#region Properties & Data    
    StatementExp m_sexp;
    public StatementExp StmtExp
    {
        get { return m_sexp; }
    }
#endregion    

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {
        Debug.Assert(m_sexp != null);        
        m_sexp.DebugCheck(s);
    }
    
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("ExpStatement");        
        m_sexp.Dump(o);                    
        o.WriteEndElement();
    }
    
    public override void ToSource(System.CodeDom.Compiler.IndentedTextWriter sb)    
    {
        this.m_sexp.ToSource(sb);   
        sb.WriteLine(';');
    }
#endregion    

    // Semantic resolution
    public override void ResolveStatement(ISemanticResolver s)
    {
        StatementExp.ResolveExpAsRight(ref m_sexp, s);    
    }
        
    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {        
        m_sexp.GenerateAsStatement(gen);
    }
}

//-----------------------------------------------------------------------------
// Constructor chain (like a special purpose method call)
//-----------------------------------------------------------------------------
public class CtorChainStatement : Statement
{
    public enum ETarget
    {
        cThis,
        cBase
    }

#region Construction
    // General ctor chain
    public CtorChainStatement(ETarget eTarget, Exp [] arParams)
    {        
        
        m_eTarget = eTarget;            
        m_arParams = (arParams == null) ? (new Exp[0]) : arParams;
    }

    // Chain to our base's default ctor
    public CtorChainStatement()
    {
        m_eTarget = CtorChainStatement.ETarget.cBase;
        m_arParams = new Exp[0];
    }

    public void FinishInit(MethodDecl nodeCtor)
    {
        Debug.Assert(m_nodeCtor == null);
        m_nodeCtor = nodeCtor;
    }
#endregion

#region Checks
    public override void DebugCheck(ISemanticResolver s)
    {   
        Debug.Assert(m_nodeCtor != null);
        Debug.Assert(m_symTarget != null);
        m_symTarget.DebugCheck(s);
        
        foreach(Exp e in Params)
        {
            e.DebugCheck(s);
        }
    }
            
    // Dump as XML
    public override void Dump(XmlWriter o)
    {
        o.WriteStartElement("CtorChainStatement");                    
        o.WriteAttributeString("target", m_eTarget.ToString());
        o.WriteAttributeString("name", m_nodeCtor.Name);

        foreach(Exp e in Params)
        {
            e.Dump(o);
        }
        o.WriteEndElement();
    }
#endregion

#region properties & Data
    ETarget m_eTarget;
    public ETarget TargetType
    {
        get { return m_eTarget; }
    }

    Exp [] m_arParams;
    public Exp [] Params
    {
        get { return m_arParams; }
    }

    MethodDecl m_nodeCtor;
    public MethodDecl NodeCtor
    {
        get { return m_nodeCtor; }
    }

    MethodExpEntry m_symTarget;
    public MethodExpEntry SymbolTarget
    {
        get { return m_symTarget; }
    }
#endregion

#region Resolution
    // Must resolve all our parameters and the specific ctor that 
    // we chain to.
    public override void ResolveStatement(ISemanticResolver s)
    {
        Debug.Assert(m_nodeCtor != null);

        for(int i = 0; i < Params.Length; i++)
        {            
            Exp.ResolveExpAsRight(ref Params[i], s);
        }

    // Lookup what ctor we chain to.
        TypeEntry tClass = m_nodeCtor.Symbol.SymbolClass;
        if (TargetType == ETarget.cBase)
        {
            tClass = tClass.Super;
        }
        
        System.Type [] alParamTypes = new Type[Params.Length];
        for(int i = 0; i < Params.Length; i++)
        {
            alParamTypes[i] = Params[i].CLRType;
        }

        bool fVarArg;
        m_symTarget = tClass.LookupMethod(
            s, 
            new Identifier(tClass.Name, this.Location), 
            alParamTypes, 
            out fVarArg);
        Debug.Assert(!fVarArg);
        Debug.Assert(m_symTarget != null); 
        Debug.Assert(m_symTarget.SymbolClass == tClass); // @todo -legit? Make sure we actually get a method from tClass?
        
    }              
#endregion

    // Code generation
    public override void Generate(CodeGen.EmitCodeGen gen)
    {
        Debug.Assert(m_nodeCtor != null);
        gen.Generate(this);            
    }
}


} // end namespace