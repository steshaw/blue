//-----------------------------------------------------------------------------
// Symbol Information
// Contains definitions of all Symbol Table entries.
// All classes for a symbol table entry are appended with 'Entry'
// Class organization:
// SymEntry
// + NamespaceEntry
//      + ImportedNamespaceEntry - namespace imported from external assembly
//      + UserNamespaceEntry -namespace defined in our sources
// + TypeEntry - raw type (both Classes & primitives)
// + ExpEntry - can be used in an expression, has a TypeEntry
//      + MethodExpEntry - for a specific method
//      + VarExpEntry
//          + LocalVarExpEntry
//          + ParamVarExpEntry         
//      + FieldExpEntry
//      + PropertyExpEntry
//
// + MethodHeaderEntry - used to index all methods of a given name
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Xml;

using Blue.Public;
using Log = Blue.Log;
using Modifiers = AST.Modifiers;

namespace SymbolEngine
{
#region SymEntry base class for all Symbols
    //-----------------------------------------------------------------------------
    // Base class for symbol table entries
    //-----------------------------------------------------------------------------
    
    /// <summary>
    /// The base class for all symbols.
    /// </summary>
    /// <remarks>
    /// </remarks>
    public abstract class SymEntry
    {
        public string m_strName; // name of this symbol
        
        public string Name
        {
            get { return m_strName; }
        }
        
        // Write out this Entry's name, attributes,     
        public abstract void Dump(XmlWriter o, bool fRecursive);
        
        
        // Debugging support - Validate each symbol entry
        // Allow recursive flag so that we don't get stuck in an infinite loop
        public virtual void DebugCheck(ISemanticResolver s) { }
        
        
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

#region Class for LabelEntry symbol (for gotos)
    //-----------------------------------------------------------------------------
    // Labels (for gotos)
    //-----------------------------------------------------------------------------
    public class LabelEntry : SymEntry
    {        
        // Used by a labelstatement
        public LabelEntry(string stName, AST.LabelStatement node)
        {
            Debug.Assert(node != null);
            Debug.Assert(stName != null);

            m_strName = stName;
            m_node = node;
        }


        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("LabelEntry");
            o.WriteAttributeString("name", m_strName);
            o.WriteEndElement();
        }

        AST.LabelStatement m_node;
        public AST.LabelStatement Node
        {
            get { return m_node; }
        }

        // Opaque object used by codegen
        object m_objCookie;
        public object CodegenCookie
        {
            get { return m_objCookie; }
            set { m_objCookie = value; }
        }        
    }
#endregion

#region Namespace Symbols
    //-----------------------------------------------------------------------------
    // Namespace entries
    //-----------------------------------------------------------------------------
    public abstract class NamespaceEntry : SymEntry
    {  
        // Scope for children
        protected Scope m_scope;
        public Scope ChildScope
        {
            get { return m_scope; }
        }

        protected string m_stFullName;
        public string FullName
        {   
            get { return m_stFullName; }
        }
    }
    
    //-----------------------------------------------------------------------------
    // Represent an imported namespace
    //-----------------------------------------------------------------------------
    public class ImportedNamespaceEntry : NamespaceEntry
    {
        // assembly we're imported from?
                
        public ImportedNamespaceEntry(string stNamespace, string stFullName)
        {
            m_strName = stNamespace;
            m_stFullName = stFullName;
            m_scope = new Scope("imported_namespace:" + stFullName, null, null);
        }
    
        // Write out this Entry's name, attributes, (and if fRecursive, then nested scope too)
        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("ImportedNamespace");
            o.WriteAttributeString("name", this.m_strName);
            if (fRecursive && (m_scope != null)) 
            {
                m_scope.Dump(o, true);
            }
            o.WriteEndElement();        
        }
        
