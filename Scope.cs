//-----------------------------------------------------------------------------
// Scope.cs
// Represent a scope
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Xml;
using Blue.Public;

namespace SymbolEngine
{
  
//-----------------------------------------------------------------------------
/// <summary>
/// IScopeLookup is used by a <see cref="Scope"/> to do a smart lookup.
/// <seealso cref="Scope"/>
/// </summary>
/// <remarks>
/// While all scopes have the same notion of 'lexical' parent, each type of 
/// scope has its own notion of 'super' or 'inherited' parent. For example:
/// <list type="bullet">
/// <item>BlockStatement, Method - 0 parents </item>
/// <item>Class - 1 parent, for the base class</item>
/// <item>Interfaces - multiple parents, 1 parent for each base interface</item>
/// <item>Namespace - A pseudo-parent from each using clauses.</item>
/// </list>
/// 
/// Either Nodes or Symbols may implement IScopeLookup, depending on which
/// can best provide the super class.
/// Only a Scope should have a reference to these. 
///</remarks>
//-----------------------------------------------------------------------------
public interface ILookupController
{
    // @todo - can I remove the Scope paremeters to this and DumpScope?
    
    // The main worker function.
    // Look for the given identifier in the given scope. 
    // This is a smart lookup because it can use extra information to search
    // the scope's dynamic range (ex: super classes, using-clauses)
    // Return Null if not found. (Don't throw an exception).
    SymEntry SmartLookup(string stIdentifier, Scope scope);
    
    // Get a node responsible for this scope.
    // For imported types, this will be null
    AST.Node OwnerNode { get ; }
    
    // Get a symbol responsible for this scope. 
    // This may be null. (If this is null, then OwnerNode should not be null).
    SymEntry OwnerSymbol { get; }
    
    // For debugging purposes. Used by DumpTree();
    void DumpScope(Scope scope);
}
    
    //-----------------------------------------------------------------------------    
    /// <summary>
    /// A scope is a low-level object to associate <see cref="Identifier"/> objects 
    /// with <see cref="SymEntry"/> objects.
    /// <seealso cref="ILookupController"/>      
    /// </summary>    
    /// <remarks>
    ///
    /// <para>Every scope has a lexical parent. The following list shows what type
    /// of scopes can be lexically nested in which other scopes:
    /// <list type="bullet">
    /// <item>block -> block, method</item>
    /// <item>method -> class</item>
    /// <item>class -> class, interface, namespace</item>
    /// <item>interface -> namespace </item>
    /// <item>namespace -> namespace</item>
    /// </list> </para>
    ///
    /// <para> Scopes have no knowledge of either the higher level type-system nor any 
    /// object derived from <see cref="SymEntry"/>.  </para>
    ///
    /// <para> Each of the different scopes above may also inherit items from super scopes
    /// A class's scope inherits from its base class. An interface inherits from all of
    /// its base interfaces. Some scopes, such as those for blocks and methods, only have
    /// a lexical parent, but no logical parent to inherit from.</para>
    /// <para>Because a scope is a 'dumb' object, it can't be aware of its super scopes.
    /// So a scope uses an <see cref="ILookupController"/> to do smart lookups in 
    /// super scopes.</para> 
    /// </remarks>
    //-----------------------------------------------------------------------------
    public class Scope
    {
#region Construction  
        private Scope()
        {
            m_fIsLocked     = false;            
            m_pController   = null;
            
            #if DEBUG
            // Set unique id
            m_id            = m_count;
            m_count++;
            #endif
            
        }
        
        /// <summary>
        /// Create a Scope object
        /// <seealso cref="CreateSharedScope"/>
        /// </summary>
        /// <param name="szDebugName">A string name to identify the scope for debugging uses.</param>
        /// <param name="pController">An ILookupController to allow the scope to do smart lookups. (can be null)</param>
        /// <param name="scopeLexicalParent">The lexical parent of the scope. (may be null). </param>
        public Scope(string szDebugName, ILookupController pController, Scope scopeLexicalParent) 
            : this()
        {
            #if false
            if (szDebugName == "cClass_") 
                System.Diagnostics.Debugger.Break();
            #endif                
            
            m_szDebugName   = szDebugName;            
            m_pController   = pController;
            m_LexicalParent = scopeLexicalParent;
                        
            m_table         = new System.Collections.Hashtable();
        }
        
