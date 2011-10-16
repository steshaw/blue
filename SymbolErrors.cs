//-----------------------------------------------------------------------------
// General place to put errors relating to symbol Resolution
// These errors can come from anywhere during Resolution (including any
// AST node, a scope, or the Semantic Checker).
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Reflection;

using AST;
using SymbolEngine;

using StringBuilder = System.Text.StringBuilder;

/// <summary>
/// The <c>SymbolError</c> class contains the error codes for all errors occuring durring
/// symbol resolution as well as type-safe static methods to provide the exceptions for
/// each type of error.
/// </summary>
public class SymbolError
{
    //-----------------------------------------------------------------------------
    // Syntax error code specific to Checker
    //-----------------------------------------------------------------------------
    public enum Code
    {
        cUndefinedSymbol,       // a symbol is undefined
        cSymbolAlreadyDefined,  // a symbol is already defined
        cTypeMismatch,          // two types are incompatible
        cIllegalImportAssembly, // Some problem in importing an assembly
        cMissingAsmReference,   // expected the following assembly reference
        
        cShadowCatchHandlers,   // A catch handler is shadowing a previous handler
        cLabelAlreadyDefined,   // A label is already defined
        cBadSymbolType,         // the symbol exists, but is not the type it should be
        cMustBeInsideLoop,      // 'break' & 'continue' must exist inside a loop
        
        cOnlySingleInheritence, // Only support single inheritence
        cNoReturnTypeExpected,  // A void function is trying to return something
        
        cAmbiguousMethod,       // can't resolve an overload
        cMethodNotDefined,      // The method is not defined
        cNoAcceptableOverload,  // The method exists, but there's no proper overload

        cCircularReference,     // Type A derives from B, and B derives from A
        
        cNoParamsOnStaticCtor,  // A static constructor can't have any parameters
        cNotValidLHS,           // The expression is not valid on the LHS
        
        cNotYetImplemented,     // The feature is not yet implemented
                
        cNoFieldInitForStructs, // structs can't have instance field initializers
        cNoAcceptableOperator,  // No operator is defined to takes the given args
        cAsOpOnlyOnRefTypes,    // The as operator can only be used on Reference types
        cBadTypeIfExp,          // The args on an ?: operator have incompatible types
        cMissingInterfaceMethod,// An class is missing a method from a base interface
        cIMethodMustBePublic,   // Methods implementing interfaces must be public
        
        cSymbolNotInNamespace,  // the symbol is not defined in the given namespace
        cSymbolNotInType,       // the symbol is not defined in the given type
        cClassMustBeAbstract,   // Class must be abstract because it has abstract members
        cNoMethodToOverload,
        cCantOverrideFinal,
        cCantOverrideNonVirtual,
        cVisibilityMismatch,
        cMustDeriveFromInterface,
        cNoEventOnRHS,
        cMustBeCompileTimeConstant,
        cNewArrayBoundsMismatch,
        cNoAcceptableIndexer,
        cBaseAccessCantBeStatic,
    }
    
    //-----------------------------------------------------------------------------
    // When we get a symbol error, we want to throw an exception
    //-----------------------------------------------------------------------------
    public class SymbolErrorException : ErrorException
    {
        // @dogfood - if a parameter is of type 'code', then that gets confused with
        // the ErrorException get_Code Property from our base class.
        // For now, use a fully-qualified name. But we should fix this.
        //internal SymbolErrorException(Code c, FileRange location, string s) : 
        internal SymbolErrorException(SymbolError.Code c, FileRange location, string s) : 
            base (c, location, s)
        {
            // All Symbol errors will come through this body.
        }
    }
    
    //-----------------------------------------------------------------------------
    // The symbol is undefined    
    //-----------------------------------------------------------------------------
    public static SymbolErrorException UndefinedSymbol(Identifier id)
    {
        return new SymbolErrorException(
            Code.cUndefinedSymbol,
            id.Location, 
            "'" + id.Text + "' is undefined."
        );
    }
       
    //-----------------------------------------------------------------------------            
    // The expression is not valid on the lefthand side
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NotValidLHS(FileRange location)
    {
        return new SymbolErrorException(
            Code.cNotValidLHS,
            location,
            "This expression is not a valid Left-Hand-Side expression"
            );    
    }
    
    //-----------------------------------------------------------------------------
    // The specified feature is not yet implemented
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NotYetImpl(FileRange location, string stHint)
    {
        return new SymbolErrorException(
            Code.cNotYetImplemented,
            location,
            "'" + stHint + "' not implemented."
            );
    }
    

    //-----------------------------------------------------------------------------
    // A static constructor can't have any paramters
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NoParamsOnStaticCtor(AST.MethodDecl m)
    {
        return new SymbolErrorException(
            Code.cNoParamsOnStaticCtor,
            m.Location,
            "A static constructor can't have any parameters"
            );
    }