        public override string ToString()
        {
            return "ImportedNamespaceSym:" + m_strName;        
        }        
    }
    
    //-----------------------------------------------------------------------------
    // represent a user namespace
    //-----------------------------------------------------------------------------
    public class UserNamespaceEntry : NamespaceEntry
    {
#region Construction    
        // Global namespace has "" name.
        public UserNamespaceEntry(AST.NamespaceDecl node, string stFullName)
        {
            Debug.Assert(node != null);
            Debug.Assert(stFullName != null);
            
            m_strName = node.Name;
            m_stFullName = stFullName;

            // Create the real scope for the namespace. This scope just has the symbols,
            // but it can't be attached to a particular node or lexical parent
            // since it's shared by all blocks for that namespace.
            if (IsGlobal)
            {
                // Add filename as a debugging hint
                string stFilename = node.Location.Filename;
                m_scope = new Scope("global " + stFilename, null, null);
            } 
            else 
            {
                m_scope = new Scope("user_namespace:" + node.Name, null, null);
            }
            //m_node = node;
            m_node = null;
        }
#endregion        

#region Properties & Data
        public bool IsGlobal
        {
            get { return m_strName == ""; }
        }
        
        AST.NamespaceDecl m_node;
        public AST.NamespaceDecl Node
        {
            get { return m_node; }
        }
        /*
        public void SetCurrentNode(AST.NamespaceDecl node)
        {
            Debug.Assert((node == null) ^ (m_node == null));        
            m_node = node;
        }
        */
#endregion

#region Checks        
        // Write out this Entry's name, attributes, (and if fRecursive, then nested scope too)
        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("UserNamespace");
            o.WriteAttributeString("name", this.Name);
            o.WriteAttributeString("fullname", this.FullName);
            
            if (fRecursive)             
                m_scope.Dump(o, true);
                
            o.WriteEndElement();        
        }        
        
        public override string ToString()
        {
            return "UserNamespaceSym:" + m_strName;        
        }  
#endregion              
    }

#endregion


#region Classes for ExpEntry symbols (Locals,Params,Methods,Properties)   
    //-----------------------------------------------------------------------------
    // Symbol table entry that can evaluate to a type
    //-----------------------------------------------------------------------------
    public abstract class ExpEntry : SymEntry
    {
        public TypeEntry m_type;
        
        public System.Type CLRType
        {
            get { return m_type.CLRType; }
        }
    }
    
#region Events
    //-----------------------------------------------------------------------------
    // Events
    //-----------------------------------------------------------------------------
    public class EventExpEntry : ExpEntry
    {
        // User defined events
        public EventExpEntry(TypeEntry tClass, AST.EventDecl e)
        {
            Debug.Assert(tClass != null);
            Debug.Assert(e != null);
            
            this.m_strName          = e.Name.Text;
            this.m_type             = e.EventType.BlueType;
            this.m_tClassDefined    = tClass;
            this.m_node             = e;
            this.m_mods             = e.Mods;
        }
        
        
        // Imported events
        public EventExpEntry(
            System.Reflection.EventInfo eInfo,
            ISemanticResolver s
        )
        {   
            Debug.Assert(eInfo != null);
            Debug.Assert(s != null);
            
            this.m_strName = eInfo.Name;
            this.m_tClassDefined = s.ResolveCLRTypeToBlueType(eInfo.DeclaringType);
            this.m_type = s.ResolveCLRTypeToBlueType(eInfo.EventHandlerType);
            
            this.m_node = null;
                     
            System.Reflection.MethodInfo mAdd = eInfo.GetAddMethod();
            System.Reflection.MethodInfo mRemove = eInfo.GetRemoveMethod();
                                        
            SetAddMethod(new MethodExpEntry(s, mAdd));
            SetRemoveMethod(new MethodExpEntry(s, mRemove));
            
            this.m_mods = new Modifiers(mAdd);
        }
        
    
#region Checks
        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("EventExpEntry");
            o.WriteEndElement();
        }
        
        public override string ToString()
        {
            return "EventExpEntry:" + m_strName + " of " + m_type.ToString();
        }
#endregion
    
