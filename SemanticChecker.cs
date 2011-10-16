//-----------------------------------------------------------------------------
// File: SemanticChecker.cs
//
// Description:
//  Provide a class to traverse the AST and populate the symbol table.
//  
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Reflection;
using System.Collections;
using System.Xml;

using Blue.Public;
using Log = Blue.Log;
using ErrorLog = Blue.Utilities.ErrorLog;

namespace SymbolEngine
{    
    //-----------------------------------------------------------------------------
    // Interface used by nodes during semantic resolution
    //-----------------------------------------------------------------------------
    
    /// <summary>
    /// Interface used by all functions doing symbol resolution. 
    /// </summary>
    /// <remarks>
    /// This interface provides context for symbols, scope lookup functions,
    /// type-utility functions, and safe exposure to an <see cref="ICLRtypeProvider"/>.
    /// It is not exposed to the driver.
    /// <para> The Driver starts symbol resolution via the <see cref="ISemanticChecker"/> interface.
    /// Resolution is done entirely in:
    /// <list type="number">
    /// <item>ResolveXX() methods on the <see cref="AST.Node"/> class </item>
    /// <item> Helper functions on the <see cref="SymEntry"/> classes. </item> 
    /// <item> The <see cref="SemanticChecker"/> class, which implements the ISemanticChecker. </item>
    /// </list>
    /// An ISemanticResolver interface exposes an API to allow these code sections 
    /// (and no other sections) to do symbol resolution.
    /// </para>    
    /// </remarks>
    public interface ISemanticResolver
    {
        //.....................................................................
        // Context
        //.....................................................................
        
        // Sets the current context that we lookup symbols against.
        // Returns the previous current context, which should be
        // passed to RestoreContext()
        Scope SetCurrentContext(Scope scopeNewContext);
        Scope GetCurrentContext();
        void RestoreContext(Scope scopePreviousContext);
        
        // Lookup a symbol in the current context.
        // The context includes the lexical scope stack, super scopes, 
        // and using directives
        SymEntry LookupSymbolWithContext(Identifier strName, bool fMustExist);
    
        // @todo - make this implicit in the Push & Pop scope    
        // Set the current class that we're processing
        void SetCurrentClass(TypeEntry type);
        TypeEntry GetCurrentClass();

        void SetCurrentMethod(MethodExpEntry m);
        MethodExpEntry GetCurrentMethod();
    
        //.....................................................................
        // Lookup.
        //.....................................................................
        
        // Lookup symbol in a particular scope (and its inherited scopes)    
        SymEntry LookupSymbol(Scope scope, Identifier id, bool fMustExist);
        SymEntry LookupSymbol(Scope scope, string st, bool fMustExist); // deprecated

        // Context-free
        // Lookup a system type, always must exist (else we throw internal error).
        TypeEntry LookupSystemType(string st);
            
        // Get the CLR type for an array / ref type. This will go through IProvider
        System.Type GetArrayType(ArrayTypeEntry sym);
        System.Type GetRefToType(System.Type t); // get a reference type

        // Lookup symbol in current scope stack
    
        // For importing, need a mapping from CLR types to blue types    
        TypeEntry ResolveCLRTypeToBlueType(System.Type t);    
        void AddClrResolvedType(TypeEntry sym);

        //.....................................................................
        // Utility
        //.....................................................................
        
        // For type checking, make sure that tDerived is of type tBase.
        // Throw an exception elsewise        
        void EnsureAssignable(AST.Exp expFrom, System.Type tTo);
        void EnsureAssignable(System.Type tFrom, System.Type cTo, FileRange location);
    
        // Ensure that a symbol is what we expect it to be (ie, we don't try to use a function name 
        // as a label);
        // If match, returns the symbol passed in (for nesting purposes)
        // Else throws an exception
        SymEntry EnsureSymbolType(SymEntry sym, System.Type tExpected, FileRange location);

