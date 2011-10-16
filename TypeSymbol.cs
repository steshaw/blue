//-----------------------------------------------------------------------------
// Symbols for types
//
// @todo - Note, currently these have a terrible object model. TypeEntry
// should have a lot of virtual functions on it that the derived types
// just override. (Similar to the Type AST nodes). Need to fix this.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Xml;

using Blue.Public;
using Log = Blue.Log;
using Modifiers = AST.Modifiers;


namespace SymbolEngine
{
#region Classes for Type Symbols
    //-----------------------------------------------------------------------------
    // Helper for TypeEntry. Keep overload info for resolving members
    //-----------------------------------------------------------------------------
    public class OverloadedErrorInfo
    {
        protected MethodExpEntry m_symbol;
        protected MethodExpEntry m_symbolVarArg;
        protected int m_count = 0;
        protected bool m_fNoHeader;
        
        // Keep a lazy string record of the overloads for error information.
        // We don't initialize this until we actually get some ambiguity.        
        protected System.Text.StringBuilder m_sbOverloads;

#if DEBUG            
        public static bool m_fLog = false;           
#endif        
                
        public void SetNoHeader()
        {
#if DEBUG        
            if (m_fLog)
                Console.WriteLine("Set no header");        
#endif        
        
            m_fNoHeader = true;
        }
        
        public bool NoHeader
        {
            get { return m_fNoHeader; }
        }
                
        // Return # of matches, giving precedence to non-vararg
        public int MatchCount
        {
            get 
            { 
                return (m_count == 0) ? 
                    ((m_symbolVarArg == null) ? 0 : 1) :
                    m_count; 
            }
        }
        
        public string GetOverloadList()
        {
            return m_sbOverloads.ToString();
        }
        
        public void AddMatch(MethodExpEntry symbol)
        {
#if DEBUG        
            if (m_fLog)
            {
                Console.WriteLine("Adding match:{0}", //TypeEntry.GetDecoratedParams(symbol),
                    symbol.PrettyDecoratedName);        
            }
#endif      
            // If we're ambiguous, then start tracking it for error info          
            // Don't create the string builder until we hit our first ambiguous overload
            if (m_count == 1)
            {                
                m_sbOverloads = new System.Text.StringBuilder();
                
                // Add the first entry
                // @dogfood - should be able to match 0 non-varargs
                //m_sbOverloads.AppendFormat("\t(1):");
                m_sbOverloads.Append("\t(1):");
                m_sbOverloads.Append(m_symbol.PrettyDecoratedName);
                m_sbOverloads.Append('\n');
            }
                        
            m_count++;            
            m_symbol = symbol;
            
            if (m_count > 1)
            {
                // Keep appending the overload entries
                m_sbOverloads.AppendFormat("\t({0}):", m_count);
                m_sbOverloads.Append(symbol.PrettyDecoratedName);
                m_sbOverloads.Append('\n');
            }
        }

        public void AddVarargMatch(MethodExpEntry symbol)
        {
#if DEBUG        
            if (m_fLog)
                Console.WriteLine("Adding vararg match:{0}", TypeEntry.GetDecoratedParams(symbol));        
#endif                
            m_symbolVarArg = symbol;
        }

        public bool IsVarArgMatch
        {
            get 
            {
                return (m_symbol == null) && (m_symbolVarArg != null);
            }
        }

        // Return the symbol, giving precedence to the non-vararg version
        public MethodExpEntry Symbol
        {            
            get { return (m_symbol == null) ? m_symbolVarArg : m_symbol; }
        }
    }

#if false    
    // @todo - this may be a much better hierarchy than what we currently have.

#region TypeEntry base class
    //-----------------------------------------------------------------------------
    // Abstract base class to represent Blue's notion of a type.
    // It's rather silly that we have to maintain an entire symbol table when
    // these are so closely related to System.Type, but alas. TypeBuilders are 
    // not System.Type. 
    //-----------------------------------------------------------------------------
    public abstract class TypeEntry : SymEntry
    {
    
#region Properties
        // Get the System.Type associated with this type entry.
        System.Type CLRType
        {
            get;
        }
#endregion
    }
    
    // A nonreference type
    public abstract class NonRefTypeEntry : TypeEntry
    {
    
    }
    
    // Referenece to any NonRefType
    // Flyweights
    public class RefTypeEntry : TypeEntry
    {
    
    }
    
    // Array of non-ref types
    // These are flyweights. 
    public class ArrayTypeEntry : NonRefTypeEntry
    {
    
    }
    
    // Simple type. These are unique for each type and have a 1:1 match
    // with the a System.Type.
    public class SimpleTypeEntry : NonRefTypeEntry
    {
    
    }
    
    // Enum.
    public class EnumTypeEntry : SimpleTypeEntry
    {
    
    }
    
    // Imported type already has it's CLRType set.
    public class ImportedTypeEntry : NonRefTypeEntry
    {
        
    }
#endregion
#endif


    //-----------------------------------------------------------------------------
    // Represent a type
    // Under the CLR, All types are objects and can have members on them
    // (even primitives like int)
    //-----------------------------------------------------------------------------
    public class TypeEntry : SymEntry, ILookupController
    {
        public enum Genre
        {
            cClass,
            cStruct,
            cInterface,
            
            cReference, // A reference isn't a Class/Struct/Interface
        }
        
        public static Genre GenreFromClrType(System.Type t)
        {
            if (t.IsInterface)
                return Genre.cInterface;
            else if (t.IsValueType)
                return Genre.cStruct;
                
                // ...
                // This is another bug in the CLR Frameworks. MSDN clearly says that
                // typeof(System.Enum).IsClass is supposed to be true 
                // (even though Enum derives from ValueType).
                // But it's not, so we have to special case that.
                // In V2,the docs  will probably be changed to reflect the behavior
                // (because we can't exactly change the behavior at this point). So
                // in that sense, this will be converted from a 'bug' into a 'feature'.
            else if (t.IsClass || (t == typeof(System.Enum)))
                return Genre.cClass;            
            
            
            Debug.Assert(false, "expected Interface,Class, or Struct");  
            return Genre.cClass;
        }
    
#region Construction

#region User Types
        // We need 2-phase initilization
        // 1 - Add stubs
        // 2 - add rest of data (super class, base interfaces)
        
        // Creating from a classdecl
        public TypeEntry(
            string stFullName,            
            AST.ClassDecl nodeDecl,             
            Genre genre,
            Scope scopeParent
            )
        {            
            m_strName = (nodeDecl ==null) ? "<empty>" : nodeDecl.Name;
            m_stFullName = stFullName;
            m_decl = nodeDecl;
            
            m_genre = genre;
            if (genre == Genre.cReference)
            {
            
            } 
            else 
            {
                this.m_mods = nodeDecl.Mods;
            }
            
            // Create a scope now so that we can add stubs for nested types into it.
            // We'll have to set the super-scope later
            // ******** need this for nested types ....
            if (genre != Genre.cReference)
            {
                //m_scope = new Scope((m_genre.ToString()) + "_" + m_strName, nodeDecl, scopeParent);
                m_scope = new Scope((m_genre.ToString()) + "_" + m_strName, this, scopeParent);
            }            
        }