#region Properties & Data   
        AST.EventDecl m_node;
        public AST.EventDecl Node
        {
            get { return m_node; }
        }        
 
        TypeEntry m_tClassDefined;
        public TypeEntry SymbolClass
        {
            get { return m_tClassDefined; }
        }
        
        public TypeEntry EventType
        {
            get { return this.m_type; }
        }
        
        MethodExpEntry m_methodAdd;
        public MethodExpEntry AddMethod
        {
            get { return m_methodAdd; }
        }
                
        MethodExpEntry m_methodRemove;
        public MethodExpEntry RemoveMethod
        {
            get { return m_methodRemove; }
        }  
        
        FieldExpEntry m_field;
        public FieldExpEntry Field
        {
            get { return m_field; }
        }        
        
        Modifiers m_mods;
        public Modifiers Mods
        {
            get  { return m_mods; }
        }  
        
#endregion
        public void SetAddMethod(MethodExpEntry m)
        {
            Debug.Assert(m_methodAdd == null);
            m_methodAdd = m;
        }
        
        public void SetRemoveMethod(MethodExpEntry m)
        {
            Debug.Assert(m_methodRemove == null);
            m_methodRemove = m;
        }
        
        // An event with a default handler is backed by a field.        
        // This does not have to be set.
        internal void SetDefaultField(FieldExpEntry f)
        {
            m_field = f;
        }
    
        public void SetInfo(ICLRtypeProvider provider)
        {
            //Debug.Assert(m_info == null);
                        
            //m_info = 
            provider.CreateCLREvent(this);            
        }
    } // end class Events
#endregion Events    
    
    
    
#region Property
    //-----------------------------------------------------------------------------
    // Properties
    //-----------------------------------------------------------------------------
    public class PropertyExpEntry : ExpEntry
    {
#region Ctor        
        // Ctor for user-declared properties
        // We'll rip our data from the node
        public PropertyExpEntry (            
            TypeEntry tDefiningClassType,
            AST.PropertyDecl nodeDecl,
            AST.MethodDecl nodeGet,
            AST.MethodDecl nodeSet
            )
        {
            Debug.Assert(nodeDecl != null);
            
            m_strName = nodeDecl.Name.Text;
            m_tClassDefined = tDefiningClassType;
            
            m_node = nodeDecl;
            
            m_type = nodeDecl.BlueType;
            
            m_symbolGet = (nodeGet == null) ? null : nodeGet.Symbol;
            m_symbolSet = (nodeSet == null) ? null : nodeSet.Symbol;
            
            m_mods = nodeDecl.Mods;            
        }
        
        // Ctor for imported properties
        public PropertyExpEntry(        
            ISemanticResolver s,
            System.Reflection.PropertyInfo info
            )
        {
            m_info = info;
            
            m_strName = m_info.Name;
                       
            // Class that we're defined in?
            System.Type tClrClass = info.DeclaringType;
            m_tClassDefined = s.ResolveCLRTypeToBlueType(tClrClass);
            
            // Symbol type
            this.m_type = s.ResolveCLRTypeToBlueType(info.PropertyType);
            
            // Spoof accessors
            if (info.CanRead) // Has Get
            {
                System.Reflection.MethodInfo mGet = info.GetGetMethod();
                m_symbolGet = new MethodExpEntry(s, mGet);
            }
            
            if (info.CanWrite) // Has Set
            {
                System.Reflection.MethodInfo mSet = info.GetSetMethod();
                m_symbolSet = new MethodExpEntry(s, mSet);
            }
            
            // Get modifiers
            System.Reflection.MethodInfo [] m = info.GetAccessors();
            
            m_mods = new Modifiers(m[0]);
            /*
            m_mods = new Modifiers();
            if (m[0].IsStatic) m_mods.SetStatic(); 
            if (m[0].IsAbstract) m_mods.SetAbstract(); 
            if (m[0].IsVirtual) m_mods.SetVirtual();
            */
            
        }
#endregion

#region Data                
        TypeEntry m_tClassDefined;
        
        MethodExpEntry m_symbolGet;
        MethodExpEntry m_symbolSet;
        
        System.Reflection.PropertyInfo m_info;
        AST.PropertyDecl m_node;
    
        Modifiers m_mods;
#endregion

#region Properties
        public AST.PropertyDecl Node
        {
            get { return m_node; }            
        }
        
        public TypeEntry PropertyType
        {
            get { return m_type; }        
        }
        
        public TypeEntry SymbolClass
        {
            get { return m_tClassDefined; }
        }
        
        public MethodExpEntry SymbolGet
        {
            get { return m_symbolGet; }
        }
        
        public MethodExpEntry SymbolSet
        {
            get { return m_symbolSet; }
        }
        
        public System.Reflection.PropertyInfo Info
        {
            get { return m_info; }
        }
        
        public bool IsStatic
        {
            get { return Mods.IsStatic; }
        }
        
        public bool IsVirtual
        {
            get { return Mods.IsVirtual; }
        }
        
        public bool IsAbstract
        {
            get { return Mods.IsAbstract; }
        }
        
        public Modifiers Mods
        {
            get { return this.m_mods; }
        }
#endregion        

#region Resolution
        public void SetInfo(ICLRtypeProvider provider)
        {
            Debug.Assert(m_info == null);
            
            // This will create a clr info for this property
            // as well as the accessors                    
            m_info = provider.CreateCLRProperty(this);
        }

#endregion
    
#region Checks        
        public override void DebugCheck(ISemanticResolver s)
        {
            Debug.Assert(Name != null);            
            Debug.Assert(Info != null);
            Debug.Assert(SymbolClass != null);
            
            if (Node != null)
            {
                Debug.Assert(Node.Symbol == this);
            }
            
            //SymbolClass.DebugCheck(s);
            //PropertyType.DebugCheck(s);
            
        }


        // Write out this Entry's name, attributes, (and if fRecursive, then nested scope too)
        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("PropertyExpEntry");
            o.WriteAttributeString("name", m_strName);
            o.WriteAttributeString("type", (m_type == null) ? "null" : m_type.ToString());
            o.WriteAttributeString("class", (m_tClassDefined == null) ? "null" : m_tClassDefined.ToString());
            
            o.WriteAttributeString("HasGet", (m_symbolGet != null) ? "true" : "false" );
            o.WriteAttributeString("HasSet", (m_symbolSet != null) ? "true" : "false" );           
            
            o.WriteAttributeString("Imported", (Node == null) ? "true" : "false");
                                                
            o.WriteEndElement();        
        }