        // Just checks, doesn't throw an exception either way    
        bool IsDerivedType(TypeEntry tBase, TypeEntry tDerived);
            
        //.....................................................................
        // Debugging
        //.....................................................................
        void Dump();
    }	

    

//-----------------------------------------------------------------------------
// Class to provide a context for semantic checking (symbol resolution)
//-----------------------------------------------------------------------------

/// <summary>
/// This class walks the AST to resolve all symbols.
/// </summary>
public class SemanticChecker : ISemanticChecker, ISemanticResolver
{
#region Checks
    // Dump the entire symbol contents to an XML file
    public void DumpSymbolTable(XmlWriter o)
    {
        o.WriteStartDocument();
            
        o.WriteStartElement("SymbolTable");
        if (m_scopeGlobal != null)            
            m_scopeGlobal.Dump(o, true);
                        
        o.WriteEndElement(); // SymbolTable
                                    
        o.WriteEndDocument();
            
        o.Close();
        
    }
#endregion
    
#region Error Handling

//-----------------------------------------------------------------------------
// Error handling
//-----------------------------------------------------------------------------    
    
    // Shortcut helper functions.
    public void PrintError(SymbolError.SymbolErrorException e)
    {    
        Blue.Driver.StdErrorLog.PrintError(e);
    }
    public void ThrowError(SymbolError.SymbolErrorException e)
    {    
        Blue.Driver.StdErrorLog.ThrowError(e);
    }
    
    
    
    
    
    public virtual void EnsureAssignable(
        AST.Exp expFrom, 
        System.Type tTo
    )
    {
        
        EnsureAssignable(expFrom.CLRType, tTo, expFrom.Location);
    }
#endregion

#region Type Checking functions    
    public virtual void EnsureAssignable(
        System.Type tFrom, 
        System.Type tTo, 
        FileRange location
    )
    {
        bool fOk = TypeEntry.IsAssignable(tFrom, tTo);
        if (!fOk)
        {
            ThrowError(SymbolError.TypeMismatch(tFrom, tTo, location));        
        }
    }
        
    
    // Just check. This is useful because sometimes we want to fail if something
    // is a derived type (ex, CatchHandlers shouldn't be derived type of a previous handler)
    public bool IsDerivedType(TypeEntry tBase, TypeEntry tDerived)
    {
    
        // Note that System.Array is a derived type for all arrays, so special case that
        if (tBase.CLRType == typeof(System.Array))
        {
            if (tDerived.IsArray)
                return true;
        }
        
        // @todo - For now, figure out how to safely convert System.Array to an array
        if (tDerived.CLRType == typeof(System.Array))
        {
            if (tBase.CLRType == typeof(System.Array))
                return true;
        }
        
    
        // Note that arrays may be different instances
        // but non arrays have only one TypeEntry per type
        // So peel off the arrays and get to the base type
        while (tBase.IsArray)
        {
            if (!tDerived.IsArray)
                return false;
                
            tBase = tBase.AsArrayType.ElemType;
            tDerived = tDerived.AsArrayType.ElemType;        
        }
        
        // Compare innermost types
        if (tDerived.IsArray)
        {
            return false;
        } 
        
        // Now walk up tDerived's chain looking for tBase
        TypeEntry t = tDerived;
        do
        {
            if (tBase == t)
                return true;
                
            t = t.Super;
        
        } while(t != null);
                   
        // Ok. So tBase is not a base class of tDerived. Check if tBase is an interface
        // that tDerive implements
        if (tBase.IsInterface)
        {
            if (IsBaseInterface(tBase, tDerived))
                return true;
        }
                       
        return false;        
    }

