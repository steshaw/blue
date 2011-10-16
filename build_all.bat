@echo ***** Building the Blue compiler

@rem Build the compiler first using C#. This will produce a main.exe
csc /out:blue.exe @blue_source_core
@echo ***** C# compiled the blue sources to produce Blue.exe

@echo *** Show that the newly compiled blue.exe (which came from csc.exe) is Verifiable IL
peverify blue.exe

@rem Now have the compiler build itself and produce a Dogfood.exe
blue.exe /out:Dogfood.exe @blue_source_core
@echo ***** Blue.exe compiled itself to produce Dogfood.exe

@echo *** Show that blue.exe produced verifiable code
peverify dogfood.exe