#endregion       
    }

#endregion

#region Literal Fields
    //-----------------------------------------------------------------------------
    // Literal field
    //-----------------------------------------------------------------------------
    public class LiteralFieldExpEntry : FieldExpEntry
    {
    // Construction is the same as for Fields,
#region Construction
        public LiteralFieldExpEntry(
            string stName,                  // name of this field
            TypeEntry tType,                // type of this field
            TypeEntry tDefiningClassType,   // type of class we're defined in
            AST.FieldDecl nodeDecl          // AST node that we're defined at
            )
        : base(stName, tType, tDefiningClassType, nodeDecl)
        {
        }
        
        
        // For imported
        public LiteralFieldExpEntry(
            ISemanticResolver s,
            System.Reflection.FieldInfo fInfo        
        ) : base(s, fInfo)
        {
            Debug.Assert(fInfo.IsLiteral && fInfo.IsStatic);
            
            object o = fInfo.GetValue(null);
            
            // For enum fields, their literal value is an Enum, not an int.
            
                                
            Data = o;
            Type t = m_value.GetType();
            string st = m_value.ToString();
        }
        
#endregion
    // but we're associated with an object...
#region Properties        
        object m_value;
        public object Data
        {
            get { return m_value; }
            set { m_value = value; }
        }
#endregion

        
        public override void SetInfo(ICLRtypeProvider provider)
        {
            Debug.Assert(m_info == null);
            m_info = provider.CreateCLRLiteralField(this);
        }

    }
#endregion