    // Helper to check if tDerived implements the interface tBaseInterface.
    protected bool IsBaseInterface(TypeEntry tBaseInterface, TypeEntry tDerived)
    {
        Debug.Assert(tBaseInterface.IsInterface);
        
        if (tBaseInterface == tDerived)
            return true;
        
        // Check superclasses
        if (tDerived.Super != null)
            if (IsBaseInterface(tBaseInterface, tDerived.Super))
                return true;

        // Check base interfaces
        foreach(TypeEntry ti in tDerived.BaseInterfaces)
        {
            if (IsBaseInterface(tBaseInterface, ti))
                return true;
        }
        
        return false;
    }

    
    public SymEntry EnsureSymbolType(SymEntry sym, System.Type tExpected, FileRange location)
    {
        if (sym == null)
            return null;
/*
        Type tSym = sym.GetType();
        if (tSym == tExpected)
            return sym;

        if (tSym.IsSubclassOf(tExpected))
            return sym;
*/
        bool fMatch = SymbolEngine.TypeEntry.IsAssignable(sym.GetType(), tExpected);
        if (fMatch)
            return sym;

        ThrowError(SymbolError.BadSymbolType(sym, tExpected, location));
/*
        this.ThrowError(Code.cBadSymbolType,
            location,
            "Symbol '" + sym.Name + "' must be of type '" + tExpected.ToString() + "', not '" +
            sym.GetType().ToString() + "'");
*/
        return sym;
    }
#endregion 
    
//-----------------------------------------------------------------------------           
// Add aliases for the default types      
// Must have loaded mscorlib.dll first
//-----------------------------------------------------------------------------
    protected void AddDefaultTypes(Scope scopeGlobal)
    {               
    // Alias        
        scopeGlobal.AddAliasSymbol("int", LookupSystemType("Int32"));        
        scopeGlobal.AddAliasSymbol("void", LookupSystemType("Void"));
        scopeGlobal.AddAliasSymbol("char", LookupSystemType("Char"));
        scopeGlobal.AddAliasSymbol("bool", LookupSystemType("Boolean"));
        scopeGlobal.AddAliasSymbol("string", LookupSystemType("String"));    
        scopeGlobal.AddAliasSymbol("object", LookupSystemType("Object"));
        
    // Ensure that compound types that are backed by a core clr type are resovled.
    // (mainly Array, Enum, Delegate)
        TypeEntry tArray = LookupSystemType("Array");
        tArray.EnsureResolved(this);

        TypeEntry tEnum = LookupSystemType("Enum");
        tEnum.EnsureResolved(this);
        
        TypeEntry tDelegate = LookupSystemType("MulticastDelegate");
        tDelegate.EnsureResolved(this);
        
    }

#region Importing Assemblies     
//-----------------------------------------------------------------------------
// Create the scopes for an imported types
//-----------------------------------------------------------------------------
    Scope CreateImportedContext(System.Type tImport)
    {
        // Traverse namespaces to find scope
        Scope scope = m_scopeGlobal;
        string s = tImport.ToString();
               
        // In a type's string name, the '.' separates namespaces,
        // the '+' separates for nested classes.        
        // Valid form:
        // i.i.i+i+i+i
                        
        int iStart  = 0;
        int i       = s.IndexOf('.');            
        
        // Search past namespaces
        while(i != -1) 
        {                
            string stNamespace = s.Substring(iStart, i - iStart);
            SymEntry sym = LookupSymbol(scope, stNamespace, false);
                                               
            if (sym == null) 
            {            
                ImportedNamespaceEntry nsImported = new ImportedNamespaceEntry(
                    stNamespace, 
                    s.Substring(0, i)
                    );
                    
                scope.AddSymbol(nsImported);
                    
                scope = nsImported.ChildScope;
            } 
            else 
            {
                // If the symbol already exists, must be a namespace                    
                if (sym is NamespaceEntry) 
                {
                    scope = ((NamespaceEntry) sym).ChildScope;
                } 
                else 
                {
                    ThrowError(SymbolError.IllegalAssembly(tImport.Assembly, "Illegal type: " + s));
                }
            }
            iStart = i + 1;
            i = s.IndexOf('.', iStart);
        }
                   
        // If we're not a nested type, then we can return the scope now
        if (tImport.DeclaringType == null)                   
        {
            Debug.Assert(s.Substring(iStart) == tImport.Name);
            return scope;
        }
        
        // Containing class should have already been added.
        Debug.Assert(TryLookupCLRType(tImport.DeclaringType) != null);
                        
        // Else we have to traverse the class scopes to find out containing scope.        
        // n.n. c1+c2
        i = s.IndexOf('+', iStart);   
        while (i != -1)
        {
            string stClass = s.Substring(iStart, i - iStart);
            
            TypeEntry tBlue = (TypeEntry) LookupSymbol(scope, stClass, true);
            scope = tBlue.MemberScope;
            Debug.Assert(scope != null);
        
            iStart = i + 1;
            i = s.IndexOf('+', iStart);        
        }
        
        Debug.Assert(s.Substring(iStart) == tImport.Name);
        
        return scope;
    }