        protected bool m_fIsInit = false;

        public void InitLinks(
            TypeEntry tSuper, 
            TypeEntry [] interfaces  // can be null
            )
        {
            Debug.Assert(m_fIsInit == false);
            Debug.Assert(m_super == null);
            
            m_interfaces = (interfaces == null) ? m_emptyinterfaces : interfaces;
            m_super = tSuper;
            
            // Set containing class, based off lexical scope
            AST.ClassDecl nodeParent = m_scope.LexicalParent.Node as AST.ClassDecl;
            if (nodeParent != null)
            {
                m_containingClass = nodeParent.Symbol;
                Debug.Assert(m_containingClass != null);
            }
        }

        // Sets super scope
        public void FinishInit()
        {            
            Debug.Assert(m_fIsInit == false);
            
            // Relies on super classes being inited already            
            if (m_super != null)
            {
                Debug.Assert(m_super.m_fIsInit);
            }
            foreach(TypeEntry t in m_interfaces)
            {
                Debug.Assert(t.m_fIsInit);
            }
                        
            // Set scopes   
            Debug.Assert(IsInterface || (m_super.MemberScope != null));
            //Scope sSuper = (m_super == null) ? null : m_super.MemberScope;
            
            //m_scope.SetSuperScope(sSuper);

            m_fIsInit = true;
        }
#endregion        
        
#region Imported Types
        // Return a stub used for importing
        static public TypeEntry CreateImportStub(System.Type clrType)
        {
            return new TypeEntry(clrType);
        }
        
        public void FinishImportStub(
            TypeEntry tSuper,
            TypeEntry [] interfaces, // can be null,
            TypeEntry tParent
        )
        {
            m_interfaces        = (interfaces == null) ? m_emptyinterfaces : interfaces;
            m_super             = tSuper;
            m_containingClass   = tParent;
            m_fIsInit           = true;
        }
        
        // Stub entry
        private TypeEntry(System.Type clrType)
        {
            m_typeCLR       = clrType;
            m_stFullName    = clrType.FullName;
            m_strName       = clrType.Name;
            m_genre         = GenreFromClrType(clrType);
            m_fIsImported   = true;
            m_fIsInit       = false;
        }

#endregion

#region Compound types (Array, Enum, Delegate)
        // Protected constructor used by Derived TypeEntry to initialize this
        // to a standard type (ex: Array, Enum, Delegate)
        protected TypeEntry(
            ISemanticResolver s, 
            System.Type clrBaseType
            )
        {          
        
            // Common properties
            this.m_fIsImported = false; // @todo - is this right?     
            this.m_genre = Genre.cClass;
            
            // Need to set everything based of the backing type
            if (clrBaseType == typeof(System.Array))
            {   
                Debug.Assert(this.IsArray);
                
                // Must get the CLR type from the provider             
                TypeEntry tArray = s.LookupSystemType("Array");
                
                this.m_scope = tArray.m_scope;
                this.m_interfaces = tArray.m_interfaces;                
                this.m_super = tArray;
                
                this.m_fIsImported = false; // @todo - is this right?
                
                // ClrType will be set in ArrayType's FinalInit()
                this.m_typeCLR = null;
                
                // Not done init yet. Must call FinishArrayInit();
                
            
            } 
            
            else if (clrBaseType == typeof(System.Enum))
            {
                Debug.Assert(this is SymbolEngine.EnumTypeEntry);

                // Must get the CLR type from the provider             
                TypeEntry tEnum = s.LookupSystemType("Enum");
                
                //this.m_scope = tEnum.m_scope;
                this.m_interfaces = tEnum.m_interfaces;
                this.m_super = tEnum;
                                                                
                // ClrType will be set in FinalInit()
                this.m_typeCLR = null;
            }

            else 
            {
                Debug.Assert(false, "Illegal use of TypeEntry ctor");
            }
        }
        
#endregion

        public bool IsInit
        {
            get { return m_fIsInit; }
        }

#endregion
        
#region Other Methods
        public void SetCLRType(ICLRtypeProvider provider)
        {
            m_typeCLR = provider.CreateCLRClass(this);
        }
            
        // Return true if we can assisgn on object of type cFrom to an
        // object of type cTo. Else false. 
        // We should be able to do this w/ System.Type.IsAssignableFrom,
        // but the current implementation is buggy.
        //
        // Implicit conversion if:
        // - cFrom == cTo
        // - cFrom is a derived class of cTo
        // - cTo is a base interface of cFrom   
        // - cFrom is 'null' and cTo is not a value type
        // - cTo is 'Object'
        public static bool IsAssignable(System.Type cFrom, System.Type cTo)
        {
            if (cTo == typeof(object))
                return true;
                
            // @todo - IsValueType the proper check?
            if (cFrom == null && !cTo.IsValueType)
                return true;
            
            if (cFrom == null || cTo == null)
                return false;

            // First strip off References (since we can implicitly assign between
            // refs & values). 
            // Also, we can have T[]& <- T[], so strip refs before matching arrays. 
            // (Note we _can't_ have T&[])
            // If A <- B, then we can assign A <- B&, A& <- B, and A& <- B&. 
            // Codegen will deal with the indirection.
            if (cFrom.IsByRef) 
                cFrom = cFrom.GetElementType();
            
            if (cTo.IsByRef)
                cTo = cTo.GetElementType();
                
            #if false                
            // These are good checks, but we don't have to do them yet...
            // Check pointers
            if (cFrom.IsPointer && !cTo.IsPointer)                
                return false;
            if (!cFrom.IsPointer && cTo.IsPointer)
                return false;
            if (cFrom.IsPointer && cTo.IsPointer)
            {
                cFrom = cFrom.GetElementType();
                cTo = cTo.GetElementType();
                return IsAssignable(cFrom, cTo);
            }
            #endif
                
            // Check for arrays. Note that we can only do real comparisons on non-array
            // types. (ie, compare object references for equality, query for interfaces,
            // etc).
            
            // We can always assign from T[] --> System.Array
            if (cFrom.IsArray && (cTo == typeof(System.Array)))
                return true;
                       
            // If one's an array, and the other's not, then we can't assign
            if (cFrom.IsArray && !cTo.IsArray)
                return false;
            if (!cFrom.IsArray && cTo.IsArray)
                return false;
                
            // Can assign A[] -> B[] if we can assign A->B & the ranks are the same
            if (cFrom.IsArray && cTo.IsArray)
            {
                if (cFrom.GetArrayRank() != cTo.GetArrayRank()) 
                    return false;
                    
                Type cFrom1 = cFrom.GetElementType();
                Type cTo1   = cTo.GetElementType();
                
                bool fOk = IsAssignable(cFrom1, cTo1);                
                return fOk;
            }
            
            // At this point, we're not an array/pointer

            if (cFrom == cTo)
                return true;


            // Check implicit conversions
            if (HasImplicitConversion(cFrom, cTo))
                return true;

            if (cFrom.IsSubclassOf(cTo))
                return true;

            if (SearchInterfaces(cFrom, cTo))
                return true;
            
            return false;
        }
        