#region Fields
    //-----------------------------------------------------------------------------
    // Fields
    //-----------------------------------------------------------------------------    
    public class FieldExpEntry : ExpEntry
    {
#region Construction
        public FieldExpEntry(
            string stName,                  // name of this field
            TypeEntry tType,                // type of this field
            TypeEntry tDefiningClassType,   // type of class we're defined in
            AST.FieldDecl nodeDecl          // AST node that we're defined at
            )
        {
            m_strName = stName;
            m_type = tType;
            m_nodeDecl = nodeDecl;
            m_tClassDefined = tDefiningClassType;
            
            Debug.Assert(m_type != null);
            Debug.Assert(m_nodeDecl != null); // @todo - allow this for imports?
            Debug.Assert(m_tClassDefined != null);
        }
        
        // Imported field        
        public FieldExpEntry(
            ISemanticResolver s,
            System.Reflection.FieldInfo fInfo            
            )
        {
            m_strName = fInfo.Name;
            m_type = s.ResolveCLRTypeToBlueType(fInfo.FieldType);
            m_nodeDecl = null;
            m_tClassDefined = s.ResolveCLRTypeToBlueType(fInfo.DeclaringType);
            
            m_info = fInfo;
        }
#endregion

#region Properties & Data
        
        protected System.Reflection.FieldInfo m_info;
        public System.Reflection.FieldInfo Info
        {
            get { return m_info; }
        }

        // Valid only after we've set the clr type
        public bool IsStatic 
        {
            get { return m_info.IsStatic; }
        }

        public virtual void SetInfo(ICLRtypeProvider provider)
        {
            Debug.Assert(m_info == null);
            m_info = provider.CreateCLRField(this);
        }

        protected AST.FieldDecl m_nodeDecl;        
        public AST.FieldDecl Node
        {
            get { return m_nodeDecl;  }
        }
        
        public TypeEntry FieldType
        {
            get { return m_type; }
        }
        
        protected TypeEntry m_tClassDefined;
        public TypeEntry SymbolClass
        {
            get { return m_tClassDefined; }
        }
        
        public override string ToString()
        {
            return "FieldExp:" + m_tClassDefined.ToString() + "." + m_strName;
        }
#endregion

#region Checks        
        public override void DebugCheck(ISemanticResolver s)
        {
            Debug.Assert(Name != null);
            if (Node != null)
            {
                Debug.Assert(Node.Symbol == this);
            }
            Debug.Assert(Info != null);
            Debug.Assert(SymbolClass != null);
            Debug.Assert(FieldType != null);

            SymbolClass.DebugCheck(s);
            FieldType.DebugCheck(s);
        }


        // Write out this Entry's name, attributes, (and if fRecursive, then nested scope too)
        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("FieldExpEntry");
            o.WriteAttributeString("name", m_strName);
            o.WriteAttributeString("type", (m_type == null) ? "null" : m_type.ToString());
            o.WriteAttributeString("class", (m_tClassDefined == null) ? "null" : m_tClassDefined.ToString());
                        
            o.WriteEndElement();        
        }         
#endregion        

    }
#endregion