    // Test to tell if a given type is generic. 
    static bool IsGenericType(System.Type t)
    {
        // Since Blue must compile on v1.1 CLR, the methods on Type to explicitly ask if it's generic are not available.
        // So we use this hack: Generic types in C# have a backtick (`) before the type parameter.

        return t.FullName.IndexOf('`') > 0;
    }

//-----------------------------------------------------------------------------
// Helper to import the specific type and return a TypeEntry. 
// This will recursively import all base types.
// Returns null if we can't import the type.
//-----------------------------------------------------------------------------         
    protected TypeEntry AddImportedType(System.Type tImport)
    {     
        #if true   
        // Don't import non-public classes
        // be wary of nesting
        //if (tImport.IsClass || tImport.IsValueType)
        {
            if (tImport.DeclaringType == null)
            {
                // Not nested
                if (!tImport.IsPublic)
                    return null;
            } else {
                // If Nested, check topmost containing class.
                System.Type t = tImport;
                while (t.DeclaringType != null)
                {
                    t = t.DeclaringType;
                }
                if (!t.IsPublic)
                    return null;            
            }
        }
        #endif
                    
        // If we've already imported this, then nothing to do.   
        {
            TypeEntry t = TryLookupCLRType(tImport);
            if (t != null)
                return t;
        }
        
        // Blue doesn't handle Generics (from V2.0 CLR), so just ignore them when imported.
        if (IsGenericType(tImport))
        {
            Console.WriteLine("Skipping Generic type:" + tImport.FullName);
            return null;
        }

        #if false
        // Debugging facility. Userbreakpoint when we import a specific class.
        if (tImport.Name == "IDictionaryEnumerator")
            System.Diagnostics.Debugger.Break();
        #endif
        
        // Stub immediately to avoid infinite cycles.        
        TypeEntry tBlue = TypeEntry.CreateImportStub(tImport);
        m_hashClrType.Add(tImport, tBlue);
                
        // If we're a nested type, make sure our containing type is imported.                        
        if (tImport.DeclaringType != null)
        {
            AddImportedType(tImport.DeclaringType);
        }
                      
        
        Scope scope = this.CreateImportedContext(tImport);
                             
        
        string stClass = tImport.Name;     
        
        // Already check for multiple imports       
        //if (LookupSymbol(scope, stClass, false) == null)
        {           
            
            // Add Base class
            TypeEntry tSuper = null;            
            if (tImport.BaseType != null)
            {                
                tSuper = AddImportedType(tImport.BaseType);
            }
                
            // Add interfaces, removing all interfaces that we can't access
            System.Type [] tCLRInterfaces = tImport.GetInterfaces();
            ArrayList al = new ArrayList(tCLRInterfaces.Length);            
            
            foreach(System.Type tInterface in tCLRInterfaces)
            {                
                TypeEntry t = AddImportedType(tInterface);
                if (t != null)
                    al.Add(t);
            }
            TypeEntry [] tBlueInterfaces = (TypeEntry[]) al.ToArray(typeof(TypeEntry));
            
            TypeEntry tParent = (tImport.DeclaringType == null) ? 
                null :
                this.ResolveCLRTypeToBlueType(tImport.DeclaringType);
                       
            // @todo - do we have to check if we've been imported again?
            
            // We create the symbol, but don't add the scope until we need to.
            // (else that would be a lot of scopes to add that we'd never use)
            // Note that this must be done on the same reference that we added at the top
            // because by now, the other types have links to that original reference.
            tBlue.FinishImportStub(tSuper, tBlueInterfaces, tParent);
            scope.AddSymbol(tBlue);
                
            
#if true   
            // If we have any nested classes, add them.
            // This will require us to create the class scope.            
            System.Type [] tNestedTypes = tImport.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            if (tNestedTypes.Length > 0)
            {
                tBlue.EnsureScopeCreated();
                foreach(System.Type tNested in tNestedTypes)
                {                
                    AddImportedType(tNested);
                }
            }
#endif            
            return tBlue;
        } 
        /*
        else 
        {            
            ThrowError(SymbolError.IllegalAssembly(tImport.Assembly, 
                "Class '" + tImport.FullName + "' defined multiple times"));
            return null;
        }
        */
    
    } // end function

//-----------------------------------------------------------------------------
// Populate the symbol table with just the TypeEntry for all classes exposed
// through the given assembly. (Implicitly, this will add ImportedNamespaceEntries)
// The actual TypeEntry scopes will be populated on first access
//-----------------------------------------------------------------------------
    protected void ImportAssembly(Assembly a)
    {        
        // LoadFrom used for exact filename
        // Load will look in the gac, etc        
        Log.WriteLine(Log.LF.Verbose, "Importing assembly:" + a.ToString());
        
        Type [] typeList = a.GetTypes();
        
        foreach(Type t in typeList)
        {   
            if (t.IsNotPublic)
                continue;
            AddImportedType(t);        
        }
    
    } // end ImportAssembly
               