        private static bool HasImplicitConversion(System.Type cFrom, System.Type cTo)
        {
            // Check for implicit numeric conversions:
            if (cFrom == typeof(char) && cTo == typeof(int))
                return true;
        
        
            // Check for implicit operator-overload conversions
            return false;
        }
        
        // Helper for IsAssignableFrom
        // Search transitive closure of interfaces
        private static bool SearchInterfaces(System.Type cFrom, System.Type cTo)
        {
            System.Type [] tI = cFrom.GetInterfaces();
            foreach(System.Type t in tI)
            {
                if (t == cTo)
                    return true;
                    
                if (SearchInterfaces(t, cTo))
                    return true;
            }
            
            // check base class
            if (cFrom.BaseType != null)
            {
                if (SearchInterfaces(cFrom.BaseType, cTo))
                    return true;
            }
            
            return false;        
        }

#endregion

#region ILookupController
        
        // Search up the super class chain, base interfaces  
        // Return if found.      
        public SymEntry SmartLookup(string stIdentifier, Scope scope)
        {            
            if (this.IsInterface)
            {
                SymEntry sym = this.MemberScope.LookupSymbolInThisScopeOnly(stIdentifier);
                if (sym != null)
                    return sym;
                    
                Debug.Assert(this.Super == null, "No base class on interface");
                // Check base interfaces
                foreach(TypeEntry ti in BaseInterfaces)
                {
                    sym = ti.SmartLookup(stIdentifier, null);
                    if (sym != null)
                        return sym;
                }            
            } else {            
                TypeEntry tSearch = this;            
                while(tSearch != null)
                {
                    SymEntry sym = tSearch.MemberScope.LookupSymbolInThisScopeOnly(stIdentifier);
                    if (sym != null)
                        return sym;
                    tSearch = tSearch.Super;                    
                }        
            }
            return null;
        }
    
        // Get a node responsible for this scope.
        // For imported types, this will be null
        public AST.Node OwnerNode { 
            get { return this.Node; }
        }
    
        // Get a symbol responsible for this scope. 
        // This may be null. (If this is null, then OwnerNode should not be null).
        public SymEntry OwnerSymbol { 
            get { return this; }
        }
    
        // For debugging purposes. Used by DumpTree();
        public void DumpScope(Scope scope)
        {
            if (this.IsInterface)
            {
                // Interface. No superclass, multiple base classes
                // Have to dump a whole tree here
                Console.Write("->{");
                bool f = false;
                foreach(TypeEntry ti in this.BaseInterfaces)
                {
                    if (f)
                        Console.Write(",");
                    Console.Write(ti.Name);
                    ILookupController p = ti;
                    p.DumpScope(ti.MemberScope); // recursive
                    f = true;                    
                }
                Console.Write("}");
            } else {
                // Class, struct w/ Super class chain.  
                // Just dump a list              
                TypeEntry tSearch = this.Super;            
                while(tSearch != null)
                {
                    Console.Write("->{0}", tSearch.MemberScope.m_szDebugName);
                    tSearch = tSearch.Super;
                }
            }
        }

#endregion

#region Checks
        // Debugging support. Since this is a graph traversal, we need a flag
        // to note if we've already checked this (and prevent an infinite loop)
        protected bool m_fDebugChecked = false;
        public override void DebugCheck(ISemanticResolver s)
        {        
            if (m_fDebugChecked)
                return;

            Debug.Assert(m_fIsInit);
                
            m_fDebugChecked = true; // set flag before the recursion
                
            Debug.Assert(AST.Node.m_DbgAllowNoCLRType || m_typeCLR != null);
            
            if (Node != null)
            {
                Debug.Assert(Node.Symbol == this);                
            }
            
            // Scope can be null (if we're not resolved)
            if (m_scope != null) 
            {                   
                m_scope.DebugCheck(s);            
            }            
        }
        
        // Write out this Entry's name, attributes, (and if fRecursive, then nested scope too)
        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("TypeEntry");
            o.WriteAttributeString("name", this.m_strName);
            if (fRecursive && (m_scope != null)) 
            {
                m_scope.Dump(o, true);
            }
            o.WriteEndElement();        
        }
#endregion


#region Properties and Data

        static readonly TypeEntry [] m_emptyinterfaces = new TypeEntry[0];
        // Attributes on this type.
        // @todo - use reflection's attributes or our own?
        //        System.Reflection.TypeAttributes m_attrs = 0;
        
        protected Modifiers m_mods;
        public Modifiers Mods
        {
            get { return m_mods; }
        }
        
                
        Genre m_genre;
                        
        public bool IsClass
        {            
            get { return m_genre == Genre.cClass; }
        }

        public bool IsInterface
        {            
            get { return m_genre == Genre.cInterface; }
        }
        
        public bool IsStruct
        {
            get { return m_genre == Genre.cStruct;}
        }

        bool m_fIsImported;
        public bool IsImported
        {
            //get { return (m_attrs & System.Reflection.TypeAttributes.Import) == System.Reflection.TypeAttributes.Import; }
            get { return m_fIsImported; }
        }

        // Array support
        public bool IsArray
        {
            get { return this is ArrayTypeEntry; }
        }
        
        // Get the current type as an array type.
        // If we're a reference, then ignore the refence and get the nested type
        // as an array type.
        public ArrayTypeEntry AsArrayType
        {
            get 
            { 
                if (IsRef)
                    return AsRefType.ElemType.AsArrayType;                
                else                    
                    return this as ArrayTypeEntry; 
            }    
        }
        
        public bool IsRef
        {
            get { return this is RefTypeEntry; }            
        }
        
        public RefTypeEntry AsRefType
        {
            get { return this as RefTypeEntry; }        
        }
            
        // Get string repsentation
        public override string ToString()
        {
            return "Type:" + m_strName;
        }

        // Fully qualified name including Namespace
        protected string m_stFullName;
        public string FullName
        {
            get { return m_stFullName; }
        }
            
        // Location in AST where this class is declared
        protected AST.ClassDecl m_decl;
        public AST.ClassDecl Node
        {
            get { return m_decl; }
        }
        
        // Super class. Must be non-null except for System.Object
        protected TypeEntry m_super;        
        public TypeEntry Super
        {
            get { return m_super; }
        }
        
        protected TypeEntry m_containingClass;
        
        // Interfaces that we implement / derive from
        protected TypeEntry [] m_interfaces;
        public TypeEntry [] BaseInterfaces
        {
            get { return m_interfaces; }
        }
                
        
        // Scope containing our members
        protected Scope m_scope;
        public Scope MemberScope
        {
            get { return m_scope; }
        }
            
        // Track the CLR's corresponding type
        // Return null if not yet set        
        // Set this directly for imported types
        // Set this in codegen for emitted types
        protected System.Type m_typeCLR;        
        public System.Type CLRType
        {
            get { return m_typeCLR; }
        }
        
        // If this is a nested type, return the containing type.
        // If this is not a nested type, return null
        public TypeEntry GetContainingType()
        {            
            return m_containingClass;            
        }
#endregion
        
        
        
#region Resolution

#region Resolve Method Lookups   

#region Decoration     
        // Convert a function name w/ parameter types into a single decorated string
        static public string GetDecoratedParams(string stName, System.Type [] alParamTypes)
        {
            string stParam = "";
            foreach(Type t in alParamTypes)
            {       
                stParam += ((t == null) ? "null" : t.ToString()) + ";";                    
            }
            return stName + ":" + stParam;
        }
        