#region Methods
    //-----------------------------------------------------------------------------
    // Since methods can be overloaded, we have to be able to efficiently
    // search through the scope for all the methods. So we create a linked
    // list of methods and insert a header into the scope
    //-----------------------------------------------------------------------------
    public class MethodHeaderEntry : SymEntry
    {
        public MethodHeaderEntry(
            string stName
            )
        {
            this.m_strName = stName;    
        }
        
        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("MethodHeaderEntry");
            o.WriteAttributeString("name", m_strName);
            o.WriteEndElement();
        }
            
        public void AddMethodNode(MethodExpEntry entry)
        {
            Debug.Assert(entry.m_next == null);
            entry.m_next = m_head;
            m_head = entry;            
        }
        
        public MethodExpEntry GetFirstMethod()
        {
            return m_head;
        }
        public MethodExpEntry GetNextMethod(MethodExpEntry entry)
        {
            Debug.Assert(entry != null);
            return entry.m_next;
        }
        
        // For iterations
        public MethodEnumerator GetEnumerator()
        {
            return new MethodEnumerator(GetFirstMethod());
        }
        
        // For maintiaing a linked list
        MethodExpEntry m_head;
        
        public override string ToString()
        {
            int count = 0;
            MethodExpEntry h = m_head;
            while(h != null)
            {
                 h = h.m_next;
                 count++;
            }
            return "MethodHeaderEntry (" + count + " overloads)";
        }
    }
        
       
    // A MethodEnumerator is obtained from a MethodHeader
    // to get all the methods that have the same name.
    public struct MethodEnumerator
    {
        public MethodEnumerator(MethodExpEntry mStart)
        {
            m_methodCurrent = null;
            m_method = mStart;
        }
        
        public bool MoveNext()
        {
            //m_method = header.GetNextMethod(m);
            m_methodCurrent = m_method;
            if (m_method == null) 
                return false;
            m_method = m_method.m_next;
            return true;
        }
        
        public MethodExpEntry Current
        {
            get { return m_methodCurrent; }
        }
        
        MethodExpEntry m_method;
        MethodExpEntry m_methodCurrent;
    }
    
    //-----------------------------------------------------------------------------
    // Method
    //-----------------------------------------------------------------------------    
    public class MethodExpEntry : ExpEntry
    {        
#region Construction
        // User-declared
        public MethodExpEntry(
            string stName,              // name of the method
            AST.MethodDecl nodeClass,       // node of ast defining us (can be null)
            TypeEntry tDefiningClass,   // class we're defined in
            TypeEntry tReturnType      // return type            
            )
            : this(stName, nodeClass, tDefiningClass, tReturnType, false)
            {
            
            }
            
        // User-declared
        public MethodExpEntry(
            string stName,              // name of the method
            AST.MethodDecl nodeClass,       // node of ast defining us (can be null)
            TypeEntry tDefiningClass,   // class we're defined in
            TypeEntry tReturnType,      // return type
            bool fIsSpecialName
            )
        {
            Debug.Assert(tDefiningClass != null);

            this.m_strName = stName;
            this.m_classDefined = tDefiningClass;
            this.m_decl = nodeClass;
            this.m_type = tReturnType;
            this.m_fIsSpecialName = fIsSpecialName;
        }

        // Imported
        public MethodExpEntry(            
            ISemanticResolver s,
            System.Reflection.MethodBase info // CLR info for this method
            )
        {
            Debug.Assert(info != null);
            
            this.m_infoMethod = info;
            
            this.m_fIsSpecialName = info.IsSpecialName;
            this.m_strName = info.Name;
            this.m_classDefined = s.ResolveCLRTypeToBlueType(info.DeclaringType);
            
            // Set return type for non-constructors
            System.Reflection.MethodInfo mInfo = info as System.Reflection.MethodInfo;
            if (mInfo != null)
            {
                // not a ctor
                this.m_type = s.ResolveCLRTypeToBlueType(mInfo.ReturnType);
            } 
            else 
            {
                // ctor
                this.m_type = null;
                m_strName = m_classDefined.Name;
            }
        }
#endregion
    
#region Checks   
        public override string ToString()
        {
            //return "MethodExp:" + m_classDefined.ToString() + "." + m_strName;        
            return "MethodExp:" + this.PrettyDecoratedName;
        }
    
        // Write out this Entry's name, attributes, (and if fRecursive, then nested scope too)
        public override void Dump(XmlWriter o, bool fRecursive)
        {
            o.WriteStartElement("MethodExpEntry");
            o.WriteAttributeString("name", m_strName);
            o.WriteAttributeString("rettype", (m_type == null) ? "null" : m_type.ToString());
            o.WriteAttributeString("class", (m_classDefined == null) ? "null" : m_classDefined.ToString());
            
            if (fRecursive && (m_scope != null)) 
            {
                m_scope.Dump(o, true);
            }
            o.WriteEndElement();        
        }  
        
        // Debugging support
        public override void DebugCheck(ISemanticResolver s)
        {
            //Debug.Assert(AST.Node.m_DbgAllowNoCLRType || m_typeCLR != null);
            if (Node != null)
            {
                Debug.Assert(Node.Symbol == this);
                
                // Non-null means that we're a user method. So we'd better
                // have a scope
                Debug.Assert(m_scope != null);
                m_scope.DebugCheck(s);
            } 
            else 
            {
                // If Node is null, then we're imported.
                // Can't have a scope & body for an imported function...
                Debug.Assert(m_scope == null);    
            }
            
            
            if (!IsCtor)
            {
                // Must have a return type (even Void is still non-null)
                Debug.Assert(RetType != null);
                RetType.DebugCheck(s);
            }
            
            // Must be defined in some class
            Debug.Assert(m_classDefined != null);
                        
        }

#endregion         

#region Properties & Data        

        // For building a linked list of methods for searching for overloads
        // Only MethodHeaderEntry should change this.
        internal MethodExpEntry m_next;
                   
        // Location in AST that this method is declared
        protected AST.MethodDecl m_decl;
        public AST.MethodDecl Node
        {
            get { return m_decl; }
        }
        
        // Scope containing our parameters & locals
        public Scope m_scope;
        /*
        public Scope Scope
        {
            get { return m_scope; }
        }
        */

        
        // Class that we're defined in 
        protected TypeEntry m_classDefined;        
        public TypeEntry SymbolClass
        {
            get { return m_classDefined; }
        }
        
        bool m_fIsSpecialName;
        public bool IsSpecialName
        {
            get { return m_fIsSpecialName; }
            set { m_fIsSpecialName = value; }
        }
        
        // m_type is the return type of this method    
        // It's null if we're a ctor
        public bool IsCtor
        {
            get { return m_type == null; }
        }
        
        // Return type for this method
        public TypeEntry RetType
        {
            get { return m_type; }        
        }
#endregion                

#region Convenience Helpers
        // return a string that fully represents the method exp
        public string PrettyDecoratedName
        {
            get {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                
                sb.Append(this.SymbolClass.FullName);
                sb.Append('.');
                sb.Append(this.Name);
                sb.Append('(');
                for(int i = 0; i < ParamCount; i++)
                {
                    if (i != 0)
                        sb.Append(',');
                        
                    Type t = ParamCLRType(i);
                    sb.Append(t.ToString());
                }
                
                sb.Append(')');
                                
                return sb.ToString();
            }        
        }

        // Convenience helpers
        /*
            public TypeEntry ParamType(int iParam)
            {            
                if (this.Node != null) 
                {
                    return Node.Params[iParam].Symbol.m_type;
                } 
                else if (this.Info != null) 
                {
                // Can't convert from clr -> blue without a context,
                // and we don't want to pass a context around
                    return Info.GetParameters()[iParam].ParameterType.###;
                } 
                else 
                {
                    return null;                
                }        
            } // end ParamType
          */    

        // Get an array of all params (never includes a this pointer)
        // If fStripRefs is true, then we automatically convert T& --> T
        public System.Type [] ParamTypes(bool fStripRefs)
        {
            Type [] al = new Type[this.ParamCount];
            for(int i = 0; i < ParamCount; i++)
            {
                Type t = ParamCLRType(i);
                Debug.Assert(t != null, "Only call site can have null param types");
                
                if (fStripRefs)
                    if (t.IsByRef)
                        t = t.GetElementType();                
                al[i] = t;
            }
            
            return al;
        }
        

        // @todo - how does the numbering here work? Start at 0 or 1?
        // does it include the 'this' parameter
        public System.Type ParamCLRType(int iParam)
        {   
            // Parameter can be either a actual type or a reference to a type         
            if (this.Node != null) 
            {                
                ParamVarExpEntry sym = Node.Params[iParam].ParamSymbol;
                
                return sym.m_type.CLRType;
            } 
            else if (this.Info != null) 
            {
                return Info.GetParameters()[iParam].ParameterType;
            } 
            else 
            {
                return null;
            }            
        } // end ParamCLRType
        
        public int ParamCount
        {   
            get 
            {
                if (this.Node != null) 
                {
                    return Node.Params.Length;
                } 
                else if (this.Info != null) 
                {
                    return Info.GetParameters().Length;
                } 
                else 
                {
                    return -1;                
                }
            }                
        } // end ParamCount     
#endregion    

#region CLR Properties
        // Method Info    
        protected System.Reflection.MethodBase m_infoMethod;
        
        public System.Reflection.MethodBase Info
        {
            get  { return m_infoMethod; }
        }

        public void SetInfo(ICLRtypeProvider provider)
        {
            Debug.Assert(m_infoMethod == null);
            m_infoMethod = provider.CreateCLRMethod(this);
        }
        
#endregion                
        // @todo - parameter info
        public bool IsStatic
        {
            get 
            { 
                if (m_decl != null) 
                {
                    return m_decl.Mods.IsStatic;                
                } 
                else 
                {
                    return m_infoMethod.IsStatic;                
                }
            }
        }
        
        // Has Virtual, bot not NewSlot
        public bool IsOverride
        {
            get { 
                // @dogfood - these should be const & unsigned..
                int i = (int) m_infoMethod.Attributes;
                int v = (int) System.Reflection.MethodAttributes.Virtual;
                int n = (int) System.Reflection.MethodAttributes.NewSlot;
                
                return (i & (v | n)) == v;
            }
        }
    }