    protected Assembly GetMscorlib()
    {
        return Assembly.Load("mscorlib.dll");
    }
#endregion               
           
    Scope m_scopeGlobal;

#region Mapping between CLR & Blue types        
//-----------------------------------------------------------------------------        
// Resolve a CLR type to a Blue Type
// How to handle array types?
//-----------------------------------------------------------------------------
    public TypeEntry ResolveCLRTypeToBlueType(System.Type t)
    {
        Debug.Assert(t != null);
        Debug.Assert(!IsGenericType(t), "Can't resolve CLR generic type:" + t.FullName);
        if (t.IsArray)
        {        
            ArrayTypeEntry a = new ArrayTypeEntry(t, this);
            return a;
        }
        if (t.IsByRef)
        {
            System.Type clrElem = t.GetElementType();
            TypeEntry blueElem = ResolveCLRTypeToBlueType(clrElem);
            return new RefTypeEntry(blueElem, this);
        }
        
        TypeEntry type = (TypeEntry) m_hashClrType[t];
        Debug.Assert(type != null, "type '" + t.ToString() + "' is unresolve in blue");
        
        if (type == null)
        {
            Console.WriteLine("Dump: [");
            IDictionaryEnumerator e = m_hashClrType.GetEnumerator();
            while(e.MoveNext())
            {                
                Console.WriteLine("{0}\t\t{1}", e.Key, e.Value);
            }
            Console.WriteLine("] End Dump");
        }
        return type;
    }
    
    // Return the Blue type for the clr interface 
    // May return null if we haven't added it yet.
    protected TypeEntry TryLookupCLRType(System.Type t)
    {
        //Debug.Assert(t.IsInterface);
        
        TypeEntry type = (TypeEntry) m_hashClrType[t];
        return type;
    }