        // Get a decorated version right off the method symbol.
        // Comparing decorated names will only find an exact match. But because there
        // are many implicit conversions between types, different string names can
        // still match.
        static public string GetDecoratedParams(MethodExpEntry m)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(m.Name);
            sb.Append(':');
            for(int i = 0; i < m.ParamCount; i++)
            {
                Type t = m.ParamCLRType(i);
                   
                sb.Append((t == null) ? "null" : t.ToString());
                sb.Append(';');
            }
            return sb.ToString();
        }
#endregion        

#region Internal Lookup helpers        
        // Helper to look for a method header in an interface
        // Recursive. May return true / false. 
        // Does not throw exceptions
        protected bool LookForMethodHeaderInInterface(               
            string stName
            )
        {            
            bool f = false;
            if (f)
            {
                m_scope.DebugConsoleDump();
            }
            
            //SymEntry sym = m_scope.LookupSymbol(m_stMethodHeaderPrefix + stName);
            MethodHeaderEntry header = LookupMethodHeader(stName);
            if (header != null)
                return true;
            
            foreach(TypeEntry t in BaseInterfaces)
            {
                Debug.Assert(t.IsInterface);
                if (t.LookForMethodHeaderInInterface(stName))
                    return true;                
            }
            
            return false;
        }

        // Helper to lookup a method in an interface
        protected void LookupMethodInInterface(
            ISemanticResolver s, 
            string stDecorated,
            OverloadedErrorInfo info
            )
        {            
            Debug.Assert(m_scope != null);
            
            // First try a direct match
            MethodExpEntry sym = (MethodExpEntry) s.LookupSymbol(m_scope, stDecorated, false);
            
            if (sym != null) 
            {
                info.AddMatch(sym);
            }
                
            // Since we did a smart-lookup, the LookupController already checked
            // all base interfaces for us, so we don't have to repeat that exercise. 
            // If we do, we'll double count.                
            #if false                
            // Recursively check all inherited interfaces
            foreach(TypeEntry t in BaseInterfaces)
            {
                Debug.Assert(t.IsInterface);
                t.LookupMethodInInterface(s, stDecorated, info);
                //if (sym != null)
                //    return sym;
            }
            #endif
            //return null;
        }
        
        // Helper to lookup a method in an interface
        // Note, stDecorated is a function of stName & alParamTypes. We pass it anyways to avoid
        // recompute it at each level.
        // Return null if not found
        protected void SearchForOverloadInInterface(
            ISemanticResolver s,             
            string stName, 
            Type [] alParamTypes,
            OverloadedErrorInfo info
            )
        {            
            Debug.Assert(m_scope != null);
            
            SearchForOverloadedMethodInScope(m_scope, s, stName, alParamTypes, info);
                                        
            // Recursively check all inherited interfaces
            foreach(TypeEntry t in BaseInterfaces)
            {
                Debug.Assert(t.IsInterface);
                t.SearchForOverloadInInterface(s, stName, alParamTypes, info);
                //if (sym != null)
                //    return sym;
            }
            //return null;
        }        

        /*
        // Helper to throw an error
        protected void ThrowError_AmbiguousMethod(
            ISemanticResolver s,
            Identifier idName, 
            System.Type [] alParamTypes,
            string stOverloadList
            )
        {
            string stDecorated = GetDecoratedParams(idName.Text, alParamTypes);
            
            s.ThrowError(SemanticChecker.Code.cAmbiguousMethod,
                idName.Location,
                "The call to  '" + stDecorated + "'is ambiguous between:\n" + stOverloadList
                );
        }
        */


        // Lookup a method header in our class
        // return true if found, else false
        public bool HasMethodHeader(string stName)
        {
            //MethodHeaderEntry header = (MethodHeaderEntry) m_scope.LookupSymbolInThisScopeOnly(m_stMethodHeaderPrefix + stName);
            MethodHeaderEntry header = LookupMethodHeader(stName);
                
            if (header == null)
                return false;            
                
            return true;            
        }

        // Helper.
        // Return true if the parameters in alCall match a the parameters declared
        // in mDecl. Update the info.
        // Handles varargs.
        protected void AddIfParamsMatch(        
            System.Type [] alParamTypes,
            MethodExpEntry mDecl,
            OverloadedErrorInfo info
            )
        {                   
            // Callsite must have at least as many params as decl site
            // Callsite may have more if this is a varargs method
            // @todo - Callsite may have 1 less if this is avarargs method
            // and the callsite has 0 varargs.
            if (alParamTypes.Length < mDecl.ParamCount)
                return;
                    

            // Check that all params at callsite match with params at decl site                
            // This loop only checks the params that are at the decl site.
            // If this is a varargs call, then there may be params at the callsite
            // not yet checked.
            int i;
            for(i = 0; i < mDecl.ParamCount; i++)
            {
                System.Type tBase = mDecl.ParamCLRType(i);
                System.Type tDerived = alParamTypes[i];
                    
                Debug.Assert(tBase != null);
                string stBase = tBase.ToString();                    
                string stDerived = (tDerived == null) ? "null" : tDerived.ToString();
                    
                //if ((tBase != tDerived) && !tDerived.IsSubclassOf(tBase))
                if (!SymbolEngine.TypeEntry.IsAssignable(tDerived, tBase))
                    break;
                
            } // for each Param
                
            // If all parameters match, (and we've checked all of them) 
            // then we found an candidate method
            if ((i == mDecl.ParamCount) && (mDecl.ParamCount == alParamTypes.Length)) 
            {
                info.AddMatch(mDecl);                                                            
                // Have to continue searching to check for ambiguity
            }

            // If all params match except the last one, then we may have a
            // varargs
            if (i == mDecl.ParamCount - 1)
            {
                System.Type tLastParam = mDecl.ParamCLRType(i);
                if (tLastParam.IsArray)
                {
                    System.Type tBase = tLastParam.GetElementType();
                    Debug.Assert(tBase != null);

                    // Check remainder of params
                    int j;
                    for(j = i; j < alParamTypes.Length; j++)
                    {
                        System.Type tDerived = alParamTypes[j];
                    
                            
                        string stBase = tBase.ToString();                    
                        string stDerived = (tDerived == null) ? "null" : tDerived.ToString();
                    
                        //if ((tBase != tDerived) && !tDerived.IsSubclassOf(tBase))
                        if (!SymbolEngine.TypeEntry.IsAssignable(tDerived, tBase))
                            break;
                    }

                    if (j == alParamTypes.Length)
                    {
                        info.AddVarargMatch(mDecl);
                    }
                }
            } // end vararg
        } // end AddIfParamsMatch
            

        // Helper. Lookup the method in the specific scope
        // Add all matches to the info object        
        protected void SearchForOverloadedMethodInScope(
            Scope scope,
            ISemanticResolver s, 
            string stName, 
            System.Type [] alParamTypes,
            OverloadedErrorInfo info
            )
        {
            // Find header. If no no header, then the method isn't in this scope
            MethodHeaderEntry header = (MethodHeaderEntry) scope.LookupSymbolInThisScopeOnly(m_stMethodHeaderPrefix + stName);
            
            //MethodHeaderEntry header = LookupMethodHeader(stName);
            if (header == null)
                return;
                                                            
            // Now, traverse list of methods finding an appropriate match            
            foreach(MethodExpEntry m in header)
            {
                Debug.Assert(m.Name == stName);
                if (m.IsOverride)
                    continue;
                string stDecorated = TypeEntry.GetDecoratedParams(m); // debug helper
                AddIfParamsMatch(alParamTypes, m, info);
            }
                                    
        }

        // Helper:
        // Search for a method in the given class (and all of its super classes)
        // Even if we get the method from an interface, it still must be implemented in 
        // the super-class chain, so just searching that will be sufficient.
        // We don't have to search the entire interface tree.
        protected void LookupMethodInClass(
            ISemanticResolver s, 
            string stName, 
            Type [] alParamTypes,
            OverloadedErrorInfo info
            )
        {
            Debug.Assert(IsClass || IsStruct || IsRef);
            
            this.EnsureResolved(s);
            
            // Make life really easy be doing a broad search for the method header
            // If we don't find that, we won't find any overload / exact match            
            //MethodHeaderEntry header = (MethodHeaderEntry) m_scope.LookupSymbol(m_stMethodHeaderPrefix + stName);
            MethodHeaderEntry header = LookupMethodHeader(stName);
            if (header == null)
            {
                info.SetNoHeader();
                return ;
            }
            
            
            string stDecorated = GetDecoratedParams(stName, alParamTypes);
            MethodExpEntry sym = null;
            
            // Must resolve a member function (Specified by name & param types) to a symbol.
            // A function call may match multiple symbols. Ex:
            //      class Foo { int f(Base); int f(Derived); }
            // Now Foo.f(Derive) technically matches both versions of f; but there's not 
            // really any ambiguity. So....
            // 1) First try for an exact match.
            // 2) Else if we don't have an exact match, then we have to do a more 
            // thorough search based off matching parameter types.
            sym = (MethodExpEntry) s.LookupSymbol(m_scope, stDecorated, false);
                
            if (sym != null)
            {
                info.AddMatch(sym);
                return;
            }
            
            
            // @hack - strip all refs and try again...
            // @todo - need to properly implement the notion of 'best match'
            // because int& --> { int, object }, it should match with object.
            {
                System.Type [] alParamTypes2 = new System.Type[alParamTypes.Length];
                for(int i = 0; i < alParamTypes2.Length; i++)
                {
                    alParamTypes2[i] = alParamTypes[i];
                    if ((alParamTypes2[i] != null) && alParamTypes2[i].IsByRef)
                        alParamTypes2[i] = alParamTypes2[i].GetElementType();
                }
                string stDecorated2 = GetDecoratedParams(stName, alParamTypes2);
                sym = (MethodExpEntry) s.LookupSymbol(m_scope, stDecorated2, false);
                    
                if (sym != null)
                {
                    info.AddMatch(sym);
                    return;
                }
            }
            
            // No exact match found, so do a search:
            // Look at each scope in the super-class chain
            #if true            
            Scope oldScope = null;
            for(TypeEntry tSearch = this; tSearch != null; tSearch = tSearch.Super)
            {
                // @todo -major hack. T[] copies scope from System.Array. 
                // Traversing scopes was ok (since T[]'s --> System.Object, 2 links)
                // But traversing by class is not (since T[]'s --> S.Array --> S.Object, 3 links)
                // So any array member lookup happens in the same scope twice.
                if (oldScope == tSearch.MemberScope)
                    continue;
                
                oldScope = tSearch.MemberScope;
                
                // In each scope, search all of the methods for a match                
                SearchForOverloadedMethodInScope(tSearch.MemberScope, s, stName, alParamTypes, info);                
            }
            #else            
            for(Scope scope = m_scope; scope != null; scope = scope.InheritedParent)
            {
                // In each scope, search all of the methods for a match
                SearchForOverloadedMethodInScope(scope, s, stName, alParamTypes, info);                
            }
            #endif
             
        }