        /// <summary>
        /// Create a scope that has its own controller and lexical parent, but shares
        /// a set of symbols with an existing scope.
        /// </summary>
        /// <remark>
        /// We may want multiple Scopes with different controllers but that share 
        /// the same symbol set.
        /// <para>Ex: A namespace can be declared in multiple blocks. All sections reside
        /// in the same scope. However, each block has its own set of using
        /// directives. So we want multiple scopes that share the same set of symbols, 
        /// but each scope needs its own controller to handle its own set of using directives</para>        
        /// </remark>
        /// <param name="pController">The controller for the new scope</param>
        /// <param name="scopeLexicalParent"></param>
        /// <returns>A new scope with the specified lexical parent and owned by the given 
        /// controller, but sharing the same set of symbols as the current scope.</returns>
        public Scope CreateSharedScope(ILookupController pController, Scope scopeLexicalParent)
        {
            Scope s = new Scope();
            s.m_szDebugName     = m_szDebugName;
            s.m_pController     = pController;
            s.m_LexicalParent   = scopeLexicalParent;            
            
            // Both scopes share the same table.
            s.m_table           = m_table;            
            return s;
        }
                
#endregion        
        
#region Properties & Data        
        // All for debugging:
        public string m_szDebugName;
        
        // Provide unique ids for each scope.
        #if DEBUG
        private static int m_count = 0;
        private int m_id;
        #endif

        // A scope's only link to the higher level world.
        ILookupController m_pController;
        
        /// <summary><value>
        /// A symbol associated with this scope.
        /// </value></summary>
        public SymEntry Symbol
        {
            get { return m_pController.OwnerSymbol; }
        }
        
        /// <summary><value>
        /// An AST node associated with this scope.
        /// </value></summary>
        public AST.Node Node 
        {
            get { return m_pController.OwnerNode; }
        }

        // Store the actual SymEntrys in a hash.
        protected System.Collections.Hashtable m_table;        
        
        // m_LexicalParent - our lexical parent scope. Non-null except for single global scope.        
        internal Scope m_LexicalParent;
        
        /// <summary><value>
        /// The lexical parent of this scope.
        /// </value></summary>
        /// <remarks>
        /// Scopes are identified on the source level and they can be lexically nested within
        /// each other. The chain of lexical parents form a context. 
        /// <para>Imported types have no lexical parent because they have no source.</para>
        /// </remarks>
        public Scope LexicalParent
        {
            get { return m_LexicalParent; }
        }
       
#endregion

#region Locking
        
        // A locked scope can't be added too. Useful for imported scopes, and
        // for finializing scopes after we've resolved a symbol.
        // Locking is also a useful flag to implement lazy evaluation because
        // it lets us check if a scope has been finished.
        bool m_fIsLocked;
        internal void LockScope()
        {
            m_fIsLocked = true;
        }
        internal bool IsLocked
        {
            get { return m_fIsLocked; }
        }
#endregion

    
#region Add & Lookup operations
        /// <summary>
        /// Add a symbol to this scope. (SymEntry contains the string name)        
        /// <seealso cref="AddAliasSymbol"/>
        /// </summary>
        /// <remarks>
        /// Adds the given Symbol to this scope, and indexes it by the symbol's name.        
        /// </remarks>
        /// <param name="s">The symbol to add</param>
        
        public void AddSymbol(SymEntry s) 
        {
            Debug.Assert(!m_fIsLocked, "Can't add to a locked scope");
            Debug.Assert(s != null);
            Debug.Assert(s.Name != null);
            
            // If we try to add the symbol and it's already there, we have a Symbol-Redefinition
            // error. Hashtable.Add() will throw an exception, we catch that, and then throw our
            // own version. 
            // We choose to catch, rather than to check first, because we optimize for the 
            // non-error case.
            try
            {            
                m_table.Add(s.Name, s); // this already throws
            }
            catch(ArgumentException)
            {
                // @todo - How should we handle this error?
                Debug.Assert(false, "@todo - symbol already defined");   
            }
        }
    
        /// <summary>
        /// Add a symbol under an aliased name.
        /// </summary>
        /// <remarks>
        /// Add an existing symbol entry, but indexed under a new name
        /// <para><example>
        /// [globalscope].AddAliasSymbol("int", [SymEntry for System.Int32]);
        /// </example></para>
        /// <seealso cref="AddSymbol"/>
        /// </remarks>
        /// <param name="stAliasName">Aliased name of the symbol</param>
        /// <param name="s">Symbol to add</param>                
        public void AddAliasSymbol(string stAliasName, SymEntry s)
        {                   
            Debug.Assert(!m_fIsLocked, "Can't add to a locked scope");
            m_table.Add(stAliasName, s);
        }
        