    public void AddClrResolvedType(TypeEntry sym)
    {
        Debug.Assert(sym != null);
        Debug.Assert(sym.CLRType != null);

        // Make sure the blue & CLR types actually match.
        Debug.Assert(sym.FullName == sym.CLRType.FullName);

        ///////
        System.Type tEnum = sym.CLRType;
        /*
        int iEnum1 = tEnum.GetHashCode();
        int iEnum2 = ((object) tEnum).GetHashCode();
        
        int iInt1 = typeof(int).GetHashCode();
        int iInt2 = ((object) typeof(int)).GetHashCode();
        
        bool fFlag1 = Object.Equals(tEnum, typeof(int));
        bool fFlag2 = Object.Equals(typeof(int), tEnum);
        
        bool f3 = (tEnum == typeof(int));
        bool f4 = (typeof(int) == tEnum);
        */
        //////////
        try
        {
            m_hashClrType.Add(sym.CLRType, sym);
        }
        catch (System.Exception e)
        {
            object o = m_hashClrType[sym.CLRType];
            Debug.Assert(false, "Exception:"+ e.Message);
            
        }
    }

    //-----------------------------------------------------------------------------
    // ...
    // Another bug in the frameworks (10/29/01). System.Type has bad implementions
    // of GetHashCode() and Equals() that make them unsuitable for use with
    // a Hashtable. In particular, a TypeBuilder on an enum is viewed the same
    // as the it's underlying type.
    // So we have to have our own comparer that actually works.
    //-----------------------------------------------------------------------------
    
    class TypeHashProvider : IComparer, IHashCodeProvider
    {   
        public virtual int GetHashCode(object obj)
        {
            return obj.ToString().GetHashCode();
        }

        // return 0 if (a==b), else 1
        public virtual int  Compare(object objA, object objB)
        {
            Debug.Assert(objA is Type);
            Debug.Assert(objB is Type);

#if false        
        int iActual = CompareFast(objA, objB);

#if DEBUG
        // Our comparison should be functionally equivalent to comparing
        // the string names. Unfortunately, the defualt Type.Equals() doesn't
        // do it like that.           
        int iExpected = (objA.ToString() == objB.ToString()) ? 0 : 1;
        Debug.Assert(iExpected == iActual);
#endif
        return iActual;
#else
            // Another emit bug:
            // We have a Type problem. If we go from 1) T --> 2) T[] --> 3) T
            // the T in 1 & 3 may be different, but we expect them to be the same.
    
            int iExpected = (objA.ToString() == objB.ToString()) ? 0 : 1;
            //Debug.Assert(iExpected == iActual);
                
            return iExpected;
#endif
        }
    
        // Fast comparison
        // This is nice, but won't work due to silly bugs in the frameworks (see above).
        int CompareFast(object objA, object objB)
        {
            Type tA = (Type) objA;
            Type tB = (Type) objB;
        
            if (tA.IsEnum && !tB.IsEnum)
                return 1;
            if (!tA.IsEnum && tB.IsEnum)
                return 1;
            
            if (tA.IsEnum && tB.IsEnum)
                return (tA.FullName == tB.FullName) ? 0 : 1;
            
            return (tA == tB) ? 0 : 1;
        }
    } 
    
    protected Hashtable m_hashClrType = new Hashtable (1000, new TypeHashProvider(), new TypeHashProvider());
#endregion
        
