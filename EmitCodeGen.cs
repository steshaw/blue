/***************************************************************************\
*
* File: EmitCodeGen.cs
*
* Description:
* EmitCodeGen.cs defines the "System.Reflection.Emit" CodeGen for the 
* compiler.
*
*
* History:
*  8/14/2001: JStall:       Created
*
* Copyright (C) 2001.  All rights reserved.
*
\***************************************************************************/


//-----------------------------------------------------------------------------
// @todo - Split EmitCodeGen into 2 parts. 
//-----------------------------------------------------------------------------

// ...
// There is a bug in the enum builder (10/12/2001) that won't let us
// set the enum fields to the proper type. 
// We can workaround by directly using a typebuilder. When the bug is fixed,
// we should switch back over to EnumBuilders.
// This only affects CodeGen.
#define Bug_In_EnumBuilder

using System;
using System.Collections;
using System.Threading;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;

using Blue.Public;
using Utilities = Blue.Utilities;
using ErrorLog = Blue.Utilities.ErrorLog;
using Log = Blue.Log;

namespace Blue.CodeGen
{


/***************************************************************************\
*****************************************************************************
*
* class EmitCodeGen
*
* EmitCodeGen implements the CodeGen interface for using 
* System.Reflection.Emit.
*
*****************************************************************************
\***************************************************************************/

public class EmitCodeGen :
    ICLRtypeProvider,
    ICodeGenDriver
{

#region Enums

    //
    // Enums
    //
/*
    [FlagsAttribute()]
    internal enum ClassAttributes
    {
        Public      = 0x00000001,
    }
*/
    private enum TargetType
    {
        Console,
        Windows,
        Dll
    }

#endregion Enums


#region Construction

    /***************************************************************************\
    *
    * EmitCodeGen.EmitCodeGen
    *
    * EmitCodeGen() initializes a new CodeGen object.
    *
    \***************************************************************************/

    internal
    EmitCodeGen(
        Blue.Public.IOptions opt)              // Options
    {
        // Set default target type. An option handler may change it on us.
        m_TargetType        = TargetType.Console;
        

        //
        // Register OptionHandlers.
        //
        opt.AddHandler("target", "t", new OptionHandler(this.Option_Target),
            "Target module format (windows, console, library)", 
            "Select what type of executable to produce\n"+
            "/target:library - produce a dll\n"+
            "/target:windows - produce a windows executable.\n"+
            "/target:console - (default) produce a console executable\n");
                                
        #if false
            Option_DebugInfo(""); // force debugging info in all cases
        #endif
        opt.AddHandler("debug", null, new OptionHandler(this.Option_DebugInfo), 
                "Generate .PDB symbols file for debugging", 
                "Generate a .pdb file for debugging purposes. Default is off.\n"+
                "ex:\n"+
                "/debug");
                                
        opt.AddHandler("main", "m", new OptionHandler(this.Option_SetMain), 
            "Specify class containing Main method", 
            "Explicitly specify which class has the Main method.\n"+
            "ex:\n"+
            "/main:MyClass");
            
        opt.AddHandler("out", null, new OptionHandler(this.Option_Out), 
            "Specify the output name to generate", 
            "ex:\n"+
            "/out:Dogfood.exe"
            );
    }

#endregion Construction




#region Print Errors
    internal enum ErrorCodes
    {
        cDuplicateMain,
        cNoMain,
        cIOException,
        cEntryClassNotFound,
    }
    
    private class CodeGenErrorException : ErrorException
    {
        // Codegen errors aren't associated with a particular file range, so we omit location
        internal CodeGenErrorException(ErrorCodes c, string s) : 
            base (c, null, s)
        {
            // All Codegen errors will come through this body.
        }
    }    
    
    private void PrintError(CodeGenErrorException e)
    {        
        Blue.Driver.StdErrorLog.PrintError(e);
    }

    void PrintError_DuplicateMain()
    {
        PrintError(
            new CodeGenErrorException(ErrorCodes.cDuplicateMain, "Duplicate Main entry points. Use /m switch to specify which class to use.")
        );
    }
    
    void PrintError_NoMain()
    {
        PrintError(
            new CodeGenErrorException(ErrorCodes.cNoMain, "No Main function found")
        );
    }
    
    void PrintError_IOException(string stHint)
    {
        PrintError(
            new CodeGenErrorException(ErrorCodes.cIOException, stHint)
        );
    }
    
    void PrintError_EntryClassNotFound(string stClass)
    {
        PrintError(
            new CodeGenErrorException(
                ErrorCodes.cEntryClassNotFound,
               "The class '" + stClass+"' specified by the /m switch does not exist."
            )
        );
    }


#endregion

#region Implementation for SymbolEngine.ICLRtypeProvider
    // ...
    // There's a bug in ModuleBuilder.GetType(), so we have to use our own 
    // version for now.
    System.Type ModuleBuilder_GetType(ModuleBuilder m, string stType)
    {
    #if false
        // This should work, but is broken for nested types.
        return m.GetType(stType);
    #else        
        // Here's our hack to use in the meantime
        // Note, we have to deal with appended characters like [], and &
        // This is a hack that will work for now. When reflection-emit fixes the 
        // bug, we can get rid of this.
        Type [] al = m.GetTypes();
        foreach(Type t in al)
        {
            if (t.FullName == stType)
                return t;
        }
        return null;    
    #endif
    }


    // Must let the Type provider know what assemblies are imported so that
    // we can search them for types.
    void SetImportedAssemblies(Assembly [] asmImports)
    {
        Debug.Assert(asmImports != null);
        m_asmImports = asmImports;
        m_MscorLib = Assembly.Load("MsCorLib.dll");
    }
    Assembly m_MscorLib;
    Assembly [] m_asmImports;
    
    

        
    // Find a compound type (like T&, T[], T[][]&, T[,] ..)
    // May be in either the module we're building or an imported assembly.
    protected System.Type FindType(string stFullName)
    {
        System.Type tRef = null;
        
        System.Type tNatural = m_MscorLib.GetType(stFullName);
                        
        // Look in the module we're building.
        //tRef = ModuleBuilder_GetType(m_bldModule, stFullName);
        tRef = m_bldModule.GetType(stFullName);
        
        Debug.Assert((tNatural == null) || (tRef == null), "type can only be baked or unbaked, not both");
        
        if (tNatural != null)
            return tNatural;
        
        
        if (tRef != null)
            return tRef;
            
                    
                
        // Look in an imported modules
        Assembly[] alist = m_asmImports;
        foreach(Assembly a in alist)
        {
            Type t = a.GetType(stFullName);
            if (t != null)
                return t;
        }


        // Not found, there's an error:
        // - we have an illegal compound type?
        // - the elem type of the compound type doesn't exist?
        // Or perhaps we require Resolution to catch this, if so the assert is ok.
        Debug.Assert(tRef != null); // @legit
        return null;
    }

    /***************************************************************************\
    *
    * EmitCodeGen.GetReferenceType
    * - Returns a CLR type that is a reference to the type we pas in.
    \***************************************************************************/
    public virtual System.Type CreateCLRReferenceType(System.Type tElem)
    {
        Debug.Assert(!tElem.IsByRef, "Can't create a reference to a reference");
        string st = tElem.FullName + "&";
                
        System.Type tRef = this.FindType(st);
        
        Debug.Assert(tRef != null);
        return tRef;
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.CreateCLRArrayType
    * 
    \***************************************************************************/
    public virtual System.Type 
    CreateCLRArrayType(
        SymbolEngine.ArrayTypeEntry symBlueType
    )
    {        
        // Get name of an array, like T[], T[,,][,][]
        string st = symBlueType.ToString();

               
        System.Type t = this.FindType(st);

        // Semantic checking should have caught this.
        Debug.Assert(t != null);

       
        return t;
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.SetCLRClassType
    * 
    \***************************************************************************/
    public virtual System.Reflection.FieldInfo
    CreateCLRField(
        SymbolEngine.FieldExpEntry symBlueField
        )
    {
        FieldAttributes attr = (FieldAttributes) 0;
        
        AST.Modifiers mods = symBlueField.Node.Mods;
        
        if (mods.IsPublic)
            attr |= FieldAttributes.Public;
        else if (mods.IsProtected)
            attr |= FieldAttributes.Family;
        else if (mods.IsPrivate)
            attr |= FieldAttributes.Private;
        else if (mods.IsInternal)
            attr |= FieldAttributes.Assembly;
            
        
        if (mods.IsReadOnly)
            attr |= FieldAttributes.InitOnly;
            
        if (mods.IsStatic)
            attr |= FieldAttributes.Static;
                
        // Get class that we're defined in
        TypeBuilder bldClass = symBlueField.SymbolClass.CLRType as TypeBuilder;
        Debug.Assert(bldClass != null);

        // Create the field
        FieldInfo f = bldClass.DefineField(symBlueField.Name, symBlueField.FieldType.CLRType, attr);
        Debug.Assert(f != null);

        return f;
    }

    // 
    public virtual System.Reflection.FieldInfo
        CreateCLRLiteralField(
        SymbolEngine.LiteralFieldExpEntry sym)
    {
        
        Type t = sym.SymbolClass.CLRType;
#if Bug_In_EnumBuilder
        TypeBuilder bld = t as TypeBuilder;
        Debug.Assert(bld != null);
        
        FieldAttributes mods = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal;
        FieldBuilder f = bld.DefineField(sym.Name, bld, mods);                
        f.SetConstant(sym.Data);
#else
        if (t is EnumBuilder)
        {
            EnumBuilder bld = t as EnumBuilder;
            f = bld.DefineLiteral(sym.Name, sym.Data);
        } 
        else if (t is TypeBuilder)
        {
           Debug.Assert(false, "Literals not supported on non-enum Types");
        }
#endif
        // The new field should have the following properties:
        Debug.Assert(f.IsStatic);
        Debug.Assert(f.IsPublic);
        Debug.Assert(f.IsLiteral);
        Debug.Assert(f.FieldType == bld); // this is where the EnumBuilder breaks

        return f;
    }
    

    /***************************************************************************\
    *
    * EmitCodeGen.CreateCLREnumType
    * 
    \***************************************************************************/
    public virtual System.Type CreateCLREnumType(SymbolEngine.EnumTypeEntry symEnum)
    {
        TypeAttributes mods = TypeAttributes.Sealed;
                    
#if Bug_In_EnumBuilder

        string stName = symEnum.FullName;

        TypeBuilder bld;
        
        // Handle nesting
        SymbolEngine.TypeEntry tParent = symEnum.GetContainingType();
        if (tParent == null)
        {
            if (symEnum.Mods.IsPublic)
                mods |= TypeAttributes.Public;
            else
                mods |= TypeAttributes.NotPublic;
            
            Log.WriteLine(Log.LF.CodeGen, "Define enum:{0}", stName);
            bld = m_bldModule.DefineType(stName, mods, typeof(System.Enum));
        } else {
            if (symEnum.Mods.IsPublic)
                mods |= TypeAttributes.NestedPublic;
            else if (symEnum.Mods.IsPrivate)
                mods |= TypeAttributes.NestedPrivate;
            else if (symEnum.Mods.IsProtected)
                mods |= TypeAttributes.NestedFamily;
            else if (symEnum.Mods.IsInternal)
                mods |= TypeAttributes.NestedAssembly;                                
                
                
            Log.WriteLine(Log.LF.CodeGen, "Define nested enum:{0}", stName);
            TypeBuilder bldParent = tParent.CLRType as TypeBuilder;
            bld = bldParent.DefineNestedType(symEnum.Name, mods, typeof(System.Enum));
            // DefineNestedType says it wants the 'full name', but name Name can't inclue [,],\,+            
            // So we're in a pickle. And then when the TypeResolve event is fired, we don't
            // get enough info (we just get the short name).
            
            //string stName2 = symEnum.FullName.Replace('+', '\\');            
            //bld = bldParent.DefineNestedType(stName2, mods, typeof(System.Enum));
            
        }
        
        // Enums have a default magical field: 
        bld.DefineField("value__", typeof(int), 
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName);
#else
        #error - need a DefineNestedEnum()
        EnumBuilder bld = m_bldModule.DefineEnum(stName, mods, typeof(int));
#endif
        return bld;
    }

    /***************************************************************************\
    *
    * EmitCodeGen.SetCLRClassType
    * 
    \***************************************************************************/
    public virtual System.Type
    CreateCLRClass(
        SymbolEngine.TypeEntry symBlueType
    )
    {
        // We call DefineType to get a TypeBuilder, which derives from Type
        // We have to pass DefineType all the interesting parameters (ie, we
        // have to know stuff like the base class now, not later)

        // We get namespaces for free by using the full name
        // We also handle nested classes here.
        
        string stClassName = symBlueType.FullName;
        System.Type typeBaseClass = null;
        TypeAttributes attrClass = (TypeAttributes) 0;                           

        SymbolEngine.TypeEntry [] arBlueInterfaces = symBlueType.BaseInterfaces;
        System.Type [] arClrInterfaces = new System.Type[arBlueInterfaces.Length];
        for(int i =0; i < arClrInterfaces.Length; i++)
        {
            arClrInterfaces[i] = arBlueInterfaces[i].CLRType;
            Debug.Assert(arClrInterfaces[i] != null);
        }
        
        
        // Setup modifiers
        AST.Modifiers mods = symBlueType.Mods;
        TypeBuilder bldClass;
        
        if (symBlueType.IsInterface)        
            attrClass |= TypeAttributes.Interface | TypeAttributes.Abstract;
        else
        {
            typeBaseClass = symBlueType.Super.CLRType;
            Debug.Assert(typeBaseClass != null);
        }
        
        if (mods.IsSealed)
            attrClass |= TypeAttributes.Sealed;
        if (mods.IsAbstract)
            attrClass |= TypeAttributes.Abstract;
        if (mods.IsSealed)
            attrClass |= TypeAttributes.Sealed;            
        
        // Handle nesting
        SymbolEngine.TypeEntry tParent = symBlueType.GetContainingType();
        if (tParent == null)
        {
            // Not nested
            if (mods.IsPublic)
                attrClass |= TypeAttributes.Public;
            else
                attrClass |= TypeAttributes.NotPublic;                    
                            
            Log.WriteLine(Log.LF.CodeGen, "Define type:{0}", stClassName);                
            bldClass = m_bldModule.DefineType(
                stClassName, 
                attrClass, 
                typeBaseClass, 
                arClrInterfaces
            );
        } else {
            // Nested
            if (mods.IsPublic)
                attrClass |= TypeAttributes.NestedPublic;
            else if (mods.IsPrivate)
                attrClass |= TypeAttributes.NestedPrivate;
            else if (mods.IsProtected)
                attrClass |= TypeAttributes.NestedFamily;
            else if (mods.IsInternal)
                attrClass |= TypeAttributes.NestedAssembly;   
                
            Log.WriteLine(Log.LF.CodeGen, "Define nested type:{0}", stClassName);                
            TypeBuilder bldParent = tParent.CLRType as TypeBuilder;
            bldClass = bldParent.DefineNestedType(symBlueType.Name, attrClass, typeBaseClass, arClrInterfaces);       
        }            
        
        Debug.Assert(bldClass != null, "Should have TypeBuilder by now");
        
        // Make sure that we can lookup the type that we just created
        #if DEBUG                
        System.Type tNatural = this.FindType(stClassName);
        Debug.Assert(tNatural == bldClass);
        #endif        
        
        // Sanity check that the type builder is complete enough to do
        // clr inheritence checks
        #if DEBUG
        if (typeBaseClass != null)
            Debug.Assert(bldClass.IsSubclassOf(typeBaseClass));

        foreach(System.Type t in arClrInterfaces)
        {   
            bool f1 = SymbolEngine.TypeEntry.IsAssignable(bldClass, t);
            Debug.Assert(f1);
        }
        #endif


        // Return the type and the symbol engine will set it
        return bldClass;
    }
    
    //-------------------------------------------------------------------------
    // Create a CLR event for use w/ reflection-emit
    //-------------------------------------------------------------------------
    public System.Reflection.EventInfo 
    CreateCLREvent(
        SymbolEngine.EventExpEntry symEvent
    )
    {
        string stName = symEvent.Name;
        
        // Get class that we're defined in
        TypeBuilder bldClass = symEvent.SymbolClass.CLRType as TypeBuilder;
        Debug.Assert(bldClass != null);
        
        
        EventAttributes attrs = EventAttributes.None;
        AST.Modifiers mods = symEvent.Node.Mods;
        
        
        EventBuilder bld = bldClass.DefineEvent(
            stName, attrs, symEvent.EventType.CLRType
        );
        Debug.Assert(bld != null);
        
        return null;
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.SetCLRMethodType
    *
    \***************************************************************************/
    public virtual  System.Reflection.PropertyInfo 
    CreateCLRProperty(
        SymbolEngine.PropertyExpEntry symProperty
    )
    {
        string stName = symProperty.Name;
        
        // Get class that we're defined in
        TypeBuilder bldClass = symProperty.SymbolClass.CLRType as TypeBuilder;
        Debug.Assert(bldClass != null);
        
        PropertyAttributes attrs = PropertyAttributes.None;
                
        // Create the property builder
        PropertyBuilder bld = bldClass.DefineProperty(
            stName,
            attrs, 
            symProperty.PropertyType.CLRType,
            new Type[0]
            
        );
        
        // Create builders for the get / set accessors        
        if (symProperty.SymbolGet != null)
        {
            symProperty.SymbolGet.SetInfo(this);
            bld.SetGetMethod(symProperty.SymbolGet.Info as MethodBuilder);
        }
        
        if (symProperty.SymbolSet != null)
        {
            symProperty.SymbolSet.SetInfo(this);
            bld.SetSetMethod(symProperty.SymbolSet.Info as MethodBuilder);
        }
        
        return bld;
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.SetCLRMethodType
    *
    \***************************************************************************/
    
    public virtual  System.Reflection.MethodBase 
    CreateCLRMethod(
        SymbolEngine.MethodExpEntry symBlueMethod
    )
    {
        // Call DefineMethod on the TypeBuilder to get a MethodBuilder
        // MethodBuilder derives from MethodInfo
        string stMethodName         = symBlueMethod.Name;

        Debug.Assert(symBlueMethod.Node != null);
        
        AST.Modifiers mods = symBlueMethod.Node.Mods;        
        MethodAttributes attrMethod = (MethodAttributes) 0;
        
        // Everything seems to have this; but what does it do?
        attrMethod = MethodAttributes.HideBySig;
                
        if (mods.IsPublic)
            attrMethod |= MethodAttributes.Public;
        else if (mods.IsProtected)
            attrMethod |= MethodAttributes.Family;
        else if (mods.IsPrivate)
            attrMethod |= MethodAttributes.Private;
        else if (mods.IsInternal)
            attrMethod |= MethodAttributes.Assembly;
            
        
        if (symBlueMethod.IsSpecialName)
            attrMethod |= MethodAttributes.SpecialName;
            
        if (mods.IsStatic)
        {
            attrMethod |= MethodAttributes.Static;
        }
        
        if (mods.IsVirtual)
            attrMethod |= MethodAttributes.Virtual | MethodAttributes.NewSlot;
        
        if (mods.IsAbstract)
            attrMethod |= MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot;
                
        if (mods.IsOverride)
        {
            attrMethod |= MethodAttributes.Virtual;
        }
        
        if (mods.IsSealed)
            attrMethod |= MethodAttributes.Final;
        
        
        
        // Overloaded operators have a magical bit sit
        if (symBlueMethod.Node.IsOpOverload)
            attrMethod |= MethodAttributes.SpecialName;
                
        // Get signature from the parameters        
        #if false
        Type [] alParamTypes = new Type[symBlueMethod.ParamCount];            
        for(int iParam = 0; iParam < symBlueMethod.ParamCount; iParam++)
        {            
            alParamTypes[iParam] = symBlueMethod.ParamCLRType(iParam);
        }        
        #else
        Type [] alParamTypes = symBlueMethod.ParamTypes(false);
        #endif       
        
        // Get class that we're defined in
        TypeBuilder bldClass = symBlueMethod.SymbolClass.CLRType as TypeBuilder;
        Debug.Assert(bldClass != null);
        
        bool fIsCtor = symBlueMethod.IsCtor;
        
        System.Reflection.MethodBase bld = null;           
                
        // Get the builder        
        if (fIsCtor)
        {   
            
            ConstructorBuilder bldCtor = bldClass.DefineConstructor(attrMethod, CallingConventions.Standard, alParamTypes);    

            if (AST.DelegateDecl.IsDelegate(symBlueMethod.SymbolClass.CLRType))
            {
                bldCtor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);                
            }

            //return bldCtor;        
            bld = bldCtor;
        }
        else 
        {                        
            Type typeReturn = symBlueMethod.RetType.CLRType;
            Debug.Assert(typeReturn != null, "Semantic check should catch null return type");
            
            MethodBuilder bldMethod = bldClass.DefineMethod(stMethodName, attrMethod, typeReturn, alParamTypes);
            
            // The members of a delegate are special because they're implemented by the runtime.
            if (AST.DelegateDecl.IsDelegate(symBlueMethod.SymbolClass.CLRType))
            {
                bldMethod.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
            }
            
            
            //return bldMethod;
            bld = bldMethod;
        }
        
        
        // Double check that we set the attributes on the new CLR method properly.
        // Do this by converting back to blue modifier from CLR method and comparing
        // that modifier to the original.        
        
        #if DEBUG
        AST.Modifiers modsCheck = new AST.Modifiers(bld);
        string stPretty = symBlueMethod.PrettyDecoratedName;
        string stBlue = mods.ToString();
        string stCLR = modsCheck.ToString();
        
        // The 'new' modifier just tells the C# compiler to suppress warnings. There's
        // no IL bit for it, so we lose it when we roundtrip. So just add it to both
        // of them.
        mods.SetNew();
        modsCheck.SetNew();
        Debug.Assert(modsCheck == mods, "Modifer mismatch in creating CLR method");
        #endif
        
        return bld;
        
    }
    
#endregion
    

#region Implementation for Codegen Driver
    //-------------------------------------------------------------------------
    // Helper class called when a resolve type event is fired.
    // This is called when we CreateType() and it needs to also create an inner
    // type.
    //-------------------------------------------------------------------------
        
    public Assembly ResolveCreateType(Object sender, ResolveEventArgs args)
    {
        Log.WriteLine(Log.LF.CodeGen, "... got TypeResolveEvent for '{0}'", args.Name);
        
        // Get the full name of whoever originally called CreateType()
        string stParent = (string) m_CreateTypeStack.Peek();
                   
        string stName = stParent + "+" + args.Name;
        //string stName = args.Name;
        
        #if false
        // Debugging help
        Type [] ts = m_bldModule.GetTypes();
        foreach(Type t2 in ts)
        {
            Console.WriteLine("{0},{1}", t2.FullName, t2.GetType());            
        }
        #endif
    
        // ...
        // The ResolveCreateType() event is fundamentally broken for Emit in V1 of .NET.
        // The args just give us the short name of a class (ex 'B'). So we don't know 
        // if the class is a nested (really 'A+B') or a value type field.
        // Since we get an ambiguous name, we have to guess what it really is
        // But we may guess wrong. It could be either a nested class or a valuetype field.
        // Assume the nested class (more common) and assert elsewise. 
        // Until this dumb bug is fixed, we can't do any better.        
        //
        // What this means is that we can't build nested-types with the same flexibility as
        // CSC.exe. (JScript, which uses Emit, also ahas the same problem).
        Type t = ModuleBuilder_GetType(m_bldModule, stName);
        Debug.Assert(t != null, "@todo - Can't find class '" + stName + "'. This may be due to a bug in emit regarding nested classes.");
            
        // If the type isn't already baked, then bake it.        
        System.Reflection.Emit.TypeBuilder tb = t as System.Reflection.Emit.TypeBuilder;
        if (tb != null)
        {
            CreateType(tb);
        } else {
            Log.WriteLine(Log.LF.CodeGen, "'{0}' already created.", stName);
        }
        
        
        // We're suppose to return the assembly that the type was found in.
        // That's just the assembly we're creating.
        return this.m_bldModule.Assembly;
    }
    
    
    //-------------------------------------------------------------------------
    // Given an array of the assemblies that we reference (not including mscorlib)
    // return a CLRTypeProvider for symbol resolution to use.
    //-------------------------------------------------------------------------
    public virtual ICLRtypeProvider GetProvider(Assembly [] assemblyRefs)
    {
        SetImportedAssemblies(assemblyRefs);
        return this;
    }

    /***************************************************************************\
    *
    * EmitCodeGen.BeginOutput
    *
    * BeginOutput() initializes CodeGen for output to a specific file.
    *
    \***************************************************************************/
    
    public virtual void
    BeginOutput(       
        string [] stFilenames)
    {
        // We know that all of the options have been processed by this point.
        
        // If no output filename was supplied via /out, then use the first
        // name in the stFilenames list with the appropriate extension
        if (m_stOutputName == null)
        {
            string stDefault = stFilenames[0];
            
            string stExt = "";
            switch(m_TargetType)
            {
                case TargetType.Dll:
                    stExt = "dll"; break;
                case TargetType.Console:
                case TargetType.Windows:
                    stExt = "exe"; break;
            }
            
            m_stOutputName = System.IO.Path.ChangeExtension(stDefault, stExt);
        }
        
        string stShortName = System.IO.Path.GetFileNameWithoutExtension(m_stOutputName);
       
            
        m_alClasses         = new ArrayList();
                                    
		m_domain            = Thread.GetDomain();
        if (m_domain == null)
        {
            throw new AppDomainUnloadedException("Unable to retrieve AppDomain");
        }


        //
        // Setup the AssemblyName
        //

		AssemblyName asmName = new AssemblyName();

		// Note that we must exclude the extension or fusion gets confused.
		// Specifiy the full filename when we save
               
		asmName.Name = stShortName;


        //
        // Create a ModuleBuilder for our output executable file
        //

		//m_bldAssembly = m_domain.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.RunAndSave);
		m_bldAssembly = m_domain.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Save);
        Debug.Assert(m_bldAssembly != null);

		m_bldModule = m_bldAssembly.DefineDynamicModule(m_stOutputName, m_stOutputName, m_fDebugInfo);
        Debug.Assert(m_bldModule != null);


        // Hook up the event listening.        
        m_resolveHandler = new ResolveEventHandler(this.ResolveCreateType);
        m_domain.TypeResolve += m_resolveHandler;


        // Create all the PDB documents
        if (m_fDebugInfo)
        {
            // @todo - can we totally replace m_symWriter w/ emit?
            m_symWriter = m_bldModule.GetSymWriter();
            Guid g = Guid.Empty;
            
            m_stFilenames = stFilenames;
            m_symDocs = new ISymbolDocumentWriter[stFilenames.Length];
            for(int i = 0; i < stFilenames.Length; i++)
            {            
                m_symDocs[i] = 
                    m_symWriter.DefineDocument(stFilenames[i], 
                    g, 
                    g, 
                    g);
                Debug.Assert(m_symDocs[i] != null);                    
            }
        }
        
        // Get some standard hooks
        // Codegen for typeof(t) must call 'System.Type::GetTypeFromHandle()'
        // So get the methodinfo for that ahead of time.
        System.Type t = typeof(Type);        
        m_infoGetTypeFromHandle = t.GetMethod("GetTypeFromHandle");
        Debug.Assert(m_infoGetTypeFromHandle != null);
    }

    // Lookup a string filename to get the PDB document info    
    void SetCurrentDebugDocument(FileRange location)
    {
        // Nothing to do if we're not setting debug information.
        if (!m_fDebugInfo)
            return;
            
        string stFilename = location.Filename;
        
        // We know m_symDocs & m_stFilenames are parallel arrays.
        // So search the string array and return from the doc array.
        for(int i = 0; i < m_stFilenames.Length; i++)
        {
            if (m_stFilenames[i] == stFilename)
            {
                m_symCurrentDoc = m_symDocs[i];
                return;
            }
        }
        
        Debug.Assert(false, "Unknown document:" + stFilename);        
    }


    //-------------------------------------------------------------------------
    // Actually do the codegen, given the root of the tree
    //-------------------------------------------------------------------------
    public virtual void DoCodeGen(AST.ProgramDecl root)
    {
        this.Generate(root);
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.EndOutput
    *
    * EndOutput() commits any outstanding data and generates the module and assembly.
    *
    \***************************************************************************/

    public virtual void
    EndOutput()
    {
        //
        // Setup an entry point
        //

        PEFileKinds kind;
        switch (m_TargetType)
        {
        case TargetType.Console:
            kind = PEFileKinds.ConsoleApplication;
            break;

        case TargetType.Windows:
            kind = PEFileKinds.WindowApplication;
            break;

        case TargetType.Dll:
            kind = PEFileKinds.Dll;
            break;

        default:
            throw new ArgumentOutOfRangeException("Unknown Target type");
        }

        //
        // Find Main() entry point for an .exe
        //
        if (m_TargetType == TargetType.Console || m_TargetType == TargetType.Windows)
        {   
            MethodInfo miMain = null;
        
            if (m_stMainClass != null)
            {
                // A class was specified containing the main method
                Type tEntry = this.m_bldModule.GetType(m_stMainClass);
                if (tEntry == null)
                {
                    PrintError_EntryClassNotFound(m_stMainClass);
                }
                miMain = tEntry.GetMethod("Main");
                if (miMain != null)
                {
                    // Ensure that the main method is valid.
                    if (!miMain.IsStatic)
                        miMain = null;
                }
                
            } else {
                // No explicit class specified, search for main method
                // It's an error if there are multiple valid mains.                
                foreach (Type typeClass in m_alClasses)
                {
                    MethodInfo miTemp = typeClass.GetMethod("Main", 
                            BindingFlags.Static | BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.NonPublic);

                    if (miTemp != null)
                    {
                        if (miMain == null)
                        {
                            // Main has not already been found, so store this first copy.
                            miMain = miTemp;
                        }
                        else
                        {                            
                            // Main has already been found, so we may be using the wrong one.
                            this.PrintError_DuplicateMain();
                        }
                    }
                }
            } // end search
            
            // By now, we'd better have a main method;
            if (miMain == null)
            {                    
                this.PrintError_NoMain();
            } 
            else 
            {
                m_bldAssembly.SetEntryPoint(miMain, kind);
            }
            
        } // set Main() entry point

        

        //
        // Save to a file
        // This will also do a whole bunch of verfication / Checks. If we have errors
        // elsewhere, this may throw an exception
        
        try
        {    
            Log.WriteLine(Log.LF.CodeGen, "About to save Assembly:" + m_stOutputName);
            m_bldAssembly.Save(m_stOutputName);
            Log.WriteLine(Log.LF.CodeGen, "Save completed successfully");
        }
        // IO exceptions are ok. For example, we can't write to the file
        // But anything else means that we made a mistake somewhere in codegen / resolution.
        catch(System.IO.IOException e)
        {
            PrintError_IOException(e.Message);
        }
        
        
        // Unhook listeners
        m_domain.TypeResolve -= m_resolveHandler;
        m_resolveHandler = null;
        
    }

#endregion Driver

#region Methods to do Generation

#region Generate Helpers
//-----------------------------------------------------------------------------
// These are emit helpers that let us abstract various emit decisions.
//-----------------------------------------------------------------------------

    // Emit a this pointer onto the stack
    void EmitThisRef()
    {    
        m_ilGenerator.Emit(OpCodes.Ldarg_0); // load a 'this' pointer
    }
    
    
    void EmitNonPolymorphicCall(SymbolEngine.MethodExpEntry sym)
    {
        Debug.Assert(sym != null);

        MethodInfo mdInfo = sym.Info as MethodInfo;
        Debug.Assert(mdInfo != null);
        
        m_ilGenerator.Emit(OpCodes.Call, mdInfo);          
    }
    
    // Emit a call/callvirt to the specified symbol
    void EmitCall(SymbolEngine.MethodExpEntry sym)
    {   
        Debug.Assert(sym != null);

        MethodInfo mdInfo = sym.Info as MethodInfo;
        Debug.Assert(mdInfo != null);
        
        if (mdInfo.IsVirtual && !sym.SymbolClass.CLRType.IsValueType)
        {
            m_ilGenerator.Emit(OpCodes.Callvirt, mdInfo);
        } 
        else 
        {
            m_ilGenerator.Emit(OpCodes.Call, mdInfo);
        }    
    }
    
    // Do a return by jumping to a standard label
    void EmitReturn()
    {
        if (m_cTryDepth == 0)
            m_ilGenerator.Emit(OpCodes.Br, m_lblReturn);
        else
            m_ilGenerator.Emit(OpCodes.Leave, m_lblReturn);
    }
    
    // Since Enter/Exit must be paired up, return an int from Enter
    // that we pass to Exit to ensure a match
    int EnterProtectedBlock()
    {
        m_cTryDepth++;
        return m_cTryDepth;
    }
    void ExitProtectedBlock(int iOldDepth)
    {           
        Debug.Assert(m_cTryDepth > 0);
        Debug.Assert(m_cTryDepth == iOldDepth);
        m_cTryDepth--;
    }
    
    // Emit the proper form of ldarg
    void Emit_Ldarg(int iSlot)
    {
        OpCode code = OpCodes.Ldarg;
        switch(iSlot)
        {
        case 0: code = OpCodes.Ldarg_0; break;
        case 1: code = OpCodes.Ldarg_1; break;
        case 2: code = OpCodes.Ldarg_2; break;
        case 3: code = OpCodes.Ldarg_3; break;        
        }
        if (4 <= iSlot && iSlot <= 255)
            code = OpCodes.Ldarg_S;
        
        if (iSlot < 4)
            m_ilGenerator.Emit(code);
        else 
            m_ilGenerator.Emit(code, iSlot);
        
    }
    
    void Emit_Ldloc(System.Reflection.Emit.LocalBuilder l)
    {        
       m_ilGenerator.Emit(OpCodes.Ldloc_S, l);        
    }
    
    void Emit_LdlocA(System.Reflection.Emit.LocalBuilder l)
    {        
        m_ilGenerator.Emit(OpCodes.Ldloca_S, l);        
    }
    
    void Emit_Starg(int iSlot)
    {        
    // @dogfood - handle bytes
        m_ilGenerator.Emit(OpCodes.Starg_S, (int) iSlot);
    }
    
    void Emit_Stloc(System.Reflection.Emit.LocalBuilder l)
    {
        m_ilGenerator.Emit(OpCodes.Stloc_S, l);    
    }
    
    // Emit the proper load-indirect opcode, depending on our type
    void Emit_Ldi(System.Type t)
    {
        if (t== typeof(int) || t.IsEnum)
        {
            m_ilGenerator.Emit(OpCodes.Ldind_I4);
        } 
        
        else if (t == typeof(char))
        {
            m_ilGenerator.Emit(OpCodes.Ldind_U2);        
        }
        
        else if (t.IsValueType)
        {
            m_ilGenerator.Emit(OpCodes.Ldobj, t);
        } 
        else 
        {
            m_ilGenerator.Emit(OpCodes.Ldind_Ref);
        }                
    }
    
    // emit proper store-indirect opcode
    void Emit_Sti(System.Type t)
    {               
        if (t == typeof(int) || t.IsEnum)
        {
            m_ilGenerator.Emit(OpCodes.Stind_I4);
        } 
        
        else if (t == typeof(char))
        {
            m_ilGenerator.Emit(OpCodes.Stind_I2);        
        }
        else if (t.IsValueType)
        {
            m_ilGenerator.Emit(OpCodes.Stobj, t);
        } 
        else 
        {
            m_ilGenerator.Emit(OpCodes.Stind_Ref);            
        }    
    }
    
    // Emit the proper Stelem flavor, depending on type
    void Emit_Stelem(System.Type tElem)
    {        
        if ((tElem == typeof(int)) || tElem.IsEnum)
        {
            m_ilGenerator.Emit(OpCodes.Stelem_I4);            
        } 
        
        else if (tElem == typeof(char))
        {
            m_ilGenerator.Emit(OpCodes.Stelem_I2);
        } 
        
        else 
        {
            Debug.Assert(!tElem.IsValueType, "Woah! Stelem for structs? Is that possible?");
            m_ilGenerator.Emit(OpCodes.Stelem_Ref);
        }
    }
    
    void Emit_Ldelem(System.Type tElem)
    {
        if ((tElem == typeof(int)) || tElem.IsEnum)
        {
            m_ilGenerator.Emit(OpCodes.Ldelem_I4);
        } 
        
        else if (tElem == typeof(char))
        {
            m_ilGenerator.Emit(OpCodes.Ldelem_U2);
        }
        else if (tElem.IsValueType)
        {
            m_ilGenerator.Emit(OpCodes.Ldelema, tElem);
            m_ilGenerator.Emit(OpCodes.Ldobj, tElem);
        } else
        {
            Debug.Assert(!tElem.IsValueType);
            m_ilGenerator.Emit(OpCodes.Ldelem_Ref);
        }
    }
    
    /***************************************************************************\
    * 
    * EmitCodeGen.GenerateBoxable
    * 
    * If we're generating a value type and it will be assigned to an object,
    * then we need to box it. This is used with RHS expressions.
    \**************************************************************************/
    public void
        GenerateBoxable(
        AST.Exp exp,
        System.Type tTarget
        )
    {        
        Debug.Assert(tTarget != null);

        System.Type tSource = exp.CLRType;

        exp.GenerateAsRight(this);
    
        // Check if we need to box.
        // Null expressions don't have a clr type. But they're not value
        // types so they don't have to be boxed.        
        if (tSource != null)
        {
            // Strip off references to look at base types
            if (tSource.IsByRef)
                tSource = tSource.GetElementType();
            if (tTarget.IsByRef)
                tTarget = tTarget.GetElementType();                
                
                
            if (tSource.IsValueType && !tTarget.IsValueType)
                m_ilGenerator.Emit(OpCodes.Box, tSource);
        } 
        else 
        {
            // Make sure this really is a null and not just an unresolved type
            // Note that ArgExp can be null, but can't be boxed. 
            Debug.Assert(AST.Exp.CanBeNullType(exp));
        }

    }
    
#endregion

#region Generate for Decls
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the program defined by the given node.
    *
    \***************************************************************************/

    public void
    Generate(
        AST.ProgramDecl nodeProgram)
    {
        // Generate all types.      
        Log.WriteLine(Log.LF.CodeGen, "***Generate bodies for all types");
        AST.TypeDeclBase [] al = nodeProgram.Classes;
        foreach (AST.TypeDeclBase t in al)
        {
            t.GenerateType(this);
        } 
        
        Log.WriteLine(Log.LF.CodeGen, "***Finished generating bodies for types. About to call CreateType()");
        
        // Now that the bodies have all been generating, the final thing we 
        // need to do is 'bless' all the types. Do this by calling CreateType()
        // on everything. 
        // We have to separate the CreateType phase from the GenerateBody
        // because CreateType may have to call itself recursively, and so
        // we'll need all the bodies to be created.
        foreach (AST.TypeDeclBase t in al)
        {
            TypeBuilder bld = (TypeBuilder) t.CLRType;
            Debug.Assert(bld != null);
            
            CreateType(bld);
        }        
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the class defined by the given node.
    *
    \***************************************************************************/
    public void
        GenerateEnum(AST.EnumDecl node)
    {
        
        // Reflection-Emit requires that we 'bless' the enum via CreateType()
#if Bug_In_EnumBuilder
        TypeBuilder bld = (TypeBuilder) node.Symbol.CLRType;
#else
        EnumBuilder bld = (EnumBuilder) node.Symbol.CLRType;
#endif        
    }

    // ...
    // Account for another emit/loader bug. The type resolve event only give us
    // the short name. So keep a stack to get the full context.
    // When this bug is fixed, get rid of this variable.
    System.Collections.Stack m_CreateTypeStack = new System.Collections.Stack();


    //-----------------------------------------------------------------------------
    // Finalize the type and return the baked instance. This should never
    // return null. This can be called recursively.
    // We only bake a type once.
    //-----------------------------------------------------------------------------
    System.Type CreateType(TypeBuilder bld)
    {   
        string stName = bld.FullName;     
        
        // This should never throw an exception, but we catch it for debugging purposes.
        System.Type typeNewClass = null;
        
        // Since this is recursive, we may have already created the type. So check
        #if true       
        Type tCheck = ModuleBuilder_GetType(m_bldModule, stName);
        if (!(tCheck is TypeBuilder))
        {
            Log.WriteLine(Log.LF.CodeGen, "'{0}' already created..", stName);
            return typeNewClass;
        }
        #endif
                
        try
        {            
            Log.WriteLine(Log.LF.CodeGen, "About to call CreateType() for '{0}'", stName);
            
            // This stack is a bad hack to deal with a stupid Reflection-emit bug.            
            m_CreateTypeStack.Push(bld.FullName);            
            
            // This may fire a TypeResolve event which may turn around and call CreateType() again            
            typeNewClass = bld.CreateType();
            
            string s = (string) m_CreateTypeStack.Pop();
            Debug.Assert(s == bld.FullName);
            
            Log.WriteLine(Log.LF.CodeGen, "Type '{0}' fully created", stName);
        }
        
        catch(System.Exception e)
        {
            // Should never happen.
            // A very common cause for this failure is an bug in blue where
            // we fail to put an implied 'virtual' modifier on a method.Then the method doesn't
            // match in interface mapping and it looks like it wasn't implemented.
            Log.WriteLine(Log.LF.CodeGen, "CreateType() failed on '{0}' with exception:{1}", stName, e);
            throw;
        } 
        
        Debug.Assert(typeNewClass != null);
        Debug.Assert(!(typeNewClass is TypeBuilder));
        return typeNewClass;
    }


    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the class defined by the given node.
    *
    \***************************************************************************/

    public void
    GenerateClass(
        AST.ClassDecl nodeClass)
    {
        //
        // Determine information about the base class:
        // - If the base-class is not already generated, need to generate it now.
        // - Get the System.Type of the base class.
        //
        string stName = nodeClass.Symbol.FullName;
        Log.WriteLine(Log.LF.CodeGen, "Begin generating class '{0}' [", stName);

        //
        // Begin the new class definition.
        //
                
        // Set the current class builder        
        TypeBuilder bldClass = nodeClass.Symbol.CLRType as TypeBuilder;
        Debug.Assert(bldClass != null);
        
        
        // Set the debugger document
        SetCurrentDebugDocument(nodeClass.Location);
        
        //
        // Generate the member methods.
        //

        if (!nodeClass.IsInterface)
        {
            AST.MethodDecl [] al = nodeClass.Methods;
            foreach (AST.MethodDecl nodeMethod in al)
            {
                if (!nodeMethod.Mods.IsAbstract) 
                {
                    Generate(nodeMethod);
                }
            }
            
            foreach (AST.PropertyDecl nodeProp in nodeClass.Properties)
            {
                Generate(nodeProp);
            }
        }

        Log.WriteLine(Log.LF.CodeGen, "] end body for '{0}'", stName);
        //
        // End the class definition.
        //

        /*            
        // This may catch a lot of problems (because it invokes the loader). 
        Type typeNewClass = null;
        
        try
        {   
            Log.WriteLine(Log.LF.CodeGen, "] About to call CreateType() on '{0}'", stName);
            typeNewClass = bldClass.CreateType();
            Log.WriteLine(Log.LF.CodeGen, "Type '{0}' fully created", stName);
        }
        catch(System.Exception e)
        {
            Log.WriteLine(Log.LF.CodeGen, "CreateType() failed on '{0}' with exception:{1}", stName, e);            
            throw;
        }
        Debug.Assert(typeNewClass != null, "Must have successfully created new class");
        
        */
        //m_alClasses.Add(typeNewClass);
        m_alClasses.Add(bldClass);

        //m_bldClass = null;
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the method defined by the given node.
    *
    \***************************************************************************/
    public void
    Generate(
        AST.PropertyDecl nodeProp
    )
    {
        nodeProp.Generate(this);    
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the method defined by the given node.
    *
    \***************************************************************************/

    public void
    Generate(
        AST.MethodDecl nodeMethod)
    {
            
        // Delegate impls have no body....
        
        if (AST.DelegateDecl.IsDelegate(nodeMethod.Symbol.SymbolClass.CLRType))
        {                        
            return;
        }
        
        
        //
        // Begin a new method definition.
        //

        SetCurrentMethod(nodeMethod);

        string stMethodName         = nodeMethod.Name;
        SymbolEngine.MethodExpEntry symbol
                                    = nodeMethod.Symbol;
        
        
        Log.WriteLine(Log.LF.CodeGen, "Generating body for method '{0}' of class '{1}'", stMethodName, symbol.SymbolClass.FullName);
        
        Debug.Assert(!symbol.SymbolClass.IsInterface, "Don't codegen method bodies for interfaces");
        Debug.Assert(!symbol.Node.Mods.IsAbstract, "Don't codegen abstract methods");

        //
        // Get signature from the parameters
        //
        
        int iParam = 0;
        
        AST.ParamVarDecl [] alParamDecls = nodeMethod.Params;
        /*
        Type [] alParamTypes = new Type[alParamDecls.Length];
        
        foreach(AST.ParamVarDecl nodeParamDecl in alParamDecls)
        {
            SymbolEngine.ParamVarExpEntry sym = nodeParamDecl.ParamSymbol;
            alParamTypes[iParam] = sym.m_type.CLRType;
            iParam++;
        }
                
        */
                       
        ConstructorBuilder bldCtor = null;
        MethodBuilder bldMethod = null;
        // get the generator for either a Ctor or a method
        if (symbol.IsCtor)
        {            
            bldCtor = symbol.Info as ConstructorBuilder;
            Debug.Assert(bldCtor != null);            
            m_ilGenerator = bldCtor.GetILGenerator();
                        
        }
        else
        {
        
            Type typeReturn = symbol.m_type.CLRType;
            Debug.Assert(typeReturn != null, "Semantic check should catch null return type");
        
            bldMethod = symbol.Info as MethodBuilder;
            Debug.Assert(bldMethod != null);
            
            m_ilGenerator   = bldMethod.GetILGenerator();

        }
        
        // Now, doesn't matter if we're a ctor or a method. Just need an ilgenerator
        Debug.Assert(m_ilGenerator != null);
        Debug.Assert((bldCtor == null) ^ (bldMethod == null));

        //
        // Begin a new scope.
        //

        //
        // Assign builders & slots for parameters
        //
        
        // Note that CLR reserves parameter slot 0 for 'this' pointer
        // So start number the user parameters at 1
        if (nodeMethod.Mods.IsStatic)
        {
            //Debug.Assert(!symbol.IsCtor, "Static ctors are not supported");
            iParam = 0;
        }
        else 
        {
            iParam = 1;
        }
                
        int i = 1;
        foreach(AST.ParamVarDecl nodeParamDecl in alParamDecls)
        {
            SymbolEngine.ParamVarExpEntry sym = nodeParamDecl.ParamSymbol;
            
            // Set parameter attributes. These are rather counter-intuitive, but
            // this is what C# sets them too.
            ParameterAttributes attrs = ParameterAttributes.None;
            switch (nodeParamDecl.Flow)
            {
            case AST.EArgFlow.cIn:      attrs = ParameterAttributes.None;   break;
            case AST.EArgFlow.cOut:     attrs = ParameterAttributes.Out;    break;
            case AST.EArgFlow.cRef:     attrs = ParameterAttributes.None;   break;
            }
            
            ParameterBuilder p1 = (symbol.IsCtor) ?                
                bldCtor.DefineParameter(i, attrs, sym.Name) :
                bldMethod.DefineParameter(i, attrs, sym.Name) ;
                
            
            
            sym.Builder = p1;
            sym.CodeGenSlot = iParam;
        
            i++;
            iParam++;
        }

        // All returns are really just a jump to the end where we have a single
        // common return.        
        m_lblReturn = m_ilGenerator.DefineLabel();
        
        //
        // Assign builders & slots for locals. Resolve variables in nested scopes
        // here by just flattening them all to be in a single scope and then
        // giving them each unique slots. This is not optimal since we 
        // don't reuse slots for vars in disjoint scopes.
        // @todo - how do we want to resolve vars in nested scopes?
        // @todo - How should SymbolResolution & Codegen split the responsibilities
        //          for nested vars? Currently, it's all on codegen.
        //
        int iLocalSlot = 0;
        AssignLocalSlots(nodeMethod.Body, ref iLocalSlot);

        // Assign a localvar to hold the return value.
        // This way we can return from inside an exception-block        
        bool fHasReturn = !nodeMethod.Symbol.IsCtor && (nodeMethod.Symbol.RetType.CLRType != typeof(void));
        
        if (fHasReturn)
        {
            m_localReturnValue = m_ilGenerator.DeclareLocal(nodeMethod.Symbol.RetType.CLRType);
        } else {
            m_localReturnValue = null;
        }
        
        // When we return, we need to know whether to do a 'br' or a 'leave'.
        // So maintain a counter of how deep in the try/catch/finally blocks we are.
        // If the depth==0, then 'br' is ok, else we have to use 'leave'
        m_cTryDepth = 0;
        


        //
        // Generate each of the statements in the method.
        //

        AST.Statement[] al = nodeMethod.Body.Statements;
        foreach (AST.Statement nodeStatement in al)
        {
            nodeStatement.Generate(this);
            Debug.Assert(m_cTryDepth == 0);
        }


        //
        // End the scope.
        //


        // Generate end-of-method.
        m_ilGenerator.MarkLabel(m_lblReturn);
        if (fHasReturn)
        {
            this.Emit_Ldloc(m_localReturnValue);
        }
        
        // Mark the last character of the methoddecl for the implied return
        FileRange f = nodeMethod.Location;
        //this.MarkSequencePoint(new FileRange(f.Filename, f.RowEnd, f.ColEnd));
        m_ilGenerator.Emit(OpCodes.Ret);
    
        m_ilGenerator = null;

        SetCurrentMethod(null);
    }
#endregion

#region Generate Variable slot assignments 
    /***************************************************************************\
    * EmitCodeGen.AssignLocalSlots
    * 
    * 
    * Recursive function to assign slots/builders to all local variables.
    * iLocalSlot is a counter for the current slot number and must be unique
    * for each local variable. So start it at 0 and let it keep going up.
    \***************************************************************************/

    protected void 
    AssignLocalSlots(
        AST.BlockStatement block,
        ref int iLocalSlot
    )
    {
        if (block == null)
            return;
            
        AST.LocalVarDecl [] alLocalDecls = block.Locals;
           
        // Generate slots/builders for all locals in this block
        foreach(AST.LocalVarDecl nodeLocalDecl in alLocalDecls)
        {
            SymbolEngine.LocalVarExpEntry sym = nodeLocalDecl.LocalSymbol;
            LocalBuilder l1 = m_ilGenerator.DeclareLocal(sym.m_type.CLRType);

            if (m_fDebugInfo)
            {
                l1.SetLocalSymInfo(sym.Name);
            }
            
            sym.Builder = l1;
            //sym.CodeGenSlot = iLocalSlot;
            
            iLocalSlot++;
        }

        // Call on all nested blocks
        AST.Statement [] stmtList = block.Statements;
        foreach(AST.Statement s in stmtList)
        {
            
            AssignLocalSlots(s, ref iLocalSlot);            
        }
    }
    
    protected void 
    AssignLocalSlots(
        AST.Statement s,
        ref int iLocalSlot
    )
    {
        if (s == null)
            return;
            
        AST.BlockStatement blockNested = s as AST.BlockStatement;
        if (blockNested != null)
        {
            AssignLocalSlots(blockNested, ref iLocalSlot);
            return;
        }
        
        // @todo - this is a total hack. We special case the different AST types.
        // Need to decide a good way to resolve nested locals            
        AST.IfStatement s2 = s as AST.IfStatement;
        if (s2 != null)
        {               
            AssignLocalSlots(s2.ThenStmt, ref iLocalSlot);            
            AssignLocalSlots(s2.ElseStmt, ref iLocalSlot);
            return;
        }
        
        AST.LoopStatement s3 = s as AST.LoopStatement;
        if (s3 != null)
        {
            AssignLocalSlots(s3.BodyStmt, ref iLocalSlot);
            return;
        }
        
        AST.TryStatement s4 = s as AST.TryStatement;
        if (s4 != null)
        {
            AssignLocalSlots(s4.TryStmt, ref iLocalSlot);
            foreach(AST.CatchHandler c in s4.CatchHandlers)
                AssignLocalSlots(c.Body, ref iLocalSlot);
            AssignLocalSlots(s4.FinallyStmt, ref iLocalSlot);        
        }
        
        AST.ForeachStatement s5 = s as AST.ForeachStatement;
        if (s5 != null)
        {
            AssignLocalSlots(s5.ResolvedStmt, ref iLocalSlot);
        }
        
        AST.SwitchStatement s6 = s as AST.SwitchStatement;
        if (s6 != null)
        {
            AssignLocalSlots(s6.ResolvedStatement, ref iLocalSlot);
        }
        
    }
#endregion


#region Generate Statements
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/

    public void
    Generate(
        AST.ReturnStatement nodeStmt)
    {        
        MarkSequencePoint(nodeStmt.Location);
        
        AST.Exp exp = nodeStmt.Expression;
        if (exp != null)
        {            
            //exp.Generate(this);
            AST.MethodDecl m = GetCurrentMethod();
            Debug.Assert(m != null);

            GenerateBoxable(exp, m.Symbol.RetType.CLRType);
            this.Emit_Stloc(m_localReturnValue);
        }
        
        EmitReturn();
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/
    public void
    Generate(
        AST.WhileStatement nodeStmt
    )
    {
#if false
// while(TestExp) BodyStmt;
       
        lContinue:
            [TestExp]
            brfalse l2;
            [BodyStmt]
            goto l1;
        lBreak:
#endif        
        Label lContinue = m_ilGenerator.DefineLabel();
        Label lBreak = m_ilGenerator.DefineLabel();

        m_ilGenerator.MarkLabel(lContinue);
        MarkSequencePoint(nodeStmt.TestExp.Location);
        nodeStmt.TestExp.GenerateAsRight(this);
        m_ilGenerator.Emit(OpCodes.Brfalse, lBreak);

        LoopFrame f = new LoopFrame(lBreak, lContinue);
        PushLoopFrame(f);

        nodeStmt.BodyStmt.Generate(this);
        m_ilGenerator.Emit(OpCodes.Br, lContinue);

        PopLoopFrame(f);

        m_ilGenerator.MarkLabel(lBreak);
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/
    public void
        Generate(
        AST.DoStatement nodeStmt
        )
    {
#if false
// do BodyStmt while (TestExp);
       
        l1:            
            [BodyStmt]
        lContinue:
            [TestExp]
            brtrue l1;        
        lBreak:
#endif
        Label l1 = m_ilGenerator.DefineLabel();
        Label lContinue = m_ilGenerator.DefineLabel();
        Label lBreak = m_ilGenerator.DefineLabel();
        

        LoopFrame f = new LoopFrame(lBreak, lContinue);
        PushLoopFrame(f);

        m_ilGenerator.MarkLabel(l1);        
        nodeStmt.BodyStmt.Generate(this);        

        PopLoopFrame(f);

        m_ilGenerator.MarkLabel(lContinue);
        MarkSequencePoint(nodeStmt.TestExp.Location);
        nodeStmt.TestExp.GenerateAsRight(this);
        m_ilGenerator.Emit(OpCodes.Brtrue, l1);

        m_ilGenerator.MarkLabel(lBreak);

        
        
    }
    
    
    
    public void Generate(AST.SwitchStatement stmt)
    {
    // Switch statement has a resolved statement,
    // but we need CodeGen to set up the loop frame so that
    // we can use the 'break' statement                        
        Label lBreak = m_ilGenerator.DefineLabel();
        Label lContinue = lBreak; // Continue should never be used.
    
        LoopFrame f = new LoopFrame(lBreak, lContinue);
        PushLoopFrame(f);
        
        // Now generate the proxy
        stmt.ResolvedStatement.Generate(this);
                
        m_ilGenerator.MarkLabel(lBreak);
        
        
        PopLoopFrame(f);    
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/
    public void
        Generate(
        AST.ForStatement nodeStmt
        )
    {
#if false
// for(InitExp;TestExp;NextExp) S
    
        [InitExp]
    l1:
        [TestExp]
        brfalse lBreak;
        [BodyStmt]
    lContinue:
        [NextExp]        
        jmp l1;
    lBreak:
#endif
        Label l1 = m_ilGenerator.DefineLabel();
        Label lContinue = m_ilGenerator.DefineLabel();
        Label lBreak = m_ilGenerator.DefineLabel();
    
        LoopFrame f = new LoopFrame(lBreak, lContinue);
        PushLoopFrame(f);
                
        // this already marks a sequence point
        nodeStmt.InitExp.GenerateAsStatement(this);

        m_ilGenerator.MarkLabel(l1); 
        MarkSequencePoint(nodeStmt.TestExp.Location);       
        nodeStmt.TestExp.GenerateAsRight(this);
        m_ilGenerator.Emit(OpCodes.Brfalse, lBreak);
        
        nodeStmt.BodyStmt.Generate(this);        

        m_ilGenerator.MarkLabel(lContinue);
        MarkSequencePoint(nodeStmt.NextExp.Location);
        nodeStmt.NextExp.GenerateAsStatement(this);
        m_ilGenerator.Emit(OpCodes.Br, l1);
        
        m_ilGenerator.MarkLabel(lBreak);
        
        
        PopLoopFrame(f);    
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/

    public void
    Generate(
        AST.IfStatement nodeStmt)
    {   
#if false
// if (TestExp) ThenStmt
        [TestExp]
        brfalse l1;
        [ThenStmt]
    l1:
     
// if (TestExp) ThenStmt else ElseStmt
        [TestExp]
        brfalse l1;
        [ThenStmt]
        br l2;
    l1:
        [ElseStmt]
    l2:
#endif
        MarkSequencePoint(nodeStmt.TestExp.Location);
        nodeStmt.TestExp.GenerateAsRight(this);
        
        Label l1 = m_ilGenerator.DefineLabel();
        m_ilGenerator.Emit(OpCodes.Brfalse, l1);

        nodeStmt.ThenStmt.Generate(this);

        if (nodeStmt.ElseStmt == null)
        {
            // Case for no 'else'
            m_ilGenerator.MarkLabel(l1);
        } 
        else 
        {
            // case when we do have an 'else'
            Label l2 = m_ilGenerator.DefineLabel();
            m_ilGenerator.Emit(OpCodes.Br, l2);
            m_ilGenerator.MarkLabel(l1);
            
            nodeStmt.ElseStmt.Generate(this);

            m_ilGenerator.MarkLabel(l2);

        }
    }

#endregion

#region Generate LHS
    // SimplObjExp
    
    public void GenerateAsLeftPre(AST.FieldExp exp)
    {
        if (exp.CLRType.IsByRef)
            GenerateValue(exp);
        else
        {   
            if (!exp.IsStatic)
            {
                // If this is a Struct, or a Ref to a struct, then generate the address
                System.Type t = exp.InstanceExp.CLRType;
                if (t.IsValueType || 
                    (t.IsByRef && t.GetElementType().IsValueType))
                {
                    exp.InstanceExp.GenerateAddrOf(this);                                       
                } else 
                {
                    // Else just generate as a rhs. This will emit a ldi if needed
                    exp.InstanceExp.GenerateAsRight(this);
                }
            } // instance field
        }               
    } // LeftPre for FieldExp
        
    public void GenerateAsLeftPre(AST.LocalExp exp) 
    {
        if (exp.CLRType.IsByRef)
            GenerateValue(exp);
    }
    
    public void GenerateAsLeftPre(AST.ParamExp exp) 
    { 
        if (exp.CLRType.IsByRef)
            GenerateValue(exp);
    }
    
/*    
    public void GenerateAsLeftPre(AST.SimpleObjExp exp)
    {
        bool fIsRef = exp.CLRType.IsByRef;
        
        if (fIsRef)
        {
            // The variable contains an address. GenerateAsRight to push that
            // adderss on the stack.
            GenerateValue(exp);
            
            // @todo -fields, properties?
            return;
        }
        
            
        // For classes
        // Find out if we're an instance member
        // Emit 'this' reference if 
        if (exp.IsField)
        {
            SymbolEngine.FieldExpEntry f = exp.ExpType as SymbolEngine.FieldExpEntry;
            if (!f.IsStatic)
                EmitThisRef();
        } 
        
        else if (exp.IsProperty)
        {
            SymbolEngine.PropertyExpEntry p = exp.ExpType as SymbolEngine.PropertyExpEntry;
            if (!p.IsStatic)
                EmitThisRef();
        }       
    }
*/    
    
    public void GenerateAsLeftPost(AST.FieldExp exp)
    {
        if (exp.CLRType.IsByRef)
            this.Emit_Sti(exp.CLRType.GetElementType());
        else {
            if (exp.Symbol.IsStatic)                            
                m_ilGenerator.Emit(OpCodes.Stsfld, exp.Symbol.Info);             
            else                             
                m_ilGenerator.Emit(OpCodes.Stfld, exp.Symbol.Info);
        }
    }
    
    public void GenerateAsLeftPost(AST.LocalExp exp)
    {
        if (exp.CLRType.IsByRef)
            this.Emit_Sti(exp.CLRType.GetElementType());
        else 
        {
            m_ilGenerator.Emit(OpCodes.Stloc_S, exp.Symbol.Builder);
        }
    }
    
    public void GenerateAsLeftPost(AST.ParamExp exp)
    {
        if (exp.CLRType.IsByRef)
            this.Emit_Sti(exp.CLRType.GetElementType());
        else 
        {
            int iSlot = exp.Symbol.CodeGenSlot;                        
            m_ilGenerator.Emit(OpCodes.Starg_S, iSlot);            
        }
    }

/*
    // Generate the appropriate store instruction for a single identifier
    // in "a=<exp>"
    internal void GenerateAsLeftPost(AST.SimpleObjExp exp)
    {
        bool fIsRef = exp.CLRType.IsByRef;
        if (fIsRef)
        {
            Type tElem = exp.CLRType.GetElementType();            
            if (tElem == typeof(int) || tElem.IsEnum)
            {
                m_ilGenerator.Emit(OpCodes.Stind_I4);
            } else if (tElem.IsValueType)
            {
                m_ilGenerator.Emit(OpCodes.Stobj, tElem);
            } else {
                m_ilGenerator.Emit(OpCodes.Stind_Ref);            
            }
        
            return;
        }
        
        // Properties are actually a function call
        if (exp.IsProperty)
        {
            SymbolEngine.PropertyExpEntry p = exp.ExpType as SymbolEngine.PropertyExpEntry;
            EmitCall(p.SymbolSet);         
        }
        
        // Parameters use "starg"
        else if (exp.IsParam)
        {
            SymbolEngine.ParamVarExpEntry param = (SymbolEngine.ParamVarExpEntry) exp.ExpType;            
            // Unfortunately, there's no overloaded Emit() to take a ParamBuilder
            int iSlot = param.CodeGenSlot;                        
            m_ilGenerator.Emit(OpCodes.Starg_S, iSlot);
        }
            
        // Local variables use "stloc"            
        else if (exp.IsLocal)
        {
            SymbolEngine.LocalVarExpEntry symLocal = (SymbolEngine.LocalVarExpEntry) exp.ExpType;            
            m_ilGenerator.Emit(OpCodes.Stloc_S, symLocal.Builder);            
        }
        
        // Field is either static "stsfld" or instance "stfld"
        else if (exp.IsField)
        {
            SymbolEngine.FieldExpEntry symField = exp.ExpType as SymbolEngine.FieldExpEntry;
            if (symField.IsStatic)
            {                
                m_ilGenerator.Emit(OpCodes.Stsfld, symField.Info);
            } 
            else 
            {                
                m_ilGenerator.Emit(OpCodes.Stfld, symField.Info);                             
            }
        } // end field
        
        else {
            Debug.Assert(false);
        }
    }
*/  
    // DotObjExp - can be either { static field, instance field, property-set }
    /*
    public void GenerateAsLeftPre(AST.DotObjExp exp)
    {        
        AST.ObjExp oeLeft = exp.LeftObjExp;
        
        // Only Parameters can be byref, and parameters are always SimpleObjExp
        Debug.Assert(!exp.CLRType.IsByRef);
        
        
        // For Structs, have to load the addresses
        if (oeLeft.SymbolMode == AST.ObjExp.Mode.cExpEntry)
        {   
            // If this is a Struct, or a Ref to a struct, then generate the address
            if (oeLeft.CLRType.IsValueType || 
                (oeLeft.CLRType.IsByRef && oeLeft.CLRType.GetElementType().IsValueType))
            {
                oeLeft.GenerateAddrOf(this);                       
                return;
            }
        }
        
        // Generate for a Class type. This will emit the additional dereference if
        // we're a ref.
        if (oeLeft.SymbolMode == AST.ObjExp.Mode.cExpEntry)        
            oeLeft.GenerateAsRight(this);
    }
    */

/*    
    public void GenerateAsLeftPost(AST.DotObjExp exp)
    {        
        SymbolEngine.SymEntry sym = exp.Symbol;
        
        // Check if property?
        SymbolEngine.PropertyExpEntry symProp = sym as SymbolEngine.PropertyExpEntry;
        if (symProp != null)
        {               
            EmitCall(symProp.SymbolSet);        
            return;
        }
        
        // Must be a field
        SymbolEngine.FieldExpEntry symField = sym as SymbolEngine.FieldExpEntry;
        Debug.Assert(symField != null);

        // This will be a field lookup (not a method call)      
        if (!symField.IsStatic)
        {
            m_ilGenerator.Emit(OpCodes.Stfld, symField.Info);                                    
        } 
        else 
        {            
            m_ilGenerator.Emit(OpCodes.Stsfld, symField.Info);                        
        }
    
    } // end LeftPost() for DotObjExp
*/    
    // ArrayAccessExp
    public void GenerateAsLeftPre(AST.ArrayAccessExp exp)
    {           
        exp.Left.GenerateAsRight(this);            
        exp.ExpIndex.GenerateAsRight(this);
    }
    
    // Generate store for the array
    public void GenerateAsLeftPost(AST.ArrayAccessExp exp)
    {                
        // Different store instruction based off element type
        System.Type t = exp.CLRElemType;
        
        Emit_Stelem(t);        
    }


#endregion // LHS

#region Generate Addresses
    /***************************************************************************\
    * Generate Address of
    \***************************************************************************/
    public void GenerateAddrOf(AST.LocalExp exp)
    {
        //m_ilGenerator.Emit(OpCodes.Ldloca_S, exp.Symbol.Builder);
        this.Emit_LdlocA(exp.Symbol.Builder);
    }
    
    public void GenerateAddrOf(AST.ParamExp exp)
    {
        SymbolEngine.ParamVarExpEntry param = exp.Symbol;
        if (exp.CLRType.IsByRef)
        {
            // Byref is already an address, so just load arg directly
            Emit_Ldarg(param.CodeGenSlot);            
        } 
        else 
        {                            
            m_ilGenerator.Emit(OpCodes.Ldarga_S, param.CodeGenSlot);
        }
    }
    
    public void GenerateAddrOf(AST.FieldExp exp)
    {        
        SymbolEngine.FieldExpEntry symField = exp.Symbol;
                    
        if (!symField.IsStatic)
        {
            AST.Exp eLeft = exp.InstanceExp;
            System.Type t = eLeft.CLRType;
                
            // Emit stuff for the left side of the dot operator
            if (t.IsByRef)
            {                
                t = t.GetElementType();
                    
                // Ref-Structs are already addresses, so just generate the value
                if (t.IsValueType)                    
                    GenerateValue(eLeft);
                else 
                    eLeft.GenerateAsRight(this);                        
            } 
            else 
            {
                if (t.IsValueType)
                {
                    eLeft.GenerateAddrOf(this);
                } 
                else 
                {    
                    eLeft.GenerateAsRight(this);        
                }
            }
                
            // Right side of dot operator is always loading a field address
            m_ilGenerator.Emit(OpCodes.Ldflda, symField.Info);                                    
        } 
        else 
        {            
            m_ilGenerator.Emit(OpCodes.Ldsflda, symField.Info);                        
        }
    }
    
    /*
    public void GenerateAddrOf(AST.SimpleObjExp exp)
    {
        if (exp.IsLocal)
        {
            SymbolEngine.LocalVarExpEntry symLocal = (SymbolEngine.LocalVarExpEntry) exp.ExpType;            
            m_ilGenerator.Emit(OpCodes.Ldloca_S, symLocal.Builder);
        }
            
        if (exp.IsParam)
        {
            SymbolEngine.ParamVarExpEntry param = (SymbolEngine.ParamVarExpEntry) exp.ExpType;
            if (exp.CLRType.IsByRef)
            {
                // Byref is already an address, so just load arg directly
                Emit_Ldarg(param.CodeGenSlot);            
            } else {                            
                m_ilGenerator.Emit(OpCodes.Ldarga_S, param.CodeGenSlot);
            }
        }
        
        if (exp.IsField)
        {
            SymbolEngine.FieldExpEntry symField = exp.ExpType as SymbolEngine.FieldExpEntry;            
            if (symField.IsStatic)
            {
                m_ilGenerator.Emit(OpCodes.Ldsflda, symField.Info);            
            } else {
                EmitThisRef();                
                m_ilGenerator.Emit(OpCodes.Ldflda, symField.Info);
            }
        } // end field
    }
    */
    

    /***************************************************************************\
    * Generate Address of
    \***************************************************************************/
/*    
    public void GenerateAddrOf(AST.DotObjExp exp)
    {   
        if (exp.IsField)
        {            
            SymbolEngine.FieldExpEntry symField = exp.Symbol as SymbolEngine.FieldExpEntry;
            Debug.Assert(symField != null);
            
            if (!symField.IsStatic)
            {
                AST.ObjExp oeLeft = exp.LeftObjExp;
                System.Type t = oeLeft.CLRType;
                
                // Emit stuff for the left side of the dot operator
                if (t.IsByRef)
                {
                    Debug.Assert(oeLeft is AST.SimpleObjExp);
                    t = t.GetElementType();
                    
                    // Ref-Structs are already addresses, so just generate the value
                    if (t.IsValueType)                    
                        GenerateValue(oeLeft as AST.SimpleObjExp);
                    else 
                        oeLeft.GenerateAsRight(this);                        
                } else {
                    if (t.IsValueType)
                    {
                        oeLeft.GenerateAddrOf(this);
                    } 
                    else 
                    {    
                        oeLeft.GenerateAsRight(this);        
                    }
                }
                
                // Right side of dot operator is always loading a field address
                m_ilGenerator.Emit(OpCodes.Ldflda, symField.Info);                                    
            } 
            else 
            {            
                m_ilGenerator.Emit(OpCodes.Ldsflda, symField.Info);                        
            }
        }
    }
*/    
    /***************************************************************************\
    * Generate Address of
    \***************************************************************************/
    public void GenerateAddrOf(AST.ArrayAccessExp exp)
    {
        exp.Left.GenerateAsRight(this);
        exp.ExpIndex.GenerateAsRight(this);
        m_ilGenerator.Emit(OpCodes.Ldelema, exp.CLRElemType); 
    }

#endregion

#region Generate Expressions as Statements
    /***************************************************************************\
    // Statement expressions can be generated as a statement or expression.
    // As an expression, they must leave a value on the top of the stack
    // As a statement, they don't leave a value on top (and hence may
    // be able to have a more efficient impl).    
    \**************************************************************************/
    public void 
    GenerateAsStatement(AST.StatementExp e)
    {
        MarkSequencePoint(e.Location);
    // Default impl generates as a RHS exp and just pops the value off        
    // MethodCall never comes through here. If it's a void return type,
    // then we can't pop off.                 
        Debug.Assert(!(e is AST.MethodCallExp));
        e.GenerateAsRight(this);
        m_ilGenerator.Emit(OpCodes.Pop);
    }
    
    // Generate to get a temporary local variable.
    // This just means associating the builder with the symbol
    public void GenerateAsStatement(AST.DeclareLocalStmtExp e)
    {        
        SymbolEngine.LocalVarExpEntry sym = e.GetLocal().Symbol;
        
        LocalBuilder t = m_ilGenerator.DeclareLocal(sym.CLRType);        
        sym.Builder = t;
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/

    public void
        GenerateAsStatement(
        AST.MethodCallExp exp)
    {        
        MarkSequencePoint(exp.Location);
        exp.GenerateAsRight(this);
        
        // If we have a return result, have to pop it off the stack        
        if (exp.Symbol.RetType.CLRType != typeof(void))
            m_ilGenerator.Emit(OpCodes.Pop);
    }
    
    
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/
    public void 
        GenerateAsStatement(
        AST.AssignStmtExp nodeStmt)
    {
        MarkSequencePoint(nodeStmt.Location);
#if false
// a = new SomeStruct(...);    
    [a]addr
    initobj [SomeStruct]
    [...]
    call SomeStruct(...);
#endif    
        // one exception to the rule:
        // If we're doing r= new <Struct>(...), then we don't generate a LSpost.
        // But r=r2 does generate a post.
        // So Either GenerateAsLeftPost knows what the rightside is, or we
        // do a special case here.
        if (nodeStmt.Right is AST.NewObjExp)
        {
            AST.NewObjExp nodeNew = (AST.NewObjExp) nodeStmt.Right;
            if (nodeNew.CLRType.IsValueType)
            {
                nodeStmt.Left.GenerateAddrOf(this);
                //nodeStmt.Right.GenerateAsRight(this);
                
                // If we're not the default ctor, then generate the call
                if (nodeNew.SymbolCtor != null)
                {
                    foreach (AST.Exp eParam in nodeNew.Params)
                    {
                        eParam.GenerateAsRight(this);
                    }
                    ConstructorInfo info = nodeNew.SymbolCtor.Info as ConstructorInfo;
                    Debug.Assert(info != null);
                    m_ilGenerator.Emit(OpCodes.Call, info);
                } 
                else 
                {
                    // @todo - must make sure that the Ctor assigned everything since
                    // we won't call InitObj when we have a  ctor
                    m_ilGenerator.Emit(OpCodes.Initobj, nodeNew.CLRType);
                }
                
                return;
            }
        }
        
#if false
// a = b (as statement)
    [a]ls_pre
    [b]rs
    [a]ls_post
#endif    
        nodeStmt.Left.GenerateAsLeftPre(this);
        
        System.Type tExpected = nodeStmt.Left.CLRType;
        GenerateBoxable(nodeStmt.Right, tExpected);
        
        nodeStmt.Left.GenerateAsLeftPost(this);
    }
  
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/
    public void 
        GenerateAsStatement(
        AST.PrePostIncDecStmtExp nodeStmt)
    {
#if false
// --x,x--,++x,x++ as statement. 
    [x]ls_pre
    [x]rs    
    1
    sub
    [x]ls_post
#endif
        AST.Exp x = nodeStmt.Arg;
        OpCode code = (nodeStmt.IsInc) ? OpCodes.Add : OpCodes.Sub;
        
        x.GenerateAsLeftPre(this);
        x.GenerateAsRight(this);
        EmitInt(1);
        m_ilGenerator.Emit(code);
        x.GenerateAsLeftPost(this);
    }
    
#endregion

#region Generate StmtExp as Expresions
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.AssignStmtExp nodeStmt)
    {
        // Do the assignement
        GenerateAsStatement(nodeStmt);
        
        // Leave the value on top of the stack
        nodeStmt.Left.GenerateAsRight(this);
    }
   
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the statement defined by the given node.
    *
    \***************************************************************************/
    public void 
    Generate(
        AST.PrePostIncDecStmtExp nodeStmt)
    {
        Debug.Assert(false, "@Todo - x++,++x not supported as expressions yet. Can only use as statements");
    
    }

#endregion // Generate StmtExp as Expresions

#region Generate Expressions        
    /***************************************************************************\
    *
    * EmitCodeGen.GenerateArg
    * - Generate an argument expression
    \***************************************************************************/
    public void
    GenerateArg(AST.ArgExp exp)
    {
        // An ArgExp is a wrapper around an Expression. We need to generate
        // the actual expression, the ArgExp just tells us how we want the
        // actual exp generated (as a raw value, as an address, etc).
        AST.Exp eActual = exp.InnerExp;
        
        if ((eActual.CLRType !=  null) && (eActual.CLRType.IsByRef))
        {
            // If we're a by-ref, then we already have the address. So
            // if we pass that to a ref again, just generate the raw value.
            if (exp.Flow == AST.EArgFlow.cIn)
                eActual.GenerateAsRight(this);
            else
                GenerateValue(eActual);
        } else {
            if (exp.Flow == AST.EArgFlow.cIn)
                eActual.GenerateAsRight(this);
            else
                eActual.GenerateAddrOf(this);                            
        }
    
    } 
   


#region Literal Expressions
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.BoolExp nodeExp)
    {
#if false
// true
        ldc.i4.1

// false
        ldc.i4.0
#endif
        OpCode code = nodeExp.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;
        m_ilGenerator.Emit(code);
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.CharExp nodeExp)
    {
        // Characters are generated as ints        
        char ch = nodeExp.Value;
        this.EmitInt((int) ch);
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/
#if false
    public void
        Generate(
        AST.DoubleExp nodeExp)
    {
        Debug.Assert(false, "@todo - codegen for DoubleExp");
    }
#endif
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.StringExp nodeExp)
    {
        // Push this literal string onto the stack
        m_ilGenerator.Emit(OpCodes.Ldstr, nodeExp.Value);    
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.NullExp nodeExp)
    {
        m_ilGenerator.Emit(OpCodes.Ldnull);
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.IntExp nodeExp)
    {
        int nValue  = nodeExp.Value;
        EmitInt(nValue);
    }
    
    public void
        EmitInt(int nValue)
    {   
        OpCode code = OpCodes.Nop;
        switch (nValue)
        {
            case -1:    code = OpCodes.Ldc_I4_M1;   break;
            case 0:     code = OpCodes.Ldc_I4_0;    break;
            case 1:     code = OpCodes.Ldc_I4_1;    break;
            case 2:     code = OpCodes.Ldc_I4_2;    break;
            case 3:     code = OpCodes.Ldc_I4_3;    break;
            case 4:     code = OpCodes.Ldc_I4_4;    break;
            case 5:     code = OpCodes.Ldc_I4_5;    break;
            case 6:     code = OpCodes.Ldc_I4_6;    break;
            case 7:     code = OpCodes.Ldc_I4_7;    break;
            case 8:     code = OpCodes.Ldc_I4_8;    break;
        }

        if (code.Equals(OpCodes.Nop))
        {
            if ((-128 < nValue) && (nValue < 128))
            {            
                m_ilGenerator.Emit(OpCodes.Ldc_I4_S, nValue);
            }
            else
            {
                m_ilGenerator.Emit(OpCodes.Ldc_I4, nValue);
            }
        }
        else
        {
            m_ilGenerator.Emit(code);
        }
    }
#endregion // Literals
    
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.IsExp nodeExp)
    {
#if false
// Exp 'is' TypeSig
        [Exp]
        isinst [typesig]    // leaves a object on the stack, 
        ldnull              
        cgt.un              // convert obj to a boolean
#endif
        nodeExp.Left.GenerateAsRight(this);
        m_ilGenerator.Emit(OpCodes.Isinst, nodeExp.TargetType.CLRType);
        m_ilGenerator.Emit(OpCodes.Ldnull);
        m_ilGenerator.Emit(OpCodes.Cgt_Un);
    }

    /***************************************************************************\
    * Generate for the if-exp
    \***************************************************************************/
    public void Generate(AST.IfExp exp)
    {
#if false
// (exp:b ? exp:T  : exp:F)
    [b]
    br.false l1
    [T]
    br l2;
l1:    
    [F]
l2: 
#endif  
        // Note that boxing (if needed should be done on whoever gens us,
        // so we don't have to do it on the True & False exp ourselves.  
        exp.TestExp.GenerateAsRight(this);
        
        Label l1 = m_ilGenerator.DefineLabel();
        m_ilGenerator.Emit(OpCodes.Brfalse, l1);

        exp.TrueExp.GenerateAsRight(this);

        Label l2 = m_ilGenerator.DefineLabel();
        m_ilGenerator.Emit(OpCodes.Br, l2);
        m_ilGenerator.MarkLabel(l1);
        
        exp.FalseExp.GenerateAsRight(this);

        m_ilGenerator.MarkLabel(l2);
    }
        
    /***************************************************************************\
    * Generate address for if-exp
    \***************************************************************************/
    public void GenerateAddrOf(AST.IfExp exp)
    {
#if false
// (exp:b ? exp:T  : exp:F)
    [b]
    br.false l1
    [T]addr
    br l2;
l1:    
    [F]addr
l2: 
#endif  
        // Note that boxing (if needed should be done on whoever gens us,
        // so we don't have to do it on the True & False exp ourselves.  
        exp.TestExp.GenerateAsRight(this);
        
        Label l1 = m_ilGenerator.DefineLabel();
        m_ilGenerator.Emit(OpCodes.Brfalse, l1);

        exp.TrueExp.GenerateAddrOf(this);

        Label l2 = m_ilGenerator.DefineLabel();
        m_ilGenerator.Emit(OpCodes.Br, l2);
        m_ilGenerator.MarkLabel(l1);
        
        exp.FalseExp.GenerateAddrOf(this);

        m_ilGenerator.MarkLabel(l2);    
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
    Generate(
        AST.BinaryExp nodeExp)
    {
#if false
// arithmetic operators: Exp1 op Exp2
        [Exp1]
        [Exp2]
        [op]

// Exp1 && Exp2 (using short-circuiting)
        [Exp1]
        dup
        br.false L1
        pop
        [Exp2]
    L1:               

// Exp1 || Exp2 (using short-circuiting)
        [Exp1]
        dup
        br.true l1
        pop
        [Exp2]
    L1:

#endif
        AST.Exp expLeft = nodeExp.Left;
        AST.Exp expRight = nodeExp.Right;

        // Boolean operators with short-circuiting        
        switch (nodeExp.Op)
        {
            case AST.BinaryExp.BinaryOp.cAnd:   
            {
                Label l1 = m_ilGenerator.DefineLabel();
                expLeft.GenerateAsRight(this);
                m_ilGenerator.Emit(OpCodes.Dup);
                m_ilGenerator.Emit(OpCodes.Brfalse, l1);
                m_ilGenerator.Emit(OpCodes.Pop);

                expRight.GenerateAsRight(this);
                m_ilGenerator.MarkLabel(l1);
            }
                return;

            case AST.BinaryExp.BinaryOp.cOr:
            {
                Label l1 = m_ilGenerator.DefineLabel();
                expLeft.GenerateAsRight(this);
                m_ilGenerator.Emit(OpCodes.Dup);
                m_ilGenerator.Emit(OpCodes.Brtrue, l1);
                m_ilGenerator.Emit(OpCodes.Pop);

                expRight.GenerateAsRight(this);
                m_ilGenerator.MarkLabel(l1);
            }
                return;
                
            // Bitwise Xor operator is overloaded as boolean xor
            case AST.BinaryExp.BinaryOp.cBitwiseXor:
                if ((expLeft.CLRType == typeof(bool)) && (expRight.CLRType == typeof(bool)))
                {
                    // transform: a ^ b --> (a != b)
                    expLeft.GenerateAsRight(this);
                    expRight.GenerateAsRight(this);
                    EmitBinaryOp(AST.BinaryExp.BinaryOp.cNeq);                  
                    return;
                }
            
                break;
        }

        // All other operators push both arguments on the stack 
        expLeft.GenerateAsRight(this);
        expRight.GenerateAsRight(this);

        EmitBinaryOp(nodeExp.Op);
    }
    
    // Assuming both arguments are already pushed on the stack, emit the opcodes
    // for the corresponding binary op
    void EmitBinaryOp(AST.BinaryExp.BinaryOp op)
    {
        OpCode code = OpCodes.Nop;
    
        // @todo: Change this to a 2D table to account for "overflow" and unsigned.        
        switch (op)
        {
            case AST.BinaryExp.BinaryOp.cAdd:   code = OpCodes.Add;     break;
            case AST.BinaryExp.BinaryOp.cSub:   code = OpCodes.Sub;     break;
            case AST.BinaryExp.BinaryOp.cMul:   code = OpCodes.Mul;     break;
            case AST.BinaryExp.BinaryOp.cDiv:   code = OpCodes.Div;     break;
            case AST.BinaryExp.BinaryOp.cMod:   code = OpCodes.Rem;     break;        
            case AST.BinaryExp.BinaryOp.cEqu:   code = OpCodes.Ceq;     break;        
            case AST.BinaryExp.BinaryOp.cLT:    code = OpCodes.Clt;     break;
            case AST.BinaryExp.BinaryOp.cGT:    code = OpCodes.Cgt;     break;


            case AST.BinaryExp.BinaryOp.cBitwiseAnd:    code = OpCodes.And; break;
            case AST.BinaryExp.BinaryOp.cBitwiseOr:     code = OpCodes.Or;  break;
            case AST.BinaryExp.BinaryOp.cBitwiseXor:    code = OpCodes.Xor; break;
            
            case AST.BinaryExp.BinaryOp.cShiftLeft:     code = OpCodes.Shl; break;
            case AST.BinaryExp.BinaryOp.cShiftRight:    code = OpCodes.Shr; break;            
                
                // No 'le' il instruction, transform (a < b) into (a >= b) == 0    
            case AST.BinaryExp.BinaryOp.cLE:    
                m_ilGenerator.Emit(OpCodes.Cgt);
                m_ilGenerator.Emit(OpCodes.Ldc_I4_0);
                m_ilGenerator.Emit(OpCodes.Ceq);
                return;
    
                // No 'ge' il instruction, transform (a > b) into (a <= b) == 0
            case AST.BinaryExp.BinaryOp.cGE:    
                m_ilGenerator.Emit(OpCodes.Clt);
                m_ilGenerator.Emit(OpCodes.Ldc_I4_0);
                m_ilGenerator.Emit(OpCodes.Ceq);
                return;

                // No 'neq' il instruction, transform: (a != b) into (a == b) == 0
            case AST.BinaryExp.BinaryOp.cNeq:   
                m_ilGenerator.Emit(OpCodes.Ceq);
                m_ilGenerator.Emit(OpCodes.Ldc_I4_0);
                m_ilGenerator.Emit(OpCodes.Ceq);
                return;
            default:
                Debug.Assert(false, "Unknown operation");
                break;
        }

        if (!code.Equals(OpCodes.Nop))
        {
            m_ilGenerator.Emit(code);
        }
        else
        {
            Debug.Assert(false, "Unimplemented binary operator in codegen:" + op.ToString());
        }    
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.UnaryExp nodeExp)
    {
        AST.Exp expLeft = nodeExp.Left;
        
        expLeft.GenerateAsRight(this);
        
        switch (nodeExp.Op)
        {
            case AST.UnaryExp.UnaryOp.cNot:
                m_ilGenerator.Emit(OpCodes.Ldc_I4_0);
                m_ilGenerator.Emit(OpCodes.Ceq);
                break;

            case AST.UnaryExp.UnaryOp.cNegate:
                m_ilGenerator.Emit(OpCodes.Neg);
                break;

            default:
                Debug.Assert(false, "Unknown operation:" + nodeExp.Op.ToString());
                break;
        }
        
    }
#endregion

#region Generate Exception Handling (Try,Catch,Finally, Throw)
    /***************************************************************************\
    * Generate
    \***************************************************************************/
    public void Generate(AST.ThrowStatement stmt)
    {
#if false
// throw <exp> ';'
    [exp]
    throw

// throw ';' // rethrow
    rethrow
#endif
        if (stmt.ExceptionExp == null)
        {
            m_ilGenerator.Emit(OpCodes.Rethrow);
        } else {
            stmt.ExceptionExp.GenerateAsRight(this);
            m_ilGenerator.Emit(OpCodes.Throw);        
        }
    
    }
    
    /***************************************************************************\
    * Generate
    \***************************************************************************/
    public void Generate(AST.TryStatement stmt)
    {
#if false
// Codegen for: try {S1} finally {S2}
    .try {
        [S1]
        leave L1;
    } finally {
        [S2]
        endfinally
    }
L1:

// Codegen for: try {S1} catch {S2} catch {S3} ...
    .try {
        [S1]
        leave L1;
    }
    catch {
        // Exception object has been pushed on the stack by EE
        // Either pop it (if we're anonymous) or store it in a local
        [S2]
        leave L1;
    }
    ... repeat for other catch blocks ...
L1:

Some notes:
    - Can branch within a try-block, but must use 'leave' opcode to branch out
    - (try S1 -catch S2-finally S3) is codegen-ed as try (try (try S1 catch S2) finally S3)
    - emit api will spew the endfinally & leave opcodes
#endif  
        // If we have a try-catch-finally, then that's really a try-finally around a try-catch
        
        int iDepth = EnterProtectedBlock();
        
        if (stmt.FinallyStmt != null)
        {
            Label lblFinally = m_ilGenerator.BeginExceptionBlock();

            if (stmt.CatchHandlers.Length != 0) {
                GenerateCatchBlocks(stmt);
            } else {
                stmt.TryStmt.Generate(this);                
            }
            
            m_ilGenerator.BeginFinallyBlock();            
            stmt.FinallyStmt.Generate(this);
                        
            m_ilGenerator.EndExceptionBlock();
                        
        } else {
            GenerateCatchBlocks(stmt);        
        }
        
        ExitProtectedBlock(iDepth);
    
    }
    
    // Helper, emit the catch-blocks for this Try-statement
    protected void GenerateCatchBlocks(AST.TryStatement stmt)
    {
        Debug.Assert(stmt.CatchHandlers.Length != 0); // must have catch blocks to emit
        Label lblAfterCatches = m_ilGenerator.BeginExceptionBlock();
            
        stmt.TryStmt.Generate(this);
                    
        foreach(AST.CatchHandler c in stmt.CatchHandlers)
        {       
            m_ilGenerator.BeginCatchBlock(c.CatchType.CLRType);
            
            // EE leaves the exception instance on the stack. 
            // Either pop it if we don't want it, or store it into a local
            if (c.IdVarName == null)
                m_ilGenerator.Emit(OpCodes.Pop);
            else
                this.Emit_Stloc(c.CatchVarDecl.LocalSymbol.Builder);
            
            c.Body.Generate(this);
        }
            
        m_ilGenerator.EndExceptionBlock();        
    }

#endregion

#region Generate Statements 2
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.BreakStatement nodeStmt )
    {
        LoopFrame f = TopLoopFrame();
        Debug.Assert(f != null);

        MarkSequencePoint(nodeStmt.Location);
        m_ilGenerator.Emit(OpCodes.Br, f.BreakLabel);
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.ContinueStatement nodeStmt )
    {
        LoopFrame f = TopLoopFrame();
        Debug.Assert(f != null);

        MarkSequencePoint(nodeStmt.Location);
        m_ilGenerator.Emit(OpCodes.Br, f.ContinueLabel);
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.GotoStatement nodeStmt )
    {
#if false
// goto label;
        br [label]
#endif
    // Get label from symbol
        SymbolEngine.LabelEntry sym = (SymbolEngine.LabelEntry) nodeStmt.Symbol;
        Debug.Assert(sym != null);

        // Associate label with symbol
        if (sym.CodegenCookie == null)
        {            
            nodeStmt.Symbol.CodegenCookie = m_ilGenerator.DefineLabel();
        }

        Label l = (Label) sym.CodegenCookie;
        
        MarkSequencePoint(nodeStmt.Location);
        m_ilGenerator.Emit(OpCodes.Br, l);
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.LabelStatement nodeStmt)
    {   
        // Get label from symbol
        SymbolEngine.LabelEntry sym = (SymbolEngine.LabelEntry) nodeStmt.Symbol;
        Debug.Assert(sym != null);

        // Associate label with symbol
        if (sym.CodegenCookie == null)
        {            
            nodeStmt.Symbol.CodegenCookie = m_ilGenerator.DefineLabel();
        }
        Label l = (Label) sym.CodegenCookie;

        m_ilGenerator.MarkLabel(l);
    }
    
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the method defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.CtorChainStatement nodeChain)
    {
        // @todo - share code with Generate(MethodCallExp)
        if (!nodeChain.NodeCtor.Mods.IsStatic)
        {
            EmitThisRef();
        }

        int i = 0;
        foreach (AST.Exp eParam in nodeChain.Params)
        {
            GenerateBoxable(eParam, nodeChain.SymbolTarget.ParamCLRType(i));
            i++;
        }
        
        ConstructorInfo mdInfo = nodeChain.SymbolTarget.Info as ConstructorInfo;
        Debug.Assert(mdInfo != null);
        
        m_ilGenerator.Emit(OpCodes.Call, mdInfo);
            
    }
#endregion

#region Generate Object Expressions
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
    Generate(
        AST.MethodCallExp nodeExp)
    {
        // For non-static, push on the instance of the object,
        // which will become the 'this' ptr.
        if (!nodeExp.IsStaticCall)
        {
            // Can't just generate b/c we have to be 'box' aware.
            AST.Exp expLeft = nodeExp.LeftObjExp;            
            
            
            // If we're calling a regular method directly on a struct,
            // then we generate the address
            System.Type tLeft = expLeft.CLRType;
            if (tLeft.IsByRef)
                tLeft = tLeft.GetElementType();                
            if (tLeft.IsValueType && nodeExp.Symbol.SymbolClass.CLRType.IsValueType)
            {
                expLeft.GenerateAddrOf(this);    
            } 
            
            // Else, generate a box-aware 'this' reference
            else 
            {            
                System.Type tTarget = nodeExp.Symbol.SymbolClass.CLRType;
                this.GenerateBoxable(expLeft, tTarget);
            }
        }

        
        // Decide if this is a vararg or not. We really should do this on the methodcallobjexp
        //bool fIsVarArg = nodeExp.IsVarArg;
        
        // Push on all parameters.        
        //if (!fIsVarArg)
        {
            // If not vararg, really easy. Just go through and push each
            int i = 0;
            foreach (AST.Exp eParam in nodeExp.ParamExps)
            {            
                GenerateBoxable(eParam, nodeExp.Symbol.ParamCLRType(i));
                i++;
            }
        } 
        /*
        else 
        {
            // If this is a vararg, then we push the first N-1 parameters,
            // and then create an array to store the rest
            int cDeclSite = nodeExp.Symbol.ParamCount;
            int cCallSite = nodeExp.ParamExps.Length;
            
            for(int i = 0; i < cDeclSite - 1; i++)            
            {          
                AST.Exp eParam = nodeExp.ParamExps[i];
                GenerateBoxable(eParam, nodeExp.Symbol.ParamCLRType(i));
                i++;
            }
  
            System.Type tLastDeclParam = nodeExp.Symbol.ParamCLRType(cDeclSite - 1);
            Debug.Assert(tLastDeclParam.IsArray);
            System.Type tBase = tLastDeclParam.GetElementType();

            int cDiff = cCallSite - cDeclSite + 1;
            Debug.Assert(cDiff >= 0);

            m_ilGenerator.Emit(OpCodes.Ldc_I4, cDiff);
            m_ilGenerator.Emit(OpCodes.Newarr, tBase);

            for(int i = 0; i < cDiff; i++)
            {
                AST.Exp eParam = nodeExp.ParamExps[i + cDeclSite - 1];

                m_ilGenerator.Emit(OpCodes.Dup); // array
                m_ilGenerator.Emit(OpCodes.Ldc_I4, i); // index
                GenerateBoxable(eParam, tBase); // value

                m_ilGenerator.Emit(OpCodes.Stelem_Ref); // array, index, value
            }
        }
        */
        // Generate actual call
        if (nodeExp.IsNotPolymorphic)
            EmitNonPolymorphicCall(nodeExp.Symbol);
        else            
            EmitCall(nodeExp.Symbol);    
    }
        
    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/
    
    // Generate code to push just the raw value of this var onto the stack.
    // No conversion, boxing, indirection goo is emitted.
    public void 
    GenerateValue(AST.Exp exp)
    {
        if (exp is AST.ParamExp) GenerateValue(exp as AST.ParamExp);
        else if (exp is AST.LocalExp) GenerateValue(exp as AST.LocalExp);
        else if (exp is AST.FieldExp) GenerateValue(exp as AST.FieldExp);
        else {
            Debug.Assert(false);
        }                
    }
    
    public void GenerateValue(AST.ParamExp exp)
    {
        int iSlot = exp.Symbol.CodeGenSlot;            
        Emit_Ldarg(iSlot);
    }
    
    public void GenerateValue(AST.FieldExp exp)
    {
        SymbolEngine.FieldExpEntry symField = exp.Symbol;
        if (symField.IsStatic)
        {   
            // If this is an enum constant (not a field of enum type)
            // , then load an integer constant,            
            if (symField is SymbolEngine.LiteralFieldExpEntry)
            {
                SymbolEngine.LiteralFieldExpEntry l = 
                    (SymbolEngine.LiteralFieldExpEntry) symField;
                int iData = (int) l.Data;
                this.EmitInt(iData);
            } else            
                m_ilGenerator.Emit(OpCodes.Ldsfld, symField.Info);                
        } 
        else 
        {                
            //exp.InstanceExp.GenerateAsRight(this);
            // Generate the left expression (regardless if we want a LHS or RHS)
            // For Structs, have to load the addresses            
            
            AST.Exp left = exp.InstanceExp;
            Type t = left.CLRType;
            if (t.IsByRef)
            {                
                t = t.GetElementType();
                                    
                // Ref-Structs are already addresses, so just generate the value
                if (t.IsValueType)                    
                    GenerateValue(left);
                else 
                    left.GenerateAsRight(this);                        
            } 
            else 
            {            
                if (t.IsValueType)            
                    left.GenerateAddrOf(this);
                else
                    left.GenerateAsRight(this);                    
            }
            
            m_ilGenerator.Emit(OpCodes.Ldfld, symField.Info);            
        }
    }
    
    public void GenerateValue(AST.LocalExp exp)
    {
        SymbolEngine.LocalVarExpEntry symLocal = exp.Symbol;  
        Emit_Ldloc(symLocal.Builder);
    }
    
    
    
    public void Generate(AST.ParamExp exp)
    {
        GenerateValue(exp);
        GenerateDerefIfNeeded(exp.CLRType);
    }
    
    public void Generate(AST.LocalExp exp)
    {
        GenerateValue(exp);
        GenerateDerefIfNeeded(exp.CLRType);
    }
    
    public void Generate(AST.FieldExp exp)
    {
        GenerateValue(exp);
        GenerateDerefIfNeeded(exp.CLRType);
    }
    

    // Generate a Deref (proper ldi opcode) is this is a reference type
    void GenerateDerefIfNeeded(System.Type t)
    {
        if (!t.IsByRef)
            return;
        
        Type tElem = t.GetElementType();                        
        this.Emit_Ldi(tElem);
    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
    Generate(
        AST.NewArrayObjExp node
    )
    {             
#if false
// For 0-based, 1d arrays, given [element_type] and [size]
    [size]
    newarr [element_type]   
#endif
    // Generate dimensions
        Debug.Assert(node.DimensionExpList.Length == 1);
        foreach(AST.Exp e in node.DimensionExpList)
        {
            e.GenerateAsRight(this);
        }
            
        m_ilGenerator.Emit(OpCodes.Newarr,node.ElemType.CLRType);

#if false
// Initializer list is sugar for assigning.
// @todo - If it's all constant data, we should be able to bulk load
Foreach exp in the list, generate 'A[i] = Exp'
    [dup] // array
    [idx] // index
    [exp] // value
    Stelem
    
For structs, Foreach
    [dup]
    [idx]
    ldelema T
    [Exp]
    stobj T
#endif
        
        if (node.HasInitializerList)
        {
            Type tElem = node.ElemType.CLRType;
            
            if (tElem.IsValueType)
            {
                for(int i = 0; i < node.ArrayInit.Length; i++)
                {
                    m_ilGenerator.Emit(OpCodes.Dup);
                    this.EmitInt(i);
                    m_ilGenerator.Emit(OpCodes.Ldelema, tElem);
                    node.ArrayInit.GetExpAt(i).GenerateAsRight(this);
                    m_ilGenerator.Emit(OpCodes.Stobj, tElem);
                }            
            } else {            
                for(int i = 0; i < node.ArrayInit.Length; i++)
                {
                    m_ilGenerator.Emit(OpCodes.Dup);
                    this.EmitInt(i);
                    
                    AST.Exp e = node.ArrayInit.GetExpAt(i);
                    this.GenerateBoxable(e, tElem);
                    
                    Emit_Stelem(tElem);
                }
            }            
        } // end initializer
    }

    /***************************************************************************\
    * Generate Array Read (as a LS only)
    *    
    \***************************************************************************/
    public void 
    Generate(
        AST.ArrayAccessExp node
    )
    {
#if false
// A[i]
    [A]
    [i]
    ldelem.X        
#endif    
        node.Left.GenerateAsRight(this);
        node.ExpIndex.GenerateAsRight(this);
        
        System.Type t = node.CLRElemType;
        Emit_Ldelem(t);
    }


    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.CastObjExp nodeExp
        )
    {             
#if false
// Casting between an enum & it's backing type is transparent
// Same for chars & ints
// (int) exp
    [exp]
               
// (Enum) exp
    [exp]

// Casting between classes:
// (TypeSig) exp
    [exp]
    castclass [TypeSig]

// Casting from Struct to a class
    (Type) exp
    [exp]
    box [Type]
    
// Casting from Class to Struct
// (Type) exp
    [exp]
    unbox [Type] // unboxing yields an address
    ldi

#endif
        nodeExp.SourceExp.GenerateAsRight(this);

        System.Type clrSource   = nodeExp.SourceExp.CLRType;
        System.Type clrDest     = nodeExp.TargetType.CLRType;
        
        // Strip ref from source type, since GenerateAsRight will derefence a ref.
        if (clrSource.IsByRef)
            clrSource = clrSource.GetElementType();
        
        // Check enums.
        if (clrSource.IsEnum && (clrDest == typeof(int)))
            return;
        if (clrDest.IsEnum && (clrSource == typeof(int)))
            return;

        // Check chars vs. ints
        if (clrSource == typeof(int) && clrDest == typeof(char))
        {
            m_ilGenerator.Emit(OpCodes.Conv_U2);
            return;
        }
        if (clrDest == typeof(int) && clrSource == typeof(char))
            return;            
            
        // boxing: struct -> class
        if (clrSource.IsValueType && clrDest.IsClass)
        {
            m_ilGenerator.Emit(OpCodes.Box, clrSource);
            return;
        }

        // Unboxing for: Class -> Struct
        if (clrSource.IsClass && clrDest.IsValueType)
        {
            m_ilGenerator.Emit(OpCodes.Unbox, clrDest);
            this.Emit_Ldi(clrDest);
            return;            
        }

        // CastClass for: Class -> Class
        m_ilGenerator.Emit(OpCodes.Castclass, nodeExp.TargetType.CLRType);

    }
    
    public void GenerateAddrOf(AST.Exp exp)
    {
#if false
    [exp]
    stloc
    ldloca
#endif
        LocalBuilder x = m_ilGenerator.DeclareLocal(exp.CLRType);
        
        exp.GenerateAsRight(this);
        this.Emit_Stloc(x);
        this.Emit_LdlocA(x);
    }


    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(
        AST.AsExp nodeExp
        )
    {             
#if false
// exp 'as' TypeSig
    [exp]
    isinst [TypeSig]  
#endif
        nodeExp.SourceExp.GenerateAsRight(this);
        m_ilGenerator.Emit(OpCodes.Isinst, nodeExp.TargetType.CLRType);

    }

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(AST.TypeOfExp exp)
    {   
#if false
// typeof(t)
    ldtoken T
    call System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)
#endif        
        m_ilGenerator.Emit(OpCodes.Ldtoken, exp.Sig.CLRType);
        m_ilGenerator.Emit(OpCodes.Call, m_infoGetTypeFromHandle);

    }

    /***************************************************************************\
    * Generate for a MethodPointer
    \***************************************************************************/
    public void
        Generate(AST.MethodPtrExp exp)
    {    
#if false
    ldftn [symbol]    

#endif    
        MethodInfo mdInfo = exp.Symbol.Info as MethodInfo;
        Debug.Assert(mdInfo != null);
        m_ilGenerator.Emit(OpCodes.Ldftn, mdInfo);
    }
    

    /***************************************************************************\
    *
    * EmitCodeGen.Generate
    *
    * Generate() recursively generates the expression defined by the given node.
    *
    \***************************************************************************/

    public void
        Generate(AST.NewObjExp nodeNew)
    {   
        // 'operator new' behaves differently depending on Class or Struct
        // For structs: generating operator_new as a Statement uses either the
        // initobj instruction or a direct call to the ctor. It doesn't use newobj.
        // So we special case that in the assignment node.
        // For structs generating as an expression, do it just like a class does.
                
#if false
// for structs w/ default params
    - create new temporary local t
    ldloca t
    initobj [type]
    ldloc t

// For (classes) or (structs w/ non-default param)
    [params]
    newobj [ctor]
#endif
        // Push parameter for ctor
        SymbolEngine.MethodExpEntry sym = nodeNew.SymbolCtor;
        
        if (sym == null && nodeNew.CLRType.IsValueType)
        {
            LocalBuilder t = m_ilGenerator.DeclareLocal(nodeNew.CLRType);
            m_ilGenerator.Emit(OpCodes.Ldloca_S, t);
            m_ilGenerator.Emit(OpCodes.Initobj, nodeNew.CLRType);
            Emit_Ldloc(t);
            return;
        }
        
        
        Debug.Assert(sym != null);
        
        foreach (AST.Exp eParam in nodeNew.Params)
        {
            eParam.GenerateAsRight(this);
        }

        ConstructorInfo cInfo = sym.Info as ConstructorInfo;
        m_ilGenerator.Emit(OpCodes.Newobj, cInfo);        
    }
#endregion
    
        
#endregion    
    
#region Method Frame    
    /***************************************************************************\
    *  EmitCodeGen.SetCurrentMethod
    * 
    * Want to track the current method that we're generating for.
    * At the end of a method, set to null
    \***************************************************************************/
    void
    SetCurrentMethod(
        AST.MethodDecl nodeMethod
    )
    {
        // We set this only on entry / exit.
        // On entry, cur method should be null, and we're setting it to non-null
        // On Exit, cur method is non-null, and we set it back to null.
        Debug.Assert((nodeMethod == null) ^ (m_nodeCurMethod == null));

        m_nodeCurMethod = nodeMethod;
        
        // Handle debug information
        if (m_fDebugInfo) 
        {
            if (nodeMethod != null)
            {
                // Open a method
                MethodToken mtk;
                if (nodeMethod.IsCtor)
                {                
                    ConstructorBuilder bld = nodeMethod.Symbol.Info as ConstructorBuilder ;
                    mtk = bld.GetToken();
                } else {
                    MethodBuilder bld = nodeMethod.Symbol.Info as MethodBuilder;
                    mtk = bld.GetToken();
                }                    
                
                SymbolToken mtk2 = new SymbolToken(mtk.Token);
                this.m_symWriter.OpenMethod(mtk2);            
            } else {
                // Close a method
                this.m_symWriter.CloseMethod();
            }        
        } // end if DebugInfo
    }

    AST.MethodDecl
    GetCurrentMethod()
    {
        return m_nodeCurMethod;
    }
    
    // Mark a 'sequence point'. These are points in the code that we can step through.
    void MarkSequencePoint(FileRange location)
    {
        if (!this.m_fDebugInfo)
            return;

#if false            
        // Don't spew sequence points on compiler generated stubs 
        // (like default ctors)
        if (location == FileRange.NoSource)
            return;            
            
        Debug.Assert(location != null, "Line number information missing");
#else
        if (location == null || location == FileRange.NoSource)            
            return;
#endif        
        Debug.Assert(m_ilGenerator != null);
        Debug.Assert(GetCurrentMethod() != null);
        
        // m_symCurrentDoc matches location.Filename.
        m_ilGenerator.MarkSequencePoint(m_symCurrentDoc, 
            location.RowStart, location.ColStart, 
            location.RowEnd, location.ColEnd);
            
        #if false
            Console.WriteLine("Sequence Point:{0} ({1},{2})-({3},{4})", 
                location.Filename,
                location.RowStart, location.ColStart, 
                location.RowEnd, location.ColEnd);
        #endif            
    }
    
#endregion    
    
#region Loop Frame stuff
    /***************************************************************************\
    *
    * EmitCodeGen.PushLoopFrame
    *
    * Push the given frame onto the LoopFrame stack
    *
    \***************************************************************************/

    void 
    PushLoopFrame(
        LoopFrame frame
    )
    {
        frame.m_next = m_loopframeTop;
        m_loopframeTop = frame;
    }

    /***************************************************************************\
    *
    * EmitCodeGen.TopLoopFrame
    *
    * Get the most recent loop frame
    *
    \***************************************************************************/
    LoopFrame 
    TopLoopFrame()
    {
        return m_loopframeTop;
    }

    /***************************************************************************\
    *
    * EmitCodeGen.PopLoopFrame
    *
    * Pop the loop frame. As a debugging check, asserts that frame = the top 
    * frame and that we're not empty.
    *
    \***************************************************************************/
    void 
    PopLoopFrame(
        LoopFrame frame
    )
    {
        Debug.Assert(frame == m_loopframeTop);
        Debug.Assert(m_loopframeTop != null);

        m_loopframeTop = m_loopframeTop.m_next;
    }
#endregion

#region NestedClasses
    // Keep a stack of Loop statements (do, while, for) that we can use
    // continue & break in.
    public class LoopFrame
    {
        public LoopFrame(
            Label lblBreak,
            Label lblContinue
            )
        {
            m_lblBreak = lblBreak;
            m_lblContinue = lblContinue;            
        }

        Label m_lblBreak;
        public Label BreakLabel
        {
            get { return m_lblBreak; }
        }

        Label m_lblContinue;
        public Label ContinueLabel
        {
            get { return m_lblContinue; }
        }

        public LoopFrame m_next;
        
    } // end class LoopFrame
#endregion NestedClasses
    
#region Option handlers
    // Set the output filename
    private void 
    Option_Out(
        string stFilename
    )
    {
        //m_stModuleShortName = stFilename;
        // Initially null until we set it here.
        m_stOutputName = stFilename;
    }        

    // Specify which class has the main method    
    private void
    Option_SetMain(
        string stClassName
    )
    {
        m_stMainClass = stClassName;
    }

    // Specify what Win32 executable form we have
    private void 
    Option_Target(
        string stOption)
    {
        string stStd = stOption.ToLower();
        
        if (stStd.Equals("windows"))
        {
            m_TargetType = TargetType.Windows;
        }
        else if (stStd.Equals("console"))
        {
            m_TargetType = TargetType.Console;
        }
        else if (stStd.Equals("library"))
        {
            m_TargetType = TargetType.Dll;            
        }
        else
        {
            throw new Blue.Utilities.OptionErrorException("Expected 'windows', 'console', or 'library'");
        }
    }

    // Specify debug information
    private void 
    Option_DebugInfo(
        string stOption)
    {
        m_fDebugInfo = true;
    }

#endregion


#region Data
    
    private string          m_stOutputName;   

    private AppDomain                   m_domain;
    private ISymbolWriter               m_symWriter;
    private ISymbolDocumentWriter       m_symCurrentDoc;
    private ISymbolDocumentWriter []    m_symDocs;
    private string []                   m_stFilenames;
    private ILGenerator                 m_ilGenerator;
    
    private Label           m_lblReturn;
    private LocalBuilder    m_localReturnValue;
    
    private ArrayList       m_alClasses;
    private int             m_cTryDepth;

    private ModuleBuilder       m_bldModule;
    private AssemblyBuilder     m_bldAssembly;	
    private ResolveEventHandler m_resolveHandler;
    private AST.MethodDecl      m_nodeCurMethod;
    
    // private Utilities.ErrorLog m_error;
    private TargetType      m_TargetType;
    private bool            m_fDebugInfo;
    private string          m_stMainClass;

    private LoopFrame       m_loopframeTop;

    private MethodInfo      m_infoGetTypeFromHandle;
 
#endregion Data

} // class EmitCodeGen

} // namespace Blue.CodeGen
