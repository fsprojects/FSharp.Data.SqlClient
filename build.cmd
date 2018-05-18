@echo off
if not exist packages\FAKE\tools\Fake.exe ( 
  .nuget\nuget.exe install FAKE -OutputDirectory packages -ExcludeVersion -Version 4.1.2 
)
packages\FAKE\tools\FAKE.exe build.fsx %* 