    ICLRtypeProvider m_provider;
    
#region Main checking routine     
//-----------------------------------------------------------------------------        
// Main checking routine
// Return true if successful, else false
//-----------------------------------------------------------------------------
    public bool DoCheck(
        AST.ProgramDecl p,
        ICLRtypeProvider provider,
        Assembly [] refs
    )
    {   
        Debug.Assert(provider != null);
        Debug.Assert(p != null);
    
        m_provider = provider;
        
        string stSubPhase = "";
        try
        {   
            m_scopeGlobal = new Scope("Global", null, null);
            
                
                            
            // Import symbols            
            stSubPhase = "importing assemblies";            
            ImportAssembly(GetMscorlib());
            AddDefaultTypes(m_scopeGlobal);
            foreach(Assembly a in refs)
            {
                ImportAssembly(a);
            }
            
            // Pass 1 - Resolve the namespaces and stub the types.
            // This will stub all scopes and create a lexical-scope tree.            
            stSubPhase = "resolving namespaces";            
            p.ResolveNamespace(this, m_scopeGlobal);
            
            
            // Pass 2 - Resolve Types (to both CLR & Blue)             
            stSubPhase = "resolving to clr types";            
            p.ResolveTypes(this, provider);
                		    		    		
            // Pass 3 - resolve class member declarations (Methods & fields)
            stSubPhase = "resolving member declarations";            
            p.ResolveMemberDecls(this, provider);
                		    		
            // Pass 4 - resolve method bodies
            stSubPhase = "resolving member bodies";            
            p.ResolveBodies(this);
                        
            // Final Debug verify before codegen
            stSubPhase = "final debug check";
            p.DebugCheck(this);
            m_scopeGlobal.DebugCheck(this);
            
            p.NotifyResolutionDone();
            
            return true;
        }
        
        // Strip away SymbolErrors; we've already reported them when we first threw them.
        catch (SymbolError.SymbolErrorException)
        {            
            return false;
        }
        catch(System.Exception e)
        {
            Blue.Driver.PrintError_InternalError(e, "Symbol Resolution(" + stSubPhase + ")");
            return false;
        }
    }
#endregion

#region Manage the scope stack       
/*
    // AST call back on this to push / pop / query scopes
    // Can only push a scope once before popping it
    public void PushScope(Scope scope)
    {        
        Debug.Assert(scope != null);
        Debug.Assert(scope.m_parent == null);
            
        Scope t = m_top;
        m_top = scope;
        scope.m_parent = t;
    }
        
    protected Scope m_top; // top of stack
        
    // Pops off the top scope. If top scope doesn't match scope,
    // then scope stack is corrupted and we throw an exception
    public void PopScope(Scope scope) 
    { 
        Scope t= m_top;
        Debug.Assert(m_top == scope);
            
        m_top = m_top.m_parent;
        t.m_parent = null;
    }
        
    // Get the current scope. Used to add symbols
    public Scope GetCurrentScope() 
    {
        return m_top;
    }
*/    
    //-----------------------------------------------------------------------------        
    // Set the current class that we're processing
    //-----------------------------------------------------------------------------
    protected TypeEntry m_curClass;        
    public void SetCurrentClass(TypeEntry type)
    {
        m_curClass = type;
    }
    
    public TypeEntry GetCurrentClass()
    {
        return m_curClass;
    }


    protected MethodExpEntry m_curMethod;
    public void SetCurrentMethod(MethodExpEntry m)
    {
        m_curMethod = m;
    }

    public MethodExpEntry GetCurrentMethod()
    {
        return m_curMethod;
    }    
#endregion    
    
#region Hookup to Provider
    // Given a symbol for an array type, get the corresponding CLR type.
    public System.Type GetArrayType(ArrayTypeEntry sym)
    {
        System.Type t = m_provider.CreateCLRArrayType(sym);
        Debug.Assert(t != null);
        return t;
    }
    
    // get a reference type
    public System.Type GetRefToType(System.Type tElem)
    {
        System.Type t = m_provider.CreateCLRReferenceType(tElem);
        Debug.Assert(t != null);
        return t;
    }
#endregion    
    
#region Lookup functions    
    
    // We only need Lookup() during the resolve phase (because that's the only time
    // we need to convert text into symbols)
    // After that, we can just use the symbols directly    
    