#endregion Internal Lookup helpers

#region Public Lookup functions
        // Use for inheritence /debug checks.
        // return null if not found.
        public MethodExpEntry LookupExactMethod(MethodExpEntry m)
        {
            string stDecorated = GetDecoratedParams(m);
            return (MethodExpEntry) m_scope.LookupSymbol(stDecorated);
        }

        // Given an undecorated method name, get the header. Used when we want to
        // search through a scope by method name and we don't know the parameters
        // This is like the inverse of LookupIndexer (in which case we know the
        // parameters, but don't know the name)
        // Return null if not found.
        public MethodHeaderEntry  LookupMethodHeader(string stMethodName)
        {
            MethodHeaderEntry header = (MethodHeaderEntry) m_scope.LookupSymbol(m_stMethodHeaderPrefix + stMethodName);
            return header;
        }

        // Given a method defined in an interface, look it up in the current class
        // We search for an exact match.
        // Return null if not found.
        public MethodExpEntry LookupInterfaceMethod(MethodExpEntry mInterface, bool fLookInSuperClass)
        {
            Debug.Assert(this.IsClass || this.IsStruct);
            Debug.Assert(mInterface.SymbolClass.IsInterface);
            
            string stDecorated = GetDecoratedParams(mInterface);
            
            if (!fLookInSuperClass)
            {
                SymEntry sym = m_scope.LookupSymbolInThisScopeOnly(stDecorated);
                if (sym == null)
                    return null;
                Debug.Assert(sym is MethodExpEntry);
                return sym as MethodExpEntry;                    
            }
            
            // Search all class & superclasses for an exact match
            //for(Scope scope = m_scope; scope != null; scope = scope.InheritedParent)
            for(TypeEntry tSearch = this; tSearch != null; tSearch = tSearch.Super)
            {
                Scope scope = tSearch.MemberScope;
                
                SymEntry sym = scope.LookupSymbolInThisScopeOnly(stDecorated);
                if (sym != null)
                {
                    Debug.Assert(sym is MethodExpEntry);                
                    return sym as MethodExpEntry;                    
                }
            }
            
            return null;
        }

        // Find an indexer. Throw an exception if not found.
        // Indexers are recognized by Parameter type & if they're a get/set
        public MethodExpEntry LookupIndexer(
            FileRange location,
            ISemanticResolver s,
            Type [] alParamTypes, // includes the value for set_X methods
            bool fIsLeft // true for set_X, false for get_X
            )
        {
            OverloadedErrorInfo info = new OverloadedErrorInfo();
            
            // Unfortunately, we don't have the name. We can only match on signature.
            for(Scope scope = m_scope; scope != null; scope = scope.m_LexicalParent)
            {
                // In each scope, search all of the methods for a match
                foreach(SymEntry sym in scope)
                {
                    MethodExpEntry m = sym as MethodExpEntry;
                    if (m != null)
                    {
                        // Indexers have special name bit set.
                        if (m.Info.IsSpecialName && !m.IsCtor) 
                        {
                            // Still have to discriminate between an indexer & a set property
                            string stName = m.Name;
                            if (fIsLeft && !stName.StartsWith("set_"))
                                continue;                                            
                            if (!fIsLeft && !stName.StartsWith("get_"))
                                continue;
                                                    
                            this.AddIfParamsMatch(alParamTypes, m, info);
                        }
                    }
                } // foreach symentry                
            } // foreach scope
            
            MethodExpEntry mIndexer = info.Symbol;            
            
            if (mIndexer == null)
                ThrowError(SymbolError.NoAcceptableIndexer(location, alParamTypes, fIsLeft));
            
            Debug.Assert(info.MatchCount == 1, "Ambiguous indexers");
            Debug.Assert(!mIndexer.IsCtor); // indexer should not be a ctor
            
            return mIndexer;
        }


        // Helper to find an overloaded operator.
        // Return null if not found.
        public MethodExpEntry LookupOverloadedOperator(
            ISemanticResolver s,
            string stName,
            Type [] alParamTypes
            )
        {
            
            // Overloads just look in classes
            OverloadedErrorInfo info = new OverloadedErrorInfo();
            
            
            
            if (!IsInterface)
            {
                LookupMethodInClass(s, stName, alParamTypes, info);                
            }
            
            return info.Symbol;
        }
        


        // General all-purpose method lookup. Good for Class/Interface, overloaded, inherited, etc
        // This will delegate out to the proper helpers
        //
        // Find a method with the given stName that "matches" the given parameter list.
        // "matches" means that all arguments at the callsite can be implicitly converted
        // to the parameter type at the declaration
        //
        // Throw exception if method not found.
        public MethodExpEntry LookupMethod(
            ISemanticResolver s,             
            Identifier idName,
            Type [] alParamTypes,
            out bool fIsVarArg
            )
        {
            // Before we lookup anything, make sure we've resolved our scope
            this.EnsureResolved(s);
        
            fIsVarArg = false;
            
            string stName = idName.Text;
            FileRange location = idName.Location;
            //FileRange location = new FileRange();
            
            Debug.Assert(m_scope != null);
            Debug.Assert(alParamTypes != null);
            
            string stDecorated = GetDecoratedParams(stName, alParamTypes);
            
            OverloadedErrorInfo info = new OverloadedErrorInfo();
            
            MethodExpEntry sym = null;
            
            // If we're not an interface, then the method must exist in one of our base classes.
            // A normal lookup on scope searches base classes, so we're fine.
            if (!IsInterface)
            {
                LookupMethodInClass(s, stName, alParamTypes, info);
                
            } 
            else 
            {
                // Since classes just have a single base class, we have a simple linear search.
                // But since we can implement/inherit many interfaces, we have to search through
                // a tree here.
            
                // Do a quick broad seach for the method header. If that's not found, then we'll
                // never find the actual method / overload
                bool fHeader = LookForMethodHeaderInInterface(stName);
                if (!fHeader)
                    info.SetNoHeader();
                else 
                {                
                    // See if we have a direct match. Must look through entire tree for direct
                    // match first before searching for overload, else a shallow overload will
                    // hide an exact-match that's deeper in the tree.
                    LookupMethodInInterface(s, stDecorated, info);
                    if (info.MatchCount == 0)
                    {
                        // Only do search if we don't have a direct match
                        SearchForOverloadInInterface(s, stName, alParamTypes, info);
                    }
                }
                
                // @todo ....
                // If we still haven't found it, do a last effort check of System.Object.
                // Since although we have an interface, we know there's really a class
                // behind it, and all classes implement Object.
            }
            
            // Undefined
            if (info.MatchCount == 0)
            {
                // No header found. So this is really undefined
                if (info.NoHeader)
                {
                    ThrowError(SymbolError.MethodNotDefined(location, FullName + "." + stName));
                    /*
                    s.ThrowError(SemanticChecker.Code.cMethodNotDefined, location,
                        "The method '" + FullName + "." + stName + "' is not defined.");
                    */
                } 
                
                // There's a header, so we have some definitions of this method.
                // But no acceptable overloads
                else                 
                {
                    ThrowError(SymbolError.NoAcceptableOverload(location, FullName + "." + stDecorated));
                    /*
                    s.ThrowError(SemanticChecker.Code.cNoAcceptableOverload, location,
                        "No acceptable overload for '" + FullName + "." + stDecorated + "' exists");                
                    */                       
                }                
            } 
            
                // Ambiguous. More than one overload matches.
            else if (info.MatchCount > 1)
            {
                string stList = info.GetOverloadList();
                //ThrowError_AmbiguousMethod(s, idName, alParamTypes, stList);            
                ThrowError(SymbolError.AmbiguousMethod(idName, alParamTypes, stList));
            }
            
            // We should have something
            Debug.Assert(info.Symbol != null);
            
            fIsVarArg = info.IsVarArgMatch;
            sym = info.Symbol;
                        
            return sym;            
        }