#endregion

#region Simple Variables (Locals & Params)

    //-----------------------------------------------------------------------------    
    // Local variables
    //-----------------------------------------------------------------------------    
    public abstract class VarExpEntry : ExpEntry
    {
        // Write out this Entry's name, attributes, (and if fRecursive, then nested scope too)
        public override void Dump(XmlWriter o, bool fRecursive)    
        {
            o.WriteStartElement("LocalExpEntry");
            o.WriteAttributeString("name", m_strName);
            o.WriteAttributeString("type", (m_type == null) ? "null" : m_type.ToString());        
            o.WriteEndElement();    
        }
        
        public TypeEntry VarType
        {
            get { return m_type; }
        }
        
    }

    //-----------------------------------------------------------------------------    
    // Local variables
    //-----------------------------------------------------------------------------    
    public class LocalVarExpEntry : VarExpEntry
    {
        public LocalVarExpEntry()
        {
            //m_iCodeGenSlot = -1;
        }
    
        public override string ToString()
        {
            return "LocalVarExp:" + m_strName;
        }
    
        // Let codegen associate its builder with the symbol
        protected System.Reflection.Emit.LocalBuilder m_bldLocal;
        public System.Reflection.Emit.LocalBuilder Builder
        {
            get { return m_bldLocal; }
            set { m_bldLocal = value; }
        }
    
        // Codegen needs to assign each local a slot
        /*
        protected int m_iCodeGenSlot;
        public int CodeGenSlot
        {
            get { Debug.Assert(m_iCodeGenSlot != -1); return m_iCodeGenSlot; }
            set { Debug.Assert(value >= 0); m_iCodeGenSlot = value; }
        }
        */
    }

    //-----------------------------------------------------------------------------    
    // Parameters
    // Note that the CLR requires 'this' be parameter 0
    //-----------------------------------------------------------------------------    
    public class ParamVarExpEntry : VarExpEntry
    {
        public ParamVarExpEntry()
        {
            
        }
    
        public override string ToString()
        {
            return "ParamVarExp:" + m_strName;
        }
    
        // Let codegen associate its builder with the symbol
        protected System.Reflection.Emit.ParameterBuilder m_ParameterBuilder;
        public System.Reflection.Emit.ParameterBuilder Builder
        {
            get { return m_ParameterBuilder; }
            set { m_ParameterBuilder = value; }
        }
    
        // Codegen needs to assign each param a slot    
        #if true
        protected int m_iCodeGenSlot = -1;        
        public int CodeGenSlot
        {
            get { Debug.Assert(m_iCodeGenSlot != -1); return m_iCodeGenSlot; }
            set { Debug.Assert(value >= 0); m_iCodeGenSlot = value; }
        }
        #else
        public int CodeGenSlot
        {
            get { return m_ParameterBuilder.Position; }
            set { }
        }
        #endif
                
    }
#endregion


#endregion
} // end namespace