    // Lookup an entry in a specific scope
    // If it doesn't exist, then return null if !fMustExist and throw if fMustExist
    public SymEntry LookupSymbol(Scope scope, Identifier id, bool fMustExist)
    {
        SymEntry  s = scope.LookupSymbol(id.Text);
        if (fMustExist && s == null)
        {
            ThrowError(SymbolError.UndefinedSymbol(id));
        }
        return s;
    }
    
    
    // Get rid of this function
    public SymEntry LookupSymbol(Scope scope, string st, bool fMustExist)
    {
        SymEntry s = scope.LookupSymbol(st);        
        bool f= false;
        if (f) {
            System.Xml.XmlWriter o = new System.Xml.XmlTextWriter(new System.IO.StreamWriter("dump.xml"));
            scope.Dump(o, true);
            o.Close();
        }
        
        if (fMustExist && s == null)
        {
            FileRange range = new FileRange();
            range.Filename = "<not specified>";
            
            Identifier id = new Identifier(st, range);
            //ThrowError_UndefinedSymbol(id);
            ThrowError(SymbolError.UndefinedSymbol(id));
        }
        return s;
    }
    
    // Lookup a system type
    // Context-free
    public TypeEntry LookupSystemType(string st)
    {        
        NamespaceEntry nsSystem = (NamespaceEntry) LookupSymbol(m_scopeGlobal, "System", true);
        Scope scopeSystem = nsSystem.ChildScope;
        
        
        SymEntry s = LookupSymbol(scopeSystem, st, true);        
        
        // An end-user program can't lookup system types, so this assert should be fine.        
        Debug.Assert(s is TypeEntry, "Expected '" + st + "' is a type");
        
        return s as TypeEntry;
    }
    
    // Sets the current context that we lookup symbols against.
    // Returns the previous current context, which should be
    // passed to RestoreContext()
    public virtual Scope SetCurrentContext(Scope scopeNewContext)
    {
        Scope prev = m_CurrentContext;
        m_CurrentContext = scopeNewContext;
        return prev;
    }
    
    public virtual void RestoreContext(Scope scopePreviousContext)
    {
        m_CurrentContext = scopePreviousContext;
    }
    
    public virtual Scope GetCurrentContext()
    {
        return m_CurrentContext;
    }
    
    Scope m_CurrentContext;
        
    // Lookup a symbol in the current context.
    // The context includes the lexical scope stack, super scopes, 
    // and using directives        
    // If it doesn't exist, then return null if !fMustExist and throw exception if fMustExist
    public virtual SymEntry LookupSymbolWithContext(Identifier id, bool fMustExist)
    {   
        string strName = id.Text;
    
        // Search through stack of lexical scopes
        SymEntry sym = null;
        Scope t = m_CurrentContext;
        while(t != null)
        {
            sym = LookupSymbol(t, id, false); // <-- smart lookup, go through ILookupController
            if (sym != null) 
                return sym;
            t = t.m_LexicalParent;            
        }

        // Don't need this any more with ILookupControllers
        #if false
        // Check using directives if not found in the current scope stack
        // Do this by traversing the scope stack and looking for UserNamespaceEntry
        // (we can never be in an imported namespace, so that's ok)
        t = m_CurrentContext;
        while (t != null)
        {            
            AST.NamespaceDecl node = t.Node as AST.NamespaceDecl;            
            if (node != null)
            {
                sym = node.LookupSymbolInUsingDirectives(this, id);
                if (sym != null)
                    return sym;
            }

            t = t.m_LexicalParent;
        }
        #endif

            
        // Symbol  not found
        if (fMustExist) 
        {            
            //ThrowError_UndefinedSymbol(id);
            ThrowError(SymbolError.UndefinedSymbol(id));
        }
        return null;
    }
#endregion        

#if DEBUG
#region Debugging    
    // Debugging helper to print the current context
    public virtual void Dump()
    {    
        Console.WriteLine("*** Dump of current SemanticChecker state [");
        m_CurrentContext.DumpTree();
        m_CurrentContext.DumpKeys();
        Console.WriteLine("Current class:{0}", 
            (m_curClass == null) ? "null" : m_curClass.ToString());
        Console.WriteLine("Current method:{0}", 
            (m_curMethod == null) ? "null" : m_curMethod.PrettyDecoratedName);
        Console.WriteLine("]");        
    }
#endregion
#endif

} // end class SemanticChecker

} // end namespace