    //-----------------------------------------------------------------------------
    // Something went wrong went importing an assembly.
    // Given the assembly we tried to import and a hint with more information.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException IllegalAssembly(Assembly asm, string stHint)
    {
        return new SymbolErrorException(
            Code.cIllegalImportAssembly,
            null, 
            "Error importing '" + asm.ToString() + "'. " + stHint
            );
    }
    
    //-----------------------------------------------------------------------------
    // To import type t, we expect an explicit import for assembly stFilename
    //-----------------------------------------------------------------------------
    public static SymbolErrorException MissingAsmReference(Type t, string stFilename)
    {
        return new SymbolErrorException(
            Code.cMissingAsmReference,
            null,
            "In order to use type '" + t.ToString() + "', you must explicitly reference assembly '"+stFilename + "'");
    }
    
    //-----------------------------------------------------------------------------
    // Type Mismatch
    //-----------------------------------------------------------------------------
    public static SymbolErrorException TypeMismatch(System.Type tFrom, System.Type tTo, FileRange location)
    {
        return new SymbolErrorException(
            Code.cTypeMismatch,
            location,
            "Can't convert from '" + tFrom.ToString() + "' to '" + tTo.ToString() + "'"
        );
    }
    
    //-----------------------------------------------------------------------------
    // @todo - this should be a parser error
    // Helper functions for errors that occur during resolution
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NoFieldInitForStructs(FieldDecl f)
    {
        return new SymbolErrorException(
            Code.cNoFieldInitForStructs,
            f.Location,
            "Structs can't have field intializers for instance fields"
            );  
    }
    
    //-----------------------------------------------------------------------------
    // Overload for binary & unary
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NoAcceptableOperator(        
        FileRange location,
        System.Type tLeft, 
        System.Type tRight,
        BinaryExp.BinaryOp op
        )
    {
        return new SymbolErrorException(
            Code.cNoAcceptableOperator,
            location,
            "No binary operator '" + op +  
            "' that takes arguments '" + tLeft.ToString() + "' and '" + tRight.ToString() + "'.");
            
    }   
         
    //-----------------------------------------------------------------------------
    // Shadow
    //-----------------------------------------------------------------------------
    public static SymbolErrorException ShadowCatchHandlers(        
        FileRange location,
        System.Type tCatchType,
        System.Type tPrevCatchType)
    {
        return new SymbolErrorException(
            Code.cShadowCatchHandlers, location, 
            "Catch handler for type '"+ tCatchType.ToString()+
            "' is inaccessible because of a previous handler of type '" +
            tPrevCatchType.ToString() + "'");
    }
    
    //-----------------------------------------------------------------------------
    // Limits on 'As' operator
    //-----------------------------------------------------------------------------
    public static SymbolErrorException AsOpOnlyOnRefTypes(        
        FileRange location    
        )
    {
        return new SymbolErrorException(Code.cAsOpOnlyOnRefTypes, location,
            "The 'as' operator can only be used on reference types, not value types.");
    }

    //-----------------------------------------------------------------------------
    // Bad types for the ?: operator
    //-----------------------------------------------------------------------------
    public static SymbolErrorException BadTypeIfExp(        
        IfExp e
        )
    {
        Type t = e.TrueExp.CLRType;
        Type f = e.FalseExp.CLRType;
        
        return new SymbolErrorException(
            Code.cBadTypeIfExp, e.Location,
            "Type of '?:' operator can't be determined because there's no implicit conversion between '" + t + "' and '" + f + "'."
            );
    }
    
    //-----------------------------------------------------------------------------
    // Missing an method inherited from an interface
    //-----------------------------------------------------------------------------
    public static SymbolErrorException MissingInterfaceMethod(        
        FileRange location,
        MethodExpEntry mInterface,
        TypeEntry tClass        
        )
    {
        string stClass = tClass.FullName;
        string stMethod = mInterface.PrettyDecoratedName;
        string stInterface = mInterface.SymbolClass.FullName;
        
        return new SymbolErrorException(
            Code.cMissingInterfaceMethod, 
            location,
            "The type '"+stClass+"' does not implement method '"+
            stMethod + "' from interface '" + stInterface + "'");
    }
    
