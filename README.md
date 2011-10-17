Blue
====

A managed C# compiler written in C#.

By [Mike Stall](http://blogs.msdn.com/jmstall/)

Feb 4, 2005

------------------------

Blue is a C# compiler written entirely in C# using the .NET. It tagets the V1.0/v1.1 runtimes. The current version does not yet support v2.0 (and is actually very confused when it tries to import generics in v2.0 mscorlib).

It uses Reflection-emit to produce the IL. 

Blue is primarily a sample of reflection-emit and not intended as a production compiler.
 

Usage
-----
You can build Blue.exe via the following command line:

    csc /out:blue.exe @blue_source_core
 
build_all.bat will build Blue.exe from CSC.exe, and then
turn around and use blue.exe to compile itself to produce dogfood.exe
 
 
Use blue /? for usage details.
 

Help
----

bluehelp.xml is the xml-help produced by the /doc csc option.

BlueHelp.chm is a compiled version of bluehelp.xml using the NDoc
utility. These help files mostly document implementation details.

NDoc is available at http://ndoc.sourceforge.net/
 
Use blue /? for usage details.
 

Missing features
----------------

Blue is reasonably complete (it can compile itself), but is
still missing some features. This is a partial list of missing features:

- Custom attributes
- unsafe code
- checked/unchecked, locked keyword, using keyword
- floating point, decimal types.
- Multi dimensional arrays. (note that it can handle jagged arrays)
- Some operator overloading. (Allows binary operators like + to
be overloaded, doesn't yet handle overloaded type casts, unary ops, or ++/--.)
- Still may assert when compiling an illegal program.
 
To see further limitations, look at which command line parameters are
present in CSC but missing in Blue.