#endregion        
                
        // Prefix methodheaders to make them an illegal identifier so that a lookup
        // fo something else (like a ctor or method w/ 0 parameters) won't 
        // accidently stumble across the header.
        readonly static string m_stMethodHeaderPrefix = "<header>";
        
        // Add a method (decorated) to the scope        
        public void AddMethodToScope(MethodExpEntry sym)
        {                       
            Debug.Assert(sym.IsCtor || sym.RetType != null);
            string stDebugClass = sym.SymbolClass.FullName;
            
            // First, see if there's no header             
            MethodHeaderEntry header = (MethodHeaderEntry) m_scope.LookupSymbolInThisScopeOnly(m_stMethodHeaderPrefix + sym.Name);
            if (header == null)
            {
                header = new MethodHeaderEntry(m_stMethodHeaderPrefix + sym.Name);
                m_scope.AddSymbol(header);
            }
            
            // Get parameters
            #if false            
            // @todo - don't strip the references
            Type [] alParamTypes = sym.ParamTypes(true);            
            Debug.Assert(alParamTypes != null);                        
            string stDecorated = GetDecoratedParams(sym.Name, alParamTypes);
            #else
            
            string stDecorated = GetDecoratedParams(sym);
            
            #endif
            
            m_scope.AddAliasSymbol(stDecorated, sym);
            
            
            Log.WriteLine(Log.LF.Resolve, "Adding method '{0}' to scope '{1}'", stDecorated, m_scope.m_szDebugName);
            
            // Chain in
            header.AddMethodNode(sym);
        }
        