    //-----------------------------------------------------------------------------
    // The methdo must be public because it is implementing an interface.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException IMethodMustBePublic(        
        FileRange location,
        MethodExpEntry mInterface,
        TypeEntry tClass
        
        )
    {
        string stClass = tClass.FullName;
        string stMethod = mInterface.PrettyDecoratedName;
        string stInterface = mInterface.SymbolClass.FullName;
        
        return new SymbolErrorException(
            Code.cIMethodMustBePublic, 
            location,
            "The method '" + stMethod + "' must be public to implement interface '" + stInterface + "'");
    }
    
    //-----------------------------------------------------------------------------
    // The symbol is undefined in the namespace.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException UndefinedSymbolInNamespace(
        NamespaceEntry n,
        Identifier idMissingSymbol
        )
    {
        return new SymbolErrorException(
            Code.cSymbolNotInNamespace,
            idMissingSymbol.Location,
            "'" + idMissingSymbol.Text + "' is not defined in the namespace '" + 
            n.FullName + "'. (Are you missing an assembly reference?)");
    }
    
    //-----------------------------------------------------------------------------
    // The symbol is undefined in the type
    //-----------------------------------------------------------------------------
    public static SymbolErrorException UndefinedSymbolInType(                
        TypeEntry t,
        Identifier idMissingSymbol
        )
    {
        return new SymbolErrorException(
            Code.cSymbolNotInType,
            idMissingSymbol.Location,
            "'" + idMissingSymbol.Text + "' is not defined in the type '" + 
            t.FullName + "'.");
    }
    
    //-----------------------------------------------------------------------------
    // The class must be abstract.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException ClassMustBeAbstract(        
        ClassDecl node)
    {
        return new SymbolErrorException(
            Code.cClassMustBeAbstract,
            node.Location,
            "The class '"+node.Symbol.FullName + 
            "' must be abstract because it contains abstract members.");
    }
    
    //-----------------------------------------------------------------------------
    // No suitable method to override.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NoMethodToOverride(        
        MethodExpEntry m
        )
    {
        return new SymbolErrorException(
            Code.cNoMethodToOverload,
            m.Node.Location,
            "There is no possible method in '" + m.SymbolClass.Super.FullName + 
            "' for the method '" + m.PrettyDecoratedName + "' to override."
            );
    }
    
    //-----------------------------------------------------------------------------
    // Can't override anything marked 'final'
    //-----------------------------------------------------------------------------
    public static SymbolErrorException CantOverrideFinal(        
        MethodExpEntry m,
        MethodExpEntry mSuper
        )
    {
        return new SymbolErrorException(
            Code.cCantOverrideFinal,
            m.Node.Location,
            "'" + m.PrettyDecoratedName + "' can't override the method '" + 
            mSuper.PrettyDecoratedName + "'."
            );
    }
    
    //-----------------------------------------------------------------------------
    // Can't override a non-virtual method.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException CantOverrideNonVirtual(        
        MethodExpEntry m,
        MethodExpEntry mSuper)
    {
        return new SymbolErrorException(
            Code.cCantOverrideNonVirtual,
            m.Node.Location,
            "'" + m.PrettyDecoratedName + "' can't override the non-virtual method '" + 
            mSuper.PrettyDecoratedName + "'."
            );
    }
    
    //-----------------------------------------------------------------------------
    // Can't change visibility when overriding
    //-----------------------------------------------------------------------------
    public static SymbolErrorException VisibilityMismatch(        
        MethodExpEntry m
        )
    {
        return new SymbolErrorException(
            Code.cVisibilityMismatch,
            m.Node.Location,
            "'" +m.PrettyDecoratedName + "' changes visibility when overriding.");
            
    }
    
    //-----------------------------------------------------------------------------
    // This construct must be inside a loop
    //-----------------------------------------------------------------------------
    public static SymbolErrorException MustBeInsideLoop(AST.Node node)
    {
        string stHint = "?";
        if (node is AST.BreakStatement)
        {
            stHint = "break";
        } else if (node is AST.ContinueStatement)
        {
            stHint = "continue";
        } else {
            Debug.Assert(false, "Illegal type:"+ node.GetType());
        }        
        
        return new SymbolErrorException(
            Code.cMustBeInsideLoop,
            node.Location, 
            "'" + stHint + "' must occur inside a control block (do, while, for)"
        );
    }
    
    //-----------------------------------------------------------------------------
    // This label is already defined
    //-----------------------------------------------------------------------------
    public static SymbolErrorException LabelAlreadyDefined(string stName, FileRange lNew, FileRange lOld)
    {
        return new SymbolErrorException(
            Code.cLabelAlreadyDefined, 
            lNew, 
            "Label '" + stName + "' is already defined at '"+ 
            lOld + "' in the current scope");
    }

    //-----------------------------------------------------------------------------
    // There is a circular reference.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException CircularReference(TypeEntry t)
    {
        return new SymbolErrorException(
            Code.cCircularReference,            
            t.Node.Location, 
            "Type '" + t.FullName + "' is in a circular reference"
        );
    }
    
    //-----------------------------------------------------------------------------
    // C# only supports single inheritence.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException OnlySingleInheritence(ClassDecl c)
    {
        return new SymbolErrorException(
            Code.cOnlySingleInheritence,
            c.Location, 
            "Can only derive from a single class (For multiple inheritence, use interfaces)"
        );
    }
    
    //-----------------------------------------------------------------------------
    // A return statement can't have an expression when in a non-void function.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NoReturnTypeExpected(AST.ReturnStatement s)
    {
        return new SymbolErrorException(
            Code.cNoReturnTypeExpected,
            s.Location, 
            "Functions with void return type can't return an expression"
        );
    }
    
    //-----------------------------------------------------------------------------
    // The method is unambiguous.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException AmbiguousMethod(        
        Identifier idName, 
        System.Type [] alParamTypes,
        string stOverloadList
        )
    {        
        string stDecorated = TypeEntry.GetDecoratedParams(idName.Text, alParamTypes);
            
        return new SymbolErrorException(
            Code.cAmbiguousMethod,
            idName.Location,
            "The call to  '" + stDecorated + "'is ambiguous between:\n" + stOverloadList
        );
    }
    
    //-----------------------------------------------------------------------------
    // The method is not defined.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException MethodNotDefined(
        FileRange location,
        string stFullyQualifiedName
    )
    {
        return new SymbolErrorException(
            Code.cMethodNotDefined, 
            location,
            "The method '" + stFullyQualifiedName + "' is not defined."
        );    
    }
    
    //-----------------------------------------------------------------------------
    // There is no suitable overload
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NoAcceptableOverload(
        FileRange location,
        string stFullyQualifiedName
    )
    {
        return new SymbolErrorException(
            Code.cNoAcceptableOverload, 
            location,
            "No acceptable overload for '" + stFullyQualifiedName + "' exists"
        );                
    }
    
    //-----------------------------------------------------------------------------
    // The symbol is of the wrong type.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException BadSymbolType(SymEntry sym, System.Type tExpected, FileRange location)
    {    
        return new SymbolErrorException(
            Code.cBadSymbolType,
            location,
            "Symbol '" + sym.Name + "' must be of type '" + tExpected.ToString() + "', not '" +
            sym.GetType().ToString() + "'"
        );    
    }
    
    //-----------------------------------------------------------------------------
    // Structs & Interfaces can only derive from interfaces
    //-----------------------------------------------------------------------------
    public static SymbolErrorException MustDeriveFromInterface(ClassDecl nodeThis, TypeEntry tBase)
    {
        return new SymbolErrorException(
            Code.cMustDeriveFromInterface,
            nodeThis.Location,
            "'" + tBase.FullName + "' is not an interface and so it can't be in the base-list for '" + nodeThis.Name +"'");
    }
    
    //-----------------------------------------------------------------------------
    // No events allowed in RHS.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NoEventOnRHS(AST.EventExp e)
    {
        return new SymbolErrorException(
            Code.cNoEventOnRHS,
            e.Location,
            "Event '" + e.Symbol.Name + "' can not appear in a right-hand-side expression.");
    }

    //-----------------------------------------------------------------------------
    // The expression must resolve to a compile time constant.
    //-----------------------------------------------------------------------------
    public static SymbolErrorException MustBeCompileTimeConstant(Exp e)
    {
        return new SymbolErrorException(
            Code.cMustBeCompileTimeConstant,
            e.Location,
            "The expression must be a compile time constant.");
    
    }
    
    //-----------------------------------------------------------------------------
    // Array init list length must match size
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NewArrayBoundsMismatch(AST.NewArrayObjExp e)
    {
        int cList = e.ArrayInit.Length;
        
        return new SymbolErrorException(
            Code.cNewArrayBoundsMismatch,
            e.Location,
            "The length of the array-initializer-list (" + cList + ")does not match the rank."); 
            
    }
    
    //-----------------------------------------------------------------------------
    // No acceptable indexer (matched by parameter types)
    //-----------------------------------------------------------------------------
    public static SymbolErrorException NoAcceptableIndexer(FileRange location, System.Type [] alTypes, bool fIsLeft)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("No acceptable '");
        sb.Append(fIsLeft ? "get" : "set");
        sb.Append("' indexer has signature: (");
        
        bool fComma = false;
        foreach(Type t in alTypes)
        {
            if (fComma)
                sb.Append(",");
            sb.Append(t.FullName);   
            fComma = true;
        }
        sb.Append(").");
        
        return new SymbolErrorException(
            Code.cNoAcceptableIndexer, 
            location,
            sb.ToString());
    
    }

    //-----------------------------------------------------------------------------
    // When doing 'base.X(.....)', X must not be static.
    // If it was, we should have done 'T.X(.....)'
    //-----------------------------------------------------------------------------
    public static SymbolErrorException BaseAccessCantBeStatic(FileRange location, MethodExpEntry m)
    {
        return new SymbolErrorException(
            Code.cBaseAccessCantBeStatic,
            location,
            "Can't access static member '"+m.PrettyDecoratedName+"' via a 'base' accessor.");
            
    
    }
    

} // end class