        /// <summary>
        /// Do a deep lookup in this scope. This includes super scopes but not lexical parents.
        /// </summary>
        /// <remarks>
        /// <para>If this scope does not have an ILookupController, this is equivalent to 
        /// just calling <see cref="LookupSymbolInThisScopeOnly"/> </para>
        /// <para>else this calls <see cref="ILookupController.SmartLookup"/></para>
        /// </remarks>
        /// <param name="strName">String name to search for</param>
        /// <returns>A symbol added under this name. Null if not found.</returns>
        public SymEntry LookupSymbol(string strName)
        {
            // @todo - we do this when looking for namespaces during import.
            // If it's not there, we add it. 
            // It would be nice to enable this assert though...
            //Debug.Assert(m_fIsLocked, "Don't lookup a symbol in an unlocked scope");
            if (m_pController == null)
                return this.LookupSymbolInThisScopeOnly(strName);
            else
                return m_pController.SmartLookup(strName, this);
        }
        
        /// <summary>
        /// Lookup a SymEntry only in this scope. Don't search super scopes or lexical parents. 
        /// </summary>
        /// <remarks>This is a shallow lookup that does not invoke the ILookupController</remarks>
        /// <param name="strName">String name to search for.</param>
        /// <returns>A symbol added under this name. Null if not found.</returns>
        public SymEntry LookupSymbolInThisScopeOnly(string strName)
        {        
            return (SymEntry) m_table[strName];
        }
        
        // If we don't know the string name and we need to do a more elaborate
        // match (ex, say on parameters), then we have to provide access to our
        // values.
        // So expose an enumerator for the all the SymEntry in this scope.
        public System.Collections.IEnumerator GetEnumerator()
        {
            return this.m_table.Values.GetEnumerator();
        }

#endregion
        
#region Checks    
        #if DEBUG
        // Dump a tree view of the scope tree that this is a root for.
        // Includes the super scopes & lexical parents.
        public void DumpTree()
        {
            Console.WriteLine("** Debug print of current context **");
            Scope s = this;
            int i = 0;
            while (s != null)
            {
                Console.Write("{0}:#{1},{2}", i, s.m_id, s.m_szDebugName);
                
                if (s.m_pController != null)
                {
                    object o = s.m_pController;
                    Console.Write("({0})", o.GetType().Name);
                    s.m_pController.DumpScope(s);
                }
                
                Console.WriteLine();
                i++;
                s = s.LexicalParent;
            }
        }
        
        // Dump all the raw entries
        public void DumpKeys()
        {
            Console.WriteLine("*** Debug dump of keys in scope #{0},{1} [", this.m_id, this.m_szDebugName);
            
            System.Collections.IDictionaryEnumerator e = m_table.GetEnumerator();            
            while (e.MoveNext())
            {
                string str = (string) e.Key;
                SymEntry sym = (SymEntry) e.Value;
                
                Console.WriteLine("{0} is a {1}", str, sym.ToString()); 
            }
            Console.WriteLine("] End dump");
        }
        #endif

        // Verify integrity of all symbol elements in this scope
        public void DebugCheck(ISemanticResolver s)
        {
            System.Collections.IDictionaryEnumerator e = m_table.GetEnumerator();            
            while (e.MoveNext())
            {
                string str = (string) e.Key;
                SymEntry sym = (SymEntry) e.Value;
                
                sym.DebugCheck(s);
            }
        }
        
        // Dump the scope to an xml file
        public void Dump(XmlWriter o, bool fRecursive)
        {   
            o.WriteStartElement("scope");
            o.WriteAttributeString("name", m_szDebugName);

            ILookupController p = this.m_pController;            
            o.WriteAttributeString("controller", (p == null) ? "none" : ((object) p).GetType().Name);
            /*
            if (m_SuperScope != null) 
            {
                o.WriteAttributeString("super", m_SuperScope.m_szDebugName);
            }
            */
            
            System.Collections.IDictionaryEnumerator e = m_table.GetEnumerator();            
            while (e.MoveNext())
            {
                string str = (string) e.Key;
                SymEntry sym = (SymEntry) e.Value;
                
                sym.Dump(o, fRecursive);
            }
        
            o.WriteEndElement(); // scope
        }     
        
        // Debug function
        public void DebugConsoleDump()
        {
            XmlWriter o = new XmlTextWriter(Console.Out);
            bool f = true;
            if (f)
                Dump(o, true);
            else 
                Dump(o, false);
            o.Close();
        }
#endregion        
    } // end class scope

}