#endregion
        // Ensure that we have a hollow scope created/
        internal void EnsureScopeCreated()
        {
            if (m_scope != null)
                return;
                
            string stName = (m_genre.ToString()) + m_typeCLR.ToString();
            m_scope = new Scope(
                stName, // name
                this,   // ILookupController
                null    // lexical parent
            );  
        }
        
        // Maintain flag to protect against infinite cycles in EnsureResolved.
        //bool m_fResolving;
        
        // Ensure that we have a scope. All user defined classes will already have a scope,
        // but imported classes don't have a scope until they're first used (for memory reasons,
        // since we import all classes in an assembly but only use a few of them; we don't
        // want to allocate a ton of scopes & methods for classes we never use)    
        public void EnsureResolved(ISemanticResolver s)
        {
            // If we don't have a scope, then we're either:
            // 1. An imported type (in which case clrType is set)
            // 2. a user type refered as a base class during the ResolveType phase
            //    In this case, we don't have our clrType set yet. But that's ok.
            //    If we're just being refered as a base class in a decl, no one will use us.
            
            // If we do have a scope then we're:
            // 1. An imported type that has nested types
            // 2. A user type
            // 3. An already resolved user or imported type.
                        
            if ((m_scope == null) || (!m_scope.IsLocked))
            {                
                // Must let imported types in for refs, arrays, etc to set their element type
                                
                #if false
                // break on resolving a particular class
                    if (this.FullName == "System.MulticastDelegate")
                    {
                        int x =2;
                    }
                #endif
                
                // If our clr type isn't set yet, then we can't resolve. Someone will call
                // us later though.
                if (m_typeCLR == null)
                    return;

                Debug.Assert(m_typeCLR != null);
                
                // If we have a ref, then just populate our element type
                // Note that a ref's member scope should be the same as it's Elem type's
                // member scope (since anything on type T can be called on T&).
                if (this.IsRef)
                {
                    TypeEntry elem = this.AsRefType.ElemType;
                    elem.EnsureResolved(s);
                    m_scope = elem.m_scope;
                    return;
                }
                
                // If we have element types, make sure those are resolved too
                if (IsArray)
                {   
                    ArrayTypeEntry a = this.AsArrayType;
                    TypeEntry elem = a.ElemType;
                    elem.EnsureResolved(s);
                    
                    // Don't return here since we still have to populate
                    // our member scope w/ System.Array stuff
                }                
                                
                // Make sure our base class & inherited interfaces are resolved
                if (this.Super != null)
                    Super.EnsureResolved(s);
                    
                foreach(TypeEntry tBase in this.BaseInterfaces)
                    tBase.EnsureResolved(s);
                
                // By now, we should have resolved all of our depedent classes.
                
                // Can't call this on a user type because we can't query ourselves.
                // But that's ok, user types already have a populated scope.
                if (!this.IsImported)
                    return;
                    
                Debug.Assert(!(m_typeCLR is System.Reflection.Emit.TypeBuilder), "Can't call EnsureResolved on user type:" + FullName);
                
                
                // Create scope if we haven't already.
                if (m_scope == null)
                {                    
                    EnsureScopeCreated();                    
                } else {
                    // Only way scope can already exists is if there are nested
                    // classes.
                    // Fails on TypeBuilders, so ignore error check for now.
                    #if false                    
                    int cNestedTypes = m_typeCLR.GetNestedTypes(
                        System.Reflection.BindingFlags.Public | 
                        System.Reflection.BindingFlags.NonPublic).Length;
                        
                    Debug.Assert(cNestedTypes != 0);
                    #endif
                }
                
                //Scope sSuper = (this.Super == null) ? null : this.Super.MemberScope;                
                //m_scope.SetSuperScope(sSuper);
                
                // Use reflection to populate this scope
                
                // @dogfood - allow 'const'
                /*const*/ System.Reflection.BindingFlags flags = 
                          System.Reflection.BindingFlags.DeclaredOnly |
                          System.Reflection.BindingFlags.Public |                          
                          System.Reflection.BindingFlags.NonPublic |
                          System.Reflection.BindingFlags.Static |
                          System.Reflection.BindingFlags.Instance;
                    
                // Add each method
                System.Reflection.MethodInfo [] alMethods = m_typeCLR.GetMethods(flags);
                                
                foreach (System.Reflection.MethodInfo mInfo in alMethods)
                {                                        
                    string stName = mInfo.Name;
                    if (mInfo.IsPrivate || mInfo.IsAssembly)
                        continue;
#if false
                    // ignore all generic methods. Only in v2.0
                    if (mInfo.ContainsGenericParameters || mInfo.HasGenericArguments || mInfo.IsGenericMethodDefinition)
                        continue;
#endif

                    TypeEntry tRetType = s.ResolveCLRTypeToBlueType(mInfo.ReturnType);          
                                              
                    //MethodExpEntry symMethod = new MethodExpEntry(stName, mInfo, this, tRetType);
                    MethodExpEntry symMethod = new MethodExpEntry(s, mInfo);
                                        
                    AddMethodToScope(symMethod);
                }
                
                // Properties
                foreach(System.Reflection.PropertyInfo pInfo in m_typeCLR.GetProperties())
                {
                    string stName = pInfo.Name;
                    if (pInfo.GetAccessors()[0].IsPrivate || pInfo.GetAccessors()[0].IsAssembly)
                        continue;
                    
                    PropertyExpEntry symProp = new PropertyExpEntry(s, pInfo);
                    
                    this.m_scope.AddSymbol(symProp);
                }
                
                // Ctors
                System.Reflection.ConstructorInfo [] alCtors = m_typeCLR.GetConstructors(flags);
                foreach(System.Reflection.ConstructorInfo cInfo in alCtors)
                {                       
                    if (cInfo.IsStatic)
                        continue; // don't need to add static ctors to scope.
                    if (cInfo.IsPrivate || cInfo.IsAssembly)
                        continue;
                        
                    MethodExpEntry symCtor = new MethodExpEntry(s, cInfo);
                    AddMethodToScope(symCtor);
                }
                
                // Fields
                System.Reflection.FieldInfo [] alFields = m_typeCLR.GetFields(flags);
                foreach(System.Reflection.FieldInfo fInfo in alFields)
                {
                    FieldExpEntry symField = null;
                    
                    if (fInfo.IsPrivate || fInfo.IsAssembly)
                        continue;
                        
                    // @todo - combine LiteralField w/ Field?
                    if (fInfo.IsLiteral)                    
                        symField = new LiteralFieldExpEntry(s, fInfo);
                    else                    
                        symField = new FieldExpEntry(s, fInfo);
                      
                    this.m_scope.AddSymbol(symField);                
                }
                
                // Events
                // We may have a private delegate field and an event w/ the same name. Ugg
                // But privates aren't imported! Yay.
                System.Reflection.EventInfo [] alEvents = m_typeCLR.GetEvents(flags);
                foreach(System.Reflection.EventInfo eInfo in alEvents)
                {
                    if (eInfo.GetAddMethod().IsPrivate || eInfo.GetAddMethod().IsAssembly)
                        continue;
                    
                    EventExpEntry e = new EventExpEntry(eInfo, s);                  
                    this.m_scope.AddSymbol(e);
                }
                
                // Nested classes already stubbed when we imported
                
                                
                
                // Lock the scope. If we had to resolve it, then we shouldn't be adding
                // anything new.
                this.m_scope.LockScope();
            } 
            else 
            {
                // Scope already exists, there's nothing to do.            
            }
        
        } // end EnsureResolved
        
#endregion     
    } // end class TypeEntry
    
    //-----------------------------------------------------------------------------
    // For arrays
    //-----------------------------------------------------------------------------    
    public class ArrayTypeEntry : TypeEntry
    {
        // @todo - set real interfaces
        // For resolving CLR arrays (that don't have an AST node)
        // so clrType must be of an array
        public ArrayTypeEntry(System.Type clrType, ISemanticResolver s) :         
            this(s)
        {
            Debug.Assert(clrType!= null);
            Debug.Assert(s != null);
            Debug.Assert(clrType.IsArray);
            
            //Debug.Assert(rank == 1, "@todo - allow ranks besides 1");
                                    
            System.Type clrElem = clrType.GetElementType();
            
            m_typeElem = s.ResolveCLRTypeToBlueType(clrElem);                                                            
            m_cDimension = clrType.GetArrayRank();
            
            // All arrays must be of type System.Array     
            FinishArrayInit(s);
            VerifyClrType();
        }
        
        // For User declared arrays
        public ArrayTypeEntry(AST.ArrayTypeSig sig, ISemanticResolver s) :             
            this(s)
        {
            Debug.Assert(sig!= null);
            //Debug.Assert(rank == 1, "@todo - allow ranks besides 1");
            if (sig.Dimension != 1)
                ThrowError(SymbolError.NotYetImpl(sig.Location, "multi-dimensional arrays"));
                //s.ThrowError_NotYetImpl(sig.Location, "multi-dimensional arrays");
                                    
            m_typeElem = sig.ElemType;
            //m_typeElem = sig.ElemTypeNode.TypeRec;
            m_cDimension = sig.Dimension;
        
            FinishArrayInit(s);
            VerifyClrType();
        }
        
        // Private common constructor to initialize the base TypeEntry object to an array type
        // All other ctor should delegate to this one.
        private ArrayTypeEntry(ISemanticResolver s) : 
            //base(null, null, null, null, typeof(System.Array)) // name, fullname, super, intefaces[], clrtype            
            base(s, typeof(System.Array))            
        {
            // Must still call FinishArrayInit() after we set our ElemType & Dimension.        
            // We derive from System.Array, but our real CLR type is a
            // System.Type that we can only get from ModuleBuilder.GetType
        }
        
        protected void FinishArrayInit(ISemanticResolver s)
        {   
            this.m_stFullName = this.GetFullName();
            this.m_strName = this.GetShortName();
                                    
            this.m_typeCLR = s.GetArrayType(this);
            this.m_fIsInit = true;
        }
     
#region Checks   
        protected void VerifyClrType()
        {        
            Debug.Assert(this.IsInit);
            
            // Relation to system.array is pretty bizare.
            Debug.Assert(m_typeCLR.IsArray);
            Debug.Assert(m_typeCLR.BaseType == typeof(System.Array));                        
            Debug.Assert(m_typeCLR.GetArrayRank() == this.Dimension);
            
            if (Super.CLRType != null)
            {
                Debug.Assert(Super.CLRType == typeof(System.Array));
            }
        }
     
        // Debugging support
        public override void DebugCheck(ISemanticResolver s)
        {
            if (m_fDebugChecked)
                return;
                
            m_fDebugChecked = true; // set flag before the recursion
            
            // Verify clr type again to make sure that no one changed it on us.
            VerifyClrType();
                        
            Debug.Assert(m_typeElem != null);
            m_typeElem.DebugCheck(s);
            
            base.DebugCheck(s);            
        }
        
        // Get a string representation
        public override string ToString()
        {
            return GetFullName();
        }
        // Get short name
        public string GetShortName()
        {
            return GetBaseType().Name + GetRankSpecifierString();
        }
        
        // Get a fully-qualified string repsentation
        public string GetFullName()
        {            
            return GetBaseType().FullName + GetRankSpecifierString();
        }
        
        // Get the non-array portion of our type
        protected TypeEntry GetBaseType()
        {
            TypeEntry t = this.ElemType;
            while(t is ArrayTypeEntry)
                t = t.AsArrayType.ElemType;
        
            return t;         
        }
        
        // Get a string representing the rank specifiers
        protected string GetRankSpecifierString()
        {             
            string stBase = "";   
            TypeEntry t = this;
            while(t is ArrayTypeEntry)
            {
                stBase = stBase + "[";
                for(int i = 0; i < t.AsArrayType.Dimension - 1; i++)
                    stBase += ",";
                stBase += "]";
            
                t = t.AsArrayType.ElemType;
            }
        
            return stBase;
        }
                
                
#endregion        

#region Properties & Data
        // type entry referring to the elements that we have
        protected TypeEntry m_typeElem;
        public TypeEntry ElemType
        {
            get { return m_typeElem; }
        }
        
        // Rank of the array
        protected int m_cDimension;
        public int Dimension
        {
            get { return m_cDimension; }
        }
#endregion        
    
    }

    //-----------------------------------------------------------------------------
    // Reference types
    //-----------------------------------------------------------------------------
    public class RefTypeEntry : TypeEntry
    {
        // RefType are pretty opaque. Their only use is getting at the enclosed
        // type (which can be any type besides a RefType).
        public RefTypeEntry(TypeEntry t, ISemanticResolver s) : base(
            "",     //string stFullName,            
            null,   //AST.ClassDecl nodeDecl,             
            TypeEntry.Genre.cReference, //Genre genre,
            null   // Scope scopeParent
           
        )
        {
            Debug.Assert(t != null);
            m_typeElem = t;    
        
            m_typeCLR = s.GetRefToType(t.CLRType);
        }

#region Checks
        public override void DebugCheck(ISemanticResolver s)
        {
            m_typeElem.DebugCheck(s);
        }
        // Get a string representation
        public override string ToString()
        {
            return m_typeElem.ToString() + "&";
        }
#endregion
    
#region Properties & Data
        TypeEntry m_typeElem;
        public TypeEntry ElemType
        {
            get { return m_typeElem; }
        }
#endregion

    } // end class

    //-----------------------------------------------------------------------------
    // Enums - act just like types, so we can safely treat it as a type   
    //-----------------------------------------------------------------------------
    public class EnumTypeEntry : TypeEntry
    {
        public EnumTypeEntry(
            string stName, 
            string stFullName, 
            ISemanticResolver s, 
            Modifiers mods, 
            AST.EnumDecl node,
            Scope scopeParent
        )
            : base(s, typeof(System.Enum))
        {
            m_strName = stName;
            m_stFullName = stFullName;
            
            // Enums need their own scope (since they add fields), but have a common
            // super scope, ScopeOf(System.Enum).
            m_scope = new Scope("enum_" + m_strName, this, scopeParent);
            //m_scope = new Scope("enum_" + m_strName, node, scopeParent);
            //m_scope.SetSuperScope(this.m_super.MemberScope); // chain to System.Enum
            
            this.m_mods = mods;
            this.m_nodeEnum = node;

            // Enum's can be built in one phase init, so just go ahead and finish
            this.FinishInit();            
        }

        AST.EnumDecl m_nodeEnum;
        new public AST.EnumDecl Node
        {
            get { return m_nodeEnum; }
        }

        new public void SetCLRType(ICLRtypeProvider provider)
        {
            m_typeCLR = provider.CreateCLREnumType(this);
        }
        
        new protected void FinishInit()
        {
            // Set containing class, based off lexical scope
            AST.ClassDecl nodeParent = m_scope.LexicalParent.Node as AST.ClassDecl;
            if (nodeParent != null)
            {
                m_containingClass = nodeParent.Symbol;
                Debug.Assert(m_containingClass != null);
            }
            
            m_fIsInit = true;
        }

    }
    
#endregion

} // end namespace SymbolEngine